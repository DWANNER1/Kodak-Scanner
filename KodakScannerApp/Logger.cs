using System;
using System.IO;
using System.Text;

namespace KodakScannerApp
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logPath;

        public static void Initialize(string outputRoot)
        {
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                return;
            }
            _logPath = Path.Combine(outputRoot, "debug.log");
        }

        public static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(_logPath))
            {
                return;
            }

            var line = DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine;
            lock (_lock)
            {
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }
        }
    }
}
