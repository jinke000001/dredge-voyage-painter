using System;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace Jinke.DredgeMerchantTravel
{
    public static class Bootstrap
    {
        private static TravelController controller;
        private static bool isolated;
        private static string lastError;
        private static bool startupEntryWritten, startupSuccessWritten;
        internal static bool PainterEnabled { get { return isolated && Application.identifier == ProfileContract.PainterIdentifier; } }

        // The diagnostic catch always rethrows: mismatch must stop Steam and SaveManager initialization.
        public static void EnsureIsolatedProfile()
        {
            if (!startupEntryWritten)
            {
                startupEntryWritten = true;
                StartupDiagnostics.TryWrite("guard-entered", null);
            }
            try
            {
                string resources = ProfileContract.Verify(Application.identifier, Application.companyName, Application.productName,
                    Application.dataPath, Application.persistentDataPath, Assembly.GetExecutingAssembly().Location,
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.CurrentDirectory,
                    Environment.GetEnvironmentVariable("JINKE_DREDGE_MERCHANT_TRAVEL_PROOF"),
                    Environment.GetEnvironmentVariable("JINKE_DREDGE_MERCHANT_TRAVEL_PROFILE"));
                // Unity can change cwd to the app's parent while starting. Only the fully verified app is normalized.
                Environment.CurrentDirectory = resources;
                ProfileContract.RequireEqual(ProfileContract.FullPath(Environment.CurrentDirectory), resources, "normalized Steam working directory");
                isolated = true;
                if (!startupSuccessWritten)
                {
                    startupSuccessWritten = true;
                    StartupDiagnostics.TryWrite("guard-verified", null);
                }
                Debug.Log("[MerchantTravel] ISOLATION_VERIFIED " + Application.persistentDataPath);
            }
            catch (Exception exception)
            {
                isolated = false;
                StartupDiagnostics.TryWrite("guard-failed", exception);
                throw;
            }
        }

        public static void OnPlayer(object owner)
        {
            try
            {
                if (!isolated) throw new InvalidOperationException("Isolation guard has not completed.");
                if (!(owner is Component)) return;
                if (controller == null)
                {
                    var root = new GameObject("Jinke-MerchantTravel");
                    root.hideFlags = HideFlags.DontSave;
                    UnityEngine.Object.DontDestroyOnLoad(root);
                    controller = root.AddComponent<TravelController>();
                }
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                if (isolated) TravelLog.Error("INIT_FAILED " + exception);
                Debug.LogError("[MerchantTravel] INIT_FAILED " + exception);
            }
            if (!PainterEnabled) return;
            try { Jinke.DredgePainter.PainterAppearance.OnColors(owner); }
            catch (Exception exception) { Debug.LogError("[Painter] COLOR_REFRESH_FAILED " + exception); }
        }

        public static void OnPainterFlag(object owner)
        {
            if (!PainterEnabled) return;
            try { Jinke.DredgePainter.PainterAppearance.OnFlag(owner); }
            catch (Exception exception) { Debug.LogError("[Painter] FLAG_REFRESH_FAILED " + exception); }
        }

        public static void OnPainterRunnerAwake(object owner)
        {
            if (!PainterEnabled) return;
            try { Jinke.DredgePainter.PainterDialogue.OnRunnerAwake(owner); }
            catch (Exception exception) { Debug.LogError("[Painter] DIALOGUE_INSTALL_FAILED " + exception); }
        }

        public static void BeforePainterDialogue(object owner)
        {
            if (!PainterEnabled) return;
            try { Jinke.DredgePainter.PainterDialogue.BeforeDialogue(owner); }
            catch (Exception exception) { Debug.LogError("[Painter] LOCALIZATION_FAILED " + exception); }
        }

        // Only this exact native callback is intercepted. Other selected abilities retain their original path.
        public static bool BeforeAbilityComplete(object owner)
        {
            var bar = owner as AbilityBarUI;
            if (bar == null || !NativeManifest.IsManifest(bar.CurrentAbilityData)) return true;
            try
            {
                if (!isolated || controller == null) return false;
                return controller.BeforeAbilityComplete(bar);
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                if (isolated) TravelLog.Error("MANIFEST_ENTRY_FAILED " + exception);
                return false;
            }
        }

        // Normal gated operations only. RequestOpen is the keyboard-accessible preview path; no costs are bypassed.
        public static string RequestOpen() { return controller == null ? "航行场景尚未准备好" : controller.RequestOpen(); }
        public static string RequestTravel(string destinationId) { return controller == null ? "航行场景尚未准备好" : controller.RequestTravel(destinationId); }
        public static string Cancel() { return controller == null ? "航行场景尚未准备好" : controller.Cancel(); }
        public static string GetDiagnostics()
        {
            try { return controller == null ? JsonConvert.SerializeObject(new { ready = false, isolated, error = lastError }) : controller.Diagnostics(); }
            catch (Exception exception) { return JsonConvert.SerializeObject(new { ready = false, error = exception.Message }); }
        }

        // Invoked only by the separate, fingerprinted voyage patch profile.
        public static void AfterSaveDataInit(object owner)
        {
            if (!isolated) throw new InvalidOperationException("Voyage isolation guard has not completed.");
            Jinke.DredgeVoyage.CargoRefit.SessionAllowed = () => isolated;
            try
            {
                Jinke.DredgeVoyage.VoyageRuntime.OnSaveReady(owner);
                Jinke.DredgeVoyage.CargoRefit.ReapplyOwned();
                if (PainterEnabled) Jinke.DredgePainter.PainterAppearance.OnSaveReady(owner as SaveData);
            }
            catch (Exception ex) { Debug.LogError("[Voyage] BIND_FAILED " + ex); }
        }
        public static void AfterGridInitialized(object owner)
        {
            if (!isolated) throw new InvalidOperationException("Voyage isolation guard has not completed.");
            Jinke.DredgeVoyage.CargoRefit.SessionAllowed = () => isolated;
            Jinke.DredgeVoyage.CargoRefit.OnGridInitialized(owner);
        }
        public static void AfterUpgradesChanged(object owner)
        { if (isolated) Jinke.DredgeVoyage.CargoRefit.ReapplyOwned(); }
        public static float AdjustPotMaintenance(float original, object owner)
        { return isolated ? Jinke.DredgeVoyage.FishingHooks.AdjustPotMaintenance(original, owner) : original; }
        public static float AdjustNetMaintenance(float original, object owner)
        { return isolated ? Jinke.DredgeVoyage.FishingHooks.AdjustNetMaintenance(original, owner) : original; }
        public static float AdjustSanityDeltaTime(float original, object owner)
        { return isolated ? Jinke.DredgeVoyage.TimeTuning.AdjustSanityDeltaTime(original, owner) : original; }
        public static void AfterSpiralTargets(object owner)
        { if (isolated) Jinke.DredgeVoyage.FishingHooks.AfterSpiralTargets(owner); }
        public static void FormatDeployableDurability(LanguageManager language, float current, float maximum,
            UnityEngine.Localization.Components.LocalizeStringEvent field, string key, object owner)
        { Jinke.DredgeVoyage.FishingHooks.FormatDeployableDurability(language, current, maximum, field, key, isolated ? owner : null); }
        public static string GetVoyageDiagnostics() { return Jinke.DredgeVoyage.VoyageRuntime.Diagnostics(); }
    }
}
