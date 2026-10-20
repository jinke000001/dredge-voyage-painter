using System;
using System.IO;
using UnityEngine;

namespace Jinke.DredgeMerchantTravel
{
    internal static class TravelLog
    {
        private static readonly object Sync = new object();
        internal static void Info(string message) { Write("INFO", message); Debug.Log("[MerchantTravel] " + message); }
        internal static void Error(string message) { Write("ERROR", message); Debug.LogError("[MerchantTravel] " + message); }
        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    string directory = Path.Combine(Application.persistentDataPath, "MerchantTravel");
                    string path = Path.Combine(directory, "runtime.log");
                    ProfileContract.NoLinks(path);
                    Directory.CreateDirectory(directory);
                    if (File.Exists(path) && new FileInfo(path).Length > 262144)
                    {
                        string previous = path + ".previous";
                        ProfileContract.NoLinks(previous);
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(path, previous);
                    }
                    string bounded = message.Length > 12000 ? message.Substring(0, 12000) : message;
                    File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + level + " " + bounded + Environment.NewLine);
                }
            }
            catch (Exception) { /* Logging must not throw into an original game event. */ }
        }
    }
}
