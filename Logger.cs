using System;
using System.IO;
using static ScorchedEarth.ScorchedEarth;

namespace ScorchedEarth
{
    public static class Logger
    {
        private static string LogFilePath => $"{ModDirectory}/log.txt";
        public static void Error(Exception ex)
        {
            using (var writer = new StreamWriter(LogFilePath, true))
            {
                writer.WriteLine($"Message: {ex.Message}");
                writer.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        public static void Debug(string line)
        {
            if (!EnableDebug) return;
            using (var writer = new StreamWriter(LogFilePath, true))
            {
                writer.WriteLine(line);
            }
        }

        public static void Clear()
        {
            using (var writer = new StreamWriter(LogFilePath, false))
            {
                writer.WriteLine($"{DateTime.Now.ToLongTimeString()} Init");
            }
        }
    }
}
