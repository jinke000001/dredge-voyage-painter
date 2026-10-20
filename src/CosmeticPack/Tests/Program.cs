using System;
using System.IO;
using System.Text;
using Jinke.DredgeSkins;
using Newtonsoft.Json;

internal static class Program
{
    private static int count;

    private static void Main(string[] args)
    {
        Assert(PackContract.NormalizeSelection(null) == "original", "missing selection uses original");
        Assert(PackContract.NormalizeSelection("pirate") == "original", "unknown selection uses original");
        Assert(new SkinSettings().boat == "original" && new SkinSettings().flag == "original", "first run changes no cosmetics");
        Assert(PackContract.NormalizeSelection("ramen") == "ramen", "known selection preserved");
        string root = Path.Combine(Path.GetTempPath(), "jinke-skins-contract-tests");
        Assert(PackContract.ResolveFlagPath(root, "moon/flag.png") == Path.Combine(root, "moon/flag.png"), "relative PNG allowed");
        Reject(() => PackContract.ResolveFlagPath(root, "../other.png"), "directory traversal rejected");
        Reject(() => PackContract.ResolveFlagPath(root, "/etc/other.png"), "absolute file rejected");
        Reject(() => PackContract.ResolveFlagPath(root, "moon/flag.jpg"), "non PNG rejected");
        Reject(() => PackContract.ResolveFlagPath(root, "..\\other.png"), "alternate separator rejected");
        var manifest = new PackManifest { schemaVersion = 1, skins = new[] { Skin("ramen"), Skin("octopus"), Skin("moon") } };
        PackContract.Validate(manifest, root);
        Assert(true, "complete manifest accepted");
        manifest.skins[2].id = "ramen";
        Reject(() => PackContract.Validate(manifest, root), "duplicate ID rejected");
        manifest.skins[2].id = "moon";
        manifest.skins[2].hullColor = "#FFFFFFFF";
        Reject(() => PackContract.Validate(manifest, root), "alpha colors rejected");
        PackContract.ValidatePng(PngHeader(1536, 1024));
        Assert(true, "3:2 PNG header accepted");
        Reject(() => PackContract.ValidatePng(PngHeader(2049, 1366)), "oversize PNG rejected before decode");
        Reject(() => PackContract.ValidatePng(PngHeader(256, 256)), "wrong aspect ratio rejected");
        Reject(() => PackContract.ValidatePng(new byte[64]), "invalid PNG rejected");
        AtlasPreservesPoleAndOrientation();
        LogIsBoundedAndFailureSafe();
        ProductionJsonRoundTripAndBounds();
        if (args.Length == 1) ValidateActualPack(Path.GetFullPath(args[0]));
        Console.WriteLine("JSON dependency: " + typeof(JsonConvert).Assembly.FullName);
        Console.WriteLine("JSON dependency file version: " + System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(JsonConvert).Assembly.Location).FileVersion);
        Console.WriteLine("PASS " + count + " pure managed assertions; no Unity process or game save access.");
    }

    private static void ProductionJsonRoundTripAndBounds()
    {
        string directory = Path.Combine(Path.GetTempPath(), "jinke-skins-json-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        string manifestPath = Path.Combine(directory, "manifest.json");
        const string validSettings = "{\"schemaVersion\":1,\"boat\":\"ramen\",\"flag\":\"moon\"}";
        const string validManifest = "{\"schemaVersion\":1,\"skins\":["
            + "{\"id\":\"ramen\",\"name\":\"深海拉面\",\"hullColor\":\"#D8BF94\",\"roofColor\":\"#8C4436\",\"flagFile\":\"ramen/flag.png\"},"
            + "{\"id\":\"octopus\",\"name\":\"罐头章鱼\",\"hullColor\":\"#76A49A\",\"roofColor\":\"#C49B55\",\"flagFile\":\"octopus/flag.png\"},"
            + "{\"id\":\"moon\",\"name\":\"月航信使\",\"hullColor\":\"#A398AD\",\"roofColor\":\"#4D3859\",\"flagFile\":\"moon/flag.png\"}]}";
        Func<JsonSerializerSettings> previousGlobalSettings = JsonConvert.DefaultSettings;
        try
        {
            SkinSettings firstRun = SkinJson.ReadSettings(path);
            Assert(firstRun.boat == "original" && firstRun.flag == "original" && !Directory.Exists(directory), "production missing-settings read stays original and creates no file");
            Directory.CreateDirectory(directory);
            File.WriteAllText(manifestPath, validManifest);
            PackManifest manifest = SkinJson.LoadManifest(directory);
            Assert(manifest.schemaVersion == 1 && manifest.skins.Length == 3 && manifest.skins[0].name == "深海拉面"
                && manifest.skins[1].id == "octopus" && manifest.skins[2].roofColor == "#4D3859", "production parser retains nested array, Chinese names and colors");

            JsonConvert.DefaultSettings = () => { throw new Exception("Global Newtonsoft settings must not be consulted."); };
            SkinJson.LoadManifest(directory);
            string[] choices = { "original", "ramen", "octopus", "moon" };
            foreach (string boat in choices)
                foreach (string flag in choices)
                {
                    SkinJson.WriteSettings(path, new SkinSettings { boat = boat, flag = flag });
                    SkinSettings actual = SkinJson.ReadSettings(path);
                    if (actual.schemaVersion != 1 || actual.boat != boat || actual.flag != flag)
                        throw new Exception("Production settings round trip lost a field.");
                }
            Assert(true, "production settings write/read preserves all 16 independent boat/flag combinations without global JSON settings");
            Assert(!File.ReadAllText(path).Contains("$type") && !File.Exists(path + ".tmp"), "production settings contain no type metadata and leave no temporary file");
            string saved = File.ReadAllText(path);
            Reject(() => SkinJson.WriteSettings(path, new SkinSettings { schemaVersion = 2 }), "unsupported settings write rejected");
            Assert(File.ReadAllText(path) == saved, "invalid settings write preserves the previous file");
            SkinJson.WriteSettings(path, new SkinSettings { boat = "unknown", flag = null });
            Assert(SkinJson.ReadSettings(path).boat == "original" && SkinJson.ReadSettings(path).flag == "original", "production writer normalizes unknown choices");
            JsonConvert.DefaultSettings = previousGlobalSettings;

            File.WriteAllText(path, "{\"schemaVersion\":1}");
            Assert(SkinJson.ReadSettings(path).boat == "original" && SkinJson.ReadSettings(path).flag == "original", "explicit schema with missing choices uses original");
            File.WriteAllText(path, "{\"schemaVersion\":2,\"boat\":\"ramen\",\"flag\":\"moon\"}");
            Assert(SkinJson.ReadSettings(path).boat == "original" && SkinJson.ReadSettings(path).flag == "original", "unsupported settings version safely uses original");
            File.WriteAllText(path, "{\"schemaVersion\":1,\"boat\":\"unknown\",\"flag\":null}");
            Assert(SkinJson.ReadSettings(path).boat == "original" && SkinJson.ReadSettings(path).flag == "original", "production reader normalizes unknown and null choices");

            Action<string, string> rejectSettings = (json, name) =>
            {
                File.WriteAllText(path, json);
                Reject(() => SkinJson.ReadSettings(path), name);
            };
            rejectSettings("{\"boat\":\"ramen\"}", "missing schema cannot inherit DTO initializer");
            rejectSettings("{\"schemaVersion\":\"1\"}", "string schema rejected");
            rejectSettings("{\"schemaVersion\":1.0}", "fractional schema rejected");
            rejectSettings("{\"schemaVersion\":1,\"boat\":3}", "numeric choice cannot coerce to text");
            rejectSettings("{\"schemaVersion\":1,\"schemaVersion\":1}", "duplicate JSON property rejected");
            rejectSettings("{\"schemaVersion\":1,\"$type\":\"System.IO.FileInfo, mscorlib\"}", "type metadata rejected before DTO creation");
            rejectSettings("{\"schemaVersion\":1,\"extra\":true}", "unexpected settings property rejected");
            rejectSettings("{\"SchemaVersion\":1}", "incorrectly cased fields rejected");
            rejectSettings(validSettings + " {}", "trailing JSON document rejected");
            rejectSettings("{", "truncated JSON rejected");
            rejectSettings("null", "null JSON root rejected");
            rejectSettings("[]", "array JSON root rejected");
            rejectSettings("{\"schemaVersion\":1,\"boat\":" + new string('[', 9) + "0" + new string(']', 9) + "}", "excessive JSON depth rejected");
            rejectSettings("{\"schemaVersion\":1,\"boat\":\"" + new string('测', 1400) + "\"}", "settings input bounded by UTF-8 bytes rather than characters");
            File.WriteAllBytes(path, new byte[] { 0x7b, 0x22, 0xc3, 0x28, 0x22, 0x3a, 0x31, 0x7d });
            Reject(() => SkinJson.ReadSettings(path), "invalid UTF-8 rejected without replacement characters");
            File.WriteAllText(path, "\ufeff" + validSettings);
            Assert(SkinJson.ReadSettings(path).flag == "moon", "UTF-8 BOM accepted");
            File.WriteAllText(path, validSettings + new string(' ', SkinJson.MaxSettingsBytes - Encoding.UTF8.GetByteCount(validSettings)));
            Assert(new FileInfo(path).Length == SkinJson.MaxSettingsBytes && SkinJson.ReadSettings(path).boat == "ramen", "settings at exact byte limit accepted");
            File.AppendAllText(path, " ");
            Reject(() => SkinJson.ReadSettings(path), "settings one byte above limit rejected");

            Action<string, string> rejectManifest = (json, name) =>
            {
                File.WriteAllText(manifestPath, json);
                Reject(() => SkinJson.LoadManifest(directory), name);
            };
            rejectManifest(validManifest.Replace("\"schemaVersion\":1,", ""), "production manifest requires explicit schema");
            rejectManifest("{\"schemaVersion\":1,\"skins\":null}", "production manifest rejects lost array field");
            rejectManifest(validManifest.Replace("\"name\":\"深海拉面\"", "\"name\":42"), "production manifest rejects numeric name coercion");
            rejectManifest(validManifest.Replace("\"id\":\"moon\"", "\"id\":\"ramen\""), "production manifest rejects duplicate IDs");
            rejectManifest(validManifest.Replace("ramen/flag.png", "../flag.png"), "production manifest enforces resource directory boundary");
            rejectManifest(validManifest.Replace("\"id\":\"ramen\"", "\"$type\":\"System.IO.FileInfo, mscorlib\",\"id\":\"ramen\""), "nested type metadata rejected");
            rejectManifest(validManifest.Replace("\"skins\":", "\"$type\":\"Other.Manifest\",\"skins\":"), "manifest root type metadata rejected");
            File.WriteAllText(manifestPath, validManifest + new string(' ', PackContract.MaxManifestBytes - Encoding.UTF8.GetByteCount(validManifest)));
            Assert(new FileInfo(manifestPath).Length == PackContract.MaxManifestBytes && SkinJson.LoadManifest(directory).skins.Length == 3, "manifest at exact byte limit retains three themes");
            File.AppendAllText(manifestPath, " ");
            Reject(() => SkinJson.LoadManifest(directory), "manifest one byte above limit rejected");
        }
        finally
        {
            JsonConvert.DefaultSettings = previousGlobalSettings;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void LogIsBoundedAndFailureSafe()
    {
        string directory = Path.Combine(Path.GetTempPath(), "jinke-skins-log-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "cosmetics.log");
        try
        {
            Assert(BoundedFileLog.Append(path, "ERROR", "initialization sentinel"), "own logger works without Unity logger");
            Assert(File.ReadAllText(path).Contains("initialization sentinel"), "initialization error retained");
            for (int i = 0; i < 40; i++)
                if (!BoundedFileLog.Append(path, "INFO", new string('测', 4096))) throw new Exception("Bounded log write failed.");
            Assert(new FileInfo(path).Length <= BoundedFileLog.MaxFileBytes && new FileInfo(path + ".previous").Length <= BoundedFileLog.MaxFileBytes, "current and previous logs remain bounded in UTF-8 bytes");
            Assert(BoundedFileLog.Append(path, "INFO", "latest sentinel") && File.ReadAllText(path).Contains("latest sentinel"), "latest action survives log rotation");
            Assert(!BoundedFileLog.Append(directory, "ERROR", "cannot write to a directory"), "log I/O error is contained");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void ValidateActualPack(string root)
    {
        PackManifest manifest = SkinJson.LoadManifest(root);
        Assert(manifest.skins.Length == 3 && manifest.skins[0].id == "ramen" && manifest.skins[0].name == "深海拉面"
            && manifest.skins[1].id == "octopus" && manifest.skins[1].hullColor == "#76A49A"
            && manifest.skins[2].id == "moon" && manifest.skins[2].flagFile == "moon/flag.png", "actual asset manifest retains all production DTO fields");
        foreach (SkinDefinition skin in manifest.skins)
        {
            PackContract.ValidatePng(File.ReadAllBytes(PackContract.ResolveFlagPath(root, skin.flagFile)));
            Assert(true, "actual " + skin.id + " PNG header satisfies runtime contract");
        }
    }

    private static SkinDefinition Skin(string id)
    {
        return new SkinDefinition { id = id, name = id, hullColor = "#ABCDEF", roofColor = "#123456", flagFile = id + "/flag.png" };
    }

    private static void AtlasPreservesPoleAndOrientation()
    {
        var original = new byte[256 * 256 * 4];
        for (int i = 0; i < original.Length; i++) original[i] = (byte)(i * 37 % 251);
        // Bottom row red, top row green. Unity pixels are bottom-up.
        byte[] flag = { 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255 };
        byte[] result = AtlasLayout.Compose(original, flag, 3, 2);
        for (int y = 0; y < AtlasLayout.ClothBottom; y++)
            for (int x = 0; x < 256 * 4; x++)
                if (result[y * 256 * 4 + x] != original[y * 256 * 4 + x]) throw new Exception("Pole byte changed.");
        Assert(true, "all 77824 pole-region bytes preserved exactly");
        Assert(result[(76 * 256) * 4] == 255 && result[(76 * 256) * 4 + 1] == 0, "cloth bottom stays red");
        Assert(result[(255 * 256 + 255) * 4] == 0 && result[(255 * 256 + 255) * 4 + 1] == 255, "cloth top stays green");
        Assert(original[76 * 256 * 4] != result[76 * 256 * 4], "original pixel array is not modified");
        RejectArgument(() => AtlasLayout.Compose(new byte[4], flag, 3, 2), "unknown original atlas rejected");
    }

    private static byte[] PngHeader(int width, int height)
    {
        var data = new byte[33];
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        Array.Copy(signature, data, 8);
        data[12] = (byte)'I'; data[13] = (byte)'H'; data[14] = (byte)'D'; data[15] = (byte)'R';
        for (int i = 0; i < 4; i++) { data[16 + i] = (byte)(width >> (24 - i * 8)); data[20 + i] = (byte)(height >> (24 - i * 8)); }
        return data;
    }

    private static void Reject(Action action, string name)
    {
        try { action(); } catch (InvalidDataException) { Assert(true, name); return; }
        throw new Exception("Expected invalid data: " + name);
    }

    private static void RejectArgument(Action action, string name)
    {
        try { action(); } catch (ArgumentException) { Assert(true, name); return; }
        throw new Exception("Expected invalid argument: " + name);
    }

    private static void Assert(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        count++;
        Console.WriteLine("PASS " + name);
    }
}
