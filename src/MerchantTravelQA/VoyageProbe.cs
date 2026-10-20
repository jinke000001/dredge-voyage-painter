using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Jinke.DredgeMerchantTravel.QA
{
    // Instrumentation exists only in the separately audited QA assembly. These
    // commands call real UI/native APIs, but never represent physical input.
    internal static class VoyageProbe
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static MoveFixture pendingMove;

        internal static object Execute(JObject command, Type bootstrap)
        {
            string action = (string)command["action"];
            if (action == "voyage-snapshot") return Snapshot(bootstrap);
            GameManager game = GameManager.Instance;
            Require(game != null && game.IsPlaying && game.Player != null && game.SaveData != null, "Voyage world is not ready");
            if (action == "voyage-change")
            {
                int row = (int)command["row"], value = (int)command["value"];
                Require(row >= 0 && row <= 6 && value >= 0 && value <= ((row == 0 || row == 6) ? 2 : 3), "Setting outside fixed row/value range");
                Type runtime = bootstrap.Assembly.GetType("Jinke.DredgeVoyage.VoyageRuntime", true);
                MethodInfo change = runtime.GetMethod("Change", AnyStatic, null, new[] { typeof(int), typeof(int), typeof(string).MakeByRefType() }, null);
                Require(change != null, "Normal Voyage change entry is missing");
                object[] args = { row, value, null };
                bool accepted = (bool)change.Invoke(null, args);
                return new { accepted, error = args[2], entry = "normal internal VoyageRuntime.Change; API integration only" };
            }
            if (action == "voyage-restore")
            {
                Type runtime = bootstrap.Assembly.GetType("Jinke.DredgeVoyage.VoyageRuntime", true);
                object[] args = { null };
                bool accepted = (bool)runtime.GetMethod("RestorePreferences", AnyStatic).Invoke(null, args);
                return new { accepted, error = args[0], entry = "normal internal RestorePreferences; cargo ownership remains native" };
            }
            if (action == "voyage-click")
            {
                int id = (int)command["instanceId"];
                Button button = Object.FindObjectsOfType<Button>(true).SingleOrDefault(x => x.GetInstanceID() == id);
                Require(button != null && button.gameObject.activeInHierarchy && button.IsInteractable(), "Button must be visible and interactable");
                button.onClick.Invoke();
                return new { invoked = id, entry = "Button.onClick.Invoke; API integration, not mouse input" };
            }
            if (action == "voyage-submit")
            {
                int id = (int)command["instanceId"];
                BasicButtonWrapper wrapper = Object.FindObjectsOfType<BasicButtonWrapper>(true).SingleOrDefault(x => x.GetInstanceID() == id);
                Require(wrapper != null && wrapper.gameObject.activeInHierarchy && wrapper.Interactable && EventSystem.current != null, "Native wrapper is not available for submit");
                wrapper.OnSubmit(new BaseEventData(EventSystem.current));
                return new { invoked = id, entry = "BasicButtonWrapper.OnSubmit; API integration, not keyboard input" };
            }
            if (action == "voyage-dropdown")
            {
                int id = (int)command["instanceId"], value = (int)command["value"];
                TMP_Dropdown dropdown = Object.FindObjectsOfType<TMP_Dropdown>(true).SingleOrDefault(x => x.GetInstanceID() == id);
                Require(dropdown != null && dropdown.gameObject.activeInHierarchy && dropdown.IsInteractable(), "Dropdown must be visible and interactable");
                Require(value >= 0 && value < dropdown.options.Count, "Invalid native dropdown option");
                int before = dropdown.value;
                dropdown.value = value;
                return new { before, after = dropdown.value, callbackExpected = before != value, entry = "TMP_Dropdown.value; native callback, not physical selection" };
            }
            if (action == "voyage-dropdown-open")
            {
                int id = (int)command["instanceId"];
                TMP_Dropdown dropdown = Object.FindObjectsOfType<TMP_Dropdown>(true).SingleOrDefault(x => x.GetInstanceID() == id);
                Require(dropdown != null && dropdown.gameObject.activeInHierarchy && dropdown.IsInteractable(), "Dropdown unavailable");
                dropdown.Show(); return "Native dropdown Show; API integration only";
            }
            if (action == "voyage-fishing-press")
            {
                HarvestMinigame[] active = Object.FindObjectsOfType<HarvestMinigame>().Where(x => x.IsGameRunning).ToArray();
                Require(game.Player.IsFishing && active.Length == 1 && !game.IsPaused, "One already-running native fishing minigame is required");
                active[0].OnMinigameInteractPress();
                return "Current native minigame interaction called once; no automatic targeting; API integration only";
            }
            if (action == "voyage-pot-wear") return PotWear(command, game);
            if (action == "voyage-tooltip") return Tooltip(command, game);
            if (action == "voyage-move-item") return MoveItem(command, game);
            if (action == "voyage-restore-item") return RestoreItem(game);
            throw new InvalidOperationException("Unknown Voyage QA command " + action);
        }

        internal static object Snapshot(Type bootstrap)
        {
            return new
            {
                diagnostics = Safe(() => bootstrap == null ? null : JToken.Parse((string)bootstrap.GetMethod("GetVoyageDiagnostics").Invoke(null, null))),
                ui = Safe(UI), config = Safe(Config), fishing = Safe(Fishing), cargo = Safe(Cargo),
                gear = Safe(Gear), tooltips = Safe(Tooltips), nearbyHarvestPOIs = Safe(NearbyHarvestPOIs),
                pendingMove = pendingMove == null ? null : new { pendingMove.Item.id, original = pendingMove.Original, current = Position(pendingMove.Item) }
            };
        }

        private static object UI()
        {
            var selectables = Object.FindObjectsOfType<Selectable>(true).Where(x => x.gameObject.scene.IsValid() &&
                (x.gameObject.activeInHierarchy || Relevant(x.transform))).Take(180).ToArray();
            return new
            {
                voyageRows = Object.FindObjectsOfType<DropdownSettingInput>(true).Where(x => x.name.StartsWith("VoyageOption", StringComparison.Ordinal))
                    .Select(x => new { id = x.GetInstanceID(), path = Path(x.transform), x.CurrentIndex, active = x.gameObject.activeInHierarchy,
                        rect = Rect(x.transform as RectTransform), text = x.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text).ToArray() }).ToArray(),
                tabs = Object.FindObjectsOfType<TabbedPanelContainer>(true).Where(x => x.TabbedPanels.Any(t => t.panel != null && t.panel.name == "VoyageExperiencePanel"))
                    .Select(x => new { id = x.GetInstanceID(), x.CurrentIndex, active = x.gameObject.activeInHierarchy,
                        panels = x.TabbedPanels.Select(t => new { name = t.panel.name, active = t.panel.gameObject.activeInHierarchy }).ToArray() }).ToArray(),
                dialogs = Object.FindObjectsOfType<SettingsDialog>(true).Select(x => new { id = x.GetInstanceID(), path = Path(x.transform), active = x.gameObject.activeInHierarchy,
                    children = x.GetComponentsInChildren<CanvasGroup>(true).Select(c => new { path = Path(c.transform), c.alpha, c.interactable, c.blocksRaycasts, active = c.gameObject.activeInHierarchy }).ToArray() }).ToArray(),
                controls = selectables.Select(x => new { id = x.GetInstanceID(), path = Path(x.transform), type = x.GetType().Name,
                    active = x.gameObject.activeInHierarchy, interactable = x.IsInteractable(), rect = Rect(x.transform as RectTransform),
                    text = x.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text).Take(12).ToArray(),
                    navigation = new { mode = x.navigation.mode.ToString(), up = Id(x.navigation.selectOnUp), down = Id(x.navigation.selectOnDown), left = Id(x.navigation.selectOnLeft), right = Id(x.navigation.selectOnRight) } }).ToArray(),
                wrappers = Object.FindObjectsOfType<BasicButtonWrapper>(true).Where(x => x.gameObject.activeInHierarchy || Relevant(x.transform)).Take(100)
                    .Select(x => new { id = x.GetInstanceID(), buttonId = Id(x.Button), path = Path(x.transform), active = x.gameObject.activeInHierarchy,
                        interactable = x.Interactable, rect = Rect(x.transform as RectTransform), text = x.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text).ToArray() }).ToArray(),
                dropdowns = Object.FindObjectsOfType<TMP_Dropdown>(true).Where(x => x.gameObject.activeInHierarchy || Relevant(x.transform)).Take(60)
                    .Select(x => new { id = x.GetInstanceID(), path = Path(x.transform), x.value, active = x.gameObject.activeInHierarchy,
                        interactable = x.IsInteractable(), caption = x.captionText == null ? null : x.captionText.text,
                        options = x.options.Select(o => o.text).ToArray(), rect = Rect(x.transform as RectTransform) }).ToArray(),
                visibleText = Object.FindObjectsOfType<TMP_Text>().Where(x => x.gameObject.activeInHierarchy && !string.IsNullOrEmpty(x.text)).Take(220)
                    .Select(x => new { path = Path(x.transform), text = x.text, rect = Rect(x.transform as RectTransform) }).ToArray(),
                selected = EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null ? null : Path(EventSystem.current.currentSelectedGameObject.transform)
            };
        }

        private static object Config()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.GameConfigData == null) return null;
            GameConfigData config = game.GameConfigData;
            return new
            {
                chances = new { config.BaseAberrationSpawnChance, config.NightAberrationSpawnChance, config.MaxAberrationSpawnChance,
                    config.SpecialSpotAberrationSpawnBonus, config.SpawnChanceIncreasePerNonAberrationCaught, config.AberrationStartDay },
                fishing = config.FishingDifficultyConfigs == null ? null : config.FishingDifficultyConfigs.Select(x => new { difficulty = x.Key.ToString(), values = Difficulty(x.Value) }).ToArray(),
                dredging = config.DredgingDifficultyConfigs == null ? null : config.DredgingDifficultyConfigs.Select(x => new { difficulty = x.Key.ToString(), values = Difficulty(x.Value) }).ToArray(),
                clock = new { config.HourDurationInSeconds, config.ForcedTimePassageSpeedModifier,
                    forceful = game.Time != null && game.Time.IsTimePassingForcefully() },
                maintenance = game.PlayerStats == null ? 0f : game.PlayerStats.ResearchedEquipmentMaintenanceModifier
            };
        }

        private static object Difficulty(HarvestDifficultyConfigData x)
        {
            if (x == null) return null;
            return new { x.name, x.targetValue, x.secondsToPassivelyCatch, x.rotationSpeed, x.minTargets, x.maxTargets, x.minTargetWidth, x.maxTargetWidth,
                x.specialTargetWidth, x.numPendulumSegments, x.valueFactor, x.speedFactor, x.ballTrophySpeedFactor, x.targetZoneDegrees,
                x.diamondRotation, x.diamondScaleUpTimeSec, x.diamondTrophySpeedFactor, x.spiralRotationSpeed,
                x.spiralNumNotches, x.spiralMinNotchWidth, x.spiralMaxNotchWidth, x.spiralValueFactor };
        }

        private static object Fishing()
        {
            return Object.FindObjectsOfType<HarvestMinigame>(true).Select(x => new { id = x.GetInstanceID(), x.name,
                type = x.GetType().Name, active = x.gameObject.activeInHierarchy, x.IsGameRunning, x.Progress, x.DidHitSpecialTarget,
                parameters = Difficulty(Read(x, "difficultyConfig") as HarvestDifficultyConfigData),
                state = PrimitiveFields(x, "indicatorAngle", "isTrophyNotchShowing", "showTrophyNotch", "specialTargetIndex", "trophyNotchIndex", "currentBallProgressProp", "targetZoneAngleMin", "targetZoneAngleMax"),
                targets = Targets(x), notches = (Read(x, "notches") as List<Vector2>) == null ? null : ((List<Vector2>)Read(x, "notches")).Select(v => new[] { v.x, v.y }).ToArray(),
                spiralGates = x is SpiralMinigame ? Safe(() => SpiralGates(x)) : null
            }).ToArray();
        }

        private static object SpiralGates(HarvestMinigame owner)
        {
            IEnumerable animators = Read(owner, "gateAnimators") as IEnumerable;
            Transform container = Read(owner, "gateContainer") as Transform;
            var gates = new List<object>();
            if (animators != null)
                foreach (object entry in animators)
                {
                    Component component = entry as Component;
                    Transform transform = component == null ? null : component.transform;
                    RectTransform rect = transform as RectTransform;
                    gates.Add(new { index = gates.Count, id = component == null ? 0 : component.GetInstanceID(),
                        active = component != null && component.gameObject.activeInHierarchy,
                        worldPosition = transform == null ? null : XYZ(transform.position),
                        localPosition = transform == null ? null : XYZ(transform.localPosition),
                        anchoredPosition = rect == null ? null : new[] { rect.anchoredPosition.x, rect.anchoredPosition.y },
                        rotation = transform == null ? null : new[] { transform.rotation.x, transform.rotation.y, transform.rotation.z, transform.rotation.w } });
                }
            return new { animatorCount = animators == null ? (int?)null : gates.Count,
                containerChildCount = container == null ? (int?)null : container.childCount, gates = gates.ToArray() };
        }

        private static object NearbyHarvestPOIs()
        {
            GameManager game = GameManager.Instance;
            if (game == null || !game.IsPlaying || game.Player == null || game.SaveData == null) return null;
            Vector3 player = game.Player.transform.position;
            // Fixed existing scene POIs only. Never roll a fish, refill stock or instantiate a point.
            return Object.FindObjectsOfType<HarvestPOI>(true).Where(x => x.gameObject.scene.IsValid() &&
                x.Harvestable is HarvestPOIDataModel && (x.transform.position - player).sqrMagnitude <= 80f * 80f)
                .OrderBy(x => (x.transform.position - player).sqrMagnitude).Take(16)
                .Select(x => Safe(() => HarvestPOIInfo(x, player))).ToArray();
        }

        private static object HarvestPOIInfo(HarvestPOI poi, Vector3 player)
        {
            HarvestPOIDataModel data = (HarvestPOIDataModel)poi.Harvestable;
            float stock = data.GetStockCount(false);
            HarvestableItemData item = stock > 0f ? data.GetActiveFirstHarvestableItem() : null;
            bool hasEquipment = item != null && GameManager.Instance.PlayerStats.GetHasEquipmentForHarvestType(item.harvestableType, item.requiresAdvancedEquipment);
            return new { id = poi.GetInstanceID(), poi.name, poiId = data.id, path = Path(poi.transform),
                active = poi.gameObject.activeInHierarchy, worldPosition = XYZ(poi.transform.position), distance = Vector3.Distance(player, poi.transform.position),
                harvestability = data.IsHarvestable().ToString(), stock, storedStock = data.GetStockCount(true),
                itemId = item == null ? null : item.id, minigame = item == null ? null : item.harvestMinigameType.ToString(),
                requiresAdvancedEquipment = item != null && item.requiresAdvancedEquipment, hasEquipment,
                dayItems = data.items == null ? null : data.items.Where(x => x != null).Select(x => x.id).ToArray(),
                nightItems = data.nightItems == null ? null : data.nightItems.Where(x => x != null).Select(x => x.id).ToArray() };
        }

        private static float[] XYZ(Vector3 value) { return new[] { value.x, value.y, value.z }; }

        private static object Targets(object owner)
        {
            IEnumerable list = Read(owner, "targetConfigs") as IEnumerable;
            if (list == null) return null;
            var result = new List<object>();
            foreach (object entry in list) if (entry != null) result.Add(PrimitiveFields(entry, "angleDeg", "widthDeg", "isSpecial"));
            return result.ToArray();
        }

        private static object Cargo()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.SaveData == null || game.SaveData.Inventory == null) return null;
            SerializableGrid grid = game.SaveData.Inventory;
            var cells = new List<object>();
            if (grid.Grid != null)
                foreach (GridCellData cell in grid.Grid)
                    if (cell != null) cells.Add(new { xy = new[] { cell.x, cell.y }, cell.IsHidden, cell.damageImmune,
                        type = cell.acceptedItemType.ToString(), subtype = cell.acceptedItemSubtype.ToString(),
                        acceptedId = cell.acceptedItemData == null ? null : cell.acceptedItemData.id,
                        item = cell.spatialItemInstance == null ? null : cell.spatialItemInstance.id,
                        underlay = cell.underlaySpatialItemInstance == null ? null : cell.underlaySpatialItemInstance.id });
            return new { funds = game.SaveData.Funds, owned = game.SaveData.GetBoolVariable("jinke-voyage-cargo-expanded-v1"),
                level = game.SaveData.GetIntVariable("jinke-voyage-cargo-level-v2"),
                width = grid.Grid == null ? 0 : grid.Grid.GetLength(0), height = grid.Grid == null ? 0 : grid.Grid.GetLength(1),
                configuration = grid.GridConfiguration == null ? null : grid.GridConfiguration.name,
                cells = cells.ToArray(), items = grid.spatialItems.Select(Item).ToArray(), underlays = grid.spatialUnderlayItems.Select(Item).ToArray() };
        }

        private static object Gear()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.ItemManager == null || !game.ItemManager.HasLoaded()) return null;
            return new
            {
                assets = game.ItemManager.GetAllItemsOfType<DeployableItemData>().Select(x => new { x.id, type = x.itemSubtype.ToString(), x.TimeBetweenCatchRolls, x.CatchRate, x.MaxDurabilityDays, x.aberrationBonus }).ToArray(),
                deployedPots = game.SaveData == null || game.SaveData.serializedCrabPotPOIs == null ? null : game.SaveData.serializedCrabPotPOIs.Select(x => new {
                    x.deployableItemId, position = new[] { x.x, x.z }, x.durability, x.lastUpdate, x.timeUntilNextCatchRoll,
                    items = x.grid == null ? null : x.grid.spatialItems.Select(Item).ToArray() }).ToArray(),
                net = Object.FindObjectsOfType<TrawlNetAbility>(true).Select(x => new { id = x.GetInstanceID(), state = PrimitiveFields(x, "isActive", "trawlMode", "timeUntilNextCatchRoll", "modifiedChange", "change"),
                    item = Item(Read(x, "trawlNetItemInstance") as SpatialItemInstance) }).ToArray()
            };
        }

        private static object Tooltips()
        {
            return Object.FindObjectsOfType<TooltipSectionDeployableDetails>(true).Select(x => new { id = x.GetInstanceID(), path = Path(x.transform),
                active = x.gameObject.activeInHierarchy, itemId = (Read(x, "deployableItemData") as DeployableItemData) == null ? null : ((DeployableItemData)Read(x, "deployableItemData")).id,
                state = PrimitiveFields(x, "currentDurability", "tooltipMode"), text = x.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text).ToArray() }).ToArray();
        }

        private static object PotWear(JObject command, GameManager game)
        {
            string id = (string)command["itemId"];
            Require(id == "pot1" || id == "pot8" || id == "tir-pot1", "Wear fixture is limited to pot1, pot8, tir-pot1");
            float elapsed = command["days"] == null ? 1f / 24f : (float)command["days"];
            Require(elapsed > 0f && elapsed <= 0.25f, "Wear fixture interval must be at most six game hours");
            DeployableItemData item = game.ItemManager.GetItemDataById<DeployableItemData>(id);
            Require(item != null, "Native equipment asset missing");
            // A detached native data object avoids adding POIs, event subscriptions,
            // catches, or fixtures to the live SaveData. Only its actual method runs.
            var fixture = (SerializedCrabPotPOIData)FormatterServices.GetUninitializedObject(typeof(SerializedCrabPotPOIData));
            fixture.deployableItemId = id; fixture.durability = item.MaxDurabilityDays; fixture.lastUpdate = 0f;
            Field(typeof(SerializedCrabPotPOIData), "deployableItemData").SetValue(fixture, item);
            float before = fixture.durability;
            bool broke = fixture.AdjustDurability(elapsed);
            return new { itemId = id, elapsed, before, after = fixture.durability, lost = before - fixture.durability, broke,
                scope = "Detached native crab-pot data; actual patched AdjustDurability, not live elapsed gameplay or a saved POI" };
        }

        private static object Tooltip(JObject command, GameManager game)
        {
            int id = (int)command["instanceId"];
            string itemId = (string)command["itemId"];
            var section = Object.FindObjectsOfType<TooltipSectionDeployableDetails>(true).SingleOrDefault(x => x.GetInstanceID() == id);
            Require(section != null, "Native tooltip section unavailable");
            SpatialItemInstance item = game.SaveData.Inventory.spatialItems.FirstOrDefault(x => x.id == itemId);
            Require(item != null && item.GetItemData<DeployableItemData>() != null, "Tooltip command requires a real tool already in this isolated inventory");
            section.Init(item.GetItemData<DeployableItemData>(), item, TooltipUI.TooltipMode.HOVER);
            return "Native tooltip Init; allow localization callback before next snapshot; API integration only";
        }

        private static object MoveItem(JObject command, GameManager game)
        {
            Require((bool?)command["fixture"] == true && pendingMove == null, "Explicit fixture=true and no pending item move are required");
            Require(game.Player.IsDocked && !game.GridManager.IsCurrentlyHoldingObject(), "Fixture movement requires docked empty cursor");
            int x = (int)command["fromX"], y = (int)command["fromY"], toX = (int)command["toX"], toY = (int)command["toY"];
            SerializableGrid grid = game.SaveData.Inventory;
            SpatialItemInstance item = grid.spatialItems.SingleOrDefault(i => i.x == x && i.y == y);
            Require(item != null, "A real inventory item root is required");
            Require(item.GetItemData<SpatialItemData>().dimensions.Count == 1, "Fixture movement supports one-cell real items only");
            var old = new[] { item.x, item.y, item.z };
            pendingMove = new MoveFixture { Grid = grid, Item = item, Original = old };
            Move(grid, item, toX, toY, item.z);
            return new { moved = item.id, from = old, to = Position(item), scope = "Existing item moved through native grid APIs in QA save; no item fabricated or deleted" };
        }

        private static object RestoreItem(GameManager game)
        {
            Require(pendingMove != null && ReferenceEquals(pendingMove.Grid, game.SaveData.Inventory), "No fixture belongs to current save");
            Require(game.Player.IsDocked && !game.GridManager.IsCurrentlyHoldingObject(), "Fixture restoration requires docked empty cursor");
            if (pendingMove.Item.x != pendingMove.Original[0] || pendingMove.Item.y != pendingMove.Original[1] || !pendingMove.Grid.spatialItems.Contains(pendingMove.Item))
                Move(pendingMove.Grid, pendingMove.Item, pendingMove.Original[0], pendingMove.Original[1], pendingMove.Original[2]);
            string id = pendingMove.Item.id; pendingMove = null;
            return new { restored = id };
        }

        private static void Move(SerializableGrid grid, SpatialItemInstance item, int x, int y, int z)
        {
            Require(x >= 0 && y >= 0 && x < grid.Grid.GetLength(0) && y < grid.Grid.GetLength(1), "Target cell outside inventory");
            GridCellData cell = grid.Grid[x, y];
            Require(cell != null && !cell.IsHidden && cell.spatialItemInstance == null && cell.underlaySpatialItemInstance == null && cell.DoesCellAcceptItem(item.GetItemData<SpatialItemData>()), "Target cell cannot accept this existing item");
            Vector3Int old = item.GetPosition();
            try
            {
                grid.RemoveObjectFromGridData(item, true);
                grid.AddObjectToGridData(item, new Vector3Int(x, y, z), true);
            }
            catch
            {
                // The pending restoration record survives a native UI listener
                // exception. Do not duplicate an item already added successfully.
                if (!grid.spatialItems.Contains(item)) grid.AddObjectToGridData(item, old, false);
                throw;
            }
        }

        private sealed class MoveFixture { internal SerializableGrid Grid; internal SpatialItemInstance Item; internal int[] Original; }
        private static object Item(SpatialItemInstance item) { return item == null ? null : new { item.id, position = Position(item), item.durability, damaged = item.GetIsOnDamagedCell() }; }
        private static int[] Position(SpatialItemInstance item) { return new[] { item.x, item.y, item.z }; }
        private static int Id(Object value) { return value == null ? 0 : value.GetInstanceID(); }
        private static bool Relevant(Transform transform) { string path = Path(transform); return path.IndexOf("Voyage", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Cargo", StringComparison.OrdinalIgnoreCase) >= 0; }
        private static string Path(Transform transform)
        {
            var names = new List<string>();
            for (Transform t = transform; t != null; t = t.parent) names.Add(t.name);
            names.Reverse(); return string.Join("/", names.ToArray());
        }
        private static float[] Rect(RectTransform rect)
        {
            if (rect == null) return null;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return new[] { a.x, a.y, b.x - a.x, b.y - a.y };
        }
        private static object Safe(Func<object> read) { try { return read(); } catch (Exception error) { return new { error = error.ToString() }; } }
        private static object Read(object owner, string name) { if (owner == null) return null; FieldInfo field = Field(owner.GetType(), name); return field == null ? null : field.GetValue(owner); }
        private static FieldInfo Field(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType) { FieldInfo field = current.GetField(name, AnyInstance | BindingFlags.DeclaredOnly); if (field != null) return field; }
            return null;
        }
        private static object PrimitiveFields(object owner, params string[] names)
        {
            var values = new Dictionary<string, object>();
            foreach (string name in names)
            {
                object value = Read(owner, name);
                if (value == null) continue;
                Type type = value.GetType();
                if (type.IsPrimitive || value is decimal || value is string) values[name] = value;
                else if (type.IsEnum) values[name] = value.ToString();
            }
            return values;
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
