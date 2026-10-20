using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Jinke.DredgeSkins
{
    /// <summary>Component-owned logs only. Failures never escape into a game callback.</summary>
    internal static class BoundedFileLog
    {
        internal const int MaxFileBytes = 128 * 1024;
        private const int MaxMessageCharacters = 4096;
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        internal static bool Append(string path, string level, string message)
        {
            try
            {
                message = message ?? string.Empty;
                if (message.Length > MaxMessageCharacters) message = message.Substring(0, MaxMessageCharacters) + " [truncated]";
                string line = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) + " " + level + " " + message + Environment.NewLine;
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    long length = File.Exists(path) ? new FileInfo(path).Length : 0;
                    if (length + Utf8.GetByteCount(line) > MaxFileBytes)
                    {
                        string previous = path + ".previous";
                        if (length <= MaxFileBytes) File.Copy(path, previous, true);
                        else File.WriteAllText(previous, "Previous log exceeded the size limit and was discarded.\n", Utf8);
                        File.WriteAllText(path, string.Empty, Utf8);
                    }
                    File.AppendAllText(path, line, Utf8);
                }
                return true;
            }
            catch { return false; }
        }
    }
}
