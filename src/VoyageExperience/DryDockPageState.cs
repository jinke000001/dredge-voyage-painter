namespace Jinke.DredgeVoyage
{
    internal enum DryDockPage { Upgrades, Cargo, Appearance }

    // One owner for both subpages. A modal confirmation cannot be displaced by
    // another page, and callbacks from a former page generation cannot commit.
    internal sealed class DryDockPageState
    {
        internal DryDockPage Current { get; private set; }
        internal object Session { get; private set; }
        internal bool ModalOpen { get; private set; }
        internal int Generation { get; private set; }
        internal bool IsOpen { get { return Current != DryDockPage.Upgrades; } }

        internal bool TryOpen(DryDockPage page, object session, bool visible, bool inputAvailable)
        {
            if ((page != DryDockPage.Cargo && page != DryDockPage.Appearance) ||
                IsOpen || ModalOpen || session == null || !visible || !inputAvailable) return false;
            Current = page;
            Session = session;
            Generation++;
            return true;
        }

        internal bool Matches(DryDockPage page, object session, int generation)
        { return Current == page && ReferenceEquals(Session, session) && Generation == generation; }

        internal bool SetModal(bool open)
        {
            if (open && Current != DryDockPage.Cargo) return false;
            ModalOpen = open;
            return true;
        }

        internal bool Close(bool force)
        {
            if (!IsOpen || (ModalOpen && !force)) return false;
            Current = DryDockPage.Upgrades;
            Session = null;
            ModalOpen = false;
            Generation++;
            return true;
        }
    }
}
