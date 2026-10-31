using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using PrintCore.Core;
using PrintCore.DeviceDiscovery;
using PrintCore.Infrastructure;
using PrintCore.Logging;
using PrintCore.Models;

namespace PrintCore.Services
{
    /// <summary>
    /// Координатор обнаружения USB- и сетевых принтеров.
    /// Поддерживает индикацию хода выполнения и отмену через CancellationToken.
    /// </summary>
    public class PrinterDiscoveryService : ObservableObject
    {
        private readonly UsbPrinterScanner _usbScanner;
        private readonly NetworkPrinterScanner _netScanner;
        private readonly SettingsService _settingsService;
        private readonly OperationHistoryService _historyService;
        private readonly IAppLogger _logger;

        private CancellationTokenSource _cts;
        private bool _hasScanned;
        private bool _isScanning;
        private string _statusMessage = "Поиск ещё не выполнялся";

        public PrinterDiscoveryService(
            SettingsService settingsService,
            OperationHistoryService historyService,
            IAppLogger logger)
        {
            _settingsService = settingsService;
            _historyService = historyService;
            _logger = logger;
            _usbScanner = new UsbPrinterScanner(logger);
            _netScanner = new NetworkPrinterScanner(logger);
        }

        public ObservableCollection<DiscoveredPrinter> Printers { get; } = new ObservableCollection<DiscoveredPrinter>();

        public bool HasScanned
        {
            get { return _hasScanned; }
            private set { SetProperty(ref _hasScanned, value); }
        }

        public bool IsScanning
        {
            get { return _isScanning; }
            private set { SetProperty(ref _isScanning, value); }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { SetProperty(ref _statusMessage, value); }
        }

        public bool IsAvailable
        {
            get { return true; }
        }

        public async Task StartScanAsync()
        {
            if (IsScanning) return;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            IsScanning = true;
            StatusMessage = "Запуск сканирования устройств…";

            var foundList = new List<DiscoveredPrinter>();
            var settings = _settingsService?.Current ?? new AppSettings();

            try
            {
                Printers.Clear();

                if (settings.ScanUsb)
                {
                    StatusMessage = "Опрос подключённых USB- и Plug and Play устройств…";
                    var usbItems = await Task.Run(() => _usbScanner.Scan(ct), ct);
                    foreach (var item in usbItems)
                    {
                        Printers.Add(item);
                        foundList.Add(item);
                    }
                }

                if (settings.ScanNetwork)
                {
                    var progress = new Progress<string>(msg => StatusMessage = msg);
                    var netItems = await _netScanner.ScanAsync(settings, progress, ct);
                    foreach (var item in netItems)
                    {
                        var duplicate = false;
                        foreach (var existing in Printers)
                        {
                            if (!string.IsNullOrEmpty(item.IpAddress) &&
                                string.Equals(existing.IpAddress, item.IpAddress, StringComparison.OrdinalIgnoreCase))
                            {
                                duplicate = true;
                                break;
                            }
                        }

                        if (!duplicate)
                        {
                            Printers.Add(item);
                            foundList.Add(item);
                        }
                    }
                }

                HasScanned = true;
                StatusMessage = foundList.Count > 0
                    ? "Обнаружено устройств: " + foundList.Count + " (обновлено в " + DateTime.Now.ToString("HH:mm:ss") + ")"
                    : "Сканирование завершено · принтеры не обнаружены";

                _historyService?.Log(
                    OperationKind.PrinterDiscovered,
                    foundList.Count > 0 ? OperationResult.Success : OperationResult.Info,
                    "Поиск принтеров завершён (найдено: " + foundList.Count + ")",
                    string.Format("USB: {0}, Сеть: {1}", settings.ScanUsb ? "вкл." : "выкл.", settings.ScanNetwork ? "вкл." : "выкл."));
            }
            catch (OperationCanceledException)
            {
                HasScanned = true;
                StatusMessage = "Поиск остановлен пользователем (найдено: " + Printers.Count + ")";
                _historyService?.Log(
                    OperationKind.PrinterDiscovered,
                    OperationResult.Cancelled,
                    "Поиск принтеров остановлен пользователем",
                    "Успели обнаружить устройств: " + Printers.Count);
            }
            catch (Exception ex)
            {
                _logger?.Error("Discovery", "Ошибка при поиске принтеров", ex);
                StatusMessage = "Ошибка сканирования: " + ex.Message;
                _historyService?.Log(
                    OperationKind.Error,
                    OperationResult.Error,
                    "Ошибка при поиске принтеров",
                    ex.Message);
            }
            finally
            {
                IsScanning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public void CancelScan()
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }
        }
    }
}
