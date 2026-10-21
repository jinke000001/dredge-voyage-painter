using System;
using Jinke.DredgeShipAppearance;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jinke.DredgeVoyage
{
    internal sealed class DryDockAppearancePage
    {
        internal BasicButtonWrapper Entry { get; private set; }
        internal RectTransform Content { get; private set; }
        internal BasicButtonWrapper Back { get; private set; }
        internal ControllerFocusGrabber Focus { get; private set; }
        private BasicButtonWrapper original;
        private BasicButtonWrapper faceted;
        private BasicButtonWrapper cthulhu;
        private BasicButtonWrapper cyberpunk;
        private TextMeshProUGUI heading;
        private TextMeshProUGUI description;
        private TextMeshProUGUI status;
        private readonly Func<bool> canInteract;
        private bool rejected;

        internal DryDockAppearancePage(DryDockViewFactory view, Transform frame,
            Action open, Action close, Func<bool> canInteract)
        {
            this.canInteract = canInteract;
            try
            {
                Entry = view.Button(frame, "VoyageShipAppearanceEntry", "ship.entry", "船型外观", "Boat appearance", open);
                DryDockViewFactory.Position(Entry.transform as RectTransform, Vector2.one, Vector2.one,
                    new Vector2(250, 44), new Vector2(-22, -8));
                Content = DryDockViewFactory.Rect(frame, "VoyageShipAppearanceDetails");
                Content.anchorMin = Vector2.zero; Content.anchorMax = Vector2.one;
                Content.offsetMin = new Vector2(48, 40); Content.offsetMax = new Vector2(-48, -70);
                Content.gameObject.SetActive(false);
                heading = view.Text(Content, "Heading", "", 38, new Vector2(-420, 210), new Vector2(520, 70), TextAlignmentOptions.Left);
                cthulhu = view.Button(Content, "CthulhuTrawler", "ship.cthulhu", "深海异教渔船", "Abyssal cult trawler", () => Apply(ShipAppearance.Cthulhu));
                cyberpunk = view.Button(Content, "CyberpunkTrawler", "ship.cyberpunk", "赛博改装渔船", "Cyberpunk trawler", () => Apply(ShipAppearance.Cyberpunk));
                faceted = view.Button(Content, "FacetedTrawler", "ship.faceted", "折线渔舟", "Faceted trawler", () => Apply(ShipAppearance.Faceted));
                original = view.Button(Content, "OriginalBoat", "ship.original", "原版", "Original", () => Apply(ShipAppearance.Original));
                Back = view.Button(Content, "ShipAppearanceBack", "ship.back", "返回升级树", "Back to upgrades", close);
                Place(cthulhu, -420, 140, 420); Place(cyberpunk, -420, 70, 420);
                Place(faceted, -420, 0, 420); Place(original, -420, -70, 420); Place(Back, -420, -235, 260);
                description = view.Text(Content, "Description", "", 27, new Vector2(230, 75), new Vector2(650, 280), TextAlignmentOptions.TopLeft);
                status = view.Text(Content, "Status", "", 24, new Vector2(230, -140), new Vector2(650, 150), TextAlignmentOptions.TopLeft);
                Focus = Content.gameObject.AddComponent<ControllerFocusGrabber>();
                Focus.selectThis = Back.Button;
                Refresh(false);
            }
            catch { Dispose(); throw; }
        }

        internal void Begin() { rejected = false; Refresh(true); }

        internal void Refresh(bool interactive)
        {
            heading.text = Text("heading", "船型外观", "Boat appearance");
            description.text = Text("description-authored-v1", "为五级船体选择外观。\n\n深海异教：角冠、骨肋与幽绿眼徽。\n赛博改装：分段装甲与青色灯带。\n\n保留原有装备和船漆，包含四档伤态。装饰发光跟随原船灯。",
                "Choose an appearance for a tier-five hull.\n\nAbyssal cult: bone ribs, crown and green eye.\nCyberpunk: segmented armor and cyan strips.\n\nRetains equipment and paint, with four damage appearances. Decorative glow follows your boat lights.");
            DryDockViewFactory.SetButtonText(Entry, "ship.entry", "船型外观", "Boat appearance");
            DryDockViewFactory.SetButtonText(Back, "ship.back", "返回升级树", "Back to upgrades");
            DryDockViewFactory.SetButtonText(original, "ship.original", "原版", "Original");
            DryDockViewFactory.SetButtonText(cthulhu, "ship.cthulhu", "深海异教渔船", "Abyssal cult trawler");
            DryDockViewFactory.SetButtonText(cyberpunk, "ship.cyberpunk", "赛博改装渔船", "Cyberpunk trawler");
            bool retry = ShipAppearance.IsBoat5 && !ShipAppearance.Ready;
            DryDockViewFactory.SetButtonText(faceted, retry ? "ship.faceted-retry" : "ship.faceted",
                retry ? "重试折线渔舟" : "折线渔舟", retry ? "Retry faceted trawler" : "Faceted trawler");
            original.Interactable = interactive && ShipAppearance.IsBoat5;
            // Automatic reconciliation is deliberately bounded after a resource
            // failure. An explicit choice must remain available to retry it.
            faceted.Interactable = interactive && ShipAppearance.IsBoat5;
            cthulhu.Interactable = cyberpunk.Interactable = interactive && ShipAppearance.IsBoat5;
            Back.Interactable = interactive;
            if (!ShipAppearance.IsBoat5)
                status.text = Text("tier-only", "需要五级船体。当前船只保持原版。", "A tier-five hull is required. Your current boat remains unchanged.");
            else if (rejected)
                status.text = Text("rejected", "未能应用这次选择，已保留此前外观。请稍后重试。", "This choice could not be applied. The previous appearance has been kept. Please try again later.");
            else if (ShipAppearance.HasUnknownSelection)
                status.text = Text("unknown", "保存的外观暂不受支持，当前显示原版。原选择已保留；选择“原版”可清除。", "The saved appearance is unsupported. The original boat is shown and the saved choice is kept. Choose Original to clear it.");
            else if (!ShipAppearance.Ready)
                status.text = Text("unavailable-authored-v1", "外观暂不可用，当前显示原版。保存的选择会保留；可重新选择外观重试，或选择“原版”恢复。", "The appearance is unavailable. The original boat is shown and the saved choice is kept. Select an appearance to retry, or restore Original.");
            else
                status.text = SelectedStatus();
            status.color = GameManager.Instance.LanguageManager.GetColor(
                rejected || !ShipAppearance.IsBoat5 || !ShipAppearance.Ready || ShipAppearance.HasUnknownSelection
                    ? DredgeColorTypeEnum.WARNING : DredgeColorTypeEnum.NEUTRAL);
            Selectable choice = cthulhu.Interactable ? cthulhu.Button : Back.Button;
            BasicButtonWrapper[] choices = { cthulhu, cyberpunk, faceted, original };
            for (int i = 0; i < choices.Length; i++)
                choices[i].Navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = i == 0 ? Back.Button : choices[i - 1].Button,
                    selectOnDown = i == choices.Length - 1 ? Back.Button : choices[i + 1].Button,
                    selectOnRight = Back.Button };
            Back.Navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = original.Interactable ? original.Button : Back.Button,
                selectOnDown = choice, selectOnLeft = choice };
        }

        private static string SelectedStatus()
        {
            if (ShipAppearance.SelectedId == ShipAppearance.Cthulhu)
                return Text("selected-cthulhu", "当前外观：深海异教渔船\n选择随当前航行进度保存。", "Current appearance: Abyssal cult trawler\nSaved with your current progress.");
            if (ShipAppearance.SelectedId == ShipAppearance.Cyberpunk)
                return Text("selected-cyberpunk", "当前外观：赛博改装渔船\n选择随当前航行进度保存。", "Current appearance: Cyberpunk trawler\nSaved with your current progress.");
            return ShipAppearance.SelectedId == ShipAppearance.Faceted
                ? Text("selected-faceted", "当前外观：折线渔舟\n选择随当前航行进度保存。", "Current appearance: Faceted trawler\nSaved with your current progress.")
                : Text("selected-original", "当前外观：原版\n选择随当前航行进度保存。", "Current appearance: Original\nSaved with your current progress.");
        }

        private void Apply(string id)
        {
            if (!canInteract()) return;
            string error;
            rejected = !ShipAppearance.Change(id, out error);
            Refresh(true);
        }

        internal void Dispose()
        {
            if (Entry != null) UnityEngine.Object.Destroy(Entry.gameObject);
            if (Content != null) UnityEngine.Object.Destroy(Content.gameObject);
        }

        private static string Text(string key, string zh, string en)
        { return DryDockViewFactory.Value("ship." + key, zh, en); }

        private static void Place(BasicButtonWrapper button, float x, float y, float width)
        { DryDockViewFactory.Position(button.transform as RectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(width, 54), new Vector2(x, y)); }
    }
}
