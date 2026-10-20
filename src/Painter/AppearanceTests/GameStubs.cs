using System;
using System.Collections.Generic;

namespace Newtonsoft.Json { internal static class JsonConvert { internal static string SerializeObject(object value) { return System.Text.Json.JsonSerializer.Serialize(value); } } }
namespace UnityEngine
{
    internal class Object { }
    internal class Component : Object
    {
        internal Player Parent;
        public T GetComponentInParent<T>() where T : class { return Parent as T; }
    }
    internal struct Color
    {
        internal float r, g, b, a;
        internal Color(float red, float green, float blue) { r = red; g = green; b = blue; a = 1; }
        public static bool operator ==(Color x, Color y) { return x.r == y.r && x.g == y.g && x.b == y.b && x.a == y.a; }
        public static bool operator !=(Color x, Color y) { return !(x == y); }
        public override bool Equals(object value) { return value is Color && (Color)value == this; }
        public override int GetHashCode() { return r.GetHashCode() ^ g.GetHashCode() ^ b.GetHashCode(); }
    }
    internal class Texture : Object { internal string Name; internal Texture(string name) { Name = name; } }
    internal class Material : Object
    {
        private readonly Dictionary<int, Color> colors = new Dictionary<int, Color>();
        private readonly Dictionary<int, Texture> textures = new Dictionary<int, Texture>();
        internal bool HasProperty(int id) { return true; }
        internal Color GetColor(int id) { Color value; return colors.TryGetValue(id, out value) ? value : default(Color); }
        internal Texture GetTexture(int id) { Texture value; return textures.TryGetValue(id, out value) ? value : null; }
        internal void SetColor(int id, Color value) { colors[id] = value; }
        internal void SetTexture(int id, Texture value) { textures[id] = value; }
    }
    internal class MaterialPropertyBlock
    {
        private readonly Dictionary<int, Color> colors = new Dictionary<int, Color>();
        private readonly Dictionary<int, Texture> textures = new Dictionary<int, Texture>();
        internal bool HasColor(int id) { return colors.ContainsKey(id); }
        internal bool HasTexture(int id) { return textures.ContainsKey(id); }
        internal Color GetColor(int id) { return colors[id]; }
        internal Texture GetTexture(int id) { return textures[id]; }
        internal void SetColor(int id, Color value) { colors[id] = value; }
        internal void SetTexture(int id, Texture value) { textures[id] = value; }
        internal void CopyFrom(MaterialPropertyBlock source)
        {
            colors.Clear(); textures.Clear();
            foreach (var pair in source.colors) colors[pair.Key] = pair.Value;
            foreach (var pair in source.textures) textures[pair.Key] = pair.Value;
        }
    }
    internal class Renderer : Component
    {
        internal bool ThrowSet, Active;
        internal Material[] sharedMaterials = new[] { new Material() };
        private readonly MaterialPropertyBlock stored = new MaterialPropertyBlock();
        internal void GetPropertyBlock(MaterialPropertyBlock block, int slot) { if (slot != 0) throw new Exception("slot"); block.CopyFrom(stored); }
        internal void SetPropertyBlock(MaterialPropertyBlock block, int slot) { if (ThrowSet) throw new InvalidOperationException("render-failure"); if (slot != 0) throw new Exception("slot"); stored.CopyFrom(block); }
        internal Color EffectiveColor(int id) { return stored.HasColor(id) ? stored.GetColor(id) : sharedMaterials[0].GetColor(id); }
        internal Texture EffectiveTexture(int id) { return stored.HasTexture(id) ? stored.GetTexture(id) : sharedMaterials[0].GetTexture(id); }
    }
    internal sealed class MeshRenderer : Renderer { }
    internal sealed class SkinnedMeshRenderer : Renderer { }
    internal static class Shader
    {
        private static readonly Dictionary<string, int> names = new Dictionary<string, int>();
        internal static int PropertyToID(string name) { int id; if (!names.TryGetValue(name, out id)) { id = names.Count + 1; names[name] = id; } return id; }
    }
    internal static class Application { internal static string streamingAssetsPath = "/synthetic-assets"; }
    internal static class Time { internal static float unscaledTime; }
    internal static class Debug
    {
        internal static List<string> Logs = new List<string>();
        internal static void Log(string value) { Logs.Add(value); }
        internal static void LogWarning(string value) { Logs.Add(value); }
        internal static void LogError(string value) { Logs.Add(value); }
    }
}

internal sealed class SaveData
{
    internal readonly Dictionary<string, string> stringVariables = new Dictionary<string, string>();
    internal readonly Dictionary<string, bool> boolVariables = new Dictionary<string, bool>();
    internal readonly HashSet<string> availableDestinations = new HashSet<string> { "destination.lm-painter" };
    internal int RoofColorIndex = 1, HullColorIndex = 2, BoatFlagStyle;
    internal bool HasChangedBoatColors, ForbidSave;
    internal bool GetBoolVariable(string key) { bool value; return boolVariables.TryGetValue(key, out value) && value; }
}
internal sealed class Player : UnityEngine.Component { internal bool IsAlive = true, IsDocked = true; }
internal sealed class SaveManager { internal SaveData ActiveSaveData; }
internal sealed class GameEvents
{
    internal static GameEvents Instance;
    internal event Action<int, int> OnBoatColorsChanged;
    internal event Action<int> OnBoatFlagChanged;
    internal void TriggerBoatColorsChanged(int area, int index) { OnBoatColorsChanged?.Invoke(area, index); }
    internal void TriggerBoatFlagChanged(int index) { OnBoatFlagChanged?.Invoke(index); }
}
internal sealed class GameManager
{
    internal static GameManager Instance;
    internal bool IsPlaying = true;
    internal Player Player;
    internal SaveData SaveData;
    internal SaveManager SaveManager;
    internal DredgeDialogueRunner DialogueRunner;
}
internal sealed class PlayerColorCustomizer : UnityEngine.Component
{
    // These names and list shapes mirror the fields read by production reflection.
    private readonly List<UnityEngine.MeshRenderer> meshRenderers;
    private readonly List<UnityEngine.SkinnedMeshRenderer> buntingMeshRenderers;
    private readonly List<UnityEngine.SkinnedMeshRenderer> flagMeshRenderers;
    internal PlayerColorCustomizer(Player player, UnityEngine.MeshRenderer body, UnityEngine.SkinnedMeshRenderer bunting, UnityEngine.SkinnedMeshRenderer flag)
    { Parent = player; meshRenderers = new List<UnityEngine.MeshRenderer> { body }; buntingMeshRenderers = new List<UnityEngine.SkinnedMeshRenderer> { bunting }; flagMeshRenderers = new List<UnityEngine.SkinnedMeshRenderer> { flag }; }
    internal int References { get { return meshRenderers.Count + buntingMeshRenderers.Count + flagMeshRenderers.Count; } }
}
internal sealed class DredgeDialogueRunner
{
    internal int ColorCalls, FlagCalls;
    internal void ChangeBoatColor(int area, int index)
    {
        ColorCalls++;
        if (area == 0) GameManager.Instance.SaveData.RoofColorIndex = index;
        else if (area == 1) GameManager.Instance.SaveData.HullColorIndex = index;
        GameManager.Instance.SaveData.HasChangedBoatColors = true;
        GameEvents.Instance.TriggerBoatColorsChanged(area, index);
    }
    internal void ChangeBoatFlag(int index)
    { FlagCalls++; GameManager.Instance.SaveData.BoatFlagStyle = index; GameEvents.Instance.TriggerBoatFlagChanged(index); }
}

namespace Jinke.DredgeSkins
{
    internal sealed class LoadedSkin { internal ColorValue Value; internal UnityEngine.Color Hull { get { return Value.Hull; } } internal UnityEngine.Color Roof { get { return Value.Roof; } } }
    internal struct ColorValue { internal UnityEngine.Color Hull, Roof; }
    internal sealed class SkinAssets : IDisposable
    {
        internal static bool ThrowLoad, ThrowAtlas;
        private readonly Dictionary<string, LoadedSkin> skins = new Dictionary<string, LoadedSkin>();
        internal int AtlasCount { get; private set; }
        internal void Load(string root)
        {
            if (ThrowLoad) throw new InvalidOperationException("load-failure");
            int i = 1;
            foreach (string id in new[] { "ramen", "octopus", "moon" }) skins[id] = new LoadedSkin { Value = new ColorValue { Hull = new UnityEngine.Color(i++, 0, 0), Roof = new UnityEngine.Color(0, i, 0) } };
        }
        internal LoadedSkin Find(string id) { LoadedSkin value; return id != null && skins.TryGetValue(id, out value) ? value : null; }
        internal UnityEngine.Texture GetAtlas(UnityEngine.Texture native, LoadedSkin skin)
        { if (ThrowAtlas) throw new InvalidOperationException("atlas-failure"); AtlasCount++; return new UnityEngine.Texture("custom:" + skin.Hull.r); }
        internal void PruneAtlases() { }
        public void Dispose() { skins.Clear(); AtlasCount = 0; }
    }
}
