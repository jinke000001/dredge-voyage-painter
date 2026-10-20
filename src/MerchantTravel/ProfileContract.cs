using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;

namespace Jinke.DredgeMerchantTravel
{
    internal static class ProfileContract
    {
        internal const string Identifier = "local.jinke.dredge.merchanttravel.qa.20261018";
        internal const string VoyageIdentifier = "local.jinke.dredge.voyage.qa.20261018";
        internal const string VoyageRevisionIdentifier = "local.jinke.dredge.voyage.qa.20261019";
        internal const string PainterIdentifier = "local.jinke.dredge.voyage.painter.qa.20261019";
        internal const string Company = "Jinke Local Tests";
        internal const string Product = "DREDGE Merchant Travel QA 20261018";
        internal const string OutputRoot = "/Volumes/T7 Shield/渔帆暗涌REA/output";
        internal const string OriginalAssemblyHash = "a071ffebe51514af5f661055f126a5041acda127623ee7c681c6ef2233a24729";
        internal const string RuntimeFile = "Jinke.DredgeMerchantTravel.dll";

        internal static string FullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new InvalidDataException("Expected an absolute path.");
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }

        internal static void RequireEqual(string actual, string expected, string label)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal)) throw new InvalidDataException("Isolation mismatch: " + label);
        }

        internal static void NoLinks(string path)
        {
            string cursor = FullPath(path);
            while (!string.IsNullOrEmpty(cursor))
            {
                if (File.Exists(cursor) || Directory.Exists(cursor))
                {
                    if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Symbolic links are not permitted: " + cursor);
                }
                cursor = Path.GetDirectoryName(cursor);
            }
        }

        internal static string HashFile(string path, long maxBytes)
        {
            NoLinks(path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > maxBytes) throw new InvalidDataException("File missing or outside size bound: " + path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(stream));
        }

        internal static string HashText(string text)
        {
            using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }

        internal static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        internal static string Verify(string identifier, string company, string product, string dataPath, string persistentPath,
            string runtimePath, string home, string currentDirectory, string proofEnvironment, string profileEnvironment)
        {
            string expectedProduct;
            if (identifier == PainterIdentifier) expectedProduct = "DREDGE Voyage Painter QA 20261019";
            else if (identifier == VoyageRevisionIdentifier) expectedProduct = "DREDGE Voyage QA 20261019";
            else if (identifier == VoyageIdentifier) expectedProduct = "DREDGE Voyage QA 20261018";
            else { RequireEqual(identifier, Identifier, "bundle identifier"); expectedProduct = Product; }
            RequireEqual(company, Company, "company");
            RequireEqual(product, expectedProduct, "product");
            string resources = FullPath(dataPath);
            // On macOS Application.dataPath is the app's Contents directory in this Unity player.
            string contents = resources.EndsWith("/Contents", StringComparison.Ordinal) ? resources :
                resources.EndsWith("/Contents/Resources/Data", StringComparison.Ordinal) ? Path.GetDirectoryName(Path.GetDirectoryName(resources)) : null;
            if (contents == null) throw new InvalidDataException("Unexpected macOS player data path.");
            string app = FullPath(Path.GetDirectoryName(contents));
            if (!app.StartsWith(OutputRoot + "/", StringComparison.Ordinal) || !app.EndsWith(".app", StringComparison.Ordinal))
                throw new InvalidDataException("This runtime may only run in the isolated output app.");
            string expectedProfile = FullPath(Path.Combine(home, "Library/Application Support/" + identifier));
            RequireEqual(FullPath(persistentPath), expectedProfile, "save profile");
            RequireEqual(FullPath(runtimePath), Path.Combine(contents, "Resources/Data/Managed/" + RuntimeFile), "loaded runtime");
            string resourceDirectory = Path.Combine(contents, "Resources");
            string proofPath = Path.Combine(resourceDirectory, "merchant-travel-proof.json");
            RequireEqual(proofEnvironment, proofPath, "launcher proof");
            RequireEqual(profileEnvironment, expectedProfile, "launcher profile");
            RequireStartupWorkingDirectory(currentDirectory, resourceDirectory, app);
            NoLinks(app);
            NoLinks(expectedProfile);
            InspectProfileTree(expectedProfile);
            NoLinks(proofPath);
            var proofInfo = new FileInfo(proofPath);
            if (!proofInfo.Exists || proofInfo.Length > 32768) throw new InvalidDataException("Isolation proof unavailable.");
            JObject proof = JObject.Parse(File.ReadAllText(proofPath));
            if ((int?)proof["schemaVersion"] != 1 || (int?)proof["steamAppId"] != 1562430) throw new InvalidDataException("Unsupported isolation proof.");
            RequireEqual((string)proof["mode"], "merchant-travel-isolated", "proof mode");
            RequireEqual((string)proof["bundleIdentifier"], identifier, "proof identifier");
            RequireEqual((string)proof["company"], Company, "proof company");
            RequireEqual((string)proof["product"], product, "proof product");
            RequireEqual((string)proof["appPath"], app, "proof app");
            RequireEqual((string)proof["persistentDataPath"], expectedProfile, "proof profile");
            RequireEqual((string)proof["originalAssemblySha256"], OriginalAssemblyHash, "original assembly");
            CheckHash(proof, "assemblySha256", Path.Combine(contents, "Resources/Data/Managed/Assembly-CSharp.dll"));
            CheckHash(proof, "runtimeSha256", runtimePath);
            CheckHash(proof, "globalGameManagersSha256", Path.Combine(contents, "Resources/Data/globalgamemanagers"));
            CheckHash(proof, "infoPlistSha256", Path.Combine(contents, "Info.plist"));
            CheckHash(proof, "appInfoSha256", Path.Combine(contents, "Resources/Data/app.info"));
            CheckHash(proof, "launchGuardSha256", Path.Combine(contents, "Resources/merchant_travel_guard.py"));
            string steamFile = Path.Combine(resourceDirectory, "steam_appid.txt");
            NoLinks(steamFile);
            RequireEqual(File.ReadAllText(steamFile).Trim(), "1562430", "Steam app id");
            RequireEqual(Environment.GetEnvironmentVariable("SteamAppId"), "1562430", "SteamAppId");
            RequireEqual(Environment.GetEnvironmentVariable("SteamGameId"), "1562430", "SteamGameId");
            return resourceDirectory;
        }

        internal static void RequireStartupWorkingDirectory(string actual, string resources, string app)
        {
            string cwd = FullPath(actual);
            string expectedResources = FullPath(resources);
            string expectedApp = FullPath(app);
            RequireEqual(expectedResources, Path.Combine(expectedApp, "Contents/Resources"), "app Resources relationship");
            if (cwd != expectedResources && cwd != Path.GetDirectoryName(expectedApp))
                throw new InvalidDataException("Isolation mismatch: startup working directory");
        }

        [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
        private static extern int MacLStat(string path, IntPtr data);

        private static void InspectProfileTree(string root)
        {
            if (!Directory.Exists(root)) return;
            var pending = new Stack<string>();
            pending.Push(root);
            int count = 0;
            IntPtr buffer = Marshal.AllocHGlobal(512);
            try
            {
                while (pending.Count > 0)
                {
                    string directory = pending.Pop();
                    foreach (string entry in Directory.GetFileSystemEntries(directory))
                    {
                        if (++count > 20000) throw new InvalidDataException("Unexpectedly large isolated profile.");
                        if (MacLStat(entry, buffer) != 0) throw new IOException("Cannot inspect isolated profile item.");
                        int mode = (ushort)Marshal.ReadInt16(buffer, 4) & 0xf000;
                        int links = (ushort)Marshal.ReadInt16(buffer, 6);
                        if (mode == 0xa000 || (mode == 0x8000 && links != 1))
                            throw new InvalidDataException("Linked profile items are forbidden: " + entry);
                        if (mode == 0x4000) pending.Push(entry);
                        else if (mode != 0x8000) throw new InvalidDataException("Unexpected profile item type: " + entry);
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static void CheckHash(JObject proof, string key, string path)
        {
            string expected = (string)proof[key];
            if (!IsHash(expected)) throw new InvalidDataException("Missing proof hash: " + key);
            RequireEqual(HashFile(path, 256L * 1024 * 1024), expected, key);
        }
    }
}
