using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Jinke.DredgeVoyage
{
    // A seventh tab inside the game's real settings dialog. No extra canvas or hotkey.
    internal sealed class VoyageSettingsUI : MonoBehaviour
    {
        private static SettingsDialog installed;
        private readonly List<DropdownSettingInput> rows = new List<DropdownSettingInput>();
        private readonly List<TMP_Dropdown> dropdowns = new List<TMP_Dropdown>();
        private TabbedPanelContainer tabs;
        private BasicButtonWrapper reset;
        private bool refreshing;
        internal static void TryInstall()
        {
            var dialog = Object.FindObjectOfType<SettingsDialog>(true);
            if (dialog == null || installed == dialog) return;
            var container = Get<TabbedPanelContainer>(dialog, "dialog");
            TabConfig template = container.TabbedPanels.FirstOrDefault(t => t.panel.name == "AccessibilityPanel");
            if (template == null) throw new InvalidOperationException("原辅助设置面板未找到。");
            DropdownSettingInput rowTemplate = template.panel.GetComponentsInChildren<DropdownSettingInput>(true).FirstOrDefault(d => d.settingType == SettingType.NO_FAIL_BEHAVIOUR);
            if (rowTemplate == null) throw new InvalidOperationException("原设置控件未找到。");
            var panelObject = Object.Instantiate(template.panel.gameObject, template.panel.transform.parent);
            panelObject.name = "VoyageExperiencePanel";
            panelObject.SetActive(false);
            var panel = panelObject.GetComponent<TabbedPanel>();
            var scroll = panel.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null || scroll.content == null) throw new InvalidOperationException("原设置滚动布局未找到。");
            // These are disposable clones. The source settings panel and every native setting remain intact.
            foreach (Transform child in scroll.content.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            var view = panelObject.AddComponent<VoyageSettingsUI>();
            view.tabs = container;
            view.CreateRows(rowTemplate, scroll.content);
            view.CreateReset(dialog, scroll.content);
            foreach (var focus in panel.GetComponentsInChildren<ControllerFocusGrabber>(true)) focus.selectThis = view.dropdowns[0];
            var tab = Object.Instantiate(template.tab, template.tab.transform.parent);
            tab.name = "VoyageExperienceTab";
            tab.TabLocalizedString.StringReference = VoyageLocalization.Text("tab", "航行体验", "Voyage");
            tab.TabLocalizedString.RefreshString();
            tab.Button.onClick = new Button.ButtonClickedEvent();
            int index = container.TabbedPanels.Count;
            tab.Button.onClick.AddListener(() => container.ShowNewPanel(index, true));
            container.TabbedPanels.Add(new TabConfig { panel = panel, tab = tab });
            container.showablePanelIndexes.Add(index);
            // Retain the native tab strip's existing layout, allowing it to divide space among seven tabs.
            foreach (var config in container.TabbedPanels)
            {
                LayoutElement layout = config.tab.GetComponent<LayoutElement>();
                if (layout != null) { layout.flexibleWidth = 1; layout.minWidth = 0; }
            }
            panel.HideFinish();
            panelObject.SetActive(true);
            installed = dialog;
            Debug.Log("[Voyage] NATIVE_SETTINGS_TAB_READY options=7");
        }

        private void CreateRows(DropdownSettingInput template, RectTransform content)
        {
            string[] titles = { "钓鱼辅助", "畸变遭遇", "蟹笼捕捞", "蟹笼耐用", "拖网捕捞", "拖网耐用", "航行节奏" };
            string[] english = { "Fishing assistance", "Aberration encounters", "Pot catches", "Pot endurance", "Trawl catches", "Trawl endurance", "Voyage pace" };
            string[] descriptions = {
                "放宽普通判定，降低部分小游戏的反应要求。保留鱼种差异和金色奖杯目标；从下一轮小游戏生效。",
                "基础昼夜概率增加 1.5、3 或 30 个百分点；最高档为白天 31%、夜晚 33%。普通概率仍受原版 35% 上限限制，保留剧情解锁、特殊鱼点、装备和保底规则。",
                "普通蟹笼的捕获检查频率提高至原来的 125%、150% 或 300%，仍受鱼种、空间和海域限制。材料笼保持原版。",
                "降低普通蟹笼的耐久损耗，续航约为原来的 125%、150% 或 300%。现有耐久不补满，材料笼保持原版。",
                "普通拖网的捕获检查频率提高至原来的 125%、150% 或 300%，仍需航行、适合的鱼区和剩余空间。特殊用途拖网保持原版。",
                "降低普通拖网的耐久损耗，续航约为原来的 125%、150% 或 300%。现有耐久不补满，特殊用途拖网保持原版。",
                "航行与捕鱼的一天延长至原来的 125% 或 150%，晨昏比例不变。常规理智变化按游戏时间补偿，休息和安装等待不变。显形仍按游戏时间冷却，现实等待相应延长。"
            };
            string[] enDescriptions = {
                "Wider normal targets and more time to react. Fish difficulty and trophy targets remain distinct. Applies to the next minigame.",
                "Adds 1.5, 3 or 30 percentage points to the base chance. The highest setting gives 31% by day and 33% at night. The ordinary 35% cap, story gates, special spots, equipment and pity rules remain in place.",
                "Ordinary pots check for catches 1.25, 1.5 or 3 times as often. Stock, habitat and space still apply. Material pots are unchanged.",
                "Ordinary pots wear out more slowly for about 1.25, 1.5 or 3 times the endurance. Does not repair existing wear. Material pots are unchanged.",
                "Ordinary nets check for catches 1.25, 1.5 or 3 times as often. Movement, habitat and space still apply. Special-purpose nets are unchanged.",
                "Ordinary nets wear out more slowly for about 1.25, 1.5 or 3 times the endurance. Does not repair existing wear. Special-purpose nets are unchanged.",
                "Days while sailing or fishing last 1.25 or 1.5 times as long. Day/night proportions and regular sanity changes per game hour are preserved. Rest and installation waits stay the same. Manifest cooldown takes longer in real time."
            };
            for (int i = 0; i < titles.Length; i++)
            {
                var row = Object.Instantiate(template, content);
                row.gameObject.name = "VoyageOption" + i;
                row.gameObject.SetActive(false);
                row.settingType = SettingType.NONE;
                Set(row, "retrieveSelectedIndex", false);
                Set(row, "localizedString", VoyageLocalization.Text("option." + i, titles[i], english[i]));
                Set(row, "tooltipDescriptionString", VoyageLocalization.Text("description." + i, descriptions[i], enDescriptions[i]));
                var choices = new List<LocalizedString> { VoyageLocalization.Text("original", "原版", "Original") };
                if (i == 0) { choices.Add(VoyageLocalization.Text("relaxed", "宽松", "Relaxed")); choices.Add(VoyageLocalization.Text("more-relaxed", "更宽松", "More relaxed")); }
                else if (i == 1) { choices.Add(VoyageLocalization.Text("ab1", "略易遇见", "Slightly more common")); choices.Add(VoyageLocalization.Text("ab2", "更易遇见", "More common")); choices.Add(VoyageLocalization.Text("ab3", "大幅提高", "Much more common")); }
                else if (i == 6) { choices.Add(VoyageLocalization.Text("pace1", "舒缓 · 时长 125%", "Leisurely · 1.25× day")); choices.Add(VoyageLocalization.Text("pace2", "更舒缓 · 时长 150%", "Unhurried · 1.5× day")); }
                else { choices.Add(VoyageLocalization.Text("boost1", "适度 · 125%", "Moderate · 1.25×")); choices.Add(VoyageLocalization.Text("boost2", "增强 · 150%", "Enhanced · 1.5×")); choices.Add(VoyageLocalization.Text("boost3", "大幅增强 · 300%", "Powerful · 3×")); }
                Set(row, "optionStrings", choices);
                row.CurrentIndex = VoyageRuntime.Current.Get(i);
                var dropdown = Get<TMP_Dropdown>(row, "dropdown");
                dropdown.SetValueWithoutNotify(row.CurrentIndex);
                int captured = i;
                dropdown.onValueChanged.AddListener(value => OnValueChanged(captured, value));
                rows.Add(row); dropdowns.Add(dropdown);
                row.gameObject.SetActive(true);
            }
            for (int i = 0; i < dropdowns.Count; i++)
            {
                var navigation = dropdowns[i].navigation;
                navigation.mode = Navigation.Mode.Automatic;
                dropdowns[i].navigation = navigation;
            }
        }

        private void CreateReset(SettingsDialog dialog, RectTransform content)
        {
            var original = dialog.GetComponentInChildren<ResetAllSettingsButton>(true);
            if (original == null) original = Object.FindObjectOfType<ResetAllSettingsButton>(true);
            if (original == null) throw new InvalidOperationException("原恢复设置按钮未找到。");
            var clone = Object.Instantiate(original.gameObject, content);
            clone.name = "VoyageRestoreDefaults";
            clone.SetActive(false);
            Object.DestroyImmediate(clone.GetComponent<ResetAllSettingsButton>());
            reset = clone.GetComponent<BasicButtonWrapper>();
            reset.OnClick = RestoreConfirmation;
            reset.LocalizedString.StringReference = VoyageLocalization.Text("restore", "恢复本页默认", "Restore voyage defaults");
            reset.LocalizedString.RefreshString();
            clone.SetActive(true);
        }
        private void RestoreConfirmation()
        {
            tabs.DisableTabInput();
            ApplicationEvents.Instance.TriggerSettingsConfirmationToggled(true);
            var options = new DialogOptions {
                text = VoyageLocalization.Key("confirm-restore", "将本页的航行与捕捞设置恢复为原版。已购买的货舱改装保留，可在干船坞单独移除。", "Restore voyage and fishing settings to their original values? Purchased cargo refits remain; remove them separately at the dry dock."),
                buttonOptions = new[] {
                    new DialogButtonOptions { id = 0, buttonString = "prompt.cancel", hideOnButtonPress = true, isBackOption = true },
                    new DialogButtonOptions { id = 1, buttonString = "prompt.confirm", hideOnButtonPress = true }
                }
            };
            GameManager.Instance.DialogManager.ShowDialog(options, result => {
                string error;
                if (result.id == 1 && !VoyageRuntime.RestorePreferences(out error)) Debug.LogError("[Voyage] " + error);
                RefreshRows(); tabs.EnableTabInput();
                ApplicationEvents.Instance.TriggerSettingsConfirmationToggled(false);
                GameManager.Instance.Input.SetActiveActionLayer(ActionLayer.SYSTEM);
                EventSystem.current.SetSelectedGameObject(reset.gameObject);
            });
        }
        private void OnValueChanged(int row, int value)
        {
            if (refreshing) return;
            string error;
            if (!VoyageRuntime.Change(row, value, out error))
            { Debug.LogError("[Voyage] " + error); RefreshRows(); }
        }
        private void OnEnable() { ApplicationEvents.Instance.OnLocaleChanged += OnLocale; RefreshRows(); }
        private void OnDisable() { ApplicationEvents.Instance.OnLocaleChanged -= OnLocale; }
        private void OnLocale(Locale locale) { VoyageLocalization.Refresh(); RefreshRows(); }
        private void RefreshRows()
        {
            refreshing = true;
            try
            {
                VoyageSettings settings = VoyageRuntime.Current;
                for (int i = 0; i < rows.Count; i++)
                { rows[i].CurrentIndex = settings.Get(i); rows[i].RefreshDropdownStrings(); dropdowns[i].SetValueWithoutNotify(settings.Get(i)); }
            }
            finally { refreshing = false; }
        }
        private static T Get<T>(object target, string field)
        { return (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target); }
        private static void Set(object target, string field, object value)
        { target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value); }
    }
}
