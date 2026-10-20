using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Jinke.DredgeMerchantTravel
{
    /// <summary>Available before any profile is trusted. Never writes to Application.persistentDataPath.</summary>
    internal static class StartupDiagnostics
    {
        private const string DirectoryPath = ProfileContract.OutputRoot + "/runtime/merchant-travel-startup";

        internal static void TryWrite(string phase, Exception exception)
        {
            try
            {
                ProfileContract.NoLinks(DirectoryPath);
                Directory.CreateDirectory(DirectoryPath);
                ProfileContract.NoLinks(DirectoryPath);
                string file = Path.Combine(DirectoryPath, DateTime.UtcNow.ToString("yyyyMMddTHHmmss.fffffffZ", CultureInfo.InvariantCulture) +
                    "-" + Guid.NewGuid().ToString("N") + ".txt");
                ProfileContract.NoLinks(file);
                var text = new StringBuilder();
                Add(text, "phase", () => phase);
                Add(text, "utc", () => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                Add(text, "identifier", () => Application.identifier);
                Add(text, "company", () => Application.companyName);
                Add(text, "product", () => Application.productName);
                Add(text, "dataPath", () => Application.dataPath);
                Add(text, "persistentDataPath", () => Application.persistentDataPath);
                Add(text, "userProfile", () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                Add(text, "personal", () => Environment.GetFolderPath(Environment.SpecialFolder.Personal));
                Add(text, "cwd", () => Environment.CurrentDirectory);
                Add(text, "runtimeAssembly", () => Assembly.GetExecutingAssembly().Location);
                Add(text, "proofEnvironment", () => Environment.GetEnvironmentVariable("JINKE_DREDGE_MERCHANT_TRAVEL_PROOF"));
                Add(text, "profileEnvironment", () => Environment.GetEnvironmentVariable("JINKE_DREDGE_MERCHANT_TRAVEL_PROFILE"));
                Add(text, "SteamAppId", () => Environment.GetEnvironmentVariable("SteamAppId"));
                Add(text, "SteamGameId", () => Environment.GetEnvironmentVariable("SteamGameId"));
                Add(text, "platform", () => Application.platform.ToString());
                Add(text, "unityVersion", () => Application.unityVersion);
                Add(text, "pointerSize", () => IntPtr.Size.ToString(CultureInfo.InvariantCulture));
                if (exception != null)
                {
                    string detail = exception.ToString();
                    text.Append("exception=\n").Append(detail.Length <= 16384 ? detail : detail.Substring(0, 16384)).Append('\n');
                }
                // CreateNew never overwrites an existing path, including a file placed during a race.
                using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text.ToString());
            }
            catch (Exception) { /* Diagnostics are best effort. They cannot grant access or mask the guard exception. */ }
        }

        private static void Add(StringBuilder output, string key, Func<string> read)
        {
            string value;
            try { value = read() ?? "<null>"; }
            catch (Exception error) { value = "<read failed: " + error.GetType().FullName + ": " + error.Message + ">"; }
            value = value.Replace("\r", "\\r").Replace("\n", "\\n");
            if (value.Length > 4096) value = value.Substring(0, 4096);
            output.Append(key).Append('=').Append(value).Append('\n');
        }
    }
}
