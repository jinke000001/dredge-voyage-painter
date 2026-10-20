using System;
using UnityEngine;

namespace Jinke.DredgeSkins
{
    /// <summary>The only two methods called by the patched game assembly. No game assembly reference.</summary>
    public static class Bootstrap
    {
        private static SkinController controller;
        private static bool quitting;
        private static bool initializationFailed;

        public static void OnColors(object owner) { Dispatch(owner, false); }
        public static void OnFlag(object owner) { Dispatch(owner, true); }

        private static void Dispatch(object owner, bool flag)
        {
            try
            {
                if (quitting || initializationFailed || owner == null) return;
                EnsureController();
                controller.Register(owner, flag);
            }
            catch (Exception exception)
            {
                if (controller != null)
                {
                    try { controller.FailSafe(exception); }
                    catch (Exception cleanupError) { SkinLog.Error("[JinkeSkins] Cleanup error: " + cleanupError); }
                }
                else
                {
                    initializationFailed = true;
                    SkinLog.Error("[JinkeSkins] Bootstrap disabled: " + exception);
                }
            }
        }

        private static void EnsureController()
        {
            if (controller != null) return;
            var host = new GameObject("JinkeSkins-CosmeticsOnly");
            host.hideFlags = HideFlags.DontSave;
            UnityEngine.Object.DontDestroyOnLoad(host);
            try { controller = host.AddComponent<SkinController>(); }
            catch { UnityEngine.Object.Destroy(host); throw; }
        }

        internal static void OnQuitting() { quitting = true; }
        internal static void OnDestroyed(SkinController old) { if (controller == old) controller = null; }

        /// <summary>Runtime inspection only; reports this component's state, without touching game save data.</summary>
        public static string GetDiagnostics()
        {
            return controller != null ? controller.Diagnostics : "JinkeSkins: no player cosmetic hook observed.";
        }

        /// <summary>Optional isolated runtime harness interface. Does not persist choices or call game APIs.</summary>
        public static void ConfigurePreview(string boatId, string flagId, bool showWindow)
        {
            if (controller == null) return;
            controller.Select(boatId, flagId, false);
            controller.SetWindow(showWindow);
        }
    }
}
