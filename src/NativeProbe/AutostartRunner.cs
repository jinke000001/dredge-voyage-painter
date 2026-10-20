using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jinke.DredgeSkins.NativeQA
{
    [Serializable] public sealed class AutostartRecord
    {
        public string scope = "QA scene-load integration fixture. Calls the native scene loader, not a UI click. Native loading/gameplay may normally write only the isolated QA profile.";
        public string utc, status, error, identifier, company, product, appDataPath, persistentDataPath, expectedSaveRoot, saveFile, metaFile, saveSha256BeforeLoad, requestedApi;
        public bool identityVerified, canLoadLast, usesDlc1, ownsDlc1, usesDlc2, ownsDlc2, qualificationPassed, worldReady;
        public int slot, loadCallCount;
        public float titleWaitSeconds, worldWaitSeconds;
    }

    /// <summary>One explicit integration request only. Never creates a save, edits native fields, or calls a UI handler.</summary>
    public sealed class AutostartRunner : MonoBehaviour
    {
        private const string ExpectedIdentifier = "local.jinke.dredge.skins.qa.20261016";
        private const string ExpectedCompany = "Jinke Local Tests", ExpectedProduct = "DREDGE Skins QA 20261016";
        private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private object title, manager;
        private Assembly gameAssembly;
        private string reportPath;
        private readonly AutostartRecord record = new AutostartRecord();

        public void Initialize(object owner) { title = owner; StartCoroutine(Run()); }
        private IEnumerator Run()
        {
            yield return null;
            if (!Prepare()) { Finish(); yield break; }
            float start = Time.realtimeSinceStartup;
            bool ready = false;
            while (Time.realtimeSinceStartup - start < 30f)
            {
                ready = Ready();
                if (ready || record.error != null) break;
                yield return null;
            }
            record.titleWaitSeconds = Time.realtimeSinceStartup - start;
            if (!ready)
            {
                if (record.error == null) record.error = "Title initialization did not become ready in 30 seconds.";
                record.status = "refused"; Finish(); yield break;
            }
            if (!LoadExistingQaSave()) { Finish(); yield break; }
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 120f)
            {
                try { record.worldReady = (bool)Member(manager, "IsPlaying"); }
                catch (Exception e) { record.error = e.ToString(); }
                if (record.worldReady || record.error != null) break;
                yield return null;
            }
            record.worldWaitSeconds = Time.realtimeSinceStartup - start;
            record.status = record.worldReady ? "world-ready" : "load-timeout-or-error";
            Finish();
        }

        private bool Prepare()
        {
            record.utc = DateTime.UtcNow.ToString("o"); record.status = "checking";
            try
            {
                Require(Environment.GetEnvironmentVariable("JINKE_NATIVE_QA_AUTOSTART") == "1", "Explicit QA autostart flag is absent.");
                string output = Environment.GetEnvironmentVariable("JINKE_NATIVE_PROBE_OUTPUT");
                Require(!string.IsNullOrEmpty(output) && Path.IsPathRooted(output), "Explicit absolute probe output directory is required.");
                output = Path.GetFullPath(output); NoSymlink(output);
                Require(!ProbeOutput.HasResultFiles(output), "Probe output root is not empty.");
                string startupDirectory = Path.Combine(output, "autostart");
                Require(!Directory.Exists(startupDirectory), "Autostart output already exists; refusing repeat.");
                Directory.CreateDirectory(startupDirectory); reportPath = Path.Combine(startupDirectory, "fixture.json");
                record.identifier = Application.identifier; record.company = Application.companyName; record.product = Application.productName;
                record.appDataPath = Application.dataPath; record.persistentDataPath = Application.persistentDataPath;
                record.expectedSaveRoot = Environment.GetEnvironmentVariable("JINKE_NATIVE_QA_SAVE_ROOT");
                Require(record.identifier == ExpectedIdentifier && record.company == ExpectedCompany && record.product == ExpectedProduct, "Application is not the isolated QA identity.");
                string canonicalQaRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", ExpectedIdentifier);
                Require(!string.IsNullOrEmpty(record.expectedSaveRoot) && Path.IsPathRooted(record.expectedSaveRoot) && SamePath(record.expectedSaveRoot, canonicalQaRoot), "Expected QA save root does not match the isolated bundle profile.");
                Require(SamePath(record.persistentDataPath, record.expectedSaveRoot), "Unity persistentDataPath is not the approved QA profile.");
                NoSymlink(record.persistentDataPath); NoSymlink(record.appDataPath);
                var app = Directory.GetParent(record.appDataPath);
                Require(Path.GetFileName(record.appDataPath) == "Contents" && app != null && app.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && app.Name.IndexOf("QA", StringComparison.OrdinalIgnoreCase) >= 0, "Runtime is not inside the QA app copy.");
                Require(Path.GetFullPath(typeof(Probe).Assembly.Location).StartsWith(Path.GetFullPath(record.appDataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal), "QA probe was not loaded from the current QA app.");
                Require(title is Behaviour && ((Behaviour)title) != null, "Missing native title component.");
                gameAssembly = title.GetType().Assembly;
                record.identityVerified = true; Write(); return true;
            }
            catch (Exception e) { record.status = "refused"; record.error = e.ToString(); return false; }
        }

        private bool Ready()
        {
            try
            {
                var behaviour = (Behaviour)title;
                if (behaviour == null || !behaviour.enabled || !behaviour.gameObject.activeInHierarchy) return false;
                manager = gameAssembly.GetType("GameManager", true).GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                if (manager == null) return false;
                Require(!(bool)Member(manager, "IsPlaying"), "A game is already playing; never issue another load.");
                bool managersReady = (bool)Member(manager, "HasLoadedAsyncManagers");
                bool betweenScenes = (bool)gameAssembly.GetType("SceneLoader", true).GetProperty("BetweenScenes", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
                return managersReady && !betweenScenes && Member(manager, "SaveManager") != null && Member(manager, "EntitlementManager") != null;
            }
            catch (Exception e) { record.error = e.ToString(); return false; }
        }

        private bool LoadExistingQaSave()
        {
            try
            {
                object saveManager = Member(manager, "SaveManager");
                record.canLoadLast = (bool)Invoke(saveManager, "CanLoadLast", Type.EmptyTypes, new object[0]);
                Require(record.canLoadLast, "No existing QA save; never create a new save.");
                object settings = Member(saveManager, "ActiveSettingsData"); record.slot = (int)Member(settings, "lastSaveSlot");
                Require(record.slot >= 0 && record.slot < (int)Member(saveManager, "numSlots"), "Invalid QA save slot.");
                object strategy = Member(saveManager, "SaveStrategy");
                Require(strategy.GetType().FullName == "WindowsSaveStrategy", "Unexpected native save strategy.");
                record.saveFile = (string)Invoke(strategy, "GetFilePath", new[] { typeof(int) }, new object[] { record.slot });
                record.metaFile = (string)Invoke(strategy, "GetMetaFilePath", Type.EmptyTypes, new object[0]);
                Require(SamePath(record.saveFile, Path.Combine(record.expectedSaveRoot, "saves", "dredge-save" + record.slot + ".bin")) &&
                    SamePath(record.metaFile, Path.Combine(record.expectedSaveRoot, "dredge-settings.bin")), "Native save strategy escapes the QA profile.");
                NoSymlink(record.saveFile); NoSymlink(record.metaFile);
                Require(File.Exists(record.saveFile) && File.Exists(record.metaFile), "Existing QA save/settings are required.");
                record.saveSha256BeforeLoad = ProbeJson.Sha256(File.ReadAllBytes(record.saveFile));
                object save = Invoke(saveManager, "LoadIntoMemory", new[] { typeof(int) }, new object[] { record.slot });
                Require(save != null, "QA save is null/corrupt; refused without a UI dialog.");
                Type entitlement = gameAssembly.GetType("Entitlement", true);
                object dlc1 = Enum.Parse(entitlement, "DLC_1"), dlc2 = Enum.Parse(entitlement, "DLC_2");
                object entitlementManager = Member(manager, "EntitlementManager");
                record.usesDlc1 = (bool)Invoke(save, "GetSaveUsesEntitlement", new[] { entitlement }, new[] { dlc1 });
                record.usesDlc2 = (bool)Invoke(save, "GetSaveUsesEntitlement", new[] { entitlement }, new[] { dlc2 });
                record.ownsDlc1 = (bool)Invoke(entitlementManager, "GetHasEntitlement", new[] { entitlement }, new[] { dlc1 });
                record.ownsDlc2 = (bool)Invoke(entitlementManager, "GetHasEntitlement", new[] { entitlement }, new[] { dlc2 });
                Require((!record.usesDlc1 || record.ownsDlc1) && (!record.usesDlc2 || record.ownsDlc2), "QA save DLC qualifications failed; refused without a UI dialog.");
                record.qualificationPassed = true;
                Require(record.loadCallCount == 0, "Scene loading has already been requested.");
                record.loadCallCount = 1; record.requestedApi = "SceneLoader.LoadGameFromTitle(canCreateNew: false)"; record.status = "requesting-native-load"; Write();
                Invoke(Member(manager, "Loader"), "LoadGameFromTitle", new[] { typeof(bool) }, new object[] { false });
                record.status = "native-load-requested"; Write(); return true;
            }
            catch (Exception e) { record.status = "refused-or-call-error"; record.error = e.ToString(); return false; }
        }

        private void Finish()
        {
            try { Write(); } catch (Exception) { /* No game changes or retries on evidence failure. */ }
            Object.Destroy(gameObject);
        }
        private void Write()
        {
            if (reportPath == null) return;
            string temporary = reportPath + ".tmp"; File.WriteAllText(temporary, ProbeJson.Serialize(record, true));
            if (File.Exists(reportPath)) File.Replace(temporary, reportPath, null); else File.Move(temporary, reportPath);
        }
        private static object Member(object owner, string name)
        {
            PropertyInfo property = owner.GetType().GetProperty(name, InstanceFlags);
            if (property != null) return property.GetValue(owner, null);
            FieldInfo field = owner.GetType().GetField(name, InstanceFlags);
            if (field != null) return field.GetValue(owner);
            throw new MissingMemberException(owner.GetType().FullName, name);
        }
        private static object Invoke(object owner, string name, Type[] parameters, object[] values)
        {
            MethodInfo method = owner.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameters, null);
            if (method == null) throw new MissingMethodException(owner.GetType().FullName, name);
            return method.Invoke(owner, values);
        }
        private static bool SamePath(string left, string right) { return Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar) == Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar); }
        private static void NoSymlink(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(current) || Directory.Exists(current)) Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "QA path contains a symlink: " + current);
                current = Path.GetDirectoryName(current);
            }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
