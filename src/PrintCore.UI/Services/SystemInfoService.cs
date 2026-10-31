using System;
using Microsoft.Win32;
using PrintCore.Core;
using PrintCore.DriverInstaller;
using PrintCore.Models;

namespace PrintCore.Services
{
    /// <summary>Сбор сведений о версии Windows (7 SP1 / 8 / 8.1 / 10 / 11) и разрядности системы.</summary>
    public class SystemInfoService
    {
        public SystemInfo GetSystemInfo()
        {
            var is64 = Environment.Is64BitOperatingSystem;
            var info = new SystemInfo
            {
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                Architecture = is64 ? "x64" : "x86",
                CpuArchitecture = is64 ? CpuArchitecture.X64 : CpuArchitecture.X86,
                OsName = "Windows",
                OsBuild = Environment.OSVersion.Version.ToString(),
                FrameworkVersion = ".NET Framework 4.8",
                WindowsVersion = DetectWindowsVersion(),
                IsAdministrator = DriverInstallerService.IsCurrentProcessAdmin()
            };

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        var productName = key.GetValue("ProductName") as string;
                        var build = key.GetValue("CurrentBuild") as string;
                        var displayVersion = key.GetValue("DisplayVersion") as string;

                        int buildNumber;
                        if (!string.IsNullOrEmpty(productName))
                        {
                            if (int.TryParse(build, out buildNumber) && buildNumber >= 22000)
                            {
                                productName = productName.Replace("Windows 10", "Windows 11");
                                info.WindowsVersion = WindowsVersion.Windows11;
                            }

                            info.OsName = productName;
                        }

                        if (!string.IsNullOrEmpty(build))
                        {
                            info.OsBuild = string.IsNullOrEmpty(displayVersion)
                                ? "сборка " + build
                                : displayVersion + ", сборка " + build;
                        }
                    }
                }
            }
            catch
            {
            }

            return info;
        }

        private static WindowsVersion DetectWindowsVersion()
        {
            var v = Environment.OSVersion.Version;
            if (v.Major == 6 && v.Minor == 1) return WindowsVersion.Windows7;
            if (v.Major == 6 && v.Minor == 2) return WindowsVersion.Windows8;
            if (v.Major == 6 && v.Minor == 3) return WindowsVersion.Windows81;
            return WindowsVersion.Windows10;
        }
    }
}
