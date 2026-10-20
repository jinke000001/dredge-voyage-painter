using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Jinke.DredgeVoyage;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static GridConfiguration[] hulls;
    private static List<UpgradeData> upgrades;
    private static void Check(bool passed, string name)
    { checks++; if (!passed) throw new Exception("FAIL " + name); }
    private static void Near(float actual, float expected, string name) { Check(Math.Abs(actual - expected) < .0001, name); }
    private static void Main(string[] args)
    {
        LoadAssets(args[0]);
        TestTime();
        for (int tier = 1; tier <= 5; tier++) TestHull(tier);
        TestGuards();
        TestPersistenceAndMigration();
        TestReloadAllLevels();
        TestUpgradeOrdering();
        Console.WriteLine("PASS " + checks + " time/cargo simulation assertions; source layouts and native upgrade coordinates read from shipped bundles.");
    }
    private static GameManager Reset(int tier)
    {
        TimeTuning.Restore();
        var save = new SaveData { HullTier = tier };
        save.grids[GridKey.INVENTORY] = new SerializableGrid();
        save.grids[GridKey.OVERFLOW_STORAGE] = new SerializableGrid();
        save.Inventory.Init(hulls[tier - 1]);
        var game = new GameManager { SaveData = save, GameConfigData = new GameConfigData { Hulls = hulls }, Player = new Player(), GridManager = new GridManager(), Time = new TimeController(), UpgradeManager = new UpgradeManager() };
        game.UpgradeManager.allUpgradeData.AddRange(upgrades);
        GameManager.Instance = game;
        GameEvents.Instance = new GameEvents();
        CargoRefit.SessionAllowed = () => true;
        return game;
    }
    private static void TestTime()
    {
        var game = Reset(1); var config = game.GameConfigData;
        TimeTuning.Apply(new VoyageSettings());
        Check(config.HourWrites == 0 && config.ForcedWrites == 0, "default writes no config fields");
        var sanity = new PlayerSanity { PlayerRef = game.Player };
        Near(TimeTuning.AdjustSanityDeltaTime(.1f, sanity), .1f, "default exact delta");
        TimeTuning.Apply(new VoyageSettings { Pace = 1 });
        Near(config.HourDurationInSeconds, 12.5f, "gentle day");
        Near(config.ForcedTimePassageSpeedModifier, 6.25f, "forced actions keep real duration");
        float ordinary = TimeTuning.AdjustSanityDeltaTime(1, sanity);
        Near(ordinary * config.HourDurationInSeconds, 10, "same game hour gives native sanity delta");
        Near(ordinary * config.ForcedTimePassageSpeedModifier, 5, "sleep sanity per real second unchanged");
        int writes = config.HourWrites;
        TimeTuning.Apply(new VoyageSettings { Pace = 1 });
        Check(config.HourWrites == writes, "reapply not cumulative");
        TimeTuning.Apply(new VoyageSettings { Pace = 2 });
        Near(config.HourDurationInSeconds, 15, "pace switch uses original baseline");
        game.Player.SanityModifierDetector.IgnoreTimescale = true;
        Near(TimeTuning.AdjustSanityDeltaTime(.1f, sanity), .1f, "ignore-timescale hazards retain real seconds");
        TimeTuning.Apply(new VoyageSettings());
        Near(config.HourDurationInSeconds, 10, "restore exact hour");
        Near(config.ForcedTimePassageSpeedModifier, 5, "restore exact forced modifier");
        TimeTuning.Apply(new VoyageSettings { Pace = 2 });
        game.GameConfigData = new GameConfigData { Hulls = hulls };
        TimeTuning.Apply(new VoyageSettings { Pace = 1 });
        Near(config.HourDurationInSeconds, 10, "replaced asset restored");
        Near(game.GameConfigData.HourDurationInSeconds, 12.5f, "new asset independent baseline");
        TimeTuning.Restore();
    }
    private static void TestHull(int tier)
    {
        var game = Reset(tier); SaveData save = game.SaveData;
        CargoRefitPreview preview = CargoRefit.GetPreview();
        Check(preview != null && preview.Level == 0 && preview.MaxLevel == 8 && preview.AddedCells == 0 &&
            preview.NextCells.Length == 2 && preview.RemovableCells.Length == 0 && preview.Cost == 200m, "default preview " + tier);
        int[] legacy = TimeCargoRules.CargoCoordinates(tier);
        Check(preview.NextCells[0].Equals(new Vector2Int(legacy[0], legacy[1])) &&
            preview.NextCells[1].Equals(new Vector2Int(legacy[2], legacy[3])), "first pair remains v1 compatible " + tier);
        string original = Snapshot(save.Inventory);
        CargoRefit.ReapplyOwned();
        Check(save.boolVariables.Count == 0 && save.intVariables.Count == 0 && original == Snapshot(save.Inventory), "default exact no-op " + tier);
        save.Funds = 20000;
        var oldObjects = new Dictionary<GridCellData, SpatialItemInstance>();
        foreach (GridCellData cell in save.Inventory.Grid)
            if (!cell.IsHidden) { cell.spatialItemInstance = new SpatialItemInstance(); oldObjects.Add(cell, cell.spatialItemInstance); }
        string reason;
        for (int level = 1; level <= 8; level++)
        {
            preview = CargoRefit.GetPreview();
            string before = Snapshot(save.Inventory);
            Check(preview.Cost == 200m * level && preview.Level == level - 1, "increasing price " + tier + "/" + level);
            Check(CargoRefit.Purchase(out reason), "full hold additive purchase " + tier + "/" + level);
            CargoRefitPreview after = CargoRefit.GetPreview();
            Check(after.Level == level && after.AddedCells == level * 2 && after.Owned, "owned capacity " + tier + "/" + level);
            Check(save.Funds == 20000m - 100m * level * (level + 1) && save.GetIntVariable(CargoRefit.LevelKey) == level &&
                save.GetBoolVariable(CargoRefit.OwnershipKey), "same native state payment and level " + tier + "/" + level);
            Check(after.RemovableCells[0].Equals(preview.NextCells[0]) && after.RemovableCells[1].Equals(preview.NextCells[1]), "last pair is next removal " + tier + "/" + level);
            VerifyOnlyNewCellsChanged(before, save.Inventory, after, tier);
            foreach (var pair in oldObjects) Check(ReferenceEquals(pair.Key.spatialItemInstance, pair.Value), "existing object retained " + tier + "/" + level);
            string bought = Snapshot(save.Inventory);
            CargoRefit.ReapplyOwned();
            Check(bought == Snapshot(save.Inventory) && save.Funds == 20000m - 100m * level * (level + 1), "reapply no charge or accumulation " + tier + "/" + level);
        }
        preview = CargoRefit.GetPreview(); decimal spentFunds = save.Funds;
        Check(preview.NextCells.Length == 0 && preview.Cost == 0 && !CargoRefit.Purchase(out reason) && save.Funds == spentFunds, "max cap no extra charge " + tier);
        var unique = new HashSet<Vector2Int>(preview.Cells);
        Check(unique.Count == 16, "sixteen distinct cells " + tier);
        foreach (UpgradeData data in upgrades)
            if (data.tier == tier) foreach (CellGroupConfiguration group in ((SlotUpgradeData)data).CellGroupConfigs) save.Inventory.Apply(group);
        CargoRefit.ReapplyOwned();
        foreach (Vector2Int point in preview.Cells)
        {
            GridCellData cell = save.Inventory.Grid[point.x, point.y];
            Check(!cell.IsHidden && cell.acceptedItemType == ItemType.GENERAL, "all native upgrades retain added cargo " + tier);
            Check((cell.acceptedItemSubtype & (ItemSubtype.ENGINE | ItemSubtype.ROD | ItemSubtype.LIGHT | ItemSubtype.NET | ItemSubtype.DREDGE)) == 0, "equipment exclusions " + tier);
        }
        Vector2Int first = preview.Cells[0]; var retained = new SpatialItemInstance();
        save.Inventory.Grid[first.x, first.y].spatialItemInstance = retained;
        Check(CargoRefit.TryRemove(out reason) && CargoRefit.GetPreview().Level == 7, "earlier occupied level does not block last empty pair " + tier);
        Check(ReferenceEquals(save.Inventory.Grid[first.x, first.y].spatialItemInstance, retained), "earlier object not moved during shrink " + tier);
        Vector2Int last = CargoRefit.GetPreview().RemovableCells[0];
        save.Inventory.Grid[last.x, last.y].spatialItemInstance = new SpatialItemInstance();
        Check(!CargoRefit.TryRemove(out reason) && CargoRefit.GetPreview().Level == 7, "last pair occupied rejects shrink " + tier);
        save.Inventory.Grid[last.x, last.y].spatialItemInstance = null;
        save.Inventory.Grid[last.x, last.y].underlaySpatialItemInstance = new SpatialItemInstance();
        Check(!CargoRefit.TryRemove(out reason), "last pair underlay rejects shrink " + tier);
        save.Inventory.Grid[last.x, last.y].underlaySpatialItemInstance = null;
        for (int level = 7; level >= 1; level--)
        {
            if (level == 1)
            {
                Check(!CargoRefit.TryRemove(out reason), "earlier occupied pair blocks when it becomes last " + tier);
                save.Inventory.Grid[first.x, first.y].spatialItemInstance = null;
            }
            string before = Snapshot(save.Inventory);
            Check(CargoRefit.TryRemove(out reason) && CargoRefit.GetPreview().Level == level - 1 && save.Funds == spentFunds, "one level removed without refund " + tier + "/" + level);
            VerifyOnlyNewCellsChanged(before, save.Inventory, CargoRefit.GetPreview(), tier);
        }
        Check(!CargoRefit.GetPreview().Owned && !save.GetBoolVariable(CargoRefit.OwnershipKey) && !CargoRefit.TryRemove(out reason), "zero level cannot shrink " + tier);
        var expected = new SerializableGrid(); expected.Init(hulls[tier - 1]);
        foreach (UpgradeData data in upgrades)
            if (data.tier == tier) foreach (CellGroupConfiguration group in ((SlotUpgradeData)data).CellGroupConfigs) expected.Apply(group);
        Check(Snapshot(expected) == Snapshot(save.Inventory), "original plus purchased native upgrades fully restored " + tier);
        Check(CargoRefit.GetPreview().Cost == 200 && CargoRefit.Purchase(out reason) && save.Funds == spentFunds - 200, "repurchase charges current next-level price " + tier);
    }
    private static void TestGuards()
    {
        var game = Reset(5); string reason;
        CargoRefit.SessionAllowed = () => false;
        Check(!CargoRefit.Purchase(out reason) && game.SaveData.Funds == 1000, "non-authorized session rejected");
        CargoRefit.SessionAllowed = () => true;
        game.SaveData.Damaged = 1; Check(!CargoRefit.Purchase(out reason), "damaged hull rejected"); game.SaveData.Damaged = 0;
        game.Time.Forced = true; Check(!CargoRefit.Purchase(out reason), "forced time rejected"); game.Time.Forced = false;
        game.Player.IsDocked = false; Check(!CargoRefit.Purchase(out reason), "at sea rejected"); game.Player.IsDocked = true;
        game.GridManager.Holding = true; Check(!CargoRefit.Purchase(out reason), "held object rejected"); game.GridManager.Holding = false;
        game.SaveData.Funds = 199; Check(!CargoRefit.Purchase(out reason), "insufficient funds rejected"); game.SaveData.Funds = 1000;
        game.SaveData.OverflowStorage.spatialItems.Add(new SpatialItemInstance());
        Check(!CargoRefit.Purchase(out reason), "overflow rejected"); game.SaveData.OverflowStorage.spatialItems.Clear();
        Vector2Int next = CargoRefit.GetPreview().NextCells[0];
        game.SaveData.Inventory.Grid[next.x, next.y].underlaySpatialItemInstance = new SpatialItemInstance();
        Check(!CargoRefit.Purchase(out reason), "hidden next pair underlay rejected"); game.SaveData.Inventory.Grid[next.x, next.y].underlaySpatialItemInstance = null;
        game.SaveData.Inventory.ThrowOnRefresh = true; GameEvents.Instance.ThrowOnFunds = true;
        bool nestedAccepted = true;
        GameEvents.Instance.FundsListener = () => { string nestedReason; nestedAccepted = CargoRefit.Purchase(out nestedReason); };
        Check(CargoRefit.Purchase(out reason) && game.SaveData.Funds == 800 && CargoRefit.GetPreview().Level == 1 && !nestedAccepted, "listener exception and reentry preserve one payment");
        GameEvents.Instance.FundsListener = null;
        game.SaveData.Funds = 399; Check(!CargoRefit.Purchase(out reason) && CargoRefit.GetPreview().Level == 1, "next escalating price enforced");
        game.SaveData.Funds = 400; Check(CargoRefit.Purchase(out reason) && game.SaveData.Funds == 0 && CargoRefit.GetPreview().Level == 2, "exact second price accepted");
        game.SaveData.Damaged = 1; Check(!CargoRefit.TryRemove(out reason), "damaged hull removal rejected");
        game = Reset(5); next = CargoRefit.GetPreview().NextCells[0];
        game.UpgradeManager.allUpgradeData.Add(new SlotUpgradeData { tier = 5, CellGroupConfigs = new List<CellGroupConfiguration> { new CellGroupConfiguration { cells = new List<Vector2Int> { next } } } });
        Check(CargoRefit.GetPreview() == null && !CargoRefit.Purchase(out reason), "new native upgrade collision fails closed");
        game = Reset(5); next = CargoRefit.GetPreview().NextCells[0]; game.SaveData.Inventory.Grid[next.x, next.y].acceptedItemData = new object();
        Check(CargoRefit.GetPreview() == null, "special accepted-item restriction protected");
        foreach (int invalid in new[] { -1, 9, int.MaxValue })
        {
            game = Reset(5); game.SaveData.SetIntVariable(CargoRefit.LevelKey, invalid);
            string before = Snapshot(game.SaveData.Inventory); CargoRefit.ReapplyOwned();
            Check(CargoRefit.GetPreview() == null && !CargoRefit.Purchase(out reason) && !CargoRefit.TryRemove(out reason) &&
                Snapshot(game.SaveData.Inventory) == before && game.SaveData.Funds == 1000, "invalid level not silently clamped " + invalid);
        }
    }
    private static void TestPersistenceAndMigration()
    {
        string reason;
        for (int tier = 1; tier <= 5; tier++)
        {
            var game = Reset(tier); SaveData empty = CopySave(game.SaveData);
            game.SaveData.SetBoolVariable(CargoRefit.OwnershipKey, true);
            SaveData legacy = CopySave(game.SaveData);
            CargoRefit.ReapplyOwned();
            Check(CargoRefit.GetPreview().Level == 1 && game.SaveData.GetIntVariable(CargoRefit.LevelKey) == 1 && game.SaveData.Funds == 1000, "v1 migrates without charge " + tier);
            Check(CargoRefit.Purchase(out reason) && game.SaveData.Funds == 600, "v1 next purchase is level2 price " + tier);
            Vector2Int point = CargoRefit.GetPreview().Cells[3];
            var item = new SpatialItemInstance { Position = point };
            game.SaveData.Inventory.spatialItems.Add(item); game.SaveData.Inventory.Grid[point.x, point.y].spatialItemInstance = item;
            SaveData paid = CopySave(game.SaveData);
            LoadSnapshot(game, paid, tier);
            Check(CargoRefit.GetPreview().Level == 2 && game.SaveData.Funds == 600 &&
                game.SaveData.Inventory.Grid[point.x, point.y].ContainsObject && !game.SaveData.Inventory.Grid[point.x, point.y].IsHidden, "saved extra-cell item survives native-style remap " + tier);
            Check(!CargoRefit.TryRemove(out reason), "reloaded occupied last pair cannot shrink " + tier);
            LoadSnapshot(game, legacy, tier);
            Check(CargoRefit.GetPreview().Level == 1 && game.SaveData.Funds == 1000 && CargoRefit.GetPreview().AddedCells == 2, "old v1 checkpoint restores level and balance " + tier);
            LoadSnapshot(game, empty, tier);
            Check(CargoRefit.GetPreview().Level == 0 && game.SaveData.Funds == 1000 && game.SaveData.boolVariables.Count == 0 &&
                game.SaveData.intVariables.Count == 0, "original checkpoint reverts without new keys " + tier);
            game.SaveData.SetBoolVariable(CargoRefit.OwnershipKey, true); game.SaveData.SetIntVariable(CargoRefit.LevelKey, 0);
            CargoRefit.ReapplyOwned();
            Check(CargoRefit.GetPreview().Level == 0, "explicit level zero takes precedence over legacy marker " + tier);
            LoadSnapshot(game, paid, tier);
            Check(CargoRefit.GetPreview().Level == 2 && game.SaveData.Funds == 600, "separate save retains its own level " + tier);
        }
    }
    private static void TestUpgradeOrdering()
    {
        for (int tier = 1; tier <= 5; tier++)
            for (int level = 0; level <= 8; level++)
            {
                var game = Reset(tier); SaveData save = game.SaveData;
                if (level > 0) { save.SetIntVariable(CargoRefit.LevelKey, level); save.SetBoolVariable(CargoRefit.OwnershipKey, true); }
                int nextTier = Math.Min(tier + 1, 5);
                save.Inventory.Init(hulls[nextTier - 1]); // Native caller has NOT written the new HullTier yet.
                CargoRefit.OnGridInitialized(save.Inventory);
                CargoRefitPreview preview = CargoRefit.GetPreview();
                Check(save.HullTier == tier && preview.HullTier == nextTier && preview.Level == level && preview.AddedCells == 2 * level, "new config before HullTier/repack " + tier + "/" + level);
                foreach (Vector2Int cell in preview.Cells) Check(!save.Inventory.Grid[cell.x, cell.y].IsHidden, "every paid cell available to native repack " + tier + "/" + level);
                foreach (UpgradeData data in upgrades)
                    if (data.tier == nextTier) foreach (CellGroupConfiguration group in ((SlotUpgradeData)data).CellGroupConfigs) save.Inventory.Apply(group);
                CargoRefit.ReapplyOwned();
                foreach (Vector2Int cell in preview.Cells) Check(!save.Inventory.Grid[cell.x, cell.y].IsHidden && save.Inventory.Grid[cell.x, cell.y].acceptedItemType == ItemType.GENERAL, "native slot upgrades cannot consume paid capacity " + tier + "/" + level);
            }
    }
    private static void TestReloadAllLevels()
    {
        for (int tier = 1; tier <= 5; tier++)
            for (int level = 1; level <= 8; level++)
            {
                var game = Reset(tier); SaveData save = game.SaveData;
                save.Funds = 20000;
                string reason;
                for (int purchase = 0; purchase < level; purchase++) Check(CargoRefit.Purchase(out reason), "prepare reload level " + tier + "/" + level);
                Vector2Int point = CargoRefit.GetPreview().RemovableCells[1];
                save.Inventory.spatialItems.Add(new SpatialItemInstance { Position = point });
                LoadSnapshot(game, CopySave(save), tier);
                Check(CargoRefit.GetPreview().Level == level && CargoRefit.GetPreview().AddedCells == 2 * level &&
                    game.SaveData.Funds == 20000m - 100m * level * (level + 1) &&
                    game.SaveData.Inventory.Grid[point.x, point.y].ContainsObject && !game.SaveData.Inventory.Grid[point.x, point.y].IsHidden &&
                    !CargoRefit.TryRemove(out reason), "every level reload preserves price, item and removal guard " + tier + "/" + level);
                game.SaveData.Inventory.spatialItems.Clear();
                game.SaveData.Inventory.Grid[point.x, point.y].spatialItemInstance = null;
                Check(CargoRefit.TryRemove(out reason), "empty last pair removable after reload " + tier + "/" + level);
                LoadSnapshot(game, CopySave(game.SaveData), tier);
                Check(CargoRefit.GetPreview().Level == level - 1 && CargoRefit.GetPreview().AddedCells == 2 * (level - 1) &&
                    game.SaveData.Inventory.Grid[point.x, point.y].IsHidden &&
                    game.SaveData.Funds == 20000m - 100m * level * (level + 1), "removed level remains removed after reload without refund " + tier + "/" + level);
            }
    }
    private static SaveData CopySave(SaveData source)
    {
        var copy = new SaveData { HullTier = source.HullTier, Funds = source.Funds, Damaged = source.Damaged,
            boolVariables = new Dictionary<string, bool>(source.boolVariables), intVariables = new Dictionary<string, int>(source.intVariables) };
        copy.grids[GridKey.INVENTORY] = new SerializableGrid(); copy.grids[GridKey.OVERFLOW_STORAGE] = new SerializableGrid();
        foreach (SpatialItemInstance item in source.Inventory.spatialItems) copy.Inventory.spatialItems.Add(new SpatialItemInstance { Position = item.Position });
        return copy;
    }
    private static void LoadSnapshot(GameManager game, SaveData snapshot, int tier)
    {
        game.SaveData = CopySave(snapshot);
        game.SaveData.Inventory.Init(hulls[tier - 1]);
        CargoRefit.OnGridInitialized(game.SaveData.Inventory);
    }
    private static string Snapshot(SerializableGrid grid)
    {
        var values = new List<string>();
        foreach (GridCellData cell in grid.Grid) values.Add(cell.x + "," + cell.y + ":" + (int)cell.acceptedItemType + ":" + (int)cell.acceptedItemSubtype + ":" + cell.IsHidden + ":" + cell.damageImmune);
        return string.Join(";", values);
    }
    private static void VerifyOnlyNewCellsChanged(string before, SerializableGrid grid, CargoRefitPreview preview, int tier)
    {
        string[] previous = before.Split(';'), after = Snapshot(grid).Split(';'); int changed = 0;
        for (int i = 0; i < previous.Length; i++) if (previous[i] != after[i]) changed++;
        Check(changed == 2, "only two cells changed; dimensions and equipment preserved " + tier);
    }
    private static void LoadAssets(string path)
    {
        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
        {
            hulls = new GridConfiguration[5]; upgrades = new List<UpgradeData>();
            foreach (JsonElement hull in doc.RootElement.GetProperty("hulls").EnumerateArray())
            {
                int tier = hull.GetProperty("tier").GetInt32(); JsonElement data = hull.GetProperty("data");
                var config = new GridConfiguration { columns = data.GetProperty("columns").GetInt32(), rows = data.GetProperty("rows").GetInt32(), MainItemType = (ItemType)data.GetProperty("mainItemType").GetInt32(), MainItemSubtype = (ItemSubtype)data.GetProperty("mainItemSubtype").GetInt32() };
                config.cellGroupConfigs = Groups(data); hulls[tier - 1] = config;
            }
            foreach (JsonElement data in doc.RootElement.GetProperty("upgrades").EnumerateArray()) upgrades.Add(new SlotUpgradeData { tier = data.GetProperty("tier").GetInt32(), CellGroupConfigs = Groups(data) });
        }
    }
    private static List<CellGroupConfiguration> Groups(JsonElement data)
    {
        var result = new List<CellGroupConfiguration>();
        foreach (JsonElement group in data.GetProperty("cellGroupConfigs").EnumerateArray())
        {
            var cells = new List<Vector2Int>();
            foreach (JsonElement point in group.GetProperty("cells").EnumerateArray()) cells.Add(new Vector2Int(point.GetProperty("x").GetInt32(), point.GetProperty("y").GetInt32()));
            result.Add(new CellGroupConfiguration { cells = cells, ItemType = (ItemType)group.GetProperty("itemType").GetInt32(), ItemSubtype = (ItemSubtype)group.GetProperty("itemSubtype").GetInt32(), IsHidden = group.GetProperty("isHidden").GetInt32() != 0, DamageImmune = group.GetProperty("damageImmune").GetInt32() != 0 });
        }
        return result;
    }
}
