using System.Collections.Generic;

// Bounded managed doubles: these tests exercise data restoration and hook math,
// not Unity loading, asset serialization, rendering, or actual game execution.
public enum ItemSubtype { POT, NET, OTHER }
public enum HarvestDifficulty { EASY, HARD }
public enum DredgeColorTypeEnum { POSITIVE }
public sealed class HarvestDifficultyConfigData
{
    public int minTargetWidth = 20, maxTargetWidth = 30, maxTargets = 3, specialTargetWidth = 6;
    public float speedFactor = 2f, ballTrophySpeedFactor = 1.5f, diamondScaleUpTimeSec = 3f, diamondTrophySpeedFactor = 0.5f;
    public float rotationSpeed = 40f, secondsToPassivelyCatch = 15f, targetValue = 0.2f;
}
public sealed class GameConfigData
{
    private float baseAberrationSpawnChance = 0.01f, nightAberrationSpawnChance = 0.03f;
    public float BaseAberrationSpawnChance { get { return baseAberrationSpawnChance; } }
    public float NightAberrationSpawnChance { get { return nightAberrationSpawnChance; } }
    public float MaxAberrationSpawnChance { get { return 0.35f; } }
    public Dictionary<HarvestDifficulty, HarvestDifficultyConfigData> FishingDifficultyConfigs = new Dictionary<HarvestDifficulty, HarvestDifficultyConfigData>();
    public Dictionary<HarvestDifficulty, HarvestDifficultyConfigData> DredgingDifficultyConfigs = new Dictionary<HarvestDifficulty, HarvestDifficultyConfigData>();
}
public sealed class DeployableItemData
{
    private float timeBetweenCatchRolls;
    public string id;
    public ItemSubtype itemSubtype;
    public float MaxDurabilityDays { get; private set; }
    public float CatchRate { get { return 1f; } }
    public float TimeBetweenCatchRolls { get { return timeBetweenCatchRolls; } }
    public DeployableItemData(string name, ItemSubtype type, float interval, float durability)
    { id = name; itemSubtype = type; timeBetweenCatchRolls = interval; MaxDurabilityDays = durability; }
}
public sealed class ItemManager
{
    public bool Loaded = true;
    public List<DeployableItemData> Items = new List<DeployableItemData>();
    public bool HasLoaded() { return Loaded; }
    public List<T> GetAllItemsOfType<T>() where T : class
    {
        var results = new List<T>();
        foreach (var item in Items) if (item is T) results.Add(item as T);
        return results;
    }
}
public sealed class PlayerStats { public float ResearchedEquipmentMaintenanceModifier { get; set; } }
public sealed class GameManager
{
    public static GameManager Instance;
    public GameConfigData GameConfigData = new GameConfigData();
    public ItemManager ItemManager = new ItemManager();
    public PlayerStats PlayerStats = new PlayerStats();
    public LanguageManager LanguageManager = new LanguageManager();
    public SaveData SaveData = new SaveData();
}
public sealed class SaveData
{
    public SpatialItemInstance Net;
    public SpatialItemInstance EquippedTrawlNetInstance() { return Net; }
}
public sealed class SpatialItemInstance
{
    private readonly DeployableItemData item;
    public SpatialItemInstance(DeployableItemData value) { item = value; }
    public T GetItemData<T>() where T : class { return item as T; }
}
public sealed class SerializedCrabPotPOIData { public string deployableItemId; }
public sealed class TrawlNetAbility
{
    private DeployableItemData trawlNetItemData;
    public TrawlNetAbility(DeployableItemData item) { trawlNetItemData = item; }
    public DeployableItemData OriginalItem { get { return trawlNetItemData; } }
}
public sealed class TooltipSectionDeployableDetails
{
    private DeployableItemData deployableItemData;
    public TooltipSectionDeployableDetails(DeployableItemData item) { deployableItemData = item; }
    public DeployableItemData OriginalItem { get { return deployableItemData; } }
}
public sealed class OccasionalGridPanel
{
    private DeployableItemData deployableItemData;
    public OccasionalGridPanel(DeployableItemData item) { deployableItemData = item; }
    public DeployableItemData OriginalItem { get { return deployableItemData; } }
}
public sealed class NetPanel { }
public sealed class LanguageManager
{
    public int Calls;
    public float LastCurrent, LastMaximum;
    public UnityEngine.Localization.Components.LocalizeStringEvent LastTarget;
    public string LastKey;
    public void FormatTimeStringForDurability(float current, float maximum, UnityEngine.Localization.Components.LocalizeStringEvent target, string key)
    { Calls++; LastCurrent = current; LastMaximum = maximum; LastTarget = target; LastKey = key; }
    public UnityEngine.Color GetColor(DredgeColorTypeEnum type) { return new UnityEngine.Color(); }
}
public sealed class SpiralMinigame
{
    public struct SpiralConfig { public float Marker; }
    private List<UnityEngine.Vector2> notches = new List<UnityEngine.Vector2>();
    private List<UnityEngine.Animator> gateAnimators = new List<UnityEngine.Animator>();
    private UnityEngine.UI.Image spiralImage = new UnityEngine.UI.Image();
    private SpiralConfig spiralConfig = new SpiralConfig { Marker = 17f };
    private int trophyNotchIndex = 1;
    private float startDeadzone = 0.01f, endDeadzone = 0.01f;
    public int Draws;
    public SpiralMinigame()
    {
        notches.Add(new UnityEngine.Vector2(0.10f, 0.04f));
        notches.Add(new UnityEngine.Vector2(0.40f, 0.04f));
        notches.Add(new UnityEngine.Vector2(0.80f, 0.04f));
        for (int i = 0; i < notches.Count; i++)
            CreateNotch(notches[i], "_notch" + (i + 1), new UnityEngine.Color());
    }
    public List<UnityEngine.Vector2> OriginalNotches { get { return notches; } }
    public List<UnityEngine.Animator> Gates { get { return gateAnimators; } }
    public UnityEngine.Material Material { get { return spiralImage.material; } }
    public int Trophy { get { return trophyNotchIndex; } }
    public float Deadzone { get { return startDeadzone + endDeadzone; } }
    // Mirrors the native allocation side effect; calling this from a postfix is a bug.
    private void CreateNotch(UnityEngine.Vector2 config, string prefix, UnityEngine.Color color)
    {
        Draws++;
        spiralImage.material.SetFloat(prefix + "_start", config.x);
        spiralImage.material.SetFloat(prefix + "_end", config.x + config.y);
        spiralImage.material.SetFloat(prefix + "_opacity", 1f);
        var animator = new UnityEngine.Animator();
        gateAnimators.Add(animator);
        animator.Component.SetPosition(config.x + config.y, spiralConfig);
        animator.Component.SetRotation(config.x + config.y, spiralConfig);
    }
}
public sealed class SpiralComponent
{
    public float Position, Rotation, ConfigMarker;
    public int PositionCalls, RotationCalls;
    public void SetPosition(float position, SpiralMinigame.SpiralConfig config)
    { Position = position; ConfigMarker = config.Marker; PositionCalls++; }
    public void SetRotation(float rotation, SpiralMinigame.SpiralConfig config)
    { Rotation = rotation; ConfigMarker = config.Marker; RotationCalls++; }
}
namespace UnityEngine
{
    public sealed class Animator
    {
        public readonly SpiralComponent Component = new SpiralComponent();
        public T GetComponent<T>() where T : class { return Component as T; }
    }
    public sealed class Material
    {
        private readonly Dictionary<string, float> floats = new Dictionary<string, float>();
        public int Writes;
        public void SetFloat(string name, float value) { floats[name] = value; Writes++; }
        public float GetFloat(string name) { return floats[name]; }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float xValue, float yValue) { x = xValue; y = yValue; }
    }
    public struct Color { }
    public static class Debug { public static void LogWarning(string message) { throw new System.Exception(message); } }
}
namespace UnityEngine.UI { public sealed class Image { public UnityEngine.Material material = new UnityEngine.Material(); } }
namespace UnityEngine.Localization.Components { public sealed class LocalizeStringEvent { } }
