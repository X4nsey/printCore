using System;
using PrintCore.Models;

namespace PrintCore.Core
{
    /// <summary>Семейство версии Windows.</summary>
    public enum WindowsVersion
    {
        Any = 0,
        Windows7 = 7,
        Windows8 = 8,
        Windows81 = 81,
        Windows10 = 10,
        Windows11 = 11
    }

    /// <summary>Поддерживаемая разрядность ОС для пакета драйвера.</summary>
    [Flags]
    public enum CpuArchitecture
    {
        None = 0,
        X86 = 1,
        X64 = 2,
        Both = X86 | X64
    }

    /// <summary>Тип установочного пакета драйвера.</summary>
    public enum PackageType
    {
        Unknown,
        InfDirectory,
        ZipArchive,
        ExecutableInstaller,
        MsiInstaller,
        CabArchive
    }

    /// <summary>Состояние доступности обнаруженного устройства.</summary>
    public enum DeviceAvailability
    {
        Unknown,
        Online,
        Offline,
        Attention
    }

    /// <summary>Результат операции в журнале.</summary>
    public enum OperationResult
    {
        Info,
        Success,
        Warning,
        Error,
        Cancelled
    }

    /// <summary>Состояние цифровой подписи пакета.</summary>
    public enum SignatureState
    {
        NotChecked,
        Valid,
        Unsigned,
        Invalid
    }

    /// <summary>Степень совпадения драйвера с устройством и системой.</summary>
    public enum MatchLevel
    {
        Incompatible,
        ManufacturerGeneric,
        ModelMatch,
        HardwareIdExact
    }
}
