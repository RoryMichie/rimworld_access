using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin bridge for the windowless bills menu (a crafting station's bill list):
    /// <see cref="IsActive"/> is the flag <see cref="BillsScopeMirror"/> watches to
    /// push/pop <see cref="BillsScope"/> (the menu's entire row-building, cursor,
    /// typeahead, and announcement implementation now lives on that scope, per the
    /// table-model migration playbook, following the PawnAreaMenuState/AssignMenuState
    /// precedent). This class only
    /// owns what code OUTSIDE the scope reads/calls: the two inspect-tab-adjacent
    /// open call sites (<see cref="Adapters.BillsAdapter"/> and
    /// <see cref="BillConfigState"/>'s own reopen-on-return path), <see cref="BillGiver"/>/
    /// <see cref="BillGiverPos"/> (read once by the scope's OnPush), and two external
    /// pokes <see cref="BillConfigState"/> makes into the still-open menu:
    /// <see cref="RefreshMenuItems"/> (resync labels after e.g. a rename, without
    /// disturbing the cursor) and <see cref="Reannounce"/> (the "which windowless
    /// menu currently owns the screen" ladder in <see cref="InspectionReturnHelper"/>).
    /// </summary>
    public static class BillsMenuState
    {
        public static bool IsActive { get; private set; }

        /// <summary>The work table (or other bill giver) this menu is editing. Set by <see cref="Open"/>; read once by the scope on push.</summary>
        public static IBillGiver BillGiver { get; private set; }

        /// <summary>The bill giver's map position, threaded through to <see cref="BillConfigState.Open"/>.</summary>
        public static IntVec3 BillGiverPos { get; private set; }

        public static void Open(IBillGiver giver, IntVec3 position)
        {
            if (giver == null)
            {
                Log.Error("Cannot open bills menu: giver is null");
                return;
            }

            BillGiver = giver;
            BillGiverPos = position;
            IsActive = true;
        }

        public static void Close()
        {
            IsActive = false;
            BillGiver = null;
        }

        /// <summary>
        /// Rebuilds the menu rows (labels re-derived from live bill/recipe data)
        /// while preserving the cursor's INDEX (not identity) — called externally
        /// when bill data changes without a reorder (e.g. after a rename via
        /// <see cref="BillConfigState"/>). A no-op while the menu isn't open.
        /// </summary>
        public static void RefreshMenuItems()
        {
            if (!IsActive) return;
            BillsScopeMirror.RefreshRows();
        }

        /// <summary>Re-announces the current row; consulted by <see cref="InspectionReturnHelper"/>.</summary>
        public static void Reannounce()
        {
            if (!IsActive) return;
            BillsScopeMirror.ReannounceCurrent();
        }
    }
}
