using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PrintCore.Core;
using PrintCore.DriverCatalog;
using PrintCore.DriverDownloader;
using PrintCore.DriverInstaller;
using PrintCore.Infrastructure;
using PrintCore.Models;
using PrintCore.OnlineDriverCatalog;
using PrintCore.Services;

namespace PrintCore.ViewModels
{
    /// <summary>
    /// Раздел «Поиск принтеров»:
    /// 1) Сканирование USB- и сетевых принтеров с отменой и фильтрацией по типу подключения;
    /// 2) Панель подробностей выбранного устройства;
    /// 3) 9-шаговый алгоритм подбора драйвера (локальная библиотека → онлайн-каталог производителей);
    /// 4) Загрузка пакета с прогрессом/скоростью или переход на официальный портал производителя;
    /// 5) Проверка совместимости и установка драйвера через PnPUtil с подтверждением.
    /// </summary>
    public class PrinterDiscoveryViewModel : PageViewModel
    {
        private readonly PrinterDiscoveryService _discovery;
        private readonly DriverLibraryService _library;
        private readonly OnlineDriverCatalogService _onlineCatalog;
        private readonly DriverDownloaderService _downloader;
        private readonly DriverInstallerService _installer;
        private readonly OperationHistoryService _history;
        private readonly SystemInfo _systemInfo;

        private string _selectedFilter = "All";
        private DiscoveredPrinter _selectedPrinter;
        private DriverCandidate _selectedCandidate;
        private bool _isSearchingDrivers;
        private string _driverSearchStatus;
        private bool _isInstalling;
        private string _installResultMessage;
        private StatusTone _installResultTone = StatusTone.Neutral;

        public PrinterDiscoveryViewModel(
            PrinterDiscoveryService discovery,
            DriverLibraryService library,
            OnlineDriverCatalogService onlineCatalog,
            DriverDownloaderService downloader,
            DriverInstallerService installer,
            OperationHistoryService history,
            SystemInfo systemInfo)
            : base(PageKey.PrinterDiscovery, "Поиск принтеров", "Обнаружение USB- и сетевых принтеров, подбор и установка драйверов")
        {
            _discovery = discovery;
            _library = library;
            _onlineCatalog = onlineCatalog;
            _downloader = downloader;
            _installer = installer;
            _history = history;
            _systemInfo = systemInfo;

            _discovery.PropertyChanged += (s, e) => RaiseDiscoveryState();
            _discovery.Printers.CollectionChanged += (s, e) => RebuildFilteredPrinters();

            StartScanCommand = new RelayCommand(async () => await _discovery.StartScanAsync(), () => !_discovery.IsScanning);
            StopScanCommand = new RelayCommand(() => _discovery.CancelScan(), () => _discovery.IsScanning);
            SetFilterCommand = new RelayCommand(p => SelectedFilter = p as string ?? "All");
            SearchDriverCommand = new RelayCommand(async () => await FindDriversForSelectedPrinterAsync(), () => SelectedPrinter != null && !IsSearchingDrivers);
            OpenOfficialSiteCommand = new RelayCommand(OpenOfficialSupportPage, () => SelectedCandidate?.Package?.OfficialSupportUrl != null);
            InstallSelectedCandidateCommand = new RelayCommand(async () => await InstallCandidateAsync(), () => CanInstallSelectedCandidate());

            RebuildFilteredPrinters();
        }

        public ObservableCollection<DiscoveredPrinter> FilteredPrinters { get; } = new ObservableCollection<DiscoveredPrinter>();

        public ObservableCollection<DriverCandidate> DriverCandidates { get; } = new ObservableCollection<DriverCandidate>();

        public bool IsScanning
        {
            get { return _discovery.IsScanning; }
        }

        public bool HasScanned
        {
            get { return _discovery.HasScanned; }
        }

        public string StatusMessage
        {
            get { return _discovery.StatusMessage; }
        }

        public bool HasFilteredPrinters
        {
            get { return FilteredPrinters.Count > 0; }
        }

        public string SelectedFilter
        {
            get { return _selectedFilter; }
            set
            {
                if (SetProperty(ref _selectedFilter, value))
                {
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterUsb));
                    OnPropertyChanged(nameof(IsFilterNetwork));
                    RebuildFilteredPrinters();
                }
            }
        }

        public bool IsFilterAll { get { return _selectedFilter == "All"; } }
        public bool IsFilterUsb { get { return _selectedFilter == "Usb"; } }
        public bool IsFilterNetwork { get { return _selectedFilter == "Network"; } }

        public DiscoveredPrinter SelectedPrinter
        {
            get { return _selectedPrinter; }
            set
            {
                if (SetProperty(ref _selectedPrinter, value))
                {
                    OnPropertyChanged(nameof(HasSelectedPrinter));
                    DriverCandidates.Clear();
                    SelectedCandidate = null;
                    DriverSearchStatus = null;
                    InstallResultMessage = null;
                    RelayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasSelectedPrinter
        {
            get { return _selectedPrinter != null; }
        }

        public DriverCandidate SelectedCandidate
        {
            get { return _selectedCandidate; }
            set
            {
                if (SetProperty(ref _selectedCandidate, value))
                {
                    OnPropertyChanged(nameof(HasSelectedCandidate));
                    OnPropertyChanged(nameof(SelectedCandidateRequiresBrowser));
                    OnPropertyChanged(nameof(SelectedCandidateIsLocal));
                    RelayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasSelectedCandidate
        {
            get { return _selectedCandidate != null; }
        }

        public bool SelectedCandidateRequiresBrowser
        {
            get { return _selectedCandidate?.Package != null && _selectedCandidate.Package.RequiresBrowserDownload; }
        }

        public bool SelectedCandidateIsLocal
        {
            get { return _selectedCandidate?.Package != null && _selectedCandidate.Package.IsLocal; }
        }

        public bool HasDriverCandidates
        {
            get { return DriverCandidates.Count > 0; }
        }

        public bool IsSearchingDrivers
        {
            get { return _isSearchingDrivers; }
            private set
            {
                if (SetProperty(ref _isSearchingDrivers, value))
                {
                    RelayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string DriverSearchStatus
        {
            get { return _driverSearchStatus; }
            private set { SetProperty(ref _driverSearchStatus, value); }
        }

        public bool IsInstalling
        {
            get { return _isInstalling; }
            private set
            {
                if (SetProperty(ref _isInstalling, value))
                {
                    RelayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string InstallResultMessage
        {
            get { return _installResultMessage; }
            private set
            {
                if (SetProperty(ref _installResultMessage, value))
                {
                    OnPropertyChanged(nameof(HasInstallResult));
                }
            }
        }

        public bool HasInstallResult
        {
            get { return !string.IsNullOrEmpty(_installResultMessage); }
        }

        public StatusTone InstallResultTone
        {
            get { return _installResultTone; }
            private set { SetProperty(ref _installResultTone, value); }
        }

        public SystemInfo SystemInfo
        {
            get { return _systemInfo; }
        }

        public ICommand StartScanCommand { get; }
        public ICommand StopScanCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand SearchDriverCommand { get; }
        public ICommand OpenOfficialSiteCommand { get; }
        public ICommand InstallSelectedCandidateCommand { get; }

        /// <summary>
        /// 9-шаговый алгоритм подбора драйвера:
        /// 1. Модель и производитель.
        /// 2. Hardware ID.
        /// 3. Версия и разрядность Windows.
        /// 4–5. Поиск в локальной библиотеке.
        /// 6–8. Поиск в онлайн-каталоге и расчёт совместимости.
        /// 9. Предоставление выбора пользователю.
        /// </summary>
        public async Task FindDriversForSelectedPrinterAsync()
        {
            if (SelectedPrinter == null || IsSearchingDrivers) return;

            IsSearchingDrivers = true;
            InstallResultMessage = null;
            DriverCandidates.Clear();
            SelectedCandidate = null;

            var printer = SelectedPrinter;
            printer.DriverStatus = DriverStatus.Searching;

            try
            {
                var query = new DriverSearchQuery
                {
                    Manufacturer = printer.Manufacturer,
                    Model = printer.Model,
                    HardwareId = printer.HardwareId,
                    TargetOs = _systemInfo.WindowsVersion,
                    TargetArchitecture = _systemInfo.CpuArchitecture
                };

                DriverSearchStatus = "Проверка локальной библиотеки драйверов…";
                var localMatches = await _library.Catalog.FindCandidatesAsync(query);
                foreach (var m in localMatches)
                {
                    DriverCandidates.Add(m);
                }

                if (localMatches.Count == 0)
                {
                    DriverSearchStatus = "В локальной библиотеке совпадений нет · поиск в онлайн-каталоге производителей…";
                }
                else
                {
                    DriverSearchStatus = "Проверка официальных источников производителя…";
                }

                var onlineMatches = await _onlineCatalog.FindCandidatesAsync(query);
                foreach (var m in onlineMatches)
                {
                    DriverCandidates.Add(m);
                }

                OnPropertyChanged(nameof(HasDriverCandidates));
                SelectedCandidate = DriverCandidates.FirstOrDefault();

                if (localMatches.Count > 0)
                {
                    printer.DriverStatus = DriverStatus.Available;
                    DriverSearchStatus = "Найдено в локальной библиотеке: " + localMatches.Count + " (всего вариантов: " + DriverCandidates.Count + ")";
                }
                else if (DriverCandidates.Count > 0)
                {
                    printer.DriverStatus = DriverStatus.Available;
                    DriverSearchStatus = "Локальный пакет не найден. Доступен официальный источник производителя (" + DriverCandidates.Count + ").";
                }
                else
                {
                    printer.DriverStatus = DriverStatus.NotFound;
                    DriverSearchStatus = "Подходящий драйвер не найден. Попробуйте импортировать пакет вручную в «Библиотеке драйверов».";
                }

                _history?.Log(
                    OperationKind.DriverSearch,
                    DriverCandidates.Count > 0 ? OperationResult.Success : OperationResult.Warning,
                    "Подбор драйвера для " + printer.DisplayModel,
                    "Локальных: " + localMatches.Count + " · Официальных источников: " + onlineMatches.Count,
                    printer.DisplayModel);
            }
            catch (Exception ex)
            {
                printer.DriverStatus = DriverStatus.Error;
                DriverSearchStatus = "Ошибка поиска драйвера: " + ex.Message;
                _history?.Log(OperationKind.Error, OperationResult.Error, "Ошибка подбора драйвера", ex.Message, printer.DisplayModel);
            }
            finally
            {
                IsSearchingDrivers = false;
            }
        }

        private bool CanInstallSelectedCandidate()
        {
            return !IsInstalling
                && SelectedCandidate?.Package != null
                && SelectedCandidate.Package.IsLocal
                && SelectedCandidate.IsFullyCompatible;
        }

        private async Task InstallCandidateAsync()
        {
            if (!CanInstallSelectedCandidate()) return;
            var pkg = SelectedCandidate.Package;
            var printer = SelectedPrinter;

            // Предупреждение, если цифровая подпись пакета не проверена или отсутствует
            if (pkg.SignatureState == SignatureState.Unsigned || pkg.SignatureState == SignatureState.Invalid)
            {
                var confirm = System.Windows.MessageBox.Show(
                    "Пакет «" + pkg.DisplayTitle + "» не имеет подтверждённой цифровой подписи (" + pkg.SignatureText + ").\n\nПродолжить установку штатным средством Windows PnPUtil?",
                    "Проверка безопасности пакета",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (confirm != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
            }

            IsInstalling = true;
            InstallResultMessage = "Выполняется установка драйвера через Windows PnPUtil…";
            InstallResultTone = StatusTone.Accent;

            try
            {
                var res = await _installer.InstallPackageAsync(pkg);
                InstallResultMessage = res.Summary;
                InstallResultTone = res.Success
                    ? (res.RequiresWarning ? StatusTone.Warning : StatusTone.Success)
                    : StatusTone.Danger;

                if (res.Success && printer != null)
                {
                    printer.DriverStatus = DriverStatus.Installed;
                }

                _history?.Log(
                    OperationKind.DriverInstall,
                    res.Success ? (res.RequiresWarning ? OperationResult.Warning : OperationResult.Success) : OperationResult.Error,
                    (res.Success ? "Установлен драйвер: " : "Не удалось установить драйвер: ") + pkg.DisplayTitle,
                    res.Summary + (string.IsNullOrEmpty(res.CommandOutput) ? "" : " | " + res.CommandOutput),
                    printer?.DisplayModel);
            }
            catch (Exception ex)
            {
                InstallResultMessage = "Ошибка установки: " + ex.Message;
                InstallResultTone = StatusTone.Danger;
                _history?.Log(OperationKind.Error, OperationResult.Error, "Ошибка установки драйвера " + pkg.DisplayTitle, ex.Message, printer?.DisplayModel);
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private void OpenOfficialSupportPage()
        {
            var url = SelectedCandidate?.Package?.OfficialSupportUrl;
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                _history?.Log(
                    OperationKind.DriverSearch,
                    OperationResult.Info,
                    "Открыта официальная страница поддержки " + SelectedCandidate.Package.Manufacturer,
                    url,
                    SelectedPrinter?.DisplayModel);
            }
            catch
            {
            }
        }

        private void RebuildFilteredPrinters()
        {
            FilteredPrinters.Clear();
            foreach (var p in _discovery.Printers)
            {
                if (_selectedFilter == "Usb" && p.Connection != ConnectionType.Usb) continue;
                if (_selectedFilter == "Network" && p.Connection != ConnectionType.Network && p.Connection != ConnectionType.Wsd) continue;
                FilteredPrinters.Add(p);
            }

            OnPropertyChanged(nameof(HasFilteredPrinters));
            if (SelectedPrinter == null && FilteredPrinters.Count > 0)
            {
                SelectedPrinter = FilteredPrinters[0];
            }
        }

        private void RaiseDiscoveryState()
        {
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(HasScanned));
            OnPropertyChanged(nameof(StatusMessage));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
