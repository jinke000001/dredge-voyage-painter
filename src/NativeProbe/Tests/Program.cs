using Jinke.DredgeSkins.NativeQA;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

var tests = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); tests.Add(name); Console.WriteLine("PASS " + name); }
ProbeJson.AssertDifferenceSensitivity();
Check(true, "native DTO nested material/light/color/flag-hash changes detected");
var sample = new ProbeReport
{
    scope = "SERIALIZER FIXTURE ONLY; NOT NATIVE GAME EVIDENCE", passed = false,
    nativeBefore = new NativeState { roofId = "fixture", timeScale = 1f }, nativeAfter = new NativeState { roofId = "fixture", timeScale = 1f },
    renderers = new[] { new RendererState { id = 12, name = "fixture", meshBounds = new Vector3(1f, 2f, 3f),
        materials = new[] { new MaterialState { name = "fixture-material", properties = new[] { "float-property=0.1" } } }, lightOverrides = new[] { "light=0.1" } } },
    effectiveBefore = new[] { new EffectiveState { category = "fixture", rendererId = 12, hull = new Color(0.2f, 0.3f, 0.4f, 1f), flagRgbaSha256 = "fixture-hash" } },
    effectiveAfter = new[] { new EffectiveState { category = "fixture", rendererId = 12, hull = new Color(0.2f, 0.3f, 0.4f, 1f), flagRgbaSha256 = "fixture-hash" } },
    skins = Array.Empty<SkinResult>()
};
string json = ProbeJson.Serialize(sample);
var tree = JObject.Parse(json);
Check((float?)tree["renderers"]![0]!["meshBounds"]!["z"] == 3f, "Vector3 fields retain exact values");
Check(((JObject)tree["effectiveBefore"]![0]!["hull"]!).Count == 4, "Color contains only four RGBA fields");
Check(ProbeJson.Same(sample, ProbeJson.Deserialize<ProbeReport>(json)), "full report roundtrip preserves DTO fields");
var previous = JsonConvert.DefaultSettings;
try
{
    JsonConvert.DefaultSettings = () => new JsonSerializerSettings { Converters = { new RejectAllConverter() }, NullValueHandling = NullValueHandling.Ignore };
    Check(ProbeJson.Serialize(sample) == json, "external Newtonsoft global converters and null policies cannot alter report");
}
finally { JsonConvert.DefaultSettings = previous; }
string directory = Path.Combine(Directory.GetCurrentDirectory(), "src/NativeProbe/Tests/results", DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
Directory.CreateDirectory(directory);
string path = Path.Combine(directory, "fixture-report.json");
ProbeJson.WriteVerifiedReport(path, sample);
Check(File.Exists(path + ".readback.json") && (bool)JObject.Parse(File.ReadAllText(path + ".readback.json"))["passed"]!, "report file readback is verified with hash sidecar");
var incomplete = JObject.Parse(json); incomplete.Remove("renderers");
bool rejected = false;
try { ProbeJson.VerifyReportShape(incomplete, sample); } catch (InvalidDataException) { rejected = true; }
Check(rejected, "missing required renderer array is rejected");
rejected = false;
try { ProbeJson.VerifyReportShape(JObject.Parse("{\"passed\":true}"), new ProbeReport { passed = true }); } catch (InvalidDataException) { rejected = true; }
Check(rejected, "old or empty-object report cannot be accepted");
string sidecarDirectory = Path.Combine(directory, "output-sidecar-fixture");
Directory.CreateDirectory(sidecarDirectory);
File.WriteAllText(Path.Combine(sidecarDirectory, "._autostart"), "AppleDouble fixture");
File.WriteAllText(Path.Combine(sidecarDirectory, ".DS_Store"), "Finder fixture");
Check(!ProbeOutput.HasResultFiles(sidecarDirectory), "output containing only AppleDouble and Finder sidecars is accepted");
File.WriteAllText(Path.Combine(sidecarDirectory, "native-probe.json"), "existing result fixture");
Check(ProbeOutput.HasResultFiles(sidecarDirectory), "real result alongside system sidecars is rejected");
string hiddenResultDirectory = Path.Combine(directory, "output-hidden-result-fixture");
Directory.CreateDirectory(hiddenResultDirectory);
File.WriteAllText(Path.Combine(hiddenResultDirectory, ".prior-result.json"), "existing hidden result fixture");
Check(ProbeOutput.HasResultFiles(hiddenResultDirectory), "hidden non-system result file is still rejected");
string qaSettings = Path.Combine(directory, "qa-settings");
SettingsCheckContract.VerifyTarget("local.jinke.dredge.skins.qa.20261016", "Jinke Local Tests", "DREDGE Skins QA 20261016", qaSettings, Path.Combine(qaSettings, "settings.json"));
Check(true, "settings integration accepts exact QA identity and explicit matching qa-settings path");
foreach (var bad in new[]
{
    new[] { "production.identifier", qaSettings, Path.Combine(qaSettings, "settings.json") },
    new[] { "local.jinke.dredge.skins.qa.20261016", "qa-settings", Path.Combine(qaSettings, "settings.json") },
    new[] { "local.jinke.dredge.skins.qa.20261016", directory, Path.Combine(directory, "settings.json") },
    new[] { "local.jinke.dredge.skins.qa.20261016", qaSettings, Path.Combine(directory, "settings.json") }
})
{
    rejected = false;
    try { SettingsCheckContract.VerifyTarget(bad[0], "Jinke Local Tests", "DREDGE Skins QA 20261016", bad[1], bad[2]); } catch (InvalidOperationException) { rejected = true; }
    if (!rejected) throw new Exception("Unsafe settings integration target was accepted.");
}
Check(true, "settings integration rejects wrong identity, relative path, wrong directory name and mismatched production path");
Check(SettingsCheckContract.OnlySettingsKeys(new[] { "schemaVersion", "boat", "flag" }) && !SettingsCheckContract.OnlySettingsKeys(new[] { "schemaVersion", "boat", "flag", "$type" }), "settings file permits only three explicit fields and rejects metadata");
var effectiveOriginal = new[] { new EffectiveState { rendererId = 42, category = "boat", flagRgbaSha256 = "unchanged", hull = new Color(0.5098039f, 0.3738562f, 0.305882335f, 1f) } };
var effectiveRoundtrip = ProbeJson.Deserialize<EffectiveState[]>(ProbeJson.Serialize(effectiveOriginal));
effectiveRoundtrip[0].hull = new Color(0.5098038f, 0.373856157f, 0.3058823f, 1f);
Check(EffectiveAppearanceComparison.Equivalent(effectiveOriginal, effectiveRoundtrip, out double maxDelta) && maxDelta > 0d && maxDelta <= 0.000001d && !ProbeJson.Same(effectiveOriginal, effectiveRoundtrip), "effective-only comparison accepts observed float roundtrip while general Same stays exact");
foreach (string difference in new[] { "color", "alpha", "hash", "id", "category", "nan" })
{
    var changed = ProbeJson.Deserialize<EffectiveState[]>(ProbeJson.Serialize(effectiveOriginal));
    if (difference == "color") changed[0].hull.r += 0.0001f;
    if (difference == "alpha") changed[0].hull.a -= 0.0001f;
    if (difference == "hash") changed[0].flagRgbaSha256 = "different";
    if (difference == "id") changed[0].rendererId++;
    if (difference == "category") changed[0].category = "different";
    if (difference == "nan") changed[0].hull.r = float.NaN;
    if (EffectiveAppearanceComparison.Equivalent(effectiveOriginal, changed, out _)) throw new Exception("Effective comparison ignored " + difference);
}
Check(true, "effective comparison rejects meaningful RGB/alpha changes, texture hash, id, category and non-finite values");
var blankPixels = Enumerable.Repeat(new Color32(18, 23, 31, 255), PreviewPixels.Width * PreviewPixels.Height).ToArray();
Check(PreviewPixels.CountNonBackground(blankPixels) == 0, "uniform native QA05 background is rejected as a blank preview");
for (int i = 1; i <= PreviewPixels.MinimumVisiblePixels; i++) blankPixels[i] = new Color32(180, 85, 31, 255);
Check(PreviewPixels.CountNonBackground(blankPixels) >= PreviewPixels.MinimumVisiblePixels, "visible mesh-colored coverage is detected independently of file creation");
File.WriteAllText(Path.Combine(directory, "checks.json"), JsonConvert.SerializeObject(new { scope = "offline serializer and output guard regression tests only", passed = tests.Count, tests }, Formatting.Indented));
Console.WriteLine("RESULT " + directory);

sealed class RejectAllConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => true;
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, Newtonsoft.Json.JsonSerializer serializer) => throw new Exception("External converter must not run.");
    public override void WriteJson(JsonWriter writer, object? value, Newtonsoft.Json.JsonSerializer serializer) => throw new Exception("External converter must not run.");
}
