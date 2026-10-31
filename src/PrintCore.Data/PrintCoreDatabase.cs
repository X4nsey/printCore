using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PrintCore.Core;
using PrintCore.Logging;
using PrintCore.Models;

namespace PrintCore.Data
{
    /// <summary>
    /// Репозиторий базы данных SQLite PrintCore.
    /// Управляет схемой из 8 таблиц (Manufacturers, PrinterModels, Drivers,
    /// SupportedOperatingSystems, DriverFiles, DriverSources, OperationHistory, ApplicationSettings),
    /// обеспечивает режим WAL и транзакционную целостность.
    /// </summary>
    public class PrintCoreDatabase
    {
        private readonly object _sync = new object();
        private readonly IAppLogger _logger;

        public PrintCoreDatabase(IAppLogger logger, string dataDirectory = null)
        {
            _logger = logger;
            DataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
                ? AppSettings.ResolveBaseDataDirectory()
                : dataDirectory;
            DatabaseFilePath = Path.Combine(DataDirectory, "printcore.db");
        }

        public string DataDirectory { get; }

        public string DatabaseFilePath { get; }

        public void Initialize()
        {
            lock (_sync)
            {
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                    using (var conn = OpenConnection())
                    {
                        CreateSchema(conn);
                        SeedManufacturers(conn);
                    }

                    _logger?.Info("Data", "База данных SQLite инициализирована: " + DatabaseFilePath);
                }
                catch (Exception ex)
                {
                    _logger?.Error("Data", "Ошибка инициализации SQLite", ex);
                    throw;
                }
            }
        }

        private SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection(DatabaseFilePath);
            conn.ExecuteNonQuery("PRAGMA journal_mode = WAL;");
            conn.ExecuteNonQuery("PRAGMA synchronous = NORMAL;");
            conn.ExecuteNonQuery("PRAGMA foreign_keys = ON;");
            return conn;
        }

        private static void CreateSchema(SqliteConnection conn)
        {
            var statements = new[]
            {
                @"CREATE TABLE IF NOT EXISTS Manufacturers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    UsbVendorId TEXT,
                    OfficialSiteUrl TEXT,
                    SupportSearchUrlTemplate TEXT
                );",
                @"CREATE TABLE IF NOT EXISTS PrinterModels (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ManufacturerId INTEGER NOT NULL,
                    ModelName TEXT NOT NULL,
                    HardwareIdPattern TEXT,
                    FOREIGN KEY (ManufacturerId) REFERENCES Manufacturers(Id) ON DELETE CASCADE
                );",
                @"CREATE TABLE IF NOT EXISTS DriverSources (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SourceName TEXT NOT NULL,
                    SourceUrl TEXT,
                    OfficialSupportUrl TEXT,
                    IsOfficial INTEGER NOT NULL DEFAULT 1
                );",
                @"CREATE TABLE IF NOT EXISTS Drivers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ManufacturerId INTEGER NOT NULL,
                    PrinterModelId INTEGER,
                    SourceId INTEGER,
                    Title TEXT NOT NULL,
                    ModelName TEXT NOT NULL,
                    DriverVersion TEXT,
                    DriverDate TEXT,
                    Architecture INTEGER NOT NULL,
                    SupportedOsText TEXT,
                    HardwareIdsCsv TEXT,
                    PackageType INTEGER NOT NULL,
                    LocalPath TEXT NOT NULL,
                    PrimaryInfFile TEXT,
                    Sha256 TEXT,
                    TotalSizeBytes INTEGER NOT NULL DEFAULT 0,
                    SignatureState INTEGER NOT NULL DEFAULT 0,
                    SignerSubject TEXT,
                    AddedAt TEXT NOT NULL,
                    FOREIGN KEY (ManufacturerId) REFERENCES Manufacturers(Id),
                    FOREIGN KEY (PrinterModelId) REFERENCES PrinterModels(Id) ON DELETE SET NULL,
                    FOREIGN KEY (SourceId) REFERENCES DriverSources(Id) ON DELETE SET NULL
                );",
                @"CREATE TABLE IF NOT EXISTS SupportedOperatingSystems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DriverId INTEGER NOT NULL,
                    OsVersion INTEGER NOT NULL,
                    Architecture INTEGER NOT NULL,
                    FOREIGN KEY (DriverId) REFERENCES Drivers(Id) ON DELETE CASCADE
                );",
                @"CREATE TABLE IF NOT EXISTS DriverFiles (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DriverId INTEGER NOT NULL,
                    RelativePath TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    Sha256 TEXT,
                    FOREIGN KEY (DriverId) REFERENCES Drivers(Id) ON DELETE CASCADE
                );",
                @"CREATE TABLE IF NOT EXISTS OperationHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Kind INTEGER NOT NULL,
                    Result INTEGER NOT NULL,
                    Title TEXT NOT NULL,
                    Details TEXT,
                    TargetDevice TEXT,
                    Timestamp TEXT NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS ApplicationSettings (
                    Key TEXT PRIMARY KEY NOT NULL,
                    Value TEXT
                );"
            };

            foreach (var sql in statements)
            {
                conn.ExecuteNonQuery(sql);
            }
        }

        private static void SeedManufacturers(SqliteConnection conn)
        {
            foreach (var m in VendorKnowledgeBase.KnownManufacturers)
            {
                conn.ExecuteNonQuery(
                    @"INSERT OR IGNORE INTO Manufacturers (Name, UsbVendorId, OfficialSiteUrl, SupportSearchUrlTemplate)
                      VALUES (@Name, @Vid, @Url, @Search);",
                    new Dictionary<string, object>
                    {
                        ["@Name"] = m.Name,
                        ["@Vid"] = m.UsbVendorId,
                        ["@Url"] = m.OfficialSiteUrl,
                        ["@Search"] = m.SupportSearchUrlTemplate
                    });
            }
        }

        // ==================== НАСТРОЙКИ ====================

        public Dictionary<string, string> LoadSettingsDictionary()
        {
            lock (_sync)
            {
                using (var conn = OpenConnection())
                {
                    var pairs = conn.Query(
                        "SELECT Key, Value FROM ApplicationSettings;",
                        s => new KeyValuePair<string, string>(s.GetString(0), s.GetString(1)));
                    return pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        public void SaveSettingsDictionary(IDictionary<string, string> values)
        {
            lock (_sync)
            {
                using (var conn = OpenConnection())
                {
                    conn.ExecuteNonQuery("BEGIN IMMEDIATE;");
                    try
                    {
                        foreach (var kv in values)
                        {
                            conn.ExecuteNonQuery(
                                "INSERT OR REPLACE INTO ApplicationSettings (Key, Value) VALUES (@Key, @Value);",
                                new Dictionary<string, object> { ["@Key"] = kv.Key, ["@Value"] = kv.Value });
                        }

                        conn.ExecuteNonQuery("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecuteNonQuery("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
        }

        // ==================== ИСТОРИЯ ОПЕРАЦИЙ ====================

        public long InsertOperation(OperationRecord record)
        {
            lock (_sync)
            {
                using (var conn = OpenConnection())
                {
                    conn.ExecuteNonQuery(
                        @"INSERT INTO OperationHistory (Kind, Result, Title, Details, TargetDevice, Timestamp)
                          VALUES (@Kind, @Result, @Title, @Details, @Target, @Ts);",
                        new Dictionary<string, object>
                        {
                            ["@Kind"] = (int)record.Kind,
                            ["@Result"] = (int)record.Result,
                            ["@Title"] = record.Title ?? "",
                            ["@Details"] = record.Details,
                            ["@Target"] = record.TargetDevice,
                            ["@Ts"] = record.Timestamp.ToString("O")
                        });
                    record.Id = conn.LastInsertRowId();
                    return record.Id;
                }
            }
        }

        public List<OperationRecord> GetOperationHistory(int limit = 250)
        {
            lock (_sync)
            {
                using (var conn = OpenConnection())
                {
                    return conn.Query(
                        "SELECT Id, Kind, Result, Title, Details, TargetDevice, Timestamp FROM OperationHistory ORDER BY Id DESC LIMIT @Limit;",
                        s =>
                        {
                            DateTime dt;
                            if (!DateTime.TryParse(s.GetString(6), null, DateTimeStyles.RoundtripKind, out dt))
                            {
                                dt = DateTime.Now;
                            }

                            return new OperationRecord
                            {
                                Id = s.GetInt64(0),
                                Kind = (OperationKind)s.GetInt32(1),
                                Result = (OperationResult)s.GetInt32(2),
                                Title = s.GetString(3),
                                Details = s.GetString(4),
                                TargetDevice = s.GetString(5),
                                Timestamp = dt
                            };
                        },
                        new Dictionary<string, object> { ["@Limit"] = limit });
                }
            }
        }

        public void ClearOperationHistory()
        {
            lock (_sync)
            {
                using (var conn = OpenConnection())
                {
                    conn.ExecuteNonQuery("DELETE FROM OperationHistory;");
                }
            }
        }

        // ==================== ДРАЙВЕРЫ ====================

        public long SaveDriverPackage(DriverPackage pkg, IEnumerable<Tuple<string, long, string>> files = null)
        {
            lock (_sync)
            {
                using (var conn = OpenConnection())
                {
                    conn.ExecuteNonQuery("BEGIN IMMEDIATE;");
                    try
                    {
                        var manufacturerName = string.IsNullOrWhiteSpace(pkg.Manufacturer) ? "Прочие" : pkg.Manufacturer.Trim();
                        conn.ExecuteNonQuery(
                            "INSERT OR IGNORE INTO Manufacturers (Name) VALUES (@Name);",
                            new Dictionary<string, object> { ["@Name"] = manufacturerName });

                        var mfgId = conn.ExecuteScalarLong(
                            "SELECT Id FROM Manufacturers WHERE Name = @Name LIMIT 1;",
                            new Dictionary<string, object> { ["@Name"] = manufacturerName });

                        var modelName = string.IsNullOrWhiteSpace(pkg.Model) ? "Универсальная модель" : pkg.Model.Trim();
                        conn.ExecuteNonQuery(
                            "INSERT INTO PrinterModels (ManufacturerId, ModelName, HardwareIdPattern) VALUES (@Mfg, @Model, @Hw);",
                            new Dictionary<string, object>
                            {
                                ["@Mfg"] = mfgId,
                                ["@Model"] = modelName,
                                ["@Hw"] = pkg.HardwareIds != null && pkg.HardwareIds.Count > 0 ? pkg.HardwareIds[0] : null
                            });
                        var modelId = conn.LastInsertRowId();

                        conn.ExecuteNonQuery(
                            "INSERT INTO DriverSources (SourceName, SourceUrl, OfficialSupportUrl, IsOfficial) VALUES (@Name, @Url, @Support, 1);",
                            new Dictionary<string, object>
                            {
                                ["@Name"] = pkg.SourceName ?? "Локальный импорт",
                                ["@Url"] = pkg.SourceUrl,
                                ["@Support"] = pkg.OfficialSupportUrl
                            });
                        var sourceId = conn.LastInsertRowId();

                        conn.ExecuteNonQuery(
                            @"INSERT INTO Drivers (
                                ManufacturerId, PrinterModelId, SourceId, Title, ModelName, DriverVersion, DriverDate,
                                Architecture, SupportedOsText, HardwareIdsCsv, PackageType, LocalPath, PrimaryInfFile,
                                Sha256, TotalSizeBytes, SignatureState, SignerSubject, AddedAt
                              ) VALUES (
                                @Mfg, @ModelId, @SrcId, @Title, @Model, @Ver, @Date,
                                @Arch, @OsText, @HwCsv, @PkgType, @Path, @Inf,
                                @Sha, @Size, @SigState, @Signer, @Added
                              );",
                            new Dictionary<string, object>
                            {
                                ["@Mfg"] = mfgId,
                                ["@ModelId"] = modelId,
                                ["@SrcId"] = sourceId,
                                ["@Title"] = pkg.DisplayTitle,
                                ["@Model"] = modelName,
                                ["@Ver"] = pkg.DriverVersion,
                                ["@Date"] = pkg.DriverDate.HasValue ? pkg.DriverDate.Value.ToString("yyyy-MM-dd") : null,
                                ["@Arch"] = (int)pkg.Architecture,
                                ["@OsText"] = pkg.SupportedOsText,
                                ["@HwCsv"] = pkg.HardwareIds != null ? string.Join(";", pkg.HardwareIds) : "",
                                ["@PkgType"] = (int)pkg.PackageType,
                                ["@Path"] = pkg.LocalPath ?? "",
                                ["@Inf"] = pkg.PrimaryInfFile,
                                ["@Sha"] = pkg.Sha256,
                                ["@Size"] = pkg.TotalSizeBytes,
                                ["@SigState"] = (int)pkg.SignatureState,
                                ["@Signer"] = pkg.SignerSubject,
                                ["@Added"] = pkg.AddedAt.ToString("O")
                            });
                        var driverId = conn.LastInsertRowId();
                        pkg.Id = driverId;

                        var osList = pkg.SupportedOsList != null && pkg.SupportedOsList.Count > 0
                            ? pkg.SupportedOsList
                            : new List<WindowsVersion> { WindowsVersion.Windows7, WindowsVersion.Windows8, WindowsVersion.Windows81, WindowsVersion.Windows10, WindowsVersion.Windows11 };

                        foreach (var os in osList)
                        {
                            conn.ExecuteNonQuery(
                                "INSERT INTO SupportedOperatingSystems (DriverId, OsVersion, Architecture) VALUES (@Drv, @Os, @Arch);",
                                new Dictionary<string, object>
                                {
                                    ["@Drv"] = driverId,
                                    ["@Os"] = (int)os,
                                    ["@Arch"] = (int)pkg.Architecture
                                });
                        }

                        if (files != null)
                        {
                            foreach (var f in files)
                            {
                                conn.ExecuteNonQuery(
                                    "INSERT INTO DriverFiles (DriverId, RelativePath, FileSize, Sha256) VALUES (@Drv, @Rel, @Sz, @Sha);",
                                    new Dictionary<string, object>
                                    {
                                        ["@Drv"] = driverId,
                                        ["@Rel"] = f.Item1,
                                        ["@Sz"] = f.Item2,
                                        ["@Sha"] = f.Item3
                                    });
                            }
                        }

                        conn.ExecuteNonQuery("COMMIT;");
                        return driverId;
                    }
                    catch
                    {
                        try { conn.ExecuteNonQuery("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
        }

        public List<DriverPackage> GetAllDrivers()
        {
            lock (_sync)
            {
                using (var conn = OpenConnection())
                {
                    var list = conn.Query(
                        @"SELECT d.Id, m.Name, d.ModelName, d.Title, d.DriverVersion, d.DriverDate,
                                 d.Architecture, d.SupportedOsText, d.HardwareIdsCsv, d.PackageType,
                                 d.LocalPath, d.PrimaryInfFile, d.Sha256, d.TotalSizeBytes,
                                 d.SignatureState, d.SignerSubject, d.AddedAt,
                                 s.SourceName, s.SourceUrl, s.OfficialSupportUrl
                          FROM Drivers d
                          JOIN Manufacturers m ON m.Id = d.ManufacturerId
                          LEFT JOIN DriverSources s ON s.Id = d.SourceId
                          ORDER BY d.Id DESC;",
                        s =>
                        {
                            DateTime added;
                            if (!DateTime.TryParse(s.GetString(16), null, DateTimeStyles.RoundtripKind, out added))
                            {
                                added = DateTime.Now;
                            }

                            DateTime drvDate;
                            DateTime? parsedDrvDate = DateTime.TryParse(s.GetString(5), out drvDate) ? drvDate : (DateTime?)null;

                            var hwCsv = s.GetString(8) ?? "";
                            var hwList = hwCsv.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                            return new DriverPackage
                            {
                                Id = s.GetInt64(0),
                                Manufacturer = s.GetString(1),
                                Model = s.GetString(2),
                                Title = s.GetString(3),
                                DriverVersion = s.GetString(4),
                                DriverDate = parsedDrvDate,
                                Architecture = (CpuArchitecture)s.GetInt32(6),
                                SupportedOsText = s.GetString(7),
                                HardwareIds = hwList,
                                PackageType = (PackageType)s.GetInt32(9),
                                LocalPath = s.GetString(10),
                                PrimaryInfFile = s.GetString(11),
                                Sha256 = s.GetString(12),
                                TotalSizeBytes = s.GetInt64(13),
                                SignatureState = (SignatureState)s.GetInt32(14),
                                SignerSubject = s.GetString(15),
                                AddedAt = added,
                                SourceName = s.GetString(17) ?? "Локальная библиотека",
                                SourceUrl = s.GetString(18),
                                OfficialSupportUrl = s.GetString(19),
                                IsLocal = true
                            };
                        });

                    return list;
                }
            }
        }

        public void DeleteDriver(long driverId)
        {
            lock (_sync)
            {
                using (var conn = OpenConnection())
                {
                    conn.ExecuteNonQuery(
                        "DELETE FROM Drivers WHERE Id = @Id;",
                        new Dictionary<string, object> { ["@Id"] = driverId });
                }
            }
        }
    }
}
