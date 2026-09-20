using System;
using System.Diagnostics;
using System.IO;

namespace RejiDisplay.Helpers
{
    public static class BuildInfo
    {
        public static string GitCommitHash => "fa445cd";
        public static string BuildTimestamp => "2026-09-20 18:45:00";

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
