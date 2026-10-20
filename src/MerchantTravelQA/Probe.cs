using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jinke.DredgeMerchantTravel.QA
{
    // This assembly is never included in the playable delivery. Commands prepare explicit
    // isolated fixtures or call normal game/component APIs; they do not count as UI tests.
    public sealed class Probe : MonoBehaviour
    {
        private const string Identifier = "local.jinke.dredge.merchanttravel.qa.20261018";
        private const string VoyageIdentifier = "local.jinke.dredge.voyage.qa.20261019";
        private const string PainterIdentifier = "local.jinke.dredge.voyage.painter.qa.20261019";
        private static Probe instance;
        private string output, lastCommand;
        private float nextPoll;
        private GameEvents events;
        private SaveManager observedSaveManager;
        private int saveCompletions;
        private int teleportBegins, teleportCompletes;
        private bool executing, outputValidated, achievementWritesSuppressed;
        private bool radialWasOpen;
        private int radialObservations;
        private Type bootstrap;
        private readonly Queue<object> observedInputs = new Queue<object>();
        private static UnityEngine.SceneManagement.Scene geometryScene;
        private readonly JsonSerializerSettings json = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            Formatting = Formatting.Indented
        };

        public static void AfterTitle(object owner) { StartProbe(); }
        public static void AfterPlayer(object owner) { StartProbe(); }
        private static void StartProbe()
        {
            if (instance != null || Environment.GetEnvironmentVariable("JINKE_MERCHANT_QA") != "1") return;
            var host = new GameObject("MerchantTravel-QA-ONLY");
            Object.DontDestroyOnLoad(host);
            instance = host.AddComponent<Probe>();
        }
        private void Awake()
        {
            try
            {
                output = Environment.GetEnvironmentVariable("JINKE_MERCHANT_QA_OUTPUT");
                Require(!string.IsNullOrEmpty(output) && Path.IsPathRooted(output), "Absolute QA output required");
                output = Path.GetFullPath(output);
                Require(Directory.Exists(output), "QA output must be explicitly created by the runner");
                Require(!File.Exists(Path.Combine(output, "started.json")), "Fresh QA session required");
                NoLinks(output);
                bool voyage = Application.identifier == VoyageIdentifier && Application.productName == "DREDGE Voyage QA 20261019";
                bool painter = Application.identifier == PainterIdentifier && Application.productName == "DREDGE Voyage Painter QA 20261019";
                bool merchant = Application.identifier == Identifier && Application.productName == "DREDGE Merchant Travel QA 20261018";
                Require(voyage || painter || merchant, "Wrong QA identity");
                Require(Application.dataPath.Replace('\\', '/').Contains("/output/runtime/") && Application.dataPath.Contains("QA"), "Probe may only run in a runtime QA app");
                string runtimeRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
                string evidenceFolder = painter ? "painter-2026-10-19" : voyage ? "voyage-testing" : "merchanttravel-testing";
                Require(output.StartsWith(Path.Combine(runtimeRoot, evidenceFolder) + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Evidence output escapes the explicit QA workspace");
                outputValidated = true;
                bootstrap = Assembly.Load("Jinke.DredgeMerchantTravel").GetType("Jinke.DredgeMerchantTravel.Bootstrap", true);
                bootstrap.GetMethod("EnsureIsolatedProfile").Invoke(null, null);
                string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support", painter ? PainterIdentifier : voyage ? VoyageIdentifier : Identifier);
                Require(Path.GetFullPath(Application.persistentDataPath) == expected, "Wrong QA save root");
                Require(Path.GetFullPath(typeof(Probe).Assembly.Location).StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Probe not loaded from current app");
                // Synthetic QA progress must not award/clear Steam account achievements.
                // This uses the game's own no-op strategy only in the instrumented test app.
                var achievements = GameManager.Instance.AchievementManager;
                var strategy = typeof(AchievementManager).GetField("achievementStrategy", BindingFlags.NonPublic | BindingFlags.Instance);
                Require(achievements != null && strategy != null, "QA achievement isolation unavailable");
                strategy.SetValue(achievements, new DefaultAchievementStrategy());
                achievementWritesSuppressed = true;
                Write("started", Snapshot());
            }
            catch (Exception ex)
            {
                if (outputValidated)
                    try { Write("refused", new { error = ex.ToString() }); } catch (Exception) { }
                enabled = false;
            }
        }
        private void Update()
        {
            string[] observedKeys = new[] { KeyCode.Return, KeyCode.Escape, KeyCode.Space, KeyCode.Tab, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.F8, KeyCode.F9,
                KeyCode.F, KeyCode.E, KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.X }
                .Where(Input.GetKeyDown).Select(k => k.ToString()).ToArray();
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonUp(1) || observedKeys.Length != 0)
            {
                observedInputs.Enqueue(new { frame = Time.frameCount, mouseDown = Input.GetMouseButtonDown(0), mouseUp = Input.GetMouseButtonUp(0),
                    rightMouseDown = Input.GetMouseButtonDown(1), rightMouseUp = Input.GetMouseButtonUp(1),
                    enter = Input.GetKeyDown(KeyCode.Return), f9 = Input.GetKeyDown(KeyCode.F9), keys = observedKeys, position = V(Input.mousePosition), focused = Application.isFocused });
                while (observedInputs.Count > 32) observedInputs.Dequeue();
            }
            if (GameEvents.Instance != events)
            {
                if (events != null) { events.OnTeleportBegin -= OnBegin; events.OnTeleportComplete -= OnComplete; }
                events = GameEvents.Instance;
                if (events != null) { events.OnTeleportBegin += OnBegin; events.OnTeleportComplete += OnComplete; }
            }
            ObserveRadialTransition();
            SaveManager saveManager = GameManager.Instance == null ? null : GameManager.Instance.SaveManager;
            if (saveManager != observedSaveManager)
            {
                if (observedSaveManager != null) observedSaveManager.OnSaveComplete -= OnSaveComplete;
                observedSaveManager = saveManager;
                if (observedSaveManager != null) observedSaveManager.OnSaveComplete += OnSaveComplete;
            }
            if (Time.realtimeSinceStartup < nextPoll || executing) return;
            nextPoll = Time.realtimeSinceStartup + 0.1f;
            string path = Path.Combine(output, "command.json");
            if (!File.Exists(path)) return;
            executing = true;
            string id = null;
            try
            {
                Require(new FileInfo(path).Length < 16384, "QA command too large");
                var c = JObject.Parse(File.ReadAllText(path));
                id = (string)c["id"];
                Require(!string.IsNullOrEmpty(id) && id.All(ch => char.IsLetterOrDigit(ch) || ch == '-'), "Invalid command ID");
                if (id == lastCommand) return;
                lastCommand = id;
                Require(!File.Exists(Path.Combine(output, id + ".json")), "Command was already recorded");
                var before = Snapshot();
                object result = Execute(c);
                Write(id, new { utc = DateTime.UtcNow.ToString("o"), command = c, scope = "QA fixture / API integration; not a mouse or keyboard action", result, before, after = Snapshot() });
            }
            catch (Exception ex)
            {
                if (id != null && !File.Exists(Path.Combine(output, id + ".json")))
                    Write(id, new { error = ex.ToString(), snapshot = Snapshot() });
            }
            finally { executing = false; }
        }
        private void ObserveRadialTransition()
        {
            try
            {
                if (bootstrap == null) return;
                var controller = bootstrap.GetField("controller", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                var property = controller == null ? null : controller.GetType().GetProperty("Open", BindingFlags.Instance | BindingFlags.NonPublic);
                bool isOpen = property != null && (bool)property.GetValue(controller, null);
                if (isOpen == radialWasOpen) return;
                radialWasOpen = isOpen;
                Observe("radial-" + (isOpen ? "open-" : "closed-") + (++radialObservations));
            }
            catch (Exception) { /* Observation must never affect production input. */ }
        }
        private object Execute(JObject c)
        {
            var g = GameManager.Instance;
            string action = (string)c["action"];
            if (action == "snapshot") return "observed";
            if (action != null && action.StartsWith("painter-", StringComparison.Ordinal))
            {
                Require(Application.identifier == PainterIdentifier, "Painter commands require the strict Painter QA identity");
                return PainterProbe.Execute(c, bootstrap, output);
            }
            if (action != null && action.StartsWith("voyage-", StringComparison.Ordinal))
            {
                Require(Application.identifier == VoyageIdentifier || Application.identifier == PainterIdentifier, "Voyage commands require the strict Voyage QA identity");
                return VoyageProbe.Execute(c, bootstrap);
            }
            Require(g != null && g.SaveManager != null, "Game manager unavailable");
            if (action == "load")
            {
                Require(!g.IsPlaying && g.HasLoadedAsyncManagers && !SceneLoader.BetweenScenes, "Title not ready");
                if (c["slot"] != null)
                {
                    int slot = (int)c["slot"];
                    Require(slot == 0 || slot == 1, "QA uses only the two explicit test slots");
                    Require(g.SaveManager.HasSaveFile(slot), "Requested isolated test slot does not exist");
                    g.SettingsSaveData.lastSaveSlot = slot;
                }
                Require(g.SaveManager.CanLoadLast(), "No existing QA fixture");
                var data = g.SaveManager.LoadIntoMemory(g.SettingsSaveData.lastSaveSlot);
                Require(data != null, "Corrupt QA save");
                foreach (Entitlement e in new[] { Entitlement.DLC_1, Entitlement.DLC_2 })
                    Require(!data.GetSaveUsesEntitlement(e) || g.EntitlementManager.GetHasEntitlement(e), "Unowned DLC in fixture");
                g.Loader.LoadGameFromTitle(false); return "native scene load requested";
            }
            Require(g.IsPlaying && g.Player != null && g.SaveData != null, "World not ready");
            switch (action)
            {
                case "open": return Call("RequestOpen");
                case "travel": return Call("RequestTravel", (string)c["dockId"]);
                case "cancel": return Call("Cancel");
                case "save": g.Save(); return "native save requested";
                case "save-settings": g.SaveManager.SaveSettings(); return "native settings-only save requested";
                case "title":
                    Require(!SceneLoader.BetweenScenes && !g.SaveData.ForbidSave && g.Player.IsAlive && !g.Player.IsImmuneModeEnabled, "Unsafe title transition");
                    Call("Cancel"); g.Save(); g.Loader.LoadTitleFromGame(); return "native save and return to title requested";
                case "undock":
                    Require(g.Player.IsDocked && !g.IsPaused && !g.DialogueRunner.IsDialogueRunning &&
                        g.Input.GetActiveActionLayer() == ActionLayer.DOCKED && g.UI.ShowingWindowTypes.Contains(UIWindowType.DOCK),
                        "Undock requires the visible normal dock screen without pause or dialogue");
                    var dockScreens = Object.FindObjectsOfType<DockUI>().Where(screen => screen.isActiveAndEnabled && screen.gameObject.activeInHierarchy).ToArray();
                    Require(dockScreens.Length == 1, "Exactly one active native dock screen is required");
                    dockScreens[0].Leave();
                    return "native DockUI.Leave requested, including dock camera cleanup; API integration, not physical input";
                case "select-ability":
                    var selectedAbility = g.PlayerAbilities.GetAbilityDataByName((string)c["ability"]);
                    Require(selectedAbility != null && g.PlayerAbilities.GetIsAbilityUnlocked(selectedAbility), "Ability must already be unlocked in this isolated save");
                    GameEvents.Instance.SelectPlayerAbility(selectedAbility);
                    return "native ability selection event; not a physical radial gesture";
                case "rest":
                    Require(g.Player.IsDocked && !g.IsPaused && !g.DialogueRunner.IsDialogueRunning &&
                        g.Input.GetActiveActionLayer() == ActionLayer.DOCKED && !g.Time.IsTimePassingForcefully(), "Rest requires the normal dock screen");
                    var boatActions = Object.FindObjectsOfType<BoatActionsDestinationUI>();
                    Require(boatActions.Length == 1 && boatActions[0].RestDestinationButton.gameObject.activeInHierarchy,
                        "Exactly one visible native Rest control is required");
                    var restAction = boatActions[0].RestDestinationButton.BasicButtonWrapper.OnClick;
                    Require(restAction != null, "Native Rest control has no action");
                    restAction();
                    return "native Rest control invoked; ordinary sleep/time progression, not a physical mouse click";
                case "fixture":
                    if (c["manifest"] != null)
                    {
                        if ((bool)c["manifest"]) g.PlayerAbilities.UnlockAbility("manifest");
                        else g.SaveData.unlockedAbilities.Remove("manifest");
                    }
                    if (c["visits"] is JObject visits)
                        foreach (var v in visits.Properties())
                        {
                            Require(IsMerchant(v.Name), "Unknown merchant fixture");
                            g.SaveData.SetBoolVariable("has-visited-dock-" + v.Name, (bool)v.Value);
                        }
                    if (c["time"] != null) { decimal t = (decimal)c["time"]; Require(t >= 0 && t < 100000, "Bad fixture time"); g.SaveData.Time = t; }
                    if (c["restoreSanity"] != null && (bool)c["restoreSanity"]) g.Player.Sanity.ChangeSanity(1f);
                    if (c["forbidSave"] != null) g.SaveData.ForbidSave = (bool)c["forbidSave"];
                    return "explicit synthetic progress fixture applied only to isolated QA save";
                case "fixture-position":
                    Require(!g.Player.IsDocked && !g.IsPaused && g.Input.GetActiveActionLayer() == ActionLayer.BASE, "Require free water state");
                    float x = (float)c["x"], z = (float)c["z"];
                    Require(Math.Abs(x) < 1500 && Math.Abs(z) < 1500, "Fixture position outside world");
                    g.Player.transform.position = new Vector3(x, -1f, z);
                    var body = g.Player.GetComponent<Rigidbody>(); if (body != null) body.velocity = Vector3.zero;
                    return "synthetic test starting position; this is not a feature teleport";
                case "native-manifest":
                    Require(!g.Player.IsDocked && !g.IsPaused, "Require free water state");
                    var dest = GameObject.FindGameObjectWithTag("TeleportDestination");
                    Require(dest != null && Vector3.Distance(dest.transform.position, g.Player.transform.position) < 300f, "QA does not trigger the distance achievement");
                    return g.PlayerAbilities.ActivateAbility(g.PlayerAbilities.GetAbilityDataByName("manifest"));
                case "anchor-place":
                    Require(g.EntitlementManager.GetHasEntitlement(Entitlement.DLC_1), "DLC licence required");
                    Require(!g.Player.IsDocked && !g.SaveData.GetHasTeleportAnchor(), "Existing movable anchor or docked state");
                    g.ItemLogicHandler.CreateTeleportAnchor(g.Player.transform.position, true);
                    return "native anchor creation fixture; no reward progression implied";
                case "anchors": return Anchors();
                case "landing-context": return LandingContext(g.Player);
                case "geometry-experiment": return GeometryExperiment();
                default: throw new InvalidOperationException("Unknown QA command: " + action);
            }
        }
        private object Call(string name, params object[] args) { return bootstrap.GetMethod(name).Invoke(null, args); }
        private object Snapshot(bool includeGeometry = true)
        {
            var g = GameManager.Instance;
            var p = g != null ? g.Player : null;
            var s = g != null ? g.SaveData : null;
            object diagnostics = null;
            try { if (includeGeometry && bootstrap != null) diagnostics = JToken.Parse((string)Call("GetDiagnostics")); } catch (Exception e) { diagnostics = e.Message; }
            return new
            {
                utc = DateTime.UtcNow.ToString("o"), frame = Time.frameCount,
                achievementWritesSuppressed,
                observedInputs = observedInputs.ToArray(),
                input = new { mousePosition = V(Input.mousePosition), cursorVisible = Cursor.visible, cursorLock = Cursor.lockState.ToString(),
                    nativeInputEnabled = g != null && g.Input != null && g.Input.enabled,
                    width = Screen.width, height = Screen.height, focused = Application.isFocused,
                    eventSystems = Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>(true).Select(e => new { e.enabled,
                        active = e.gameObject.activeInHierarchy, e.sendNavigationEvents,
                        selected = e.currentSelectedGameObject == null ? null : e.currentSelectedGameObject.name,
                        module = e.currentInputModule == null ? null : e.currentInputModule.GetType().Name }).ToArray() },
                identifier = Application.identifier, persistentDataPath = Application.persistentDataPath,
                appDataPath = Application.dataPath, timeScale = Time.timeScale,
                playing = g != null && g.IsPlaying, paused = g != null && g.IsPaused,
                sceneTransition = SceneLoader.BetweenScenes,
                actionLayer = g != null && g.Input != null ? g.Input.GetActiveActionLayer().ToString() : null,
                player = p == null ? null : new { position = V(p.transform.position), alive = p.IsAlive, docked = p.IsDocked, fishing = p.IsFishing, immune = p.IsImmuneModeEnabled,
                    movementAllowed = p.Controller.IsMovementAllowed, visible = p.BoatModelProxy.gameObject.activeSelf, sanity = p.Sanity.CurrentSanity,
                    currentDock = p.CurrentDock != null ? p.CurrentDock.Data.Id : null, damage = s.GetNumberOfDamagedSlots(),
                    oozeNear = g.OozePatchManager != null && g.OozePatchManager.isOozeNearToPlayer,
                    oozeFar = g.OozePatchManager != null && g.OozePatchManager.isOozeFarToPlayer,
                    oozeSpeedModifier = g.PlayerStats == null ? 1f : g.PlayerStats.OozeMovementSpeedModifier },
                nativeSave = s == null ? null : new { time = s.Time, s.lastSavedTime, s.version, s.dockId, s.dockSlotIndex, s.ForbidSave, s.LastSelectedAbility, unlockedAbilities = s.unlockedAbilities.ToArray(),
                    history = s.abilityHistory.ToDictionary(k => k.Key, v => v.Value), visited = s.boolVariables.Where(k => k.Key.StartsWith("has-visited-dock-")).ToDictionary(k => k.Key, v => v.Value),
                    questStates = s.questEntries.ToDictionary(k => k.Key, v => new { state = v.Value.state.ToString(), v.Value.activeStepId, v.Value.completedStepIds, v.Value.resolutionIndex }),
                    worldPhase = s.WorldPhase, tprWorldPhase = s.TPRWorldPhase, tirWorldPhase = s.TIRWorldPhase,
                    inventory = s.grids.Where(k => k.Key == GridKey.INVENTORY).SelectMany(k => k.Value.spatialItems).GroupBy(i => i.id).ToDictionary(k => k.Key, v => v.Count()),
                    hasAnchor = s.GetHasTeleportAnchor(), anchorPosition = V(s.GetTeleportAnchorPosition()) },
                ownsDlc1 = g != null && g.EntitlementManager != null && g.EntitlementManager.GetHasEntitlement(Entitlement.DLC_1),
                ownsDlc2 = g != null && g.EntitlementManager != null && g.EntitlementManager.GetHasEntitlement(Entitlement.DLC_2),
                manifest = ManifestContext(),
                dialogue = DialogueContext(),
                windows = g != null && g.UI != null ? g.UI.ShowingWindowTypes.Select(w => w.ToString()).ToArray() : null,
                teleportBegins, teleportCompletes, saveCompletions, anchors = Anchors(), diagnostics,
                voyage = Application.identifier == VoyageIdentifier || Application.identifier == PainterIdentifier ? VoyageProbe.Snapshot(bootstrap) : null
            };
        }
        private object ManifestContext()
        {
            var g = GameManager.Instance;
            if (g == null || g.PlayerAbilities == null || g.SaveData == null || g.UI == null || g.UI.AbilityBarUI == null) return null;
            var data = g.PlayerAbilities.GetAbilityDataByName("manifest");
            var ability = data == null ? null : g.PlayerAbilities.GetAbilityForData(data) as TeleportAbility;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var destination = ability == null ? null : typeof(TeleportAbility).GetField("teleportDestinationObject", flags).GetValue(ability) as GameObject;
            var hold = typeof(AbilityBarUI).GetField("currentAbilityActionHold", flags).GetValue(g.UI.AbilityBarUI) as DredgePlayerActionHold;
            return new { selected = g.UI.AbilityBarUI.CurrentAbilityData == data,
                castTime = data == null ? 0 : data.castTime, cooldown = data == null ? 0 : data.cooldown,
                sinceLastCast = data == null ? 0 : g.PlayerAbilities.GetTimeSinceLastCast(data),
                holdProgress = hold == null ? 0 : hold.currentHoldTime,
                holdEnabled = hold != null && hold.Enabled,
                actionPressed = g.Input != null && g.Input.Controls.DoAbility.IsPressed,
                destinationName = destination == null ? null : destination.name,
                destinationId = destination == null ? 0 : destination.GetInstanceID(),
                destinationPosition = destination == null ? null : V(destination.transform.position) };
        }
        private object Anchors()
        {
            return Object.FindObjectsOfType<TeleportAnchorPOI>().Select(a => new { id = a.GetInstanceID(), a.name, position = V(a.transform.position), a.CanBeRetrieved, a.PendingDelete, pair = a.PairedAnchor == null ? 0 : a.PairedAnchor.GetInstanceID() }).ToArray();
        }
        private object DialogueContext()
        {
            var runner = GameManager.Instance != null ? GameManager.Instance.DialogueRunner : null;
            var type = bootstrap == null ? null : bootstrap.Assembly.GetType("Jinke.DredgeMerchantTravel.TravelController");
            var controller = bootstrap == null ? null : bootstrap.GetField("controller", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var cachedField = type == null ? null : type.GetField("dialogue", BindingFlags.Instance | BindingFlags.NonPublic);
            return new { running = runner != null && runner.IsDialogueRunning,
                current = runner == null ? null : runner.CurrentNodeName,
                previous = runner == null ? null : runner.PreviousNodeName,
                didPlayAnyLines = runner != null && runner.DidPlayAnyLines,
                cached = controller == null || cachedField == null ? (bool?)null : (bool)cachedField.GetValue(controller) };
        }
        private static object LandingContext(Player player)
        {
            var hull = player.GetComponentsInChildren<Collider>(true);
            var layers = hull.Select(c => c.gameObject.layer).Distinct().ToArray();
            var docks = Object.FindObjectsOfType<DockPOI>(true).Where(d => d.dock != null && d.dock.Data != null && IsMerchant(d.dock.Data.Id)).ToArray();
            return new
            {
                hull = hull.Select(ColliderInfo).ToArray(),
                collisionMatrix = layers.Select(layer => new { layer, name = LayerMask.LayerToName(layer), entries = Enumerable.Range(0, 32).Select(i => new { layer = i, name = LayerMask.LayerToName(i), ignored = Physics.GetIgnoreLayerCollision(layer, i) }).ToArray() }).ToArray(),
                docks = docks.Select(d => new { id = d.dock.Data.Id, position = V(d.transform.position), slots = d.dockSlots.Select(t => V(t.position)).ToArray() }).ToArray(),
                nearby = Object.FindObjectsOfType<Collider>(true).Where(c => c.gameObject.scene.IsValid() &&
                    (c.name == "Plane" || c.name == "LargePlane" || docks.Any(d => (c.transform.position - d.transform.position).sqrMagnitude < 100f * 100f))).Select(ColliderInfo).ToArray()
            };
        }
        private static object ColliderInfo(Collider c)
        {
            return new { id = c.GetInstanceID(), c.name, type = c.GetType().Name, c.enabled, c.isTrigger,
                active = c.gameObject.activeInHierarchy, layer = c.gameObject.layer, layerName = LayerMask.LayerToName(c.gameObject.layer),
                position = V(c.transform.position), size = V(c.bounds.size), attachedRigidbody = c.attachedRigidbody == null ? null : c.attachedRigidbody.name,
                components = c.GetComponents<Component>().Select(x => x == null ? "missing" : x.GetType().Name).ToArray(),
                mesh = c is MeshCollider ? ((MeshCollider)c).sharedMesh == null ? null : ((MeshCollider)c).sharedMesh.name : null };
        }
        private static object GeometryExperiment()
        {
            // All synthetic geometry lives in an independent physics scene, without a Rigidbody.
            // It never enters the original world's physics simulation.
            if (!geometryScene.IsValid()) geometryScene = UnityEngine.SceneManagement.SceneManager.CreateScene("MerchantTravel-QA-Geometry",
                new UnityEngine.SceneManagement.CreateSceneParameters(UnityEngine.SceneManagement.LocalPhysicsMode.Physics3D));
            var scene = geometryScene;
            var a = new GameObject("QA-penetration-probe"); a.SetActive(false);
            var b = new GameObject("QA-penetration-target"); b.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(a, scene);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(b, scene);
            Mesh mesh = null;
            try
            {
                var box = a.AddComponent<BoxCollider>(); box.enabled = false; box.size = Vector3.one;
                var target = b.AddComponent<BoxCollider>(); target.enabled = false; target.size = Vector3.one * 2f;
                Vector3 direction; float distance;
                bool disabledBoxOverlap = Physics.ComputePenetration(box, new Vector3(1.1f,0,0), Quaternion.identity, target, Vector3.zero, Quaternion.identity, out direction, out distance);
                bool disabledBoxApart = Physics.ComputePenetration(box, new Vector3(4,0,0), Quaternion.identity, target, Vector3.zero, Quaternion.identity, out direction, out distance);
                mesh = new Mesh { name = "QA-outward-cube" };
                mesh.vertices = new[] { new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1) };
                mesh.triangles = new[] {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
                mesh.RecalculateBounds();
                var targetMesh = b.AddComponent<MeshCollider>(); targetMesh.enabled = false; targetMesh.sharedMesh = mesh;
                bool disabledMeshOverlap = Physics.ComputePenetration(box, new Vector3(1.1f,0,0), Quaternion.identity, targetMesh, Vector3.zero, Quaternion.identity, out direction, out distance);
                bool disabledMeshApart = Physics.ComputePenetration(box, new Vector3(4,0,0), Quaternion.identity, targetMesh, Vector3.zero, Quaternion.identity, out direction, out distance);
                targetMesh.enabled = true; // Parent remains inactive, as with culled island geometry.
                bool inactiveMeshOverlap = Physics.ComputePenetration(box, new Vector3(1.1f,0,0), Quaternion.identity, targetMesh, Vector3.zero, Quaternion.identity, out direction, out distance);
                bool inactiveMeshApart = Physics.ComputePenetration(box, new Vector3(4,0,0), Quaternion.identity, targetMesh, Vector3.zero, Quaternion.identity, out direction, out distance);
                box.enabled = true; target.enabled = true; a.SetActive(true); b.SetActive(true);
                bool activeBoxOverlap = Physics.ComputePenetration(box, new Vector3(1.1f,0,0), Quaternion.identity, target, Vector3.zero, Quaternion.identity, out direction, out distance);
                bool activeBoxApart = Physics.ComputePenetration(box, new Vector3(4,0,0), Quaternion.identity, target, Vector3.zero, Quaternion.identity, out direction, out distance);
                bool activeMeshOverlap = Physics.ComputePenetration(box, new Vector3(1.1f,0,0), Quaternion.identity, targetMesh, Vector3.zero, Quaternion.identity, out direction, out distance);
                bool activeMeshApart = Physics.ComputePenetration(box, new Vector3(4,0,0), Quaternion.identity, targetMesh, Vector3.zero, Quaternion.identity, out direction, out distance);
                targetMesh.enabled = false;
                bool disabledTargetOverlap = Physics.ComputePenetration(box, new Vector3(1.1f,0,0), Quaternion.identity, targetMesh, Vector3.zero, Quaternion.identity, out direction, out distance);
                return new { disabledBoxOverlap, disabledBoxApart, disabledMeshOverlap, disabledMeshApart, inactiveMeshOverlap, inactiveMeshApart,
                    activeBoxOverlap, activeBoxApart, activeMeshOverlap, activeMeshApart, disabledTargetOverlap,
                    privateScene = scene.name, noRigidbodies = a.GetComponent<Rigidbody>() == null && b.GetComponent<Rigidbody>() == null };
            }
            // DREDGE resets its input listeners on any sceneUnloaded event, so keep the empty
            // isolated scene for this process and destroy only our own temporary geometry.
            finally { Object.Destroy(a); Object.Destroy(b); if (mesh != null) Object.Destroy(mesh); }
        }
        private void OnBegin() { teleportBegins++; Observe("event-begin-" + teleportBegins); }
        private void OnComplete() { teleportCompletes++; Observe("event-complete-" + teleportCompletes); }
        private void OnSaveComplete() { saveCompletions++; Observe("save-complete-" + saveCompletions); }
        private void Observe(string id)
        {
            try { Write(id, Snapshot(false)); }
            catch (Exception ex)
            {
                // An evidence failure must never interrupt an original game's multicast event.
                try { Write("observation-error-" + Time.frameCount, new { error = ex.ToString() }); }
                catch (Exception) { }
            }
        }
        private void OnDestroy()
        {
            if (events != null) { events.OnTeleportBegin -= OnBegin; events.OnTeleportComplete -= OnComplete; }
            if (observedSaveManager != null) observedSaveManager.OnSaveComplete -= OnSaveComplete;
        }
        private void Write(string id, object value)
        {
            Require(outputValidated && !string.IsNullOrEmpty(id) && id.Length <= 80 && id.All(c => char.IsLetterOrDigit(c) || c == '-'), "Invalid evidence id");
            string path = Path.GetFullPath(Path.Combine(output, id + ".json"));
            Require(Path.GetDirectoryName(path) == output, "Evidence path escapes QA output");
            NoLinks(path);
            using (var file = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)))
                file.Write(JsonConvert.SerializeObject(value, json));
        }
        private static float[] V(Vector3 p) { return new[] { p.x, p.y, p.z }; }
        private static bool IsMerchant(string id) { return new[] { "dock.pontoon-gc", "dock.pontoon-sb", "dock.pontoon-ts", "dock.pontoon-ds", "dock.pontoon-tpr", "dock.the-iron-rig" }.Contains(id); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void NoLinks(string path)
        {
            for (string p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
                if (Directory.Exists(p) || File.Exists(p)) Require((File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0, "Symlink not allowed");
        }
    }
}
