using System.IO;

namespace Jinke.DredgeSkins
{
    internal static class SkinLog
    {
        internal static void Info(string message) { Write("INFO", message); }
        internal static void Warning(string message) { Write("WARN", message); }
        internal static void Error(string message) { Write("ERROR", message); }

        private static void Write(string level, string message)
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(SettingsStore.FilePath), "cosmetics.log");
                BoundedFileLog.Append(path, level, message);
            }
            catch { /* A read-only sidecar directory must not break game behavior. */ }
        }
    }
}
