using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Jinke.DredgeSkins
{
    /// <summary>Reads native pause state. Owns a raycast shield and restores captured input/navigation states.</summary>
    internal sealed class PauseUiGuard : IDisposable
    {
        private readonly Transform parent;
        private FieldInfo gameInstance;
        private PropertyInfo isPlaying;
        private PropertyInfo isPaused;
        private PropertyInfo canUnpause;
        private PropertyInfo inputProperty;
        private PropertyInfo controlsProperty;
        private PropertyInfo isBinding;
        private GameObject shield;
        private RectTransform hitArea;
        private EventSystem navigationOwner;
        private bool originalNavigation;
        private bool holdingNavigation;
        private GameObject originalSelection;
        private Behaviour inputOwner;
        private bool originalInputEnabled;
        private bool holdingInput;
        private int releaseAfterFrame = -1;

        internal PauseUiGuard(Transform parent) { this.parent = parent; }

        internal void Bind(Assembly gameAssembly)
        {
            if (gameInstance != null) return;
            Type gameManager = gameAssembly.GetType("GameManager", true);
            gameInstance = gameManager.GetField("Instance", BindingFlags.Static | BindingFlags.Public);
            isPlaying = gameManager.GetProperty("IsPlaying", BindingFlags.Public | BindingFlags.Instance);
            isPaused = gameManager.GetProperty("IsPaused", BindingFlags.Public | BindingFlags.Instance);
            canUnpause = gameManager.GetProperty("CanUnpause", BindingFlags.Public | BindingFlags.Instance);
            inputProperty = gameManager.GetProperty("Input", BindingFlags.Public | BindingFlags.Instance);
            if (inputProperty != null) controlsProperty = inputProperty.PropertyType.GetProperty("Controls");
            if (controlsProperty != null) isBinding = controlsProperty.PropertyType.GetProperty("IsListeningForBinding");
            if (gameInstance == null || isPlaying == null || isPaused == null || canUnpause == null || inputProperty == null || controlsProperty == null || isBinding == null)
                throw new MissingMemberException("Native pause-state read contract is unavailable.");
        }

        internal bool IsSafe
        {
            get
            {
                if (gameInstance == null || EventSystem.current == null) return false;
                object instance = gameInstance.GetValue(null);
                if (instance == null || (instance is Object && (Object)instance == null)) return false;
                if (!(bool)isPlaying.GetValue(instance, null) || !(bool)isPaused.GetValue(instance, null) || !(bool)canUnpause.GetValue(instance, null)) return false;
                object input = inputProperty.GetValue(instance, null);
                if (input == null) return false;
                object controls = controlsProperty.GetValue(input, null);
                return controls != null && !(bool)isBinding.GetValue(controls, null);
            }
        }

        internal void Sync(bool windowOpen, float scale)
        {
            if (!IsSafe)
            {
                ReleaseInputAndNavigation(false);
                DestroyShield();
                return;
            }
            EnsureShield();
            if (windowOpen)
            {
                releaseAfterFrame = -1;
                HoldInput();
                HoldNavigation();
            }
            else if (holdingInput || holdingNavigation)
            {
                // The closing click/key never reaches the native paused menu on its release frame.
                if (AnyClosingControlHeld) releaseAfterFrame = -1;
                else if (releaseAfterFrame < 0) releaseAfterFrame = Time.frameCount + 1;
                else if (Time.frameCount >= releaseAfterFrame) ReleaseInputAndNavigation(true);
            }
            if (windowOpen || holdingInput || holdingNavigation)
            {
                hitArea.anchorMin = Vector2.zero;
                hitArea.anchorMax = Vector2.one;
                hitArea.offsetMin = Vector2.zero;
                hitArea.offsetMax = Vector2.zero;
            }
            else
            {
                hitArea.anchorMin = new Vector2(1, 1);
                hitArea.anchorMax = new Vector2(1, 1);
                hitArea.pivot = new Vector2(1, 1);
                hitArea.sizeDelta = new Vector2(128 * scale, 32 * scale);
                hitArea.anchoredPosition = new Vector2(-16 * scale, -16 * scale);
            }
        }

        internal static bool PointerHeld
        {
            get { return Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2) || Input.touchCount > 0; }
        }

        private static bool AnyClosingControlHeld
        {
            get { return PointerHeld || Input.GetKey(KeyCode.Escape) || Input.GetKey(KeyCode.F8); }
        }

        private void HoldInput()
        {
            Behaviour current = inputProperty.GetValue(gameInstance.GetValue(null), null) as Behaviour;
            if (current == null) throw new InvalidOperationException("Native paused input component unavailable.");
            if (holdingInput && inputOwner != current) ReleaseInputAndNavigation(false);
            if (!holdingInput)
            {
                inputOwner = current;
                originalInputEnabled = current.enabled;
                holdingInput = true;
                SkinLog.Info("[JinkeSkins] UI_INPUT_CAPTURE originalEnabled=" + originalInputEnabled);
            }
            current.enabled = false;
        }

        private void EnsureShield()
        {
            if (shield != null) return;
            shield = new GameObject("JinkeSkins-PointerShield", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            shield.hideFlags = HideFlags.DontSave;
            shield.transform.SetParent(parent, false);
            Canvas canvas = shield.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            canvas.overrideSorting = true;
            var area = new GameObject("HitArea", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            area.transform.SetParent(shield.transform, false);
            hitArea = area.GetComponent<RectTransform>();
            Image graphic = area.GetComponent<Image>();
            graphic.color = Color.clear;
            graphic.raycastTarget = true;
            area.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
        }

        private void HoldNavigation()
        {
            EventSystem current = EventSystem.current;
            if (holdingNavigation && navigationOwner != current) ReleaseNavigation(false);
            if (!holdingNavigation)
            {
                navigationOwner = current;
                originalNavigation = current.sendNavigationEvents;
                originalSelection = current.currentSelectedGameObject;
                holdingNavigation = true;
                current.SetSelectedGameObject(null);
            }
            current.sendNavigationEvents = false;
        }

        private void ReleaseNavigation(bool restoreSelection)
        {
            if (!holdingNavigation) return;
            if (navigationOwner != null)
            {
                navigationOwner.sendNavigationEvents = originalNavigation;
                // Do not steal selection back from a newly opened native popup or a replacement EventSystem.
                if (restoreSelection && navigationOwner == EventSystem.current && navigationOwner.currentSelectedGameObject == null && originalSelection != null && originalSelection.activeInHierarchy)
                    navigationOwner.SetSelectedGameObject(originalSelection);
            }
            navigationOwner = null;
            originalSelection = null;
            holdingNavigation = false;
        }

        private void ReleaseInputAndNavigation(bool restoreSelection)
        {
            bool wasHeld = holdingInput;
            try { ReleaseNavigation(restoreSelection); }
            finally
            {
                if (holdingInput && inputOwner != null) inputOwner.enabled = originalInputEnabled;
                if (wasHeld) SkinLog.Info("[JinkeSkins] UI_INPUT_RESTORE enabled=" + originalInputEnabled + "; normalClose=" + restoreSelection);
                inputOwner = null;
                holdingInput = false;
                releaseAfterFrame = -1;
            }
        }

        private void DestroyShield()
        {
            if (shield == null) return;
            // Removing the raycaster immediately avoids a one-frame lingering hit target before Destroy completes.
            shield.GetComponent<GraphicRaycaster>().enabled = false;
            Object.Destroy(shield);
            shield = null;
            hitArea = null;
        }

        public void Dispose()
        {
            bool restoreSelection = false;
            try { restoreSelection = IsSafe; }
            catch (Exception exception) { SkinLog.Warning("[JinkeSkins] Pause state unavailable during cleanup: " + exception.Message); }
            try { ReleaseInputAndNavigation(restoreSelection); }
            finally { DestroyShield(); }
        }
    }
}
