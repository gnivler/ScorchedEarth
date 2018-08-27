using System;
using System.IO;
using System.Reflection;
using Harmony;
using static ScorchedEarth.ScorchedEarth;

namespace ScorchedEarth
{
    public static class Logger
    {
        private static string LogFilePath => Path.Combine(ModDirectory, "log.txt");
        private static readonly string Version = ((AssemblyFileVersionAttribute) Attribute.GetCustomAttribute(
            Assembly.GetExecutingAssembly(), typeof(AssemblyFileVersionAttribute), false)).Version;

        public static void LogError(Exception ex)
        {
            using (var writer = new StreamWriter(LogFilePath, true))
            {
                writer.WriteLine($"Message: {ex.Message}");
                writer.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        public static void LogDebug(string line)
        {
            if (!debug) return;
            using (var writer = new StreamWriter(LogFilePath, true))
            {
                writer.WriteLine(line);
            }
        }

        public static void LogClear()
        {
            using (var writer = new StreamWriter(LogFilePath, false))
            {
                writer.WriteLine($"{DateTime.Now.ToLongTimeString()} Scorched Earth v{Version}");
            }
        }
    }
}
