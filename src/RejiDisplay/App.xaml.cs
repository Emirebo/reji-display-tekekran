using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RejiDisplay
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException(e.Exception);
            MessageBox.Show($"Uygulamada bir hata oluştu:\n\n{e.Exception.Message}\n\nDetaylar log dosyasına yazıldı.",
                            "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogException(ex);
            }
        }

        public static void LogException(Exception ex)
        {
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RejiDisplay", "Logs");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "crash.log");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Unhandled Exception:");

                Exception? current = ex;
                int depth = 0;
                while (current != null)
                {
                    sb.AppendLine($"=== Exception Level {depth}: {current.GetType().FullName} ===");
                    sb.AppendLine($"Message: {current.Message}");
                    sb.AppendLine($"Source: {current.Source}");
                    sb.AppendLine($"StackTrace:\n{current.StackTrace}");
                    current = current.InnerException;
                    depth++;
                }
                sb.AppendLine("----------------------------------------\n");

                File.AppendAllText(logFile, sb.ToString());
            }
            catch
            {
                // Fallback if logging fails
            }
        }
    }
}
