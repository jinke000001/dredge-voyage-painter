using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Jinke.DredgeMerchantTravel
{
    /// <summary>Scoped ownership only; never changes native action layers or native pause flags.</summary>
    internal sealed class InputLease : IDisposable
    {
        private Player player;
        private DredgeInputManager input;
        private EventSystem events;
        private bool hadNavigation;
        private GameObject selected, shield;
        private float priorScale;
        private bool priorCursorVisible;
        private CursorLockMode priorCursorLock;
        private bool held;
        internal bool HadInput { get; private set; }
        internal bool SlowsWorld { get; private set; }
        internal const float RadialTimeScale = .3f;
        internal bool IsHeld { get { return held; } }
        internal bool Owns(Player value) { return player != null && player == value && input == GameManager.Instance.Input; }

        internal void Acquire(Player value, Transform parent)
        {
            if (IsHeld) throw new InvalidOperationException("Input already held.");
            player = value;
            input = GameManager.Instance.Input;
            HadInput = input.enabled;
            priorScale = Time.timeScale;
            priorCursorVisible = Cursor.visible;
            priorCursorLock = Cursor.lockState;
            held = true;
            input.enabled = false;
            events = EventSystem.current;
            if (events != null)
            {
                hadNavigation = events.sendNavigationEvents;
                selected = events.currentSelectedGameObject;
                events.SetSelectedGameObject(null);
                events.sendNavigationEvents = false;
            }
            shield = new GameObject("MerchantTravel-PointerShield", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            shield.hideFlags = HideFlags.DontSave;
            shield.transform.SetParent(parent, false);
            var canvas = shield.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            canvas.overrideSorting = true;
            var area = new GameObject("HitArea", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            area.transform.SetParent(shield.transform, false);
            var rect = area.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            area.GetComponent<Image>().color = Color.clear;
            area.GetComponent<Image>().raycastTarget = true;
            area.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
            Time.timeScale = RadialTimeScale;
            SlowsWorld = true;
            MaintainCursor();
            TravelLog.Info("INPUT_CAPTURE");
        }

        internal void MaintainCursor()
        {
            if (!IsHeld) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        internal void ResumeWorld()
        {
            if (!SlowsWorld) return;
            GameManager game = GameManager.Instance;
            if (Mathf.Abs(Time.timeScale - RadialTimeScale) < .001f && (game == null || !game.IsPaused)) Time.timeScale = priorScale;
            SlowsWorld = false;
        }

        internal static bool ClosingControlHeld
        {
            get { GameManager game = GameManager.Instance;
                return (game != null && game.Input != null && game.Input.Controls != null && game.Input.Controls.DoAbility.IsPressed) ||
                    Input.GetKey(KeyCode.Escape) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter); }
        }

        public void Dispose()
        {
            try
            {
                ResumeWorld();
                GameManager game = GameManager.Instance;
                bool restoreSelection = game != null && player != null && game.Player == player && player.IsAlive && !player.IsDocked &&
                    game.Input != null && game.Input.GetActiveActionLayer() == ActionLayer.BASE && !game.IsPaused &&
                    game.UI != null && game.UI.ShowingWindowTypes.Count == 0 && !game.UI.IsInCutscene;
                if (input != null)
                {
                    input.enabled = HadInput;
                    // A popup/controller change may now own the cursor; ask the current native input state.
                    input.RefreshCursorLockState();
                }
                else if (held)
                {
                    Cursor.lockState = priorCursorLock;
                    Cursor.visible = priorCursorVisible;
                }
                if (events != null)
                {
                    events.sendNavigationEvents = hadNavigation;
                    if (restoreSelection && events == EventSystem.current && events.currentSelectedGameObject == null && selected != null && selected.activeInHierarchy)
                        events.SetSelectedGameObject(selected);
                }
            }
            finally
            {
                if (shield != null)
                {
                    shield.GetComponent<GraphicRaycaster>().enabled = false;
                    Object.Destroy(shield);
                }
                bool wasHeld = held;
                held = false;
                shield = null; player = null; input = null; events = null; selected = null;
                if (wasHeld) TravelLog.Info("INPUT_RESTORE");
            }
        }
    }
}
