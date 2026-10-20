using System.Text.Json;
using TargetInvocationException = System.Reflection.TargetInvocationException;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using Jinke.DredgeSkins.Weaver;
using Mono.Cecil;
using Mono.Cecil.Cil;
using WeaverProgram = Jinke.DredgeSkins.Weaver.Program;

var results = new List<object>();
string root = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
string original = Path.Combine(root, "Contents/Resources/Data/Managed/Assembly-CSharp.dll");
string evidence = Path.Combine(root, "output/weaver-tests", DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(evidence);
string runtimePath = Path.Combine(evidence, "fixture-Jinke.DredgeSkins.dll");
using var runtime = Runtime();
runtime.Write(runtimePath);
string merchantRuntimePath = Path.Combine(evidence, "fixture-Jinke.DredgeMerchantTravel.dll");
using var merchantRuntime = Runtime(PatchProfile.MerchantTravel);
// Managed references in both directions are valid. Never Resolve() or execute the real game during weaving.
merchantRuntime.MainModule.AssemblyReferences.Add(new AssemblyNameReference("Assembly-CSharp", new Version(0, 0, 0, 0)));
merchantRuntime.Write(merchantRuntimePath);
string voyageRuntimePath = Path.Combine(evidence, "fixture-voyage-Jinke.DredgeMerchantTravel.dll");
using var voyageRuntime = Runtime(PatchProfile.VoyageExperience);
voyageRuntime.Write(voyageRuntimePath);
string painterRuntimePath = Path.Combine(evidence, "fixture-painter-Jinke.DredgeMerchantTravel.dll");
using var painterRuntime = Runtime(PatchProfile.VoyagePainter);
painterRuntime.Write(painterRuntimePath);
AssemblyDefinition RuntimeFor(PatchProfile profile) => profile == PatchProfile.Skins ? runtime :
    profile == PatchProfile.MerchantTravel ? merchantRuntime : profile == PatchProfile.VoyageExperience ? voyageRuntime : painterRuntime;

void Test(string name, Action run)
{
    run();
    results.Add(new { name, passed = true });
    Console.WriteLine("PASS " + name);
}
void Rejected(Action run, string contains)
{
    try { run(); }
    catch (InvalidOperationException e) when (e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase)) { return; }
    throw new Exception("Expected rejection containing: " + contains);
}
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
AssemblyDefinition Roundtrip(AssemblyDefinition source)
{
    var stream = new MemoryStream(); source.Write(stream); stream.Position = 0;
    return AssemblyDefinition.ReadAssembly(stream, new ReaderParameters { InMemory = true });
}
(AssemblyDefinition target, SemanticAudit.Snapshot before, List<Patcher.HookDetail> details) Fixture(PatchProfile? profile = null)
{
    profile ??= PatchProfile.Skins;
    bool voyage = profile == PatchProfile.VoyageExperience || profile == PatchProfile.VoyagePainter;
    string assemblyName = voyage ? "Assembly-CSharp" : "Fixture";
    using var fresh = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(assemblyName, new Version(1, 0)), assemblyName, ModuleKind.Dll);
    var module = fresh.MainModule;
    var type = new TypeDefinition("", "PlayerColorCustomizer", TypeAttributes.Class | TypeAttributes.Public, module.TypeSystem.Object);
    module.Types.Add(type);
    type.Fields.Add(new FieldDefinition("GameplayConstant", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal, module.TypeSystem.Int32) { Constant = 42 });
    foreach (string name in new[] { "RefreshBoatColors", "RefreshBoatFlag" })
    {
        var method = new MethodDefinition(name, MethodAttributes.Private, module.TypeSystem.Void);
        type.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        var ret1 = Instruction.Create(OpCodes.Ret); var ret2 = Instruction.Create(OpCodes.Ret);
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Brtrue_S, ret2));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Append(Instruction.Create(OpCodes.Switch, new[] { ret1, ret2 }));
        il.Append(ret1); il.Append(ret2);
    }
    var other = new MethodDefinition("Gameplay", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Int32);
    type.Methods.Add(other); other.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 42)); other.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    if (profile != PatchProfile.Skins)
    {
        var saves = new TypeDefinition("", "SaveManager", Mono.Cecil.TypeAttributes.Class | Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(saves);
        var writes = new FieldDefinition("SaveWrites", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static, module.TypeSystem.Int32);
        saves.Fields.Add(writes);
        var init = new MethodDefinition("Init", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
        saves.Methods.Add(init);
        init.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        init.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, writes));
        init.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var steam = new TypeDefinition("", "SteamManager", TypeAttributes.Class | TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(steam);
        var calls = new FieldDefinition("SteamCalls", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32);
        steam.Fields.Add(calls);
        var awake = new MethodDefinition("Awake", MethodAttributes.Public, module.TypeSystem.Void);
        steam.Methods.Add(awake);
        var start = Instruction.Create(OpCodes.Ldc_I4_1);
        var handler = Instruction.Create(OpCodes.Pop);
        var end = Instruction.Create(OpCodes.Ret);
        foreach (var instruction in new[] { start, Instruction.Create(OpCodes.Stsfld, calls), Instruction.Create(OpCodes.Leave_S, end),
            handler, Instruction.Create(OpCodes.Ldc_I4_2), Instruction.Create(OpCodes.Stsfld, calls), Instruction.Create(OpCodes.Leave_S, end), end })
            awake.Body.Instructions.Add(instruction);
        awake.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = start, TryEnd = handler, HandlerStart = handler, HandlerEnd = end,
            CatchType = new TypeReference("System", "Exception", module, module.TypeSystem.Object.Scope)
        });
        var ability = new TypeDefinition("", "AbilityBarUI", TypeAttributes.Class | TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(ability);
        var abilityCalls = new FieldDefinition("AbilityCalls", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32);
        ability.Fields.Add(abilityCalls);
        var complete = new MethodDefinition("OnCurrentPressComplete", MethodAttributes.Public, module.TypeSystem.Void);
        ability.Methods.Add(complete);
        foreach (var instruction in new[] { Instruction.Create(OpCodes.Ldsfld, abilityCalls), Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add), Instruction.Create(OpCodes.Stsfld, abilityCalls), Instruction.Create(OpCodes.Ret) })
            complete.Body.Instructions.Add(instruction);
    }
    if (voyage)
    {
        if (profile == PatchProfile.VoyagePainter)
        {
            var dialogue = new TypeDefinition("", "DredgeDialogueRunner", TypeAttributes.Public, module.TypeSystem.Object);
            module.Types.Add(dialogue);
            var calls = new FieldDefinition("Calls", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32);
            var node = new FieldDefinition("LastNode", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.String);
            dialogue.Fields.Add(calls); dialogue.Fields.Add(node);
            var awake = new MethodDefinition("Awake", MethodAttributes.Public, module.TypeSystem.Void);
            awake.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            dialogue.Methods.Add(awake);
            var start = new MethodDefinition("StartDialogue", MethodAttributes.Public, module.TypeSystem.Void);
            start.Parameters.Add(new ParameterDefinition("node", ParameterAttributes.None, module.TypeSystem.String));
            foreach (var instruction in new[] { Instruction.Create(OpCodes.Ldsfld, calls), Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Add), Instruction.Create(OpCodes.Stsfld, calls), Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Stsfld, node), Instruction.Create(OpCodes.Ret) }) start.Body.Instructions.Add(instruction);
            dialogue.Methods.Add(start);
        }
        var stats = new TypeDefinition("", "PlayerStats", TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(stats);
        var getter = new MethodDefinition("get_ResearchedEquipmentMaintenanceModifier", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Single);
        stats.Methods.Add(getter);
        getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R4, 0.25f));
        getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var unity = new AssemblyNameReference("UnityEngine.CoreModule", new Version(0, 0));
        module.AssemblyReferences.Add(unity);
        var deltaTime = new MethodReference("get_deltaTime", module.TypeSystem.Single,
            new TypeReference("UnityEngine", "Time", module, unity)) { HasThis = false };
        var localization = new AssemblyNameReference("Unity.Localization", new Version(1, 0));
        module.AssemblyReferences.Add(localization);
        var localize = new TypeReference("UnityEngine.Localization.Components", "LocalizeStringEvent", module, localization);
        var language = new TypeDefinition("", "LanguageManager", TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(language);
        var format = new MethodDefinition("FormatTimeStringForDurability", MethodAttributes.Public, module.TypeSystem.Void);
        foreach (var argument in new[] { module.TypeSystem.Single, module.TypeSystem.Single, localize, module.TypeSystem.String })
            format.Parameters.Add(new ParameterDefinition(argument));
        language.Methods.Add(format);
        var formatCalls = new FieldDefinition("Calls", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32);
        var current = new FieldDefinition("LastCurrent", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Single);
        var maximum = new FieldDefinition("LastMaximum", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Single);
        var text = new FieldDefinition("LastText", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.String);
        foreach (var field in new[] { formatCalls, current, maximum, text }) language.Fields.Add(field);
        foreach (var instruction in new[] { Instruction.Create(OpCodes.Ldsfld, formatCalls), Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add), Instruction.Create(OpCodes.Stsfld, formatCalls), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Stsfld, current),
            Instruction.Create(OpCodes.Ldarg_2), Instruction.Create(OpCodes.Stsfld, maximum), Instruction.Create(OpCodes.Ldarg, format.Parameters[3]),
            Instruction.Create(OpCodes.Stsfld, text), Instruction.Create(OpCodes.Ret) }) format.Body.Instructions.Add(instruction);
        TypeReference ParameterType(string name)
        {
            if (name == "System.Boolean") return module.TypeSystem.Boolean;
            if (name == "System.Single") return module.TypeSystem.Single;
            var existing = module.GetType(name);
            if (existing != null) return existing;
            var added = new TypeDefinition("", name, TypeAttributes.Public, module.TypeSystem.Object);
            module.Types.Add(added);
            return added;
        }
        foreach (var spec in PatchProfile.VoyageExperience.Hooks.Values.Where(s => !PatchProfile.MerchantTravel.Hooks.ContainsKey(s.Method)))
        {
            var identity = spec.Method.Split(new[] { ' ', ':', '(' }, StringSplitOptions.RemoveEmptyEntries);
            var owner = new TypeDefinition("", identity[1], TypeAttributes.Public, module.TypeSystem.Object);
            module.Types.Add(owner);
            bool pot = identity[1] == "SerializedCrabPotPOIData";
            var method = new MethodDefinition(identity[2], MethodAttributes.Public, pot ? module.TypeSystem.Boolean : module.TypeSystem.Void);
            owner.Methods.Add(method);
            foreach (string name in spec.Method[(spec.Method.IndexOf('(') + 1)..^1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                method.Parameters.Add(new ParameterDefinition(ParameterType(name)));
            if (spec.Placement == HookPlacement.AfterCallTransformSingle)
            {
                var value = new FieldDefinition("LastValue", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Single);
                owner.Fields.Add(value);
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, spec.CalleeAssembly == "UnityEngine.CoreModule" ? deltaTime : getter));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, value));
            }
            if (spec.Placement == HookPlacement.ReplaceCallWithOwner)
            {
                var manager = new FieldDefinition("Manager", FieldAttributes.Public, language);
                owner.Fields.Add(manager);
                for (int index = 0; index < spec.ExpectedCalls; index++)
                    foreach (var instruction in new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, manager),
                        Instruction.Create(OpCodes.Ldc_R4, 2f + index), Instruction.Create(OpCodes.Ldc_R4, 4f + index), Instruction.Create(OpCodes.Ldnull),
                        Instruction.Create(OpCodes.Ldstr, "duration"), Instruction.Create(OpCodes.Callvirt, format) }) method.Body.Instructions.Add(instruction);
            }
            if (identity[1] == "SaveData")
            {
                var start = Instruction.Create(OpCodes.Nop);
                var handler = Instruction.Create(OpCodes.Nop);
                var end = Instruction.Create(OpCodes.Nop);
                foreach (var instruction in new[] { start, Instruction.Create(OpCodes.Leave, end), handler, Instruction.Create(OpCodes.Endfinally), end })
                    method.Body.Instructions.Add(instruction);
                method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
                    { TryStart = start, TryEnd = handler, HandlerStart = handler, HandlerEnd = end });
            }
            if (pot) method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
    }
    var target = Roundtrip(fresh);
    var before = SemanticAudit.Capture(target);
    var details = Patcher.InsertHooks(target.MainModule, RuntimeFor(profile).MainModule, profile);
    return (target, before, details);
}
void AuditMutation(Action<AssemblyDefinition> mutate, string reason, PatchProfile? profile = null)
{
    profile ??= PatchProfile.Skins;
    var f = Fixture(profile); using var target = f.target;
    mutate(target);
    using var serialized = Roundtrip(target);
    Rejected(() => SemanticAudit.Verify(f.before, serialized, RuntimeFor(profile).Name, f.details, profile), reason);
}

Test("conditional and switch branches execute hooks at every return", () =>
{
    var f = Fixture(); using var target = f.target; using var serialized = Roundtrip(target);
    var result = SemanticAudit.Verify(f.before, serialized, runtime.Name, f.details);
    Check(result.passed && result.unchangedMethodBodies == 1, "Fixture audit failed.");
    Check(f.details.All(d => d.returns == 2 && d.redirectedBranches == 3), "Expected branch/switch redirection missing.");
});
Test("audit rejects unrelated gameplay IL change", () => AuditMutation(a =>
    a.MainModule.GetType("PlayerColorCustomizer").Methods.Single(m => m.Name == "Gameplay").Body.Instructions[0].Operand = 99, "Unapproved method"));
Test("audit rejects field constant change", () => AuditMutation(a =>
    a.MainModule.GetType("PlayerColorCustomizer").Fields[0].Constant = 99, "definitions/fields/constants"));
Test("audit rejects changes within a hooked method", () => AuditMutation(a =>
    a.MainModule.GetType("PlayerColorCustomizer").Methods[0].Body.Instructions[0].OpCode = OpCodes.Ldnull, "beyond the allowed hook"));
Test("audit rejects a branch bypassing a hook", () => AuditMutation(a =>
{
    var method = a.MainModule.GetType("PlayerColorCustomizer").Methods[0];
    method.Body.Instructions[1].Operand = method.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
}, "branch skips"));
Test("audit rejects an extra assembly reference", () => AuditMutation(a =>
    a.MainModule.AssemblyReferences.Add(new AssemblyNameReference("Unapproved", new Version(1, 0))), "exactly one"));
Test("audit rejects a missing hook", () => AuditMutation(a =>
{
    var method = a.MainModule.GetType("PlayerColorCustomizer").Methods[0];
    method.Body.Instructions.Remove(method.Body.Instructions.Last(i => i.OpCode == OpCodes.Call));
}, "Missing exact hook"));
Test("audit rejects resource changes", () => AuditMutation(a =>
    a.MainModule.Resources.Add(new EmbeddedResource("unapproved", ManifestResourceAttributes.Private, new byte[] { 1, 2, 3 })), "definitions/fields/constants"));
Test("patch rejects exception regions", () =>
{
    var f = Fixture(); using var target = f.target;
    var method = target.MainModule.GetType("PlayerColorCustomizer").Methods[0];
    method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally));
    Rejected(() => Patcher.InsertHooks(target.MainModule, runtime.MainModule), "exception regions");
});
Test("patch rejects an overflowing short branch", () =>
{
    using var fresh = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Overflow", new Version(1, 0)), "Overflow", ModuleKind.Dll);
    var module = fresh.MainModule;
    var type = new TypeDefinition("", "PlayerColorCustomizer", TypeAttributes.Public, module.TypeSystem.Object); module.Types.Add(type);
    foreach (string name in new[] { "RefreshBoatColors", "RefreshBoatFlag" })
    {
        var m = new MethodDefinition(name, MethodAttributes.Private, module.TypeSystem.Void); type.Methods.Add(m);
        var end = Instruction.Create(OpCodes.Ret);
        m.Body.Instructions.Add(Instruction.Create(OpCodes.Br_S, end));
        for (int i = 0; i < 122; i++) m.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
        m.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        m.Body.Instructions.Add(end);
    }
    Rejected(() => Patcher.InsertHooks(module, runtime.MainModule), "overflow a short branch");
});

string output = Path.Combine(evidence, "fixture-patched-Assembly-CSharp.dll");
Test("real fixed-fingerprint assembly passes whole-assembly audit with inert fixture runtime", () =>
{
    WeaverProgram.Run(original, output, runtimePath);
    using var audit = JsonDocument.Parse(File.ReadAllText(output + ".audit.json"));
    var result = audit.RootElement.GetProperty("audit");
    Check(result.GetProperty("passed").GetBoolean(), "Real assembly audit failed.");
    Check(result.GetProperty("changedMethods").GetArrayLength() == 2, "Wrong changed method count.");
});
Test("rejects input equals output", () => Rejected(() => WeaverProgram.Run(original, original, runtimePath), "must differ"));
Test("rejects output in original package", () => Rejected(() => WeaverProgram.Run(original,
    Path.Combine(root, "Contents/Resources/Data/Managed/forbidden.dll"), runtimePath), "research/ or output/"));
Test("rejects output outside approved directories", () => Rejected(() => WeaverProgram.Run(original,
    Path.Combine(root, "src/Weaver/forbidden.dll"), runtimePath), "research/ or output/"));
Test("rejects existing output", () => Rejected(() => WeaverProgram.Run(original, output, runtimePath), "already exists"));
Test("rejects existing audit sidecar", () =>
{
    string path = Path.Combine(evidence, "occupied-audit.dll"); File.WriteAllText(path + ".audit.json", "sentinel");
    Rejected(() => WeaverProgram.Run(original, path, runtimePath), "already exists");
    Check(!File.Exists(path), "Output leaked after sidecar refusal.");
});
Test("rejects repeat patch", () => Rejected(() => WeaverProgram.Run(output,
    Path.Combine(evidence, "repeat.dll"), runtimePath), "fingerprint differs"));
Test("rejects other game versions", () =>
{
    byte[] bad = File.ReadAllBytes(original); bad[^1] ^= 1;
    string path = Path.Combine(evidence, "modified-input.dll"); File.WriteAllBytes(path, bad);
    Rejected(() => WeaverProgram.Run(path, Path.Combine(evidence, "bad-version.dll"), runtimePath), "fingerprint differs");
});
Test("rejects symlink escape into original package", () =>
{
    string link = Path.Combine(evidence, "package-link"); Directory.CreateSymbolicLink(link, Path.Combine(root, "Contents"));
    Rejected(() => WeaverProgram.Run(original, Path.Combine(link, "forbidden.dll"), runtimePath), "research/ or output/");
});
Test("original assembly remains bit-identical", () => Check(WeaverProgram.Hash(File.ReadAllBytes(original)) == WeaverProgram.OriginalSha256, "Original changed."));

Test("merchant profile audits isolation prefixes, conditional ability prefix and owner postfix while preserving flag method", () =>
{
    var f = Fixture(PatchProfile.MerchantTravel); using var target = f.target; using var serialized = Roundtrip(target);
    var audit = SemanticAudit.Verify(f.before, serialized, merchantRuntime.Name, f.details, PatchProfile.MerchantTravel);
    Check(audit.passed && audit.unchangedMethodBodies == 2 && audit.changedMethods.Length == 4, "Merchant fixture audit failed.");
    Check(audit.changedMethods.Select(m => m.method).Order().SequenceEqual(PatchProfile.MerchantTravel.Hooks.Keys.Order()), "Unexpected hooked method set.");
    Check(f.details.Where(d => d.hook == "EnsureIsolatedProfile").Count(d => d.placement == "BeforeBody") == 2, "Save/Steam guards are not prefixes.");
    Check(f.details.Single(d => d.hook == "OnPlayer").redirectedBranches == 3, "Merchant return branches were not redirected.");
    Check(f.details.Single(d => d.hook == "BeforeAbilityComplete").placement == "ConditionalBeforeBody", "Missing conditional ability prefix.");
});
Test("conditional prefix passes the exact owner, skips on false and executes the original once on true in an executable fixture", () =>
{
    var f = Fixture(PatchProfile.MerchantTravel); using var target = f.target;
    var context = new AssemblyLoadContext("WeaverAbilityFixture", isCollectible: true);
    try
    {
        using var runtimeBytes = new MemoryStream(); merchantRuntime.Write(runtimeBytes); runtimeBytes.Position = 0;
        var loadedRuntime = context.LoadFromStream(runtimeBytes);
        using var targetBytes = new MemoryStream(); target.Write(targetBytes); targetBytes.Position = 0;
        var loadedTarget = context.LoadFromStream(targetBytes);
        var bootstrap = loadedRuntime.GetType(PatchProfile.MerchantTravel.HookType)!;
        var allow = bootstrap.GetField("AllowOriginal")!;
        var owner = bootstrap.GetField("LastOwner")!;
        var targetType = loadedTarget.GetType("AbilityBarUI")!;
        var calls = targetType.GetField("AbilityCalls")!;
        var entry = targetType.GetMethod("OnCurrentPressComplete")!;
        var instance = RuntimeHelpers.GetUninitializedObject(targetType);
        allow.SetValue(null, false);
        entry.Invoke(instance, null);
        Check(ReferenceEquals(owner.GetValue(null), instance) && (int)calls.GetValue(null)! == 0, "False did not consume the call before the original body.");
        allow.SetValue(null, true);
        entry.Invoke(instance, null);
        Check(ReferenceEquals(owner.GetValue(null), instance) && (int)calls.GetValue(null)! == 1, "True did not execute the exact original once.");
    }
    finally { context.Unload(); }
});
Test("conditional audit rejects inverted true/false behavior", () => AuditMutation(a =>
    a.MainModule.GetType("AbilityBarUI").Methods[0].Body.Instructions[2].OpCode = OpCodes.Brfalse,
    "Conditional prefix", PatchProfile.MerchantTravel));
Test("conditional audit rejects skipping the first original instruction", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("AbilityBarUI").Methods[0].Body;
    body.Instructions[2].Operand = body.Instructions[5];
}, "Conditional prefix", PatchProfile.MerchantTravel));
Test("conditional audit rejects a substituted owner", () => AuditMutation(a =>
    a.MainModule.GetType("AbilityBarUI").Methods[0].Body.Instructions[0].OpCode = OpCodes.Ldnull,
    "Conditional prefix", PatchProfile.MerchantTravel));
Test("conditional audit rejects missing consume return", () => AuditMutation(a =>
    a.MainModule.GetType("AbilityBarUI").Methods[0].Body.Instructions[3].OpCode = OpCodes.Nop,
    "Conditional prefix", PatchProfile.MerchantTravel));
Test("conditional audit rejects a void hook substitution", () => AuditMutation(a =>
{
    var method = a.MainModule.GetType("AbilityBarUI").Methods[0];
    ((MethodReference)method.Body.Instructions[1].Operand).ReturnType = a.MainModule.TypeSystem.Void;
}, "Incorrect hook target", PatchProfile.MerchantTravel));
Test("conditional audit rejects branch reentry into its injected return", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("AbilityBarUI").Methods[0].Body;
    body.Instructions.Insert(4, Instruction.Create(OpCodes.Br, body.Instructions[3]));
    body.Instructions[2].Operand = body.Instructions[4];
}, "re-enters", PatchProfile.MerchantTravel));
Test("conditional audit rejects changes to original activation history body", () => AuditMutation(a =>
    a.MainModule.GetType("AbilityBarUI").Methods[0].Body.Instructions[5].OpCode = OpCodes.Ldc_I4_2,
    "beyond the allowed hook", PatchProfile.MerchantTravel));
Test("merchant guard exception blocks original save and Steam instructions outside original catch in executable fixtures", () =>
{
    var f = Fixture(PatchProfile.MerchantTravel); using var target = f.target;
    var context = new AssemblyLoadContext("WeaverGuardFixture", isCollectible: true);
    try
    {
        using var runtimeBytes = new MemoryStream(); merchantRuntime.Write(runtimeBytes); runtimeBytes.Position = 0;
        var loadedRuntime = context.LoadFromStream(runtimeBytes);
        using var targetBytes = new MemoryStream(); target.Write(targetBytes); targetBytes.Position = 0;
        var loadedTarget = context.LoadFromStream(targetBytes);
        var reject = loadedRuntime.GetType("Jinke.DredgeMerchantTravel.Bootstrap")!.GetField("Reject")!;
        foreach (var (typeName, methodName, counter) in new[] { ("SaveManager", "Init", "SaveWrites"), ("SteamManager", "Awake", "SteamCalls") })
        {
            var targetType = loadedTarget.GetType(typeName)!;
            var writes = targetType.GetField(counter)!;
            var entry = targetType.GetMethod(methodName)!;
            var instance = RuntimeHelpers.GetUninitializedObject(targetType);
            reject.SetValue(null, true);
            bool refused = false;
            try { entry.Invoke(instance, null); }
            catch (TargetInvocationException e) when (e.InnerException is NullReferenceException) { refused = true; }
            Check(refused && (int)writes.GetValue(null)! == 0, "Failed guard allowed original " + typeName + " body or catch to execute.");
            reject.SetValue(null, false);
            entry.Invoke(instance, null);
            Check((int)writes.GetValue(null)! == 1, "Successful guard did not preserve original " + typeName + " body.");
        }
    }
    finally { context.Unload(); }
});
Test("merchant audit rejects a missing isolation prefix", () => AuditMutation(a =>
    a.MainModule.GetType("SaveManager").Methods[0].Body.Instructions.RemoveAt(0), "Missing exact hook", PatchProfile.MerchantTravel));
Test("merchant audit rejects an instruction ahead of the isolation prefix", () => AuditMutation(a =>
    a.MainModule.GetType("SaveManager").Methods[0].Body.Instructions.Insert(0, Instruction.Create(OpCodes.Nop)), "Missing exact hook", PatchProfile.MerchantTravel));
Test("merchant audit rejects changes within the original save body", () => AuditMutation(a =>
    a.MainModule.GetType("SaveManager").Methods[0].Body.Instructions[1].OpCode = OpCodes.Ldc_I4_2, "beyond the allowed hook", PatchProfile.MerchantTravel));
Test("merchant audit rejects branch reentry into the isolation prefix", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("SaveManager").Methods[0].Body;
    body.Instructions.Insert(1, Instruction.Create(OpCodes.Br, body.Instructions[0]));
}, "re-enters", PatchProfile.MerchantTravel));
Test("merchant audit rejects original exception region expanded around guard", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("SteamManager").Methods[0].Body;
    body.ExceptionHandlers[0].TryStart = body.Instructions[0];
}, "exception boundary includes", PatchProfile.MerchantTravel));
Test("merchant audit rejects a changed flag method", () => AuditMutation(a =>
    a.MainModule.GetType("PlayerColorCustomizer").Methods.Single(m => m.Name == "RefreshBoatFlag").Body.Instructions[0].OpCode = OpCodes.Ldnull,
    "Unapproved method body", PatchProfile.MerchantTravel));
Test("merchant audit rejects skins call substituted for player hook", () => AuditMutation(a =>
{
    var method = a.MainModule.GetType("PlayerColorCustomizer").Methods[0];
    ((MethodReference)method.Body.Instructions.First(i => i.OpCode == OpCodes.Call).Operand).Name = "OnColors";
}, "Incorrect hook target", PatchProfile.MerchantTravel));
Test("merchant audit rejects skin assembly dependency", () => AuditMutation(a =>
    a.MainModule.AssemblyReferences.Add(new AssemblyNameReference(WeaverProgram.RuntimeName, new Version(1, 0))), "exactly one", PatchProfile.MerchantTravel));
Test("merchant audit refuses the wrong profile", () =>
{
    var f = Fixture(PatchProfile.MerchantTravel); using var target = f.target;
    Rejected(() => SemanticAudit.Verify(f.before, target, merchantRuntime.Name, f.details), "Wrong runtime assembly identity");
});
Test("merchant runtime without a no-argument guard is refused", () =>
{
    using var invalid = Runtime(PatchProfile.MerchantTravel);
    invalid.MainModule.GetType(PatchProfile.MerchantTravel.HookType).Methods.Single(m => m.Name == "EnsureIsolatedProfile")
        .Parameters.Add(new ParameterDefinition(invalid.MainModule.TypeSystem.Object));
    var f = Fixture(PatchProfile.MerchantTravel); using var target = f.target;
    Rejected(() => Patcher.InsertHooks(target.MainModule, invalid.MainModule, PatchProfile.MerchantTravel), "EnsureIsolatedProfile()");
});
Test("merchant runtime with a void conditional hook is refused", () =>
{
    using var invalid = Runtime(PatchProfile.MerchantTravel);
    invalid.MainModule.GetType(PatchProfile.MerchantTravel.HookType).Methods.Single(m => m.Name == "BeforeAbilityComplete")
        .ReturnType = invalid.MainModule.TypeSystem.Void;
    var f = Fixture(PatchProfile.MerchantTravel); using var target = f.target;
    Rejected(() => Patcher.InsertHooks(target.MainModule, invalid.MainModule, PatchProfile.MerchantTravel), "bool BeforeAbilityComplete(object)");
});
string merchantOutput = Path.Combine(evidence, "merchant-fixture-patched-Assembly-CSharp.dll");
Test("merchant CLI accepts game-referencing runtime and audits exactly four hooks on original assembly", () =>
{
    Check(WeaverProgram.Main(["--merchant-travel", original, merchantOutput, merchantRuntimePath]) == 0, "Merchant CLI failed.");
    using var audit = JsonDocument.Parse(File.ReadAllText(merchantOutput + ".audit.json"));
    var report = audit.RootElement;
    var result = report.GetProperty("audit");
    Check(report.GetProperty("profile").GetString() == "merchant-travel" && report.GetProperty("sourceMvid").GetString() == WeaverProgram.OriginalMvid, "Missing profile or pinned MVID.");
    Check(result.GetProperty("passed").GetBoolean() && result.GetProperty("changedMethods").GetArrayLength() == 4, "Merchant original audit failed.");
    Check(result.GetProperty("unchangedMethodBodies").GetInt32() == result.GetProperty("totalMethodBodies").GetInt32() - 4, "Unexpected unchanged method count.");
    Check(result.GetProperty("addedAssemblyReferences").GetArrayLength() == 1 && result.GetProperty("addedAssemblyReferences")[0].GetString()!.StartsWith(PatchProfile.MerchantTravel.RuntimeName + ","), "Unexpected runtime dependency.");
});
Test("merchant runtime cannot be applied through default skins mode", () => Rejected(() => WeaverProgram.Run(original,
    Path.Combine(evidence, "wrong-default-profile.dll"), merchantRuntimePath), "Wrong runtime assembly identity"));
Test("skins runtime cannot be applied through merchant mode", () => Rejected(() => WeaverProgram.Run(original,
    Path.Combine(evidence, "wrong-merchant-profile.dll"), runtimePath, PatchProfile.MerchantTravel), "Wrong runtime assembly identity"));
Test("unknown CLI profile is refused before writing", () =>
    Check(WeaverProgram.Main(["--unknown", original, Path.Combine(evidence, "unknown.dll"), merchantRuntimePath]) == 1, "Unknown CLI profile was accepted."));
Test("merchant profile preserves output-path protection", () => Rejected(() => WeaverProgram.Run(original,
    Path.Combine(root, "Contents/Resources/Data/Managed/merchant-forbidden.dll"), merchantRuntimePath, PatchProfile.MerchantTravel), "research/ or output/"));
Test("original assembly remains bit-identical after both profiles", () => Check(WeaverProgram.Hash(File.ReadAllBytes(original)) == WeaverProgram.OriginalSha256, "Original changed."));

Test("voyage profile restores exactly the original bodies after stripping fixed call transforms", () =>
{
    Check(PatchProfile.VoyageExperience.Hooks.Count == 14, "Voyage must have exactly 14 approved target methods.");
    var f = Fixture(PatchProfile.VoyageExperience); using var target = f.target; using var serialized = Roundtrip(target);
    var audit = SemanticAudit.Verify(f.before, serialized, voyageRuntime.Name, f.details, PatchProfile.VoyageExperience);
    Check(audit.passed && audit.changedMethods.Length == PatchProfile.VoyageExperience.Hooks.Count, "Voyage fixture audit failed.");
    Check(f.details.Count(d => d.transformedCalls == 1 && d.callee == "System.Single PlayerStats::get_ResearchedEquipmentMaintenanceModifier()") == 2, "Fixed call transforms missing.");
});
Test("call transforms preserve input float, pass exact owner and resume the original body in executable fixtures", () =>
{
    var f = Fixture(PatchProfile.VoyageExperience); using var target = f.target;
    var context = new AssemblyLoadContext("WeaverVoyageFixture", isCollectible: true);
    try
    {
        using var runtimeBytes = new MemoryStream(); voyageRuntime.Write(runtimeBytes); runtimeBytes.Position = 0;
        var loadedRuntime = context.LoadFromStream(runtimeBytes);
        using var targetBytes = new MemoryStream(); target.Write(targetBytes); targetBytes.Position = 0;
        var loadedTarget = context.LoadFromStream(targetBytes);
        var lastOwner = loadedRuntime.GetType(PatchProfile.VoyageExperience.HookType)!.GetField("LastOwner")!;
        foreach (string typeName in new[] { "SerializedCrabPotPOIData", "TrawlNetAbility" })
        {
            var type = loadedTarget.GetType(typeName)!;
            var instance = RuntimeHelpers.GetUninitializedObject(type);
            var method = type.GetMethod(typeName == "TrawlNetAbility" ? "Update" : "AdjustDurability")!;
            method.Invoke(instance, typeName == "TrawlNetAbility" ? null : new object[] { 42f });
            Check(ReferenceEquals(lastOwner.GetValue(null), instance), "Transform received the wrong owner.");
            Check((float)type.GetField("LastValue")!.GetValue(null)! == 1.25f, "Transform did not receive original float or original body did not resume.");
        }
    }
    finally { context.Unload(); }
});
Test("transform audit rejects replacement owner", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("TrawlNetAbility").Methods[0].Body;
    body.Instructions[1].OpCode = OpCodes.Ldnull;
}, "exact owner", PatchProfile.VoyageExperience));
Test("transform audit rejects a branch into the hook", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("TrawlNetAbility").Methods[0].Body;
    body.Instructions.Insert(0, Instruction.Create(OpCodes.Br, body.Instructions[1]));
}, "branch enters", PatchProfile.VoyageExperience));
Test("transform audit rejects changed callee", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("TrawlNetAbility").Methods[0].Body;
    var originalCall = (MethodReference)body.Instructions[0].Operand;
    body.Instructions[0].Operand = new MethodReference("UnexpectedGetter", originalCall.ReturnType, originalCall.DeclaringType) { HasThis = originalCall.HasThis };
}, "callee count", PatchProfile.VoyageExperience));
Test("transform audit rejects wrong hook return type", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("TrawlNetAbility").Methods[0].Body;
    ((MethodReference)body.Instructions[2].Operand).ReturnType = a.MainModule.TypeSystem.Double;
}, "Incorrect hook target", PatchProfile.VoyageExperience));
Test("transform audit rejects original calculation mutation", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("TrawlNetAbility").Methods[0].Body;
    body.GetILProcessor().InsertBefore(body.Instructions[3], Instruction.Create(OpCodes.Neg));
}, "beyond the allowed hook", PatchProfile.VoyageExperience));
Test("typed wrapper forwards every original argument exactly once per call and receives the exact UI owner", () =>
{
    var f = Fixture(PatchProfile.VoyageExperience); using var target = f.target;
    var context = new AssemblyLoadContext("WeaverWrapperFixture", isCollectible: true);
    try
    {
        using var localization = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Unity.Localization", new Version(1, 0)), "Unity.Localization", ModuleKind.Dll);
        localization.MainModule.Types.Add(new TypeDefinition("UnityEngine.Localization.Components", "LocalizeStringEvent", TypeAttributes.Public, localization.MainModule.TypeSystem.Object));
        using var localizationBytes = new MemoryStream(); localization.Write(localizationBytes); localizationBytes.Position = 0;
        context.LoadFromStream(localizationBytes);
        using var runtimeBytes = new MemoryStream(); voyageRuntime.Write(runtimeBytes); runtimeBytes.Position = 0;
        var loadedRuntime = context.LoadFromStream(runtimeBytes);
        using var targetBytes = new MemoryStream(); target.Write(targetBytes); targetBytes.Position = 0;
        var loadedTarget = context.LoadFromStream(targetBytes);
        var language = loadedTarget.GetType("LanguageManager")!;
        var bootstrap = loadedRuntime.GetType(PatchProfile.VoyageExperience.HookType)!;
        foreach (var owner in new[] { (Type: "TooltipSectionDeployableDetails", Method: "RefreshUI", Calls: 2),
            (Type: "OccasionalGridPanel", Method: "RefreshDurabilityUI", Calls: 1), (Type: "NetPanel", Method: "RefreshUI", Calls: 1) })
        {
            var type = loadedTarget.GetType(owner.Type)!;
            var instance = RuntimeHelpers.GetUninitializedObject(type);
            type.GetField("Manager")!.SetValue(instance, RuntimeHelpers.GetUninitializedObject(language));
            language.GetField("Calls")!.SetValue(null, 0);
            type.GetMethod(owner.Method)!.Invoke(instance, null);
            Check(ReferenceEquals(bootstrap.GetField("LastOwner")!.GetValue(null), instance), owner.Type + ": wrapper received wrong owner.");
            Check((int)language.GetField("Calls")!.GetValue(null)! == owner.Calls, owner.Type + ": wrapper must call original formatter once per original call.");
            Check((float)language.GetField("LastCurrent")!.GetValue(null)! == 1f + owner.Calls && (float)language.GetField("LastMaximum")!.GetValue(null)! == 3f + owner.Calls &&
                (string)language.GetField("LastText")!.GetValue(null)! == "duration", owner.Type + ": original wrapper arguments were changed.");
        }
    }
    finally { context.Unload(); }
});
Test("wrapper audit rejects substituted owner", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("TooltipSectionDeployableDetails").Methods[0].Body;
    var call = body.Instructions.First(i => i.Operand is MethodReference m && m.Name == "FormatDeployableDurability");
    call.Previous.OpCode = OpCodes.Ldnull;
}, "exact owner", PatchProfile.VoyageExperience));
Test("wrapper audit rejects a branch that skips owner loading", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("TooltipSectionDeployableDetails").Methods[0].Body;
    var call = body.Instructions.First(i => i.Operand is MethodReference m && m.Name == "FormatDeployableDurability");
    body.GetILProcessor().InsertBefore(call.Previous, Instruction.Create(OpCodes.Br, call));
}, "skips the wrapper owner", PatchProfile.VoyageExperience));
Test("wrapper audit rejects a missing wrapped call", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("TooltipSectionDeployableDetails").Methods[0].Body;
    var call = body.Instructions.First(i => i.Operand is MethodReference m && m.Name == "FormatDeployableDurability");
    call.OpCode = OpCodes.Nop; call.Operand = null;
}, "wrapper hook count", PatchProfile.VoyageExperience));
Test("wrapper audit rejects changed original argument", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("TooltipSectionDeployableDetails").Methods[0].Body;
    body.Instructions.First(i => i.OpCode == OpCodes.Ldc_R4).Operand = 99f;
}, "beyond the allowed hook", PatchProfile.VoyageExperience));
Test("return audit refuses an exception region ending at the injected call", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("SaveData").Methods[0].Body;
    body.ExceptionHandlers[0].HandlerEnd = body.Instructions[^2];
}, "boundary enters", PatchProfile.VoyageExperience));
Test("return audit refuses a return on an exception boundary", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("SaveData").Methods[0].Body;
    body.ExceptionHandlers[0].HandlerEnd = body.Instructions[^1];
}, "outside exception", PatchProfile.VoyageExperience));
string voyageOutput = Path.Combine(evidence, "voyage-fixture-patched-Assembly-CSharp.dll");
Test("native crab pot and net panels each have exactly one matching formatter call", () =>
{
    using var native = AssemblyDefinition.ReadAssembly(original);
    foreach (string signature in new[] { "System.Void OccasionalGridPanel::RefreshDurabilityUI()", "System.Void NetPanel::RefreshUI()" })
    {
        var spec = PatchProfile.VoyageExperience.Hooks[signature];
        var method = native.MainModule.Types.SelectMany(t => t.Methods).Single(m => m.FullName == signature);
        var calls = method.Body.Instructions.Where(i => i.Operand is MethodReference m && m.FullName == spec.Callee).ToArray();
        Check(spec.Placement == HookPlacement.ReplaceCallWithOwner && spec.Hook == "FormatDeployableDurability" && spec.ExpectedCalls == 1,
            "Wrong fixed panel wrapper contract: " + signature);
        Check(calls.Length == 1 && calls[0].OpCode == OpCodes.Callvirt && ((MethodReference)calls[0].Operand).DeclaringType.Scope is ModuleDefinition scope && scope.Assembly.Name.Name == spec.CalleeAssembly,
            "Wrong native panel call count or identity: " + signature);
        Check(method.Body.MaxStackSize == 5, "Unexpected native panel MaxStack: " + signature);
    }
});
Test("native panel wrapper refuses a missing or duplicate formatter before writing", () =>
{
    foreach (string signature in new[] { "System.Void OccasionalGridPanel::RefreshDurabilityUI()", "System.Void NetPanel::RefreshUI()" })
    foreach (bool duplicate in new[] { false, true })
    {
        using var native = AssemblyDefinition.ReadAssembly(original);
        var spec = PatchProfile.VoyageExperience.Hooks[signature];
        var method = native.MainModule.Types.SelectMany(t => t.Methods).Single(m => m.FullName == signature);
        var call = method.Body.Instructions.Single(i => i.Operand is MethodReference m && m.FullName == spec.Callee);
        if (duplicate) method.Body.GetILProcessor().InsertAfter(call, Instruction.Create(OpCodes.Callvirt, (MethodReference)call.Operand));
        else { call.OpCode = OpCodes.Nop; call.Operand = null; }
        Rejected(() => Patcher.InsertHooks(native.MainModule, voyageRuntime.MainModule, PatchProfile.VoyageExperience), "wrapper callee opcode or count");
    }
});
Test("voyage CLI adds only the approved hooks to the fixed original assembly", () =>
{
    Check(WeaverProgram.Main(["--voyage-experience", original, voyageOutput, voyageRuntimePath]) == 0, "Voyage CLI failed.");
    using var document = JsonDocument.Parse(File.ReadAllText(voyageOutput + ".audit.json"));
    var report = document.RootElement;
    Check(report.GetProperty("profile").GetString() == "voyage-experience", "Wrong voyage profile.");
    var audit = report.GetProperty("audit");
    Check(audit.GetProperty("changedMethods").GetArrayLength() == PatchProfile.VoyageExperience.Hooks.Count, "Wrong voyage hooks.");
    var headers = audit.GetProperty("maxStackHeaderChanges").EnumerateArray().ToArray();
    Check(headers.Length == 6 && headers.All(h => h.GetProperty("patched").GetInt32() == h.GetProperty("original").GetInt32() + 1 &&
        PatchProfile.VoyageExperience.Hooks[h.GetProperty("method").GetString()!].ExpectedCalls > 0), "Unexpected IL stack header changes.");
});
Test("original assembly remains bit-identical after voyage profile", () => Check(WeaverProgram.Hash(File.ReadAllBytes(original)) == WeaverProgram.OriginalSha256, "Original changed."));

Test("painter profile inherits every voyage hook and adds only flag and dialogue lifecycle hooks", () =>
{
    Check(PatchProfile.Skins.Hooks.Count == 2 && PatchProfile.MerchantTravel.Hooks.Count == 4 && PatchProfile.VoyageExperience.Hooks.Count == 14,
        "A historical profile changed.");
    Check(PatchProfile.VoyagePainter.Hooks.Count == 17, "Painter must target exactly seventeen methods.");
    foreach (var hook in PatchProfile.VoyageExperience.Hooks)
        Check(PatchProfile.VoyagePainter.Hooks[hook.Key] == hook.Value, "Painter changed an inherited hook: " + hook.Key);
    var flag = PatchProfile.VoyagePainter.Hooks["System.Void PlayerColorCustomizer::RefreshBoatFlag()"];
    Check(flag.Hook == "OnPainterFlag" && flag.Placement == HookPlacement.BeforeReturn, "Wrong native flag refresh hook.");
    var awake = PatchProfile.VoyagePainter.Hooks["System.Void DredgeDialogueRunner::Awake()"];
    var start = PatchProfile.VoyagePainter.Hooks["System.Void DredgeDialogueRunner::StartDialogue(System.String)"];
    Check(awake.Hook == "OnPainterRunnerAwake" && awake.Placement == HookPlacement.BeforeReturn, "Wrong dialogue project installation hook.");
    Check(start.Hook == "BeforePainterDialogue" && start.Placement == HookPlacement.BeforeBodyWithOwner, "Wrong localized dialogue preparation hook.");
    var f = Fixture(PatchProfile.VoyagePainter); using var target = f.target; using var serialized = Roundtrip(target);
    var audit = SemanticAudit.Verify(f.before, serialized, painterRuntime.Name, f.details, PatchProfile.VoyagePainter);
    Check(audit.passed && audit.changedMethods.Length == 17, "Painter fixture did not restore all original bodies.");
    var detail = f.details.Single(d => d.method == flag.Method);
    Check(detail.returns == 2 && detail.redirectedBranches == 3, "Painter flag hook skipped an early/switch return.");
});
string painterOutput = Path.Combine(evidence, "painter-fixture-patched-Assembly-CSharp.dll");
Test("painter CLI audits exactly seventeen methods and the unchanged voyage stack adjustments", () =>
{
    Check(WeaverProgram.Main(["--voyage-painter", original, painterOutput, painterRuntimePath]) == 0, "Painter CLI failed.");
    using var document = JsonDocument.Parse(File.ReadAllText(painterOutput + ".audit.json"));
    var report = document.RootElement;
    var audit = report.GetProperty("audit");
    Check(report.GetProperty("profile").GetString() == "voyage-painter" && audit.GetProperty("passed").GetBoolean(), "Wrong painter audit profile.");
    var names = audit.GetProperty("changedMethods").EnumerateArray().Select(m => m.GetProperty("method").GetString()!).ToHashSet(StringComparer.Ordinal);
    Check(names.SetEquals(PatchProfile.VoyagePainter.Hooks.Keys), "Painter changed a method outside its fixed profile.");
    using var voyageDocument = JsonDocument.Parse(File.ReadAllText(voyageOutput + ".audit.json"));
    Check(audit.GetProperty("maxStackHeaderChanges").GetRawText() == voyageDocument.RootElement.GetProperty("audit").GetProperty("maxStackHeaderChanges").GetRawText(),
        "Painter altered stack headers beyond the original voyage transforms.");
});
Test("painter dialogue prefix passes the exact owner and preserves the original node argument once", () =>
{
    var f = Fixture(PatchProfile.VoyagePainter); using var target = f.target;
    var context = new AssemblyLoadContext("WeaverPainterFixture", isCollectible: true);
    try
    {
        using var runtimeBytes = new MemoryStream(); painterRuntime.Write(runtimeBytes); runtimeBytes.Position = 0;
        var loadedRuntime = context.LoadFromStream(runtimeBytes);
        using var targetBytes = new MemoryStream(); target.Write(targetBytes); targetBytes.Position = 0;
        var loadedTarget = context.LoadFromStream(targetBytes);
        var dialogue = loadedTarget.GetType("DredgeDialogueRunner")!;
        var instance = RuntimeHelpers.GetUninitializedObject(dialogue);
        dialogue.GetMethod("StartDialogue")!.Invoke(instance, new object[] { "Painter_Custom_Node" });
        Check(ReferenceEquals(loadedRuntime.GetType(PatchProfile.VoyagePainter.HookType)!.GetField("LastOwner")!.GetValue(null), instance),
            "Painter dialogue prefix received a substituted owner.");
        Check((int)dialogue.GetField("Calls")!.GetValue(null)! == 1 && (string)dialogue.GetField("LastNode")!.GetValue(null)! == "Painter_Custom_Node",
            "Painter prefix altered or repeated the original dialogue call.");
    }
    finally { context.Unload(); }
});
Test("painter dialogue audit rejects a substituted prefix owner", () => AuditMutation(a =>
{
    a.MainModule.GetType("DredgeDialogueRunner").Methods.Single(m => m.Name == "StartDialogue").Body.Instructions[0].OpCode = OpCodes.Ldnull;
}, "exact owner", PatchProfile.VoyagePainter));
Test("painter dialogue audit rejects branch reentry into its prefix", () => AuditMutation(a =>
{
    var body = a.MainModule.GetType("DredgeDialogueRunner").Methods.Single(m => m.Name == "StartDialogue").Body;
    body.GetILProcessor().InsertBefore(body.Instructions[^1], Instruction.Create(OpCodes.Br, body.Instructions[0]));
}, "re-enters", PatchProfile.VoyagePainter));
Test("painter profile refuses an old voyage runtime missing its flag hook", () =>
    Rejected(() => WeaverProgram.Run(original, Path.Combine(evidence, "painter-old-runtime.dll"), voyageRuntimePath, PatchProfile.VoyagePainter), "OnPainterFlag"));
Test("painter profile refuses a skins runtime identity", () =>
    Rejected(() => WeaverProgram.Run(original, Path.Combine(evidence, "painter-wrong-runtime.dll"), runtimePath, PatchProfile.VoyagePainter), "Wrong runtime assembly identity"));
Test("painter patch cannot be chained onto an already patched voyage assembly", () =>
    Rejected(() => WeaverProgram.Run(voyageOutput, Path.Combine(evidence, "painter-chained.dll"), painterRuntimePath, PatchProfile.VoyagePainter), "Input fingerprint"));
Test("painter profile refuses to overwrite existing artifacts", () =>
{
    string before = WeaverProgram.Hash(File.ReadAllBytes(painterOutput));
    Rejected(() => WeaverProgram.Run(original, painterOutput, painterRuntimePath, PatchProfile.VoyagePainter), "overwriting");
    Check(WeaverProgram.Hash(File.ReadAllBytes(painterOutput)) == before, "Painter overwrite refusal changed an existing output.");
});
Test("original assembly remains bit-identical after painter profile", () => Check(WeaverProgram.Hash(File.ReadAllBytes(original)) == WeaverProgram.OriginalSha256, "Original changed."));

string reportPath = Path.Combine(evidence, "test-results.json");
File.WriteAllText(reportPath, JsonSerializer.Serialize(new { validation = "Offline fixtures and original IL only; no game execution", count = results.Count, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("RESULT " + reportPath);

static AssemblyDefinition Runtime(PatchProfile? profile = null)
{
    profile ??= PatchProfile.Skins;
    var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(profile.RuntimeName, new Version(1, 0, 0, 0)), profile.RuntimeName, ModuleKind.Dll);
    var module = assembly.MainModule;
    var bootstrap = new TypeDefinition(profile.RuntimeName, "Bootstrap", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
    module.Types.Add(bootstrap);
    foreach (var spec in profile.Hooks.Values.DistinctBy(h => h.Hook))
    {
        bool conditional = spec.Placement == HookPlacement.ConditionalBeforeBody;
        bool transform = spec.Placement == HookPlacement.AfterCallTransformSingle;
        var method = new MethodDefinition(spec.Hook, Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            transform ? module.TypeSystem.Single : conditional ? module.TypeSystem.Boolean : module.TypeSystem.Void);
        if (spec.Placement == HookPlacement.ReplaceCallWithOwner)
        {
            var game = new AssemblyNameReference("Assembly-CSharp", new Version(1, 0));
            var localization = new AssemblyNameReference("Unity.Localization", new Version(1, 0));
            module.AssemblyReferences.Add(game); module.AssemblyReferences.Add(localization);
            var language = new TypeReference("", "LanguageManager", module, game);
            var localize = new TypeReference("UnityEngine.Localization.Components", "LocalizeStringEvent", module, localization);
            foreach (var argument in new[] { language, module.TypeSystem.Single, module.TypeSystem.Single, localize, module.TypeSystem.String, module.TypeSystem.Object })
                method.Parameters.Add(new ParameterDefinition(argument));
            var original = new MethodReference("FormatTimeStringForDurability", module.TypeSystem.Void, language) { HasThis = true };
            foreach (var argument in method.Parameters.Skip(1).Take(4)) original.Parameters.Add(new ParameterDefinition(argument.ParameterType));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg, method.Parameters[5]));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, bootstrap.Fields.Single(f => f.Name == "LastOwner")));
            foreach (var argument in method.Parameters.Take(5)) method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg, argument));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, original));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            bootstrap.Methods.Add(method);
            continue;
        }
        if (transform)
        {
            method.Parameters.Add(new ParameterDefinition("original", ParameterAttributes.None, module.TypeSystem.Single));
            method.Parameters.Add(new ParameterDefinition("owner", ParameterAttributes.None, module.TypeSystem.Object));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, bootstrap.Fields.Single(f => f.Name == "LastOwner")));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R4, 1f));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Add));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            bootstrap.Methods.Add(method);
            continue;
        }
        if (conditional)
        {
            method.Parameters.Add(new ParameterDefinition("owner", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Object));
            var allow = new FieldDefinition("AllowOriginal", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Boolean);
            var owner = new FieldDefinition("LastOwner", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Object);
            bootstrap.Fields.Add(allow); bootstrap.Fields.Add(owner);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, owner));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, allow));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            bootstrap.Methods.Add(method);
            continue;
        }
        if (spec.Placement == HookPlacement.BeforeBodyWithOwner)
        {
            method.Parameters.Add(new ParameterDefinition("owner", ParameterAttributes.None, module.TypeSystem.Object));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, bootstrap.Fields.Single(f => f.Name == "LastOwner")));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            bootstrap.Methods.Add(method);
            continue;
        }
        if (spec.Placement == HookPlacement.BeforeReturn)
            method.Parameters.Add(new ParameterDefinition("customizer", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Object));
        else
        {
            var reject = new FieldDefinition("Reject", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static, module.TypeSystem.Boolean);
            bootstrap.Fields.Add(reject);
            var ret = Instruction.Create(OpCodes.Ret);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, reject));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Brfalse_S, ret));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Throw));
            method.Body.Instructions.Add(ret);
            bootstrap.Methods.Add(method);
            continue;
        }
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); bootstrap.Methods.Add(method);
    }
    return assembly;
}
