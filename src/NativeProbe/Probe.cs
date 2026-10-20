using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jinke.DredgeSkins.NativeQA
{
    /// <summary>QA-only. Requires an explicit output environment variable. Never included in the release package.</summary>
    public static class Probe
    {
        private static bool scheduled;
        private static bool titleScheduled;
        private static readonly List<object> owners = new List<object>();
        public static void AfterColors(object owner)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JINKE_NATIVE_PROBE_OUTPUT"))) return;
                if (owner != null && !owners.Contains(owner)) owners.Add(owner);
                if (scheduled) return;
                scheduled = true;
                var host = new GameObject("QA-ONLY-Jinke-NativeProbe");
                host.hideFlags = HideFlags.DontSave;
                Object.DontDestroyOnLoad(host);
                host.AddComponent<ProbeRunner>().Initialize(owners);
            }
            catch (Exception e) { Debug.LogError("[JinkeNativeProbe] SCHEDULE FAILED: " + e); }
        }
        public static void AfterTitle(object owner)
        {
            if (titleScheduled || Environment.GetEnvironmentVariable("JINKE_NATIVE_QA_AUTOSTART") != "1") return;
            titleScheduled = true;
            try
            {
                var host = new GameObject("QA-ONLY-Jinke-SceneLoadFixture");
                host.hideFlags = HideFlags.DontSave; Object.DontDestroyOnLoad(host);
                host.AddComponent<AutostartRunner>().Initialize(owner);
            }
            catch (Exception e) { Debug.LogError("[JinkeNativeProbe] QA fixture unavailable: " + e); }
        }
    }

    [Serializable] public sealed class NativeState
    {
        public string roofId, hullId, flagId, hullTier, hasChangedBoatColors, bunting, isPlaying, isPaused, canUnpause;
        public float timeScale;
    }
    [Serializable] public sealed class MaterialState
    {
        public int id;
        public string name, shader;
        public int renderQueue;
        public string[] keywords;
        public string[] properties;
    }
    [Serializable] public sealed class RendererState
    {
        public int id;
        public string name, kind, mesh;
        public bool activeSelf, activeInHierarchy, enabled;
        public int vertexCount, subMeshCount;
        public Vector3 meshBounds;
        public MaterialState[] materials;
        public string[] lightOverrides;
    }
    [Serializable] public sealed class FlagCheck
    {
        public int rendererId, nativeTextureId, effectiveTextureId, lowerRowsDifferentBytes, clothDifferentPixels;
        public bool hasOverride, visible, enabled;
        public string nativeAtlas, effectiveAtlas;
    }
    [Serializable] public sealed class EffectiveState
    {
        public int rendererId;
        public string category, flagRgbaSha256;
        public Color hull, roof;
    }
    [Serializable] public sealed class SkinResult
    {
        public string selection, diagnostic, preview, previewError;
        public bool previewUnavailable = true;
        public int previewNonBackgroundPixels, previewTotalPixels, previewMinimumVisiblePixels = PreviewPixels.MinimumVisiblePixels;
        public bool colorsMatch, nativeStateUnchanged, rendererStateUnchanged;
        public int verifiedBoatCount, verifiedFlagCount;
        public NativeState nativeAfter;
        public FlagCheck[] flags;
    }
    [Serializable] public sealed class ProbeReport
    {
        public int serializationVersion = 2;
        public string serialization = "Newtonsoft.Json; explicit DTO public fields only; readback sidecar required";
        public string scope = "QA-only renderer assertions. The rendering probe never writes native fields or saves. Optional autostart fixture invokes native QA loading, which can normally write isolated QA saves/settings.";
        public string utc, unityVersion, graphicsDevice, initialBoat, initialFlag, initialDiagnostic, restoredDiagnostic, error;
        public bool passed, initialWindow, initialSelectionRestored, nativeStateRestored, rendererStateRestored, effectiveAppearanceRestored;
        public bool serializationSensitivityPassed, prerequisitesReady;
        public double effectiveColorTolerance = EffectiveAppearanceComparison.ColorTolerance, effectiveMaxAbsChannelDelta;
        public float prerequisiteWaitSeconds;
        public string prerequisiteObservation;
        public int boatCount, flagCount;
        public NativeState nativeBefore, nativeAfter;
        public RendererState[] renderers;
        public EffectiveState[] effectiveBefore, effectiveAfter;
        public SkinResult[] skins;
    }

    public sealed partial class ProbeRunner : MonoBehaviour
    {
        internal const string Hull = "_Hull_Color", Roof = "_Roof_Color", Flag = "Texture2D_9aa7ba2263944b48bbf43c218dc48459";
        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private List<object> owners;
        private List<Renderer> boats, flags;
        private string directory;
        private object gameManager;
        private ProbeReport report;
        private bool changedSelection;
        private bool prerequisitesReady;
        private float readinessWait;
        private string readinessObservation;
        public void Initialize(List<object> observedOwners) { owners = observedOwners; StartCoroutine(RunNextFrame()); }
        private IEnumerator RunNextFrame()
        {
            yield return null; // Let the original OnEnable finish registering flags and their native visibility.
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 60f)
            {
                bool stop = false;
                try
                {
                    if (owners.Count == 0) throw new InvalidOperationException("No customizer observed.");
                    Type gm = owners[0].GetType().Assembly.GetType("GameManager", true);
                    object instance = gm.GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                    object controller = ReadController();
                    int registeredFlags = ((IEnumerable)Member(controller, "flags")).Cast<object>().Count();
                    int registeredBoats = ((IEnumerable)Member(controller, "boats")).Cast<object>().Count();
                    bool playing = instance != null && (bool)Member(instance, "IsPlaying");
                    bool available = (bool)Member(controller, "Available");
                    readinessObservation = "IsPlaying=" + playing + "; Available=" + available + "; boatRenderers=" + registeredBoats + "; flagRenderers=" + registeredFlags;
                    prerequisitesReady = playing && available && registeredBoats == 20 && registeredFlags == 5;
                }
                catch (Exception e) { readinessObservation = e.ToString(); stop = true; }
                if (prerequisitesReady || stop) break;
                yield return null;
            }
            readinessWait = Time.realtimeSinceStartup - start;
            Run();
            Object.Destroy(gameObject);
        }
        private void Run()
        {
            report = new ProbeReport { utc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName,
                prerequisitesReady = prerequisitesReady, prerequisiteWaitSeconds = readinessWait, prerequisiteObservation = readinessObservation };
            var results = new List<SkinResult>();
            bool mainReportVerified = false;
            try
            {
                directory = Path.GetFullPath(Environment.GetEnvironmentVariable("JINKE_NATIVE_PROBE_OUTPUT"));
                if (ProbeOutput.HasResultFiles(directory)) throw new IOException("Probe output directory is not empty; refusing overwrite.");
                Directory.CreateDirectory(directory);
                ProbeJson.AssertDifferenceSensitivity(); report.serializationSensitivityPassed = true;
                if (!prerequisitesReady) throw new InvalidOperationException("Native prerequisites not ready within bounded wait: " + readinessObservation);
                boats = Collect("meshRenderers", "buntingMeshRenderers"); flags = Collect("flagMeshRenderers");
                report.boatCount = boats.Count; report.flagCount = flags.Count;
                if (owners.Count == 0) throw new InvalidOperationException("No PlayerColorCustomizer observed.");
                Type gm = owners[0].GetType().Assembly.GetType("GameManager", true);
                gameManager = gm.GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                report.initialDiagnostic = Bootstrap.GetDiagnostics();
                report.initialBoat = ReadDiagnostic("boat"); report.initialFlag = ReadDiagnostic("flag"); report.initialWindow = ReadWindow();
                report.nativeBefore = ReadNative(); report.renderers = ReadRenderers(); report.effectiveBefore = ReadEffectiveAppearance();
                Type parser = typeof(Bootstrap).Assembly.GetType("Jinke.DredgeSkins.SkinJson", true);
                MethodInfo loadManifest = parser.GetMethod("LoadManifest", BindingFlags.Static | BindingFlags.NonPublic);
                if (loadManifest == null) throw new MissingMethodException("SkinJson.LoadManifest");
                PackManifest manifest = (PackManifest)loadManifest.Invoke(null, new object[] { Path.Combine(Application.streamingAssetsPath, "JinkeSkins") });
                foreach (string id in new[] { "original", "ramen", "octopus", "moon" })
                    results.Add(CheckSkin(id, manifest));
                report.passed = report.boatCount == 20 && report.flagCount == 5 && results.All(r => r.colorsMatch && r.nativeStateUnchanged && r.rendererStateUnchanged &&
                    r.verifiedBoatCount == boats.Count && r.verifiedFlagCount == flags.Count && r.flags.All(f => f.lowerRowsDifferentBytes == 0 && (r.selection == "original" ? f.clothDifferentPixels == 0 : f.clothDifferentPixels > 0)));
            }
            catch (Exception e) { report.passed = false; report.error = e.ToString(); Debug.LogError("[JinkeNativeProbe] FAILED: " + e); }
            finally
            {
                try
                {
                    if (report.initialBoat != null && changedSelection)
                    {
                        Bootstrap.ConfigurePreview(report.initialBoat, report.initialFlag, report.initialWindow);
                        report.restoredDiagnostic = Bootstrap.GetDiagnostics();
                        report.initialSelectionRestored = ReadDiagnostic("boat") == report.initialBoat && ReadDiagnostic("flag") == report.initialFlag && ReadWindow() == report.initialWindow;
                        report.nativeAfter = ReadNative();
                        report.nativeStateRestored = Same(report.nativeBefore, report.nativeAfter);
                        report.rendererStateRestored = Same(report.renderers, ReadRenderers());
                        report.effectiveAfter = ReadEffectiveAppearance();
                        report.effectiveAppearanceRestored = EffectiveAppearanceComparison.Equivalent(report.effectiveBefore, report.effectiveAfter, out report.effectiveMaxAbsChannelDelta);
                        report.passed &= report.initialSelectionRestored && report.nativeStateRestored && report.rendererStateRestored && report.effectiveAppearanceRestored;
                    }
                }
                catch (Exception e) { report.passed = false; report.error += "\nRestore failure: " + e; }
                report.skins = results.ToArray();
                if (directory != null && Directory.Exists(directory))
                {
                    string path = Path.Combine(directory, "native-probe.json");
                    if (!File.Exists(path))
                    {
                        try { ProbeJson.WriteVerifiedReport(path, report); mainReportVerified = true; }
                        catch (Exception e) { File.WriteAllText(path + ".verification-error.txt", e.ToString()); }
                    }
                }
                Debug.Log("[JinkeNativeProbe] COMPLETE passed=" + report.passed + "; output=" + directory);
            }
            if (report.passed && mainReportVerified) RunSettingsCheck();
        }

        private SkinResult CheckSkin(string id, PackManifest manifest)
        {
            changedSelection = true;
            Bootstrap.ConfigurePreview(id, id, false);
            var result = new SkinResult { selection = id, diagnostic = Bootstrap.GetDiagnostics(), colorsMatch = true };
            SkinDefinition definition = manifest.skins.FirstOrDefault(s => s.id == id);
            Color expectedHull = default(Color), expectedRoof = default(Color);
            if (definition != null) { ColorUtility.TryParseHtmlString(definition.hullColor, out expectedHull); ColorUtility.TryParseHtmlString(definition.roofColor, out expectedRoof); }
            foreach (Renderer renderer in boats)
            {
                Material material = renderer.sharedMaterials[0];
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
                Color actualHull = block.HasColor(Hull) ? block.GetColor(Hull) : material.GetColor(Hull);
                Color actualRoof = block.HasColor(Roof) ? block.GetColor(Roof) : material.GetColor(Roof);
                bool matches = Close(actualHull, definition == null ? material.GetColor(Hull) : expectedHull) && Close(actualRoof, definition == null ? material.GetColor(Roof) : expectedRoof);
                result.colorsMatch &= matches; if (matches) result.verifiedBoatCount++;
            }
            var flagResults = new List<FlagCheck>();
            var exported = new Dictionary<int, string>();
            foreach (Renderer renderer in flags)
            {
                Material material = renderer.sharedMaterials[0];
                Texture native = material.GetTexture(Flag);
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
                Texture effective = block.HasTexture(Flag) ? block.GetTexture(Flag) : native;
                if (native == null || effective == null || native.width != 256 || native.height != 256 || effective.width != 256 || effective.height != 256)
                    throw new InvalidOperationException("Unexpected native/effective flag atlas dimensions.");
                Texture2D nativeCopy = GpuExport.Read(native), effectiveCopy = GpuExport.Read(effective);
                try
                {
                    Color32[] nativePixels = nativeCopy.GetPixels32(), actualPixels = effectiveCopy.GetPixels32();
                    var check = new FlagCheck { rendererId = renderer.GetInstanceID(), nativeTextureId = native.GetInstanceID(), effectiveTextureId = effective.GetInstanceID(),
                        hasOverride = block.HasTexture(Flag), visible = renderer.gameObject.activeInHierarchy, enabled = renderer.enabled };
                    for (int i = 0; i < nativePixels.Length; i++)
                    {
                        Color32 a = nativePixels[i], b = actualPixels[i];
                        if (i < 256 * 76) check.lowerRowsDifferentBytes += (a.r == b.r ? 0 : 1) + (a.g == b.g ? 0 : 1) + (a.b == b.b ? 0 : 1) + (a.a == b.a ? 0 : 1);
                        else if (!a.Equals(b)) check.clothDifferentPixels++;
                    }
                    check.nativeAtlas = SaveAtlas(native, nativeCopy, "native", exported);
                    check.effectiveAtlas = SaveAtlas(effective, effectiveCopy, id, exported);
                    flagResults.Add(check); result.verifiedFlagCount++;
                }
                finally { Object.Destroy(nativeCopy); Object.Destroy(effectiveCopy); }
            }
            result.flags = flagResults.ToArray();
            result.nativeAfter = ReadNative(); result.nativeStateUnchanged = Same(report.nativeBefore, result.nativeAfter);
            result.rendererStateUnchanged = Same(report.renderers, ReadRenderers());
            if (flags.Count > 0)
            {
                try
                {
                    PreviewExport preview = GpuExport.RenderMesh(flags[0], directory, id);
                    result.preview = preview.FileName; result.previewNonBackgroundPixels = preview.NonBackgroundPixels; result.previewTotalPixels = preview.TotalPixels;
                    result.previewUnavailable = preview.NonBackgroundPixels < PreviewPixels.MinimumVisiblePixels;
                    if (result.previewUnavailable) result.previewError = "Blank or insufficient mesh coverage: " + preview.NonBackgroundPixels + " non-background pixels; required " + PreviewPixels.MinimumVisiblePixels + ". Image is not visual-pass evidence.";
                }
                catch (Exception e) { result.previewUnavailable = true; result.previewError = e.ToString(); Debug.LogWarning("[JinkeNativeProbe] Mesh preview unavailable: " + e.Message); }
            }
            return result;
        }
        private string SaveAtlas(Texture source, Texture2D copy, string selection, Dictionary<int, string> exported)
        {
            string name;
            if (exported.TryGetValue(source.GetInstanceID(), out name)) return name;
            name = selection + "-atlas-" + source.GetInstanceID() + ".png";
            string path = Path.Combine(directory, name);
            if (!File.Exists(path)) File.WriteAllBytes(path, ImageConversion.EncodeToPNG(copy));
            exported.Add(source.GetInstanceID(), name); return name;
        }
        private List<Renderer> Collect(params string[] fields)
        {
            var result = new List<Renderer>();
            foreach (object owner in owners)
                foreach (string field in fields)
                    foreach (object value in (IEnumerable)owner.GetType().GetField(field, AnyInstance).GetValue(owner))
                    {
                        Renderer renderer = value as Renderer;
                        if (renderer != null && !result.Contains(renderer)) result.Add(renderer);
                    }
            return result.OrderBy(r => r.GetInstanceID()).ToList();
        }
        private string ReadDiagnostic(string key)
        {
            string prefix = key + "=";
            foreach (string field in Bootstrap.GetDiagnostics().Split(';'))
            {
                string text = field.Trim(); if (text.StartsWith(prefix, StringComparison.Ordinal)) return text.Substring(prefix.Length);
            }
            throw new InvalidOperationException("Missing runtime diagnostic: " + key);
        }
        private static bool ReadWindow()
        {
            return (bool)Member(ReadController(), "WindowOpen");
        }
        private static object ReadController()
        {
            FieldInfo controllerField = typeof(Bootstrap).GetField("controller", BindingFlags.NonPublic | BindingFlags.Static);
            if (controllerField == null) throw new MissingFieldException("Bootstrap.controller");
            object controller = controllerField.GetValue(null);
            if (controller == null) throw new InvalidOperationException("Runtime controller is unavailable.");
            return controller;
        }
        private static object Member(object owner, string name)
        {
            PropertyInfo p = owner.GetType().GetProperty(name, AnyInstance);
            if (p != null) return p.GetValue(owner, null);
            FieldInfo f = owner.GetType().GetField(name, AnyInstance);
            if (f != null) return f.GetValue(owner);
            throw new MissingMemberException(owner.GetType().FullName, name);
        }
        private NativeState ReadNative()
        {
            object save = Member(gameManager, "SaveData");
            return new NativeState { roofId = Text(Member(save, "RoofColorIndex")), hullId = Text(Member(save, "HullColorIndex")), flagId = Text(Member(save, "BoatFlagStyle")),
                hullTier = Text(Member(save, "HullTier")), hasChangedBoatColors = Text(Member(save, "HasChangedBoatColors")), bunting = Text(Member(save, "IsBoatBuntingEnabled")),
                isPlaying = Text(Member(gameManager, "IsPlaying")), isPaused = Text(Member(gameManager, "IsPaused")), canUnpause = Text(Member(gameManager, "CanUnpause")), timeScale = Time.timeScale };
        }
        private RendererState[] ReadRenderers()
        {
            return boats.Concat(flags).Distinct().Select(r =>
            {
                var skin = r as SkinnedMeshRenderer; var filter = r.GetComponent<MeshFilter>(); Mesh mesh = skin != null ? skin.sharedMesh : filter == null ? null : filter.sharedMesh;
                return new RendererState { id = r.GetInstanceID(), name = r.name, kind = r.GetType().Name, activeSelf = r.gameObject.activeSelf, activeInHierarchy = r.gameObject.activeInHierarchy, enabled = r.enabled,
                    mesh = mesh == null ? "" : mesh.name, vertexCount = mesh == null ? 0 : mesh.vertexCount, subMeshCount = mesh == null ? 0 : mesh.subMeshCount, meshBounds = mesh == null ? Vector3.zero : mesh.bounds.size,
                    materials = r.sharedMaterials.Select(ReadMaterial).ToArray(), lightOverrides = ReadLightOverrides(r) };
            }).ToArray();
        }
        private EffectiveState[] ReadEffectiveAppearance()
        {
            var states = new List<EffectiveState>();
            foreach (Renderer renderer in boats)
            {
                Material material = renderer.sharedMaterials[0]; var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
                states.Add(new EffectiveState { rendererId = renderer.GetInstanceID(), category = "boat",
                    hull = block.HasColor(Hull) ? block.GetColor(Hull) : material.GetColor(Hull), roof = block.HasColor(Roof) ? block.GetColor(Roof) : material.GetColor(Roof) });
            }
            foreach (Renderer renderer in flags)
            {
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
                Texture texture = block.HasTexture(Flag) ? block.GetTexture(Flag) : renderer.sharedMaterials[0].GetTexture(Flag);
                Texture2D copy = GpuExport.Read(texture);
                try
                {
                    Color32[] pixels = copy.GetPixels32(); byte[] bytes = new byte[pixels.Length * 4];
                    for (int i = 0; i < pixels.Length; i++) { bytes[i * 4] = pixels[i].r; bytes[i * 4 + 1] = pixels[i].g; bytes[i * 4 + 2] = pixels[i].b; bytes[i * 4 + 3] = pixels[i].a; }
                    using (SHA256 hash = SHA256.Create()) states.Add(new EffectiveState { rendererId = renderer.GetInstanceID(), category = "flag", flagRgbaSha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() });
                }
                finally { Object.Destroy(copy); }
            }
            return states.ToArray();
        }
        private static string[] ReadLightOverrides(Renderer renderer)
        {
            var values = new List<string>();
            for (int slot = -1; slot < renderer.sharedMaterials.Length; slot++)
            {
                var block = new MaterialPropertyBlock();
                if (slot < 0) renderer.GetPropertyBlock(block); else renderer.GetPropertyBlock(block, slot);
                values.Add(slot + ":_LightStrength=" + (block.HasFloat("_LightStrength") ? block.GetFloat("_LightStrength").ToString("R", CultureInfo.InvariantCulture) : "absent"));
            }
            return values.ToArray();
        }
        private static MaterialState ReadMaterial(Material material)
        {
            if (material == null) return new MaterialState { name = "null" };
            var props = new List<string>(); Shader shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i), kind = shader.GetPropertyType(i).ToString(), value;
                if (kind == "Color") { Color c = material.GetColor(name); value = Numbers(c.r, c.g, c.b, c.a); }
                else if (kind == "Vector") { Vector4 v = material.GetVector(name); value = Numbers(v.x, v.y, v.z, v.w); }
                else if (kind == "Texture")
                {
                    Texture texture = material.GetTexture(name); value = texture == null ? "null" : texture.GetInstanceID() + ":" + texture.name + ":" + texture.width + "x" + texture.height;
                    value += ":scale=" + material.GetTextureScale(name) + ":offset=" + material.GetTextureOffset(name);
                }
                else value = material.GetFloat(name).ToString("R", CultureInfo.InvariantCulture);
                props.Add(name + "|" + kind + "|" + value);
            }
            return new MaterialState { id = material.GetInstanceID(), name = material.name, shader = shader.name, renderQueue = material.renderQueue, keywords = material.shaderKeywords, properties = props.ToArray() };
        }
        private static string Numbers(params float[] values) { return string.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)).ToArray()); }
        private static string Text(object value) { return Convert.ToString(value, CultureInfo.InvariantCulture); }
        private static bool Close(Color a, Color b) { return Mathf.Abs(a.r - b.r) < 0.00001f && Mathf.Abs(a.g - b.g) < 0.00001f && Mathf.Abs(a.b - b.b) < 0.00001f && Mathf.Abs(a.a - b.a) < 0.00001f; }
        private static bool Same(object a, object b) { return ProbeJson.Same(a, b); }
    }
}
