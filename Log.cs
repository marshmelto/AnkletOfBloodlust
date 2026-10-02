using System.IO;
using System.Runtime.CompilerServices;
using BepInEx.Logging;

namespace AnkletOfBloodlust
{
    // Mod-wide logger: writes to the BepInEx console/LogOutput.log, prefixed with the file and line
    // that logged it, e.g. "[AssetManager.cs:25] Couldn't load ...". Call Log.Init(Logger) in Awake.
    internal static class Log
    {
        private static ManualLogSource _logSource;

        internal static void Init(ManualLogSource logSource)
        {
            _logSource = logSource;
        }

        private static string Format(object data, string file, int line)
        {
            return $"[{Path.GetFileName(file)}:{line}] {data}";
        }

        internal static void Debug(object data, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => _logSource.LogDebug(Format(data, file, line));

        internal static void Info(object data, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => _logSource.LogInfo(Format(data, file, line));

        internal static void Message(object data, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => _logSource.LogMessage(Format(data, file, line));

        internal static void Warning(object data, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => _logSource.LogWarning(Format(data, file, line));

        internal static void Error(object data, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => _logSource.LogError(Format(data, file, line));

        internal static void Fatal(object data, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => _logSource.LogFatal(Format(data, file, line));
    }
}
