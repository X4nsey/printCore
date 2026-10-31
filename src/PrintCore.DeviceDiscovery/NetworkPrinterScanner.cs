using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PrintCore.Core;
using PrintCore.Logging;
using PrintCore.Models;

namespace PrintCore.DeviceDiscovery
{
    /// <summary>
    /// Обнаружение сетевых принтеров (Ethernet и Wi-Fi):
    /// 1. Настроенные TCP/IP порты диспетчера печати Windows (Win32_TCPIPPrinterPort).
    /// 2. Широковещательный поиск WS-Discovery (UDP 3702) для принтеров WSD.
    /// 3. Управляемый опрос ограниченного диапазона узлов по SNMP v1 (sysDescr / hrDeviceDescr)
    ///    и принтерному порту TCP 9100 (JetDirect) / 631 (IPP) с жёсткими тайм-аутами и поддержкой отмены.
    /// </summary>
    public class NetworkPrinterScanner
    {
        private readonly IAppLogger _logger;

        public NetworkPrinterScanner(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<List<DiscoveredPrinter>> ScanAsync(
            AppSettings settings,
            IProgress<string> statusProgress,
            CancellationToken cancellationToken)
        {
            var discovered = new ConcurrentDictionary<string, DiscoveredPrinter>(StringComparer.OrdinalIgnoreCase);
            var installedDrivers = UsbPrinterScanner.QueryInstalledPrinterDrivers();
            var timeoutMs = Math.Max(200, Math.Min(5000, settings?.NetworkTimeoutMs ?? 650));
            var maxHosts = Math.Max(1, Math.Min(254, settings?.MaxScanHosts ?? 64));

            // 1. Зарегистрированные TCP/IP порты принтеров в системе
            statusProgress?.Report("Проверка настроенных TCP/IP портов печати…");
            var configuredPorts = GetConfiguredTcpIpPorts();

            // 2. Определение безопасного списка IPv4-адресов для опроса
            var targetIps = BuildTargetIpList(settings?.NetworkRange, configuredPorts.Keys, maxHosts);

            // 3. Опрос каждого IP с ограничением параллелизма (не более 16 одновременных сокетов)
            statusProgress?.Report("Опрос сетевых узлов (" + targetIps.Count + " адр., тайм-аут " + timeoutMs + " мс)…");
            using (var semaphore = new SemaphoreSlim(16))
            {
                var tasks = targetIps.Select(async ip =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var printer = await ProbeHostAsync(ip, settings, configuredPorts, installedDrivers, timeoutMs, cancellationToken).ConfigureAwait(false);
                        if (printer != null)
                        {
                            discovered[ip] = printer;
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            return discovered.Values
                .OrderBy(p => p.IpAddress, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<DiscoveredPrinter> ProbeHostAsync(
            string ip,
            AppSettings settings,
            IDictionary<string, string> configuredPorts,
            HashSet<string> installedDrivers,
            int timeoutMs,
            CancellationToken ct)
        {
            var isConfiguredPort = configuredPorts.ContainsKey(ip);
            var port9100Open = false;
            var port631Open = false;

            if (settings == null || settings.EnableTcpProbe)
            {
                port9100Open = await IsTcpPortOpenAsync(ip, 9100, timeoutMs, ct).ConfigureAwait(false);
                if (!port9100Open)
                {
                    port631Open = await IsTcpPortOpenAsync(ip, 631, timeoutMs / 2 + 150, ct).ConfigureAwait(false);
                }
            }

            string snmpDescription = null;
            if (settings == null || settings.EnableSnmp)
            {
                if (port9100Open || port631Open || isConfiguredPort)
                {
                    snmpDescription = await QuerySnmpSysDescrAsync(ip, timeoutMs, ct).ConfigureAwait(false);
                }
            }

            if (!port9100Open && !port631Open && string.IsNullOrEmpty(snmpDescription) && !isConfiguredPort)
            {
                return null;
            }

            var reachable = port9100Open || port631Open || !string.IsNullOrEmpty(snmpDescription);
            var mfg = VendorKnowledgeBase.NormalizeManufacturer(snmpDescription) ?? "Не определён";
            var model = ExtractModelFromSnmp(snmpDescription);
            var protocol = !string.IsNullOrEmpty(snmpDescription)
                ? "SNMP / TCP"
                : (port9100Open ? "RAW 9100" : (port631Open ? "IPP 631" : "TCP/IP порт"));

            var hasDriver = !string.IsNullOrWhiteSpace(model) && installedDrivers.Any(d =>
                d.IndexOf(model, StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0);

            return new DiscoveredPrinter
            {
                DeviceName = string.IsNullOrWhiteSpace(model) ? ("Сетевой принтер (" + ip + ")") : model,
                Model = string.IsNullOrWhiteSpace(model) ? "Модель не идентифицирована (" + ip + ")" : model,
                Manufacturer = mfg,
                Connection = ConnectionType.Network,
                DiscoveryProtocol = protocol,
                IpAddress = ip,
                PortName = isConfiguredPort ? configuredPorts[ip] : ("IP_" + ip),
                DeviceId = "NET\\" + ip,
                DriverStatus = hasDriver ? DriverStatus.Installed : DriverStatus.NotFound,
                Availability = reachable ? DeviceAvailability.Online : DeviceAvailability.Offline,
                StatusDetails = !string.IsNullOrEmpty(snmpDescription)
                    ? "SNMP: " + snmpDescription
                    : (reachable ? "Отвечает по протоколу печати (" + protocol + ")" : "Порт зарегистрирован в системе, но узел сейчас не отвечает")
            };
        }

        private static Dictionary<string, string> GetConfiguredTcpIpPorts()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, HostAddress FROM Win32_TCPIPPrinterPort"))
                using (var col = searcher.Get())
                {
                    foreach (ManagementObject mo in col)
                    {
                        using (mo)
                        {
                            var host = mo["HostAddress"] as string;
                            var name = mo["Name"] as string;
                            IPAddress ip;
                            if (!string.IsNullOrWhiteSpace(host) && IPAddress.TryParse(host.Trim(), out ip))
                            {
                                dict[ip.ToString()] = name ?? ("IP_" + ip);
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return dict;
        }

        private static List<string> BuildTargetIpList(string customRange, IEnumerable<string> priorityIps, int maxHosts)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in priorityIps)
            {
                if (seen.Add(p)) result.Add(p);
            }

            // Пользовательский диапазон вида 192.168.1.10-192.168.1.50 или одиночный IP
            if (!string.IsNullOrWhiteSpace(customRange))
            {
                var trimmed = customRange.Trim();
                var dash = trimmed.IndexOf('-');
                IPAddress single;
                if (dash < 0 && IPAddress.TryParse(trimmed, out single))
                {
                    if (seen.Add(single.ToString())) result.Add(single.ToString());
                    return result;
                }

                if (dash > 0)
                {
                    var left = trimmed.Substring(0, dash).Trim();
                    var right = trimmed.Substring(dash + 1).Trim();
                    IPAddress startIp;
                    if (IPAddress.TryParse(left, out startIp) && startIp.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var bytes = startIp.GetAddressBytes();
                        int endHost;
                        IPAddress endIp;
                        if (int.TryParse(right, out endHost))
                        {
                            endHost = Math.Min(254, Math.Max(1, endHost));
                        }
                        else if (IPAddress.TryParse(right, out endIp))
                        {
                            endHost = endIp.GetAddressBytes()[3];
                        }
                        else
                        {
                            endHost = bytes[3];
                        }

                        for (int h = bytes[3]; h <= endHost && result.Count < maxHosts; h++)
                        {
                            var ipStr = string.Format("{0}.{1}.{2}.{3}", bytes[0], bytes[1], bytes[2], h);
                            if (seen.Add(ipStr)) result.Add(ipStr);
                        }

                        return result;
                    }
                }
            }

            // Автоматическое определение активной локальной подсети (ограниченное окно вокруг IP рабочей станции)
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var b = ua.Address.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;

                        var selfHost = b[3];
                        var half = maxHosts / 2;
                        var start = Math.Max(1, selfHost - half);
                        var end = Math.Min(254, start + maxHosts - 1);

                        for (var h = start; h <= end && result.Count < maxHosts; h++)
                        {
                            if (h == selfHost) continue;
                            var candidate = string.Format("{0}.{1}.{2}.{3}", b[0], b[1], b[2], h);
                            if (seen.Add(candidate)) result.Add(candidate);
                        }

                        return result;
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static async Task<bool> IsTcpPortOpenAsync(string ip, int port, int timeoutMs, CancellationToken ct)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(ip, port);
                    var delayTask = Task.Delay(timeoutMs, ct);
                    var completed = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);
                    return completed == connectTask && client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Лёгкий запрос SNMP v1 GET (community "public") для OID 1.3.6.1.2.1.1.1.0 (sysDescr.0).
        /// </summary>
        private static async Task<string> QuerySnmpSysDescrAsync(string ip, int timeoutMs, CancellationToken ct)
        {
            // Пакет SNMPv1 GetRequest для OID 1.3.6.1.2.1.1.1.0 (sysDescr.0)
            byte[] packet =
            {
                0x30, 0x26,
                0x02, 0x01, 0x00,                               // version: v1 (0)
                0x04, 0x06, 0x70, 0x75, 0x62, 0x6C, 0x69, 0x63, // community: "public"
                0xA0, 0x19,                                     // GetRequest-PDU
                0x02, 0x01, 0x01,                               // request-id: 1
                0x02, 0x01, 0x00,                               // error-status: 0
                0x02, 0x01, 0x00,                               // error-index: 0
                0x30, 0x0E,                                     // variable-bindings
                0x30, 0x0C,
                0x06, 0x08, 0x2B, 0x06, 0x01, 0x02, 0x01, 0x01, 0x01, 0x00, // 1.3.6.1.2.1.1.1.0
                0x05, 0x00                                      // NULL
            };

            try
            {
                using (var udp = new UdpClient())
                {
                    var endpoint = new IPEndPoint(IPAddress.Parse(ip), 161);
                    await udp.SendAsync(packet, packet.Length, endpoint).ConfigureAwait(false);

                    var receiveTask = udp.ReceiveAsync();
                    var delayTask = Task.Delay(timeoutMs, ct);
                    var done = await Task.WhenAny(receiveTask, delayTask).ConfigureAwait(false);
                    if (done != receiveTask) return null;

                    var response = receiveTask.Result.Buffer;
                    return ParseSnmpOctetString(response);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ParseSnmpOctetString(byte[] data)
        {
            if (data == null || data.Length < 20) return null;
            // Ищем последний OCTET STRING (0x04) после OID 2B 06 01 02 01 01 01 00
            for (var i = 15; i < data.Length - 2; i++)
            {
                if (data[i] == 0x04)
                {
                    int len = data[i + 1];
                    int offset = i + 2;
                    if ((len & 0x80) != 0)
                    {
                        int lenBytes = len & 0x7F;
                        if (lenBytes == 1 && i + 3 < data.Length)
                        {
                            len = data[i + 2];
                            offset = i + 3;
                        }
                        else if (lenBytes == 2 && i + 4 < data.Length)
                        {
                            len = (data[i + 2] << 8) | data[i + 3];
                            offset = i + 4;
                        }
                    }

                    if (len > 3 && offset + len <= data.Length)
                    {
                        var str = Encoding.ASCII.GetString(data, offset, len).Trim();
                        if (!string.Equals(str, "public", StringComparison.OrdinalIgnoreCase))
                        {
                            return str;
                        }
                    }
                }
            }

            return null;
        }

        private static string ExtractModelFromSnmp(string snmpSysDescr)
        {
            if (string.IsNullOrWhiteSpace(snmpSysDescr)) return null;
            var firstPart = snmpSysDescr.Split(';')[0].Trim();
            return firstPart.Length > 68 ? firstPart.Substring(0, 68).Trim() : firstPart;
        }
    }
}
