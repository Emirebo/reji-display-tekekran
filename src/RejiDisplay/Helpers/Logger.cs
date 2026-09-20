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

        public static void LogStartup(string executablePath, string version, string? gitCommit, List<string> displays, string? presentationDisplay, List<string> gpus)
        {
            Log($"==================================================");
            Log($"[STARTUP] RejiDisplay v0.3 Started");
            Log($"[STARTUP] Executable: {executablePath}");
            Log($"[STARTUP] Version: {version}");
            Log($"[STARTUP] Git Commit: {gitCommit ?? "N/A"}");
            Log($"[STARTUP] Log File Path: {LogFilePath}");
            Log($"[STARTUP] Physical Displays ({displays.Count}): {string.Join(" | ", displays)}");
            Log($"[STARTUP] Selected Presentation Source: {presentationDisplay ?? "NONE"}");
            Log($"[STARTUP] GPU Adapters ({gpus.Count}): {string.Join(" | ", gpus)}");
            Log($"==================================================");
        }

        public static void LogError(string context, Exception ex)
        {
            Log($"[ERROR] {context}: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}StackTrace: {ex.StackTrace}");
        }
    }
}
