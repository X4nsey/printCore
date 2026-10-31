using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrintCore.Core;
using PrintCore.Models;

namespace PrintCore.DriverCatalog
{
    /// <summary>Запрос на подбор подходящего драйвера для обнаруженного принтера.</summary>
    public class DriverSearchQuery
    {
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string HardwareId { get; set; }
        public WindowsVersion TargetOs { get; set; } = WindowsVersion.Windows10;
        public CpuArchitecture TargetArchitecture { get; set; } = CpuArchitecture.X64;
    }

    /// <summary>Общий интерфейс каталога драйверов (локального или сетевого).</summary>
    public interface IDriverCatalog
    {
        Task<IReadOnlyList<DriverCandidate>> FindCandidatesAsync(DriverSearchQuery query, CancellationToken cancellationToken = default(CancellationToken));
    }

    /// <summary>
    /// Алгоритм сопоставления драйвера с принтером и системой:
    /// проверяет разрядность, версию Windows, совпадение Hardware ID или точной модели.
    /// Не подменяет точное совпадение модели случайным похожим драйвером.
    /// </summary>
    public static class DriverMatcher
    {
        public static DriverCandidate Evaluate(DriverPackage pkg, DriverSearchQuery query)
        {
            if (pkg == null || query == null)
            {
                return null;
            }

            var archOk = (pkg.Architecture & query.TargetArchitecture) != 0 || pkg.Architecture == CpuArchitecture.Both;
            var osOk = pkg.SupportedOsList == null || pkg.SupportedOsList.Count == 0
                || pkg.SupportedOsList.Contains(WindowsVersion.Any)
                || pkg.SupportedOsList.Contains(query.TargetOs);

            var matchLevel = MatchDevice(pkg, query);
            if (matchLevel == MatchLevel.Incompatible)
            {
                return null;
            }

            var summaryParts = new List<string>
            {
                archOk ? ("Совместим с " + (query.TargetArchitecture == CpuArchitecture.X64 ? "x64" : "x86")) : "Неподходящая архитектура",
                osOk ? "Поддерживает текущую версию Windows" : "Версия Windows не заявлена в пакете"
            };

            return new DriverCandidate
            {
                Package = pkg,
                MatchLevel = matchLevel,
                IsArchitectureCompatible = archOk,
                IsOsCompatible = osOk,
                CompatibilitySummary = string.Join(" · ", summaryParts)
            };
        }

        private static MatchLevel MatchDevice(DriverPackage pkg, DriverSearchQuery query)
        {
            // 1. Точное совпадение по Hardware ID (VID/PID или USBPRINT\...)
            if (!string.IsNullOrWhiteSpace(query.HardwareId) && pkg.HardwareIds != null && pkg.HardwareIds.Count > 0)
            {
                var qHw = NormalizeHwId(query.HardwareId);
                foreach (var hw in pkg.HardwareIds)
                {
                    var pkgHw = NormalizeHwId(hw);
                    if (!string.IsNullOrEmpty(pkgHw) && (qHw.Contains(pkgHw) || pkgHw.Contains(qHw)))
                    {
                        return MatchLevel.HardwareIdExact;
                    }
                }
            }

            // 2. Проверка производителя
            var qMfg = VendorKnowledgeBase.NormalizeManufacturer(query.Manufacturer, query.HardwareId);
            var pMfg = VendorKnowledgeBase.NormalizeManufacturer(pkg.Manufacturer);
            var sameManufacturer = !string.IsNullOrEmpty(qMfg) && !string.IsNullOrEmpty(pMfg)
                && string.Equals(qMfg, pMfg, StringComparison.OrdinalIgnoreCase);

            // 3. Проверка точного совпадения модели (без подмены случайными числами)
            var qModelNorm = NormalizeModelToken(query.Model, qMfg);
            if (!string.IsNullOrEmpty(qModelNorm))
            {
                var candidates = new List<string>();
                if (!string.IsNullOrWhiteSpace(pkg.Model)) candidates.Add(pkg.Model);
                if (pkg.SupportedModels != null) candidates.AddRange(pkg.SupportedModels);

                foreach (var c in candidates)
                {
                    var cNorm = NormalizeModelToken(c, pMfg);
                    if (!string.IsNullOrEmpty(cNorm) && string.Equals(qModelNorm, cNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        return MatchLevel.ModelMatch;
                    }
                }
            }

            // 4. Универсальный драйвер того же производителя (только если пакет явно помечен как Universal)
            if (sameManufacturer && IsUniversalPackage(pkg))
            {
                return MatchLevel.ManufacturerGeneric;
            }

            return MatchLevel.Incompatible;
        }

        private static bool IsUniversalPackage(DriverPackage pkg)
        {
            var text = ((pkg.Title ?? "") + " " + (pkg.Model ?? "")).ToUpperInvariant();
            return text.Contains("UNIVERSAL") || text.Contains("УНИВЕРСАЛЬН") || text.Contains("GLOBAL PRINT DRIVER");
        }

        private static string NormalizeHwId(string hw)
        {
            if (string.IsNullOrWhiteSpace(hw)) return "";
            var upper = hw.Trim().ToUpperInvariant();
            var idx = upper.IndexOf("&REV_", StringComparison.Ordinal);
            if (idx > 0) upper = upper.Substring(0, idx);
            return upper;
        }

        private static string NormalizeModelToken(string model, string manufacturer)
        {
            if (string.IsNullOrWhiteSpace(model)) return "";
            var upper = model.ToUpperInvariant();
            if (!string.IsNullOrEmpty(manufacturer))
            {
                upper = upper.Replace(manufacturer.ToUpperInvariant(), " ");
            }

            upper = upper
                .Replace("SERIES", " ")
                .Replace("PRINTER", " ")
                .Replace("PCL6", " ")
                .Replace("PCL 6", " ")
                .Replace("UFR II", " ")
                .Replace("CLASS DRIVER", " ");

            var chars = upper.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }
    }
}
