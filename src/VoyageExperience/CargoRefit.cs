using System;
using UnityEngine;

namespace Jinke.DredgeVoyage
{
    internal sealed class CargoRefitPreview
    {
        internal int HullTier { get; private set; }
        internal Vector2Int[] Cells { get; private set; }
        internal Vector2Int[] NextCells { get; private set; }
        internal Vector2Int[] RemovableCells { get; private set; }
        internal int Level { get; private set; }
        internal int MaxLevel { get { return TimeCargoRules.CargoMaxLevel; } }
        internal int AddedCells { get { return Cells.Length; } }
        internal decimal Cost { get { return TimeCargoRules.CargoPrice(Level); } }
        internal bool Owned { get { return Level > 0; } }
        internal CargoRefitPreview(int tier, Vector2Int[] candidates, int level)
        {
            HullTier = tier; Level = level;
            Cells = Slice(candidates, 0, level * 2);
            NextCells = Slice(candidates, level * 2, level < MaxLevel ? 2 : 0);
            RemovableCells = Slice(candidates, level > 0 ? (level - 1) * 2 : 0, level > 0 ? 2 : 0);
        }
        private static Vector2Int[] Slice(Vector2Int[] source, int start, int count)
        {
            var result = new Vector2Int[count];
            Array.Copy(source, start, result, 0, count);
            return result;
        }
    }

    internal static class CargoRefit
    {
        internal const string OwnershipKey = "jinke-voyage-cargo-expanded-v1";
        internal const string LevelKey = "jinke-voyage-cargo-level-v2";
        internal static Func<bool> SessionAllowed { get; set; }
        private static bool mutating;
        private const ItemSubtype CargoTypes = ItemSubtype.FISH | ItemSubtype.GENERAL | ItemSubtype.RELIC |
            ItemSubtype.TRINKET | ItemSubtype.MATERIAL | ItemSubtype.POT | ItemSubtype.GADGET;

        internal static CargoRefitPreview GetPreview()
        {
            SaveData save;
            if (!TrySession(out save)) return null;
            CargoRefitPreview preview;
            string reason;
            return TryPlan(save.Inventory, save, out preview, out reason) ? preview : null;
        }

        internal static bool CanPurchase(out string reason)
        {
            if (mutating) { reason = "货舱整备进行中。"; return false; }
            SaveData save;
            if (!TrySession(out save)) { reason = "当前存档不支持货舱改装。"; return false; }
            if (!CanWorkAtDock(save, out reason)) return false;
            CargoRefitPreview preview;
            if (!TryPlan(save.Inventory, save, out preview, out reason)) return false;
            if (preview.Level == preview.MaxLevel) { reason = "货舱已达到整备上限。"; return false; }
            if (save.Funds < preview.Cost) { reason = "资金不足，无法继续整备。"; return false; }
            foreach (Vector2Int point in preview.NextCells)
            {
                GridCellData cell = save.Inventory.Grid[point.x, point.y];
                if (!cell.IsHidden || cell.acceptedItemType != ItemType.NONE || cell.ContainsObject || cell.underlaySpatialItemInstance != null)
                { reason = "新增位置已被占用，暂时不能改装。"; return false; }
            }
            reason = null;
            return true;
        }

        internal static bool Purchase(out string reason)
        {
            if (!CanPurchase(out reason)) return false;
            SaveData save = GameManager.Instance.SaveData;
            CargoRefitPreview preview = GetPreview();
            mutating = true;
            try
            {
                // Level and payment share the SAME native SaveData snapshot;
                // an old checkpoint restores both together. No sidecar state.
                save.Funds -= preview.Cost;
                WriteLevel(save, preview.Level + 1);
                ApplyCells(save.Inventory, preview.NextCells);
                Refresh(save.Inventory, -preview.Cost);
                return true;
            }
            finally { mutating = false; }
        }

        // Called after SerializableGrid.Init and before its caller continues.
        // Hull upgrades call Init BEFORE repacking items: reveal these cells now
        // so even a full hold retains its extra room during native repacking.
        internal static void OnGridInitialized(object owner)
        {
            SaveData save;
            SerializableGrid grid = owner as SerializableGrid;
            if (grid == null || !TrySession(out save) || !ReferenceEquals(grid, save.Inventory) || ReadLevel(save) == 0) return;
            CargoRefitPreview preview;
            string reason;
            if (TryPlan(grid, save, out preview, out reason))
            {
                bool changed = ApplyCells(grid, preview.Cells);
                // Lazy native migration only after the shipped layout validates.
                // A default/never-purchased save does not acquire either key.
                if (!save.intVariables.ContainsKey(LevelKey)) WriteLevel(save, preview.Level);
                if (changed) Refresh(grid, null);
            }
        }

        internal static void ReapplyOwned()
        {
            SaveData save;
            if (TrySession(out save)) OnGridInitialized(save.Inventory);
        }

        internal static bool CanRemove(out string reason)
        {
            if (mutating) { reason = "货舱整备进行中。"; return false; }
            SaveData save;
            if (!TrySession(out save)) { reason = "当前存档不支持货舱改装。"; return false; }
            if (!CanWorkAtDock(save, out reason)) return false;
            CargoRefitPreview preview;
            if (!TryPlan(save.Inventory, save, out preview, out reason)) return false;
            if (!preview.Owned) { reason = "尚未改装货舱。"; return false; }
            foreach (Vector2Int point in preview.RemovableCells)
            {
                GridCellData cell = save.Inventory.Grid[point.x, point.y];
                if (cell.ContainsObject || cell.underlaySpatialItemInstance != null)
                { reason = "请先清空最后整备的两个货物格并修复损伤。"; return false; }
            }
            reason = null;
            return true;
        }

        // Explicit removal only. Never called by Restore/default settings.
        // No item is moved or deleted and no grid dimension is reduced.
        internal static bool TryRemove(out string reason)
        {
            if (!CanRemove(out reason)) return false;
            SaveData save = GameManager.Instance.SaveData;
            CargoRefitPreview preview = GetPreview();
            mutating = true;
            try
            {
                foreach (Vector2Int point in preview.RemovableCells)
                {
                    GridCellData cell = save.Inventory.Grid[point.x, point.y];
                    cell.acceptedItemType = ItemType.NONE;
                    cell.acceptedItemSubtype = ItemSubtype.NONE;
                    cell.IsHidden = true;
                    cell.damageImmune = true;
                }
                WriteLevel(save, preview.Level - 1);
                Refresh(save.Inventory, null);
                return true;
            }
            finally { mutating = false; }
        }

        private static bool TrySession(out SaveData save)
        {
            save = null;
            if (SessionAllowed == null || !SessionAllowed()) return false;
            GameManager game = GameManager.Instance;
            if (game == null || game.SaveData == null || game.SaveData.boolVariables == null || game.SaveData.intVariables == null ||
                game.SaveData.grids == null || !game.SaveData.grids.ContainsKey(GridKey.INVENTORY)) return false;
            save = game.SaveData;
            return save.Inventory != null;
        }

        private static int ReadLevel(SaveData save)
        { return save.GetIntVariable(LevelKey, save.GetBoolVariable(OwnershipKey) ? 1 : 0); }

        private static void WriteLevel(SaveData save, int level)
        {
            save.SetIntVariable(LevelKey, level);
            save.SetBoolVariable(OwnershipKey, level > 0);
        }

        private static bool CanWorkAtDock(SaveData save, out string reason)
        {
            GameManager game = GameManager.Instance;
            if (game.Player == null || !game.Player.IsDocked || game.GridManager == null ||
                game.GridManager.IsCurrentlyHoldingObject() || game.Time == null || game.Time.IsTimePassingForcefully())
            { reason = "请靠港并放下手中物品后改装。"; return false; }
            if (save.GetNumberOfDamagedSlots() > 0) { reason = "请先修复船体损伤。"; return false; }
            if (save.OverflowStorage != null && save.OverflowStorage.spatialItems.Count > 0)
            { reason = "请先取出溢出仓库中的物品。"; return false; }
            reason = null;
            return true;
        }

        private static bool TryPlan(SerializableGrid grid, SaveData save, out CargoRefitPreview preview, out string reason)
        {
            preview = null;
            reason = "当前船体布局暂不支持这项改装。";
            GameManager game = GameManager.Instance;
            if (grid == null || grid.Grid == null || grid.GridConfiguration == null || game.GameConfigData == null ||
                game.UpgradeManager == null || !game.UpgradeManager.HasLoadedUpgradeData) return false;
            int level = ReadLevel(save);
            if (level < 0 || level > TimeCargoRules.CargoMaxLevel)
            { reason = "货舱整备记录暂不支持当前版本。"; return false; }
            int tier = 0;
            for (int candidate = 1; candidate <= 5; candidate++)
                if (ReferenceEquals(grid.GridConfiguration, game.GameConfigData.GetGridConfigForHullTier(candidate))) { tier = candidate; break; }
            if (tier == 0 || grid.Grid.GetLength(0) != TimeCargoRules.Columns(tier) || grid.Grid.GetLength(1) != TimeCargoRules.Rows(tier)) return false;
            int[] coordinates = TimeCargoRules.CargoCoordinates(tier, TimeCargoRules.CargoMaxLevel);
            var cells = new Vector2Int[coordinates.Length / 2];
            for (int i = 0; i < cells.Length; i++) cells[i] = new Vector2Int(coordinates[i * 2], coordinates[i * 2 + 1]);
            foreach (Vector2Int point in cells)
            {
                GridCellData current = grid.Grid[point.x, point.y];
                if (current == null || current.acceptedItemData != null) return false;
                bool originallyHidden = false;
                foreach (CellGroupConfiguration group in grid.GridConfiguration.cellGroupConfigs)
                    if (group.cells.Contains(point)) originallyHidden = group.IsHidden && group.ItemType == ItemType.NONE && group.ItemSubtype == ItemSubtype.NONE && group.DamageImmune;
                if (!originallyHidden) return false;
                // Protect future/native slot upgrades as well as today's layouts.
                foreach (UpgradeData upgrade in game.UpgradeManager.allUpgradeData)
                {
                    SlotUpgradeData slot = upgrade as SlotUpgradeData;
                    if (slot != null && slot.tier == tier)
                        foreach (CellGroupConfiguration group in slot.CellGroupConfigs) if (group.cells.Contains(point)) return false;
                }
                if (current.acceptedItemType != ItemType.NONE &&
                    (current.acceptedItemType != ItemType.GENERAL || current.acceptedItemSubtype != CargoTypes)) return false;
            }
            preview = new CargoRefitPreview(tier, cells, level);
            reason = null;
            return true;
        }

        private static bool ApplyCells(SerializableGrid grid, Vector2Int[] cells)
        {
            bool changed = false;
            foreach (Vector2Int point in cells)
            {
                GridCellData cell = grid.Grid[point.x, point.y];
                changed |= cell.IsHidden || cell.acceptedItemType != ItemType.GENERAL || cell.acceptedItemSubtype != CargoTypes || cell.damageImmune;
                cell.acceptedItemType = ItemType.GENERAL;
                cell.acceptedItemSubtype = CargoTypes;
                cell.IsHidden = false;
                cell.damageImmune = false;
            }
            return changed;
        }

        private static void Refresh(SerializableGrid grid, decimal? fundsChanged)
        {
            // Listener failures must not turn a completed purchase into a second
            // payment attempt or roll back ownership after objects were exposed.
            try { grid.TriggerRefreshEvent(); }
            catch (Exception error) { Debug.LogError("[VoyageCargo] Refresh: " + error); }
            if (fundsChanged.HasValue)
                try { GameEvents.Instance.TriggerPlayerFundsChanged(GameManager.Instance.SaveData.Funds, fundsChanged.Value); }
                catch (Exception error) { Debug.LogError("[VoyageCargo] Funds refresh: " + error); }
        }
    }
}
