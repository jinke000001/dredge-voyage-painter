using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        internal string name;
        public static void Destroy(Object value) { value.Destroyed = true; }
        public static bool operator ==(Object a, Object b)
        { return ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed)); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    internal class Component : Object
    {
        internal GameObject gameObject;
        internal Transform transform { get { return gameObject.transform; } }
        internal T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
    }
    internal sealed class Transform : Component
    {
        internal readonly Dictionary<string, Transform> Children = new Dictionary<string, Transform>();
        internal Transform Find(string key) { Transform value; return Children.TryGetValue(key, out value) ? value : null; }
    }
    internal sealed class GameObject : Object
    {
        private readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();
        internal bool activeInHierarchy = true;
        internal readonly Transform transform;
        internal GameObject(string label) { name = label; transform = new Transform { gameObject = this }; }
        internal T Add<T>(T value) where T : Component { value.gameObject = this; components[typeof(T)] = value; return value; }
        internal T GetComponent<T>() where T : class { Component value; return components.TryGetValue(typeof(T), out value) ? value as T : null; }
    }
    internal sealed class Mesh : Object { internal int subMeshCount = 2; }
    internal sealed class MeshFilter : Component
    {
        private Mesh value;
        internal bool ThrowOnce;
        internal Mesh sharedMesh
        {
            get { return value; }
            set { if (ThrowOnce) { ThrowOnce = false; throw new InvalidOperationException("upload-failure"); } this.value = value; }
        }
    }
    internal sealed class Material : Object
    {
        internal bool Valid = true;
        internal readonly Dictionary<string, float> Values = new Dictionary<string, float>();
        internal readonly Dictionary<int, Texture> Textures = new Dictionary<int, Texture>();
        internal Material()
        {
            Textures[Shader.PropertyToID(Jinke.DredgeShipAppearance.Boat5WindowOverrides.AlbedoName)] = new Texture { name = "NativeAlbedo" };
            Textures[Shader.PropertyToID(Jinke.DredgeShipAppearance.Boat5WindowOverrides.EmissionName)] = new Texture { name = "NativeEmission" };
        }
        internal bool HasProperty(string key) { return Valid; }
        internal bool HasProperty(int key) { return Valid; }
        internal Texture GetTexture(int key) { Texture value; return Textures.TryGetValue(key, out value) ? value : null; }
    }
    internal class Texture : Object { }
    internal sealed class Texture2D : Texture { }
    internal static class Shader
    {
        private static readonly Dictionary<string, int> ids = new Dictionary<string, int>();
        internal static int PropertyToID(string name)
        { int id; if (!ids.TryGetValue(name, out id)) { id = ids.Count + 1; ids.Add(name, id); } return id; }
    }
    internal sealed class MaterialPropertyBlock
    {
        internal readonly Dictionary<int, Texture> Textures = new Dictionary<int, Texture>();
        internal readonly Dictionary<int, float> Floats = new Dictionary<int, float>();
        internal bool HasTexture(int key) { return Textures.ContainsKey(key); }
        internal Texture GetTexture(int key) { Texture value; return Textures.TryGetValue(key, out value) ? value : null; }
        internal void SetTexture(int key, Texture value) { Textures[key] = value; }
        internal void SetFloat(int key, float value) { Floats[key] = value; }
        internal float GetFloat(int key) { float value; return Floats.TryGetValue(key, out value) ? value : 0; }
        internal void CopyFrom(MaterialPropertyBlock source)
        {
            Textures.Clear(); Floats.Clear();
            foreach (var pair in source.Textures) Textures.Add(pair.Key, pair.Value);
            foreach (var pair in source.Floats) Floats.Add(pair.Key, pair.Value);
        }
    }
    internal class Renderer : Component
    {
        internal Material[] sharedMaterials = { new Material(), new Material() };
        internal readonly Dictionary<int, MaterialPropertyBlock> Blocks = new Dictionary<int, MaterialPropertyBlock>();
        internal bool ThrowBlockOnce;
        internal void GetPropertyBlock(MaterialPropertyBlock target, int slot)
        {
            MaterialPropertyBlock value;
            target.CopyFrom(Blocks.TryGetValue(slot, out value) ? value : new MaterialPropertyBlock());
        }
        internal void SetPropertyBlock(MaterialPropertyBlock source, int slot)
        {
            if (ThrowBlockOnce) { ThrowBlockOnce = false; throw new InvalidOperationException("block-upload-failure"); }
            var copy = new MaterialPropertyBlock(); copy.CopyFrom(source); Blocks[slot] = copy;
        }
    }
    internal sealed class MeshRenderer : Renderer { }
    internal static class Mathf
    {
        internal static int CeilToInt(float value) { return (int)Math.Ceiling(value); }
        internal static int Clamp(int value, int min, int max) { return Math.Min(max, Math.Max(min, value)); }
    }
    internal static class Debug
    {
        internal static readonly List<string> Messages = new List<string>();
        internal static void LogWarning(string value) { Messages.Add(value); }
        internal static void LogError(string value) { Messages.Add(value); }
    }
}
internal sealed class BoatModelProxy : UnityEngine.Component
{
    internal UnityEngine.MeshRenderer BoatMeshRenderer;
    internal List<UnityEngine.Mesh> DamageStateMeshes = new List<UnityEngine.Mesh>();
}
internal sealed class Player : UnityEngine.Component
{
    internal BoatModelProxy BoatModelProxy;
    internal bool IsAlive = true, IsDocked = true;
}
internal sealed class SaveData
{
    internal Dictionary<string, string> stringVariables = new Dictionary<string, string>();
    internal int HullTier = 5, Damage;
    internal bool ForbidSave, ThrowWrite;
    internal int GetNumberOfDamagedSlots() { return Damage; }
    internal string GetStringVariable(string key, string fallback)
    { string value; return stringVariables.TryGetValue(key, out value) ? value : fallback; }
    internal void SetStringVariable(string key, string value)
    { if (ThrowWrite) throw new InvalidOperationException("write-failure"); stringVariables[key] = value; }
}
internal sealed class SaveManager { internal SaveData ActiveSaveData; }
internal sealed class PlayerStats { internal int DamageThreshold = 8; }
internal sealed class GameManager
{
    internal static GameManager Instance;
    internal SaveData SaveData;
    internal SaveManager SaveManager;
    internal Player Player;
    internal PlayerStats PlayerStats = new PlayerStats();
    internal bool IsPlaying = true;
}
internal sealed class GameEvents
{
    internal static GameEvents Instance;
    internal event Action OnBoatModelChanged;
    internal int Listeners { get { return OnBoatModelChanged == null ? 0 : OnBoatModelChanged.GetInvocationList().Length; } }
    internal void ModelChanged() { if (OnBoatModelChanged != null) OnBoatModelChanged(); }
}
namespace Jinke.DredgeMerchantTravel { internal static class Bootstrap { internal static bool PainterEnabled = true; } }
namespace Jinke.DredgeShipAppearance
{
    // The geometry/factory implementation has its own tests. This substitute
    // injects upload/allocation failures into the production lifecycle code.
    internal static class Boat5MeshFactory
    {
        internal static bool ThrowCreate;
        internal static int Creations;
        internal static readonly List<UnityEngine.Mesh> Created = new List<UnityEngine.Mesh>();
        internal static UnityEngine.Mesh[] Create(UnityEngine.Mesh source)
        {
            Creations++;
            if (ThrowCreate) throw new InvalidOperationException("resource-failure");
            var result = new UnityEngine.Mesh[4];
            for (int i = 0; i < 4; i++) { result[i] = new UnityEngine.Mesh { name = "Generated" + i }; Created.Add(result[i]); }
            return result;
        }
        internal static void Dispose(UnityEngine.Mesh[] meshes)
        { if (meshes != null) foreach (UnityEngine.Mesh mesh in meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh); }
    }
    internal static class Boat5AuthoredAssets
    {
        internal static bool ThrowCreate;
        internal static string ThrowId;
        internal static readonly List<Boat5SkinResources> Created = new List<Boat5SkinResources>();
        internal static Boat5SkinResources Create(string id)
        {
            if (ThrowCreate || id == ThrowId) throw new InvalidOperationException("authored-resource-failure");
            var meshes = new UnityEngine.Mesh[4];
            for (int i = 0; i < 4; i++) meshes[i] = new UnityEngine.Mesh { name = id + "_" + i };
            var value = new Boat5SkinResources(meshes, new UnityEngine.Texture2D { name = id + "Albedo" }, new UnityEngine.Texture2D { name = id + "Emission" });
            Created.Add(value); return value;
        }
    }
}
