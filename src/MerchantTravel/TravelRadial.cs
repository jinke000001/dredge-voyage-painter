using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Jinke.DredgeMerchantTravel
{
    // Only owned Transform/Image/TMP objects. No original radial scripts, events, tweens or listeners are cloned.
    internal sealed class TravelRadial : IDisposable
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly TravelController controller;
        private readonly Transform owner;
        private GameObject root;
        private RectTransform wheel;
        private Canvas canvas;
        private CanvasGroup group;
        private Image selectionImage;
        private TextMeshProUGUI heading, detail, footer;
        private readonly List<Image> icons = new List<Image>();
        private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
        private AbilityRadial source;
        private Sprite lockedSprite, manifestSprite, dockSprite, rigSprite;
        private AssetReference openSound, closeSound, selectSound;
        private Vector2 openingPointer, lastPointer;
        private bool pointerArmed, keyboardSelection, visible;
        private float shownTime;
        private int lastSelection = -1;
        private string fontName, materialName, questionSpriteName;
        private TMP_FontAsset titleFont, descriptionFont;
        private const float Diameter = 600f;
        private const float Deadzone = 140f;

        internal TravelRadial(TravelController value, Transform parent) { controller = value; owner = parent; }

        internal void Show()
        {
            EnsureVisuals();
            root.SetActive(true); visible = true; shownTime = Time.unscaledTime;
            pointerArmed = keyboardSelection = false;
            openingPointer = lastPointer = Input.mousePosition;
            lastSelection = -1;
            group.alpha = 0;
            wheel.localScale = new Vector3(.92f, .92f, 1f);
            Refresh();
            Play(openSound);
        }
        internal void Hide()
        {
            if (!visible) return;
            visible = false;
            if (root != null) root.SetActive(false);
            Play(closeSound);
        }

        internal int UpdateSelection(int current)
        {
            Vector2 point = Input.mousePosition;
            bool moved = (point - lastPointer).sqrMagnitude > .25f;
            if (!pointerArmed && (point - openingPointer).sqrMagnitude >= 64f) pointerArmed = true;
            if (moved && pointerArmed) keyboardSelection = false;
            lastPointer = point;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.DownArrow))
            { keyboardSelection = true; return current < 0 ? 0 : (current + 1) % RadialRules.Count; }
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow))
            { keyboardSelection = true; return current < 0 ? RadialRules.Count - 1 : (current + RadialRules.Count - 1) % RadialRules.Count; }
            DredgeInputManager input = GameManager.Instance.Input;
            if (input.IsUsingController)
            {
                Vector2 stick = input.Controls.RadialSelect.Vector;
                float deadzone = Read<float>(source, "controllerDeadzoneMagnitude");
                if (stick.magnitude >= deadzone) { keyboardSelection = false; return RadialRules.Selection(stick.x, stick.y, deadzone); }
                return keyboardSelection ? current : -1;
            }
            if (keyboardSelection) return current;
            if (!pointerArmed) return -1;
            Vector2 center = RectTransformUtility.WorldToScreenPoint(null, wheel.position);
            Vector2 delta = point - center;
            return RadialRules.Selection(delta.x, delta.y, Deadzone * canvas.scaleFactor);
        }

        internal void Refresh()
        {
            if (root == null || !visible) return;
            float progress = Mathf.Clamp01((Time.unscaledTime - shownTime) / .35f);
            float ease = progress >= 1 ? 1 : 1 - Mathf.Pow(2, -10 * progress);
            group.alpha = ease;
            wheel.localScale = new Vector3(.92f + .08f * ease, .92f + .08f * ease, 1);
            int selected = controller.Selection;
            if (selected != lastSelection)
            {
                if (selected >= 0) Play(selectSound);
                lastSelection = selected;
            }
            selectionImage.enabled = selected >= 0;
            selectionImage.rectTransform.localEulerAngles = new Vector3(0, 0, 360f - selected * (360f / RadialRules.Count) + 90f + 180f / RadialRules.Count);
            for (int i = 0; i < controller.Targets.Count; i++)
            {
                TargetSnapshot target = controller.Targets[i];
                bool known = target.nativeDestination || (target.visited && target.hasDlc);
                bool highlighted = i == selected;
                icons[i].sprite = !known ? lockedSprite : target.nativeDestination ? manifestSprite :
                    target.id == "dock.the-iron-rig" ? rigSprite : dockSprite;
                icons[i].color = highlighted ? Color.black : Color.white;
                labels[i].color = highlighted ? Color.black : new Color(.93f, .93f, .9f);
                labels[i].text = known ? target.region : "???";
            }
            TargetSnapshot active = selected >= 0 && selected < controller.Targets.Count ? controller.Targets[selected] : null;
            bool reveal = active != null && (active.nativeDestination || (active.visited && active.hasDlc));
            heading.text = active == null ? "显形" : reveal ? active.name : "未知目的地";
            detail.text = active == null ? "滑动鼠标选择目的地\n松开能力按键前往" : !reveal ?
                (active.hasDlc ? "亲自停靠后解锁" : "需要对应 DLC\n并亲自停靠") :
                active.unavailable ?? active.region;
            footer.text = controller.Notice ?? "移回中心或 Esc 取消    ·    方向键选择 / Enter 确认";
        }

        private void EnsureVisuals()
        {
            if (root != null) return;
            foreach (AbilityRadial candidate in Resources.FindObjectsOfTypeAll<AbilityRadial>())
                if (candidate != null && candidate.gameObject.scene.IsValid() && Read<AbilityBarUI>(candidate, "abilityBarUI") == GameManager.Instance.UI.AbilityBarUI)
                { if (source != null) throw new InvalidOperationException("Multiple native radial sources."); source = candidate; }
            if (source == null) throw new InvalidOperationException("Native radial visual source unavailable.");
            Image wedge = Read<Image>(source, "selectionWedgeImage");
            Image cooldown = Read<Image>(source, "cooldownRadialFill");
            List<AbilityRadialWedge> sourceWedges = Read<List<AbilityRadialWedge>>(source, "abilityWedges");
            Component titleComponent = Read<Component>(source, "localizedTitleField");
            Component descriptionComponent = Read<Component>(source, "localizedDescriptionField");
            TextMeshProUGUI title = titleComponent.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI description = descriptionComponent.GetComponent<TextMeshProUGUI>();
            if (wedge == null || wedge.sprite == null || cooldown == null || title == null || title.font == null || description == null || sourceWedges.Count == 0)
                throw new InvalidOperationException("Native radial art/font contract unavailable.");
            lockedSprite = Read<Sprite>(sourceWedges[0], "lockedSprite");
            manifestSprite = NativeManifest.Data.icon;
            if (lockedSprite == null || manifestSprite == null) throw new InvalidOperationException("Native radial icon unavailable.");
            dockSprite = FindNativeSprite("dock-interact-icon") ?? manifestSprite;
            rigSprite = FindNativeSprite("IronhavenMapMarker") ?? dockSprite;
            openSound = Read<AssetReference>(source, "openSFX"); closeSound = Read<AssetReference>(source, "closeSFX"); selectSound = Read<AssetReference>(source, "selectSFX");
            titleFont = LocalizedFont(title); descriptionFont = LocalizedFont(description);
            fontName = titleFont.name; materialName = wedge.material != null ? wedge.material.name : null; questionSpriteName = lockedSprite.name;
            root = new GameObject("Jinke-Manifest-DestinationRadial", typeof(RectTransform));
            root.SetActive(false); root.hideFlags = HideFlags.DontSave;
            root.transform.SetParent(owner, false);
            canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
            group = root.AddComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            Image veil = CreateImage("Backdrop", (RectTransform)root.transform, null, new Color(0, 0, 0, .42f));
            veil.rectTransform.anchorMin = Vector2.zero; veil.rectTransform.anchorMax = Vector2.one;
            veil.rectTransform.offsetMin = veil.rectTransform.offsetMax = Vector2.zero;
            wheel = CreateRect("Wheel", root.transform, new Vector2(Diameter, Diameter), new Vector2(0, 20));
            Image disk = CopyImage("NativeDisk", wheel, wedge, Diameter, new Color(0, 0, 0, .7843f));
            disk.type = Image.Type.Simple; disk.fillAmount = 1;
            selectionImage = CopyImage("NativeSelection", wheel, wedge, Diameter, Color.white);
            selectionImage.type = Image.Type.Filled; selectionImage.fillMethod = Image.FillMethod.Radial360;
            selectionImage.fillClockwise = true; selectionImage.fillOrigin = (int)Image.Origin360.Right;
            selectionImage.fillAmount = 1f / RadialRules.Count;
            Image center = CopyImage("NativeCenter", wheel, cooldown.sprite != null ? cooldown : wedge, 280, Color.black);
            center.type = Image.Type.Simple; center.fillAmount = 1;
            heading = Label("Destination", wheel, title, titleFont, 32, new Vector2(256, 62), new Vector2(0, 24));
            detail = Label("Region", wheel, description, descriptionFont, 21, new Vector2(244, 70), new Vector2(0, -36));
            detail.enableWordWrapping = true;
            footer = Label("Controls", (RectTransform)root.transform, description, descriptionFont, 22, new Vector2(1000, 56), new Vector2(0, -350));
            for (int i = 0; i < RadialRules.Count; i++)
            {
                float angle = i * Mathf.PI * 2 / RadialRules.Count;
                Vector2 direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                Image icon = CreateImage("DestinationIcon-" + i, wheel, manifestSprite, Color.white);
                icon.rectTransform.sizeDelta = new Vector2(58, 58); icon.rectTransform.anchoredPosition = direction * 220f;
                icon.preserveAspect = true; icon.material = Read<Image>(sourceWedges[0], "image").material;
                icons.Add(icon);
                TextMeshProUGUI label = Label("DestinationRegion-" + i, wheel, description, descriptionFont, 22,
                    new Vector2(170, 36), direction * 220f + new Vector2(0, -48));
                labels.Add(label);
            }
            // Labels/icons are above the selected native wedge, while the central hole stays opaque.
            root.SetActive(false);
            TravelLog.Info("RADIAL_ART " + fontName + " / " + questionSpriteName + " / " + materialName);
        }
        private static RectTransform CreateRect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = size; rect.anchoredPosition = position;
            return rect;
        }
        private static Image CreateImage(string name, RectTransform parent, Sprite sprite, Color color)
        {
            RectTransform rect = CreateRect(name, parent, parent.rect.size, Vector2.zero);
            Image image = rect.gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = color; image.raycastTarget = false;
            return image;
        }
        private static Image CopyImage(string name, RectTransform parent, Image sourceImage, float diameter, Color color)
        {
            Image result = CreateImage(name, parent, sourceImage.sprite, color);
            result.material = sourceImage.material; result.rectTransform.sizeDelta = new Vector2(diameter, diameter);
            result.preserveAspect = sourceImage.preserveAspect;
            return result;
        }
        private static TMP_FontAsset LocalizedFont(TextMeshProUGUI text)
        {
            // The native radial may never have been opened: its disabled child localizer has not necessarily run.
            // Resolve the same native Fonts-table entry without enabling or modifying that component.
            LocalizeFontBypass localizer = text.GetComponent<LocalizeFontBypass>();
            TMP_FontAsset font = localizer != null ? LocalizationSettings.AssetDatabase.GetLocalizedAsset<TMP_FontAsset>(
                localizer.tableString, localizer.tableEntryString) : text.font;
            if (font == null) throw new InvalidOperationException("Native localized radial font unavailable.");
            return font;
        }
        private static Sprite FindNativeSprite(string name)
        {
            Sprite found = null;
            foreach (Sprite candidate in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (candidate == null || candidate.name != name) continue;
                if (found != null && found != candidate) return null; // Ambiguous art is never guessed.
                found = candidate;
            }
            return found;
        }
        private static TextMeshProUGUI Label(string name, RectTransform parent, TextMeshProUGUI sourceText, TMP_FontAsset font, float size, Vector2 bounds, Vector2 point)
        {
            RectTransform rect = CreateRect(name, parent, bounds, point);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSharedMaterial = font == sourceText.font ? sourceText.fontSharedMaterial : font.material;
            text.fontSize = size; text.fontStyle = sourceText.fontStyle; text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false; text.richText = false;
            return text;
        }
        private static T Read<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, Private);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            object value = field.GetValue(target);
            if (!(value is T)) throw new InvalidOperationException("Native radial field unavailable: " + name);
            return (T)value;
        }
        private static void Play(AssetReference sound)
        { if (sound != null && GameManager.Instance != null && GameManager.Instance.AudioPlayer != null) GameManager.Instance.AudioPlayer.PlaySFX(sound, AudioLayer.SFX_UI); }
        internal object Diagnostics()
        {
            Vector2 center = wheel != null ? RectTransformUtility.WorldToScreenPoint(null, wheel.position) : Vector2.zero;
            return new { visible, pointerArmed, keyboardSelection, fontName, materialName, questionSpriteName,
                nativeSource = source != null ? source.GetInstanceID() : 0, center = new[] { center.x, center.y },
                dockIcon = dockSprite != null ? dockSprite.name : null, rigIcon = rigSprite != null ? rigSprite.name : null,
                deadzonePixels = canvas != null ? Deadzone * canvas.scaleFactor : 0, count = RadialRules.Count,
                openingPointer = new[] { openingPointer.x, openingPointer.y } };
        }
        internal void Reset()
        {
            Hide();
            if (root != null) Object.Destroy(root);
            root = null; source = null; wheel = null; canvas = null; group = null;
            icons.Clear(); labels.Clear();
        }
        public void Dispose() { Reset(); }
    }
}
