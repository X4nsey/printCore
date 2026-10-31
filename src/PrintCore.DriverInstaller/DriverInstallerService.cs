using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PrintCore.Core;
using PrintCore.Logging;

namespace PrintCore.DriverInstaller
{
    public class InstallResult
    {
        public bool Success { get; set; }
        public bool RequiresWarning { get; set; }
        public int ExitCode { get; set; }
        public string Summary { get; set; }
        public string CommandOutput { get; set; }
    }

    /// <summary>
    /// Модуль установки драйверов Windows (совместимый с Windows 7 SP1, 8, 8.1, 10, 11).
    /// Использует штатный PnPUtil:
    /// - На Windows 10 (1607+) / Windows 11: pnputil.exe /add-driver "..." /install
    /// - На Windows 7 / 8 / 8.1 (и как резервный вариант): pnputil.exe -i -a "..."
    /// При отсутствии прав администратора запрашивает повышение прав штатным окном UAC (Verb="runas").
    /// Не отключает проверку цифровой подписи Windows.
    /// </summary>
    public class DriverInstallerService
    {
        private readonly IAppLogger _logger;

        public DriverInstallerService(IAppLogger logger)
        {
            _logger = logger;
        }

        public static bool IsCurrentProcessAdmin()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public Task<InstallResult> InstallPackageAsync(DriverPackage pkg, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => ExecuteInstall(pkg, cancellationToken), cancellationToken);
        }

        private InstallResult ExecuteInstall(DriverPackage pkg, CancellationToken ct)
        {
            if (pkg == null || string.IsNullOrWhiteSpace(pkg.LocalPath))
            {
                return new InstallResult { Success = false, Summary = "Путь к локальному пакету драйвера не указан." };
            }

            ct.ThrowIfCancellationRequested();

            // Определяем INF или исполняемый установщик
            var infPath = ResolveInfPath(pkg);
            if (!string.IsNullOrEmpty(infPath) && File.Exists(infPath))
            {
                return InstallInfWithPnpUtil(infPath);
            }

            var installerExe = ResolveExecutablePath(pkg);
            if (!string.IsNullOrEmpty(installerExe) && File.Exists(installerExe))
            {
                return RunVendorInstaller(installerExe);
            }

            return new InstallResult
            {
                Success = false,
                Summary = "В выбранном пакете не найден INF-файл или поддерживаемый установщик (EXE/MSI)."
            };
        }

        private InstallResult InstallInfWithPnpUtil(string infFilePath)
        {
            var pnpUtilPath = ResolvePnpUtilExecutable();
            var useLegacySyntax = IsLegacyWindowsVersion();

            // На Windows 7/8/8.1 синтаксис pnputil: -i -a "<path.inf>"
            // На Windows 10 1607+ и Windows 11: /add-driver "<path.inf>" /install
            var args = useLegacySyntax
                ? "-i -a \"" + infFilePath + "\""
                : "/add-driver \"" + infFilePath + "\" /install";

            _logger?.Info("Installer", "Запуск PnPUtil: " + pnpUtilPath + " " + args);

            var isAdmin = IsCurrentProcessAdmin();
            try
            {
                if (isAdmin)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = pnpUtilPath,
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.GetEncoding(866)
                    };

                    using (var proc = Process.Start(psi))
                    {
                        var stdout = proc.StandardOutput.ReadToEnd();
                        var stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit(120000);

                        var combined = (stdout + Environment.NewLine + stderr).Trim();
                        var code = proc.ExitCode;
                        // Коды PnPUtil: 0 — успех, 259 (STILL_ACTIVE / уже установлен), 3010 — требуется перезагрузка
                        var ok = code == 0 || code == 259 || code == 3010;
                        return new InstallResult
                        {
                            Success = ok,
                            RequiresWarning = code == 3010,
                            ExitCode = code,
                            Summary = ok
                                ? (code == 3010 ? "Драйвер добавлен в хранилище Windows (рекомендуется перезагрузка)." : "Драйвер успешно установлен в хранилище драйверов Windows.")
                                : "PnPUtil вернул код ошибки " + code + ".",
                            CommandOutput = combined
                        };
                    }
                }
                else
                {
                    // Запрос повышения прав через стандартный диалог UAC Windows
                    var psi = new ProcessStartInfo
                    {
                        FileName = pnpUtilPath,
                        Arguments = args,
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using (var proc = Process.Start(psi))
                    {
                        proc.WaitForExit(120000);
                        var code = proc.ExitCode;
                        var ok = code == 0 || code == 259 || code == 3010;
                        return new InstallResult
                        {
                            Success = ok,
                            RequiresWarning = code == 3010,
                            ExitCode = code,
                            Summary = ok
                                ? "Драйвер успешно установлен с повышением прав (UAC)."
                                : "Установка с правами администратора завершилась с кодом " + code + ".",
                            CommandOutput = "Выполнено: pnputil " + args + " (код выхода: " + code + ")"
                        };
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 1223)
            {
                return new InstallResult
                {
                    Success = false,
                    ExitCode = 1223,
                    Summary = "Установка отменена пользователем в окне подтверждения UAC."
                };
            }
            catch (Exception ex)
            {
                _logger?.Error("Installer", "Ошибка запуска PnPUtil", ex);
                return new InstallResult
                {
                    Success = false,
                    Summary = "Не удалось выполнить PnPUtil: " + ex.Message
                };
            }
        }

        private InstallResult RunVendorInstaller(string installerPath)
        {
            try
            {
                var isMsi = Path.GetExtension(installerPath).Equals(".msi", StringComparison.OrdinalIgnoreCase);
                var psi = new ProcessStartInfo
                {
                    FileName = isMsi ? "msiexec.exe" : installerPath,
                    Arguments = isMsi ? "/i \"" + installerPath + "\"" : "",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(300000);
                    var ok = proc.ExitCode == 0 || proc.ExitCode == 3010;
                    return new InstallResult
                    {
                        Success = ok,
                        ExitCode = proc.ExitCode,
                        Summary = ok
                            ? "Установщик пакета драйвера успешно завершил работу."
                            : "Установщик завершился с кодом " + proc.ExitCode + "."
                    };
                }
            }
            catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 1223)
            {
                return new InstallResult
                {
                    Success = false,
                    ExitCode = 1223,
                    Summary = "Запуск установщика отменён пользователем в окне UAC."
                };
            }
            catch (Exception ex)
            {
                _logger?.Error("Installer", "Ошибка запуска установщика производителя", ex);
                return new InstallResult
                {
                    Success = false,
                    Summary = "Ошибка запуска установщика: " + ex.Message
                };
            }
        }

        private static string ResolveInfPath(DriverPackage pkg)
        {
            if (!string.IsNullOrEmpty(pkg.PrimaryInfFile) && File.Exists(pkg.PrimaryInfFile))
            {
                return pkg.PrimaryInfFile;
            }

            if (File.Exists(pkg.LocalPath) && Path.GetExtension(pkg.LocalPath).Equals(".inf", StringComparison.OrdinalIgnoreCase))
            {
                return pkg.LocalPath;
            }

            if (Directory.Exists(pkg.LocalPath))
            {
                return Directory.GetFiles(pkg.LocalPath, "*.inf", SearchOption.AllDirectories).FirstOrDefault();
            }

            return null;
        }

        private static string ResolveExecutablePath(DriverPackage pkg)
        {
            if (File.Exists(pkg.LocalPath))
            {
                var ext = Path.GetExtension(pkg.LocalPath).ToLowerInvariant();
                if (ext == ".exe" || ext == ".msi") return pkg.LocalPath;
            }

            if (Directory.Exists(pkg.LocalPath))
            {
                return Directory.GetFiles(pkg.LocalPath, "*.exe", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(pkg.LocalPath, "*.msi", SearchOption.AllDirectories))
                    .FirstOrDefault();
            }

            return null;
        }

        private static string ResolvePnpUtilExecutable()
        {
            // Для 32-битного процесса на 64-битной Windows pnputil находится в Sysnative
            if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
            {
                var sysnative = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative", "pnputil.exe");
                if (File.Exists(sysnative)) return sysnative;
            }

            return Path.Combine(Environment.SystemDirectory, "pnputil.exe");
        }

        private static bool IsLegacyWindowsVersion()
        {
            var v = Environment.OSVersion.Version;
            // Windows 7 (6.1), Windows 8 (6.2), Windows 8.1 (6.3)
            return v.Major < 6 || (v.Major == 6 && v.Minor <= 3);
        }
    }
}
