using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using PrintCore.DriverDownloader;
using PrintCore.DriverInstaller;
using PrintCore.Infrastructure;
using PrintCore.Models;
using PrintCore.OnlineDriverCatalog;
using PrintCore.Services;

namespace PrintCore.ViewModels
{
    /// <summary>
    /// Модель главного окна: боковое меню, верхняя панель и текущий раздел.
    /// Разделы создаются один раз и сохраняют состояние между переходами.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        private readonly ThemeService _themeService;
        private readonly Dictionary<PageKey, PageViewModel> _pages;
        private PageViewModel _currentPage;

        public MainViewModel(
            ThemeService themeService,
            SettingsService settingsService,
            SystemInfo systemInfo,
            PrinterDiscoveryService discovery,
            InstalledPrinterService installed,
            DriverLibraryService library,
            OnlineDriverCatalogService onlineCatalog,
            DriverDownloaderService downloader,
            DriverInstallerService installer,
            OperationHistoryService history)
        {
            _themeService = themeService;
            SystemInfo = systemInfo;

            _pages = new PageViewModel[]
            {
                new DashboardViewModel(discovery, installed, library, history, NavigateTo),
                new PrinterDiscoveryViewModel(discovery, library, onlineCatalog, downloader, installer, history, systemInfo),
                new InstalledPrintersViewModel(installed),
                new DriverLibraryViewModel(library, onlineCatalog, downloader, installer, history),
                new OperationHistoryViewModel(history),
                new SettingsViewModel(themeService, settingsService, library, systemInfo)
            }.ToDictionary(p => p.Key);

            NavigationItems = new[]
            {
                CreateItem(PageKey.Dashboard, "Главная", "Icon.Home"),
                CreateItem(PageKey.PrinterDiscovery, "Поиск принтеров", "Icon.Search"),
                CreateItem(PageKey.InstalledPrinters, "Установленные принтеры", "Icon.Printer"),
                CreateItem(PageKey.DriverLibrary, "Библиотека драйверов", "Icon.Package"),
                CreateItem(PageKey.OperationHistory, "История операций", "Icon.History")
            };

            FooterNavigationItems = new[]
            {
                CreateItem(PageKey.Settings, "Настройки", "Icon.Settings")
            };

            NavigateCommand = new RelayCommand(p =>
            {
                if (p is PageKey key)
                {
                    NavigateTo(key);
                }
                else if (p is string text && System.Enum.TryParse(text, out PageKey parsed))
                {
                    NavigateTo(parsed);
                }
            });
            ToggleThemeCommand = new RelayCommand(() => _themeService.Toggle());
            _themeService.ThemeChanged += (s, e) => OnPropertyChanged(nameof(IsDarkTheme));

            NavigateTo(PageKey.Dashboard);
        }

        public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

        public IReadOnlyList<NavigationItemViewModel> FooterNavigationItems { get; }

        public SystemInfo SystemInfo { get; }

        public string AppVersion
        {
            get { return ((SettingsViewModel)_pages[PageKey.Settings]).AppVersion; }
        }

        public PageViewModel CurrentPage
        {
            get { return _currentPage; }
            private set { SetProperty(ref _currentPage, value); }
        }

        public bool IsDarkTheme
        {
            get { return _themeService.IsDark; }
        }

        public ICommand NavigateCommand { get; }

        public ICommand ToggleThemeCommand { get; }

        public void NavigateTo(PageKey key)
        {
            foreach (var item in NavigationItems.Concat(FooterNavigationItems))
            {
                item.SetSelectedSilently(item.Key == key);
            }

            var page = _pages[key];
            if (!ReferenceEquals(CurrentPage, page))
            {
                CurrentPage = page;
            }

            page.OnNavigatedTo();
        }

        private NavigationItemViewModel CreateItem(PageKey key, string title, string iconKey)
        {
            return new NavigationItemViewModel(key, title, iconKey, item => NavigateTo(item.Key));
        }
    }
}
