using PrintCore.Core;

namespace PrintCore.Models
{
    /// <summary>Принтер, найденный модулем обнаружения.</summary>
    public class DiscoveredPrinter
    {
        public string DeviceName { get; set; }
        public string Model { get; set; }
        public string Manufacturer { get; set; }
        public ConnectionType Connection { get; set; }
        public string IpAddress { get; set; }
        public string HardwareId { get; set; }
        public string DeviceId { get; set; }
        public string PortName { get; set; }
        public string DiscoveryProtocol { get; set; }
        public string InstalledDriverName { get; set; }
        public DriverStatus DriverStatus { get; set; }
        public DeviceAvailability Availability { get; set; } = DeviceAvailability.Online;
        public string StatusDetails { get; set; }

        public string DisplayModel
        {
            get { return string.IsNullOrWhiteSpace(Model) ? (DeviceName ?? "Неопознанное устройство") : Model; }
        }

        public string DisplayManufacturer
        {
            get { return string.IsNullOrWhiteSpace(Manufacturer) ? "Не определён" : Manufacturer; }
        }

        public string ConnectionText
        {
            get
            {
                switch (Connection)
                {
                    case ConnectionType.Usb: return "USB";
                    case ConnectionType.Network:
                        return string.IsNullOrEmpty(DiscoveryProtocol) ? "Сеть (TCP/IP)" : "Сеть (" + DiscoveryProtocol + ")";
                    case ConnectionType.Wsd: return "Сеть (WSD)";
                    case ConnectionType.Other: return "Локальный порт";
                    default: return "Неизвестно";
                }
            }
        }

        public string IpAddressText
        {
            get { return string.IsNullOrWhiteSpace(IpAddress) ? "—" : IpAddress; }
        }

        public string AvailabilityText
        {
            get
            {
                switch (Availability)
                {
                    case DeviceAvailability.Online: return "Доступен";
                    case DeviceAvailability.Offline: return "Недоступен";
                    case DeviceAvailability.Attention: return "Требует внимания";
                    default: return "Неизвестно";
                }
            }
        }

        public StatusTone AvailabilityTone
        {
            get
            {
                switch (Availability)
                {
                    case DeviceAvailability.Online: return StatusTone.Success;
                    case DeviceAvailability.Offline: return StatusTone.Neutral;
                    case DeviceAvailability.Attention: return StatusTone.Warning;
                    default: return StatusTone.Neutral;
                }
            }
        }
    }
}
