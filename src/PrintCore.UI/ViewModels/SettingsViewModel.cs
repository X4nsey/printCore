using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using PrintCore.Infrastructure;
using PrintCore.Models;
using PrintCore.Services;

namespace PrintCore.ViewModels
{
    /// <summary>
    /// Раздел «Настройки»:
    /// - Тема оформления (Светлая / Тёмная / Как в системе);
    /// - Папка локальной библиотеки драйверов;
    /// - Параметры обнаружения USB и сетевых принтеров (SNMP, TCP/IP, диапазон, тайм-аут, лимит узлов);
    /// - Сведения о системе и версия приложения.
    /// </summary>
    public class SettingsViewModel : PageViewModel
    {
        private readonly ThemeService _themeService;
        private readonly SettingsService _settings;
        private readonly DriverLibraryService _libraryService;
        private string _saveFeedback;

        public SettingsViewModel(
            ThemeService themeService,
            SettingsService settings,
            DriverLibraryService libraryService,
            SystemInfo systemInfo)
            : base(PageKey.Settings, "Настройки", "Оформление, папка библиотеки драйверов и параметры поиска")
        {
            _themeService = themeService;
            _settings = settings;
            _libraryService = libraryService;
            SystemInfo = systemInfo;

            _themeService.ThemeChanged += (s, e) => RaiseThemeProperties();
            OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
            BrowseLibraryFolderCommand = new RelayCommand(BrowseLibraryFolder);
            SaveSearchSettingsCommand = new RelayCommand(SaveSettingsExplicit);

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            AppVersion = version.Major + "." + version.Minor + "." + version.Build;
        }

        public SystemInfo SystemInfo { get; }

        public string AppVersion { get; }

        public string DataDirectory
        {
            get { return _settings.DataDirectory; }
        }

        public string LibraryDirectory
        {
            get { return _settings.Current.LibraryDirectory; }
            set
            {
                if (_settings.Current.LibraryDirectory != value)
                {
                    _settings.Current.LibraryDirectory = value;
                    if (_libraryService?.Catalog != null)
                    {
                        _libraryService.Catalog.LibraryDirectory = value;
                    }

                    _settings.Save();
                    OnPropertyChanged();
                }
            }
        }

        public bool ScanUsb
        {
            get { return _settings.Current.ScanUsb; }
            set
            {
                if (_settings.Current.ScanUsb != value)
                {
                    _settings.Current.ScanUsb = value;
                    _settings.Save();
                    OnPropertyChanged();
                }
            }
        }

        public bool ScanNetwork
        {
            get { return _settings.Current.ScanNetwork; }
            set
            {
                if (_settings.Current.ScanNetwork != value)
                {
                    _settings.Current.ScanNetwork = value;
                    _settings.Save();
                    OnPropertyChanged();
                }
            }
        }

        public bool EnableSnmp
        {
            get { return _settings.Current.EnableSnmp; }
            set
            {
                if (_settings.Current.EnableSnmp != value)
                {
                    _settings.Current.EnableSnmp = value;
                    _settings.Save();
                    OnPropertyChanged();
                }
            }
        }

        public bool EnableTcpProbe
        {
            get { return _settings.Current.EnableTcpProbe; }
            set
            {
                if (_settings.Current.EnableTcpProbe != value)
                {
                    _settings.Current.EnableTcpProbe = value;
                    _settings.Save();
                    OnPropertyChanged();
                }
            }
        }

        public string NetworkRange
        {
            get { return _settings.Current.NetworkRange; }
            set
            {
                if (_settings.Current.NetworkRange != value)
                {
                    _settings.Current.NetworkRange = value;
                    _settings.Save();
                    OnPropertyChanged();
                }
            }
        }

        public int NetworkTimeoutMs
        {
            get { return _settings.Current.NetworkTimeoutMs; }
            set
            {
                var clamped = Math.Max(200, Math.Min(5000, value));
                if (_settings.Current.NetworkTimeoutMs != clamped)
                {
                    _settings.Current.NetworkTimeoutMs = clamped;
                    _settings.Save();
                    OnPropertyChanged();
                }
            }
        }

        public int MaxScanHosts
        {
            get { return _settings.Current.MaxScanHosts; }
            set
            {
                var clamped = Math.Max(1, Math.Min(254, value));
                if (_settings.Current.MaxScanHosts != clamped)
                {
                    _settings.Current.MaxScanHosts = clamped;
                    _settings.Save();
                    OnPropertyChanged();
                }
            }
        }

        public string SaveFeedback
        {
            get { return _saveFeedback; }
            private set { SetProperty(ref _saveFeedback, value); }
        }

        public bool IsLightTheme
        {
            get { return _themeService.SelectedTheme == AppTheme.Light; }
            set { if (value) { _themeService.SetTheme(AppTheme.Light); } }
        }

        public bool IsDarkTheme
        {
            get { return _themeService.SelectedTheme == AppTheme.Dark; }
            set { if (value) { _themeService.SetTheme(AppTheme.Dark); } }
        }

        public bool IsSystemTheme
        {
            get { return _themeService.SelectedTheme == AppTheme.System; }
            set { if (value) { _themeService.SetTheme(AppTheme.System); } }
        }

        public ICommand OpenDataFolderCommand { get; }
        public ICommand BrowseLibraryFolderCommand { get; }
        public ICommand SaveSearchSettingsCommand { get; }

        private void RaiseThemeProperties()
        {
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsSystemTheme));
        }

        private void BrowseLibraryFolder()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Выберите папку для хранения локальной библиотеки драйверов PrintCore";
                dlg.SelectedPath = LibraryDirectory;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    LibraryDirectory = dlg.SelectedPath;
                    SaveFeedback = "Папка библиотеки обновлена и сохранена.";
                }
            }
        }

        private void SaveSettingsExplicit()
        {
            _settings.Save();
            SaveFeedback = "Параметры сохранены (" + DateTime.Now.ToString("HH:mm:ss") + ").";
        }

        private void OpenDataFolder()
        {
            try
            {
                Directory.CreateDirectory(_settings.DataDirectory);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _settings.DataDirectory + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Не удалось открыть папку данных: " + ex);
            }
        }
    }
}
