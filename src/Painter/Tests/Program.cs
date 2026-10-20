using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Google.Protobuf;
using Jinke.DredgePainter;
using Yarn;
using Yarn.Unity;
using Op = Yarn.Instruction.Types.OpCode;

internal static class Tests
{
    private static int checks;
    private static byte[] source;
    private static Yarn.Program original, patched;

    private static void Main(string[] args)
    {
        source = File.ReadAllBytes(args[0]);
        original = Yarn.Program.Parser.ParseFrom(source);
        patched = PainterDialogueProgram.Build(source);
        Check("fixed original program and source bytes preserved", PainterDialogueProgram.Digest(source) == PainterDialogueProgram.SourceSha256);
        Check("all 951 unrelated original nodes remain byte-identical", original.Nodes.Where(p => p.Key != "Painter_Customize_Color_Page_2" && p.Key != "Painter_Customize_Flag2")
            .All(p => p.Value.ToByteArray().SequenceEqual(patched.Nodes[p.Key].ToByteArray())));
        Check("only eight private nodes added", patched.Nodes.Count == original.Nodes.Count + 8 &&
            patched.Nodes.Keys.Except(original.Nodes.Keys).All(n => n.StartsWith("Jinke_Painter_", StringComparison.Ordinal)));
        Check("original initial values and program name preserved", original.InitialValues.Equals(patched.InitialValues) && original.Name == patched.Name);
        Check("all original color instructions and labels preserved after removing the bounded insertion", RestorePage("Painter_Customize_Color_Page_2", 48));
        Check("all original flag instructions and labels preserved after removing the bounded insertion", RestorePage("Painter_Customize_Flag2", 22));
        Check("patch deterministic and does not mutate source array", patched.ToByteArray().SequenceEqual(PainterDialogueProgram.Build(source).ToByteArray()) &&
            PainterDialogueProgram.Digest(source) == PainterDialogueProgram.SourceSha256);
        byte[] changed = (byte[])source.Clone(); changed[0] ^= 1;
        Reject("unknown program refused", () => PainterDialogueProgram.Build(changed));
        Reject("repeated patch refused", () => PainterDialogueProgram.Build(patched.ToByteArray()));
        ValidateGraph();
        VerifyMetadata(args[1]);
        foreach (bool ready in new[] { false, true })
        {
            var color = new Run(patched, ready); color.Start("Painter_Customize_Color_Page_2");
            Check("color page keeps four native choices and navigation ready=" + ready, color.Options.Count == (ready ? 7 : 6));
            var flag = new Run(patched, ready); flag.Start("Painter_Customize_Flag2");
            Check("flag page keeps locked native choices visible ready=" + ready, flag.Options.Count == (ready ? 7 : 6) &&
                flag.Options.Where(o => new[] { "line:056bb84", "line:01d4a34", "line:0824566", "line:048fbac" }.Contains(o.Line.ID)).All(o => !o.IsAvailable));
        }
        foreach (int area in new[] { 0, 1 })
        {
            foreach (string id in PainterDialogueProgram.SkinIds)
            {
                var run = new Run(patched, true); run.Storage.SetValue("$boat_customization_area_index", (float)area);
                run.Start(PainterDialogueProgram.ColorPage); run.Choose(PainterDialogueProgram.ColorLine(id));
                Check("custom color " + area + "/" + id + " uses native two-hour timing and exact command", run.Commands.Count(c => c == "PassTime 2 feedback.pass-time-paint-boat") == 1 &&
                    run.Commands.Contains("PlayClip Paint_Boat") && run.Commands.Contains(PainterDialogueProgram.ColorCommand + " " + area + " " + id) &&
                    !run.Commands.Any(c => c.StartsWith("ChangeBoatColor ", StringComparison.Ordinal)) && run.Lines.Contains("line:0001e01") &&
                    run.Node == "Painter_Customize_Color_Area");
            }
        }
        foreach (string id in PainterDialogueProgram.SkinIds)
        {
            var run = new Run(patched, true); run.Start(PainterDialogueProgram.FlagPage); run.Choose(PainterDialogueProgram.FlagLine(id));
            Check("custom flag " + id + " uses native confirmation and returns to painter", run.Commands.Contains(PainterDialogueProgram.FlagCommand + " " + id) &&
                run.Commands.Contains("PlayClip Change_Flag") && !run.Commands.Any(c => c.StartsWith("ChangeBoatFlag ", StringComparison.Ordinal)) &&
                !run.Commands.Any(c => c.StartsWith("PassTime ", StringComparison.Ordinal)) && run.Lines.Contains("line:07941e3") && run.Node == "Painter_Customize");
        }
        Navigation();
        UnavailableAndRejected();
        NativeChoices();
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", checks, evidence = "Offline shipped Yarn VM only; no game execution, Unity rendering, or save writes.",
            sourceSha256 = PainterDialogueProgram.Digest(source), originalNodes = original.Nodes.Count, patchedNodes = patched.Nodes.Count }));
    }

    private static bool RestorePage(string name, int insertion)
    {
        Node restored = patched.Nodes[name].Clone();
        while (restored.Instructions.Count > original.Nodes[name].Instructions.Count + 7) restored.Instructions.RemoveAt(restored.Instructions.Count - 1);
        for (int i = 0; i < 7; i++) restored.Instructions.RemoveAt(insertion);
        foreach (string label in new[] { "JinkeNextPage", "JinkeSkipNextPage", "JinkeEndNextPage" }) restored.Labels.Remove(label);
        foreach (string label in restored.Labels.Keys.ToArray()) if (restored.Labels[label] >= insertion + 7) restored.Labels[label] -= 7;
        return restored.Equals(original.Nodes[name]);
    }

    private static void ValidateGraph()
    {
        foreach (Node node in patched.Nodes.Values.Where(n => n.Name.StartsWith("Jinke_", StringComparison.Ordinal) ||
            n.Name == "Painter_Customize_Color_Page_2" || n.Name == "Painter_Customize_Flag2"))
        {
            foreach (Instruction instruction in node.Instructions)
            {
                string label = instruction.Opcode == Op.AddOption ? instruction.Operands[1].StringValue :
                    instruction.Opcode == Op.JumpTo || instruction.Opcode == Op.JumpIfFalse ? instruction.Operands[0].StringValue : null;
                if (label != null && (!node.Labels.ContainsKey(label) || node.Labels[label] >= node.Instructions.Count))
                    throw new Exception("Invalid branch " + node.Name + "/" + label);
            }
        }
        Check("all new and shifted branches target valid instructions", true);
        foreach (string page in new[] { PainterDialogueProgram.ColorPage, PainterDialogueProgram.FlagPage })
        {
            Node node = patched.Nodes[page];
            Check("third page has three themes plus native previous/back " + page,
                node.Instructions.Count(i => i.Opcode == Op.AddOption) == 5 && node.Instructions.Where(i => i.Opcode == Op.AddOption).Take(3)
                .Select(i => i.Operands[0].StringValue).All(id => PainterDialogueProgram.Entries.Any(e => e.Id == id)));
        }
    }

    private static void VerifyMetadata(string path)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        Type metadataType = typeof(LineMetadata);
        Type entryType = metadataType.Assembly.GetType("Yarn.Unity.LineMetadataTableEntry", true);
        var empty = Array.CreateInstance(entryType, 0);
        var metadata = (LineMetadata)Activator.CreateInstance(metadataType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { empty }, null);
        FieldInfo field = metadataType.GetField("_lineMetadata", BindingFlags.NonPublic | BindingFlags.Instance);
        var dictionary = (IDictionary<string, string>)field.GetValue(metadata);
        foreach (var value in values) dictionary.Add(value.Key, value.Value);
        LineMetadata clone = PainterDialogueMetadata.CopyWithEntries(metadata);
        var copied = (IDictionary<string, string>)field.GetValue(clone);
        Check("metadata is an independent object and independent dictionary", !ReferenceEquals(metadata, clone) && !ReferenceEquals(dictionary, copied));
        Check("all original metadata preserved with exactly seven new entries", dictionary.Count == values.Count && copied.Count == values.Count + 7 &&
            values.All(v => copied[v.Key] == v.Value));
        Check("native paint icon and repeat metadata present", PainterDialogueProgram.Entries.Where(e => e.Id != PainterDialogueProgram.FailureLine).All(e =>
            clone.GetMetadata(e.Id).Contains("repeat") && (e.Id.Contains("-flag-") || clone.GetMetadata(e.Id).Contains("paint"))));
        copied["line:03949a5"] = "changed";
        Check("mutating cloned metadata cannot alter original", dictionary["line:03949a5"] == values["line:03949a5"]);
        Reject("metadata collision refuses a repeated application", () => PainterDialogueMetadata.CopyWithEntries(clone));
    }

    private static void Navigation()
    {
        var color = new Run(patched, true); color.Start("Painter_Customize_Color_Area"); color.Choose("line:0300338");
        color.Choose("line:01b7c6c"); color.Choose("line:01b7c6c");
        Check("native hull path reaches third color page", color.Node == PainterDialogueProgram.ColorPage && color.Options.Count == 5);
        color.Choose("line:062a122");
        Check("color previous returns through native area prompt", color.Node == "Painter_Customize_Color_Page_2" && color.Lines.Last() == "line:0a1ecaf" &&
            color.Storage.Number("$boat_customization_area_index") == 1);
        color.Choose("line:01b7c6c"); color.Choose("line:03f37d3");
        Check("third color page back returns to native area picker", color.Node == "Painter_Customize_Color_Area");
        var flag = new Run(patched, true); flag.Start("Painter_Customize_Flag1"); flag.Choose("line:0f9e0dd"); flag.Choose("line:0f9e0dd");
        Check("native flag path reaches third flag page", flag.Node == PainterDialogueProgram.FlagPage && flag.Options.Count == 5);
        flag.Choose("line:0f9ae17");
        Check("third flag page previous returns to native second page", flag.Node == "Painter_Customize_Flag2");
        flag.Choose("line:0f9e0dd"); flag.Choose("line:0591391");
        Check("third flag page back returns to native painter root choices", flag.Node == "Painter_Customize");
    }

    private static void UnavailableAndRejected()
    {
        foreach (bool color in new[] { false, true })
        {
            var unavailable = new Run(patched, false);
            unavailable.Start(color ? PainterDialogueProgram.ColorPage : PainterDialogueProgram.FlagPage);
            Check("not-ready third page falls back to original " + color, unavailable.Node == (color ? "Painter_Customize_Color_Page_2" : "Painter_Customize_Flag2") &&
                unavailable.Options.Count == 6 && !unavailable.Commands.Any(c => c.StartsWith("PassTime ", StringComparison.Ordinal)));
            var beforeChoice = new Run(patched, true); beforeChoice.Start(color ? PainterDialogueProgram.ColorPage : PainterDialogueProgram.FlagPage);
            beforeChoice.Ready = false; beforeChoice.Choose(color ? PainterDialogueProgram.ColorLine("ramen") : PainterDialogueProgram.FlagLine("ramen"));
            Check("readiness lost after page opens does not consume time or run a custom command " + color,
                !beforeChoice.Commands.Any(c => c.StartsWith("PassTime ", StringComparison.Ordinal) || c.StartsWith("JinkePainter", StringComparison.Ordinal)));
            var rejected = new Run(patched, true) { ChoiceSuccess = false }; rejected.Start(color ? PainterDialogueProgram.ColorPage : PainterDialogueProgram.FlagPage);
            rejected.Choose(color ? PainterDialogueProgram.ColorLine("ramen") : PainterDialogueProgram.FlagLine("ramen"));
            Check("rejected command avoids success text and returns safely " + color,
                !rejected.Lines.Contains(color ? "line:0001e01" : "line:07941e3") && rejected.Lines.Contains(PainterDialogueProgram.FailureLine) &&
                rejected.Node == (color ? "Painter_Customize_Color_Area" : "Painter_Customize"));
        }
    }

    private static void NativeChoices()
    {
        var colors = new Dictionary<string, int> { { "line:0466fdd", 0 }, { "line:08ced27", 1 }, { "line:0046243", 2 }, { "line:0563aa7", 3 },
            { "line:07e84ab", 4 }, { "line:0692291", 5 }, { "line:0774725", 6 }, { "line:018928b", 7 } };
        foreach (var color in colors)
        {
            var run = new Run(patched, true) { AllOwned = true }; run.Storage.SetValue("$boat_customization_area_index", 1f);
            run.Start(color.Value < 4 ? "Painter_Customize_Color_Page_1" : "Painter_Customize_Color_Page_2"); run.Choose(color.Key);
            Check("native color remains reachable with original command " + color.Value, run.Commands.Contains("ChangeBoatColor 1 " + color.Value) &&
                run.Commands.Count(c => c == "PassTime 2 feedback.pass-time-paint-boat") == 1);
        }
        var flags = new Dictionary<string, int> { { "line:0530526", 0 }, { "line:0dd4029", 1 }, { "line:0f26fe9", 2 }, { "line:08aed22", 3 },
            { "line:056bb84", 4 }, { "line:01d4a34", 5 }, { "line:0824566", 6 }, { "line:048fbac", 7 } };
        foreach (var flag in flags)
        {
            var run = new Run(patched, true) { AllOwned = true };
            run.Start(flag.Value < 4 ? "Painter_Customize_Flag1" : "Painter_Customize_Flag2"); run.Choose(flag.Key);
            Check("native flag remains reachable with original command " + flag.Value, run.Commands.Contains("ChangeBoatFlag " + flag.Value));
        }
    }

    private static void Check(string title, bool value)
    { if (!value) throw new Exception("FAIL " + title); checks++; Console.WriteLine("PASS " + title); }
    private static void Reject(string title, Action action)
    { try { action(); } catch (InvalidOperationException) { Check(title, true); return; } throw new Exception("FAIL " + title); }

    private sealed class Storage : IVariableStorage
    {
        private readonly Dictionary<string, object> values = new Dictionary<string, object>();
        public void SetValue(string key, string value) { values[key] = value; }
        public void SetValue(string key, float value) { values[key] = value; }
        public void SetValue(string key, bool value) { values[key] = value; }
        public void Clear() { values.Clear(); }
        public bool TryGetValue<T>(string key, out T value)
        { object item; if (values.TryGetValue(key, out item) && item is T) { value = (T)item; return true; } value = default(T); return false; }
        internal float Number(string key) { float value; return TryGetValue(key, out value) ? value : 0; }
    }

    private sealed class Run
    {
        internal readonly Storage Storage = new Storage();
        internal readonly List<string> Lines = new List<string>(), Commands = new List<string>();
        internal List<OptionSet.Option> Options = new List<OptionSet.Option>();
        internal bool Ready, AllOwned, ChoiceSuccess = true;
        internal string Node { get { return dialogue.CurrentNode; } }
        private readonly Dialogue dialogue;
        private Line? pendingLine;
        private Command? pendingCommand;
        private bool showUnavailable;
        internal Run(Yarn.Program program, bool ready)
        {
            Ready = ready; dialogue = new Dialogue(Storage); dialogue.SetProgram(program);
            Storage.SetValue("$boat_customization_area_index", 0f);
            dialogue.LogErrorMessage = error => throw new Exception(error);
            dialogue.LogDebugMessage = _ => { };
            dialogue.NodeStartHandler = _ => { };
            dialogue.NodeCompleteHandler = _ => { };
            dialogue.DialogueCompleteHandler = () => { };
            dialogue.Library.RegisterFunction(PainterDialogueProgram.ReadyFunction, (Func<bool>)(() => Ready));
            dialogue.Library.RegisterFunction("GetHasPaint", (Func<float, bool>)(id => AllOwned || id < 3));
            dialogue.Library.RegisterFunction("GetHasFlag", (Func<float, bool>)(id => AllOwned || id == 3));
            dialogue.Library.RegisterFunction("GetIdOfFlagInInventoryAndStorage", (Func<float>)(() => 0));
            dialogue.LineHandler = line => pendingLine = line;
            dialogue.CommandHandler = command => pendingCommand = command;
            dialogue.OptionsHandler = options => Options = options.Options.Where(o => showUnavailable || o.IsAvailable).ToList();
        }
        internal void Start(string node) { dialogue.SetNode(node); dialogue.Continue(); Pump(); }
        internal void Choose(string textId)
        {
            OptionSet.Option option = Options.Single(o => o.Line.ID == textId && o.IsAvailable);
            Options.Clear(); dialogue.SetSelectedOption(option.ID); dialogue.Continue(); Pump();
        }
        private void Pump()
        {
            int remaining = 500;
            while ((pendingLine != null || pendingCommand != null) && remaining-- > 0)
            {
                if (pendingLine != null) { Lines.Add(pendingLine.Value.ID); pendingLine = null; }
                else
                {
                    string text = pendingCommand.Value.Text; pendingCommand = null; Commands.Add(text);
                    if (text.StartsWith("ShowUnavailableOptions ", StringComparison.Ordinal)) showUnavailable = text.EndsWith("true", StringComparison.Ordinal);
                    if (text.StartsWith("JinkePainter", StringComparison.Ordinal)) Storage.SetValue(PainterDialogueProgram.ChoiceResult, ChoiceSuccess);
                }
                dialogue.Continue();
            }
            if (remaining <= 0) throw new Exception("Dialogue did not yield to native options.");
        }
    }
}
