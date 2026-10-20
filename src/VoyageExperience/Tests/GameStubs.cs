// Simulation only. The production build is separately compiled against the
// original managed assemblies. These stubs never load or launch the game.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;
        public Vector2Int(int px, int py) { x = px; y = py; }
        public bool Equals(Vector2Int other) { return x == other.x && y == other.y; }
        public override bool Equals(object other) { return other is Vector2Int && Equals((Vector2Int)other); }
        public override int GetHashCode() { return x * 397 ^ y; }
    }
    public static class Debug { public static void LogError(object value) { } }
}
namespace Jinke.DredgeVoyage { internal sealed class VoyageSettings { internal int Pace { get; set; } } }
[Flags] public enum ItemType { NONE = 0, GENERAL = 1, EQUIPMENT = 2, DAMAGE = 4, ALL = -1 }
[Flags] public enum ItemSubtype { NONE = 0, FISH = 1, ENGINE = 2, ROD = 4, GENERAL = 8, RELIC = 16, TRINKET = 32, MATERIAL = 64, LIGHT = 128, POT = 256, NET = 512, DREDGE = 1024, GADGET = 2048, ALL = -1 }
public enum GridKey { INVENTORY, OVERFLOW_STORAGE }
public sealed class GameConfigData
{
    private float hour = 10, forced = 5;
    public int HourWrites, ForcedWrites;
    public float HourDurationInSeconds { get { return hour; } set { hour = value; HourWrites++; } }
    public float ForcedTimePassageSpeedModifier { get { return forced; } set { forced = value; ForcedWrites++; } }
    public GridConfiguration[] Hulls;
    public GridConfiguration GetGridConfigForHullTier(int tier) { return Hulls[tier - 1]; }
}
public sealed class GameManager
{
    public static GameManager Instance;
    public GameConfigData GameConfigData;
    public SaveData SaveData;
    public Player Player;
    public GridManager GridManager;
    public TimeController Time;
    public UpgradeManager UpgradeManager;
}
public sealed class Player { public bool IsDocked = true; public SanityModifierDetector SanityModifierDetector = new SanityModifierDetector(); }
public sealed class SanityModifierDetector { public bool IgnoreTimescale; }
public sealed class PlayerSanity { public Player PlayerRef; }
public sealed class TimeController { public bool Forced; public bool IsTimePassingForcefully() { return Forced; } }
public sealed class GridManager { public bool Holding; public bool IsCurrentlyHoldingObject() { return Holding; } }
public sealed class UpgradeManager { public bool HasLoadedUpgradeData = true; public List<UpgradeData> allUpgradeData = new List<UpgradeData>(); }
public class UpgradeData { public int tier; }
public sealed class SlotUpgradeData : UpgradeData { public List<CellGroupConfiguration> CellGroupConfigs = new List<CellGroupConfiguration>(); }
public sealed class GameEvents
{
    public static GameEvents Instance = new GameEvents();
    public bool ThrowOnFunds;
    public Action FundsListener;
    public void TriggerPlayerFundsChanged(decimal funds, decimal change)
    { if (FundsListener != null) FundsListener(); if (ThrowOnFunds) throw new Exception("listener"); }
}
public sealed class SaveData
{
    public Dictionary<string, bool> boolVariables = new Dictionary<string, bool>();
    public Dictionary<string, int> intVariables = new Dictionary<string, int>();
    public Dictionary<GridKey, SerializableGrid> grids = new Dictionary<GridKey, SerializableGrid>();
    public decimal Funds = 1000m;
    public int HullTier = 1, Damaged;
    public SerializableGrid Inventory { get { return grids[GridKey.INVENTORY]; } }
    public SerializableGrid OverflowStorage { get { return grids[GridKey.OVERFLOW_STORAGE]; } }
    public bool GetBoolVariable(string key) { bool value; return boolVariables.TryGetValue(key, out value) && value; }
    public void SetBoolVariable(string key, bool value) { boolVariables[key] = value; }
    public int GetIntVariable(string key, int defaultValue = 0) { int value; return intVariables.TryGetValue(key, out value) ? value : defaultValue; }
    public void SetIntVariable(string key, int value) { intVariables[key] = value; }
    public int GetNumberOfDamagedSlots() { return Damaged; }
}
public sealed class SpatialItemInstance { public UnityEngine.Vector2Int Position; }
public sealed class GridCellData
{
    public int x, y;
    public bool damageImmune, IsHidden;
    public ItemType acceptedItemType;
    public ItemSubtype acceptedItemSubtype;
    public object acceptedItemData;
    public SpatialItemInstance spatialItemInstance, underlaySpatialItemInstance;
    public bool ContainsObject { get { return spatialItemInstance != null; } }
}
public struct CellGroupConfiguration
{
    public List<UnityEngine.Vector2Int> cells;
    public ItemType ItemType;
    public ItemSubtype ItemSubtype;
    public bool IsHidden, DamageImmune;
}
public sealed class GridConfiguration
{
    public List<CellGroupConfiguration> cellGroupConfigs = new List<CellGroupConfiguration>();
    public int columns, rows;
    public ItemType MainItemType;
    public ItemSubtype MainItemSubtype;
}
public sealed class SerializableGrid
{
    public GridCellData[,] Grid;
    public GridConfiguration GridConfiguration;
    public List<SpatialItemInstance> spatialItems = new List<SpatialItemInstance>();
    public int Refreshes;
    public bool ThrowOnRefresh;
    public void TriggerRefreshEvent() { Refreshes++; if (ThrowOnRefresh) throw new Exception("listener"); }
    public void Init(GridConfiguration configuration)
    {
        GridConfiguration = configuration;
        Grid = new GridCellData[configuration.columns, configuration.rows];
        for (int x = 0; x < configuration.columns; x++)
            for (int y = 0; y < configuration.rows; y++)
                Grid[x,y] = new GridCellData { x = x, y = y, acceptedItemType = configuration.MainItemType, acceptedItemSubtype = configuration.MainItemSubtype };
        foreach (CellGroupConfiguration group in configuration.cellGroupConfigs) Apply(group);
        // Native Init maps existing items even while their cells are hidden.
        // The injected after-Init hook then restores purchased cell visibility.
        foreach (SpatialItemInstance item in spatialItems)
            Grid[item.Position.x, item.Position.y].spatialItemInstance = item;
    }
    public void Apply(CellGroupConfiguration group)
    {
        foreach (UnityEngine.Vector2Int point in group.cells)
        {
            GridCellData cell = Grid[point.x, point.y];
            cell.IsHidden = group.IsHidden; cell.damageImmune = group.DamageImmune;
            cell.acceptedItemType = group.ItemType; cell.acceptedItemSubtype = group.ItemSubtype;
        }
    }
}
