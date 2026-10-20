using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jinke.DredgeMerchantTravel.QA
{
    // Explicit API integration checks in the separately fingerprinted QA build.
    // Production never includes this probe, its commands, or its test fixtures.
    internal static class PainterProbe
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Methods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string PainterQA = "local.jinke.dredge.voyage.painter.qa.20261019";
        private const string PainterDestination = "destination.lm-painter";
        private const string LittleMarrow = "dock.little-marrow";
        private static readonly string[] Keys = { "jinke-painter-roof-v1", "jinke-painter-hull-v1", "jinke-painter-flag-v1" };

        internal static object Execute(JObject command, Type bootstrap, string output)
        {
            string action = (string)command["action"];
            if (action == "painter-snapshot") return Snapshot(bootstrap);
            Require(Application.identifier == PainterQA && Environment.GetEnvironmentVariable("JINKE_MERCHANT_QA") == "1",
                "Painter mutations require the explicit isolated Painter QA process");
            GameManager game = GameManager.Instance;
            Require(game != null && game.IsPlaying && game.Player != null && game.Player.IsDocked && game.SaveData != null,
                "Painter API checks require a loaded, docked QA save");
            if (action == "painter-fixture-seed-dock") return SeedNextLoadDock(game, bootstrap);
            if (action == "painter-ui-open") return OpenNativePainter(game, bootstrap);
            if (action == "painter-ui-select") return SelectNativeOption(game, command, bootstrap);
            if (action == "painter-ui-continue") return ContinueNativeLine(game, bootstrap);
            if (action == "painter-native-color")
            {
                int area = (int)command["area"], index = (int)command["index"];
                Require((area == 0 || area == 1) && index >= 0 && index <= 7 && game.SaveData.GetBoolVariable("has-paint-" + index),
                    "Only an already owned native paint is allowed");
                game.DialogueRunner.ChangeBoatColor(area, index);
            }
            else if (action == "painter-native-flag")
            {
                int index = (int)command["index"];
                Require(index >= 0 && index <= 7 && (index == 0 || game.SaveData.GetBoolVariable("has-flag-" + index)),
                    "Only no flag or an already owned native flag is allowed");
                game.DialogueRunner.ChangeBoatFlag(index);
            }
            else if (action == "painter-change-color" || action == "painter-change-flag")
            {
                Type appearance = bootstrap.Assembly.GetType("Jinke.DredgePainter.PainterAppearance", true);
                string id = (string)command["skin"];
                Require(id == "ramen" || id == "octopus" || id == "moon", "Unknown fixture skin");
                object[] args;
                string method;
                if (action == "painter-change-color")
                {
                    int area = (int)command["area"];
                    Require(area == 0 || area == 1, "Unknown native color area");
                    method = "ChangeColor"; args = new object[] { area, id };
                }
                else { method = "ChangeFlag"; args = new object[] { id }; }
                Require((bool)appearance.GetMethod(method, Methods).Invoke(null, args), "Production painter command rejected the fixture");
            }
            else throw new InvalidOperationException("Unknown Painter QA action");
            return new { scope = "Production command API; does not invoke original dialogue animation/time or represent physical input", state = Snapshot(bootstrap) };
        }

        private static object SeedNextLoadDock(GameManager game, Type bootstrap)
        {
            RequireNormalDock(game);
            SaveData save = game.SaveData;
            Require(save.availableDestinations.Contains(PainterDestination) && save.GetBoolVariable("has-visited-dock-" + LittleMarrow),
                "The isolated save must already have unlocked the painter and visited Little Marrow");
            Require(save.dockId == game.Player.CurrentDock.Data.Id, "A pending seed must be reloaded before another fixture change");
            string previousId = save.dockId;
            int previousSlot = save.dockSlotIndex;
            try
            {
                save.dockId = LittleMarrow;
                save.dockSlotIndex = 0;
                game.Save();
                Require(ReferenceEquals(save, game.SaveData) && save.dockId == LittleMarrow && save.dockSlotIndex == 0,
                    "The next-load dock seed was not retained by the native save");
            }
            catch
            {
                save.dockId = previousId;
                save.dockSlotIndex = previousSlot;
                throw;
            }
            Debug.Log("[PainterQA] NEXT_LOAD_DOCK_SEED actualDock=" + game.Player.CurrentDock.Data.Id + " savedDock=" + LittleMarrow + " slot=0");
            return new { scope = "Explicit QA fixture: native save of next-load dock seed only; no sailing, teleport, Player.Dock, or physical input",
                previousSeed = new { dockId = previousId, dockSlotIndex = previousSlot }, nextLoadDock = LittleMarrow, nextLoadSlot = 0,
                actualDockUnchanged = game.Player.CurrentDock.Data.Id, requiresNativeTitleAndLoad = true, state = Snapshot(bootstrap) };
        }

        private static object OpenNativePainter(GameManager game, Type bootstrap)
        {
            RequireNormalDock(game);
            RequireLittleMarrow(game);
            Require(game.SaveData.availableDestinations.Contains(PainterDestination), "The native painter must already be unlocked");
            var destinations = game.Player.CurrentDock.GetDestinations().Where(d => d != null && d.Id == PainterDestination).ToArray();
            Require(destinations.Length == 1, "The current dock must own exactly one native painter destination");
            var buttons = Object.FindObjectsOfType<DestinationButton>().Where(b => b != null && b.isActiveAndEnabled &&
                b.gameObject.activeInHierarchy && ReferenceEquals(b.destination, destinations[0])).ToArray();
            Require(buttons.Length == 1, "Exactly one visible native painter destination button is required");
            BasicButtonWrapper wrapper = buttons[0].BasicButtonWrapper;
            RequireClickable(wrapper);
            string text = string.Join(" | ", buttons[0].GetComponentsInChildren<TMP_Text>().Select(t => t.text).ToArray());
            Require(!string.IsNullOrEmpty(text), "The native painter destination must have visible text");
            decimal time = game.SaveData.Time;
            wrapper.OnClick();
            Debug.Log("[PainterQA] UI_CALLBACK destination=" + PainterDestination + " callback=BasicButtonWrapper.OnClick text=" + text);
            return new { scope = "Native destination-button callback API; original UI.ShowDestination and Yarn entry; not physical mouse/controller input",
                requested = "open-painter", visibleText = text, timeBefore = time, state = Snapshot(bootstrap) };
        }

        private static object SelectNativeOption(GameManager game, JObject command, Type bootstrap)
        {
            DredgeDialogueView view = RequirePainterDialogue(game);
            string text = (string)command["text"];
            Require(!string.IsNullOrEmpty(text) && text.Length <= 160, "An exact visible option text is required");
            var buttons = VisibleOptions(view).Where(b => OptionText(b) == text).ToArray();
            Require(buttons.Length == 1, "Exactly one visible native option must match the complete text");
            BasicButtonWrapper wrapper = buttons[0].BasicButtonWrapper;
            RequireClickable(wrapper);
            string node = game.DialogueRunner.CurrentNodeName;
            decimal time = game.SaveData.Time;
            wrapper.OnClick();
            Debug.Log("[PainterQA] UI_CALLBACK node=" + node + " callback=TextOptionButton.BasicButtonWrapper.OnClick text=" + text);
            return new { scope = "Visible native dialogue-option callback API; original Yarn selection, commands, animation, and time flow; not physical input",
                requested = "select-option", exactVisibleText = text, fromNode = node, timeBefore = time, state = Snapshot(bootstrap) };
        }

        private static object ContinueNativeLine(GameManager game, Type bootstrap)
        {
            DredgeDialogueView view = RequirePainterDialogue(game);
            Require(VisibleOptions(view).Length == 0, "Select an actual visible option instead of continuing while options are shown");
            var textField = Value<TMP_Text>(view, "dialogueTextField");
            Require(textField != null && textField.isActiveAndEnabled && textField.gameObject.activeInHierarchy && !string.IsNullOrEmpty(textField.text),
                "An actual visible native dialogue line is required");
            bool typing = Value<bool>(view, "isTypewriterRunning");
            if (typing)
                Require(Time.time > Value<float>(view, "lineDisplayTime") + Value<float>(view, "delayBeforeCanSkipTypewriter"),
                    "The native typewriter skip delay has not elapsed");
            else
                Require(Value<bool>(view, "needsDismissing") && Time.time > Value<float>(view, "lineCompleteTime") + Value<float>(view, "delayAfterLineCompleteBeforeCanContinue"),
                    "The native line is not yet ready to continue");
            var action = Value<DredgePlayerActionPress>(view, "continueLineAction");
            Require(action != null && action.OnPressComplete != null, "The native dialogue continue action is unavailable");
            string node = game.DialogueRunner.CurrentNodeName, text = textField.text;
            decimal time = game.SaveData.Time;
            action.OnPressComplete();
            Debug.Log("[PainterQA] UI_CALLBACK node=" + node + " callback=continueLineAction.OnPressComplete effect=" + (typing ? "skip-typewriter" : "continue-line"));
            return new { scope = "Native dialogue Continue action callback API with original typewriter/delay checks; not physical input or forced completion",
                requested = typing ? "skip-typewriter" : "continue-line", fromNode = node, visibleLine = text, timeBefore = time, state = Snapshot(bootstrap) };
        }

        private static void RequireNormalDock(GameManager game)
        {
            Require(!SceneLoader.BetweenScenes && !game.IsPaused && game.Player.IsAlive && game.SaveManager != null &&
                ReferenceEquals(game.SaveManager.ActiveSaveData, game.SaveData) && !game.SaveData.ForbidSave &&
                game.Player.CurrentDock != null && game.Player.CurrentDock.Data != null && game.UI != null && game.Input != null &&
                game.DialogueRunner != null && !game.DialogueRunner.IsDialogueRunning && !game.DialogueRunner.Dialogue.IsActive &&
                game.Input.GetActiveActionLayer() == ActionLayer.DOCKED && game.UI.ShowingWindowTypes.Contains(UIWindowType.DOCK) &&
                game.UI.CurrentDestinationUI == null && game.UI.DockUI != null && game.UI.DockUI.isActiveAndEnabled &&
                game.UI.DockUI.gameObject.activeInHierarchy && !game.Time.IsTimePassingForcefully(),
                "Require the normal visible dock screen, without pause, dialogue, active destination, forced time, or transition");
        }

        private static void RequireLittleMarrow(GameManager game)
        {
            Require(game.Player.CurrentDock != null && game.Player.CurrentDock.Data != null && game.Player.CurrentDock.Data.Id == LittleMarrow &&
                game.SaveData.dockId == LittleMarrow, "Painter UI callbacks require actual Little Marrow docking and a matching saved dock seed");
        }

        private static DredgeDialogueView RequirePainterDialogue(GameManager game)
        {
            RequireLittleMarrow(game);
            Require(!SceneLoader.BetweenScenes && !game.IsPaused && game.Player.IsAlive && !game.SaveData.ForbidSave && game.UI != null &&
                game.DialogueRunner != null && game.DialogueRunner.IsDialogueRunning && game.DialogueRunner.Dialogue.IsActive &&
                game.Input.GetActiveActionLayer() == ActionLayer.DIALOGUE && game.UI.ShowingWindowTypes.Contains(UIWindowType.DIALOGUE),
                "Require a running native painter dialogue in the native dialogue input layer");
            BaseDestinationUI destinationUI = game.UI.CurrentDestinationUI;
            // Native CharacterDestinationUI starts Yarn before its carrier GameObject is activated.
            Require(destinationUI is CharacterDestinationUI && game.UI.ShowingWindowTypes.Contains(UIWindowType.CHARACTER_DESTINATION),
                "The current native character destination window must belong to the painter dialogue");
            var destination = typeof(BaseDestinationUI).GetField("destination", Fields).GetValue(destinationUI) as BaseDestination;
            Require(destination != null && destination.Id == PainterDestination, "The current native destination is not the painter");
            string node = game.DialogueRunner.CurrentNodeName;
            Require(node != null && (node.StartsWith("Painter_", StringComparison.Ordinal) || node.StartsWith("Jinke_Painter_", StringComparison.Ordinal)),
                "Only an actual native or appended painter node is allowed");
            var views = game.DialogueRunner.dialogueViews.OfType<DredgeDialogueView>().Where(v => v != null && v.isActiveAndEnabled && v.gameObject.activeInHierarchy).ToArray();
            Require(views.Length == 1, "Exactly one active runner-owned native dialogue view is required");
            return views[0];
        }

        private static TextOptionButton[] VisibleOptions(DredgeDialogueView view)
        {
            var values = Value<IEnumerable>(view, "allButtons");
            Require(values != null, "The native dialogue option list is unavailable");
            return values.Cast<object>().OfType<TextOptionButton>().Where(b => b != null && b.isActiveAndEnabled && b.gameObject.activeInHierarchy).ToArray();
        }

        private static string OptionText(TextOptionButton button)
        {
            TMP_Text text = Value<TMP_Text>(button, "textField");
            return text == null ? null : text.text;
        }

        private static void RequireClickable(BasicButtonWrapper wrapper)
        {
            Require(wrapper != null && wrapper.isActiveAndEnabled && wrapper.gameObject.activeInHierarchy && wrapper.Button != null &&
                wrapper.Button.IsActive() && wrapper.Button.IsInteractable() && wrapper.Interactable && Value<bool>(wrapper, "canBeClicked") &&
                wrapper.OnClick != null, "The native button must be visible, available, and past its original click delay");
            foreach (CanvasGroup group in wrapper.GetComponentsInParent<CanvasGroup>())
                Require(group.alpha > 0 && group.interactable && group.blocksRaycasts, "The native button is hidden or blocked by a canvas group");
        }

        private static T Value<T>(object owner, string name)
        {
            FieldInfo field = owner.GetType().GetField(name, Fields);
            Require(field != null, "Verified native UI field is unavailable: " + name);
            return (T)field.GetValue(owner);
        }

        internal static object Snapshot(Type bootstrap)
        {
            GameManager game = GameManager.Instance;
            SaveData save = game == null ? null : game.SaveData;
            Type appearance = bootstrap.Assembly.GetType("Jinke.DredgePainter.PainterAppearance", true);
            string diagnostics = (string)appearance.GetMethod("Diagnostics", Methods).Invoke(null, null);
            var owners = Object.FindObjectsOfType<PlayerColorCustomizer>(true);
            return new
            {
                diagnostics = JToken.Parse(diagnostics),
                native = save == null ? null : new
                {
                    save.RoofColorIndex, save.HullColorIndex, save.BoatFlagStyle, save.HasChangedBoatColors,
                    time = save.Time, funds = save.Funds, save.ForbidSave, save.dockId,
                    save.dockSlotIndex,
                    actualDock = game.Player == null || game.Player.CurrentDock == null || game.Player.CurrentDock.Data == null ? null : game.Player.CurrentDock.Data.Id,
                    paints = Enumerable.Range(0, 8).Select(i => save.GetBoolVariable("has-paint-" + i)).ToArray(),
                    flags = Enumerable.Range(1, 7).Select(i => save.GetBoolVariable("has-flag-" + i)).ToArray(),
                    selections = Keys.Where(k => save.stringVariables.ContainsKey(k)).ToDictionary(k => k, k => save.stringVariables[k])
                },
                dialogue = DialogueSnapshot(game, bootstrap),
                renderers = owners.SelectMany(o => new[] { "meshRenderers", "buntingMeshRenderers", "flagMeshRenderers" }
                    .SelectMany(field => Renderers(o, field).Select(r => RendererSnapshot(r, field)))).ToArray(),
                f8WindowComponents = Object.FindObjectsOfType<MonoBehaviour>(true)
                    .Where(c => c != null && (c.GetType().FullName == "Jinke.DredgeSkins.SkinController" || c.GetType().FullName == "Jinke.DredgeSkins.PauseUiGuard"))
                    .Select(c => c.GetType().FullName).ToArray()
            };
        }

        private static object DialogueSnapshot(GameManager game, Type bootstrap)
        {
            if (game == null || game.DialogueRunner == null) return null;
            DredgeDialogueRunner runner = game.DialogueRunner;
            Type integration = bootstrap.Assembly.GetType("Jinke.DredgePainter.PainterDialogue", true);
            object session = integration.GetField("current", Methods).GetValue(null);
            BaseDestinationUI destinationUI = game.UI == null ? null : game.UI.CurrentDestinationUI;
            BaseDestination destination = destinationUI == null ? null : typeof(BaseDestinationUI).GetField("destination", Fields).GetValue(destinationUI) as BaseDestination;
            var line = typeof(Yarn.Unity.DialogueRunner).GetProperty("CurrentLine", Fields).GetValue(runner, null) as Yarn.Unity.LocalizedLine;
            return new
            {
                runner.IsDialogueRunning, active = runner.Dialogue.IsActive, node = runner.PreviousNodeName,
                currentNode = runner.CurrentNodeName, destination = destination == null ? null : destination.Id,
                destinationUI = destinationUI == null ? null : new
                {
                    type = destinationUI.GetType().FullName, destinationUI.isActiveAndEnabled,
                    gameObjectActive = destinationUI.gameObject.activeInHierarchy,
                    nativeCharacterWindowShown = game.UI.ShowingWindowTypes.Contains(UIWindowType.CHARACTER_DESTINATION)
                },
                inputLayer = game.Input == null ? null : game.Input.GetActiveActionLayer().ToString(),
                windows = game.UI == null ? null : game.UI.ShowingWindowTypes.Select(w => w.ToString()).ToArray(),
                currentLine = line == null ? null : new { line.TextID, line.RawText, line.CharacterName },
                injection = new
                {
                    sessionPresent = session != null,
                    runnerMatchesSession = session != null && ReferenceEquals(Value<object>(session, "Runner"), runner),
                    localized = session != null && Value<bool>(session, "Localized"),
                    projectName = runner.yarnProject == null ? null : runner.yarnProject.name,
                    compiledBytes = runner.yarnProject == null || runner.yarnProject.compiledYarnProgram == null ? 0 : runner.yarnProject.compiledYarnProgram.Length,
                    customColorPageLoaded = game.IsPlaying && runner.NodeExists("Jinke_Painter_Color_Page_3"),
                    customFlagPageLoaded = game.IsPlaying && runner.NodeExists("Jinke_Painter_Flag3"),
                    customColorMetadata = runner.yarnProject == null || runner.yarnProject.lineMetadata == null ? null :
                        runner.yarnProject.lineMetadata.GetMetadata("line:jinke-painter-color-ramen-v1")
                },
                views = runner.dialogueViews.OfType<DredgeDialogueView>().Where(v => v != null).Select(v => new
                {
                    active = v.isActiveAndEnabled && v.gameObject.activeInHierarchy,
                    text = Value<TMP_Text>(v, "dialogueTextField").text,
                    typing = Value<bool>(v, "isTypewriterRunning"), needsDismissing = Value<bool>(v, "needsDismissing"),
                    lineDisplayTime = Value<float>(v, "lineDisplayTime"), lineCompleteTime = Value<float>(v, "lineCompleteTime"),
                    now = Time.time,
                    options = VisibleOptions(v).Select(b => new
                    {
                        text = OptionText(b), b.HasQuickExit, available = b.BasicButtonWrapper.Interactable,
                        canBeClicked = Value<bool>(b.BasicButtonWrapper, "canBeClicked"), rect = Rect(b.transform as RectTransform)
                    }).ToArray()
                }).ToArray(),
                options = Object.FindObjectsOfType<TextOptionButton>().Where(b => b.gameObject.activeInHierarchy)
                    .Select(b => new { text = b.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text).ToArray(),
                        b.HasQuickExit, available = b.BasicButtonWrapper.Interactable,
                        canBeClicked = Value<bool>(b.BasicButtonWrapper, "canBeClicked"), rect = Rect(b.transform as RectTransform) }).ToArray()
            };
        }

        private static Renderer[] Renderers(PlayerColorCustomizer owner, string name)
        {
            IEnumerable values = typeof(PlayerColorCustomizer).GetField(name, Fields).GetValue(owner) as IEnumerable;
            return values == null ? new Renderer[0] : values.Cast<object>().OfType<Renderer>().Where(r => r != null).ToArray();
        }
        private static object RendererSnapshot(Renderer renderer, string category)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
            Material[] materials = renderer.sharedMaterials;
            Material material = materials.Length == 0 ? null : materials[0];
            int hull = Shader.PropertyToID("_Hull_Color"), roof = Shader.PropertyToID("_Roof_Color");
            int flag = Shader.PropertyToID("Texture2D_9aa7ba2263944b48bbf43c218dc48459");
            Texture texture = block.HasTexture(flag) ? block.GetTexture(flag) : material != null && material.HasProperty(flag) ? material.GetTexture(flag) : null;
            return new
            {
                name = renderer.name, id = renderer.GetInstanceID(), category, renderer.enabled,
                active = renderer.gameObject.activeInHierarchy, renderer.isVisible,
                hull = ColorValue(block, material, hull), roof = ColorValue(block, material, roof),
                flagTexture = texture == null ? null : new { texture.name, id = texture.GetInstanceID(), texture.width, texture.height },
                materials = materials.Select(m => m == null ? null : new
                {
                    m.name, id = m.GetInstanceID(), shader = m.shader == null ? null : m.shader.name,
                    light = m.HasProperty("_LightStrength") ? (float?)m.GetFloat("_LightStrength") : null
                }).ToArray()
            };
        }
        private static float[] ColorValue(MaterialPropertyBlock block, Material material, int property)
        {
            if (!block.HasColor(property) && (material == null || !material.HasProperty(property))) return null;
            Color color = block.HasColor(property) ? block.GetColor(property) : material.GetColor(property);
            return new[] { color.r, color.g, color.b, color.a };
        }
        private static float[] Rect(RectTransform rect)
        {
            if (rect == null) return null;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return new[] { corners[0].x, corners[0].y, corners[2].x, corners[2].y };
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
