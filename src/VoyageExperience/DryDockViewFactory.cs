using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace Jinke.DredgeVoyage
{
    // Both pages use the native button, font and localization bindings.
    internal sealed class DryDockViewFactory
    {
        private readonly GameObject buttonTemplate;
        private readonly string fontTable;
        private readonly string fontEntry;

        internal DryDockViewFactory(GameObject buttonTemplate, string fontTable, string fontEntry)
        { this.buttonTemplate = buttonTemplate; this.fontTable = fontTable; this.fontEntry = fontEntry; }

        internal BasicButtonWrapper Button(Transform parent, string name, string key, string zh, string en, Action clicked)
        {
            GameObject instance = UnityEngine.Object.Instantiate(buttonTemplate, parent);
            instance.name = name; instance.SetActive(false);
            try
            {
                BasicButtonWrapper wrapper = instance.GetComponent<BasicButtonWrapper>();
                CargoRefitUI.Set(wrapper, "automaticallyAnimateTransition", false);
                CargoRefitUI.Set(wrapper, "randomizeTransitionInDelay", false);
                CargoRefitUI.Set(wrapper, "transitionOutOnClick", false);
                CargoRefitUI.Set(wrapper, "needsAnimatingWhenReEnabling", false);
                CargoRefitUI.Set(wrapper, "awaitingTransition", false);
                object effect = CargoRefitUI.Read<object>(wrapper, "transitionEffect");
                if (effect != null)
                {
                    PropertyInfo factor = effect.GetType().GetProperty("effectFactor");
                    if (factor != null && factor.CanWrite) factor.SetValue(effect, 1f, null);
                }
                wrapper.Button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                wrapper.OnClick = clicked;
                wrapper.SetCanBeClicked(true);
                SetButtonText(wrapper, key, zh, en);
                foreach (TextMeshProUGUI label in instance.GetComponentsInChildren<TextMeshProUGUI>(true))
                { label.fontSize = 25; label.enableAutoSizing = true; label.fontSizeMin = 20; label.fontSizeMax = 25; }
                instance.SetActive(true);
                return wrapper;
            }
            catch { UnityEngine.Object.Destroy(instance); throw; }
        }

        internal TextMeshProUGUI Text(Transform parent, string name, string text, float fontSize,
            Vector2 position, Vector2 size, TextAlignmentOptions alignment)
        {
            RectTransform rect = Rect(parent, name);
            Position(rect, new Vector2(.5f, .5f), new Vector2(.5f, .5f), size, position);
            rect.gameObject.SetActive(false);
            try
            {
                TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
                TMP_FontAsset font = LocalizationSettings.AssetDatabase.GetLocalizedAsset<TMP_FontAsset>(fontTable, fontEntry);
                if (font == null) throw new InvalidOperationException("原生本地化字体尚未准备好。");
                label.font = font; label.fontSharedMaterial = font.material;
                label.text = text; label.fontSize = fontSize; label.alignment = alignment;
                label.color = GameManager.Instance.LanguageManager.GetColor(DredgeColorTypeEnum.NEUTRAL);
                label.enableWordWrapping = true; label.raycastTarget = false;
                LocalizeFontBypass binding = rect.gameObject.AddComponent<LocalizeFontBypass>();
                binding.textField = label; binding.tableString = fontTable; binding.tableEntryString = fontEntry;
                rect.gameObject.SetActive(true);
                return label;
            }
            catch { UnityEngine.Object.Destroy(rect.gameObject); throw; }
        }

        internal static void SetButtonText(BasicButtonWrapper button, string key, string zh, string en)
        {
            button.LocalizedString.StringReference = VoyageLocalization.Text(key, zh, en);
            button.LocalizedString.StringReference.Arguments = null;
            button.LocalizedString.RefreshString();
        }

        internal static string Value(string key, string zh, string en)
        { return LocalizationSettings.StringDatabase.GetLocalizedString(LanguageManager.STRING_TABLE, VoyageLocalization.Key(key, zh, en)); }

        internal static RectTransform Rect(Transform parent, string name)
        {
            var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            result.SetParent(parent, false); return result;
        }

        internal static void Position(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
        { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.sizeDelta = size; rect.anchoredPosition = position; }
    }
}
