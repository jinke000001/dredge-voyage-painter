using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

namespace Jinke.DredgeSkins.Weaver;

public static class Program
{
    public const string OriginalSha256 = "a071ffebe51514af5f661055f126a5041acda127623ee7c681c6ef2233a24729";
    public const string OriginalMvid = "d810863b-fda8-4056-bdde-77f7c349ae63";
    public const string RuntimeName = "Jinke.DredgeSkins";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 4 && args[0] == "--merchant-travel")
                Run(args[1], args[2], args[3], PatchProfile.MerchantTravel);
            else if (args.Length == 4 && args[0] == "--voyage-experience")
                Run(args[1], args[2], args[3], PatchProfile.VoyageExperience);
            else if (args.Length == 4 && args[0] == "--voyage-painter")
                Run(args[1], args[2], args[3], PatchProfile.VoyagePainter);
            else if (args.Length == 3 && !args[0].StartsWith("--", StringComparison.Ordinal))
                Run(args[0], args[1], args[2]);
            else
                throw new InvalidOperationException("Usage: Weaver [--merchant-travel|--voyage-experience|--voyage-painter] <input Assembly-CSharp.dll> <new output Assembly-CSharp.dll> <runtime.dll>");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("REFUSED: " + e.Message);
            return 1;
        }
    }

    public static void Run(string inputArg, string outputArg, string runtimeArg) =>
        Run(inputArg, outputArg, runtimeArg, PatchProfile.Skins);

    public static void Run(string inputArg, string outputArg, string runtimeArg, PatchProfile profile)
    {
        string root = FindWorkspace();
        string input = RealPath(inputArg), output = RealPath(outputArg), runtime = RealPath(runtimeArg);
        string auditPath = output + ".audit.json";
        Require(!SamePath(input, output), "Input and output must differ.");
        Require(!SamePath(runtime, output), "Runtime and output must differ.");
        Require(IsChild(output, RealPath(Path.Combine(root, "research"))) || IsChild(output, RealPath(Path.Combine(root, "output"))),
            "Output must be a new file under this workspace's research/ or output/ directory.");
        Require(!IsChild(output, RealPath(Path.Combine(root, "Contents"))), "The original Contents package is read-only.");
        Require(!PathExists(output) && !PathExists(auditPath), "Output or audit file already exists; overwriting is forbidden.");
        byte[] sourceBytes = File.ReadAllBytes(input), runtimeBytes = File.ReadAllBytes(runtime);
        Require(Hash(sourceBytes) == OriginalSha256, "Input fingerprint differs from the verified DREDGE 1.5.3 Mac original (or was already patched).");
        using var sourceStream = new MemoryStream(sourceBytes, false);
        using var runtimeStream = new MemoryStream(runtimeBytes, false);
        using var original = AssemblyDefinition.ReadAssembly(sourceStream, new ReaderParameters { InMemory = true });
        using var runtimeAssembly = AssemblyDefinition.ReadAssembly(runtimeStream, new ReaderParameters { InMemory = true });
        Require(original.Modules.Count == 1 && !original.Name.HasPublicKey, "Unexpected multi-module or signed input.");
        Require(original.MainModule.Mvid.ToString() == OriginalMvid, "Input module identity differs from the verified original.");
        Require(runtimeAssembly.Name.Name == profile.RuntimeName && runtimeAssembly.Modules.Count == 1, "Wrong runtime assembly identity for " + profile.Id + ".");
        Require(!original.MainModule.AssemblyReferences.Any(r => r.Name == profile.RuntimeName), "Runtime reference already exists; refusing a repeated patch.");
        var before = SemanticAudit.Capture(original);
        var details = Patcher.InsertHooks(original.MainModule, runtimeAssembly.MainModule, profile);
        using var patchedBytes = new MemoryStream();
        original.Write(patchedBytes, new WriterParameters { WriteSymbols = false });
        patchedBytes.Position = 0;
        using var reread = AssemblyDefinition.ReadAssembly(patchedBytes, new ReaderParameters { InMemory = true });
        var audit = SemanticAudit.Verify(before, reread, runtimeAssembly.Name, details, profile);
        byte[] result = patchedBytes.ToArray();
        var report = new
        {
            schemaVersion = 1,
            profile = profile.Id,
            verifiedUtc = DateTimeOffset.UtcNow,
            source = input,
            output,
            runtime,
            sourceSha256 = Hash(sourceBytes),
            outputSha256 = Hash(result),
            runtimeSha256 = Hash(runtimeBytes),
            sourceMvid = original.MainModule.Mvid,
            sourceGame = "DREDGE 1.5.3 Mac; fixed original fingerprint",
            validationScope = "Offline IL/metadata equivalence only; no native game execution or save validation.",
            audit
        };
        byte[] reportBytes = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        // CreateNew is the final overwrite guard. All validation finishes before either output is created.
        using var outputFile = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        bool auditCreated = false;
        try
        {
            using var auditFile = new FileStream(auditPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            auditCreated = true;
            outputFile.Write(result);
            outputFile.Flush(true);
            auditFile.Write(reportBytes);
            auditFile.Flush(true);
        }
        catch
        {
            outputFile.Dispose();
            File.Delete(output);
            if (auditCreated) File.Delete(auditPath);
            throw;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "verified", output, auditPath, audit.unchangedMethodBodies, hooks = details }));
    }

    private static string FindWorkspace()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? dir = new(start); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Contents/Resources/Data/Managed/Assembly-CSharp.dll")))
                    return RealPath(dir.FullName);
        }
        throw new InvalidOperationException("Cannot locate workspace containing the original Contents package.");
    }

    // Resolve each existing ancestor, including links to directories, before applying the output allowlist.
    internal static string RealPath(string path)
    {
        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full)!;
        foreach (string segment in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget != null)
                current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Cannot resolve output path symlink.");
        }
        return Path.GetFullPath(current);
    }

    private static bool IsChild(string path, string root) => path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool SamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget != null;
    public static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
