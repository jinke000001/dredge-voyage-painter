using System;
using System.Linq;
using Jinke.DredgePainter;
using Jinke.DredgeSkins;
using UnityEngine;

internal static class Program
{
    private static int passed;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); passed++; Console.WriteLine("PASS " + name); }
    private sealed class World
    {
        internal SaveData Save;
        internal GameManager Game;
        internal PlayerColorCustomizer Owner;
        internal readonly MeshRenderer Body = new MeshRenderer();
        internal readonly SkinnedMeshRenderer Bunting = new SkinnedMeshRenderer(), Flag = new SkinnedMeshRenderer();
        internal World(bool ready = true, bool nativeSubscribersFirst = true)
        {
            PainterAppearance.ResetSession(); SkinAssets.ThrowLoad = SkinAssets.ThrowAtlas = false;
            Save = new SaveData(); Save.boolVariables["has-flag-3"] = true;
            var player = new Player();
            Game = new GameManager { SaveData = Save, SaveManager = new SaveManager { ActiveSaveData = Save }, Player = player, DialogueRunner = new DredgeDialogueRunner() };
            GameManager.Instance = Game; GameEvents.Instance = new GameEvents(); Time.unscaledTime = 0;
            Owner = new PlayerColorCustomizer(player, Body, Bunting, Flag);
            if (nativeSubscribersFirst) SubscribeNative();
            PainterAppearance.OnSaveReady(Save);
            if (!nativeSubscribersFirst) SubscribeNative();
            Body.sharedMaterials[0].SetTexture(ShaderFields.Flag, new Texture("unused"));
            Flag.sharedMaterials[0].SetTexture(ShaderFields.Flag, new Texture("native3"));
            if (ready) { NativeColors(0, Save.RoofColorIndex); NativeFlag(Save.BoatFlagStyle); }
        }
        private void SubscribeNative()
        { GameEvents.Instance.OnBoatColorsChanged += NativeColors; GameEvents.Instance.OnBoatFlagChanged += NativeFlag; }
        private void NativeColors(int area, int index)
        {
            foreach (Renderer renderer in new Renderer[] { Body, Bunting })
            {
                renderer.sharedMaterials[0].SetColor(ShaderFields.Hull, new Color(Save.HullColorIndex / 10f, 0, 0));
                renderer.sharedMaterials[0].SetColor(ShaderFields.Roof, new Color(0, Save.RoofColorIndex / 10f, 0));
            }
            PainterAppearance.OnColors(Owner);
        }
        private void NativeFlag(int index)
        {
            Flag.Active = Save.BoatFlagStyle != 0;
            if (Save.BoatFlagStyle != 0) Flag.sharedMaterials[0].SetTexture(ShaderFields.Flag, new Texture("native" + Save.BoatFlagStyle));
            PainterAppearance.OnFlag(Owner);
        }
    }
    public static void Main()
    {
        State(); PaletteCompatibility();
        foreach (bool nativeFirst in new[] { true, false }) NativeChoices(nativeFirst);
        Failures(); Session();
        Console.WriteLine("TOTAL " + passed);
    }
    private static void State()
    {
        var save = new SaveData(); var state = new PainterState(save);
        Check(state.Color(0) == "original" && state.Color(1) == "original" && state.Flag == "original" && save.stringVariables.Count == 0, "empty state is original without save writes");
        foreach (string id in new[] { "ramen", "octopus", "moon" })
        {
            state.SetColor(0, id); state.SetColor(1, id); state.SetFlag(id);
            Check(state.Color(0) == id && state.Color(1) == id && state.Flag == id, "known state roundtrip " + id);
        }
        state.SetColor(0, "ramen"); state.ClearColor(1);
        Check(state.Color(0) == "ramen" && state.Color(1) == "original" && state.Flag == "moon", "area reset leaves other selections intact");
        save.stringVariables[PainterState.RoofKey] = "future-skin";
        Check(state.Color(0) == "original" && save.stringVariables[PainterState.RoofKey] == "future-skin", "unknown saved ID fails closed without read mutation");
        var previous = state.CaptureColor(0); state.SetColor(0, "moon"); state.RestoreColor(0, previous);
        Check(save.stringVariables[PainterState.RoofKey] == "future-skin", "rollback preserves raw prior ID");
        previous = state.CaptureColor(1); state.SetColor(1, "moon"); state.RestoreColor(1, previous);
        Check(!save.stringVariables.ContainsKey(PainterState.HullKey), "rollback preserves absent key");
        bool rejected = false; try { state.SetFlag("original"); } catch (ArgumentException) { rejected = true; }
        Check(rejected && state.Flag == "moon", "only three custom IDs can be written");
    }
    private static void PaletteCompatibility()
    {
        var renderer = new MeshRenderer();
        renderer.sharedMaterials[0].SetColor(ShaderFields.Hull, new Color(.2f, 0, 0));
        renderer.sharedMaterials[0].SetColor(ShaderFields.Roof, new Color(0, .3f, 0));
        var skin = new LoadedSkin { Value = new ColorValue { Hull = new Color(1, 0, 0), Roof = new Color(0, 1, 0) } };
        var patch = new RendererOverrides(renderer);
        patch.ApplyColors(skin);
        Check(renderer.EffectiveColor(ShaderFields.Hull) == skin.Hull && renderer.EffectiveColor(ShaderFields.Roof) == skin.Roof, "standalone ApplyColors still sets both colors");
        patch.ApplyPalette(skin, null);
        Check(renderer.EffectiveColor(ShaderFields.Hull) == skin.Hull && renderer.EffectiveColor(ShaderFields.Roof) == new Color(0, .3f, 0), "independent palette can restore only roof");
        patch.Restore();
        Check(renderer.EffectiveColor(ShaderFields.Hull) == new Color(.2f, 0, 0) && renderer.EffectiveColor(ShaderFields.Roof) == new Color(0, .3f, 0), "palette restores original material colors");
    }
    private static void NativeChoices(bool nativeFirst)
    {
        var world = new World(nativeSubscribersFirst: nativeFirst);
        string order = nativeFirst ? "native-first" : "painter-first";
        int beforeReady = world.Save.stringVariables.Count;
        Check(PainterAppearance.Ready && world.Save.stringVariables.Count == beforeReady, "ready query does not mutate save " + order);
        Check(PainterAppearance.ChangeColor(0, "ramen") && world.Body.EffectiveColor(ShaderFields.Roof) == new Color(0, 2, 0) && world.Body.EffectiveColor(ShaderFields.Hull) == new Color(.2f, 0, 0), "roof selection preserves native hull " + order);
        Check(PainterAppearance.ChangeColor(1, "moon") && world.Body.EffectiveColor(ShaderFields.Roof) == new Color(0, 2, 0) && world.Body.EffectiveColor(ShaderFields.Hull) == new Color(3, 0, 0), "hull selection preserves custom roof " + order);
        world.Game.DialogueRunner.ChangeBoatColor(0, world.Save.RoofColorIndex);
        Check(!world.Save.stringVariables.ContainsKey(PainterState.RoofKey) && world.Save.stringVariables[PainterState.HullKey] == "moon" && world.Body.EffectiveColor(ShaderFields.Roof) == new Color(0, .1f, 0), "same native roof index explicitly clears custom roof " + order);
        Check(PainterAppearance.ChangeFlag("ramen") && world.Save.BoatFlagStyle == 3 && world.Flag.Active && world.Save.stringVariables[PainterState.FlagKey] == "ramen", "new flag equips owned native fallback from zero " + order);
        Check(PainterAppearance.ChangeFlag("moon") && world.Save.stringVariables[PainterState.FlagKey] == "moon" && world.Flag.EffectiveTexture(ShaderFields.Flag).Name == "custom:3", "native event does not clear final custom selection " + order);
        world.Game.DialogueRunner.ChangeBoatFlag(3);
        Check(!world.Save.stringVariables.ContainsKey(PainterState.FlagKey) && world.Flag.EffectiveTexture(ShaderFields.Flag).Name == "native3", "same native flag index clears custom flag " + order);
        PainterAppearance.ChangeFlag("octopus"); world.Game.DialogueRunner.ChangeBoatFlag(0);
        Check(!world.Flag.Active && !world.Save.stringVariables.ContainsKey(PainterState.FlagKey), "native zero unmounts flag " + order);
        Check(world.Save.boolVariables.Keys.SequenceEqual(new[] { "has-flag-3" }) && world.Save.RoofColorIndex == 1 && world.Save.HullColorIndex == 2, "no native collection or color indexing expansion " + order);
    }
    private static void Failures()
    {
        var world = new World(); world.Save.ForbidSave = true;
        Check(!PainterAppearance.ChangeFlag("moon") && world.Game.DialogueRunner.FlagCalls == 0 && world.Save.stringVariables.Count == 0, "forbidden save rejects before native changes");
        world = new World(ready: false); SkinAssets.ThrowLoad = true; PainterAppearance.OnColors(world.Owner); PainterAppearance.OnFlag(world.Owner);
        Check(!PainterAppearance.Ready && world.Save.stringVariables.Count == 0, "failed assets hide painter additions without save writes");
        Check(!PainterAppearance.ChangeColor(0, "moon") && world.Save.RoofColorIndex == 1 && !world.Save.HasChangedBoatColors && world.Save.stringVariables.Count == 0, "resource load failure never writes a selection");
        world = new World(); world.Body.ThrowSet = true;
        bool laterSubscriber = false; GameEvents.Instance.OnBoatColorsChanged += (area, index) => laterSubscriber = true;
        bool escaped = false; try { world.Game.DialogueRunner.ChangeBoatColor(0, 1); } catch { escaped = true; }
        Check(!escaped && laterSubscriber, "painter event failures do not interrupt original subscribers");
        world.Body.ThrowSet = false;
        world = new World(); PainterAppearance.ChangeFlag("ramen"); SkinAssets.ThrowAtlas = true;
        Check(!PainterAppearance.ChangeFlag("moon") && world.Save.BoatFlagStyle == 3 && world.Save.stringVariables[PainterState.FlagKey] == "ramen", "failed flag transaction restores index and previous selection");
        world = new World(); world.Body.ThrowSet = true;
        Check(!PainterAppearance.ChangeColor(1, "moon") && world.Save.HullColorIndex == 2 && !world.Save.HasChangedBoatColors && world.Save.stringVariables.Count == 0, "failed color transaction restores native changed flag and absent key");
        world.Body.ThrowSet = false;
    }
    private static void Session()
    {
        var first = new World(); PainterAppearance.ChangeColor(0, "moon"); PainterAppearance.ChangeFlag("ramen");
        first.Game.IsPlaying = false;
        Check(!PainterAppearance.Ready, "ready closes immediately when world stops playing");
        PainterAppearance.Tick();
        Check(first.Body.EffectiveColor(ShaderFields.Roof) == new Color(0, .1f, 0) && first.Flag.EffectiveTexture(ShaderFields.Flag).Name == "native3", "return to title restores renderers");
        Check(first.Save.stringVariables[PainterState.RoofKey] == "moon" && first.Save.stringVariables[PainterState.FlagKey] == "ramen", "session cleanup does not erase saved selections");
        var next = new SaveData(); next.boolVariables["has-flag-3"] = true; first.Game.SaveData = next; first.Game.SaveManager.ActiveSaveData = next;
        PainterAppearance.OnSaveReady(next); GameEvents.Instance.TriggerBoatFlagChanged(0);
        Check(next.stringVariables.Count == 0 && first.Save.stringVariables[PainterState.FlagKey] == "ramen", "new save does not inherit or erase prior save preferences");
        Check(PainterAppearance.Diagnostics().Contains("\"roof\":\"original\"") && PainterAppearance.Diagnostics().Contains("\"flag\":\"original\""), "diagnostics follow active save only");
        PainterAppearance.ResetSession();
    }
}
