using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PrintCore.Core;

namespace PrintCore.LocalDriverCatalog
{
    /// <summary>
    /// Анализатор INF-файлов драйверов принтеров Windows.
    /// Извлекает Class=Printer, DriverVer, Provider/Manufacturer, архитектуру (.NTx86, .NTamd64),
    /// модели принтеров и аппаратные идентификаторы (USB\VID_..., USBPRINT\..., WSDPRINT\...).
    /// </summary>
    public static class InfFileParser
    {
        private static readonly Regex DriverVerRegex = new Regex(
            @"^\s*DriverVer\s*=\s*(?<date>[^,;]+)(?:\s*,\s*(?<ver>[^\s;]+))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HwIdRegex = new Regex(
            @"(?<id>(?:USB|USBPRINT|WSDPRINT|DOT4PRT|LPTENUM|MF)\\[A-Z0-9_&.\-]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static DriverPackage InspectPackagePath(string path, out List<Tuple<string, long, string>> fileRecords)
        {
            fileRecords = new List<Tuple<string, long, string>>();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Путь к пакету не указан.");
            }

            var pkg = new DriverPackage
            {
                AddedAt = DateTime.Now,
                IsLocal = true,
                SourceName = "Локальный импорт",
                SupportedOsList = new List<WindowsVersion>
                {
                    WindowsVersion.Windows7,
                    WindowsVersion.Windows8,
                    WindowsVersion.Windows81,
                    WindowsVersion.Windows10,
                    WindowsVersion.Windows11
                }
            };

            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                var ext = fi.Extension.ToLowerInvariant();
                pkg.LocalPath = fi.FullName;
                pkg.TotalSizeBytes = fi.Length;
                pkg.Sha256 = PackageVerifier.ComputeFileSha256(fi.FullName);
                fileRecords.Add(Tuple.Create(fi.Name, fi.Length, pkg.Sha256));

                if (ext == ".inf")
                {
                    pkg.PackageType = PackageType.InfDirectory;
                    pkg.PrimaryInfFile = fi.FullName;
                    pkg.LocalPath = fi.DirectoryName ?? fi.FullName;
                    ParseInfFile(fi.FullName, pkg);
                    VerifyFolderSignature(fi.DirectoryName, pkg);
                }
                else
                {
                    pkg.PackageType = DetectFilePackageType(ext);
                    string signer;
                    pkg.SignatureState = PackageVerifier.VerifyAuthenticode(fi.FullName, out signer);
                    pkg.SignerSubject = signer;
                    InferMetadataFromFileName(fi.Name, pkg);
                }

                return pkg;
            }

            if (Directory.Exists(path))
            {
                var dir = new DirectoryInfo(path);
                pkg.LocalPath = dir.FullName;
                pkg.PackageType = PackageType.InfDirectory;

                var allFiles = dir.GetFiles("*", SearchOption.AllDirectories);
                long total = 0;
                foreach (var f in allFiles)
                {
                    total += f.Length;
                    if (fileRecords.Count < 60 &&
                        (f.Extension.Equals(".inf", StringComparison.OrdinalIgnoreCase) ||
                         f.Extension.Equals(".cat", StringComparison.OrdinalIgnoreCase) ||
                         f.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                         f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                    {
                        var rel = f.FullName.Substring(dir.FullName.Length).TrimStart('\\', '/');
                        fileRecords.Add(Tuple.Create(rel, f.Length, PackageVerifier.ComputeFileSha256(f.FullName)));
                    }
                }

                pkg.TotalSizeBytes = total;

                var infFiles = allFiles.Where(f => f.Extension.Equals(".inf", StringComparison.OrdinalIgnoreCase)).ToList();
                if (infFiles.Count > 0)
                {
                    pkg.PrimaryInfFile = infFiles[0].FullName;
                    pkg.Sha256 = PackageVerifier.ComputeFileSha256(infFiles[0].FullName);
                    foreach (var inf in infFiles)
                    {
                        ParseInfFile(inf.FullName, pkg);
                    }
                }
                else
                {
                    var exe = allFiles.FirstOrDefault(f => f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".msi", StringComparison.OrdinalIgnoreCase));
                    if (exe != null)
                    {
                        pkg.PackageType = exe.Extension.Equals(".msi", StringComparison.OrdinalIgnoreCase)
                            ? PackageType.MsiInstaller
                            : PackageType.ExecutableInstaller;
                        pkg.Sha256 = PackageVerifier.ComputeFileSha256(exe.FullName);
                        InferMetadataFromFileName(exe.Name, pkg);
                    }
                    else
                    {
                        InferMetadataFromFileName(dir.Name, pkg);
                    }
                }

                VerifyFolderSignature(dir.FullName, pkg);
                return pkg;
            }

            throw new FileNotFoundException("Указанный путь не найден: " + path);
        }

        public static void ParseInfFile(string infFilePath, DriverPackage target)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(infFilePath, DetectInfEncoding(infFilePath));
            }
            catch
            {
                return;
            }

            var stringsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var section = "";
            var hasX86 = false;
            var hasX64 = false;

            // Первый проход: секция [Strings]
            foreach (var raw in lines)
            {
                var line = StripComment(raw).Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                if (section.StartsWith("Strings", StringComparison.OrdinalIgnoreCase))
                {
                    var eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        var k = line.Substring(0, eq).Trim();
                        var v = line.Substring(eq + 1).Trim().Trim('"');
                        if (!stringsDict.ContainsKey(k)) stringsDict[k] = v;
                    }
                }
            }

            section = "";
            foreach (var raw in lines)
            {
                var line = StripComment(raw).Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    if (section.IndexOf("NTamd64", StringComparison.OrdinalIgnoreCase) >= 0) hasX64 = true;
                    if (section.IndexOf("NTx86", StringComparison.OrdinalIgnoreCase) >= 0) hasX86 = true;
                    continue;
                }

                if (section.Equals("Version", StringComparison.OrdinalIgnoreCase))
                {
                    var m = DriverVerRegex.Match(line);
                    if (m.Success)
                    {
                        DateTime dt;
                        if (DateTime.TryParse(m.Groups["date"].Value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                        {
                            target.DriverDate = dt;
                        }

                        var ver = m.Groups["ver"].Value.Trim();
                        if (!string.IsNullOrEmpty(ver))
                        {
                            target.DriverVersion = ver;
                        }
                    }
                    else if (line.StartsWith("Provider", StringComparison.OrdinalIgnoreCase))
                    {
                        var eq = line.IndexOf('=');
                        if (eq > 0 && string.IsNullOrEmpty(target.Manufacturer))
                        {
                            var provider = ResolveToken(line.Substring(eq + 1).Trim(), stringsDict);
                            target.Manufacturer = VendorKnowledgeBase.NormalizeManufacturer(provider) ?? provider;
                        }
                    }
                }

                var hwMatch = HwIdRegex.Match(line);
                if (hwMatch.Success)
                {
                    var hwId = hwMatch.Groups["id"].Value.ToUpperInvariant();
                    if (!target.HardwareIds.Contains(hwId, StringComparer.OrdinalIgnoreCase))
                    {
                        target.HardwareIds.Add(hwId);
                    }

                    if (string.IsNullOrEmpty(target.Manufacturer))
                    {
                        target.Manufacturer = VendorKnowledgeBase.NormalizeManufacturer(null, hwId);
                    }

                    var eqIdx = line.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        var leftName = ResolveToken(line.Substring(0, eqIdx).Trim().Trim('"'), stringsDict);
                        if (!string.IsNullOrWhiteSpace(leftName) && !target.SupportedModels.Contains(leftName, StringComparer.OrdinalIgnoreCase))
                        {
                            target.SupportedModels.Add(leftName);
                            if (string.IsNullOrEmpty(target.Model))
                            {
                                target.Model = leftName;
                            }
                        }
                    }
                }
            }

            if (hasX86 && hasX64) target.Architecture = CpuArchitecture.Both;
            else if (hasX64) target.Architecture = CpuArchitecture.X64;
            else if (hasX86) target.Architecture = CpuArchitecture.X86;

            if (string.IsNullOrEmpty(target.Model))
            {
                target.Model = Path.GetFileNameWithoutExtension(infFilePath);
            }

            if (string.IsNullOrEmpty(target.Title))
            {
                target.Title = DriverPackage.BuildCleanTitle(target.Manufacturer, target.Model);
            }
        }

        private static void VerifyFolderSignature(string directory, DriverPackage pkg)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            try
            {
                var catOrExe = Directory.GetFiles(directory, "*.cat", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(directory, "*.exe", SearchOption.AllDirectories))
                    .FirstOrDefault();
                if (catOrExe != null)
                {
                    string signer;
                    pkg.SignatureState = PackageVerifier.VerifyAuthenticode(catOrExe, out signer);
                    pkg.SignerSubject = signer;
                }
                else
                {
                    pkg.SignatureState = SignatureState.Unsigned;
                }
            }
            catch
            {
                pkg.SignatureState = SignatureState.NotChecked;
            }
        }

        private static PackageType DetectFilePackageType(string ext)
        {
            switch (ext)
            {
                case ".zip": return PackageType.ZipArchive;
                case ".cab": return PackageType.CabArchive;
                case ".msi": return PackageType.MsiInstaller;
                case ".exe": return PackageType.ExecutableInstaller;
                default: return PackageType.Unknown;
            }
        }

        private static void InferMetadataFromFileName(string name, DriverPackage pkg)
        {
            var clean = Path.GetFileNameWithoutExtension(name).Replace('_', ' ').Replace('-', ' ');
            pkg.Manufacturer = VendorKnowledgeBase.NormalizeManufacturer(clean) ?? "Не указан";
            pkg.Model = clean;
            pkg.Title = clean;
            var upper = clean.ToUpperInvariant();
            if (upper.Contains("X64") || upper.Contains("64BIT") || upper.Contains("AMD64"))
            {
                pkg.Architecture = upper.Contains("X86") || upper.Contains("32BIT") ? CpuArchitecture.Both : CpuArchitecture.X64;
            }
            else if (upper.Contains("X86") || upper.Contains("32BIT"))
            {
                pkg.Architecture = CpuArchitecture.X86;
            }
        }

        private static Encoding DetectInfEncoding(string filePath)
        {
            var bom = new byte[2];
            using (var fs = File.OpenRead(filePath))
            {
                if (fs.Read(bom, 0, 2) == 2 && bom[0] == 0xFF && bom[1] == 0xFE)
                {
                    return Encoding.Unicode;
                }
            }

            return Encoding.UTF8;
        }

        private static string StripComment(string line)
        {
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') inQuotes = !inQuotes;
                else if (line[i] == ';' && !inQuotes) return line.Substring(0, i);
            }

            return line;
        }

        private static string ResolveToken(string raw, IDictionary<string, string> strings)
        {
            var trimmed = raw.Trim().Trim('"');
            if (trimmed.StartsWith("%", StringComparison.Ordinal) && trimmed.EndsWith("%", StringComparison.Ordinal) && trimmed.Length > 2)
            {
                var key = trimmed.Substring(1, trimmed.Length - 2);
                string resolved;
                if (strings.TryGetValue(key, out resolved))
                {
                    return resolved;
                }
            }

            return trimmed;
        }
    }
}
