using System;
using Newtonsoft.Json;
using UnityEngine;

namespace Jinke.DredgeVoyage
{
    internal sealed class VoyageRuntime : MonoBehaviour
    {
        private static VoyageRuntime instance;
        private readonly VoyageStore store = new VoyageStore();
        private readonly FishingTuning fishing = new FishingTuning();
        private bool enteredWorld;
        private float nextCheck;
        private string lastError;
        internal static bool Ready { get { return instance != null && instance.store.Ready && GameManager.Instance != null && GameManager.Instance.IsPlaying; } }
        internal static VoyageSettings Current { get { return instance == null || instance.store.Settings == null ? new VoyageSettings() : instance.store.Settings.Copy(); } }

        internal static void OnSaveReady(object owner)
        {
            SaveData save = owner as SaveData;
            if (save == null || GameManager.Instance == null || !ReferenceEquals(save, GameManager.Instance.SaveData)) return;
            if (instance == null)
            {
                var root = new GameObject("Jinke-VoyageExperience");
                root.hideFlags = HideFlags.DontSave;
                DontDestroyOnLoad(root);
                instance = root.AddComponent<VoyageRuntime>();
            }
            instance.Bind(save);
        }

        private void Bind(SaveData save)
        {
            if (store.Ready && ReferenceEquals(store.Data, save)) return;
            ResetSession();
            store.Bind(GameManager.Instance.SaveManager, save);
            Apply();
            Debug.Log("[Voyage] SAVE_READY defaults=" + store.Settings.IsDefault);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            try
            {
                if (Jinke.DredgeMerchantTravel.Bootstrap.PainterEnabled)
                {
                    Jinke.DredgePainter.PainterAppearance.Tick();
                }
                GameManager game = GameManager.Instance;
                if (game == null || !game.IsPlaying || game.Player == null)
                {
                    if (enteredWorld) ResetSession();
                    return;
                }
                enteredWorld = true;
                if (!store.Ready || !ReferenceEquals(store.Data, game.SaveData)) Bind(game.SaveData);
                if (!fishing.IsReady) Apply();
                VoyageSettingsUI.TryInstall();
                CargoRefitUI.TryInstall();
            }
            catch (Exception error)
            {
                if (lastError != error.ToString()) { lastError = error.ToString(); Debug.LogError("[Voyage] RUNTIME_FAILED " + error); }
            }
        }

        internal static bool Change(int row, int value, out string error)
        {
            VoyageSettings next = Current;
            next.Set(row, value);
            return Change(next, out error);
        }
        internal static bool RestorePreferences(out string error) { return Change(new VoyageSettings(), out error); }
        private static bool Change(VoyageSettings next, out string error)
        {
            error = null;
            if (!Ready) { error = "请进入航行后调整设置。"; return false; }
            VoyageSettings previous = Current;
            try
            {
                next.Validate();
                instance.fishing.Apply(next);
                TimeTuning.Apply(next);
                if (!instance.fishing.IsReady) throw new InvalidOperationException("渔具尚未准备好，请稍后再试。");
                instance.store.Change(next);
                Debug.Log("[Voyage] SETTINGS_APPLIED " + JsonConvert.SerializeObject(next));
                return true;
            }
            catch (Exception ex)
            {
                instance.fishing.Apply(previous); TimeTuning.Apply(previous);
                error = "设置未保存，已恢复上一档。";
                Debug.LogError("[Voyage] SETTINGS_REJECTED " + ex);
                return false;
            }
        }
        private void Apply() { fishing.Apply(store.Settings); TimeTuning.Apply(store.Settings); }
        private void ResetSession()
        {
            fishing.Restore(); TimeTuning.Restore(); store.Dispose(); enteredWorld = false;
        }
        private void OnDestroy() { ResetSession(); if (instance == this) instance = null; }
        internal static string Diagnostics()
        {
            return JsonConvert.SerializeObject(new { ready = Ready, settings = Current, fishingReady = instance != null && instance.fishing.IsReady,
                error = instance == null ? null : instance.lastError, cargo = CargoRefit.GetPreview() });
        }
    }
}
