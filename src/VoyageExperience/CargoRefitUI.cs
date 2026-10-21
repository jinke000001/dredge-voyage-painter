using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace Jinke.DredgeVoyage
{
    internal static class CargoRefitUI
    {
        internal static void TryInstall()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.UI == null || game.UI.UpgradeWindow == null) return;
            UpgradeWindow window = game.UI.UpgradeWindow as UpgradeWindow;
            if (window == null) return;
            CargoRefitPanel panel = window.GetComponent<CargoRefitPanel>();
            if (panel == null) panel = window.gameObject.AddComponent<CargoRefitPanel>();
            panel.TrySetup(window);
            panel.RefreshAvailability();
        }

        internal static T Read<T>(object owner, string name) where T : class
        {
            if (owner == null) return null;
            for (Type type = owner.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(owner) as T;
            }
            return null;
        }

        internal static void Set(object owner, string name, object value)
        {
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) throw new MissingFieldException(owner.GetType().Name, name);
            field.SetValue(owner, value);
        }
    }

    // A subpage of the existing dry-dock window. It does not introduce a new
    // top-level canvas, pause mode, hotkey, inventory grid, or upgrade record.
    internal sealed class CargoRefitPanel : MonoBehaviour
    {
        private UpgradeWindow window;
        private GameObject nodes;
        private RectTransform content;
        private RectTransform diagram;
        private BasicButtonWrapper entry;
        private BasicButtonWrapper purchase;
        private BasicButtonWrapper remove;
        private BasicButtonWrapper back;
        private TextMeshProUGUI heading;
        private TextMeshProUGUI description;
        private TextMeshProUGUI status;
        private TextMeshProUGUI legend;
        private string fontTable;
        private string fontEntry;
        private GridCell cellSource;
        private GameObject buttonTemplate;
        private ControllerFocusGrabber nativeFocus;
        private Selectable nativeFocusTarget;
        private ControllerFocusGrabber detailFocus;
        private DredgePlayerActionPress nativeBack;
        private DredgePlayerActionPress detailBack;
        private readonly Dictionary<Selectable, Navigation> previousNavigation = new Dictionary<Selectable, Navigation>();
        private bool initialized;
        private readonly DryDockPageState pages = new DryDockPageState();
        private DryDockViewFactory view;
        private DryDockAppearancePage appearance;
        private bool details { get { return pages.Current == DryDockPage.Cargo; } }
        private bool dialogOpen { get { return pages.ModalOpen; } set { pages.SetModal(value); } }
        private Selectable treeFocusTarget;
        private bool nativeFocusWasEnabled;
        private bool restoreFocusOnShow;
        private bool backLease;
        private bool destroying;
        private DredgeInputManager leaseInput;
        private int drawnTier;
        private int drawnLevel;
        private SaveData shownSave;
        private string lastStatus;
        private bool didLogFailure;

        internal void TrySetup(UpgradeWindow owner)
        {
            if (initialized) return;
            GameManager game = GameManager.Instance;
            RectTransform frame = owner.transform.Find("Container/Window") as RectTransform;
            Transform tree = frame == null ? null : frame.Find("Nodes");
            GameObject popupPrefab = CargoRefitUI.Read<GameObject>(game.DialogManager, "popupDialogPrefab");
            PopupDialog popup = popupPrefab == null ? null : popupPrefab.GetComponent<PopupDialog>();
            buttonTemplate = CargoRefitUI.Read<GameObject>(popup, "buttonPrefab");
            GridUI inventory = game.UI.InventoryGrid;
            cellSource = inventory == null || inventory.gridCellPrefab == null ? null : inventory.gridCellPrefab.GetComponent<GridCell>();
            BasicButtonWrapper templateWrapper = buttonTemplate == null ? null : buttonTemplate.GetComponent<BasicButtonWrapper>();
            if (frame == null || tree == null || templateWrapper == null || templateWrapper.Button == null ||
                templateWrapper.LocalizedString == null || cellSource == null || cellSource.backplateImage == null || cellSource.fillImage == null ||
                (cellSource.backplateImage.sprite == null && cellSource.fillImage.sprite == null)) return;
            // Inactive native text can still hold its serialized English font.
            // Copy the native localization binding, never that stale font/material.
            foreach (LocalizeFontBypass binding in buttonTemplate.GetComponentsInChildren<LocalizeFontBypass>(true))
                if (binding.textField != null && !string.IsNullOrEmpty(binding.tableString) && !string.IsNullOrEmpty(binding.tableEntryString))
                { fontTable = binding.tableString; fontEntry = binding.tableEntryString; break; }
            nativeBack = CargoRefitUI.Read<DredgePlayerActionPress>(owner, "backAction");
            nativeFocus = CargoRefitUI.Read<ControllerFocusGrabber>(owner, "controllerFocusGrabber");
            if (string.IsNullOrEmpty(fontTable) || nativeBack == null || nativeFocus == null) return;
            treeFocusTarget = nativeFocus.selectThis;
            view = new DryDockViewFactory(buttonTemplate, fontTable, fontEntry);
            try
            {
                window = owner; nodes = tree.gameObject;
                entry = CreateButton(frame, "VoyageCargoEntry", "entry", "货舱整备", OpenDetails);
                Position(entry.transform as RectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(250, 44), new Vector2(-22, -8));
                content = NewRect(frame, "VoyageCargoDetails");
                content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
                content.offsetMin = new Vector2(48, 40); content.offsetMax = new Vector2(-48, -70);
                content.gameObject.SetActive(false);
                diagram = NewRect(content, "HullLayoutPreview");
                Position(diagram, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(390, 440), new Vector2(-450, -5));
                legend = CreateText(content, "PreviewLegend", CargoRefitText.Value("legend", "格位示意  ·  + 为已扩建，空框为下一次"), 22, new Vector2(-450, -260), new Vector2(460, 70), TextAlignmentOptions.Center);
                heading = CreateText(content, "Heading", CargoRefitText.Value("entry", "货舱整备"), 38, new Vector2(190, 220), new Vector2(740, 70), TextAlignmentOptions.Left);
                description = CreateText(content, "Description", "", 27, new Vector2(190, 65), new Vector2(740, 230), TextAlignmentOptions.TopLeft);
                status = CreateText(content, "Status", "", 23, new Vector2(190, -125), new Vector2(740, 90), TextAlignmentOptions.TopLeft);
                back = CreateButton(content, "VoyageCargoBack", "back", "返回升级树", CloseDetails);
                remove = CreateButton(content, "VoyageCargoRemove", "remove", "收起最后两格", () => Confirm(true));
                purchase = CreateButton(content, "VoyageCargoPurchase", "purchase", "扩建两格  ·  $200", () => Confirm(false));
                Position(back.transform as RectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(230, 54), new Vector2(-100, -235));
                Position(remove.transform as RectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(230, 54), new Vector2(155, -235));
                Position(purchase.transform as RectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(300, 54), new Vector2(445, -235));
                back.Navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = remove.Button };
                remove.Navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = back.Button, selectOnRight = purchase.Button };
                purchase.Navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = remove.Button };
                detailFocus = content.gameObject.AddComponent<ControllerFocusGrabber>();
                detailFocus.selectThis = back.Button;
                detailBack = new DredgePlayerActionPress("prompt.back", game.Input.Controls.Back)
                { showInControlArea = true, evaluateWhenPaused = true, priority = 1 };
                detailBack.OnPressComplete += CloseDetails;
                if (Jinke.DredgeMerchantTravel.Bootstrap.PainterEnabled)
                {
                    appearance = new DryDockAppearancePage(view, frame, OpenAppearance, CloseDetails, CanInteractWithAppearance);
                    Position(entry.transform as RectTransform, Vector2.one, Vector2.one, new Vector2(250, 44), new Vector2(-290, -8));
                }
                window.OnShowComplete += OnNativeShown;
                window.OnHideComplete += OnNativeHidden;
                ApplicationEvents.Instance.OnLocaleChanged += OnLocaleChanged;
                initialized = true;
                if (window.IsShowing) OnNativeShown();
            }
            catch (Exception error)
            {
                if (entry != null) Destroy(entry.gameObject);
                if (content != null) Destroy(content.gameObject);
                if (appearance != null) { appearance.Dispose(); appearance = null; }
                if (!didLogFailure) { didLogFailure = true; Debug.LogError("[VoyageCargoUI] Install: " + error); }
            }
        }

        internal void RefreshAvailability()
        {
            if (!initialized || window == null) return;
            GameManager game = GameManager.Instance;
            if (game == null || game.Input == null) { CloseSubpage(true); return; }
            CargoRefitPreview preview = CargoRefit.GetPreview();
            entry.gameObject.SetActive(preview != null);
            bool canEnter = !pages.IsOpen && CanReturnToWindow &&
                game.Input.GetActiveActionLayer() == ActionLayer.POPUP_WINDOW;
            entry.Interactable = canEnter && preview != null;
            if (appearance != null)
            {
                appearance.Entry.gameObject.SetActive(Jinke.DredgeMerchantTravel.Bootstrap.PainterEnabled);
                appearance.Entry.Interactable = canEnter;
            }
            if (!window.IsShowing) { CloseSubpage(true); return; }
            if (!pages.IsOpen) { ConnectNavigation(); return; }
            if (!ReferenceEquals(shownSave, game.SaveData) || (details && preview == null))
            { CloseSubpage(true); return; }
            if (details) RefreshDetail(preview);
            else if (appearance != null) appearance.Refresh(CanInteractWithAppearance());
        }

        private void OnNativeShown()
        {
            if (!initialized) return;
            lastStatus = null;
            RefreshAvailability();
            if (restoreFocusOnShow && CanReturnToWindow && nativeFocus != null)
            {
                restoreFocusOnShow = false;
                nativeFocus.enabled = nativeFocusWasEnabled;
            }
        }

        private void OnNativeHidden()
        {
            CloseSubpage(true); RestoreNavigation();
            if (nativeFocus != null) nativeFocus.selectThis = treeFocusTarget;
        }

        private void OnLocaleChanged(Locale locale)
        {
            if (!initialized) return;
            VoyageLocalization.Refresh();
            legend.text = CargoRefitText.Value("legend", "格位示意  ·  + 为已扩建，空框为下一次");
            SetButtonText(entry, "entry", "货舱整备");
            SetButtonText(back, "back", "返回升级树");
            if (appearance != null) appearance.Refresh(CanInteractWithAppearance());
            RefreshAvailability();
        }

        private void OpenDetails()
        {
            CargoRefitPreview preview = CargoRefit.GetPreview();
            if (preview == null || !OpenSubpage(DryDockPage.Cargo)) return;
            lastStatus = null; drawnTier = 0;
            RefreshDetail(preview);
            Select(back.Button);
        }

        private void OpenAppearance()
        {
            if (appearance == null || !Jinke.DredgeMerchantTravel.Bootstrap.PainterEnabled ||
                !OpenSubpage(DryDockPage.Appearance)) return;
            appearance.Begin();
            Select(appearance.Back.Button);
        }

        private bool OpenSubpage(DryDockPage page)
        {
            if (!initialized || !CanReturnToWindow) return false;
            GameManager game = GameManager.Instance;
            if (game == null || game.Input == null || !pages.TryOpen(page, game.SaveData, window.IsShowing,
                game.Input.GetActiveActionLayer() == ActionLayer.POPUP_WINDOW)) return false;
            shownSave = game.SaveData;
            nativeFocusWasEnabled = nativeFocus.enabled;
            nativeFocusTarget = nativeFocus.selectThis;
            try
            {
                nativeFocus.enabled = false;
                leaseInput = game.Input;
                game.Input.RemoveActionListener(new DredgePlayerActionBase[] { nativeBack }, ActionLayer.POPUP_WINDOW);
                backLease = true;
                game.Input.AddActionListener(new DredgePlayerActionBase[] { detailBack }, ActionLayer.POPUP_WINDOW);
                nodes.SetActive(false);
                content.gameObject.SetActive(page == DryDockPage.Cargo);
                if (appearance != null) appearance.Content.gameObject.SetActive(page == DryDockPage.Appearance);
                entry.Interactable = false;
                if (appearance != null) appearance.Entry.Interactable = false;
                return true;
            }
            catch { CloseSubpage(true); throw; }
        }

        private bool CanInteractWithAppearance()
        {
            GameManager game = GameManager.Instance;
            return CanReturnToWindow && pages.Current == DryDockPage.Appearance && !dialogOpen &&
                appearance != null && window != null && window.IsShowing && game != null &&
                ReferenceEquals(shownSave, game.SaveData) && game.Input != null &&
                game.Input.GetActiveActionLayer() == ActionLayer.POPUP_WINDOW;
        }

        private bool CanReturnToWindow
        {
            get
            {
                return !destroying && isActiveAndEnabled && gameObject.activeInHierarchy &&
                    window != null && window.IsShowing && window.gameObject.activeInHierarchy;
            }
        }

        private void CloseDetails()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.Input == null || game.Input.GetActiveActionLayer() != ActionLayer.POPUP_WINDOW) return;
            CloseSubpage(false);
        }

        private void CloseSubpage(bool force)
        {
            DryDockPage closing = pages.Current;
            if (!pages.Close(force) && (pages.IsOpen || !backLease)) return;
            bool interactiveReturn = CanReturnToWindow;
            BasicButtonWrapper returnEntry = closing == DryDockPage.Appearance && appearance != null ? appearance.Entry : entry;
            if (backLease && leaseInput != null)
            {
                leaseInput.RemoveActionListener(new DredgePlayerActionBase[] { detailBack }, ActionLayer.POPUP_WINDOW);
                if (interactiveReturn)
                    leaseInput.AddActionListener(new DredgePlayerActionBase[] { nativeBack }, ActionLayer.POPUP_WINDOW);
            }
            backLease = false;
            leaseInput = null;
            if (content != null) content.gameObject.SetActive(false);
            if (appearance != null && appearance.Content != null) appearance.Content.gameObject.SetActive(false);
            if (nodes != null) nodes.SetActive(true);
            if (nativeFocus != null)
            {
                nativeFocus.selectThis = interactiveReturn && returnEntry != null ? returnEntry.Button : nativeFocusTarget;
                if (interactiveReturn)
                {
                    nativeFocus.enabled = nativeFocusWasEnabled;
                    StartCoroutine(RestoreNativeFocusTarget(returnEntry == null ? null : returnEntry.Button, pages.Generation));
                }
                else
                {
                    // Enabling a focus grabber can itself start a selection
                    // coroutine. Defer it until the next visible native Show.
                    restoreFocusOnShow = !destroying && nativeFocusWasEnabled;
                }
            }
            shownSave = null;
            if (interactiveReturn)
            { RefreshAvailability(); if (returnEntry != null) Select(returnEntry.Button); }
        }

        private void RefreshDetail(CargoRefitPreview preview)
        {
            heading.text = CargoRefitText.Format("heading-level", "货舱整备  ·  {0}/{1}", "Cargo refit  ·  {0}/{1}", preview.Level, preview.MaxLevel);
            description.text = CargoRefitText.Format("description-level",
                "已增加 {0} 个普通货物格。\n每次扩建两格，价格逐次增加 $200。\n\n船体升级后保留额外格位。\n收起最后两格前，请先清空；费用不退还。",
                "{0} general cargo slots added.\nEach refit adds two slots; its price rises by $200.\n\nHull upgrades retain the extra capacity.\nEmpty the last pair before removal. Fees are not refunded.", preview.AddedCells);
            SetButtonText(purchase, "purchase-step", preview.Level < preview.MaxLevel
                ? "扩建两格  ·  $" + preview.Cost.ToString("0") : "已达整备上限",
                preview.Level < preview.MaxLevel ? "Add two slots  ·  $" + preview.Cost.ToString("0") : "Fully refitted");
            SetButtonText(remove, "remove", "收起最后两格");
            string reason, removeReason;
            bool possible = CargoRefit.CanPurchase(out reason);
            bool removable = CargoRefit.CanRemove(out removeReason);
            purchase.Interactable = possible && !dialogOpen;
            remove.Interactable = removable && !dialogOpen;
            back.Interactable = !dialogOpen;
            string notice = (!possible && preview.Level < preview.MaxLevel ? reason : null) ??
                (preview.Owned && !removable ? removeReason : null) ?? lastStatus ?? reason ?? "整备结果会随当前航行进度保存。";
            status.text = CargoRefitText.Message(notice);
            status.color = GameManager.Instance.LanguageManager.GetColor(possible || removable ? DredgeColorTypeEnum.NEUTRAL : DredgeColorTypeEnum.WARNING);
            // A disabled removal button must not trap keyboard focus on level zero.
            back.Navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = removable ? remove.Button : purchase.Button };
            purchase.Navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = removable ? remove.Button : back.Button };
            remove.Navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = back.Button, selectOnRight = possible ? purchase.Button : back.Button };
            if (drawnTier != preview.HullTier || drawnLevel != preview.Level) DrawLayout(preview);
        }

        private void Confirm(bool removing)
        {
            GameManager owner = GameManager.Instance;
            if (dialogOpen || !details || window == null || !window.IsShowing || owner == null || owner.Input == null ||
                owner.Input.GetActiveActionLayer() != ActionLayer.POPUP_WINDOW || !ReferenceEquals(shownSave, owner.SaveData)) return;
            CargoRefitPreview preview = CargoRefit.GetPreview();
            if (preview == null) return;
            string reason;
            bool possible = removing ? CargoRefit.CanRemove(out reason) : CargoRefit.CanPurchase(out reason);
            if (!possible) { lastStatus = reason; RefreshDetail(preview); return; }
            int shownLevel = preview.Level;
            int pageGeneration = pages.Generation;
            SaveData confirmingSave = shownSave;
            string key = CargoRefitText.Put(removing ? "confirm-remove-step" : "confirm-purchase-step", removing
                ? "收起最后扩建的两个空格？\n\n已付费用不退还。更早扩建的格位与原有物品保持原样。"
                : "支付 $" + preview.Cost.ToString("0") + "，再增加两个普通货物格？\n\n本次完成后共增加 " + (preview.AddedCells + 2) + " 格。费用随整备次数增加。",
                removing ? "Remove the last two added slots?\n\nPaid fees are not refunded. Earlier refits and existing items remain."
                : "Pay $" + preview.Cost.ToString("0") + " to add two general cargo slots?\n\nTotal extra slots after this refit: " + (preview.AddedCells + 2) + ". The price rises with each refit.");
            dialogOpen = true; purchase.Interactable = false; remove.Interactable = false;
            var options = new DialogOptions
            {
                text = key, disableGameCanvas = false, showScrim = true,
                buttonOptions = new[]
                {
                    new DialogButtonOptions { id = 0, buttonString = "prompt.cancel", hideOnButtonPress = true, isBackOption = true },
                    new DialogButtonOptions { id = 1, buttonString = "prompt.confirm", hideOnButtonPress = true }
                }
            };
            try
            {
                GameManager.Instance.DialogManager.ShowDialog(options, result =>
                {
                    if (this == null || !pages.Matches(DryDockPage.Cargo, confirmingSave, pageGeneration)) return;
                    dialogOpen = false;
                    GameManager game = GameManager.Instance;
                    if (game == null || !details || !ReferenceEquals(shownSave, game.SaveData)) return;
                    game.Input.SetActiveActionLayer(ActionLayer.POPUP_WINDOW);
                    if (result.id == 1)
                    {
                        string error;
                        CargoRefitPreview current = CargoRefit.GetPreview();
                        if (current == null || current.Level != shownLevel) lastStatus = "整备状态已变化，请重新确认。";
                        else
                        {
                            bool completed = removing ? CargoRefit.TryRemove(out error) : CargoRefit.Purchase(out error);
                            lastStatus = completed ? (removing ? "已收起最后两个空格，费用不退还。" : "整备完成，新增货物格已开放。") : error;
                        }
                    }
                    RefreshAvailability();
                    // Re-enable our focus grabber after the native dialog has
                    // temporarily removed its controller-change subscription.
                    detailFocus.enabled = false;
                    detailFocus.enabled = true;
                    Select(back.Button);
                });
            }
            catch (Exception error)
            {
                dialogOpen = false; lastStatus = "确认界面暂时不可用，未进行改装。";
                GameManager.Instance.Input.SetActiveActionLayer(ActionLayer.POPUP_WINDOW);
                Debug.LogError("[VoyageCargoUI] Dialog: " + error);
                RefreshAvailability();
            }
        }

        private void DrawLayout(CargoRefitPreview preview)
        {
            foreach (Transform child in diagram) Destroy(child.gameObject);
            GridCellData[,] cells = GameManager.Instance.SaveData.Inventory.Grid;
            int width = cells.GetLength(0), height = cells.GetLength(1);
            float size = Mathf.Min(34f, Mathf.Min(360f / width, 420f / height));
            for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
            {
                GridCellData cell = cells[x, y];
                bool added = false, next = false;
                foreach (Vector2Int point in preview.Cells) if (point.x == x && point.y == y) added = true;
                foreach (Vector2Int point in preview.NextCells) if (point.x == x && point.y == y) next = true;
                if (cell.IsHidden && !added && !next) continue;
                RectTransform square = NewRect(diagram, "Cell-" + x + "-" + y);
                Position(square, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(size - 2, size - 2),
                    new Vector2((x - (width - 1) * .5f) * size, ((height - 1) * .5f - y) * size));
                Color tint = GameManager.Instance.LanguageManager.GetColor(added || next ? DredgeColorTypeEnum.POSITIVE : DredgeColorTypeEnum.NEUTRAL);
                tint.a = next ? .35f : added ? 1f : .6f;
                Image image = square.gameObject.AddComponent<Image>();
                image.sprite = cellSource.backplateImage.sprite != null ? cellSource.backplateImage.sprite : cellSource.fillImage.sprite;
                image.type = cellSource.backplateImage.type; image.color = tint; image.raycastTarget = false;
                if (added) CreateText(square, "Added", "+", 24, Vector2.zero, new Vector2(size, size), TextAlignmentOptions.Center);
                else if (next) CreateText(square, "Next", "□", 24, Vector2.zero, new Vector2(size, size), TextAlignmentOptions.Center);
                else
                {
                    Sprite icon = cell.acceptedItemSubtype.HasFlag(ItemSubtype.ENGINE) ? cellSource.engineSprite :
                        cell.acceptedItemSubtype.HasFlag(ItemSubtype.ROD) ? cellSource.tackleSprite :
                        cell.acceptedItemSubtype.HasFlag(ItemSubtype.LIGHT) ? cellSource.lightSprite :
                        cell.acceptedItemSubtype.HasFlag(ItemSubtype.NET) ? cellSource.trawlSprite : null;
                    if (icon != null)
                    {
                        RectTransform iconRect = NewRect(square, "Equipment");
                        Position(iconRect, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(size * .6f, size * .6f), Vector2.zero);
                        Image equipment = iconRect.gameObject.AddComponent<Image>(); equipment.sprite = icon; equipment.color = tint; equipment.raycastTarget = false;
                    }
                }
            }
            drawnTier = preview.HullTier; drawnLevel = preview.Level;
        }

        private void ConnectNavigation()
        {
            if (pages.IsOpen || nodes == null) return;
            RestoreNavigation();
            var entries = new List<BasicButtonWrapper>();
            if (entry != null && entry.gameObject.activeInHierarchy && entry.Interactable) entries.Add(entry);
            if (appearance != null && appearance.Entry.gameObject.activeInHierarchy && appearance.Entry.Interactable) entries.Add(appearance.Entry);
            if (entries.Count == 0) return;
            Selectable nearest = null; float highest = float.NegativeInfinity;
            foreach (UpgradeNodeUI node in nodes.GetComponentsInChildren<UpgradeNodeUI>(false))
            {
                Button button = node.BasicButtonWrapper.Button;
                if (!button.IsInteractable()) continue;
                if (button.transform.position.y > highest) { highest = button.transform.position.y; nearest = button; }
            }
            if (nearest != null)
            {
                previousNavigation[nearest] = nearest.navigation;
                Navigation link = nearest.navigation; link.mode = Navigation.Mode.Explicit;
                link.selectOnUp = entries[0].Button; nearest.navigation = link;
            }
            for (int i = 0; i < entries.Count; i++)
                entries[i].Navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? entries[i - 1].Button : nearest,
                    selectOnRight = i + 1 < entries.Count ? entries[i + 1].Button : nearest,
                    selectOnDown = nearest, selectOnUp = nearest };
            // At the final hull tier every native node may be disabled. Keep a
            // live default target for controller activation/input-mode changes.
            if (nativeFocus != null && (nativeFocus.selectThis == null ||
                !nativeFocus.selectThis.IsInteractable() || !nativeFocus.selectThis.gameObject.activeInHierarchy))
                nativeFocus.selectThis = nearest ?? entries[0].Button;
            if (EventSystem.current != null)
            {
                GameObject current = EventSystem.current.currentSelectedGameObject;
                Selectable selected = current == null ? null : current.GetComponent<Selectable>();
                if (selected == null || !selected.IsInteractable() || !selected.gameObject.activeInHierarchy)
                    Select(nearest ?? entries[0].Button);
            }
        }

        private void RestoreNavigation()
        {
            foreach (KeyValuePair<Selectable, Navigation> item in previousNavigation) if (item.Key != null) item.Key.navigation = item.Value;
            previousNavigation.Clear();
        }

        private BasicButtonWrapper CreateButton(Transform parent, string name, string key, string text, Action clicked)
        {
            BasicButtonWrapper button = view.Button(parent, name, "cargo." + key, text, text, clicked);
            SetButtonText(button, key, text);
            return button;
        }

        private static void SetButtonText(BasicButtonWrapper button, string key, string value)
        {
            string entry = CargoRefitText.Put(key, value);
            button.LocalizedString.StringReference.SetReference(LanguageManager.STRING_TABLE, entry);
            button.LocalizedString.StringReference.Arguments = null;
            button.LocalizedString.RefreshString();
        }

        private static void SetButtonText(BasicButtonWrapper button, string key, string zh, string en)
        {
            button.LocalizedString.StringReference = VoyageLocalization.Text("cargo." + key, zh, en);
            button.LocalizedString.StringReference.Arguments = null;
            button.LocalizedString.RefreshString();
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize, Vector2 position, Vector2 size, TextAlignmentOptions alignment)
        { return view.Text(parent, name, text, fontSize, position, size, alignment); }

        private static RectTransform NewRect(Transform parent, string name)
        { return DryDockViewFactory.Rect(parent, name); }

        private static void Position(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
        { DryDockViewFactory.Position(rect, anchor, pivot, size, position); }

        private static void Select(Selectable selectable)
        { if (selectable != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(selectable.gameObject); }

        private IEnumerator RestoreNativeFocusTarget(Selectable returnTarget, int generation)
        {
            // Native focus selects after EndOfFrame. Keep the entry as its target
            // for that selection, then preserve the original tree's next-open target.
            yield return null;
            if (!pages.IsOpen && pages.Generation == generation && nativeFocus != null &&
                nativeFocus.selectThis == returnTarget) nativeFocus.selectThis = nativeFocusTarget;
        }

        private void OnDisable()
        {
            if (!initialized) return;
            CloseSubpage(true); RestoreNavigation();
        }

        private void OnDestroy()
        {
            destroying = true;
            CloseSubpage(true); RestoreNavigation();
            if (initialized && ApplicationEvents.Instance != null) ApplicationEvents.Instance.OnLocaleChanged -= OnLocaleChanged;
            if (window != null) { window.OnShowComplete -= OnNativeShown; window.OnHideComplete -= OnNativeHidden; }
            if (appearance != null) appearance.Dispose();
        }
    }

    internal static class CargoRefitText
    {
        internal static string Value(string suffix, string value)
        { return LocalizationSettings.StringDatabase.GetLocalizedString(LanguageManager.STRING_TABLE, Put(suffix, value)); }

        internal static string Format(string suffix, string zh, string en, params object[] values)
        {
            // Native GetLocalizedString formats non-Smart entries even with
            // an empty argument list. Insert completed text, not placeholders.
            return LocalizationSettings.StringDatabase.GetLocalizedString(LanguageManager.STRING_TABLE,
                Put(suffix, string.Format(System.Globalization.CultureInfo.CurrentCulture, zh, values),
                    string.Format(System.Globalization.CultureInfo.CurrentCulture, en, values)));
        }

        internal static string Put(string suffix, string zh, string en)
        { return VoyageLocalization.Key("cargo." + suffix, zh, en); }

        internal static string Message(string value)
        {
            string suffix;
            switch (value)
            {
                case "整备结果会随当前航行进度保存。": suffix = "status-save"; break;
                case "已恢复原布局。": suffix = "status-removed"; break;
                case "整备完成，新增货物格已开放。": suffix = "status-purchased"; break;
                case "确认界面暂时不可用，未进行改装。": suffix = "status-dialog-unavailable"; break;
                case "当前存档不支持货舱改装。": suffix = "status-save-unsupported"; break;
                case "已完成货舱改装。": suffix = "status-already-owned"; break;
                case "货舱整备进行中。": suffix = "status-busy"; break;
                case "货舱已达到整备上限。": suffix = "status-max"; break;
                case "资金不足，无法继续整备。": suffix = "status-funds-step"; break;
                case "请先清空最后整备的两个货物格并修复损伤。": suffix = "status-clear-last"; break;
                case "货舱整备记录暂不支持当前版本。": suffix = "status-version"; break;
                case "整备状态已变化，请重新确认。": suffix = "status-changed"; break;
                case "已收起最后两个空格，费用不退还。": suffix = "status-removed-step"; break;
                case "需要 $200。": suffix = "status-funds"; break;
                case "新增位置已被占用，暂时不能改装。": suffix = "status-position-occupied"; break;
                case "尚未改装货舱。": suffix = "status-not-owned"; break;
                case "请先清空新增的两个货物格并修复损伤。": suffix = "status-clear-slots"; break;
                case "请靠港并放下手中物品后改装。": suffix = "status-dock"; break;
                case "请先修复船体损伤。": suffix = "status-repair"; break;
                case "请先取出溢出仓库中的物品。": suffix = "status-overflow"; break;
                case "当前船体布局暂不支持这项改装。": suffix = "status-layout-unsupported"; break;
                default: return value;
            }
            return Value(suffix, value);
        }

        internal static string Put(string suffix, string value)
        {
            string key = "cargo." + suffix;
            string english;
            switch (suffix)
            {
                case "entry": english = "Cargo refit"; break;
                case "back": english = "Back to upgrades"; break;
                case "purchase": english = "Add two slots  ·  $200"; break;
                case "remove": english = "Remove last two slots"; break;
                case "legend": english = "Hull layout  ·  + added, outline next"; break;
                case "heading-owned": english = "Cargo refit · Complete"; break;
                case "description": english = "Add 2 general cargo slots  ·  $200\n\nEngine, rod, light and net slots remain unchanged.\nLater hull upgrades retain these two extra cargo slots."; break;
                case "description-owned": english = "2 general cargo slots have been added.\n\nEquipment slots and hull upgrades are unchanged.\nEmpty the extra slots and repair all damage before restoring the layout. The $200 fee is not refunded."; break;
                case "status-save": english = "The refit is saved with your current progress."; break;
                case "status-removed": english = "Original layout restored."; break;
                case "status-purchased": english = "Refit complete. The extra cargo slots are ready."; break;
                case "status-dialog-unavailable": english = "Confirmation is unavailable. No refit was made."; break;
                case "status-save-unsupported": english = "This save does not support cargo refits."; break;
                case "status-already-owned": english = "This cargo refit is already complete."; break;
                case "status-busy": english = "A cargo refit is already in progress."; break;
                case "status-max": english = "The cargo hold is fully refitted."; break;
                case "status-funds-step": english = "Insufficient funds for the next refit."; break;
                case "status-clear-last": english = "Empty the last two added slots and repair all damage first."; break;
                case "status-version": english = "This cargo refit record is not supported by this version."; break;
                case "status-changed": english = "The refit state changed. Please confirm again."; break;
                case "status-removed-step": english = "The last two slots were removed. Fees were not refunded."; break;
                case "status-funds": english = "$200 required."; break;
                case "status-position-occupied": english = "The extra positions are occupied. Refit is unavailable."; break;
                case "status-not-owned": english = "The cargo hold has not been refitted."; break;
                case "status-clear-slots": english = "Empty both extra cargo slots and repair the damage first."; break;
                case "status-dock": english = "Dock and put down any held item before refitting."; break;
                case "status-repair": english = "Repair the hull damage first."; break;
                case "status-overflow": english = "Remove all items from overflow storage first."; break;
                case "status-layout-unsupported": english = "This hull layout does not support the refit."; break;
                case "confirm-purchase": english = "Pay $200 to add two general cargo slots?\n\nEquipment slots are unchanged. Future hull upgrades retain the extra capacity."; break;
                case "confirm-remove": english = "Restore the original cargo layout?\n\nOnly the two empty extra slots are removed. The $200 fee is not refunded. Native hull and equipment upgrades remain."; break;
                default: english = value; break;
            }
            return VoyageLocalization.Key(key, value, english);
        }
    }
}
