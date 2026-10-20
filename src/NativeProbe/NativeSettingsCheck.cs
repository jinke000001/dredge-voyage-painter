using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Jinke.DredgeSkins.NativeQA
{
    [Serializable] public sealed class SettingsCheckReport
    {
        public int serializationVersion = 2;
        public string scope = "QA component settings integration test, not a UI mouse test. Calls production Select/SettingsStore.Read; intentionally retains QA boat=ramen, flag=moon for next process startup. No native save or input/window changes.";
        public string utc, error, settingsPath, settingsSha256Before, settingsSha256, initialDiagnostic, finalDiagnostic, rendererBeforeSha256, rendererAfterSha256;
        public string persistedBoatBefore, persistedFlagBefore, persistedBoat, persistedFlag;
        public string[] settingsKeys;
        public int persistedSchemaVersion, rendererBeforeCount, rendererAfterCount;
        public bool passed, mainProbePassed, identityAndPathVerified, selectCalled, settingsFileExistedBefore, settingsFileChanged, persistedReadMatches, onlyExpectedSettingsFields;
        public bool nativeStateUnchanged, rendererStateUnchanged, windowUnchanged, qaSelectionRetained;
        public NativeState nativeBefore, nativeAfter;
    }

    public static class SettingsCheckContract
    {
        public static void VerifyTarget(string identifier, string company, string product, string settingsDirectory, string productionFilePath)
        {
            Need(identifier == "local.jinke.dredge.skins.qa.20261016" && company == "Jinke Local Tests" && product == "DREDGE Skins QA 20261016", "Settings test requires exact QA identity.");
            Need(!string.IsNullOrEmpty(settingsDirectory) && Path.IsPathRooted(settingsDirectory), "Explicit absolute QA settings directory is required.");
            string fullDirectory = Path.GetFullPath(settingsDirectory).TrimEnd(Path.DirectorySeparatorChar);
            Need(Path.GetFileName(fullDirectory) == "qa-settings", "Settings test requires a directory named qa-settings.");
            Need(!string.IsNullOrEmpty(productionFilePath) && Path.GetFullPath(productionFilePath) == Path.Combine(fullDirectory, "settings.json"), "Production settings path differs from the explicit QA directory.");
            string current = Path.GetFullPath(productionFilePath);
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(current) || Directory.Exists(current)) Need((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "QA settings path contains a symlink.");
                current = Path.GetDirectoryName(current);
            }
        }

        public static bool OnlySettingsKeys(string[] keys)
        {
            return keys != null && keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(new[] { "boat", "flag", "schemaVersion" });
        }
        internal static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }

    public sealed partial class ProbeRunner
    {
        private void RunSettingsCheck()
        {
            if (Environment.GetEnvironmentVariable("JINKE_NATIVE_QA_PREFS_CHECK") != "1") return;
            var check = new SettingsCheckReport { utc = DateTime.UtcNow.ToString("o"), mainProbePassed = report.passed };
            string path = Path.Combine(directory, "native-settings.json");
            try
            {
                SettingsCheckContract.Need(!File.Exists(path) && !File.Exists(path + ".readback.json"), "Settings check evidence already exists.");
                Type store = typeof(Bootstrap).Assembly.GetType("Jinke.DredgeSkins.SettingsStore", true);
                const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic;
                PropertyInfo filePath = store.GetProperty("FilePath", staticFlags);
                MethodInfo read = store.GetMethod("Read", staticFlags, null, Type.EmptyTypes, null);
                SettingsCheckContract.Need(filePath != null && read != null, "Production settings API is unavailable.");
                check.settingsPath = (string)filePath.GetValue(null, null);
                SettingsCheckContract.VerifyTarget(Application.identifier, Application.companyName, Application.productName,
                    Environment.GetEnvironmentVariable("JINKE_DREDGE_SKINS_SETTINGS_DIR"), check.settingsPath);
                check.identityAndPathVerified = true;
                check.settingsFileExistedBefore = File.Exists(check.settingsPath);
                SkinSettings prior = (SkinSettings)read.Invoke(null, null);
                check.persistedBoatBefore = prior.boat; check.persistedFlagBefore = prior.flag;
                SettingsCheckContract.Need(!check.settingsFileExistedBefore || prior.boat != "ramen" || prior.flag != "moon", "QA settings already contain the target; cannot prove a new production write.");
                if (check.settingsFileExistedBefore) check.settingsSha256Before = ProbeJson.Sha256(File.ReadAllBytes(check.settingsPath));
                object controller = ReadController();
                MethodInfo select = controller.GetType().GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string), typeof(bool) }, null);
                SettingsCheckContract.Need(select != null && (bool)Member(controller, "Available"), "Production selection API is unavailable.");
                check.initialDiagnostic = Bootstrap.GetDiagnostics(); check.nativeBefore = ReadNative();
                RendererState[] before = ReadRenderers(); bool windowBefore = ReadWindow();
                check.rendererBeforeCount = before.Length;
                check.rendererBeforeSha256 = ProbeJson.Sha256(Encoding.UTF8.GetBytes(ProbeJson.Serialize(before)));
                check.selectCalled = true;
                select.Invoke(controller, new object[] { "ramen", "moon", true });
                SettingsCheckContract.Need(File.Exists(check.settingsPath), "Production selection did not create the settings file.");
                SkinSettings persisted = (SkinSettings)read.Invoke(null, null);
                check.persistedSchemaVersion = persisted.schemaVersion; check.persistedBoat = persisted.boat; check.persistedFlag = persisted.flag;
                check.persistedReadMatches = persisted.schemaVersion == 1 && persisted.boat == "ramen" && persisted.flag == "moon";
                byte[] settingsBytes = File.ReadAllBytes(check.settingsPath);
                check.settingsSha256 = ProbeJson.Sha256(settingsBytes);
                check.settingsFileChanged = !check.settingsFileExistedBefore || check.settingsSha256Before != check.settingsSha256;
                check.settingsKeys = JObject.Parse(Encoding.UTF8.GetString(settingsBytes)).Properties().Select(p => p.Name).ToArray();
                check.onlyExpectedSettingsFields = SettingsCheckContract.OnlySettingsKeys(check.settingsKeys);
                check.nativeAfter = ReadNative(); check.nativeStateUnchanged = Same(check.nativeBefore, check.nativeAfter);
                RendererState[] after = ReadRenderers(); check.rendererAfterCount = after.Length;
                check.rendererAfterSha256 = ProbeJson.Sha256(Encoding.UTF8.GetBytes(ProbeJson.Serialize(after)));
                check.rendererStateUnchanged = Same(before, after);
                check.windowUnchanged = windowBefore == ReadWindow(); check.finalDiagnostic = Bootstrap.GetDiagnostics();
                check.qaSelectionRetained = ReadDiagnostic("boat") == "ramen" && ReadDiagnostic("flag") == "moon";
                check.passed = check.settingsFileChanged && check.persistedReadMatches && check.onlyExpectedSettingsFields && check.nativeStateUnchanged && check.rendererStateUnchanged &&
                    check.windowUnchanged && check.qaSelectionRetained && check.rendererBeforeCount == 25 && check.rendererAfterCount == 25;
            }
            catch (Exception e) { check.passed = false; check.error = e.ToString(); }
            try
            {
                if (File.Exists(path)) return;
                WriteNewSettingsEvidence(path, ProbeJson.Serialize(check, true));
                SettingsCheckReport reread = ProbeJson.Deserialize<SettingsCheckReport>(File.ReadAllText(path));
                SettingsCheckContract.Need(ProbeJson.Same(check, reread), "Settings evidence readback changed DTO fields.");
                WriteNewSettingsEvidence(path + ".readback.json", ProbeJson.Serialize(new System.Collections.Generic.Dictionary<string, object>
                { { "passed", true }, { "serializationVersion", 2 }, { "sha256", ProbeJson.Sha256(File.ReadAllBytes(path)) } }, true));
            }
            catch (Exception e)
            {
                string failure = path + ".verification-error.txt";
                if (!File.Exists(failure)) WriteNewSettingsEvidence(failure, e.ToString());
            }
        }

        private static void WriteNewSettingsEvidence(string path, string text)
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text); file.Write(bytes, 0, bytes.Length); file.Flush(true);
            }
        }
    }
}
