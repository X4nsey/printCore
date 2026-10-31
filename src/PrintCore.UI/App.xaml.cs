using System.Windows;
using PrintCore.Data;
using PrintCore.DriverDownloader;
using PrintCore.DriverInstaller;
using PrintCore.LocalDriverCatalog;
using PrintCore.Logging;
using PrintCore.OnlineDriverCatalog;
using PrintCore.Services;
using PrintCore.ViewModels;
using PrintCore.Views;

namespace PrintCore
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Журналирование и база данных SQLite
            var logger = new FileAppLogger();
            var database = new PrintCoreDatabase(logger);
            database.Initialize();

            // 2. Загрузка настроек (SQLite + XML) и применение темы оформления
            var settingsService = new SettingsService(database, logger);
            var settings = settingsService.Load();

            var themeService = new ThemeService(settingsService);
            themeService.Apply(settings.Theme);

            // 3. Инициализация всех модулей PrintCore
            var systemInfo = new SystemInfoService().GetSystemInfo();
            var historyService = new OperationHistoryService(database, logger);
            var discoveryService = new PrinterDiscoveryService(settingsService, historyService, logger);
            var installedService = new InstalledPrinterService();

            var localCatalog = new LocalDriverCatalogService(database, logger, settings.LibraryDirectory);
            var libraryService = new DriverLibraryService(localCatalog, historyService, logger);
            var onlineCatalog = new OnlineDriverCatalogService(logger);
            var downloader = new DriverDownloaderService(localCatalog, logger);
            var installer = new DriverInstallerService(logger);

            // 4. Сборка главной модели представления и показ окна
            var mainViewModel = new MainViewModel(
                themeService,
                settingsService,
                systemInfo,
                discoveryService,
                installedService,
                libraryService,
                onlineCatalog,
                downloader,
                installer,
                historyService);

            var window = new MainWindow { DataContext = mainViewModel };
            MainWindow = window;
            window.Show();
        }
    }
}
