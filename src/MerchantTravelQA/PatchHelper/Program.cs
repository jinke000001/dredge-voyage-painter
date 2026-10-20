using System.Text;
using System.Text.Json;
using Jinke.DredgeSkins.Weaver;
using Mono.Cecil;
using Mono.Cecil.Cil;
using WeaverProgram = Jinke.DredgeSkins.Weaver.Program;

const string ProbeName = "Jinke.DredgeMerchantTravel.QA";
const string ProbeType = "Jinke.DredgeMerchantTravel.QA.Probe";
var qaHooks = new Dictionary<string, string>
{
    ["System.Void PlayerColorCustomizer::RefreshBoatColors()"] = "AfterPlayer",
    ["System.Void ContinueOrNewButton::Start()"] = "AfterTitle"
};
try
{
    if (args.Length != 5) throw new InvalidOperationException("QA-ONLY usage: PatchHelper <original-game-dll> <release-patched-dll> <unchanged-release-runtime-dll> <native-probe-dll> <new-qa-output-dll>");
    string[] paths = args.Select(Path.GetFullPath).ToArray();
    string output = paths[4], auditPath = output + ".qa-only.audit.json";
    Require(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == paths.Length, "All five input/output paths must differ.");
    Require(!File.Exists(output) && !Directory.Exists(output) && !File.Exists(auditPath), "QA output or audit already exists.");
    Require(!output.Split(Path.DirectorySeparatorChar).Any(s => s.Equals("Contents", StringComparison.OrdinalIgnoreCase)), "Never patch directly into a native app package.");
    Require(output.Split(Path.DirectorySeparatorChar).Any(s => s.Contains("QA", StringComparison.OrdinalIgnoreCase) || s.Equals("MerchantTravelQA", StringComparison.OrdinalIgnoreCase)), "Output must be clearly named as QA-only.");
    for (DirectoryInfo? dir = new(Path.GetDirectoryName(output)!); dir != null; dir = dir.Parent)
        Require(dir.LinkTarget == null, "Output path must not contain symlinks.");
    Require(new FileInfo(output).LinkTarget == null && new FileInfo(auditPath).LinkTarget == null, "Output must not be a symlink.");
    byte[] originalBytes = File.ReadAllBytes(paths[0]), releaseBytes = File.ReadAllBytes(paths[1]), runtimeBytes = File.ReadAllBytes(paths[2]), probeBytes = File.ReadAllBytes(paths[3]);
    Require(WeaverProgram.Hash(originalBytes) == WeaverProgram.OriginalSha256, "Original game fingerprint mismatch.");
    using var original = Read(originalBytes); using var release = Read(releaseBytes); using var runtime = Read(runtimeBytes); using var probe = Read(probeBytes);
    Require(runtime.Name.Name == PatchProfile.MerchantTravel.RuntimeName && probe.Name.Name == ProbeName, "Wrong runtime/probe identity.");
    bool isVoyage = release.MainModule.Types.SelectMany(t => t.Methods).Where(m => m.HasBody)
        .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference mr &&
            mr.DeclaringType.FullName == PatchProfile.VoyageExperience.HookType && mr.Name == "AfterSaveDataInit" && mr.DeclaringType.Scope.Name == runtime.Name.Name);
    bool isPainter = release.MainModule.Types.SelectMany(t => t.Methods).Where(m => m.HasBody)
        .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference mr &&
            mr.DeclaringType.FullName == PatchProfile.VoyageExperience.HookType && mr.Name == "OnPainterFlag" && mr.DeclaringType.Scope.Name == runtime.Name.Name);
    var profile = isPainter ? PatchProfile.VoyagePainter : isVoyage ? PatchProfile.VoyageExperience : PatchProfile.MerchantTravel;
    Require(!isVoyage || runtime.MainModule.GetType("Jinke.DredgeVoyage.VoyageRuntime") != null, "Voyage patch requires its matching runtime.");
    var releaseDetails = profile.Hooks.Values.Select(spec => new Patcher.HookDetail(spec.Method, spec.Hook,
        Find(original.MainModule, spec.Method).Body.Instructions.Count(i => i.OpCode == OpCodes.Ret), 0, spec.Placement.ToString(), spec.Callee, spec.ExpectedCalls)).ToList();
    var releaseAudit = SemanticAudit.Verify(SemanticAudit.Capture(original), release, runtime.Name, releaseDetails, profile);
    Require(!release.MainModule.AssemblyReferences.Any(a => a.Name == ProbeName), "Already contains the QA probe.");
    var before = SemanticAudit.Capture(release);
    var bootstrap = probe.MainModule.GetType(ProbeType);
    Require(bootstrap != null && bootstrap.IsPublic, "Missing public QA probe type.");
    var reference = AssemblyNameReference.Parse(probe.Name.FullName); release.MainModule.AssemblyReferences.Add(reference);
    var probeType = new TypeReference("Jinke.DredgeMerchantTravel.QA", "Probe", release.MainModule, reference);
    foreach (var hook in qaHooks)
    {
        var signature = bootstrap!.Methods.Single(m => m.Name == hook.Value);
        Require(signature.IsPublic && signature.IsStatic && signature.HasBody && signature.ReturnType.FullName == "System.Void" && signature.Parameters.Count == 1 && signature.Parameters[0].ParameterType.FullName == "System.Object", "Wrong QA hook signature.");
        var callRef = new MethodReference(hook.Value, release.MainModule.TypeSystem.Void, probeType) { HasThis = false };
        callRef.Parameters.Add(new ParameterDefinition(release.MainModule.TypeSystem.Object));
        var target = Find(release.MainModule, hook.Key);
        Require(!target.Body.HasExceptionHandlers, "Unexpected exception regions.");
        var returns = target.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray();
        Require(returns.Length == 1, "Verified QA hook target must have exactly one return.");
        var load = Instruction.Create(OpCodes.Ldarg_0); var call = Instruction.Create(OpCodes.Call, callRef); var ret = returns[0];
        foreach (var i in target.Body.Instructions)
        {
            if (i.Operand == ret) i.Operand = load;
            else if (i.Operand is Instruction[] branches)
                for (int n = 0; n < branches.Length; n++) if (branches[n] == ret) branches[n] = load;
        }
        target.Body.GetILProcessor().InsertBefore(ret, load); target.Body.GetILProcessor().InsertBefore(ret, call);
    }
    using var stream = new MemoryStream(); release.Write(stream); byte[] resultBytes = stream.ToArray();
    using var result = Read(resultBytes);
    var after = SemanticAudit.Capture(result);
    Require(before.metadata == after.metadata, "QA patch changed definitions/resources.");
    Require(before.bodies.Keys.SequenceEqual(after.bodies.Keys), "QA patch changed method body set.");
    Require(before.bodies.Where(e => !qaHooks.ContainsKey(e.Key)).All(e => e.Value == after.bodies[e.Key]), "QA patch changed an unapproved method.");
    Require(after.references.Length == before.references.Length + 1 && before.references.Except(after.references).Count() == 0, "QA patch changed unexpected references.");
    foreach (var hook in qaHooks)
    {
        var rereadTarget = Find(result.MainModule, hook.Key);
        var finalRet = rereadTarget.Body.Instructions.Single(i => i.OpCode == OpCodes.Ret);
        var qaCall = finalRet.Previous; var qaLoad = qaCall.Previous;
        Require(qaCall.OpCode == OpCodes.Call && qaLoad.OpCode == OpCodes.Ldarg_0 && qaCall.Operand is MethodReference mr && mr.FullName == "System.Void Jinke.DredgeMerchantTravel.QA.Probe::" + hook.Value + "(System.Object)" && mr.DeclaringType.Scope.Name == ProbeName, "QA hook did not roundtrip correctly.");
        foreach (var i in rereadTarget.Body.Instructions)
        {
            Require(i.Operand != qaCall && i.Operand != finalRet, "Branch bypasses QA hook.");
            if (i.Operand == qaLoad) i.Operand = finalRet;
            else if (i.Operand is Instruction[] branches)
                for (int n = 0; n < branches.Length; n++) { Require(branches[n] != qaCall && branches[n] != finalRet, "Switch bypasses QA hook."); if (branches[n] == qaLoad) branches[n] = finalRet; }
        }
        rereadTarget.Body.Instructions.Remove(qaCall); rereadTarget.Body.Instructions.Remove(qaLoad);
    }
    result.MainModule.AssemblyReferences.Remove(result.MainModule.AssemblyReferences.Single(a => a.Name == ProbeName));
    var restored = SemanticAudit.Capture(result);
    Require(before.metadata == restored.metadata && before.bodies.SequenceEqual(restored.bodies) && before.references.SequenceEqual(restored.references), "Removing QA hook did not restore exact release semantics.");
    var report = new
    {
        QA_ONLY_DO_NOT_SHIP = true,
        releaseProfile = profile.Id,
        createdUtc = DateTimeOffset.UtcNow,
        originalSha256 = WeaverProgram.Hash(originalBytes), releasePatchSha256 = WeaverProgram.Hash(releaseBytes),
        unchangedReleaseRuntimeSha256 = WeaverProgram.Hash(runtimeBytes), probeSha256 = WeaverProgram.Hash(probeBytes), qaOutputSha256 = WeaverProgram.Hash(resultBytes),
        paths = new { original = paths[0], releasePatched = paths[1], runtime = paths[2], probe = paths[3], qaOutput = output },
        releaseAudit,
        qaAudit = new { passed = true, unchangedMethodBodies = before.bodies.Count - qaHooks.Count,
            changedMethods = qaHooks.Select(h => new { method = h.Key, addedCall = "Jinke.DredgeMerchantTravel.QA.Probe." + h.Value + "(object)",
                beforeBodySha256 = HashText(before.bodies[h.Key]), afterBodySha256 = HashText(after.bodies[h.Key]), restoredBodySha256 = HashText(restored.bodies[h.Key]) }).ToArray(),
            addedAssemblyReference = reference.FullName, releaseRuntimeUnmodified = WeaverProgram.Hash(File.ReadAllBytes(paths[2])) == WeaverProgram.Hash(runtimeBytes) }
    };
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    using var outputFile = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    using var auditFile = new FileStream(auditPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    outputFile.Write(resultBytes); outputFile.Flush(true);
    auditFile.Write(JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true })); auditFile.Flush(true);
    Console.WriteLine(JsonSerializer.Serialize(new { QA_ONLY_DO_NOT_SHIP = true, output, auditPath, unchangedBodies = before.bodies.Count - qaHooks.Count }));
    return 0;
}
catch (Exception e) { Console.Error.WriteLine("QA PATCH REFUSED: " + e.Message); return 1; }

static AssemblyDefinition Read(byte[] bytes) => AssemblyDefinition.ReadAssembly(new MemoryStream(bytes, false), new ReaderParameters { InMemory = true });
static MethodDefinition Find(ModuleDefinition module, string fullName) => module.Types.SelectMany(t => t.Methods).Single(m => m.FullName == fullName);
static string HashText(string value) => WeaverProgram.Hash(Encoding.UTF8.GetBytes(value));
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
