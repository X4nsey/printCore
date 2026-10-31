using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using PrintCore.Core;
using PrintCore.Data;
using PrintCore.Logging;
using PrintCore.Models;

namespace PrintCore.Services
{
    /// <summary>
    /// Загрузка и сохранение пользовательских настроек в таблице ApplicationSettings базы SQLite
    /// (с дублированием в %AppData%\PrintCore\settings.xml для обратной совместимости с Этапом 1).
    /// </summary>
    public class SettingsService
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(AppSettings));
        private readonly PrintCoreDatabase _db;
        private readonly IAppLogger _logger;

        public SettingsService(PrintCoreDatabase db = null, IAppLogger logger = null)
        {
            _db = db;
            _logger = logger;
            DataDirectory = db != null
                ? db.DataDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintCore");
            SettingsFilePath = Path.Combine(DataDirectory, "settings.xml");
        }

        public string DataDirectory { get; }

        public string SettingsFilePath { get; }

        public AppSettings Current { get; private set; } = new AppSettings();

        public AppSettings Load()
        {
            var settings = new AppSettings();

            // 1. Чтение из XML, если файл уже существует
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    using (var stream = File.OpenRead(SettingsFilePath))
                    {
                        settings = (AppSettings)Serializer.Deserialize(stream) ?? new AppSettings();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn("Settings", "Не удалось прочитать settings.xml: " + ex.Message);
            }

            // 2. Чтение из таблицы ApplicationSettings в SQLite (приоритетный источник)
            if (_db != null)
            {
                try
                {
                    var map = _db.LoadSettingsDictionary();
                    string val;
                    AppTheme parsedTheme;
                    if (map.TryGetValue("Theme", out val) && Enum.TryParse(val, out parsedTheme))
                    {
                        settings.Theme = parsedTheme;
                    }

                    if (map.TryGetValue("LibraryDirectory", out val) && !string.IsNullOrWhiteSpace(val))
                    {
                        settings.LibraryDirectory = val;
                    }

                    bool b;
                    if (map.TryGetValue("ScanUsb", out val) && bool.TryParse(val, out b)) settings.ScanUsb = b;
                    if (map.TryGetValue("ScanNetwork", out val) && bool.TryParse(val, out b)) settings.ScanNetwork = b;
                    if (map.TryGetValue("EnableSnmp", out val) && bool.TryParse(val, out b)) settings.EnableSnmp = b;
                    if (map.TryGetValue("EnableTcpProbe", out val) && bool.TryParse(val, out b)) settings.EnableTcpProbe = b;
                    if (map.TryGetValue("NetworkRange", out val)) settings.NetworkRange = val ?? "";

                    int num;
                    if (map.TryGetValue("NetworkTimeoutMs", out val) && int.TryParse(val, out num)) settings.NetworkTimeoutMs = num;
                    if (map.TryGetValue("MaxScanHosts", out val) && int.TryParse(val, out num)) settings.MaxScanHosts = num;
                }
                catch (Exception ex)
                {
                    _logger?.Warn("Settings", "Не удалось прочитать ApplicationSettings из SQLite: " + ex.Message);
                }
            }

            if (string.IsNullOrWhiteSpace(settings.LibraryDirectory))
            {
                settings.LibraryDirectory = Path.Combine(DataDirectory, "Library");
            }

            Current = settings;
            return Current;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                if (!string.IsNullOrWhiteSpace(Current.LibraryDirectory))
                {
                    try { Directory.CreateDirectory(Current.LibraryDirectory); } catch { }
                }

                // Сохранение в SQLite
                if (_db != null)
                {
                    _db.SaveSettingsDictionary(new Dictionary<string, string>
                    {
                        ["Theme"] = Current.Theme.ToString(),
                        ["LibraryDirectory"] = Current.LibraryDirectory ?? "",
                        ["ScanUsb"] = Current.ScanUsb.ToString(),
                        ["ScanNetwork"] = Current.ScanNetwork.ToString(),
                        ["EnableSnmp"] = Current.EnableSnmp.ToString(),
                        ["EnableTcpProbe"] = Current.EnableTcpProbe.ToString(),
                        ["NetworkRange"] = Current.NetworkRange ?? "",
                        ["NetworkTimeoutMs"] = Current.NetworkTimeoutMs.ToString(),
                        ["MaxScanHosts"] = Current.MaxScanHosts.ToString()
                    });
                }

                // Дублирование в XML
                var tempPath = SettingsFilePath + ".tmp";
                using (var stream = File.Create(tempPath))
                {
                    Serializer.Serialize(stream, Current);
                }

                if (File.Exists(SettingsFilePath))
                {
                    File.Delete(SettingsFilePath);
                }

                File.Move(tempPath, SettingsFilePath);
            }
            catch (Exception ex)
            {
                _logger?.Error("Settings", "Ошибка сохранения настроек", ex);
            }
        }
    }
}
