using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Jinke.DredgeMerchantTravel.QA
{
    // Test-only commands: strict disposable identity, no imported save, no
    // production hooks. API fixtures and simulated navigation are labelled.
    internal static class ShipAppearanceProbe
    {
        private const string Identifier = "local.jinke.dredge.boat5.mvp.qa.20261021";
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] FixtureItems = { "net1", "rod1", "light1" };

        internal static object Execute(JObject command, Type bootstrap, string output)
        {
            Require(Application.identifier == Identifier && Application.productName == "DREDGE Boat5 MVP QA 20261021", "Disposable Boat5 identity required.");
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support", Identifier);
            Require(Path.GetFullPath(Application.persistentDataPath) == expected, "Disposable profile mismatch.");
            GameManager game = GameManager.Instance;
            Require(game != null && game.SaveManager != null, "Save manager is not ready.");
            string action = (string)command["action"];
            if (action == "ship-snapshot") return Snapshot(bootstrap);
            if (action == "ship-new")
            {
                Require((bool?)command["fixture"] == true && !game.IsPlaying && !game.SaveManager.HasAnySaveFiles() &&
                    game.HasLoadedAsyncManagers && !SceneLoader.BetweenScenes, "A fresh empty title profile is required.");
                game.SettingsSaveData.lastSaveSlot = 0;
                game.Loader.LoadGameFromTitle(true);
                return "Native NEW request from original template in empty disposable profile; API fixture, not physical New Game input.";
            }
            Require(game.IsPlaying && game.Player != null && game.SaveData != null &&
                ReferenceEquals(game.SaveData, game.SaveManager.ActiveSaveData), "Disposable world must be ready.");
            if (action == "ship-fixture")
            {
                Require((bool?)command["fixture"] == true && game.Player.IsDocked, "Explicit docked fixture required.");
                Require(game.EntitlementManager.GetHasEntitlement(Entitlement.DLC_2), "Boat5 requires the owned Iron Rig entitlement.");
                foreach (int tier in new[] { 2, 3, 4, 5 })
                {
                    HullUpgradeData upgrade = game.UpgradeManager.GetHullUpgradeDataByTier(tier) as HullUpgradeData;
                    Require(upgrade != null, "Native hull upgrade is missing.");
                    if (!game.SaveData.GetOwnedUpgrades().Any(x => x.id == upgrade.id))
                        game.UpgradeManager.AddUpgrade(upgrade, true);
                }
                foreach (SlotUpgradeData upgrade in game.UpgradeManager.allUpgradeData.OfType<SlotUpgradeData>().Where(x => x.tier == 5))
                    if (!game.SaveData.GetOwnedUpgrades().Any(x => x.id == upgrade.id))
                        game.UpgradeManager.AddUpgrade(upgrade, true);
                Require(game.SaveData.HullTier == 5 && game.Player.BoatModelProxy.name == "Boat5", "Native upgrade did not activate Boat5.");
                Require(!game.GridManager.IsCurrentlyHoldingObject(), "Fixture requires an empty inventory cursor.");
                object rodRelocation = MakeNetSpace(game);
                foreach (string id in FixtureItems)
                {
                    SerializableGrid grid = game.SaveData.Inventory;
                    if (!grid.spatialItems.Any(x => x.id == id))
                    {
                        Vector3Int position;
                        Require(grid.FindPositionForObject(game.ItemManager.GetItemDataById<SpatialItemData>(id), out position), "No native fixture position: " + id);
                        game.ItemManager.AddItemById(id, grid);
                    }
                    SpatialItemInstance installed = grid.spatialItems.SingleOrDefault(x => x.id == id);
                    Require(installed != null, "Fixture item could not be installed: " + id);
                    SpatialItemData data = installed.GetItemData<SpatialItemData>();
                    var cells = grid.GetCellsAffectedByObjectAtPosition(data.dimensions, installed.GetPosition());
                    Require(cells.Count == data.GetSize() && cells.All(c => !c.IsHidden && c.DoesCellAcceptItem(data) &&
                        ReferenceEquals(c.spatialItemInstance, installed)), "Fixture item footprint differs: " + id);
                }
                game.SaveData.SetBoolVariable("has-flag-3", true);
                game.DialogueRunner.ChangeBoatFlag(3);
                game.SaveData.Funds = 5000;
                foreach (UpgradeDestination destination in game.Player.CurrentDock.GetDestinations().OfType<UpgradeDestination>())
                    if (!game.SaveData.availableDestinations.Contains(destination.Id)) game.SaveData.availableDestinations.Add(destination.Id);
                game.Save();
                return new { scope = "Synthetic Boat5/gear fixture using native upgrade and item APIs; no imported progress", rodRelocation, snapshot = Snapshot(bootstrap) };
            }
            if (action == "ship-drydock")
            {
                Require(game.Player.IsDocked && !game.IsPaused && !game.DialogueRunner.IsDialogueRunning, "Normal dock required.");
                UpgradeDestination destination = game.Player.CurrentDock.GetDestinations().OfType<UpgradeDestination>().Single();
                game.UI.ShowDestination(destination);
                return "Native drydock destination requested; API setup, not physical dock selection.";
            }
            if (action == "ship-damage")
            {
                Require((bool?)command["fixture"] == true && game.Player.IsDocked, "Explicit docked damage fixture required.");
                int count = (int)command["count"];
                Require(count >= 0 && count <= game.PlayerStats.DamageThreshold, "Damage would exceed safe hull threshold.");
                game.ItemManager.RepairHullDamage(true);
                if (count > 0) game.GridManager.AddDamageToInventory(count);
                Require(game.Player.IsAlive && game.SaveData.GetNumberOfDamagedSlots() == count, "Native damage amount differs.");
                return new { scope = "Native repair/damage APIs on disposable progress; their own events drive actual mesh selection", count, snapshot = Snapshot(bootstrap) };
            }
            if (action == "ship-lights")
            {
                AbilityData light = game.PlayerAbilities.GetAbilityDataByName("lights");
                Require(light != null && game.PlayerAbilities.GetIsAbilityUnlocked(light), "Native light ability is locked.");
                return game.Player.GetComponentInChildren<LightAbility>(true).Activate();
            }
            if (action == "ship-trawl")
            {
                Require(!game.Player.IsDocked && !game.IsPaused && game.Player.IsAlive && !SceneLoader.BetweenScenes,
                    "Trawl fixture requires an unpaused, alive, undocked player.");
                AbilityData trawl = game.PlayerAbilities.GetAbilityDataByName("trawl");
                Require(trawl != null && game.PlayerAbilities.GetIsAbilityUnlocked(trawl), "Native trawl ability is locked.");
                SpatialItemInstance net = game.SaveData.EquippedTrawlNetInstance();
                Require(net != null && net.durability > 0, "Equipped native net requires positive durability.");
                bool accepted = game.PlayerAbilities.ActivateAbility(trawl);
                return new { accepted, net = net.id, durability = net.durability,
                    scope = "Native PlayerAbilityManager.ActivateAbility(trawl) API; NOT physical ability input", snapshot = Snapshot(bootstrap) };
            }
            if (action == "ship-quit")
            {
                Require((bool?)command["fixture"] == true && !SceneLoader.BetweenScenes && !game.SaveData.ForbidSave &&
                    game.Player.IsAlive && !game.Player.IsImmuneModeEnabled, "Safe active disposable save required for quit.");
                int completions = 0;
                Action observedSave = () => completions++;
                game.SaveManager.OnSaveComplete += observedSave;
                try { game.Save(); }
                finally { game.SaveManager.OnSaveComplete -= observedSave; }
                Require(completions >= 2, "Native game and settings save completions were not observed.");
                Application.Quit();
                return new { requested = true, nativeSaveCompletions = completions,
                    scope = "Unity Application.Quit after native GameManager.Save; NOT physical menu input. Original QuitGameButton uses Process.Kill. Actual exit code belongs to the external runner." };
            }
            if (action == "ship-change")
            {
                string id = (string)command["skin"];
                Require(id == "original" || id == "faceted-trawler-v1", "Unknown test selection.");
                object[] args = { id, null };
                bool accepted = (bool)Appearance(bootstrap).GetMethod("Change", Static).Invoke(null, args);
                return new { accepted, error = args[1], scope = "Production Change API; not physical selection" };
            }
            if (action == "ship-move")
            {
                Require(EventSystem.current != null, "No event system.");
                string direction = (string)command["direction"];
                Require(new[] { "Left", "Right", "Up", "Down" }.Contains(direction), "Bounded navigation direction required.");
                var selected = EventSystem.current.currentSelectedGameObject;
                Require(selected != null && selected.activeInHierarchy, "No active selected control.");
                ExecuteEvents.Execute(selected, new AxisEventData(EventSystem.current) { moveDir = (MoveDirection)Enum.Parse(typeof(MoveDirection), direction) }, ExecuteEvents.moveHandler);
                return new { scope = "Simulated Unity move event, NOT physical controller input", snapshot = Snapshot(bootstrap) };
            }
            if (action == "ship-focus")
            {
                Require(EventSystem.current != null, "No event system.");
                int id = (int)command["instanceId"];
                BasicButtonWrapper wrapper = Object.FindObjectsOfType<BasicButtonWrapper>(true).SingleOrDefault(x => x.GetInstanceID() == id);
                Require(wrapper != null && wrapper.isActiveAndEnabled && wrapper.gameObject.activeInHierarchy && wrapper.Interactable &&
                    wrapper.Button != null && wrapper.Button.IsActive() && wrapper.Button.IsInteractable(), "Visible interactable wrapper required.");
                foreach (CanvasGroup group in wrapper.GetComponentsInParent<CanvasGroup>())
                    Require(group.alpha > 0 && group.interactable && group.blocksRaycasts, "Wrapper is hidden or blocked.");
                float[] rect = Rect(wrapper.transform as RectTransform);
                Require(rect != null && rect[2] > 0 && rect[0] < Screen.width && rect[3] > 0 && rect[1] < Screen.height, "Wrapper is outside the screen.");
                EventSystem.current.SetSelectedGameObject(wrapper.Button.gameObject);
                return new { scope = "Simulated EventSystem focus, NOT physical mouse or controller input", wrapper = id, snapshot = Snapshot(bootstrap) };
            }
            if (action == "ship-preview") return Preview(command, output, game.Player);
            if (action == "ship-screen")
            {
                string path = FreshImage(output, (string)command["id"]);
                ScreenCapture.CaptureScreenshot(path);
                return new { path, scope = "Game framebuffer capture requested; confirm file after end of frame" };
            }
            throw new InvalidOperationException("Unknown Boat5 QA command.");
        }

        private static object MakeNetSpace(GameManager game)
        {
            SerializableGrid grid = game.SaveData.Inventory;
            if (grid.spatialItems.Any(x => x.id == "net1")) return null;
            SpatialItemData net = game.ItemManager.GetItemDataById<SpatialItemData>("net1");
            Vector3Int found;
            Require(net != null, "Native net1 data is missing.");
            if (grid.FindPositionForObject(net, out found)) return null;
            SpatialItemInstance rod = grid.spatialItems.SingleOrDefault(x => x.id == "rod1");
            Require(rod != null, "Expected the original rod1 blocking the native net footprint.");
            SpatialItemData data = rod.GetItemData<SpatialItemData>();
            Vector3Int old = rod.GetPosition();
            Require(grid.GetCellsAffectedByObjectAtPosition(data.dimensions, old).Any(c => c.DoesCellAcceptItem(net)), "Original rod is not blocking a net cell.");
            for (int y = 0; y < grid.Grid.GetLength(1); y++)
                for (int x = 0; x < grid.Grid.GetLength(0); x++)
                    foreach (int z in new[] { 0, 90, 180, 270 })
                    {
                        if (!grid.TestPos(data, x, y, z, out found)) continue;
                        var cells = grid.GetCellsAffectedByObjectAtPosition(data.dimensions, found);
                        if (cells.Count != data.GetSize() || cells.Any(c => c.IsHidden || c.spatialItemInstance != null ||
                            c.underlaySpatialItemInstance != null || !c.DoesCellAcceptItem(data) || c.DoesCellAcceptItem(net))) continue;
                        int count = grid.spatialItems.Count;
                        try
                        {
                            grid.RemoveObjectFromGridData(rod, true);
                            grid.AddObjectToGridData(rod, found, true);
                        }
                        catch
                        {
                            // A native listener can throw after adding successfully. Preserve
                            // the same object without duplicating it; a rerun remains safe.
                            if (!grid.spatialItems.Contains(rod)) grid.AddObjectToGridData(rod, old, false);
                            throw;
                        }
                        Require(grid.spatialItems.Count == count && grid.spatialItems.Count(i => ReferenceEquals(i, rod)) == 1,
                            "Moving the original rod changed inventory membership.");
                        Require(grid.GetCellsAffectedByObjectAtPosition(data.dimensions, found).All(c => ReferenceEquals(c.spatialItemInstance, rod)),
                            "Native rod relocation footprint differs.");
                        Require(grid.FindPositionForObject(net, out found), "Rod relocation did not free the native net footprint.");
                        return new { id = rod.id, from = new[] { old.x, old.y, old.z }, to = new[] { rod.x, rod.y, rod.z } };
                    }
            throw new InvalidOperationException("No fully valid non-net rod footprint; original equipment was preserved.");
        }

        private static object Snapshot(Type bootstrap)
        {
            GameManager game = GameManager.Instance;
            Player player = game == null ? null : game.Player;
            BoatModelProxy boat = player == null ? null : player.BoatModelProxy;
            Type appearance = Appearance(bootstrap);
            return new
            {
                identifier = Application.identifier, profile = Application.persistentDataPath,
                gameTime = game == null || game.SaveData == null ? 0 : game.SaveData.Time,
                ready = appearance.GetProperty("Ready", Static).GetValue(null, null),
                selected = appearance.GetProperty("SelectedId", Static).GetValue(null, null),
                error = appearance.GetProperty("Status", Static).GetValue(null, null),
                stored = game == null || game.SaveData == null ? null : game.SaveData.GetStringVariable("jinke-ship-appearance-v1", "<missing>"),
                tier = game == null || game.SaveData == null ? 0 : game.SaveData.HullTier,
                damage = game == null || game.SaveData == null ? 0 : game.SaveData.GetNumberOfDamagedSlots(),
                threshold = game == null || game.PlayerStats == null ? 0 : game.PlayerStats.DamageThreshold,
                boat = boat == null ? null : new { name = boat.name, instance = boat.GetInstanceID(),
                    states = boat.DamageStateMeshes.Select(MeshInfo).ToArray(), current = MeshInfo(boat.BoatMeshRenderer.GetComponent<MeshFilter>().sharedMesh),
                    renderer = boat.BoatMeshRenderer.GetInstanceID(), materials = boat.BoatMeshRenderer.sharedMaterials.Select(m => new { id = m.GetInstanceID(), name = m.name }).ToArray(),
                    lightStrength = boat.BoatMeshRenderer.sharedMaterials[1].GetFloat("_LightStrength"),
                    attachments = boat.GetComponentsInChildren<Transform>(true).Where(t => new[] { "Light", "FishingRod", "Trawl", "Flag", "rudder", "propeller", "Chimney" }
                        .Any(part => t.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)).Select(t => new { id = t.GetInstanceID(), path = Hierarchy(t), active = t.gameObject.activeInHierarchy,
                            local = V(t.localPosition), scale = V(t.localScale) }).ToArray() },
                physics = player == null ? null : new { colliders = player.GetComponentsInChildren<Collider>(true).Select(c => new { id = c.GetInstanceID(), path = Hierarchy(c.transform),
                    c.enabled, c.isTrigger, layer = c.gameObject.layer, mesh = c is MeshCollider && ((MeshCollider)c).sharedMesh != null ? ((MeshCollider)c).sharedMesh.name : null }).ToArray(),
                    rigidbody = player.GetComponent<Rigidbody>().GetInstanceID(), mass = player.GetComponent<Rigidbody>().mass,
                    effectors = player.GetComponent<BuoyantObject>().effectors.Select(t => new { id = t.GetInstanceID(), local = V(t.localPosition) }).ToArray() },
                ui = UI(), joystickNames = Input.GetJoystickNames(), usingController = game != null && game.Input != null && game.Input.IsUsingController
            };
        }

        private static object UI()
        {
            return new { selected = EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null ? null : Hierarchy(EventSystem.current.currentSelectedGameObject.transform),
                width = Screen.width, height = Screen.height,
                buttons = Object.FindObjectsOfType<BasicButtonWrapper>(true).Where(b => b.gameObject.activeInHierarchy).Take(90).Select(b => new { name = b.name, instance = b.GetInstanceID(),
                    button = b.Button.GetInstanceID(), active = b.gameObject.activeInHierarchy, interactable = b.Interactable,
                    text = b.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text).ToArray(), rect = Rect(b.transform as RectTransform),
                    nav = new { left = Name(b.Button.navigation.selectOnLeft), right = Name(b.Button.navigation.selectOnRight), up = Name(b.Button.navigation.selectOnUp), down = Name(b.Button.navigation.selectOnDown) } }).ToArray() };
        }
        private static object Preview(JObject command, string output, Player player)
        {
            int angle = (int?)command["angle"] ?? 0;
            Require(angle >= 0 && angle <= 3, "Four fixed preview angles only.");
            string path = FreshImage(output, (string)command["id"]);
            Vector3[] offsets = { new Vector3(3.3f, 2.9f, 4.5f), new Vector3(-3.3f, 2.9f, 4.5f), new Vector3(3.3f, 2.9f, -4.5f), new Vector3(0, 5.5f, .1f) };
            var host = new GameObject("Boat5-QA-observation-camera");
            RenderTexture texture = null; Texture2D image = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                Camera camera = host.AddComponent<Camera>(); camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .14f, .18f, 1);
                camera.nearClipPlane = .05f; camera.farClipPlane = 30; camera.fieldOfView = 38;
                camera.transform.position = player.transform.TransformPoint(offsets[angle]);
                camera.transform.LookAt(player.transform.TransformPoint(new Vector3(0, .9f, .2f)), player.transform.up);
                texture = new RenderTexture(768, 768, 24); camera.targetTexture = texture;
                camera.Render(); RenderTexture.active = texture;
                image = new Texture2D(768, 768, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 768, 768), 0, 0); image.Apply();
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                { byte[] bytes = image.EncodeToPNG(); stream.Write(bytes, 0, bytes.Length); }
                return new { path, angle, scope = "Actual Unity GPU offscreen camera; same live boat, not normal player camera/input" };
            }
            finally
            {
                RenderTexture.active = previous;
                if (image != null) Object.Destroy(image);
                if (texture != null) { texture.Release(); Object.Destroy(texture); }
                Object.Destroy(host);
            }
        }
        private static string FreshImage(string output, string id)
        {
            Require(!string.IsNullOrEmpty(id) && id.All(c => char.IsLetterOrDigit(c) || c == '-'), "Image ID must be bounded.");
            string path = Path.Combine(output, id + ".png"); Require(!File.Exists(path), "Image already exists."); return path;
        }
        private static Type Appearance(Type bootstrap) { return bootstrap.Assembly.GetType("Jinke.DredgeShipAppearance.ShipAppearance", true); }
        private static object MeshInfo(Mesh mesh) { return mesh == null ? null : new { name = mesh.name, instance = mesh.GetInstanceID(), vertices = mesh.vertexCount, submeshes = mesh.subMeshCount, center = V(mesh.bounds.center), size = V(mesh.bounds.size) }; }
        private static string Name(Selectable value) { return value == null ? null : value.name; }
        private static float[] Rect(RectTransform rect)
        {
            if (rect == null) return null;
            Vector3[] corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(null, corners[0]), b = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            return new[] { a.x, a.y, b.x, b.y };
        }
        private static string Hierarchy(Transform t) { string result = t.name; while (t.parent != null) { t = t.parent; result = t.name + "/" + result; } return result; }
        private static float[] V(Vector3 p) { return new[] { p.x, p.y, p.z }; }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
