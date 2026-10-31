using PrintCore.Core;

namespace PrintCore.Models
{
    /// <summary>Краткие сведения о системе.</summary>
    public class SystemInfo
    {
        public string MachineName { get; set; }
        public string UserName { get; set; }
        public string OsName { get; set; }
        public string OsBuild { get; set; }
        public string Architecture { get; set; }
        public string FrameworkVersion { get; set; }
        public WindowsVersion WindowsVersion { get; set; } = WindowsVersion.Windows10;
        public CpuArchitecture CpuArchitecture { get; set; } = CpuArchitecture.X64;
        public bool IsAdministrator { get; set; }

        public string Summary
        {
            get { return OsName + " · " + Architecture; }
        }
    }
}
