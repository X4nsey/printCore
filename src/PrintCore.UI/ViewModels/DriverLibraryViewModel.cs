using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PrintCore.Core;
using PrintCore.DriverDownloader;
using PrintCore.DriverInstaller;
using PrintCore.Infrastructure;
using PrintCore.Models;
using PrintCore.OnlineDriverCatalog;
using PrintCore.Services;

namespace PrintCore.ViewModels
{
    /// <summary>
    /// Раздел «Библиотека драйверов»:
    /// - Список пакетов в локальном хранилище;
    /// - Импорт файла (.inf/.zip/.exe/.msi/.cab) или папки с автоматическим анализом INF, SHA-256 и Authenticode;
    /// - Сканирование папки локальной библиотеки;
    /// - Загрузка пакета по прямой официальной ссылке с отображением прогресса, скорости и кнопкой отмены;
    /// - Справочник официальных сайтов производителей;
    /// - Установка выбранного пакета в Windows и удаление (с подтверждением удаления файлов с диска).
    /// </summary>
    public class DriverLibraryViewModel : PageViewModel
    {
        private readonly DriverLibraryService _library;
        private readonly OnlineDriverCatalogService _onlineCatalog;
        private readonly DriverDownloaderService _downloader;
        private readonly DriverInstallerService _installer;
        private readonly OperationHistoryService _history;

        private DriverPackage _selectedDriver;
        private string _searchText = "";
        private string _statusMessage = "Готово";
        private bool _isBusy;

        // Поля загрузки по официальной ссылке
        private string _downloadUrl = "";
        private string _downloadManufacturer = "";
        private string _downloadModel = "";
        private string _downloadExpectedSha256 = "";
        private bool _isDownloading;
        private double _downloadProgressPercent;
        private string _downloadProgressText;
        private CancellationTokenSource _downloadCts;

        public DriverLibraryViewModel(
            DriverLibraryService library,
            OnlineDriverCatalogService onlineCatalog,
            DriverDownloaderService downloader,
            DriverInstallerService installer,
            OperationHistoryService history)
            : base(PageKey.DriverLibrary, "Библиотека драйверов", "Локальное хранилище пакетов, загрузка из официальных источников и установка")
        {
            _library = library;
            _onlineCatalog = onlineCatalog;
            _downloader = downloader;
            _installer = installer;
            _history = history;

            OfficialManufacturers = _onlineCatalog.GetOfficialManufacturers();
            _library.Drivers.CollectionChanged += (s, e) => RebuildFiltered();

            ImportFileCommand = new RelayCommand(async () => await ImportFileInteractiveAsync(), () => !IsBusy);
            ImportFolderCommand = new RelayCommand(async () => await ImportFolderInteractiveAsync(), () => !IsBusy);
            RescanLibraryCommand = new RelayCommand(async () => await RescanLibraryAsync(), () => !IsBusy);
            OpenPackageFolderCommand = new RelayCommand(OpenSelectedFolder, () => SelectedDriver != null);
            InstallSelectedCommand = new RelayCommand(async () => await InstallSelectedAsync(), () => SelectedDriver != null && !IsBusy);
            DeleteRecordOnlyCommand = new RelayCommand(async () => await DeleteSelectedAsync(false), () => SelectedDriver != null && !IsBusy);
            DeleteWithFilesCommand = new RelayCommand(async () => await DeleteSelectedAsync(true), () => SelectedDriver != null && !IsBusy);

            StartDownloadCommand = new RelayCommand(async () => await StartDownloadAsync(), () => !IsDownloading && !string.IsNullOrWhiteSpace(DownloadUrl));
            CancelDownloadCommand = new RelayCommand(() => _downloadCts?.Cancel(), () => IsDownloading);
            OpenManufacturerPortalCommand = new RelayCommand(p =>
            {
                var m = p as ManufacturerInfo;
                if (m != null && !string.IsNullOrWhiteSpace(m.OfficialSiteUrl))
                {
                    try { Process.Start(new ProcessStartInfo(m.OfficialSiteUrl) { UseShellExecute = true }); } catch { }
                }
            });
        }

        public ObservableCollection<DriverPackage> FilteredDrivers { get; } = new ObservableCollection<DriverPackage>();

        public IReadOnlyList<ManufacturerInfo> OfficialManufacturers { get; }

        public bool HasDrivers
        {
            get { return FilteredDrivers.Count > 0; }
        }

        public DriverPackage SelectedDriver
        {
            get { return _selectedDriver; }
            set
            {
                if (SetProperty(ref _selectedDriver, value))
                {
                    OnPropertyChanged(nameof(HasSelectedDriver));
                    RelayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasSelectedDriver
        {
            get { return _selectedDriver != null; }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    RebuildFiltered();
                }
            }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { SetProperty(ref _statusMessage, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RelayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string DownloadUrl
        {
            get { return _downloadUrl; }
            set
            {
                if (SetProperty(ref _downloadUrl, value))
                {
                    RelayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string DownloadManufacturer
        {
            get { return _downloadManufacturer; }
            set { SetProperty(ref _downloadManufacturer, value); }
        }

        public string DownloadModel
        {
            get { return _downloadModel; }
            set { SetProperty(ref _downloadModel, value); }
        }

        public string DownloadExpectedSha256
        {
            get { return _downloadExpectedSha256; }
            set { SetProperty(ref _downloadExpectedSha256, value); }
        }

        public bool IsDownloading
        {
            get { return _isDownloading; }
            private set
            {
                if (SetProperty(ref _isDownloading, value))
                {
                    RelayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public double DownloadProgressPercent
        {
            get { return _downloadProgressPercent; }
            private set { SetProperty(ref _downloadProgressPercent, value); }
        }

        public string DownloadProgressText
        {
            get { return _downloadProgressText; }
            private set { SetProperty(ref _downloadProgressText, value); }
        }

        public ICommand ImportFileCommand { get; }
        public ICommand ImportFolderCommand { get; }
        public ICommand RescanLibraryCommand { get; }
        public ICommand OpenPackageFolderCommand { get; }
        public ICommand InstallSelectedCommand { get; }
        public ICommand DeleteRecordOnlyCommand { get; }
        public ICommand DeleteWithFilesCommand { get; }
        public ICommand StartDownloadCommand { get; }
        public ICommand CancelDownloadCommand { get; }
        public ICommand OpenManufacturerPortalCommand { get; }

        public override void OnNavigatedTo()
        {
            if (!_library.HasLoaded && !_library.IsLoading)
            {
                var _ = LoadInitialAsync();
            }
        }

        private async Task LoadInitialAsync()
        {
            await _library.RefreshAsync();
            RebuildFiltered();
            StatusMessage = "Пакетов в локальной библиотеке: " + _library.Drivers.Count;
        }

        private async Task ImportFileInteractiveAsync()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите INF-файл, архив или установщик драйвера",
                Filter = "Пакеты драйверов (*.inf;*.zip;*.exe;*.msi;*.cab)|*.inf;*.zip;*.exe;*.msi;*.cab|Все файлы (*.*)|*.*"
            };

            if (dlg.ShowDialog() != true) return;
            await ImportPathCoreAsync(dlg.FileName);
        }

        private async Task ImportFolderInteractiveAsync()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Выберите папку с пакетом драйвера (содержащую INF-файлы)";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                await ImportPathCoreAsync(dlg.SelectedPath);
            }
        }

        private async Task ImportPathCoreAsync(string path)
        {
            IsBusy = true;
            StatusMessage = "Анализ и импорт пакета драйвера…";
            try
            {
                var pkg = await _library.ImportPackageAsync(path);
                SelectedDriver = _library.Drivers.FirstOrDefault(d => d.Id == pkg.Id) ?? pkg;
                StatusMessage = "Добавлен пакет: " + pkg.DisplayTitle + " (" + pkg.SignatureText + ")";
            }
            catch (Exception ex)
            {
                StatusMessage = "Ошибка импорта: " + ex.Message;
                _history?.Log(OperationKind.Error, OperationResult.Error, "Ошибка импорта драйвера", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RescanLibraryAsync()
        {
            IsBusy = true;
            StatusMessage = "Сканирование папки локальной библиотеки…";
            try
            {
                var added = await _library.RescanFolderAsync();
                StatusMessage = "Сканирование завершено · новых пакетов: " + added + " (всего: " + _library.Drivers.Count + ")";
            }
            catch (Exception ex)
            {
                StatusMessage = "Ошибка сканирования папки: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task StartDownloadAsync()
        {
            if (IsDownloading || string.IsNullOrWhiteSpace(DownloadUrl)) return;

            _downloadCts = new CancellationTokenSource();
            IsDownloading = true;
            DownloadProgressPercent = 0;
            DownloadProgressText = "Подключение к серверу…";

            try
            {
                var progress = new Progress<DownloadProgressInfo>(info =>
                {
                    DownloadProgressPercent = info.Percentage;
                    DownloadProgressText = info.StatusText;
                });

                var pkg = await _downloader.DownloadAndImportAsync(
                    DownloadUrl,
                    DownloadManufacturer,
                    DownloadModel,
                    DownloadExpectedSha256,
                    progress,
                    _downloadCts.Token);

                await _library.RefreshAsync();
                SelectedDriver = _library.Drivers.FirstOrDefault(d => d.Id == pkg.Id) ?? pkg;
                DownloadProgressText = "Пакет загружен, проверен и сохранён в библиотеку: " + pkg.DisplayTitle;
                StatusMessage = DownloadProgressText;
                DownloadUrl = "";
                DownloadExpectedSha256 = "";

                _history?.Log(
                    OperationKind.DriverDownload,
                    OperationResult.Success,
                    "Загружен пакет драйвера: " + pkg.DisplayTitle,
                    "SHA-256: " + pkg.Sha256 + " · Подпись: " + pkg.SignatureText,
                    pkg.Model);
            }
            catch (OperationCanceledException)
            {
                DownloadProgressText = "Загрузка отменена пользователем.";
                _history?.Log(OperationKind.DriverDownload, OperationResult.Cancelled, "Загрузка пакета отменена пользователем", DownloadUrl);
            }
            catch (Exception ex)
            {
                DownloadProgressText = "Ошибка загрузки: " + ex.Message;
                _history?.Log(OperationKind.Error, OperationResult.Error, "Ошибка загрузки пакета драйвера", ex.Message);
            }
            finally
            {
                IsDownloading = false;
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
        }

        private async Task InstallSelectedAsync()
        {
            if (SelectedDriver == null || IsBusy) return;
            var pkg = SelectedDriver;

            var confirm = MessageBox.Show(
                "Установить пакет драйвера в хранилище Windows?\n\n" +
                "Модель: " + pkg.DisplayTitle + "\n" +
                "Версия: " + (pkg.DriverVersion ?? "не указана") + "\n" +
                "Архитектура: " + pkg.ArchitectureText + "\n" +
                "Подпись: " + pkg.SignatureText,
                "Подтверждение установки драйвера",
                MessageBoxButton.YesNo,
                pkg.SignatureState == SignatureState.Valid ? MessageBoxImage.Question : MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = "Установка пакета через Windows PnPUtil…";
            try
            {
                var res = await _installer.InstallPackageAsync(pkg);
                StatusMessage = res.Summary;
                _history?.Log(
                    OperationKind.DriverInstall,
                    res.Success ? OperationResult.Success : OperationResult.Error,
                    (res.Success ? "Установлен пакет: " : "Ошибка установки пакета: ") + pkg.DisplayTitle,
                    res.Summary,
                    pkg.Model);
            }
            catch (Exception ex)
            {
                StatusMessage = "Ошибка установки: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DeleteSelectedAsync(bool deleteFilesFromDisk)
        {
            if (SelectedDriver == null || IsBusy) return;
            var pkg = SelectedDriver;

            if (deleteFilesFromDisk)
            {
                var answer = MessageBox.Show(
                    "Удалить пакет «" + pkg.DisplayTitle + "» из библиотеки И безвозвратно удалить его файлы с диска?\n\nПуть: " + pkg.LocalPath,
                    "Подтверждение удаления файлов",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }

            IsBusy = true;
            try
            {
                await _library.DeleteAsync(pkg, deleteFilesFromDisk);
                SelectedDriver = FilteredDrivers.FirstOrDefault();
                StatusMessage = deleteFilesFromDisk
                    ? "Запись и файлы пакета удалены."
                    : "Запись удалена из библиотеки (файлы сохранены на диске).";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OpenSelectedFolder()
        {
            if (SelectedDriver == null || string.IsNullOrWhiteSpace(SelectedDriver.LocalPath)) return;
            try
            {
                var dir = Directory.Exists(SelectedDriver.LocalPath)
                    ? SelectedDriver.LocalPath
                    : Path.GetDirectoryName(SelectedDriver.LocalPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
                }
            }
            catch
            {
            }
        }

        private void RebuildFiltered()
        {
            FilteredDrivers.Clear();
            var q = (_searchText ?? "").Trim();
            foreach (var d in _library.Drivers)
            {
                if (!string.IsNullOrEmpty(q))
                {
                    var match = (d.DisplayTitle != null && d.DisplayTitle.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                             || (d.Manufacturer != null && d.Manufacturer.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                             || (d.Model != null && d.Model.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                             || (d.DriverVersion != null && d.DriverVersion.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!match) continue;
                }

                FilteredDrivers.Add(d);
            }

            OnPropertyChanged(nameof(HasDrivers));
            if (SelectedDriver == null && FilteredDrivers.Count > 0)
            {
                SelectedDriver = FilteredDrivers[0];
            }
        }
    }
}
