using System;
using System.IO;
using PrintCore.Core;

namespace PrintCore.Models
{
    /// <summary>Пользовательские настройки, сохраняемые в SQLite и XML.</summary>
    public class AppSettings
    {
        public static string ResolveBaseDataDirectory()
        {
            var overrideDir = Environment.GetEnvironmentVariable("PRINTCORE_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                return overrideDir;
            }

            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintCore");
            try
            {
                Directory.CreateDirectory(appData);
                return appData;
            }
            catch
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppData");
            }
        }

        public AppSettings()
        {
            LibraryDirectory = Path.Combine(ResolveBaseDataDirectory(), "Library");
        }

        public AppTheme Theme { get; set; } = AppTheme.Light;

        /// <summary>Каталог локальной библиотеки драйверов.</summary>
        public string LibraryDirectory { get; set; }

        /// <summary>Искать USB-принтеры.</summary>
        public bool ScanUsb { get; set; } = true;

        /// <summary>Искать сетевые принтеры (WSD, SNMP, TCP/IP).</summary>
        public bool ScanNetwork { get; set; } = true;

        /// <summary>Опрашивать устройства по SNMP v1 (UDP 161, public).</summary>
        public bool EnableSnmp { get; set; } = true;

        /// <summary>Проверять принтерные порты TCP 9100/631/515.</summary>
        public bool EnableTcpProbe { get; set; } = true;

        /// <summary>Диапазон IPv4 (пусто — автоматическая локальная подсеть /24 вокруг хоста).</summary>
        public string NetworkRange { get; set; } = "";

        /// <summary>Тайм-аут сетевого опроса одного узла в миллисекундах (200–5000).</summary>
        public int NetworkTimeoutMs { get; set; } = 650;

        /// <summary>Максимальное число сканируемых узлов за один проход (защита от бесконтрольного сканирования).</summary>
        public int MaxScanHosts { get; set; } = 64;
    }
}
