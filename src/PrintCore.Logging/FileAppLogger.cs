using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PrintCore.Logging
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>Потокобезопасный файловый журнал приложения.</summary>
    public interface IAppLogger
    {
        string LogFilePath { get; }
        void Info(string module, string message);
        void Warn(string module, string message);
        void Error(string module, string message, Exception ex = null);
    }

    public class FileAppLogger : IAppLogger
    {
        private readonly object _sync = new object();

        public FileAppLogger(string dataDirectory = null)
        {
            var baseDir = string.IsNullOrWhiteSpace(dataDirectory)
                ? PrintCore.Models.AppSettings.ResolveBaseDataDirectory()
                : dataDirectory;

            var logsDir = Path.Combine(baseDir, "Logs");
            try { Directory.CreateDirectory(logsDir); } catch { }
            LogFilePath = Path.Combine(logsDir, "printcore-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        }

        public string LogFilePath { get; }

        public void Info(string module, string message)
        {
            Write(LogLevel.Info, module, message, null);
        }

        public void Warn(string module, string message)
        {
            Write(LogLevel.Warning, module, message, null);
        }

        public void Error(string module, string message, Exception ex = null)
        {
            Write(LogLevel.Error, module, message, ex);
        }

        private void Write(LogLevel level, string module, string message, Exception ex)
        {
            var line = string.Format(
                "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] {3}{4}",
                DateTime.Now,
                level.ToString().ToUpperInvariant(),
                module ?? "App",
                message,
                ex == null ? "" : " | " + ex.GetType().Name + ": " + ex.Message);

            Debug.WriteLine(line);
            lock (_sync)
            {
                try
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Ошибка записи лога не должна нарушать работу приложения.
                }
            }
        }
    }
}
