using Mono.Cecil;
using Mono.Cecil.Cil;
using static Jinke.DredgeSkins.Weaver.Program;

namespace Jinke.DredgeSkins.Weaver;

public static class Patcher
{
    public static readonly IReadOnlyDictionary<string, string> Hooks = new Dictionary<string, string>
    {
        ["System.Void PlayerColorCustomizer::RefreshBoatColors()"] = "OnColors",
        ["System.Void PlayerColorCustomizer::RefreshBoatFlag()"] = "OnFlag"
    };
    public const string HookType = "Jinke.DredgeSkins.Bootstrap";

    public record HookDetail(string method, string hook, int returns, int redirectedBranches, string placement = "BeforeReturn", string? callee = null, int transformedCalls = 0);

    public static List<HookDetail> InsertHooks(ModuleDefinition target, ModuleDefinition runtime) =>
        InsertHooks(target, runtime, PatchProfile.Skins);

    public static List<HookDetail> InsertHooks(ModuleDefinition target, ModuleDefinition runtime, PatchProfile profile)
    {
        Require(runtime.Assembly.Name.Name == profile.RuntimeName, "Wrong runtime assembly identity for " + profile.Id + ".");
        var bootstrap = runtime.GetType(profile.HookType) ?? throw new InvalidOperationException("Missing runtime Bootstrap type.");
        Require(bootstrap.IsPublic && !bootstrap.HasGenericParameters, "Runtime Bootstrap must be a public, non-generic type.");
        var runtimeReference = AssemblyNameReference.Parse(runtime.Assembly.Name.FullName);
        target.AssemblyReferences.Add(runtimeReference);
        var bootstrapReference = new TypeReference(bootstrap.Namespace, bootstrap.Name, target, runtimeReference);
        var result = new List<HookDetail>();
        foreach (var spec in profile.Hooks.Values)
        {
            var method = target.Types.SelectMany(t => t.Methods).SingleOrDefault(m => m.FullName == spec.Method)
                ?? throw new InvalidOperationException("Missing exact hook target: " + spec.Method);
            bool transform = spec.Placement == HookPlacement.AfterCallTransformSingle;
            bool wrap = spec.Placement == HookPlacement.ReplaceCallWithOwner;
            Require(method.HasBody && !method.IsStatic && !method.HasGenericParameters &&
                (transform || method.ReturnType.FullName == "System.Void"),
                "Hook target must be an instance method with a supported return type.");
            if (spec.Placement == HookPlacement.BeforeReturn) ValidateReturnRegions(method, spec);
            bool conditional = spec.Placement == HookPlacement.ConditionalBeforeBody;
            bool passOwner = spec.Placement != HookPlacement.BeforeBody;
            var returnType = transform ? target.TypeSystem.Single : conditional ? target.TypeSystem.Boolean : target.TypeSystem.Void;
            var parameterTypes = HookParameterTypes(target, spec);
            var hook = bootstrap.Methods.SingleOrDefault(m => m.Name == spec.Hook && m.IsPublic && m.IsStatic &&
                m.HasBody && !m.IsPInvokeImpl && m.CallingConvention == MethodCallingConvention.Default &&
                !m.HasGenericParameters && m.ReturnType.FullName == returnType.FullName &&
                m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameterTypes.Select(t => t.FullName)))
                ?? throw new InvalidOperationException("Runtime hook signature must be public static " + (transform ? "float " : conditional ? "bool " : "void ") + spec.Hook + (transform ? "(float, object)." : passOwner ? "(object)." : "()."));
            Require(hook.ReturnType.Scope.Name == returnType.Scope.Name &&
                hook.Parameters.Select(p => ScopeName(p.ParameterType)).SequenceEqual(parameterTypes.Select(ScopeName)),
                "Runtime hook uses an incompatible core library.");
            // Reuse the game's existing core types; generic importing can duplicate mscorlib AssemblyRef.
            var reference = new MethodReference(hook.Name, returnType, bootstrapReference) { HasThis = false };
            foreach (var parameterType in parameterTypes) reference.Parameters.Add(new ParameterDefinition(parameterType));
            var returns = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray();
            Require(returns.Length > 0, "Hook target has no normal return.");
            if (wrap)
            {
                Require(!method.Body.HasExceptionHandlers, "Call wrapper target must not have exception regions.");
                var originalCallee = WrappedCallee(target, spec);
                var calls = method.Body.Instructions.Where(i => i.Operand is MethodReference callee &&
                    callee.FullName == originalCallee.FullName && ScopeName(callee.DeclaringType) == spec.CalleeAssembly).ToArray();
                Require(calls.Length == spec.ExpectedCalls && calls.All(c => c.OpCode == OpCodes.Callvirt), "Unexpected wrapper callee opcode or count.");
                int wrapRedirects = 0;
                foreach (var call in calls)
                {
                    Require(call.Previous?.OpCode.OpCodeType != OpCodeType.Prefix, "Wrapper cannot replace a prefixed call.");
                    var load = Instruction.Create(OpCodes.Ldarg_0);
                    foreach (var instruction in method.Body.Instructions.ToArray())
                    {
                        if (instruction.Operand == call) { instruction.Operand = load; wrapRedirects++; }
                        else if (instruction.Operand is Instruction[] targets)
                            for (int i = 0; i < targets.Length; i++)
                                if (targets[i] == call) { targets[i] = load; wrapRedirects++; }
                    }
                    method.Body.GetILProcessor().InsertBefore(call, load);
                    call.OpCode = OpCodes.Call;
                    call.Operand = reference;
                }
                VerifyShortBranches(method);
                result.Add(new HookDetail(spec.Method, spec.Hook, returns.Length, wrapRedirects, spec.Placement.ToString(), spec.Callee, calls.Length));
                continue;
            }
            if (transform)
            {
                var calls = FindTransformCalls(method, spec);
                foreach (var call in calls)
                {
                    // The original float stays on the stack. The exact owning instance is
                    // added, and the runtime replaces only that float before original IL resumes.
                    var load = Instruction.Create(OpCodes.Ldarg_0);
                    var il = method.Body.GetILProcessor();
                    il.InsertAfter(call, load);
                    il.InsertAfter(load, Instruction.Create(OpCodes.Call, reference));
                }
                VerifyShortBranches(method);
                result.Add(new HookDetail(spec.Method, spec.Hook, returns.Length, 0, spec.Placement.ToString(), spec.Callee, calls.Length));
                continue;
            }
            if (conditional)
            {
                // True resumes at the exact original first instruction; false returns without running it.
                // Original branches and exception boundaries stay wholly within the original method body.
                var first = method.Body.Instructions[0];
                var il = method.Body.GetILProcessor();
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, Instruction.Create(OpCodes.Call, reference));
                il.InsertBefore(first, Instruction.Create(OpCodes.Brtrue, first));
                il.InsertBefore(first, Instruction.Create(OpCodes.Ret));
                VerifyShortBranches(method);
                result.Add(new HookDetail(spec.Method, spec.Hook, returns.Length, 0, spec.Placement.ToString()));
                continue;
            }
            if (spec.Placement == HookPlacement.BeforeBody || spec.Placement == HookPlacement.BeforeBodyWithOwner)
            {
                // A failed isolation guard propagates before any original instruction can read or write saves.
                // Branch and exception-region boundaries keep pointing at the original body, outside this guard.
                var first = method.Body.Instructions[0];
                var il = method.Body.GetILProcessor();
                if (spec.Placement == HookPlacement.BeforeBodyWithOwner)
                    il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, Instruction.Create(OpCodes.Call, reference));
                result.Add(new HookDetail(spec.Method, spec.Hook, returns.Length, 0, spec.Placement.ToString()));
                continue;
            }
            int redirects = 0;
            foreach (var ret in returns)
            {
                var load = Instruction.Create(OpCodes.Ldarg_0);
                var call = Instruction.Create(OpCodes.Call, reference);
                // A branch to the old ret must execute the hook, too. No EH metadata is changed.
                foreach (var instruction in method.Body.Instructions.ToArray())
                {
                    if (instruction.Operand == ret) { instruction.Operand = load; redirects++; }
                    else if (instruction.Operand is Instruction[] targets)
                        for (int i = 0; i < targets.Length; i++)
                            if (targets[i] == ret) { targets[i] = load; redirects++; }
                }
                method.Body.GetILProcessor().InsertBefore(ret, load);
                method.Body.GetILProcessor().InsertBefore(ret, call);
            }
            VerifyShortBranches(method);
            result.Add(new HookDetail(spec.Method, spec.Hook, returns.Length, redirects, spec.Placement.ToString()));
        }
        return result;
    }

    internal static Instruction[] FindTransformCalls(MethodDefinition method, HookSpec spec)
    {
        Require(spec.Callee != null && spec.CalleeAssembly != null && spec.ExpectedCalls > 0, "Transform hook needs a fixed callee, assembly and count.");
        var calls = method.Body.Instructions.Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
            i.Operand is MethodReference callee && callee.FullName == spec.Callee).ToArray();
        Require(calls.Length == spec.ExpectedCalls, "Unexpected transform callee count in " + method.FullName);
        foreach (var call in calls)
        {
            var callee = (MethodReference)call.Operand;
            Require(callee is not GenericInstanceMethod && !callee.HasGenericParameters &&
                callee.ReturnType.FullName == "System.Single" && callee.ReturnType.Scope.Name == method.Module.TypeSystem.Single.Scope.Name &&
                (callee.DeclaringType.Scope is ModuleDefinition local ? (local == method.Module && spec.CalleeAssembly == "Assembly-CSharp") :
                    callee.DeclaringType.Scope is AssemblyNameReference external && external.Name == spec.CalleeAssembly) &&
                callee.CallingConvention == MethodCallingConvention.Default,
                "Unexpected transform callee signature or scope.");
            Require(call.Previous?.OpCode != OpCodes.Tail, "Transform cannot follow a tail call.");
        }
        return calls;
    }

    internal static string ScopeName(TypeReference type) => type.Scope is ModuleDefinition module ? module.Assembly.Name.Name : type.Scope.Name;

    internal static MethodDefinition WrappedCallee(ModuleDefinition module, HookSpec spec)
    {
        Require(spec.Placement == HookPlacement.ReplaceCallWithOwner && spec.CalleeAssembly == "Assembly-CSharp" &&
            PatchProfile.VoyageExperience.Hooks.TryGetValue(spec.Method, out var approved) && spec == approved && spec.ExpectedCalls > 0,
            "Call wrapper requires its fixed local callee and count.");
        var callee = module.Types.SelectMany(t => t.Methods).SingleOrDefault(m => m.FullName == spec.Callee);
        Require(callee != null && !callee.IsStatic && !callee.HasGenericParameters && callee.ReturnType.FullName == "System.Void" &&
            callee.CallingConvention == MethodCallingConvention.Default, "Missing exact local wrapper callee.");
        return callee!;
    }

    internal static TypeReference[] HookParameterTypes(ModuleDefinition module, HookSpec spec)
    {
        if (spec.Placement == HookPlacement.ReplaceCallWithOwner)
        {
            var callee = WrappedCallee(module, spec);
            return new[] { callee.DeclaringType }.Concat(callee.Parameters.Select(p => p.ParameterType)).Append(module.TypeSystem.Object).ToArray();
        }
        return spec.Placement == HookPlacement.AfterCallTransformSingle ? new[] { module.TypeSystem.Single, module.TypeSystem.Object } :
            spec.Placement == HookPlacement.BeforeBody ? Array.Empty<TypeReference>() : new[] { module.TypeSystem.Object };
    }

    internal static void ValidateReturnRegions(MethodDefinition method, HookSpec spec)
    {
        if (!method.Body.HasExceptionHandlers) return;
        Require(spec.AllowUnprotectedReturn, "Unexpected exception regions in return hook target " + spec.Method + "; refusing to alter their boundaries.");
        var instructions = method.Body.Instructions;
        bool Within(Instruction candidate, Instruction? start, Instruction? end) => start != null &&
            instructions.IndexOf(candidate) >= instructions.IndexOf(start) && (end == null || instructions.IndexOf(candidate) < instructions.IndexOf(end));
        foreach (var ret in instructions.Where(i => i.OpCode == OpCodes.Ret))
            foreach (var handler in method.Body.ExceptionHandlers)
                Require(!Within(ret, handler.TryStart, handler.TryEnd) && !Within(ret, handler.HandlerStart, handler.HandlerEnd) &&
                    !Within(ret, handler.FilterStart, handler.HandlerStart) &&
                    ret != handler.TryStart && ret != handler.TryEnd && ret != handler.HandlerStart && ret != handler.HandlerEnd && ret != handler.FilterStart,
                    "Return hook must be wholly outside exception regions and their boundaries.");
    }

    private static void VerifyShortBranches(MethodDefinition method)
    {
        var offsets = new Dictionary<Instruction, int>();
        int offset = 0;
        foreach (var instruction in method.Body.Instructions) { offsets.Add(instruction, offset); offset += instruction.GetSize(); }
        foreach (var instruction in method.Body.Instructions)
            if (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget && instruction.Operand is Instruction target)
            {
                int distance = offsets[target] - (offsets[instruction] + instruction.GetSize());
                Require(distance >= sbyte.MinValue && distance <= sbyte.MaxValue, "Injection would overflow a short branch; refusing.");
            }
    }
}
