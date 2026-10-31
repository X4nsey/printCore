using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using PrintCore.Core;
using PrintCore.Logging;
using PrintCore.Models;

namespace PrintCore.DeviceDiscovery
{
    /// <summary>
    /// Обнаружение принтеров, подключённых по USB и через Plug and Play (WMI Win32_PnPEntity и Win32_Printer),
    /// совместимое с Windows 7 SP1 / 8 / 8.1 / 10 / 11.
    /// </summary>
    public class UsbPrinterScanner
    {
        private readonly IAppLogger _logger;

        public UsbPrinterScanner(IAppLogger logger)
        {
            _logger = logger;
        }

        public List<DiscoveredPrinter> Scan(CancellationToken cancellationToken)
        {
            var results = new List<DiscoveredPrinter>();
            var installedDrivers = QueryInstalledPrinterDrivers();

            try
            {
                // 1. Опрос Plug and Play устройств печати (USBPRINT\*, WSDPRINT\*, ClassGuid принтеров)
                const string wql = "SELECT Name, Caption, Description, DeviceID, PNPDeviceID, Manufacturer, HardwareID, ConfigManagerErrorCode, Status, PNPClass FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USBPRINT%' OR PNPDeviceID LIKE 'DOT4PRT%' OR PNPClass = 'Printer'";
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2", wql))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection)
                    {
                        using (obj)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var deviceId = SafeGetString(obj, "PNPDeviceID") ?? SafeGetString(obj, "DeviceID");
                            if (string.IsNullOrWhiteSpace(deviceId)) continue;

                            // Пропускаем виртуальные программные очереди в PnP (Print to PDF, XPS, Fax)
                            if (IsVirtualSoftwareQueue(deviceId, SafeGetString(obj, "Name"))) continue;

                            var hwIds = SafeGetStringArray(obj, "HardwareID");
                            var primaryHwId = hwIds.FirstOrDefault() ?? deviceId;
                            var rawName = SafeGetString(obj, "Name") ?? SafeGetString(obj, "Caption") ?? "Принтер";
                            var rawMfg = SafeGetString(obj, "Manufacturer");
                            var errCode = SafeGetUInt32(obj, "ConfigManagerErrorCode");

                            var mfg = VendorKnowledgeBase.NormalizeManufacturer(rawMfg, primaryHwId)
                                      ?? VendorKnowledgeBase.NormalizeManufacturer(rawName, primaryHwId)
                                      ?? "Не определён";

                            var model = CleanModelName(rawName, primaryHwId);
                            var hasDriver = errCode == 0 && !IsMissingDriverErrorCode(errCode);
                            var matchedQueueDriver = FindMatchingQueueDriver(model, installedDrivers);
                            if (!string.IsNullOrEmpty(matchedQueueDriver))
                            {
                                hasDriver = true;
                            }

                            var connType = ConnectionType.Usb;
                            var protocol = "USB PnP";
                            if (deviceId.StartsWith("WSDPRINT", StringComparison.OrdinalIgnoreCase) ||
                                deviceId.StartsWith("SWD\\DAFWSDPROVIDER", StringComparison.OrdinalIgnoreCase))
                            {
                                connType = ConnectionType.Wsd;
                                protocol = "WSD";
                            }
                            else if (!deviceId.StartsWith("USB", StringComparison.OrdinalIgnoreCase) &&
                                     !deviceId.StartsWith("DOT4", StringComparison.OrdinalIgnoreCase))
                            {
                                connType = ConnectionType.Other;
                                protocol = "PnP";
                            }

                            results.Add(new DiscoveredPrinter
                            {
                                DeviceName = rawName,
                                Model = model,
                                Manufacturer = mfg,
                                Connection = connType,
                                DiscoveryProtocol = protocol,
                                DeviceId = deviceId,
                                HardwareId = primaryHwId,
                                InstalledDriverName = matchedQueueDriver,
                                DriverStatus = hasDriver ? DriverStatus.Installed : DriverStatus.NotFound,
                                Availability = errCode == 0 ? DeviceAvailability.Online : DeviceAvailability.Attention,
                                StatusDetails = errCode == 0
                                    ? "Устройство подключено и работает нормально"
                                    : "Код состояния диспетчера устройств: " + errCode + (errCode == 28 ? " (драйвер не установлен)" : "")
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Warn("UsbScanner", "Ошибка опроса Win32_PnPEntity: " + ex.Message);
            }

            // 2. Дополнительно проверяем очереди Win32_Printer с портами USB001..USB999 или WSD-*
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, DriverName, PortName, WorkOffline, PrinterStatus, PNPDeviceID FROM Win32_Printer"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection)
                    {
                        using (obj)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var name = SafeGetString(obj, "Name");
                            var port = SafeGetString(obj, "PortName") ?? "";
                            var driverName = SafeGetString(obj, "DriverName");
                            var pnpId = SafeGetString(obj, "PNPDeviceID");
                            var workOffline = SafeGetBool(obj, "WorkOffline");

                            if (IsVirtualSoftwareQueue(pnpId ?? port, name)) continue;

                            var isUsbPort = port.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ||
                                            port.StartsWith("DOT4", StringComparison.OrdinalIgnoreCase);
                            var isWsdPort = port.StartsWith("WSD", StringComparison.OrdinalIgnoreCase);
                            var isIpPort = port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase) ||
                                           LooksLikeIpAddress(port);

                            if (!isUsbPort && !isWsdPort && !isIpPort) continue;

                            // Если уже добавлен по PnPDeviceID или модели — обогатим порт и статус драйвера
                            var existing = results.FirstOrDefault(r =>
                                (!string.IsNullOrEmpty(pnpId) && string.Equals(r.DeviceId, pnpId, StringComparison.OrdinalIgnoreCase)) ||
                                string.Equals(r.Model, driverName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(r.DeviceName, name, StringComparison.OrdinalIgnoreCase));

                            if (existing != null)
                            {
                                existing.PortName = port;
                                if (!string.IsNullOrEmpty(driverName))
                                {
                                    existing.InstalledDriverName = driverName;
                                    existing.DriverStatus = DriverStatus.Installed;
                                }

                                if (workOffline)
                                {
                                    existing.Availability = DeviceAvailability.Offline;
                                }

                                continue;
                            }

                            var mfg = VendorKnowledgeBase.NormalizeManufacturer(driverName ?? name, pnpId) ?? "Не определён";
                            results.Add(new DiscoveredPrinter
                            {
                                DeviceName = name,
                                Model = !string.IsNullOrWhiteSpace(driverName) ? driverName : name,
                                Manufacturer = mfg,
                                Connection = isUsbPort ? ConnectionType.Usb : (isWsdPort ? ConnectionType.Wsd : ConnectionType.Network),
                                DiscoveryProtocol = isUsbPort ? "USB Spooler" : (isWsdPort ? "WSD Spooler" : "TCP/IP Spooler"),
                                IpAddress = isIpPort ? ExtractIpFromPort(port) : null,
                                PortName = port,
                                DeviceId = pnpId ?? ("SPOOLER\\" + name),
                                HardwareId = pnpId,
                                InstalledDriverName = driverName,
                                DriverStatus = string.IsNullOrWhiteSpace(driverName) ? DriverStatus.Unknown : DriverStatus.Installed,
                                Availability = workOffline ? DeviceAvailability.Offline : DeviceAvailability.Online,
                                StatusDetails = workOffline ? "Очередь печати в автономном режиме (Порт " + port + ")" : "Очередь печати активна (Порт " + port + ")"
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Warn("UsbScanner", "Ошибка опроса Win32_Printer: " + ex.Message);
            }

            return results;
        }

        public static HashSet<string> QueryInstalledPrinterDrivers()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name FROM Win32_PrinterDriver"))
                using (var col = searcher.Get())
                {
                    foreach (ManagementObject mo in col)
                    {
                        using (mo)
                        {
                            var raw = SafeGetString(mo, "Name");
                            if (!string.IsNullOrWhiteSpace(raw))
                            {
                                // Формат в Win32_PrinterDriver: "HP Universal Printing PCL 6,3,Windows x64"
                                var comma = raw.IndexOf(',');
                                set.Add(comma > 0 ? raw.Substring(0, comma).Trim() : raw.Trim());
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return set;
        }

        private static string FindMatchingQueueDriver(string model, HashSet<string> installedDrivers)
        {
            if (string.IsNullOrWhiteSpace(model) || installedDrivers == null) return null;
            foreach (var drv in installedDrivers)
            {
                if (drv.IndexOf(model, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    model.IndexOf(drv, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return drv;
                }
            }

            return null;
        }

        private static string CleanModelName(string rawName, string hwId)
        {
            if (!string.IsNullOrWhiteSpace(rawName) &&
                !rawName.Equals("Поддержка USB принтеров", StringComparison.OrdinalIgnoreCase) &&
                !rawName.Equals("USB Printing Support", StringComparison.OrdinalIgnoreCase))
            {
                return rawName.Trim();
            }

            // Попытка извлечь имя модели из USBPRINT\VendorModel\...
            if (!string.IsNullOrWhiteSpace(hwId) && hwId.StartsWith("USBPRINT\\", StringComparison.OrdinalIgnoreCase))
            {
                var parts = hwId.Split('\\');
                if (parts.Length >= 2)
                {
                    return parts[1].Replace('_', ' ').Trim();
                }
            }

            return rawName ?? "USB-принтер";
        }

        private static bool IsMissingDriverErrorCode(uint code)
        {
            // Код 1 (не настроено) или 28 (драйверы для этого устройства не установлены)
            return code == 1 || code == 28;
        }

        private static bool IsVirtualSoftwareQueue(string idOrPort, string name)
        {
            var combined = ((idOrPort ?? "") + " " + (name ?? "")).ToUpperInvariant();
            return combined.Contains("PRINT TO PDF")
                || combined.Contains("XPS DOCUMENT WRITER")
                || combined.Contains("ONENOTE")
                || combined.Contains("FAX")
                || combined.Contains("ROOT\\RDPDR")
                || combined.Contains("PORTPROMPT:");
        }

        private static bool LooksLikeIpAddress(string text)
        {
            System.Net.IPAddress ip;
            return System.Net.IPAddress.TryParse(text, out ip);
        }

        private static string ExtractIpFromPort(string port)
        {
            if (string.IsNullOrWhiteSpace(port)) return null;
            if (port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase))
            {
                return port.Substring(3);
            }

            return LooksLikeIpAddress(port) ? port : null;
        }

        private static string SafeGetString(ManagementBaseObject obj, string prop)
        {
            try { return obj[prop] as string; } catch { return null; }
        }

        private static string[] SafeGetStringArray(ManagementBaseObject obj, string prop)
        {
            try { return (obj[prop] as string[]) ?? new string[0]; } catch { return new string[0]; }
        }

        private static uint SafeGetUInt32(ManagementBaseObject obj, string prop)
        {
            try { return obj[prop] != null ? Convert.ToUInt32(obj[prop]) : 0u; } catch { return 0u; }
        }

        private static bool SafeGetBool(ManagementBaseObject obj, string prop)
        {
            try { return obj[prop] != null && Convert.ToBoolean(obj[prop]); } catch { return false; }
        }
    }
}
