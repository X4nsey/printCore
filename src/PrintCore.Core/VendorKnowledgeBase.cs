using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace PrintCore.Core
{
    /// <summary>
    /// Известные производители принтеров, их USB Vendor ID и официальные страницы поддержки.
    /// Никаких сторонних агрегаторов — только домены производителей.
    /// </summary>
    public static class VendorKnowledgeBase
    {
        public static readonly IReadOnlyList<ManufacturerInfo> KnownManufacturers = new[]
        {
            new ManufacturerInfo
            {
                Name = "HP",
                UsbVendorId = "03F0",
                OfficialSiteUrl = "https://support.hp.com/drivers/printers",
                SupportSearchUrlTemplate = "https://support.hp.com/drivers/printers"
            },
            new ManufacturerInfo
            {
                Name = "Canon",
                UsbVendorId = "04A9",
                OfficialSiteUrl = "https://www.canon-europe.com/support/",
                SupportSearchUrlTemplate = "https://www.canon-europe.com/support/"
            },
            new ManufacturerInfo
            {
                Name = "Epson",
                UsbVendorId = "04B8",
                OfficialSiteUrl = "https://epson.com/Support/Printers/sh/s1",
                SupportSearchUrlTemplate = "https://epson.com/Support/Printers/sh/s1"
            },
            new ManufacturerInfo
            {
                Name = "Brother",
                UsbVendorId = "04F9",
                OfficialSiteUrl = "https://support.brother.com/",
                SupportSearchUrlTemplate = "https://support.brother.com/"
            },
            new ManufacturerInfo
            {
                Name = "Kyocera",
                UsbVendorId = "0482",
                OfficialSiteUrl = "https://www.kyoceradocumentsolutions.com/support_product/",
                SupportSearchUrlTemplate = "https://www.kyoceradocumentsolutions.com/support_product/"
            },
            new ManufacturerInfo
            {
                Name = "Xerox",
                UsbVendorId = "0924",
                OfficialSiteUrl = "https://www.support.xerox.com/",
                SupportSearchUrlTemplate = "https://www.support.xerox.com/"
            },
            new ManufacturerInfo
            {
                Name = "Samsung",
                UsbVendorId = "04E8",
                OfficialSiteUrl = "https://support.hp.com/drivers/printers",
                SupportSearchUrlTemplate = "https://support.hp.com/drivers/printers"
            },
            new ManufacturerInfo
            {
                Name = "Ricoh",
                UsbVendorId = "05CA",
                OfficialSiteUrl = "https://support.ricoh.com/bb/html/dr_ut_e/rc3/model/p_i/p_i.htm",
                SupportSearchUrlTemplate = "https://support.ricoh.com/bb/html/dr_ut_e/rc3/model/p_i/p_i.htm"
            },
            new ManufacturerInfo
            {
                Name = "Pantum",
                UsbVendorId = "232B",
                OfficialSiteUrl = "https://global.pantum.com/support/download/driver/",
                SupportSearchUrlTemplate = "https://global.pantum.com/support/download/driver/"
            },
            new ManufacturerInfo
            {
                Name = "Lexmark",
                UsbVendorId = "043D",
                OfficialSiteUrl = "https://support.lexmark.com/",
                SupportSearchUrlTemplate = "https://support.lexmark.com/"
            },
            new ManufacturerInfo
            {
                Name = "OKI",
                UsbVendorId = "06BC",
                OfficialSiteUrl = "https://www.oki.com/eu/printing/support/drivers-and-utilities/",
                SupportSearchUrlTemplate = "https://www.oki.com/eu/printing/support/drivers-and-utilities/"
            },
            new ManufacturerInfo
            {
                Name = "Konica Minolta",
                UsbVendorId = "132B",
                OfficialSiteUrl = "https://www.konicaminolta.eu/eu-en/support/download-centre",
                SupportSearchUrlTemplate = "https://www.konicaminolta.eu/eu-en/support/download-centre"
            }
        };

        public static string NormalizeManufacturer(string rawText, string hardwareId = null)
        {
            if (!string.IsNullOrEmpty(hardwareId))
            {
                var upperHw = hardwareId.ToUpperInvariant();
                foreach (var vendor in KnownManufacturers)
                {
                    if (!string.IsNullOrEmpty(vendor.UsbVendorId) &&
                        upperHw.Contains("VID_" + vendor.UsbVendorId))
                    {
                        return vendor.Name;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(rawText))
            {
                return null;
            }

            var text = rawText.Trim();
            var upper = text.ToUpperInvariant();

            if (upper.Contains("HEWLETT") || upper.StartsWith("HP ") || upper == "HP") return "HP";
            if (upper.Contains("CANON")) return "Canon";
            if (upper.Contains("EPSON") || upper.Contains("SEIKO")) return "Epson";
            if (upper.Contains("BROTHER")) return "Brother";
            if (upper.Contains("KYOCERA")) return "Kyocera";
            if (upper.Contains("XEROX")) return "Xerox";
            if (upper.Contains("SAMSUNG")) return "Samsung";
            if (upper.Contains("RICOH") || upper.Contains("LANIER") || upper.Contains("SAVIN")) return "Ricoh";
            if (upper.Contains("PANTUM")) return "Pantum";
            if (upper.Contains("LEXMARK")) return "Lexmark";
            if (upper.Contains("OKI")) return "OKI";
            if (upper.Contains("KONICA") || upper.Contains("MINOLTA")) return "Konica Minolta";

            if (upper.Contains("STANDARD") || upper.Contains("GENERIC") || upper.Contains("СТАНДАРТН"))
            {
                return null;
            }

            return text;
        }

        public static ManufacturerInfo FindByName(string name)
        {
            var normalized = NormalizeManufacturer(name);
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            foreach (var item in KnownManufacturers)
            {
                if (string.Equals(item.Name, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }
    }

    /// <summary>Проверка контрольных сумм SHA-256 и сертификатов Authenticode.</summary>
    public static class PackageVerifier
    {
        public static string ComputeFileSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }

        public static SignatureState VerifyAuthenticode(string filePath, out string signerSubject)
        {
            signerSubject = null;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return SignatureState.NotChecked;
            }

            try
            {
                var cert = X509Certificate.CreateFromSignedFile(filePath);
                var cert2 = new X509Certificate2(cert);
                signerSubject = cert2.GetNameInfo(X509NameType.SimpleName, false);

                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
                    var valid = chain.Build(cert2);
                    return valid ? SignatureState.Valid : SignatureState.Invalid;
                }
            }
            catch (CryptographicException)
            {
                return SignatureState.Unsigned;
            }
            catch
            {
                return SignatureState.NotChecked;
            }
        }
    }
}
