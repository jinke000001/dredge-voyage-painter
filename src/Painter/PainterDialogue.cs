using System;
using System.Collections.Generic;
using System.Reflection;
using Google.Protobuf;
using UnityEngine;
using UnityEngine.Localization.Settings;
using Yarn.Unity;

namespace Jinke.DredgePainter
{
    internal static class PainterDialogue
    {
        private sealed class Session
        {
            internal DredgeDialogueRunner Runner;
            internal YarnProject Project;
            internal bool Localized;
        }
        private static Session current;

        // Runs after the native Awake registrations and before the native base Start loads the project.
        internal static void OnRunnerAwake(object owner)
        {
            var runner = owner as DredgeDialogueRunner;
            if (runner == null || (current != null && ReferenceEquals(current.Runner, runner))) return;
            YarnProject clone = null;
            bool colorAdded = false, flagAdded = false, readyAdded = false;
            try
            {
                YarnProject original = runner.yarnProject;
                if (original == null) throw new InvalidOperationException("原版油漆匠对话尚未准备好。");
                if (runner.IsDialogueRunning || runner.Dialogue.IsActive) throw new InvalidOperationException("正在运行对话，保留原版内容。");
                var handlers = CommandHandlers(runner);
                if (handlers.ContainsKey(PainterDialogueProgram.ColorCommand) || handlers.ContainsKey(PainterDialogueProgram.FlagCommand) ||
                    runner.Dialogue.Library.FunctionExists(PainterDialogueProgram.ReadyFunction))
                    throw new InvalidOperationException("油漆匠扩展接口重复，保留原版内容。");
                Yarn.Program patched = PainterDialogueProgram.Build(original.compiledYarnProgram);
                LineMetadata metadata = PainterDialogueMetadata.CopyWithEntries(original.lineMetadata);
                clone = UnityEngine.Object.Instantiate(original);
                clone.name = original.name + " (Painter)";
                clone.hideFlags = HideFlags.DontSave;
                clone.compiledYarnProgram = patched.ToByteArray();
                clone.lineMetadata = metadata;
                var staged = new Session { Runner = runner, Project = clone };
                runner.AddCommandHandler<int, string>(PainterDialogueProgram.ColorCommand, (area, id) =>
                    RunChoice(staged, () => PainterAppearance.ChangeColor(area, id)));
                colorAdded = true;
                runner.AddCommandHandler<string>(PainterDialogueProgram.FlagCommand, id =>
                    RunChoice(staged, () => PainterAppearance.ChangeFlag(id)));
                flagAdded = true;
                runner.AddFunction<bool>(PainterDialogueProgram.ReadyFunction, () =>
                    ReferenceEquals(current, staged) && staged.Localized && PainterAppearance.Ready);
                readyAdded = true;
                Session previous = current;
                runner.yarnProject = clone;
                current = staged;
                clone = null;
                Release(previous);
                Debug.Log("[Painter] DIALOGUE_PREPARED native-pages=3 original-nodes=953");
            }
            catch (Exception error)
            {
                if (colorAdded) runner.RemoveCommandHandler(PainterDialogueProgram.ColorCommand);
                if (flagAdded) runner.RemoveCommandHandler(PainterDialogueProgram.FlagCommand);
                if (readyAdded) runner.RemoveFunction(PainterDialogueProgram.ReadyFunction);
                if (clone != null) UnityEngine.Object.Destroy(clone);
                Debug.LogError("[Painter] NATIVE_DIALOGUE_DISABLED " + error);
            }
        }

        // Entries are refreshed before a dialogue begins, so changing the game's language keeps native buttons usable.
        internal static void BeforeDialogue(object owner)
        {
            Session session = current;
            if (session == null || !ReferenceEquals(session.Runner, owner)) return;
            session.Localized = false;
            try
            {
                var locale = LocalizationSettings.SelectedLocale;
                if (locale == null) throw new InvalidOperationException("游戏语言尚未准备好。");
                var table = LocalizationSettings.StringDatabase.GetTable(LanguageManager.YARN_TABLE, locale);
                if (table == null) throw new InvalidOperationException("原版油漆匠文字表尚未准备好。");
                bool chinese = locale.Identifier.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                foreach (PainterDialogueProgram.Entry text in PainterDialogueProgram.Entries)
                {
                    string value = chinese ? text.Chinese : text.English;
                    var entry = table.GetEntry(text.Id);
                    if (entry == null) table.AddEntry(text.Id, value); else entry.Value = value;
                    if (table.GetEntry(text.Id) == null || table.GetEntry(text.Id).Value != value)
                        throw new InvalidOperationException("油漆匠文字未能加载。");
                }
                session.Localized = true;
            }
            catch (Exception error) { Debug.LogWarning("[Painter] NATIVE_TEXT_UNAVAILABLE " + error.Message); }
        }

        private static void RunChoice(Session session, Func<bool> choice)
        {
            bool success = false;
            try
            {
                if (ReferenceEquals(current, session) && session.Localized && PainterAppearance.Ready) success = choice();
            }
            catch (Exception error) { Debug.LogError("[Painter] NATIVE_CHOICE_FAILED " + error); }
            session.Runner.VariableStorage.SetValue(PainterDialogueProgram.ChoiceResult, success);
        }

        private static IDictionary<string, Delegate> CommandHandlers(DredgeDialogueRunner runner)
        {
            FieldInfo field = typeof(DialogueRunner).GetField("commandHandlers", BindingFlags.NonPublic | BindingFlags.Instance);
            var handlers = field == null ? null : field.GetValue(runner) as IDictionary<string, Delegate>;
            if (handlers == null) throw new InvalidOperationException("原版对话命令接口不匹配。");
            return handlers;
        }

        private static void Release(Session session)
        {
            if (session == null) return;
            session.Localized = false;
            try
            {
                if (session.Runner != null)
                {
                    session.Runner.RemoveCommandHandler(PainterDialogueProgram.ColorCommand);
                    session.Runner.RemoveCommandHandler(PainterDialogueProgram.FlagCommand);
                    session.Runner.RemoveFunction(PainterDialogueProgram.ReadyFunction);
                }
                if (session.Project != null) UnityEngine.Object.Destroy(session.Project);
            }
            catch (Exception error) { Debug.LogWarning("[Painter] OLD_DIALOGUE_CLEANUP_FAILED " + error.Message); }
        }
    }
}
