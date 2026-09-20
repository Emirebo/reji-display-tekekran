using System;
using System.IO;

namespace RejiDisplay.Helpers
{
    public static class Logger
    {
        private static readonly object _lock = new();
        public static string LogFilePath { get; }

        static Logger()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logDir = Path.Combine(appData, "RejiDisplay-TekEkran");
            Directory.CreateDirectory(logDir);
            LogFilePath = Path.Combine(logDir, "diagnostics.log");
        }

        public static void Log(string message)
        {
            lock (_lock)
            {
                try
                {
                    string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFilePath, entry);
                }
                catch
                {
                    // Ignore logging failures
                }
            }
        }

        public static void LogError(string context, Exception ex)
        {
            Log($"[ERROR] {context}: {ex.Message}{Environment.NewLine}StackTrace: {ex.StackTrace}");
        }
    }
}
