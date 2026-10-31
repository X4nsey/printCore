using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrintCore.Core;
using PrintCore.Data;
using PrintCore.DriverCatalog;
using PrintCore.Logging;

namespace PrintCore.LocalDriverCatalog
{
    /// <summary>
    /// Реализация локальной библиотеки драйверов на диске и в SQLite:
    /// импорт папок/файлов, копирование в хранилище PrintCore, парсинг INF,
    /// расчёт SHA-256, проверка подписи и подбор совместимых пакетов.
    /// </summary>
    public class LocalDriverCatalogService : IDriverCatalog
    {
        private readonly PrintCoreDatabase _db;
        private readonly IAppLogger _logger;

        public LocalDriverCatalogService(PrintCoreDatabase db, IAppLogger logger, string libraryDirectory)
        {
            _db = db;
            _logger = logger;
            LibraryDirectory = libraryDirectory;
        }

        public string LibraryDirectory { get; set; }

        public Task<IReadOnlyList<DriverPackage>> LoadAllAsync()
        {
            return Task.Run<IReadOnlyList<DriverPackage>>(() => _db.GetAllDrivers());
        }

        public Task<IReadOnlyList<DriverCandidate>> FindCandidatesAsync(DriverSearchQuery query, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<DriverCandidate>>(() =>
            {
                var all = _db.GetAllDrivers();
                var matches = new List<DriverCandidate>();
                foreach (var pkg in all)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Directory.Exists(pkg.LocalPath) && !File.Exists(pkg.LocalPath))
                    {
                        continue;
                    }

                    var candidate = DriverMatcher.Evaluate(pkg, query);
                    if (candidate != null)
                    {
                        matches.Add(candidate);
                    }
                }

                return matches
                    .OrderByDescending(c => c.IsFullyCompatible)
                    .ThenByDescending(c => c.MatchLevel)
                    .ToList();
            }, cancellationToken);
        }

        /// <summary>
        /// Импортирует файл или папку драйвера в управляемое хранилище библиотеки и сохраняет в SQLite.
        /// </summary>
        public Task<DriverPackage> ImportAsync(
            string sourcePath,
            string overrideManufacturer = null,
            string overrideModel = null,
            string overrideVersion = null,
            CpuArchitecture? overrideArch = null,
            string sourceName = "Локальный импорт",
            string sourceUrl = null,
            bool copyToLibrary = true,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    throw new ArgumentException("Путь для импорта не задан.");
                }

                Directory.CreateDirectory(LibraryDirectory);
                var targetPath = sourcePath;

                if (copyToLibrary && !IsInsideLibrary(sourcePath))
                {
                    var folderName = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
                    var destDir = Path.Combine(LibraryDirectory, folderName);
                    Directory.CreateDirectory(destDir);

                    if (File.Exists(sourcePath))
                    {
                        var destFile = Path.Combine(destDir, Path.GetFileName(sourcePath));
                        File.Copy(sourcePath, destFile, true);
                        targetPath = Path.GetExtension(destFile).Equals(".inf", StringComparison.OrdinalIgnoreCase) ? destDir : destFile;
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        CopyDirectoryRecursive(sourcePath, destDir, cancellationToken);
                        targetPath = destDir;
                    }
                }

                List<Tuple<string, long, string>> files;
                var pkg = InfFileParser.InspectPackagePath(targetPath, out files);

                if (!string.IsNullOrWhiteSpace(overrideManufacturer)) pkg.Manufacturer = overrideManufacturer.Trim();
                if (!string.IsNullOrWhiteSpace(overrideModel)) pkg.Model = overrideModel.Trim();
                if (!string.IsNullOrWhiteSpace(overrideVersion)) pkg.DriverVersion = overrideVersion.Trim();
                if (overrideArch.HasValue && overrideArch.Value != CpuArchitecture.None) pkg.Architecture = overrideArch.Value;
                if (!string.IsNullOrWhiteSpace(sourceName)) pkg.SourceName = sourceName;
                if (!string.IsNullOrWhiteSpace(sourceUrl)) pkg.SourceUrl = sourceUrl;

                if (string.IsNullOrWhiteSpace(pkg.Manufacturer)) pkg.Manufacturer = "Не указан";
                if (string.IsNullOrWhiteSpace(pkg.Model)) pkg.Model = Path.GetFileName(targetPath);
                pkg.Title = DriverPackage.BuildCleanTitle(pkg.Manufacturer, pkg.Model);

                _db.SaveDriverPackage(pkg, files);
                _logger?.Info("LocalCatalog", "Импортирован пакет драйвера: " + pkg.DisplayTitle + " (" + pkg.LocalPath + ")");
                return pkg;
            }, cancellationToken);
        }

        /// <summary>Сканирует корневую папку библиотеки и регистрирует новые подпапки/пакеты.</summary>
        public Task<int> RescanLibraryDirectoryAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(LibraryDirectory) || !Directory.Exists(LibraryDirectory))
                {
                    return 0;
                }

                var existingPaths = new HashSet<string>(
                    _db.GetAllDrivers().Select(d => d.LocalPath ?? ""),
                    StringComparer.OrdinalIgnoreCase);

                var added = 0;
                foreach (var subDir in Directory.GetDirectories(LibraryDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (existingPaths.Contains(subDir)) continue;

                    var hasContent = Directory.GetFiles(subDir, "*", SearchOption.AllDirectories).Length > 0;
                    if (!hasContent) continue;

                    List<Tuple<string, long, string>> files;
                    var pkg = InfFileParser.InspectPackagePath(subDir, out files);
                    _db.SaveDriverPackage(pkg, files);
                    added++;
                }

                return added;
            }, cancellationToken);
        }

        public void DeleteRecord(DriverPackage pkg, bool deleteLocalFiles)
        {
            if (pkg == null) return;
            _db.DeleteDriver(pkg.Id);

            if (deleteLocalFiles && !string.IsNullOrWhiteSpace(pkg.LocalPath) && IsInsideLibrary(pkg.LocalPath))
            {
                try
                {
                    if (File.Exists(pkg.LocalPath))
                    {
                        var parent = Path.GetDirectoryName(pkg.LocalPath);
                        File.Delete(pkg.LocalPath);
                        if (!string.IsNullOrEmpty(parent) && IsInsideLibrary(parent) &&
                            !string.Equals(Path.GetFullPath(parent).TrimEnd('\\'), Path.GetFullPath(LibraryDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) &&
                            Directory.GetFileSystemEntries(parent).Length == 0)
                        {
                            Directory.Delete(parent, false);
                        }
                    }
                    else if (Directory.Exists(pkg.LocalPath))
                    {
                        Directory.Delete(pkg.LocalPath, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warn("LocalCatalog", "Не удалось удалить локальные файлы пакета: " + ex.Message);
                }
            }
        }

        private bool IsInsideLibrary(string path)
        {
            try
            {
                var fullLib = Path.GetFullPath(LibraryDirectory).TrimEnd('\\', '/') + "\\";
                var fullTarget = Path.GetFullPath(path).TrimEnd('\\', '/') + "\\";
                return fullTarget.StartsWith(fullLib, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir, CancellationToken ct)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                ct.ThrowIfCancellationRequested();
                File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
            }

            foreach (var sub in Directory.GetDirectories(sourceDir))
            {
                ct.ThrowIfCancellationRequested();
                CopyDirectoryRecursive(sub, Path.Combine(targetDir, Path.GetFileName(sub)), ct);
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "driver";
            foreach (var ch in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(ch, '_');
            }

            return name.Length > 40 ? name.Substring(0, 40) : name;
        }
    }
}
