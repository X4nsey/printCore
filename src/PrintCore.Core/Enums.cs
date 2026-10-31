namespace PrintCore.Models
{
    /// <summary>Тема оформления приложения.</summary>
    public enum AppTheme
    {
        Light,
        Dark,
        System
    }

    /// <summary>Разделы приложения.</summary>
    public enum PageKey
    {
        Dashboard,
        PrinterDiscovery,
        InstalledPrinters,
        DriverLibrary,
        OperationHistory,
        Settings
    }

    /// <summary>Тип подключения принтера.</summary>
    public enum ConnectionType
    {
        Unknown,
        Usb,
        Network,
        Wsd,
        Other
    }

    /// <summary>Состояние драйвера для обнаруженного принтера.</summary>
    public enum DriverStatus
    {
        Unknown,
        Searching,
        NotFound,
        Available,
        Downloaded,
        Installed,
        Error
    }

    /// <summary>Тип операции в журнале.</summary>
    public enum OperationKind
    {
        PrinterDiscovered,
        DriverSearch,
        DriverDownload,
        DriverInstall,
        Error
    }

    /// <summary>Визуальный тон индикатора состояния.</summary>
    public enum StatusTone
    {
        Neutral,
        Accent,
        Success,
        Warning,
        Danger
    }
}
