using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static Jinke.DredgeSkins.Weaver.Program;

namespace Jinke.DredgeSkins.Weaver;

public static class SemanticAudit
{
    public record Snapshot(string metadata, SortedDictionary<string, string> bodies, SortedDictionary<string, int> maxStacks, string[] references);
    public record MethodAudit(string method, string originalBodySha256, string patchedBodySha256, string restoredBodySha256, int originalMaxStack, int patchedMaxStack);
    public record Result(bool passed, int unchangedMethodBodies, int totalMethodBodies, string unchangedMetadataSha256,
        string[] addedAssemblyReferences, MethodAudit[] changedMethods, object[] maxStackHeaderChanges);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static string Json(object? value) => JsonSerializer.Serialize(value, JsonOptions);
    private static string TextHash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    private static string Type(TypeReference? t) => t switch
    {
        null => "",
        GenericInstanceType g => Type(g.ElementType) + "<" + string.Join(",", g.GenericArguments.Select(Type)) + ">",
        RequiredModifierType m => "modreq(" + Type(m.ModifierType) + ") " + Type(m.ElementType),
        OptionalModifierType m => "modopt(" + Type(m.ModifierType) + ") " + Type(m.ElementType),
        FunctionPointerType f => "fnptr " + Type(f.ReturnType) + "(" + string.Join(",", f.Parameters.Select(p => Type(p.ParameterType))) + ")" +
            $" [{(int)f.CallingConvention},{f.HasThis},{f.ExplicitThis}]",
        TypeSpecification s => s.FullName + " [element=" + Type(s.ElementType) + "]",
        GenericParameter g => $"generic:{g.Type}:{g.Position}:{g.Name}",
        _ => t.FullName + " @ " + Scope(t.Scope)
    };
    private static string Scope(IMetadataScope? scope) => scope switch
    {
        AssemblyNameReference a => a.FullName,
        ModuleDefinition m => m.Assembly.Name.FullName,
        null => "",
        _ => scope.Name
    };
    private static string Method(MethodReference? m) => m switch
    {
        null => "",
        GenericInstanceMethod g => Method(g.ElementMethod) + "<" + string.Join(",", g.GenericArguments.Select(Type)) + ">",
        _ => m.FullName + " @ " + Type(m.DeclaringType) + " return=" + Type(m.ReturnType) + " args=" +
            string.Join(",", m.Parameters.Select(p => Type(p.ParameterType))) + $" [{(int)m.CallingConvention},{m.HasThis},{m.ExplicitThis}]"
    };
    private static string Field(FieldReference f) => f.FullName + " @ " + Type(f.DeclaringType) + " type=" + Type(f.FieldType);
    private static object[] Attributes(ICustomAttributeProvider provider) => provider.CustomAttributes
        .Select(a => (object)new { ctor = Method(a.Constructor), blob = Convert.ToHexString(a.GetBlob()) }).ToArray();
    private static object[] Security(ISecurityDeclarationProvider provider) => provider.SecurityDeclarations
        .Select(a => (object)new { action = (int)a.Action, blob = Convert.ToHexString(a.GetBlob()) }).ToArray();
    private static object? Constant(object? value) => value switch
    {
        null => null,
        float f => new { type = "Single", bits = BitConverter.SingleToInt32Bits(f).ToString("X8") },
        double d => new { type = "Double", bits = BitConverter.DoubleToInt64Bits(d).ToString("X16") },
        byte[] bytes => new { type = "Bytes", bits = Convert.ToHexString(bytes) },
        _ => new { type = value.GetType().FullName, value = Convert.ToString(value, CultureInfo.InvariantCulture) }
    };

    // MarshalInfo is a small Cecil value hierarchy. Read public scalar properties only; never Resolve() types.
    private static object? Marshal(MarshalInfo? info)
    {
        if (info == null) return null;
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in info.GetType().GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
        {
            var value = property.GetValue(info);
            values[property.Name] = value is TypeReference t ? Type(t) : value is Enum e ? Convert.ToInt64(e) : value;
        }
        return new { type = info.GetType().FullName, values };
    }

    private static object Parameter(ParameterDefinition p) => new
    {
        p.Name, p.Index, type = Type(p.ParameterType), attributes = (int)p.Attributes,
        p.HasConstant, constant = p.HasConstant ? Constant(p.Constant) : null,
        marshal = p.HasMarshalInfo ? Marshal(p.MarshalInfo) : null, custom = Attributes(p)
    };
    private static object[] Generics(IGenericParameterProvider provider) => provider.GenericParameters.Select(g => (object)new
    {
        g.Name, g.Position, attributes = (int)g.Attributes, custom = Attributes(g),
        constraints = g.Constraints.Select(c => new { type = Type(c.ConstraintType), custom = Attributes(c) }).ToArray()
    }).ToArray();

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        static IEnumerable<TypeDefinition> Descendants(TypeDefinition type)
        {
            yield return type;
            foreach (var child in type.NestedTypes)
                foreach (var nested in Descendants(child)) yield return nested;
        }
        return module.Types.SelectMany(Descendants).OrderBy(t => t.FullName, StringComparer.Ordinal);
    }

    public static Snapshot Capture(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;
        var types = AllTypes(module).ToArray();
        var bodies = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var maxStacks = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var method in types.SelectMany(t => t.Methods).Where(m => m.HasBody))
        {
            Require(bodies.TryAdd(method.FullName, Body(method)), "Ambiguous duplicate method body name: " + method.FullName);
            maxStacks.Add(method.FullName, method.Body.MaxStackSize);
        }
        string metadata = Json(new
        {
            assemblyName = AssemblyName(assembly.Name), custom = Attributes(assembly), security = Security(assembly),
            module = new
            {
                module.Name, module.Mvid, kind = (int)module.Kind, runtime = module.RuntimeVersion,
                architecture = (int)module.Architecture, attributes = (int)module.Attributes, characteristics = (int)module.Characteristics,
                module.MetadataKind, entry = Method(module.EntryPoint), custom = Attributes(module),
                references = module.ModuleReferences.Select(r => r.Name).ToArray(),
                exported = module.ExportedTypes.Select(t => new { t.FullName, attributes = (int)t.Attributes, scope = Scope(t.Scope), t.Identifier }).ToArray(),
                resources = module.Resources.Select(Resource).ToArray()
            },
            types = types.Select(t => new
            {
                t.FullName, attributes = (int)t.Attributes, baseType = Type(t.BaseType), t.PackingSize, t.ClassSize,
                custom = Attributes(t), security = Security(t), generics = Generics(t),
                interfaces = t.Interfaces.Select(i => new { type = Type(i.InterfaceType), custom = Attributes(i) }).ToArray(),
                fields = t.Fields.Select(f => new
                {
                    f.Name, type = Type(f.FieldType), attributes = (int)f.Attributes, f.Offset, f.HasConstant,
                    constant = f.HasConstant ? Constant(f.Constant) : null, initialValue = Convert.ToHexString(f.InitialValue),
                    marshal = f.HasMarshalInfo ? Marshal(f.MarshalInfo) : null, custom = Attributes(f)
                }).ToArray(),
                methods = t.Methods.Select(m => new
                {
                    m.FullName, attributes = (int)m.Attributes, implAttributes = (int)m.ImplAttributes,
                    semantics = (int)m.SemanticsAttributes, convention = (int)m.CallingConvention,
                    m.HasThis, m.ExplicitThis, generics = Generics(m), parameters = m.Parameters.Select(Parameter).ToArray(),
                    returns = new
                    {
                        type = Type(m.ReturnType), m.MethodReturnType.Name, attributes = (int)m.MethodReturnType.Attributes,
                        m.MethodReturnType.HasConstant, constant = m.MethodReturnType.HasConstant ? Constant(m.MethodReturnType.Constant) : null,
                        marshal = m.MethodReturnType.HasMarshalInfo ? Marshal(m.MethodReturnType.MarshalInfo) : null,
                        custom = Attributes(m.MethodReturnType)
                    },
                    custom = Attributes(m), security = Security(m), overrides = m.Overrides.Select(Method).ToArray(),
                    pinvoke = m.HasPInvokeInfo ? new { attributes = (int)m.PInvokeInfo.Attributes, m.PInvokeInfo.EntryPoint, module = m.PInvokeInfo.Module.Name } : null
                }).ToArray(),
                properties = t.Properties.Select(p => new
                {
                    p.Name, type = Type(p.PropertyType), attributes = (int)p.Attributes, p.HasThis,
                    p.HasConstant, constant = p.HasConstant ? Constant(p.Constant) : null,
                    parameters = p.Parameters.Select(Parameter).ToArray(), custom = Attributes(p),
                    getter = Method(p.GetMethod), setter = Method(p.SetMethod), others = p.OtherMethods.Select(Method).ToArray()
                }).ToArray(),
                events = t.Events.Select(e => new
                {
                    e.Name, type = Type(e.EventType), attributes = (int)e.Attributes, custom = Attributes(e),
                    add = Method(e.AddMethod), remove = Method(e.RemoveMethod), invoke = Method(e.InvokeMethod), others = e.OtherMethods.Select(Method).ToArray()
                }).ToArray()
            }).ToArray()
        });
        return new Snapshot(metadata, bodies, maxStacks, module.AssemblyReferences.Select(a => Json(AssemblyName(a))).Order(StringComparer.Ordinal).ToArray());
    }

    private static object AssemblyName(AssemblyNameReference a) => new
    {
        a.FullName, attributes = (int)a.Attributes, algorithm = (int)a.HashAlgorithm,
        publicKey = Convert.ToHexString(a.PublicKey ?? []), hash = Convert.ToHexString(a.Hash ?? [])
    };
    private static object Resource(Resource resource) => resource switch
    {
        EmbeddedResource e => new { e.Name, attributes = (int)e.Attributes, kind = "embedded", data = Hash(e.GetResourceData()) },
        LinkedResource l => new { l.Name, attributes = (int)l.Attributes, kind = "linked", l.File, hash = Convert.ToHexString(l.Hash) },
        AssemblyLinkedResource a => new { a.Name, attributes = (int)a.Attributes, kind = "assembly", assembly = a.Assembly.FullName },
        _ => throw new InvalidOperationException("Unsupported resource kind.")
    };

    public static string Body(MethodDefinition method, string? stripHook = null, PatchProfile? profile = null)
    {
        var body = method.Body;
        var instructions = body.Instructions.ToArray();
        var removed = new HashSet<Instruction>();
        var aliases = new Dictionary<Instruction, Instruction>();
        var restoredCalls = new Dictionary<Instruction, MethodReference>();
        if (stripHook != null)
        {
            profile ??= PatchProfile.Skins;
            Require(profile.Hooks.TryGetValue(method.FullName, out var spec) && spec.Hook == stripHook,
                "Hook is not allowed by the selected profile.");
            if (spec!.Placement == HookPlacement.BeforeReturn) Patcher.ValidateReturnRegions(method, spec);
            if (spec.Placement == HookPlacement.ReplaceCallWithOwner)
            {
                Require(!body.HasExceptionHandlers, "Call wrapper target acquired exception regions.");
                var original = Patcher.WrappedCallee(method.Module, spec);
                var calls = instructions.Where(i => i.Operand is MethodReference m && m.DeclaringType.FullName == profile.HookType && m.Name == spec.Hook).ToArray();
                Require(calls.Length == spec.ExpectedCalls, "Unexpected wrapper hook count.");
                foreach (var call in calls)
                {
                    var load = call.Previous;
                    Require(load?.OpCode == OpCodes.Ldarg_0, "Missing exact owner before wrapper call.");
                    VerifyHook(call, method, spec, profile);
                    removed.Add(load!); aliases.Add(load!, call); restoredCalls.Add(call, original);
                    foreach (var instruction in instructions)
                    {
                        var targets = instruction.Operand is Instruction target ? new[] { target } : instruction.Operand as Instruction[] ?? [];
                        Require(!targets.Contains(call), "A branch skips the wrapper owner.");
                    }
                }
            }
            else if (spec.Placement == HookPlacement.AfterCallTransformSingle)
            {
                foreach (var originalCall in Patcher.FindTransformCalls(method, spec))
                {
                    var load = originalCall.Next;
                    var call = load?.Next;
                    Require(load?.OpCode == OpCodes.Ldarg_0, "Missing exact owner after transform callee.");
                    VerifyHook(call, method, spec, profile);
                    removed.Add(load!); removed.Add(call!);
                }
                foreach (var instruction in instructions)
                {
                    var targets = instruction.Operand is Instruction target ? new[] { target } : instruction.Operand as Instruction[] ?? [];
                    Require(!targets.Any(removed.Contains), "A branch enters the injected transform.");
                }
                Require(!body.ExceptionHandlers.Any(h => removed.Contains(h.TryStart) || removed.Contains(h.TryEnd) ||
                    removed.Contains(h.HandlerStart) || removed.Contains(h.HandlerEnd) || removed.Contains(h.FilterStart)),
                    "An exception boundary enters the injected transform.");
            }
            else if (spec.Placement != HookPlacement.BeforeReturn)
            {
                bool conditional = spec.Placement == HookPlacement.ConditionalBeforeBody;
                bool owner = spec.Placement == HookPlacement.BeforeBodyWithOwner;
                int prefixSize = conditional ? 4 : owner ? 2 : 1;
                Require(instructions.Length > prefixSize, "Missing original body after injected prefix.");
                var call = instructions[conditional || owner ? 1 : 0];
                if (owner)
                    Require(instructions[0].OpCode == OpCodes.Ldarg_0, "Prefix must pass the exact owner.");
                if (conditional)
                    Require(instructions[0].OpCode == OpCodes.Ldarg_0 && instructions[2].OpCode == OpCodes.Brtrue &&
                        instructions[2].Operand == instructions[prefixSize] && instructions[3].OpCode == OpCodes.Ret,
                        "Conditional prefix must resume the exact original body on true and return on false.");
                VerifyHook(call, method, spec, profile);
                foreach (var instruction in instructions.Take(prefixSize)) removed.Add(instruction);
                Require(!body.ExceptionHandlers.Any(h => removed.Contains(h.TryStart) || removed.Contains(h.TryEnd) ||
                    removed.Contains(h.HandlerStart) || removed.Contains(h.HandlerEnd) || removed.Contains(h.FilterStart)),
                    "An exception boundary includes the injected prefix.");
                foreach (var instruction in instructions.Skip(prefixSize))
                {
                    var targets = instruction.Operand is Instruction target ? new[] { target } : instruction.Operand as Instruction[] ?? [];
                    Require(!targets.Any(removed.Contains), "A branch unexpectedly re-enters the injected prefix.");
                }
            }
            else
            {
                foreach (var ret in instructions.Where(i => i.OpCode == OpCodes.Ret))
                {
                    var call = ret.Previous;
                    var load = call?.Previous;
                    Require(load?.OpCode == OpCodes.Ldarg_0, "Missing exact hook before a normal return in " + method.FullName);
                    VerifyHook(call, method, spec, profile);
                    Require(!body.ExceptionHandlers.Any(h => h.TryStart == load || h.TryEnd == load || h.HandlerStart == load || h.HandlerEnd == load || h.FilterStart == load ||
                        h.TryStart == call || h.TryEnd == call || h.HandlerStart == call || h.HandlerEnd == call || h.FilterStart == call),
                        "An exception boundary enters the injected return hook.");
                    removed.Add(load!); removed.Add(call!); aliases.Add(load!, ret);
                    foreach (var instruction in instructions)
                    {
                        var targets = instruction.Operand is Instruction target ? new[] { target } : instruction.Operand as Instruction[] ?? [];
                        Require(!targets.Contains(ret) && !targets.Contains(call!), "A branch skips or enters the middle of the injected hook.");
                    }
                }
            }
        }
        var kept = instructions.Where(i => !removed.Contains(i)).ToArray();
        var indices = kept.Select((instruction, index) => (instruction, index)).ToDictionary(x => x.instruction, x => x.index);
        int Index(Instruction? i) => i == null ? -1 : indices[aliases.TryGetValue(i, out var original) ? original : i];
        object? Operand(object? value) => value switch
        {
            null => null,
            Instruction i => new { target = Index(i) },
            Instruction[] targets => new { targets = targets.Select(i => Index(i)).ToArray() },
            MethodReference m => new { method = Method(m) },
            FieldReference f => new { field = Field(f) },
            TypeReference t => new { type = Type(t) },
            VariableDefinition v => new { local = v.Index },
            ParameterDefinition p => new { arg = p.Index },
            CallSite c => new { signature = c.FullName, returns = Type(c.ReturnType), parameters = c.Parameters.Select(p => Type(p.ParameterType)).ToArray(), c.HasThis, c.ExplicitThis, convention = (int)c.CallingConvention },
            string s => new { text = s },
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double => Constant(value),
            _ => throw new InvalidOperationException("Unsupported IL operand: " + value.GetType().FullName)
        };
        return Json(new
        {
            body.InitLocals,
            variables = body.Variables.Select(v => Type(v.VariableType)).ToArray(),
            instructions = kept.Select(i => new { opcode = restoredCalls.ContainsKey(i) ? OpCodes.Callvirt.Name : i.OpCode.Name,
                operand = Operand(restoredCalls.TryGetValue(i, out var original) ? original : i.Operand) }).ToArray(),
            exceptions = body.ExceptionHandlers.Select(h => new
            {
                type = (int)h.HandlerType, tryStart = Index(h.TryStart), tryEnd = Index(h.TryEnd),
                handlerStart = Index(h.HandlerStart), handlerEnd = Index(h.HandlerEnd), filterStart = Index(h.FilterStart), catchType = Type(h.CatchType)
            }).ToArray()
        });
    }

    private static void VerifyHook(Instruction? call, MethodDefinition method, HookSpec spec, PatchProfile profile)
    {
        Require(call?.OpCode == OpCodes.Call && call.Operand is MethodReference,
            "Missing exact hook in " + method.FullName);
        var hook = (MethodReference)call!.Operand;
        bool transform = spec.Placement == HookPlacement.AfterCallTransformSingle;
        var returnType = transform ? method.Module.TypeSystem.Single : spec.Placement == HookPlacement.ConditionalBeforeBody ? method.Module.TypeSystem.Boolean : method.Module.TypeSystem.Void;
        var parameterTypes = Patcher.HookParameterTypes(method.Module, spec);
        Require(hook.DeclaringType.FullName == profile.HookType && hook.Name == spec.Hook &&
            hook is not GenericInstanceMethod && !hook.HasGenericParameters && !hook.HasThis && !hook.ExplicitThis &&
            hook.CallingConvention == MethodCallingConvention.Default &&
            Type(hook.ReturnType) == Type(returnType) && hook.Parameters.Select(p => Type(p.ParameterType)).SequenceEqual(parameterTypes.Select(Type)) &&
            hook.DeclaringType.Scope is AssemblyNameReference a && a.Name == profile.RuntimeName,
            "Incorrect hook target after patch.");
    }

    public static Result Verify(Snapshot before, AssemblyDefinition patched, AssemblyNameDefinition runtime, List<Patcher.HookDetail> details) =>
        Verify(before, patched, runtime, details, PatchProfile.Skins);

    public static Result Verify(Snapshot before, AssemblyDefinition patched, AssemblyNameDefinition runtime, List<Patcher.HookDetail> details, PatchProfile profile)
    {
        Require(runtime.Name == profile.RuntimeName, "Wrong runtime assembly identity for audit profile.");
        Require(details.Count == profile.Hooks.Count && details.Select(d => d.method).Distinct().Count() == details.Count &&
            details.All(d => profile.Hooks.TryGetValue(d.method, out var spec) && d.hook == spec.Hook && d.placement == spec.Placement.ToString() &&
                d.callee == spec.Callee && d.transformedCalls == spec.ExpectedCalls),
            "Hook details do not match the selected profile.");
        var after = Capture(patched);
        Require(before.metadata == after.metadata, "Assembly definitions/fields/constants/resources changed unexpectedly.");
        Require(before.bodies.Keys.SequenceEqual(after.bodies.Keys), "Method body set changed unexpectedly.");
        var added = after.references.Except(before.references).ToArray();
        Require(before.references.Except(after.references).Count() == 0 && after.references.Length == before.references.Length + 1 && added.Length == 1,
            "Expected exactly one new assembly reference and no removed references. Added: " + string.Join(", ", added) +
            "; removed: " + string.Join(", ", before.references.Except(after.references)) +
            "; original count=" + before.references.Length + "; patched count=" + after.references.Length +
            "; all patched=" + string.Join(", ", after.references));
        var newRef = patched.MainModule.AssemblyReferences.Single(r => r.Name == profile.RuntimeName);
        Require(newRef.FullName == runtime.FullName && added[0] == Json(AssemblyName(newRef)), "Unexpected new assembly reference.");
        var methods = AllTypes(patched.MainModule).SelectMany(t => t.Methods).Where(m => m.HasBody).ToDictionary(m => m.FullName);
        var changes = new List<MethodAudit>();
        int unchanged = 0;
        foreach (var entry in before.bodies)
        {
            if (!profile.Hooks.TryGetValue(entry.Key, out var spec))
            {
                Require(entry.Value == after.bodies[entry.Key], "Unapproved method body change: " + entry.Key);
                unchanged++;
                continue;
            }
            string restored = Body(methods[entry.Key], spec.Hook, profile);
            Require(restored == entry.Value, "Injected method contains changes beyond the allowed hook: " + entry.Key);
            int extraReturns = spec.Placement == HookPlacement.ConditionalBeforeBody ? 1 : 0;
            Require(methods[entry.Key].Body.Instructions.Count(i => i.OpCode == OpCodes.Ret) == details.Single(d => d.method == entry.Key).returns + extraReturns,
                "Normal return count changed.");
            changes.Add(new MethodAudit(entry.Key, TextHash(entry.Value), TextHash(after.bodies[entry.Key]), TextHash(restored),
                before.maxStacks[entry.Key], after.maxStacks[entry.Key]));
        }
        Require(changes.Count == profile.Hooks.Count, "Unexpected number of hooked methods for selected profile.");
        foreach (var entry in before.maxStacks.Where(e => after.maxStacks[e.Key] != e.Value))
            Require(profile.Hooks.TryGetValue(entry.Key, out var spec) &&
                ((spec.Placement == HookPlacement.AfterCallTransformSingle || spec.Placement == HookPlacement.ReplaceCallWithOwner) && after.maxStacks[entry.Key] == entry.Value + 1 ||
                (spec.Placement == HookPlacement.BeforeReturn || spec.Placement == HookPlacement.ConditionalBeforeBody || spec.Placement == HookPlacement.BeforeBodyWithOwner) && entry.Value == 0 && after.maxStacks[entry.Key] == 1),
                "Unexpected IL stack header change: " + entry.Key);
        var headers = before.maxStacks.Where(e => after.maxStacks[e.Key] != e.Value)
            .Select(e => (object)new { method = e.Key, original = e.Value, patched = after.maxStacks[e.Key] }).ToArray();
        return new Result(true, unchanged, before.bodies.Count, TextHash(before.metadata), [newRef.FullName], changes.ToArray(), headers);
    }
}
