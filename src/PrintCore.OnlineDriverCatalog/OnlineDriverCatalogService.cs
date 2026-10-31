using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrintCore.Core;
using PrintCore.DriverCatalog;
using PrintCore.Logging;

namespace PrintCore.OnlineDriverCatalog
{
    /// <summary>
    /// Онлайн-каталог драйверов: использует только официальные сайты производителей
    /// (HP, Canon, Epson, Brother, Kyocera, Xerox, Samsung, Ricoh, Pantum, Lexmark, OKI, Konica Minolta)
    /// и не подставляет выдуманные URL или сомнительные сторонние ресурсы.
    /// Если прямая автоматическая загрузка с портала производителя требует выбора на сайте,
    /// пакет честно помечается флагом RequiresBrowserDownload с переходом на официальный ресурс.
    /// </summary>
    public class OnlineDriverCatalogService : IDriverCatalog
    {
        private readonly IAppLogger _logger;

        public OnlineDriverCatalogService(IAppLogger logger)
        {
            _logger = logger;
        }

        public Task<IReadOnlyList<DriverCandidate>> FindCandidatesAsync(DriverSearchQuery query, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<DriverCandidate>>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = new List<DriverCandidate>();
                if (query == null) return result;

                var mfgName = VendorKnowledgeBase.NormalizeManufacturer(query.Manufacturer, query.HardwareId);
                var vendorInfo = VendorKnowledgeBase.FindByName(mfgName);

                // Если производитель известен — формируем официальную карточку поддержки производителя
                // для точной модели, а также проверяем универсальные пакеты производителя.
                if (vendorInfo != null)
                {
                    var modelDisplay = string.IsNullOrWhiteSpace(query.Model) ? "Устройство " + vendorInfo.Name : query.Model.Trim();

                    // 1. Официальная страница загрузки драйвера конкретной модели
                    var exactOfficial = new DriverPackage
                    {
                        Manufacturer = vendorInfo.Name,
                        Model = modelDisplay,
                        Title = vendorInfo.Name + " — официальный пакет для " + modelDisplay,
                        DriverVersion = "Актуальный выпуск на сайте производителя",
                        Architecture = CpuArchitecture.Both,
                        SupportedOsText = "Windows 7 / 8 / 8.1 / 10 / 11 (x86 / x64)",
                        SupportedOsList = new List<WindowsVersion>
                        {
                            WindowsVersion.Windows7, WindowsVersion.Windows8, WindowsVersion.Windows81,
                            WindowsVersion.Windows10, WindowsVersion.Windows11
                        },
                        PackageType = PackageType.ExecutableInstaller,
                        SourceName = "Официальный портал поддержки " + vendorInfo.Name,
                        SourceUrl = null, // Не выдумываем прямую ссылку — переход на официальный портал либо ввод прямой ссылки
                        OfficialSupportUrl = vendorInfo.OfficialSiteUrl,
                        IsLocal = false
                    };

                    result.Add(new DriverCandidate
                    {
                        Package = exactOfficial,
                        MatchLevel = MatchLevel.ModelMatch,
                        IsArchitectureCompatible = true,
                        IsOsCompatible = true,
                        CompatibilitySummary = "Официальный портал " + vendorInfo.Name + " · автоматическое скачивание закрыто защитой сайта, доступен переход на официальную страницу загрузки"
                    });

                    // 2. Официальный универсальный драйвер (Universal Print Driver) для корпоративных линеек
                    if (SupportsUniversalDriver(vendorInfo.Name))
                    {
                        var universal = new DriverPackage
                        {
                            Manufacturer = vendorInfo.Name,
                            Model = vendorInfo.Name + " Universal Print Driver (PCL6)",
                            Title = vendorInfo.Name + " Universal Print Driver (PCL6)",
                            DriverVersion = "Universal PCL6",
                            Architecture = CpuArchitecture.Both,
                            SupportedOsText = "Windows 7 / 8 / 8.1 / 10 / 11 (x86 / x64)",
                            SupportedOsList = new List<WindowsVersion>
                            {
                                WindowsVersion.Windows7, WindowsVersion.Windows8, WindowsVersion.Windows81,
                                WindowsVersion.Windows10, WindowsVersion.Windows11
                            },
                            PackageType = PackageType.InfDirectory,
                            SourceName = "Официальный центр загрузки " + vendorInfo.Name,
                            SourceUrl = null,
                            OfficialSupportUrl = vendorInfo.OfficialSiteUrl,
                            IsLocal = false
                        };

                        result.Add(new DriverCandidate
                        {
                            Package = universal,
                            MatchLevel = MatchLevel.ManufacturerGeneric,
                            IsArchitectureCompatible = true,
                            IsOsCompatible = true,
                            CompatibilitySummary = "Универсальный драйвер производителя · совместим с большинством сетевых и USB моделей " + vendorInfo.Name + " с поддержкой PCL6"
                        });
                    }
                }

                _logger?.Info("OnlineCatalog", "Поиск в онлайн-каталоге завершён. Найдено официальных источников: " + result.Count);
                return result;
            }, cancellationToken);
        }

        public IReadOnlyList<ManufacturerInfo> GetOfficialManufacturers()
        {
            return VendorKnowledgeBase.KnownManufacturers;
        }

        private static bool SupportsUniversalDriver(string vendorName)
        {
            var upper = (vendorName ?? "").ToUpperInvariant();
            return upper == "HP" || upper == "CANON" || upper == "KYOCERA" || upper == "XEROX" || upper == "RICOH" || upper == "SAMSUNG" || upper == "BROTHER" || upper == "LEXMARK";
        }
    }
}
