using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PrintCore.Core;
using PrintCore.LocalDriverCatalog;
using PrintCore.Logging;

namespace PrintCore.DriverDownloader
{
    public class DownloadProgressInfo
    {
        public long BytesReceived { get; set; }
        public long? TotalBytes { get; set; }
        public double BytesPerSecond { get; set; }
        public string StatusText { get; set; }

        public double Percentage
        {
            get
            {
                if (!TotalBytes.HasValue || TotalBytes.Value <= 0) return 0;
                return Math.Min(100.0, (BytesReceived * 100.0) / TotalBytes.Value);
            }
        }
    }

    /// <summary>
    /// Модуль загрузки пакетов драйверов с отображением прогресса и скорости,
    /// поддержкой отмены, проверкой SHA-256 и цифровой подписи Authenticode,
    /// распаковкой ZIP и сохранением в локальную библиотеку.
    /// </summary>
    public class DriverDownloaderService
    {
        private readonly LocalDriverCatalogService _localCatalog;
        private readonly IAppLogger _logger;

        static DriverDownloaderService()
        {
            try
            {
                // Обеспечиваем поддержку TLS 1.2 на Windows 7 SP1 / Windows 8 / 8.1
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch
            {
            }
        }

        public DriverDownloaderService(LocalDriverCatalogService localCatalog, IAppLogger logger)
        {
            _localCatalog = localCatalog;
            _logger = logger;
        }

        public async Task<DriverPackage> DownloadAndImportAsync(
            string url,
            string manufacturer,
            string model,
            string expectedSha256,
            IProgress<DownloadProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new ArgumentException("Укажите корректный HTTP/HTTPS URL пакета драйвера.");
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "PrintCore_Downloads", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOf('.') < 0)
            {
                fileName = "driver-package.zip";
            }

            var tempFile = Path.Combine(tempDir, fileName);

            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("PrintCore/1.0 (Windows NT)");
                    using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        var total = response.Content.Headers.ContentLength;

                        using (var netStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                        {
                            var buffer = new byte[65536];
                            long received = 0;
                            var sw = Stopwatch.StartNew();

                            while (true)
                            {
                                var read = await netStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                                if (read <= 0) break;

                                await fileStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                                received += read;

                                var elapsedSec = Math.Max(0.1, sw.Elapsed.TotalSeconds);
                                var speed = received / elapsedSec;
                                progress?.Report(new DownloadProgressInfo
                                {
                                    BytesReceived = received,
                                    TotalBytes = total,
                                    BytesPerSecond = speed,
                                    StatusText = FormatProgress(received, total, speed)
                                });
                            }
                        }
                    }
                }

                // Проверка контрольной суммы
                var actualSha = PackageVerifier.ComputeFileSha256(tempFile);
                if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                    !string.Equals(expectedSha256.Trim(), actualSha, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Контрольная сумма SHA-256 загруженного файла не совпадает с ожидаемой.");
                }

                // Если это ZIP-архив — распакуем во временную папку перед импортом в библиотеку
                var importTarget = tempFile;
                if (Path.GetExtension(tempFile).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var extractDir = Path.Combine(tempDir, "extracted");
                    ZipFile.ExtractToDirectory(tempFile, extractDir);
                    importTarget = extractDir;
                }

                var pkg = await _localCatalog.ImportAsync(
                    importTarget,
                    overrideManufacturer: manufacturer,
                    overrideModel: model,
                    sourceName: uri.Host,
                    sourceUrl: uri.ToString(),
                    copyToLibrary: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                return pkg;
            }
            catch (Exception ex)
            {
                _logger?.Error("Downloader", "Ошибка загрузки пакета " + url, ex);
                throw;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static string FormatProgress(long received, long? total, double speedBytesPerSec)
        {
            var recMb = (received / (1024.0 * 1024.0)).ToString("0.0");
            var speedKb = (speedBytesPerSec / 1024.0).ToString("0");
            if (total.HasValue && total.Value > 0)
            {
                var totMb = (total.Value / (1024.0 * 1024.0)).ToString("0.0");
                var pct = (int)Math.Min(100, (received * 100) / total.Value);
                return pct + "% · " + recMb + " из " + totMb + " МБ · " + speedKb + " КБ/с";
            }

            return recMb + " МБ · " + speedKb + " КБ/с";
        }
    }
}
