using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using PrintCore.Infrastructure;
using PrintCore.Models;
using PrintCore.Services;

namespace PrintCore.ViewModels
{
    /// <summary>Главная страница: статистика, последние принтеры и операции, быстрые действия.</summary>
    public class DashboardViewModel : PageViewModel
    {
        private const int RecentLimit = 5;

        private readonly PrinterDiscoveryService _discovery;
        private readonly InstalledPrinterService _installed;
        private readonly DriverLibraryService _library;
        private readonly OperationHistoryService _history;

        public DashboardViewModel(
            PrinterDiscoveryService discovery,
            InstalledPrinterService installed,
            DriverLibraryService library,
            OperationHistoryService history,
            Action<PageKey> navigate)
            : base(PageKey.Dashboard, "Главная", "Обзор состояния принтеров и драйверов")
        {
            _discovery = discovery;
            _installed = installed;
            _library = library;
            _history = history;

            FoundPrintersCard = new StatCardViewModel("Найденные принтеры", "Icon.Search");
            InstalledPrintersCard = new StatCardViewModel("Установленные принтеры", "Icon.Printer");
            DriversCard = new StatCardViewModel("Драйверы в библиотеке", "Icon.Package");

            FindPrintersCommand = new RelayCommand(() => navigate(PageKey.PrinterDiscovery));
            OpenDriverLibraryCommand = new RelayCommand(() => navigate(PageKey.DriverLibrary));
            OpenInstalledPrintersCommand = new RelayCommand(() => navigate(PageKey.InstalledPrinters));
            OpenHistoryCommand = new RelayCommand(() => navigate(PageKey.OperationHistory));

            _discovery.Printers.CollectionChanged += (s, e) => UpdateDiscovery();
            _discovery.PropertyChanged += (s, e) => UpdateDiscovery();
            _installed.PropertyChanged += OnInstalledChanged;
            _installed.Printers.CollectionChanged += (s, e) => UpdateInstalled();
            _library.Drivers.CollectionChanged += (s, e) => UpdateLibrary();
            _history.Records.CollectionChanged += OnHistoryChanged;

            UpdateDiscovery();
            UpdateInstalled();
            UpdateLibrary();
            RebuildRecentOperations();
        }

        public string WelcomeTitle
        {
            get { return "Добро пожаловать в PrintCore"; }
        }

        public string WelcomeSubtitle
        {
            get { return "Управление принтерами и драйверами в одном месте"; }
        }

        public StatCardViewModel FoundPrintersCard { get; }

        public StatCardViewModel InstalledPrintersCard { get; }

        public StatCardViewModel DriversCard { get; }

        /// <summary>Последние обнаруженные принтеры (данные модуля обнаружения).</summary>
        public ObservableCollection<DiscoveredPrinter> RecentPrinters
        {
            get { return _discovery.Printers; }
        }

        public bool HasRecentPrinters
        {
            get { return _discovery.Printers.Count > 0; }
        }

        public string RecentPrintersEmptyDescription
        {
            get
            {
                return _discovery.HasScanned
                    ? "Последнее сканирование не обнаружило принтеров."
                    : "Поиск ещё не выполнялся. Запустите поиск, чтобы обнаружить USB- и сетевые принтеры.";
            }
        }

        /// <summary>Последние записи журнала операций.</summary>
        public ObservableCollection<OperationRecord> RecentOperations { get; } = new ObservableCollection<OperationRecord>();

        public bool HasRecentOperations
        {
            get { return RecentOperations.Count > 0; }
        }

        public ICommand FindPrintersCommand { get; }

        public ICommand OpenDriverLibraryCommand { get; }

        public ICommand OpenInstalledPrintersCommand { get; }

        public ICommand OpenHistoryCommand { get; }

        public override void OnNavigatedTo()
        {
            if (!_installed.HasLoaded && !_installed.IsLoading)
            {
                var _ = _installed.RefreshAsync();
            }

            if (!_library.HasLoaded && !_library.IsLoading)
            {
                var _ = _library.RefreshAsync();
            }
        }

        private void UpdateDiscovery()
        {
            if (_discovery.HasScanned)
            {
                FoundPrintersCard.SetValue(_discovery.Printers.Count, "По результатам последнего поиска");
            }
            else
            {
                FoundPrintersCard.SetNoData("Поиск ещё не выполнялся");
            }

            OnPropertyChanged(nameof(HasRecentPrinters));
            OnPropertyChanged(nameof(RecentPrintersEmptyDescription));
        }

        private void OnInstalledChanged(object sender, PropertyChangedEventArgs e)
        {
            UpdateInstalled();
        }

        private void UpdateInstalled()
        {
            if (_installed.IsLoading && !_installed.HasLoaded)
            {
                InstalledPrintersCard.SetLoading("Чтение очередей печати Windows");
            }
            else if (_installed.HasLoaded)
            {
                var defaultPrinter = _installed.Printers.FirstOrDefault(p => p.IsDefault);
                InstalledPrintersCard.SetValue(
                    _installed.Printers.Count,
                    defaultPrinter != null ? "По умолчанию: " + defaultPrinter.Name : "Очереди печати Windows");
            }
            else if (_installed.HasError)
            {
                InstalledPrintersCard.SetNoData("Не удалось прочитать список");
            }
            else
            {
                InstalledPrintersCard.SetNoData("Список ещё не загружен");
            }
        }

        private void UpdateLibrary()
        {
            if (_library.IsAvailable)
            {
                DriversCard.SetValue(_library.Drivers.Count, "Локальное хранилище драйверов");
            }
            else
            {
                DriversCard.SetNoData("Библиотека ещё не сформирована");
            }
        }

        private void OnHistoryChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildRecentOperations();
        }

        private void RebuildRecentOperations()
        {
            RecentOperations.Clear();
            foreach (var record in _history.Records.Take(RecentLimit))
            {
                RecentOperations.Add(record);
            }

            OnPropertyChanged(nameof(HasRecentOperations));
        }
    }
}
