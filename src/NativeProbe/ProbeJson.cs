using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Jinke.DredgeSkins.NativeQA
{
    /// <summary>Only public instance fields of explicit report DTOs; never Unity normalized/linear/gamma properties.</summary>
    public static class ProbeJson
    {
        private sealed class FieldsOnlyResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization ignored)
            {
                return type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                    .Where(field => !field.IsNotSerialized).OrderBy(field => field.Name, StringComparer.Ordinal)
                    .Select(field => { JsonProperty property = base.CreateProperty(field, MemberSerialization.Fields); property.Readable = true; property.Writable = !field.IsInitOnly; return property; }).ToList();
            }
        }
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new FieldsOnlyResolver(), TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Error,
            MissingMemberHandling = MissingMemberHandling.Error, CheckAdditionalContent = true, MaxDepth = 32,
            Culture = CultureInfo.InvariantCulture
        };

        public static string Serialize(object value, bool pretty = false)
        {
            using (var text = new StringWriter(CultureInfo.InvariantCulture))
            using (var writer = new JsonTextWriter(text) { Formatting = pretty ? Formatting.Indented : Formatting.None })
            {
                JsonSerializer.Create(Settings).Serialize(writer, value); writer.Flush(); return text.ToString();
            }
        }
        public static T Deserialize<T>(string json)
        {
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
                return JsonSerializer.Create(Settings).Deserialize<T>(reader);
        }
        public static bool Same(object left, object right) { return JToken.DeepEquals(JToken.Parse(Serialize(left)), JToken.Parse(Serialize(right))); }
        public static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static void AssertDifferenceSensitivity()
        {
            var renderer = new RendererState { id = 17, name = "serialization-fixture", meshBounds = new UnityEngine.Vector3(1f, 2f, 3f),
                materials = new[] { new MaterialState { name = "fixture", properties = new[] { "_LightStrength|Float|0.2" } } }, lightOverrides = new[] { "1:_LightStrength=0.2" } };
            string original = Serialize(renderer); JObject tree = JObject.Parse(original);
            Need((float?)tree["meshBounds"]["y"] == 2f && tree["materials"][0]["properties"] is JArray && tree["meshBounds"]["normalized"] == null, "DTO fields not serialized exactly.");
            var copy = Deserialize<RendererState>(original); Need(Same(renderer, copy), "Renderer roundtrip differs.");
            copy.materials[0].properties[0] = "changed"; Need(!Same(renderer, copy), "Nested material differences are invisible.");
            copy = Deserialize<RendererState>(original); copy.lightOverrides[0] = "changed"; Need(!Same(renderer, copy), "Light override differences are invisible.");
            var effective = new EffectiveState { rendererId = 17, category = "fixture", hull = new UnityEngine.Color(0.2f, 0.3f, 0.4f, 1f), flagRgbaSha256 = "original" };
            var changed = Deserialize<EffectiveState>(Serialize(effective)); changed.hull.r = 0.7f;
            Need(!Same(effective, changed), "Effective color differences are invisible.");
            changed = Deserialize<EffectiveState>(Serialize(effective)); changed.flagRgbaSha256 = "changed";
            Need(!Same(effective, changed), "Flag hash differences are invisible.");
            var color = JObject.Parse(Serialize(effective))["hull"];
            Need(color is JObject && ((JObject)color).Count == 4 && color["linear"] == null, "Color includes unexpected properties.");
        }

        public static void VerifyReportShape(JObject tree, ProbeReport report)
        {
            Need((int?)tree["serializationVersion"] == 2, "Missing serialization version.");
            Need(tree["passed"] != null && tree["error"] != null && tree["skins"] is JArray, "Missing required root report fields.");
            Need(((JArray)tree["skins"]).Count == (report.skins == null ? 0 : report.skins.Length), "Skin array did not roundtrip.");
            if (report.nativeBefore != null) Need(tree["nativeBefore"] is JObject && tree["nativeBefore"]["roofId"] != null && tree["nativeBefore"]["timeScale"] != null, "Native before state missing.");
            if (report.nativeAfter != null) Need(tree["nativeAfter"] is JObject && tree["nativeAfter"]["flagId"] != null, "Native after state missing.");
            if (report.renderers != null)
            {
                Need(tree["renderers"] is JArray && ((JArray)tree["renderers"]).Count == report.renderers.Length, "Renderer array did not roundtrip.");
                foreach (JObject renderer in (JArray)tree["renderers"])
                {
                    Need(renderer["materials"] is JArray && renderer["lightOverrides"] is JArray, "Material/light arrays missing.");
                    Need(renderer["meshBounds"] is JObject && renderer["meshBounds"]["x"] != null && renderer["meshBounds"]["y"] != null && renderer["meshBounds"]["z"] != null, "Mesh bounds scalars missing.");
                    foreach (JObject material in (JArray)renderer["materials"]) Need(material["name"] != null && material["properties"] != null, "Material fields missing.");
                }
            }
            CheckEffective(tree["effectiveBefore"], report.effectiveBefore);
            CheckEffective(tree["effectiveAfter"], report.effectiveAfter);
            foreach (JObject skin in (JArray)tree["skins"]) Need(skin["flags"] is JArray && skin["nativeAfter"] is JObject, "Skin native/flag observations missing.");
            if (report.passed)
            {
                Need(report.nativeBefore != null && report.nativeAfter != null && report.renderers != null && report.renderers.Length == 25, "Passing report lacks full native/renderer evidence.");
                Need(report.skins.Length == 4 && report.skins.All(s => s.flags != null && s.flags.Length == 5), "Passing report lacks four skins/five flags.");
                Need(report.effectiveBefore != null && report.effectiveBefore.Length == 25 && report.effectiveAfter != null && report.effectiveAfter.Length == 25, "Passing report lacks full effective-state evidence.");
            }
        }

        private static void CheckEffective(JToken token, EffectiveState[] values)
        {
            if (values == null) { Need(token != null && token.Type == JTokenType.Null, "Null effective state missing."); return; }
            Need(token is JArray && ((JArray)token).Count == values.Length, "Effective state array did not roundtrip.");
            foreach (JObject state in (JArray)token)
            {
                Need(state["hull"] is JObject && state["hull"]["r"] != null && state["hull"]["g"] != null && state["hull"]["b"] != null && state["hull"]["a"] != null, "Effective RGBA fields missing.");
                Need(state["flagRgbaSha256"] != null, "Flag texture hash field missing.");
            }
        }

        public static void WriteVerifiedReport(string path, ProbeReport report)
        {
            string json = Serialize(report, true); VerifyReportShape(JObject.Parse(json), report);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json); file.Write(bytes, 0, bytes.Length); file.Flush(true);
            }
            string reread = File.ReadAllText(path); var restored = Deserialize<ProbeReport>(reread);
            Need(restored != null && Same(report, restored), "Report readback changed DTO fields.");
            VerifyReportShape(JObject.Parse(reread), restored);
            var verification = new Dictionary<string, object>
            {
                { "passed", true }, { "serializationVersion", 2 }, { "sha256", Sha256(File.ReadAllBytes(path)) },
                { "rendererCount", restored.renderers == null ? 0 : restored.renderers.Length }, { "skinCount", restored.skins.Length },
                { "effectiveBeforeCount", restored.effectiveBefore == null ? 0 : restored.effectiveBefore.Length },
                { "effectiveAfterCount", restored.effectiveAfter == null ? 0 : restored.effectiveAfter.Length }
            };
            using (var file = new FileStream(path + ".readback.json", FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(Serialize(verification, true)); file.Write(bytes, 0, bytes.Length); file.Flush(true);
            }
        }

        private static void Need(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    }
}
