using System;
using System.Collections.Generic;
using PrintCore.Models;

namespace PrintCore.Core
{
    /// <summary>Производитель принтеров.</summary>
    public class ManufacturerInfo
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string OfficialSiteUrl { get; set; }
        public string SupportSearchUrlTemplate { get; set; }
        public string UsbVendorId { get; set; }
    }

    /// <summary>Описание пакета драйвера в локальной библиотеке или онлайн-каталоге.</summary>
    public class DriverPackage
    {
        public long Id { get; set; }
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string Title { get; set; }
        public string DriverVersion { get; set; }
        public DateTime? DriverDate { get; set; }
        public CpuArchitecture Architecture { get; set; } = CpuArchitecture.Both;
        public string SupportedOsText { get; set; } = "Windows 7 / 8 / 8.1 / 10 / 11";
        public List<WindowsVersion> SupportedOsList { get; set; } = new List<WindowsVersion>();
        public List<string> HardwareIds { get; set; } = new List<string>();
        public List<string> SupportedModels { get; set; } = new List<string>();
        public PackageType PackageType { get; set; } = PackageType.InfDirectory;
        public string LocalPath { get; set; }
        public string PrimaryInfFile { get; set; }
        public string SourceName { get; set; }
        public string SourceUrl { get; set; }
        public string OfficialSupportUrl { get; set; }
        public string Sha256 { get; set; }
        public long TotalSizeBytes { get; set; }
        public SignatureState SignatureState { get; set; } = SignatureState.NotChecked;
        public string SignerSubject { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.Now;
        public bool IsLocal { get; set; } = true;

        /// <summary>Если пакет из онлайн-каталога требует перехода на официальную страницу производителя.</summary>
        public bool RequiresBrowserDownload
        {
            get { return !IsLocal && string.IsNullOrWhiteSpace(SourceUrl) && !string.IsNullOrWhiteSpace(OfficialSupportUrl); }
        }

        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title)) return Title;
                return BuildCleanTitle(Manufacturer, Model);
            }
        }

        public static string BuildCleanTitle(string manufacturer, string model)
        {
            var mfg = (manufacturer ?? "").Trim();
            var mdl = (model ?? "").Trim();
            if (string.IsNullOrEmpty(mfg)) return mdl;
            if (string.IsNullOrEmpty(mdl)) return mfg;
            if (mdl.StartsWith(mfg + " ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mdl, mfg, StringComparison.OrdinalIgnoreCase))
            {
                return mdl;
            }

            return mfg + " " + mdl;
        }

        public string ArchitectureText
        {
            get
            {
                switch (Architecture)
                {
                    case CpuArchitecture.X86: return "x86 (32-бит)";
                    case CpuArchitecture.X64: return "x64 (64-бит)";
                    case CpuArchitecture.Both: return "x86 / x64";
                    default: return "Не указана";
                }
            }
        }

        public string PackageTypeText
        {
            get
            {
                switch (PackageType)
                {
                    case PackageType.InfDirectory: return "Пакет INF";
                    case PackageType.ZipArchive: return "Архив ZIP";
                    case PackageType.CabArchive: return "Пакет CAB";
                    case PackageType.ExecutableInstaller: return "Установщик EXE";
                    case PackageType.MsiInstaller: return "Пакет MSI";
                    default: return "Пакет драйвера";
                }
            }
        }

        public string SignatureText
        {
            get
            {
                switch (SignatureState)
                {
                    case SignatureState.Valid:
                        return string.IsNullOrEmpty(SignerSubject) ? "Подпись действительна" : "Подписан: " + SignerSubject;
                    case SignatureState.Unsigned:
                        return "Цифровая подпись отсутствует";
                    case SignatureState.Invalid:
                        return "Подпись недействительна";
                    default:
                        return "Подпись не проверялась";
                }
            }
        }

        public StatusTone SignatureTone
        {
            get
            {
                switch (SignatureState)
                {
                    case SignatureState.Valid: return StatusTone.Success;
                    case SignatureState.Unsigned: return StatusTone.Warning;
                    case SignatureState.Invalid: return StatusTone.Danger;
                    default: return StatusTone.Neutral;
                }
            }
        }

        public string SizeText
        {
            get
            {
                if (TotalSizeBytes <= 0) return "—";
                if (TotalSizeBytes < 1024) return TotalSizeBytes + " Б";
                if (TotalSizeBytes < 1024 * 1024) return (TotalSizeBytes / 1024.0).ToString("0.0") + " КБ";
                return (TotalSizeBytes / (1024.0 * 1024.0)).ToString("0.0") + " МБ";
            }
        }

        public string AddedAtText
        {
            get { return AddedAt.ToString("dd.MM.yyyy HH:mm"); }
        }
    }

    /// <summary>Вариант драйвера, подобранный для конкретного принтера, с оценкой совместимости.</summary>
    public class DriverCandidate
    {
        public DriverPackage Package { get; set; }
        public MatchLevel MatchLevel { get; set; }
        public bool IsOsCompatible { get; set; }
        public bool IsArchitectureCompatible { get; set; }
        public string CompatibilitySummary { get; set; }

        public bool IsFullyCompatible
        {
            get { return IsOsCompatible && IsArchitectureCompatible && MatchLevel != MatchLevel.Incompatible; }
        }

        public string MatchText
        {
            get
            {
                switch (MatchLevel)
                {
                    case MatchLevel.HardwareIdExact: return "Точное совпадение Hardware ID";
                    case MatchLevel.ModelMatch: return "Совпадение модели";
                    case MatchLevel.ManufacturerGeneric: return "Универсальный драйвер производителя";
                    default: return "Несовместим";
                }
            }
        }

        public StatusTone CompatibilityTone
        {
            get
            {
                if (!IsFullyCompatible) return StatusTone.Danger;
                return MatchLevel == MatchLevel.ManufacturerGeneric ? StatusTone.Warning : StatusTone.Success;
            }
        }

        public string SourceBadgeText
        {
            get
            {
                if (Package == null) return "";
                if (Package.IsLocal) return "Локальная библиотека";
                return Package.RequiresBrowserDownload ? "Официальный сайт" : "Онлайн-каталог";
            }
        }

        public StatusTone SourceBadgeTone
        {
            get { return Package != null && Package.IsLocal ? StatusTone.Success : StatusTone.Accent; }
        }
    }
}
