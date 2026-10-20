using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jinke.DredgeSkins
{
    /// <summary>The production JSON and settings-file path, shared verbatim with managed regression tests.</summary>
    internal static class SkinJson
    {
        internal const int MaxSettingsBytes = 4096;
        private const int MaxDepth = 8;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static PackManifest LoadManifest(string directory)
        {
            JObject root = ReadObject(Path.Combine(directory, "manifest.json"), PackContract.MaxManifestBytes);
            RequireKeys(root, "schemaVersion", "skins");
            if (!HasSupportedSchema(root)) throw new InvalidDataException("皮肤清单版本不受支持。");
            JArray skins = root["skins"] as JArray;
            if (skins == null || skins.Count != 3) throw new InvalidDataException("皮肤清单必须包含 3 套皮肤。");
            foreach (JToken token in skins)
            {
                JObject skin = token as JObject;
                if (skin == null) throw new InvalidDataException("皮肤定义必须为对象。");
                RequireKeys(skin, "id", "name", "hullColor", "roofColor", "flagFile");
                RequireString(skin, "id", false);
                RequireString(skin, "name", false);
                RequireString(skin, "hullColor", false);
                RequireString(skin, "roofColor", false);
                RequireString(skin, "flagFile", false);
            }
            PackManifest manifest = Deserialize<PackManifest>(root);
            PackContract.Validate(manifest, directory);
            return manifest;
        }

        internal static SkinSettings ReadSettings(string path)
        {
            if (!File.Exists(path)) return new SkinSettings();
            JObject root = ReadObject(path, MaxSettingsBytes);
            RequireKeys(root, "schemaVersion", "boat", "flag");
            if (!HasSupportedSchema(root)) return new SkinSettings();
            RequireString(root, "boat", true);
            RequireString(root, "flag", true);
            SkinSettings settings = Deserialize<SkinSettings>(root);
            settings.boat = PackContract.NormalizeSelection(settings.boat);
            settings.flag = PackContract.NormalizeSelection(settings.flag);
            return settings;
        }

        internal static void WriteSettings(string path, SkinSettings settings)
        {
            if (settings == null || settings.schemaVersion != 1) throw new InvalidDataException("外观设置版本不受支持。");
            var normalized = new SkinSettings
            {
                boat = PackContract.NormalizeSelection(settings.boat),
                flag = PackContract.NormalizeSelection(settings.flag)
            };
            string json;
            using (var text = new StringWriter(CultureInfo.InvariantCulture))
            using (var writer = new JsonTextWriter(text) { Formatting = Formatting.Indented })
            {
                CreateSerializer().Serialize(writer, normalized);
                writer.Flush();
                json = text.ToString();
            }
            if (Utf8.GetByteCount(json) > MaxSettingsBytes) throw new InvalidDataException("外观设置文件过大。");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, Utf8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static JObject ReadObject(string path, int maxBytes)
        {
            try
            {
                // Bound the read itself, including a file that grows after it is opened.
                byte[] bytes = new byte[maxBytes + 1];
                int count = 0;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    while (count < bytes.Length)
                    {
                        int read = stream.Read(bytes, count, bytes.Length - count);
                        if (read == 0) break;
                        count += read;
                    }
                }
                if (count > maxBytes) throw new InvalidDataException("外观 JSON 文件过大。");
                int offset = count >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                string json = Utf8.GetString(bytes, offset, count - offset);
                using (var text = new StringReader(json))
                using (var reader = new JsonTextReader(text) { MaxDepth = MaxDepth, DateParseHandling = DateParseHandling.None })
                {
                    if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                        throw new InvalidDataException("外观 JSON 根节点必须为对象。");
                    JObject root = JObject.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Ignore
                    });
                    if (reader.Read()) throw new InvalidDataException("外观 JSON 包含多余内容。");
                    return root;
                }
            }
            catch (JsonException error) { throw new InvalidDataException("外观 JSON 格式无效。", error); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("外观 JSON 必须为有效 UTF-8。", error); }
        }

        private static bool HasSupportedSchema(JObject root)
        {
            JToken version = root["schemaVersion"];
            if (version == null || version.Type != JTokenType.Integer)
                throw new InvalidDataException("外观 JSON 必须明确声明整数 schemaVersion。");
            return version.ToString(Formatting.None) == "1";
        }

        private static void RequireKeys(JObject value, params string[] allowed)
        {
            foreach (JProperty property in value.Properties())
                if (property.Name.StartsWith("$", StringComparison.Ordinal) || Array.IndexOf(allowed, property.Name) < 0)
                    throw new InvalidDataException("外观 JSON 含有未支持的字段：" + property.Name);
        }

        private static void RequireString(JObject value, string name, bool optional)
        {
            JToken token = value[name];
            if (optional && (token == null || token.Type == JTokenType.Null)) return;
            if (token == null || token.Type != JTokenType.String)
                throw new InvalidDataException("外观 JSON 字段必须为字符串：" + name);
        }

        private static T Deserialize<T>(JObject root) where T : class
        {
            try
            {
                using (JsonReader reader = root.CreateReader())
                {
                    T value = CreateSerializer().Deserialize<T>(reader);
                    if (value == null) throw new InvalidDataException("外观 JSON 内容为空。");
                    return value;
                }
            }
            catch (JsonException error) { throw new InvalidDataException("外观 JSON 字段无效。", error); }
        }

        private static JsonSerializer CreateSerializer()
        {
            // Create, not CreateDefault: never inherit another component's global DefaultSettings.
            return JsonSerializer.Create(new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Error,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                DateParseHandling = DateParseHandling.None,
                MaxDepth = MaxDepth,
                CheckAdditionalContent = true,
                Culture = CultureInfo.InvariantCulture
            });
        }
    }
}
