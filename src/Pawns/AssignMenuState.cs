using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin bridge for the colonist assignment table: <see cref="IsActive"/> is the flag
    /// <see cref="RimWorldAccess.Shell.AssignMenuScopeMirror"/> watches to push/pop
    /// <see cref="RimWorldAccess.Shell.AssignMenuScope"/> (the table's entire
    /// keyboard/row/column/submenu/paint/typeahead/policy-editor implementation now lives on that
    /// scope, per the table-model migration, following the AnimalsMenuState precedent). This class only
    /// owns what code OUTSIDE the scope reads:
    /// <see cref="AssignWindowInterceptPatch"/>'s window interception and the
    /// F3 ambient opener (<c>MapScope.QuickInfo</c>), the map-ambient guards
    /// (<c>ShellGuards</c>, <c>MapNavigationPatch</c>), and
    /// <see cref="AnnounceCurrentCell"/>, which <c>FloatMenuOverlayScope</c>'s
    /// Escape handler calls (grepped, load-bearing) to re-announce the assign
    /// cell after closing the <c>]</c> context menu — kept working here without
    /// editing that file.
    /// </summary>
    public static class AssignMenuState
    {
        public static bool IsActive { get; private set; } = false;

        public static void Open()
        {
            if (IsActive)
                return;
            if (!GuardHelper.RequireMap())
                return;

            // Match vanilla MainTabWindow_Assign: all maps + caravans +
            // travelling transporters, free colonists, no babies. The scope's
            // own RefreshContent rebuilds the real roster; this is only the
            // pre-open refusal check (matches AnimalsMenuState's pattern) so
            // the scope never gets pushed for an empty roster.
            bool anyColonists = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists
                .Any(p => !p.DevelopmentalStage.Baby());
            if (!anyColonists)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.NoColonists".Loc());
                return;
            }

            IsActive = true;
        }

        public static void Close()
        {
            IsActive = false;

            SoundDefOf.TabClose.PlayOneShotOnCamera();
            string tabLabel = DefDatabase<MainButtonDef>.GetNamed("Assign").LabelCap;
            TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.Closed".Loc(tabLabel));
            Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Assign);
        }

        /// <summary>
        /// Silent deactivation for MainTabWindowLink's reconcile: the vanilla
        /// tab window went away (a mouse click, another tab opening), so the
        /// screen goes with it. Close's sound and announcement belong to the
        /// user's own Escape.
        /// </summary>
        internal static void CloseSilent()
        {
            IsActive = false;
        }

        /// <summary>
        /// Bridge for FloatMenuOverlayScope's Escape re-announce (grepped,
        /// load-bearing — see class remarks): forwards to the live scope's own
        /// composer-routed cell announcement.
        /// </summary>
        public static void AnnounceCurrentCell(bool includeItemName)
        {
            RimWorldAccess.Shell.AssignMenuScopeMirror.AnnounceCurrentCellExternally(includeItemName);
        }
    }
}
