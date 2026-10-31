namespace PrintCore.Models
{
    /// <summary>Принтер, установленный в системе (очередь печати Windows).</summary>
    public class InstalledPrinter
    {
        public string Name { get; set; }
        public string Model { get; set; }
        public string Manufacturer { get; set; }
        public string DriverName { get; set; }
        public string DriverVersion { get; set; }
        public string PortName { get; set; }
        public string InfPath { get; set; }
        public string StatusText { get; set; } = "Готов";
        public StatusTone StatusTone { get; set; } = StatusTone.Success;
        public bool IsDefault { get; set; }
        public bool IsShared { get; set; }
        public bool IsNetworkConnection { get; set; }
        public bool IsOffline { get; set; }

        public string DisplayModel
        {
            get { return string.IsNullOrWhiteSpace(Model) ? DriverName : Model; }
        }

        public string DisplayDriverVersion
        {
            get { return string.IsNullOrWhiteSpace(DriverVersion) ? "Не указана" : DriverVersion; }
        }

        public string KindText
        {
            get { return IsNetworkConnection ? "Сетевое подключение" : "Локальная очередь"; }
        }
    }
}
