using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Printing;
using System.Threading.Tasks;
using PrintCore.Core;
using PrintCore.Infrastructure;
using PrintCore.Models;

namespace PrintCore.Services
{
    /// <summary>
    /// Чтение списка принтеров, установленных в Windows (через System.Printing и WMI Win32_PrinterDriver),
    /// включая версию драйвера, INF-файл, порт и состояние очереди печати.
    /// </summary>
    public class InstalledPrinterService : ObservableObject
    {
        private bool _isLoading;
        private bool _hasLoaded;
        private string _errorMessage;
        private DateTime? _lastUpdated;

        public System.Collections.ObjectModel.ObservableCollection<InstalledPrinter> Printers { get; } =
            new System.Collections.ObjectModel.ObservableCollection<InstalledPrinter>();

        public bool IsLoading
        {
            get { return _isLoading; }
            private set { SetProperty(ref _isLoading, value); }
        }

        public bool HasLoaded
        {
            get { return _hasLoaded; }
            private set { SetProperty(ref _hasLoaded, value); }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError
        {
            get { return !string.IsNullOrEmpty(_errorMessage); }
        }

        public DateTime? LastUpdated
        {
            get { return _lastUpdated; }
            private set { SetProperty(ref _lastUpdated, value); }
        }

        public async Task RefreshAsync()
        {
            if (IsLoading) return;

            IsLoading = true;
            ErrorMessage = null;
            try
            {
                var printers = await Task.Run(() => ReadInstalledPrinters());
                Printers.Clear();
                foreach (var printer in printers)
                {
                    Printers.Add(printer);
                }

                HasLoaded = true;
                LastUpdated = DateTime.Now;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Не удалось получить список принтеров: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static IReadOnlyList<InstalledPrinter> ReadInstalledPrinters()
        {
            var result = new List<InstalledPrinter>();
            var driverMeta = QueryDriverMetadata();

            string defaultName = null;
            try
            {
                using (var defaultQueue = LocalPrintServer.GetDefaultPrintQueue())
                {
                    defaultName = defaultQueue?.FullName;
                }
            }
            catch
            {
            }

            using (var server = new LocalPrintServer())
            using (var queues = server.GetPrintQueues(new[]
            {
                EnumeratedPrintQueueTypes.Local,
                EnumeratedPrintQueueTypes.Connections
            }))
            {
                foreach (var queue in queues)
                {
                    using (queue)
                    {
                        var printer = new InstalledPrinter { Name = queue.FullName };
                        printer.IsDefault = string.Equals(printer.Name, defaultName, StringComparison.OrdinalIgnoreCase);
                        printer.IsNetworkConnection = printer.Name != null && printer.Name.StartsWith(@"\\", StringComparison.Ordinal);

                        try { printer.DriverName = queue.QueueDriver?.Name; } catch { }
                        try { printer.PortName = queue.QueuePort?.Name; } catch { }
                        try { printer.IsShared = queue.IsShared; } catch { }

                        printer.Model = printer.DriverName ?? printer.Name;
                        printer.Manufacturer = VendorKnowledgeBase.NormalizeManufacturer(printer.DriverName ?? printer.Name) ?? "Системный";

                        DriverMetaInfo meta;
                        if (!string.IsNullOrEmpty(printer.DriverName) && driverMeta.TryGetValue(printer.DriverName, out meta))
                        {
                            printer.DriverVersion = meta.Version;
                            printer.InfPath = meta.InfPath;
                        }

                        try
                        {
                            var st = queue.QueueStatus;
                            if ((st & PrintQueueStatus.Offline) != 0)
                            {
                                printer.IsOffline = true;
                                printer.StatusText = "Автономно";
                                printer.StatusTone = StatusTone.Warning;
                            }
                            else if ((st & (PrintQueueStatus.Error | PrintQueueStatus.PaperJam | PrintQueueStatus.PaperOut)) != 0)
                            {
                                printer.StatusText = "Ошибка очереди";
                                printer.StatusTone = StatusTone.Danger;
                            }
                            else if (printer.IsDefault)
                            {
                                printer.StatusText = "По умолчанию";
                                printer.StatusTone = StatusTone.Accent;
                            }
                            else
                            {
                                printer.StatusText = "Готов";
                                printer.StatusTone = StatusTone.Success;
                            }
                        }
                        catch
                        {
                            printer.StatusText = "Готов";
                            printer.StatusTone = StatusTone.Success;
                        }

                        result.Add(printer);
                    }
                }
            }

            return result
                .OrderByDescending(p => p.IsDefault)
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private struct DriverMetaInfo
        {
            public string Version;
            public string InfPath;
        }

        private static Dictionary<string, DriverMetaInfo> QueryDriverMetadata()
        {
            var dict = new Dictionary<string, DriverMetaInfo>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, DriverPath, InfName FROM Win32_PrinterDriver"))
                using (var col = searcher.Get())
                {
                    foreach (ManagementObject mo in col)
                    {
                        using (mo)
                        {
                            var rawName = mo["Name"] as string;
                            if (string.IsNullOrWhiteSpace(rawName)) continue;
                            var comma = rawName.IndexOf(',');
                            var cleanName = comma > 0 ? rawName.Substring(0, comma).Trim() : rawName.Trim();

                            var drvPath = mo["DriverPath"] as string;
                            var infName = mo["InfName"] as string;
                            string version = null;
                            if (!string.IsNullOrEmpty(drvPath) && System.IO.File.Exists(drvPath))
                            {
                                try
                                {
                                    version = System.Diagnostics.FileVersionInfo.GetVersionInfo(drvPath).FileVersion;
                                }
                                catch
                                {
                                }
                            }

                            dict[cleanName] = new DriverMetaInfo { Version = version, InfPath = infName };
                        }
                    }
                }
            }
            catch
            {
            }

            return dict;
        }
    }
}
