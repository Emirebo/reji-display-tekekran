using System;
using System.Diagnostics;
using System.IO;

namespace RejiDisplay.Helpers
{
    public static class BuildInfo
    {
        private static string? _cachedGitHash;

        public static string GitCommitHash
        {
            get
            {
                if (_cachedGitHash == null)
                {
                    try
                    {
                        var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                        {
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                        };
                        using var p = Process.Start(psi);
                        if (p != null)
                        {
                            string output = p.StandardOutput.ReadToEnd().Trim();
                            p.WaitForExit(1000);
                            if (!string.IsNullOrEmpty(output) && output.Length <= 12)
                            {
                                _cachedGitHash = output;
                                return _cachedGitHash;
                            }
                        }
                    }
                    catch { }

                    _cachedGitHash = "a0fbc13";
                }
                return _cachedGitHash;
            }
        }

        public static string BuildTimestamp => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        public static string GetExecutablePath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule?.FileName 
                       ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            }
            catch
            {
                return System.Reflection.Assembly.GetExecutingAssembly().Location;
            }
        }

        public static int ProcessId
        {
            get
            {
                try
                {
                    return Process.GetCurrentProcess().Id;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static string BuildBanner => $"[BUILD_ID: commit={GitCommitHash} | pid={ProcessId} | exe={Path.GetFileName(GetExecutablePath())}]";
    }
}

