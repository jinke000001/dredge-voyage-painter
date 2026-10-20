using System;
using System.Reflection;
using UnityEngine;

namespace Jinke.DredgeMerchantTravel
{
    internal static class NativeState
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo AutoRotate = Required(typeof(PlayerController), "hasAutoRotateTarget");
        private static readonly FieldInfo Docking = Required(typeof(DockPOIHandler), "isDockingInProgress");
        private static readonly FieldInfo Frozen = Required(typeof(TimeController), "_freezeTime");
        private static readonly FieldInfo CastingForbidden = Required(typeof(AbilityBarUI), "<IsCastingForbidden>k__BackingField");
        private static readonly FieldInfo Hold = Required(typeof(AbilityBarUI), "currentAbilityActionHold");
        private static readonly FieldInfo DelegateHold = Required(typeof(AbilityBarUI), "currentAbilityActionHoldDelegate");
        private static readonly FieldInfo Body = Required(typeof(PlayerController), "rb");

        private static FieldInfo Required(Type type, string name)
        { return type.GetField(name, PrivateInstance) ?? throw new MissingFieldException(type.FullName, name); }

        internal static Rigidbody Rigidbody(Player player) { return Body.GetValue(player.Controller) as Rigidbody; }

        internal static GateState Read(Player player, InputLease lease, bool nativeTeleport, bool ownTeleport, bool finale,
            bool popup, bool cutscene, bool storageError)
        {
            var state = new GateState { NativeTeleport = nativeTeleport, OwnTeleport = ownTeleport, Finale = finale,
                Popup = popup, Cutscene = cutscene, StorageError = storageError, Focused = Application.isFocused };
            GameManager game = GameManager.Instance;
            state.Ready = game != null && player != null && game.Player == player && game.SaveData != null &&
                game.UI != null && game.Time != null && game.Input != null && player.Controller != null &&
                player.Docker != null && player.PlayerTeleport != null && player.Sanity != null &&
                game.UI.AbilityBarUI != null && game.Input.Controls != null && game.DialogueRunner != null;
            if (!state.Ready) return state;
            bool owned = lease != null && lease.Owns(player);
            state.Playing = game.IsPlaying;
            state.Alive = player.IsAlive;
            state.Docked = player.IsDocked;
            state.AutoMoving = player.Controller.IsAutoMoving || (bool)AutoRotate.GetValue(player.Controller);
            state.Docking = (bool)Docking.GetValue(player.Docker);
            state.Fishing = player.IsFishing;
            state.Harvesting = game.UI.IsHarvesting;
            state.Paused = game.IsPaused;
            state.WindowOpen = game.UI.ShowingWindowTypes.Count > 0 || game.UI.IsShowingQuestGrid || game.UI.IsShowingRadialMenu;
            state.Cutscene |= game.UI.IsInCutscene || !game.CanPause;
            state.Dialogue = game.DialogueRunner.IsDialogueRunning;
            state.CastingForbidden = (bool)CastingForbidden.GetValue(game.UI.AbilityBarUI);
            state.ForbidSave = game.SaveData.ForbidSave;
            state.Immune = player.IsImmuneModeEnabled;
            state.ForcedTime = game.Time.IsTimePassingForcefully() || game.Time.IsPlayerPassingTimeManually;
            state.FrozenTime = (bool)Frozen.GetValue(game.Time);
            state.BaseActionLayer = game.Input.GetActiveActionLayer() == ActionLayer.BASE;
            state.MovementAllowed = player.Controller.IsMovementAllowed;
            state.InputEnabled = game.Input.enabled || (owned && lease.HadInput);
            state.Binding = game.Input.Controls.IsListeningForBinding;
            var hold = Hold.GetValue(game.UI.AbilityBarUI) as DredgePlayerActionHold;
            var delegated = DelegateHold.GetValue(game.UI.AbilityBarUI) as DredgePlayerActionHoldDelegate;
            state.AbilityCharging = (hold != null && hold.currentHoldTime > 0) || (delegated != null && delegated.currentHoldTime > 0);
            state.NormalTimeScale = Mathf.Abs(Time.timeScale - 1f) < .001f ||
                (owned && lease.SlowsWorld && Mathf.Abs(Time.timeScale - InputLease.RadialTimeScale) < .001f);
            return state;
        }
    }
}
