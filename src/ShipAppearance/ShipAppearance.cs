using System;
using UnityEngine;
using Jinke.DredgeMerchantTravel;

namespace Jinke.DredgeShipAppearance
{
    internal static class ShipAppearance
    {
        internal const string Key = "jinke-ship-appearance-v1";
        internal const string Original = "original";
        internal const string Faceted = "faceted-trawler-v1";
        internal const string Cthulhu = "cthulhu-trawler-v1";
        internal const string Cyberpunk = "cyberpunk-trawler-v1";
        private static SaveData data;
        private static Player player;
        private static GameEvents events;
        private static BoatModelProxy attempted;
        private static Boat5Binding binding;
        private static int boundThreshold;
        private static bool enteredWorld;
        private static string lastError;

        internal static bool IsBoat5
        {
            get
            {
                GameManager game = GameManager.Instance;
                if (!Bootstrap.PainterEnabled || game == null || !game.IsPlaying || game.SaveData == null ||
                    game.SaveData.HullTier != 5 || game.Player == null || game.Player.BoatModelProxy == null) return false;
                Transform boat = game.Player.transform.Find("Boat5");
                return boat != null && boat.gameObject.activeInHierarchy && boat.GetComponent<BoatModelProxy>() == game.Player.BoatModelProxy;
            }
        }
        internal static bool Ready { get { return IsCurrent() && IsBoat5 && binding != null && binding.Matches(player.BoatModelProxy); } }
        internal static string SelectedId { get { return Ready ? binding.ActiveId : Original; } }
        internal static string Status { get { return lastError; } }
        internal static bool HasUnknownSelection
        {
            get { return !Known(Read()); }
        }
        private static bool Known(string id) { return id == Original || id == Faceted || id == Cthulhu || id == Cyberpunk; }

        internal static void OnSaveReady(SaveData save)
        {
            GameManager game = GameManager.Instance;
            if (!Bootstrap.PainterEnabled || game == null || save == null || !ReferenceEquals(save, game.SaveData)) return;
            try
            {
                if (!ReferenceEquals(data, save))
                {
                    ResetSession();
                    data = save;
                }
                Subscribe();
                Reconcile(false);
            }
            catch (Exception error) { Fail(error); }
        }

        internal static void Tick()
        {
            try
            {
                if (!Bootstrap.PainterEnabled) { ResetSession(); return; }
                GameManager game = GameManager.Instance;
                if (game == null || !game.IsPlaying || game.Player == null)
                {
                    if (enteredWorld) ResetSession();
                    return;
                }
                if (!ReferenceEquals(data, game.SaveData)) OnSaveReady(game.SaveData);
                Subscribe();
                Reconcile(false);
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Subscribe()
        {
            if (events == GameEvents.Instance) return;
            Unsubscribe();
            events = GameEvents.Instance;
            if (events != null) events.OnBoatModelChanged += OnBoatModelChanged;
        }
        private static void OnBoatModelChanged()
        {
            try { Reconcile(true); }
            catch (Exception error) { Fail(error); }
        }

        private static void Reconcile(bool syncDamage)
        {
            GameManager game = GameManager.Instance;
            if (!IsCurrent() || !game.IsPlaying || game.Player == null || game.PlayerStats == null) return;
            enteredWorld = true;
            if (player != game.Player)
            {
                ReleaseBinding();
                player = game.Player; attempted = null; lastError = null;
            }
            if (!IsBoat5)
            {
                ReleaseBinding(); attempted = null;
                return;
            }
            BoatModelProxy model = player.BoatModelProxy;
            if (binding != null && !binding.Matches(model)) { ReleaseBinding(); attempted = null; }
            boundThreshold = game.PlayerStats.DamageThreshold;
            if (binding == null && attempted != model)
            {
                attempted = model;
                binding = new Boat5Binding(model);
                lastError = null;
            }
            if (binding == null) return;
            int damage = data.GetNumberOfDamagedSlots(), threshold = game.PlayerStats.DamageThreshold;
            string selected = Read();
            if (Known(selected) && selected != Original)
            {
                // Native SetActive/OnEnable selects a mesh BEFORE the model
                // event. Reapply current damage immediately after that event.
                if (binding.ActiveId != selected || syncDamage) binding.Apply(selected, damage, threshold);
            }
            else binding.Restore(damage, threshold); // Unknown IDs stay stored.
        }

        internal static bool Change(string id, out string error)
        {
            error = null;
            if (!Known(id)) { error = "无法识别这款船型外观。"; return false; }
            Tick();
            GameManager game = GameManager.Instance;
            if (!IsCurrent() || !IsBoat5 || player == null || !player.IsAlive || !player.IsDocked || game.SaveManager == null ||
                !ReferenceEquals(data, game.SaveManager.ActiveSaveData) || data.ForbidSave || data.stringVariables == null)
            { error = "请停泊后，在造船坞为五级船更换外观。"; return false; }
            SaveData save = data;
            Player owner = player;
            string previous = SelectedId;
            try
            {
                if (id != Original)
                {
                    // An explicit choice must not first retry an unavailable
                    // old saved ID; it may be the user's recovery selection.
                    if (binding == null)
                    {
                        attempted = player.BoatModelProxy;
                        boundThreshold = game.PlayerStats.DamageThreshold;
                        binding = new Boat5Binding(attempted);
                    }
                    if (!Ready) throw new InvalidOperationException("Boat5 resources are unavailable.");
                    binding.Apply(id, save.GetNumberOfDamagedSlots(), game.PlayerStats.DamageThreshold);
                }
                else if (binding != null) binding.Restore(save.GetNumberOfDamagedSlots(), game.PlayerStats.DamageThreshold);
                if (!IsCurrent() || !IsBoat5 || player != owner || !ReferenceEquals(data, save))
                    throw new InvalidOperationException("Appearance session changed before the selection was stored.");
                // Only plain IDs enter the native dictionary, after successful
                // visual application. The original save timing is unchanged.
                if (id == Original) save.stringVariables.Remove(Key);
                else save.SetStringVariable(Key, id);
                lastError = null;
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    if (binding != null)
                    {
                        if (previous != Original) binding.Apply(previous, save.GetNumberOfDamagedSlots(), game.PlayerStats.DamageThreshold);
                        else binding.Restore(save.GetNumberOfDamagedSlots(), game.PlayerStats.DamageThreshold);
                    }
                }
                catch (Exception rollback) { Debug.LogError("[ShipAppearance] ROLLBACK_FAILED " + rollback); }
                lastError = exception.Message;
                error = "船型外观暂时不可用，未保存本次选择。";
                Debug.LogWarning("[ShipAppearance] CHOICE_REJECTED " + exception.Message);
                return false;
            }
        }

        private static string Read()
        {
            if (data == null || data.stringVariables == null) return Original;
            return data.GetStringVariable(Key, Original);
        }
        private static bool IsCurrent()
        {
            GameManager game = GameManager.Instance;
            return Bootstrap.PainterEnabled && game != null && data != null && ReferenceEquals(data, game.SaveData);
        }
        private static void ReleaseBinding()
        {
            if (binding == null) return;
            binding.Release(data == null ? 0 : data.GetNumberOfDamagedSlots(), boundThreshold);
            binding = null;
        }
        private static void Fail(Exception error)
        {
            if (lastError != error.Message) Debug.LogWarning("[ShipAppearance] ORIGINAL_FALLBACK " + error.Message);
            lastError = error.Message;
            try { ReleaseBinding(); }
            catch (Exception restore) { Debug.LogError("[ShipAppearance] RESTORE_FAILED " + restore.Message); }
        }
        private static void Unsubscribe()
        {
            if (events != null) events.OnBoatModelChanged -= OnBoatModelChanged;
            events = null;
        }
        internal static void ResetSession()
        {
            Unsubscribe();
            ReleaseBinding();
            data = null; player = null; attempted = null;
            boundThreshold = 0; enteredWorld = false; lastError = null;
        }
    }
}
