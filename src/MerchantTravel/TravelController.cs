using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Jinke.DredgeMerchantTravel
{
    internal sealed class TravelController : MonoBehaviour
    {
        private readonly SaveSession session = new SaveSession();
        private readonly LandingPlanner planner = new LandingPlanner();
        private readonly InputLease lease = new InputLease();
        private readonly NativeManifest manifest = new NativeManifest();
        private TravelRadial view;
        private Player player, castPlayer;
        private AbilityBarUI castOwner;
        private AbilityData castData;
        private SaveData castSave;
        private GameEvents events;
        private bool nativeTeleport, ownTeleport, finale, popup, cutscene, open, releasing;
        private int releaseFrame = -1, openedFrame;
        private bool openedByNativeHold;
        private string lastError, lastSelectedTarget, notice;
        private LandingCandidate lastSelectedLanding;
        private float[] actualArrival;
        private float teleportElapsed;
        private bool timeoutReported;
        private string teleportTimeoutError;
        private int teleportBeginSequence, nativeCommitCount, completedTravelCount;
        private List<TargetSnapshot> targets = new List<TargetSnapshot>();
        internal int Selection { get; private set; } = -1;
        internal bool Open { get { return open; } }
        internal List<TargetSnapshot> Targets { get { return targets; } }
        internal string Notice { get { return notice; } }
        internal double Remaining { get { return NativeManifest.Remaining; } }

        private void Awake() { view = new TravelRadial(this, transform); TravelLog.Info("RUNTIME_READY version=" + typeof(TravelController).Assembly.GetName().Version + " native-manifest-radial"); }

        // true is allowed only for other abilities (Bootstrap) or the exact one-shot original commit.
        internal bool BeforeAbilityComplete(AbilityBarUI owner)
        {
            if (manifest.Committing) return manifest.Consume(owner);
            try
            {
                Synchronize();
                if (open || releasing || ownTeleport) return false;
                TryOpenFromNative(owner);
            }
            catch (Exception exception) { Fail(exception); }
            return false;
        }

        private void Update()
        {
            try
            {
                Synchronize();
                if (player == null) return;
                if (ownTeleport)
                {
                    if (Application.isFocused && !GameManager.Instance.IsPaused && Time.timeScale > 0) teleportElapsed += Time.deltaTime;
                    if (!timeoutReported && teleportElapsed > 12)
                    {
                        timeoutReported = true;
                        teleportTimeoutError = "原版显形尚未完成；扩展保持阻塞，避免重复传送";
                        TravelLog.Error("NATIVE_TELEPORT_TIMEOUT");
                    }
                    return;
                }
                if (open)
                {
                    lease.MaintainCursor();
                    string blocker = FirstBlocker();
                    if (blocker != null || !SameCastContext()) { CloseImmediate(); return; }
                    if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
                    Selection = view.UpdateSelection(Selection);
                    view.Refresh();
                    bool confirm = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
                    bool released = openedByNativeHold && Time.frameCount > openedFrame && !GameManager.Instance.Input.Controls.DoAbility.IsPressed;
                    if (confirm || released)
                    {
                        if (Selection < 0) Cancel();
                        else
                        {
                            string result = RequestTravel(targets[Selection].id);
                            if (open && released) { TravelLog.Info("RELEASE_REJECTED " + result); Cancel(); }
                        }
                    }
                }
                else if (releasing)
                {
                    if (InputLease.ClosingControlHeld) releaseFrame = -1;
                    else if (releaseFrame < 0) releaseFrame = Time.frameCount + 1;
                    else if (Time.frameCount >= releaseFrame) { lease.Dispose(); releasing = false; }
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private void Synchronize()
        {
            GameManager game = GameManager.Instance;
            Player current = game != null && game.IsPlaying ? game.Player : null;
            if (current == null || game.SaveData == null)
            {
                if (session.Data != null || lease.IsHeld || !ReferenceEquals(player, null)) ResetSession();
                return;
            }
            if (player != current || !session.Matches(game.SaveManager, game.SaveData))
            {
                ResetSession(); player = current; session.Bind(game.SaveManager, game.SaveData);
            }
            if (events != GameEvents.Instance)
            {
                Unsubscribe(); events = GameEvents.Instance;
                if (events == null) return;
                events.OnTeleportBegin += OnTeleportBegin;
                events.OnTeleportComplete += OnTeleportComplete;
                events.OnFinaleVoyageStarted += OnFinale;
                events.OnFinaleCutsceneStarted += OnFinale;
                events.OnDialogueStarted += OnDialogueStarted;
                events.OnPopupWindowToggled += OnPopup;
                events.OnCutsceneToggled += OnCutscene;
            }
        }

        private GateState State()
        { return NativeState.Read(player, lease, nativeTeleport, ownTeleport, finale, popup, cutscene, session.Error != null || lastError != null); }
        private string FirstBlocker()
        { List<string> reasons = TravelRules.Blockers(State()); return reasons.Count == 0 ? null : reasons[0]; }
        private bool SameCastContext()
        {
            GameManager game = GameManager.Instance;
            return game != null && player == castPlayer && game.Player == castPlayer && ReferenceEquals(game.SaveData, castSave) &&
                session.Matches(game.SaveManager, castSave) && NativeManifest.IsCurrent(castOwner) && castOwner.CurrentAbilityData == castData;
        }

        internal string RequestOpen()
        {
            try
            {
                Synchronize();
                if (open) return "已打开";
                GameManager game = GameManager.Instance;
                return OpenCore(game != null && game.UI != null ? game.UI.AbilityBarUI : null, false);
            }
            catch (Exception exception) { Fail(exception); return "显形轮盘暂不可用"; }
        }

        private string TryOpenFromNative(AbilityBarUI owner)
        {
            if (open || releasing || ownTeleport) return Reject("等待当前操作结束");
            if (!NativeManifest.IsCurrent(owner)) return Reject("先选择原版「显形」能力");
            DredgePlayerActionHold hold = NativeManifest.Hold(owner);
            if (hold == null || !hold.Enabled || hold.currentHoldTime < owner.CurrentAbilityData.castTime ||
                !ReferenceEquals(hold.playerAction, GameManager.Instance.Input.Controls.DoAbility) || !hold.playerAction.IsPressed)
                return Reject("长按当前能力按键，完成显形蓄力后选择目的地");
            // Only this exact completed manifest hold is consumed. Other charging abilities still block.
            hold.Reset();
            return OpenCore(owner, true);
        }

        private string OpenCore(AbilityBarUI owner, bool fromNativeHold)
        {
            if (open || releasing || ownTeleport || manifest.Committing) return Reject("等待当前操作结束");
            if (!NativeManifest.IsCurrent(owner)) return Reject("先选择原版「显形」能力");
            string reason = FirstBlocker();
            if (reason != null) return Reject(reason);
            if (!GameManager.Instance.SaveData.unlockedAbilities.Contains("manifest")) return Reject("先在原版剧情中获得「显形」能力");
            if (!NativeManifest.VerifyCast(owner)) return Reject("显形尚未恢复或当前无法施放");
            castOwner = owner; castData = owner.CurrentAbilityData; castPlayer = player; castSave = GameManager.Instance.SaveData;
            targets = InspectTargets();
            Selection = -1; notice = null;
            lease.Acquire(player, transform);
            open = true; openedByNativeHold = fromNativeHold; openedFrame = Time.frameCount;
            view.Show();
            TravelLog.Info("RADIAL_OPEN nativeHold=" + fromNativeHold + " targets=" + targets.Count);
            return "已打开";
        }

        private List<TargetSnapshot> InspectTargets()
        {
            List<TargetSnapshot> result = planner.Inspect(player);
            Vector3 original = NativeManifest.OriginalDestination(NativeManifest.Data);
            result.Insert(0, new TargetSnapshot { id = RadialRules.BlackstoneId, name = "黑石岛", region = "马罗群岛",
                description = "回到收藏家的岛屿", visited = true, hasDlc = true, eligible = true, dockActive = true,
                nativeDestination = true, selected = new LandingCandidate { id = "native-blackstone", x = original.x,
                    z = original.z, safe = true } });
            return result;
        }

        internal string RequestTravel(string destinationId)
        {
            try
            {
                if (!open || ownTeleport || releasing || manifest.Committing) return Reject("先长按显形，打开目的地轮盘");
                if (destinationId != RadialRules.BlackstoneId && Destination.Find(destinationId) == null) return Reject("未知的目的地");
                string reason = FirstBlocker();
                if (reason != null) return Reject(reason);
                if (!SameCastContext()) return Reject("当前能力或存档已变化，请重新蓄力");
                if (Remaining > .00001 || !NativeManifest.VerifyCast(castOwner)) return Reject("显形尚在恢复中");
                targets = InspectTargets();
                TargetSnapshot target = targets.Find(value => value.id == destinationId);
                if (target == null || target.unavailable != null || target.selected == null || !target.selected.safe)
                    return Reject(target == null ? "未知的目的地" : target.unavailable ?? "附近暂时没有安全落点");
                CommitTravel(target);
                return "显形中";
            }
            catch (Exception exception) { Fail(exception); return "显形提交失败，请查看独立运行日志"; }
        }

        private void CommitTravel(TargetSnapshot target)
        {
            AbilityBarUI owner = castOwner;
            AbilityData ability = castData;
            int sequenceBefore = teleportBeginSequence;
            float priorHistory;
            bool hadHistory = GameManager.Instance.SaveData.abilityHistory.TryGetValue("manifest", out priorHistory);
            // Restore the world before original WaitForSeconds and VerifyAbilityCast run.
            open = false; view.Hide(); lease.Dispose();
            if (!SameCastContext() || FirstBlocker() != null)
                throw new InvalidOperationException("Native sailing context changed while restoring radial input.");
            lastSelectedTarget = target.id; lastSelectedLanding = target.selected; actualArrival = null;
            timeoutReported = false; ownTeleport = true; teleportElapsed = 0; teleportTimeoutError = null;
            if (!target.nativeDestination)
            {
                Rigidbody body = NativeState.Rigidbody(player);
                if (body == null) throw new InvalidOperationException("Player rigidbody unavailable.");
                body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            }
            nativeCommitCount++;
            TravelLog.Info("TRAVEL_SELECTED " + JsonConvert.SerializeObject(new { target = target.id, candidate = target.selected,
                cooldown = "native-manifest", hadHistory, priorHistory, nativeCommitCount }));
            manifest.Commit(owner, ability, target.nativeDestination ? (Vector3?)null : target.selected.Position);
            if (teleportBeginSequence != sequenceBefore + 1) throw new InvalidOperationException("Native manifest did not begin exactly one teleport.");
            float history;
            if (!GameManager.Instance.SaveData.abilityHistory.TryGetValue("manifest", out history))
                throw new InvalidOperationException("Native manifest did not record its cooldown.");
            TravelLog.Info("NATIVE_COMMIT_RETURN " + JsonConvert.SerializeObject(new { target = target.id, history,
                destinationRestored = manifest.DestinationRestored, originalDestinationId = manifest.OriginalDestinationId,
                temporaryDestinationId = manifest.TemporaryDestinationId, nativeCommitCount }));
        }

        internal string Cancel()
        {
            if (ownTeleport) return "原版显形已经开始，等待抵达";
            if (!open) return "没有待取消的传送";
            open = false; Selection = -1; view.Hide(); lease.ResumeWorld();
            releasing = lease.IsHeld; releaseFrame = -1;
            TravelLog.Info("RADIAL_CANCEL cost=none");
            return "已取消";
        }
        private string Reject(string reason) { notice = reason; TravelLog.Info("REQUEST_BLOCKED reason=" + reason); return reason; }

        private void OnTeleportBegin()
        {
            teleportBeginSequence++; nativeTeleport = true;
            if (!ownTeleport && open) CloseImmediate();
        }
        private void OnTeleportComplete()
        {
            nativeTeleport = false;
            if (!ownTeleport) return;
            try
            {
                timeoutReported = false; teleportTimeoutError = null; teleportElapsed = 0; ownTeleport = false;
                completedTravelCount++;
                Vector3 position = player.transform.position;
                actualArrival = new[] { position.x, position.y, position.z };
                float distance = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(lastSelectedLanding.x, lastSelectedLanding.z));
                if (distance > 2f) { lastError = "抵达位置与已检查的水面不一致"; TravelLog.Error("ARRIVAL_MISMATCH distance=" + distance); }
                else if (lastSelectedTarget != RadialRules.BlackstoneId)
                {
                    Rigidbody body = NativeState.Rigidbody(player);
                    if (body == null) throw new InvalidOperationException("Player rigidbody unavailable.");
                    body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                    body.rotation = Quaternion.Euler(0, lastSelectedLanding.heading, 0); player.transform.rotation = body.rotation;
                }
                TravelLog.Info("TRAVEL_ARRIVED " + JsonConvert.SerializeObject(new { target = lastSelectedTarget,
                    expected = lastSelectedLanding, actual = actualArrival, distance, completedTravelCount }));
            }
            catch (Exception exception) { Fail(exception); }
        }
        private void OnFinale() { finale = true; if (open || releasing) CloseImmediate(); }
        private void OnDialogueStarted() { if (open || releasing) CloseImmediate(); }
        private void OnPopup(bool value) { popup = value; if (value && open) CloseImmediate(); }
        private void OnCutscene(bool value) { cutscene = value; if (value && open) CloseImmediate(); }

        internal string Diagnostics()
        {
            GateState state = State();
            List<TargetSnapshot> snapshot = state.Ready ? InspectTargets() : new List<TargetSnapshot>();
            Vector3 position = player != null ? player.transform.position : Vector3.zero;
            return JsonConvert.SerializeObject(new { schemaVersion = 2, ready = state.Ready, open, charging = false, ownTeleport, nativeTeleport,
                selection = Selection, centerDeadzone = Selection < 0, openedByNativeHold, committing = manifest.Committing,
                destinationRestored = manifest.DestinationRestored, originalDestinationId = manifest.OriginalDestinationId,
                temporaryDestinationId = manifest.TemporaryDestinationId,
                state, blockers = TravelRules.Blockers(state), targets = snapshot, savePath = session.SavePath, saveHash = session.FileHash,
                cooldownSource = "abilityHistory.manifest", remainingGameDays = state.Ready ? Remaining : 0,
                error = lastError ?? teleportTimeoutError ?? session.Error, selectedTarget = lastSelectedTarget,
                selectedLanding = lastSelectedLanding, actualArrival, nativeCommitCount, completedTravelCount, teleportBeginSequence,
                visual = view != null ? view.Diagnostics() : null,
                playerPosition = new[] { position.x, position.y, position.z }, profile = Application.persistentDataPath }, Formatting.Indented);
        }

        private void Fail(Exception exception)
        {
            lastError = exception.Message;
            // If original Teleport has started (or partially failed), leave its state to the original coroutine.
            // Never retry, refund history, reset immunity, or synthesize a completion event.
            try { CloseImmediate(); } catch (Exception cleanup) { TravelLog.Error("CLEANUP_FAILED " + cleanup.Message); }
            TravelLog.Error("RUNTIME_FAILED " + exception);
        }
        private void CloseImmediate() { open = releasing = false; Selection = -1; if (view != null) view.Hide(); lease.Dispose(); }
        private void ResetSession()
        {
            CloseImmediate(); Unsubscribe(); session.Dispose();
            if (view != null) view.Reset();
            player = castPlayer = null; castOwner = null; castData = null; castSave = null;
            nativeTeleport = ownTeleport = finale = popup = cutscene = openedByNativeHold = false;
            lastError = teleportTimeoutError = null; teleportElapsed = 0; timeoutReported = false;
            targets.Clear();
        }
        private void Unsubscribe()
        {
            if (events == null) return;
            events.OnTeleportBegin -= OnTeleportBegin; events.OnTeleportComplete -= OnTeleportComplete;
            events.OnFinaleVoyageStarted -= OnFinale; events.OnFinaleCutsceneStarted -= OnFinale;
            events.OnDialogueStarted -= OnDialogueStarted; events.OnPopupWindowToggled -= OnPopup;
            events.OnCutsceneToggled -= OnCutscene; events = null;
        }
        private void OnApplicationFocus(bool focused) { if (!focused && (open || releasing)) CloseImmediate(); }
        private void OnDestroy() { ResetSession(); if (view != null) view.Dispose(); }
    }
}
