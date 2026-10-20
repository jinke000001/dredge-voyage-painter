using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Google.Protobuf;
using Yarn;
using Op = Yarn.Instruction.Types.OpCode;

namespace Jinke.DredgePainter
{
    // This transforms a parsed copy. No Unity bundle, original node, or native color index is edited.
    internal static class PainterDialogueProgram
    {
        internal const string SourceSha256 = "0d40d34a713e80c9dfa71e744ffcb25b7600c69a8336158f5351c9d926b52fd3";
        internal const string ReadyFunction = "JinkePainterReady";
        internal const string ColorCommand = "JinkePainterColor";
        internal const string FlagCommand = "JinkePainterFlag";
        internal const string ChoiceResult = "$jinke_painter_choice_succeeded";
        internal const string FailureLine = "line:jinke-painter-choice-failed-v1";
        internal const string ColorPage = "Jinke_Painter_Color_Page_3";
        internal const string FlagPage = "Jinke_Painter_Flag3";
        private const string Area = "$boat_customization_area_index";
        private const string ReturnPath = "$boat_customization_return_path";
        internal static readonly string[] SkinIds = { "ramen", "octopus", "moon" };

        internal sealed class Entry
        {
            internal readonly string Id, Metadata, Chinese, English;
            internal Entry(string id, string metadata, string chinese, string english)
            { Id = id; Metadata = metadata; Chinese = chinese; English = english; }
        }

        internal static readonly Entry[] Entries =
        {
            new Entry(ColorLine("ramen"), "paint repeat", "深海拉面。", "Deep-Sea Ramen."),
            new Entry(ColorLine("octopus"), "paint repeat", "罐头章鱼。", "Tinned Octopus."),
            new Entry(ColorLine("moon"), "paint repeat", "月航信使。", "Moon Courier."),
            new Entry(FlagLine("ramen"), "repeat", "深海拉面旗。", "Deep-Sea Ramen flag."),
            new Entry(FlagLine("octopus"), "repeat", "罐头章鱼旗。", "Tinned Octopus flag."),
            new Entry(FlagLine("moon"), "repeat", "月航信使旗。", "Moon Courier flag."),
            new Entry(FailureLine, "", "PAINTER_NAME_KEY: 这次没能完成。换一种试试吧。", "PAINTER_NAME_KEY: I couldn't finish that. Let's try something else.")
        };

        internal static Program Build(byte[] original)
        {
            if (original == null || Digest(original) != SourceSha256)
                throw new InvalidOperationException("油漆匠对话版本与已核验的原版不一致。");
            Program program = Program.Parser.ParseFrom(original);
            Require(program.Nodes.Count == 953, "Unexpected original Yarn node count.");
            Node colors = RequirePage(program, "Painter_Customize_Color_Page_2", 111, 50, "line:062a122", "line:03f37d3");
            Node flags = RequirePage(program, "Painter_Customize_Flag2", 58, 24, "line:0f9ae17", "line:0591391");
            Require(program.Nodes["Painter_Customize_Color_Complete"].Instructions[2].Operands[0].StringValue ==
                "PassTime 2 feedback.pass-time-paint-boat", "Original paint duration changed.");
            AddNextPage(colors, "line:01b7c6c", ColorPage, true);
            AddNextPage(flags, "line:0f9e0dd", FlagPage, false);
            program.Nodes.Add(ColorPage, BuildPage(true));
            program.Nodes.Add(FlagPage, BuildPage(false));
            foreach (string id in SkinIds)
            {
                program.Nodes.Add(ColorSelected(id), BuildSelection(id, true));
                program.Nodes.Add(FlagSelected(id), BuildSelection(id, false));
            }
            return program;
        }

        private static Node RequirePage(Program program, string name, int count, int show, string previous, string back)
        {
            Node node;
            Require(program.Nodes.TryGetValue(name, out node) && node.Instructions.Count == count,
                "Original painter page structure changed: " + name);
            Require(node.Instructions[show].Opcode == Op.ShowOptions && node.Instructions[show + 1].Opcode == Op.Jump,
                "Original painter selection changed: " + name);
            Require(node.Instructions[show - 2].Opcode == Op.AddOption && node.Instructions[show - 2].Operands[0].StringValue == previous &&
                node.Instructions[show - 1].Opcode == Op.AddOption && node.Instructions[show - 1].Operands[0].StringValue == back,
                "Original painter navigation changed: " + name);
            return node;
        }

        private static void AddNextPage(Node node, string textId, string destination, bool color)
        {
            int insertion = node.Instructions.Select((instruction, index) => new { instruction, index })
                .Single(pair => pair.instruction.Opcode == Op.ShowOptions).index - 2;
            string branch = "JinkeNextPage", skip = "JinkeSkipNextPage", end = "JinkeEndNextPage";
            var added = new[]
            {
                Float(0), Instruction(Op.CallFunc, String(ReadyFunction)), Instruction(Op.JumpIfFalse, String(skip)),
                Instruction(Op.Pop), Option(textId, branch), Instruction(Op.JumpTo, String(end)), Instruction(Op.Pop)
            };
            foreach (string label in node.Labels.Keys.ToArray())
                if (node.Labels[label] >= insertion) node.Labels[label] += added.Length;
            for (int index = 0; index < added.Length; index++) node.Instructions.Insert(insertion + index, added[index]);
            node.Labels.Add(skip, insertion + 6);
            node.Labels.Add(end, insertion + added.Length);
            node.Labels.Add(branch, node.Instructions.Count);
            if (color)
            {
                Store(node, ReturnPath, destination);
                Go(node, "Painter_Customize_Color_Pre");
            }
            else Go(node, destination);
            node.Instructions.Add(Instruction(Op.Stop));
        }

        private static Node BuildPage(bool color)
        {
            var node = new Node { Name = color ? ColorPage : FlagPage };
            ReadyOrReturn(node, color ? "Painter_Customize_Color_Page_2" : "Painter_Customize_Flag2");
            Store(node, ReturnPath, node.Name);
            node.Instructions.Add(Command("ShowUnavailableOptions " + (color ? "false" : "true")));
            if (!color)
            {
                node.Instructions.Add(Command("AutoResolveNextLine"));
                node.Instructions.Add(Line("line:0fb4ef0"));
            }
            foreach (string id in SkinIds) node.Instructions.Add(Option(color ? ColorLine(id) : FlagLine(id), "Choose_" + id));
            node.Instructions.Add(Option(color ? "line:062a122" : "line:0f9ae17", "Previous"));
            node.Instructions.Add(Option(color ? "line:03f37d3" : "line:0591391", "Back"));
            node.Instructions.Add(Instruction(Op.ShowOptions));
            node.Instructions.Add(Instruction(Op.Jump));
            foreach (string id in SkinIds)
            {
                node.Labels.Add("Choose_" + id, node.Instructions.Count);
                Go(node, color ? ColorSelected(id) : FlagSelected(id));
            }
            node.Labels.Add("Previous", node.Instructions.Count);
            if (color)
            {
                Store(node, ReturnPath, "Painter_Customize_Color_Page_2");
                Go(node, "Painter_Customize_Color_Pre");
            }
            else Go(node, "Painter_Customize_Flag2");
            node.Labels.Add("Back", node.Instructions.Count);
            Go(node, color ? "Painter_Customize_Color_Area" : "Painter_Customize");
            node.Instructions.Add(Instruction(Op.Stop));
            return node;
        }

        private static Node BuildSelection(string id, bool color)
        {
            var node = new Node { Name = color ? ColorSelected(id) : FlagSelected(id) };
            ReadyOrReturn(node, color ? "Painter_Customize_Color_Page_2" : "Painter_Customize_Flag2");
            if (color)
            {
                node.Instructions.Add(Line("line:0c6dad2"));
                node.Instructions.Add(Command("PlayClip Paint_Boat"));
                node.Instructions.Add(Command("PassTime 2 feedback.pass-time-paint-boat"));
                node.Instructions.Add(Instruction(Op.PushVariable, String(Area)));
                node.Instructions.Add(Command(ColorCommand + " {0} " + id, 1));
            }
            else node.Instructions.Add(Command(FlagCommand + " " + id));
            node.Instructions.Add(Instruction(Op.PushVariable, String(ChoiceResult)));
            node.Instructions.Add(Instruction(Op.JumpIfFalse, String("Rejected")));
            node.Instructions.Add(Instruction(Op.Pop));
            if (!color) node.Instructions.Add(Command("PlayClip Change_Flag"));
            node.Instructions.Add(Line(color ? "line:0001e01" : "line:07941e3"));
            Go(node, color ? "Painter_Customize_Color_Area" : "Painter_Customize");
            node.Labels.Add("Rejected", node.Instructions.Count);
            node.Instructions.Add(Instruction(Op.Pop));
            node.Instructions.Add(Line(FailureLine));
            Go(node, color ? "Painter_Customize_Color_Area" : "Painter_Customize");
            node.Instructions.Add(Instruction(Op.Stop));
            return node;
        }

        private static void ReadyOrReturn(Node node, string fallback)
        {
            node.Instructions.Add(Float(0));
            node.Instructions.Add(Instruction(Op.CallFunc, String(ReadyFunction)));
            node.Instructions.Add(Instruction(Op.JumpIfFalse, String("Unavailable")));
            node.Instructions.Add(Instruction(Op.Pop));
            node.Instructions.Add(Instruction(Op.JumpTo, String("Ready")));
            node.Labels.Add("Unavailable", node.Instructions.Count);
            node.Instructions.Add(Instruction(Op.Pop));
            Go(node, fallback);
            node.Labels.Add("Ready", node.Instructions.Count);
        }

        private static void Store(Node node, string variable, string value)
        {
            node.Instructions.Add(Instruction(Op.PushString, String(value)));
            node.Instructions.Add(Instruction(Op.StoreVariable, String(variable)));
            node.Instructions.Add(Instruction(Op.Pop));
        }
        private static void Go(Node node, string name)
        { node.Instructions.Add(Instruction(Op.PushString, String(name))); node.Instructions.Add(Instruction(Op.RunNode)); }
        private static Instruction Command(string command, int substitutions = 0)
        { return Instruction(Op.RunCommand, String(command), Number(substitutions)); }
        private static Instruction Line(string textId) { return Instruction(Op.RunLine, String(textId), Number(0)); }
        private static Instruction Float(float number) { return Instruction(Op.PushFloat, Number(number)); }
        private static Instruction Option(string textId, string destination)
        { return Instruction(Op.AddOption, String(textId), String(destination), Number(0), new Operand { BoolValue = false }); }
        private static Operand String(string value) { return new Operand { StringValue = value }; }
        private static Operand Number(float value) { return new Operand { FloatValue = value }; }
        private static Instruction Instruction(Op code, params Operand[] operands)
        { var instruction = new Instruction { Opcode = code }; instruction.Operands.Add(operands); return instruction; }
        internal static string ColorLine(string id) { return "line:jinke-painter-color-" + id + "-v1"; }
        internal static string FlagLine(string id) { return "line:jinke-painter-flag-" + id + "-v1"; }
        private static string ColorSelected(string id) { return "Jinke_Painter_Color_" + id; }
        private static string FlagSelected(string id) { return "Jinke_Painter_Flag_" + id; }
        internal static string Digest(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
