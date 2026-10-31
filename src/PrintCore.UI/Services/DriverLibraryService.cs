using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using PrintCore.Core;
using PrintCore.Infrastructure;
using PrintCore.LocalDriverCatalog;
using PrintCore.Logging;
using PrintCore.Models;

namespace PrintCore.Services
{
    /// <summary>
    /// Фасад локальной библиотеки драйверов для слоёв представления.
    /// </summary>
    public class DriverLibraryService : ObservableObject
    {
        private readonly LocalDriverCatalogService _catalog;
        private readonly OperationHistoryService _history;
        private readonly IAppLogger _logger;
        private bool _isLoading;
        private bool _hasLoaded;

        public DriverLibraryService(LocalDriverCatalogService catalog, OperationHistoryService history, IAppLogger logger)
        {
            _catalog = catalog;
            _history = history;
            _logger = logger;
        }

        public LocalDriverCatalogService Catalog
        {
            get { return _catalog; }
        }

        public ObservableCollection<DriverPackage> Drivers { get; } = new ObservableCollection<DriverPackage>();

        public bool IsAvailable
        {
            get { return true; }
        }

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

        public async Task RefreshAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            try
            {
                var items = await _catalog.LoadAllAsync();
                Drivers.Clear();
                foreach (var pkg in items)
                {
                    Drivers.Add(pkg);
                }

                HasLoaded = true;
            }
            catch (Exception ex)
            {
                _logger?.Error("LibraryService", "Не удалось загрузить локальную библиотеку драйверов", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<DriverPackage> ImportPackageAsync(
            string path,
            string manufacturer = null,
            string model = null,
            string version = null,
            CpuArchitecture? arch = null)
        {
            var pkg = await _catalog.ImportAsync(path, manufacturer, model, version, arch);
            await RefreshAsync();
            _history?.Log(
                OperationKind.DriverDownload,
                OperationResult.Success,
                "Драйвер добавлен в библиотеку: " + pkg.DisplayTitle,
                "Версия: " + (pkg.DriverVersion ?? "не указана") + " · Путь: " + pkg.LocalPath,
                pkg.Model);
            return pkg;
        }

        public async Task<int> RescanFolderAsync(CancellationToken ct = default(CancellationToken))
        {
            var count = await _catalog.RescanLibraryDirectoryAsync(ct);
            await RefreshAsync();
            _history?.Log(
                OperationKind.DriverSearch,
                OperationResult.Info,
                "Сканирование папки локальной библиотеки завершено",
                "Новых пакетов обнаружено: " + count);
            return count;
        }

        public async Task DeleteAsync(DriverPackage pkg, bool deleteFiles)
        {
            if (pkg == null) return;
            await Task.Run(() => _catalog.DeleteRecord(pkg, deleteFiles));
            Drivers.Remove(pkg);
            _history?.Log(
                OperationKind.DriverSearch,
                OperationResult.Info,
                "Пакет удалён из локальной библиотеки: " + pkg.DisplayTitle,
                deleteFiles ? "Локальные файлы пакета также удалены с диска" : "Файлы на диске сохранены");
        }
    }
}
