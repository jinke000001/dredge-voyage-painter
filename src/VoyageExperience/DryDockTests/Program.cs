using System;
using Jinke.DredgeVoyage;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }

    private static void Main()
    {
        var firstSave = new object();
        var secondSave = new object();
        var pages = new DryDockPageState();
        Check(!pages.IsOpen && pages.Current == DryDockPage.Upgrades, "Starts at the native tree");
        Check(!pages.TryOpen(DryDockPage.Upgrades, firstSave, true, true), "Native tree cannot lease a subpage");
        Check(!pages.TryOpen(DryDockPage.Cargo, null, true, true), "A save session is required");
        Check(!pages.TryOpen(DryDockPage.Cargo, firstSave, false, true), "Hidden window cannot open a subpage");
        Check(!pages.TryOpen(DryDockPage.Appearance, firstSave, true, false), "Another input layer cannot open a subpage");
        Check(pages.TryOpen(DryDockPage.Cargo, firstSave, true, true), "Cargo opens with one ownership lease");
        int firstGeneration = pages.Generation;
        Check(pages.IsOpen && pages.Current == DryDockPage.Cargo, "Cargo owns nodes, back and focus");
        Check(!pages.TryOpen(DryDockPage.Appearance, firstSave, true, true), "Appearance cannot steal cargo ownership");
        Check(!pages.TryOpen(DryDockPage.Cargo, firstSave, true, true), "Repeated refresh cannot acquire a second lease");
        Check(pages.Matches(DryDockPage.Cargo, firstSave, firstGeneration), "Current transaction belongs to its page and save");
        Check(!pages.Matches(DryDockPage.Cargo, secondSave, firstGeneration), "A callback cannot cross save slots");
        Check(pages.SetModal(true), "Cargo confirmation blocks navigation");
        Check(!pages.Close(false) && pages.Current == DryDockPage.Cargo && pages.ModalOpen, "Back cannot dismiss the underlying page during confirmation");
        Check(!pages.TryOpen(DryDockPage.Appearance, firstSave, true, true), "A modal cannot be displaced by appearance");
        Check(pages.Close(true), "Window hide or scene cleanup releases the modal page");
        Check(!pages.IsOpen && !pages.ModalOpen && pages.Session == null, "Cleanup releases ownership and save references");
        Check(!pages.Matches(DryDockPage.Cargo, firstSave, firstGeneration), "Late confirmation after close is rejected");
        Check(!pages.Close(true), "Repeated cleanup does not duplicate a return lease");
        Check(pages.TryOpen(DryDockPage.Appearance, secondSave, true, true), "Appearance opens after cargo cleanup");
        int appearanceGeneration = pages.Generation;
        Check(pages.Current == DryDockPage.Appearance, "Appearance owns focus and back exclusively");
        Check(!pages.SetModal(true), "Appearance cannot acquire cargo confirmation state");
        Check(!pages.TryOpen(DryDockPage.Cargo, secondSave, true, true), "Cargo cannot steal appearance ownership");
        Check(pages.Close(false), "Back returns from appearance to the tree");
        Check(pages.TryOpen(DryDockPage.Cargo, firstSave, true, true), "Cargo can reopen normally");
        Check(!pages.Matches(DryDockPage.Cargo, firstSave, firstGeneration), "Reopening the same page does not revive its old callback");
        Check(!pages.Matches(DryDockPage.Appearance, secondSave, appearanceGeneration), "Appearance callbacks cannot act on cargo");
        Check(pages.SetModal(true) && pages.SetModal(false) && pages.Close(false), "Cancel confirmation then back restores the tree");
        Console.WriteLine("DryDock shared-page state: " + checks + " checks passed (offline; no Unity input or rendering).");
    }
}
