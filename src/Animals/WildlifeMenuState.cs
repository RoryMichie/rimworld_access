using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin bridge for the wildlife table: <see cref="IsActive"/> is the flag
    /// <see cref="RimWorldAccess.Shell.WildlifeScopeMirror"/> watches to
    /// push/pop <see cref="RimWorldAccess.Shell.WildlifeScope"/> (the table's entire
    /// keyboard/row/column/paint/typeahead implementation now lives on that scope, per the table-model
    /// migration). This class only owns what code OUTSIDE the scope reads:
    /// <see cref="WildlifeMenuPatch"/>'s window interception, the map-ambient
    /// guards (<c>ShellGuards</c>, <c>MapNavigationPatch</c>), and the
    /// pre-open empty-roster check (kept here, not on the scope, so the scope
    /// never gets pushed for an empty table — matching the retired handler's
    /// refuse-to-open behavior exactly).
    /// </summary>
    public static class WildlifeMenuState
    {
        public static bool IsActive { get; private set; } = false;

        public static void Open()
        {
            if (IsActive)
                return;
            if (!GuardHelper.RequireMap())
                return;

            bool hasWildlife = Find.CurrentMap.mapPawns.AllPawns.Any(p =>
                p.Spawned &&
                (p.Faction == null || p.Faction == Faction.OfInsects) &&
                p.AnimalOrWildMan() &&
                !p.Position.Fogged(p.Map) &&
                !p.IsPrisonerInPrisonCell());
            if (!hasWildlife)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Wildlife.Menu.NoWildlife".Loc());
                return;
            }

            IsActive = true;
        }

        public static void Close()
        {
            IsActive = false;
            SoundDefOf.TabClose.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Animals.Wildlife.Menu.Closed".Loc());
            Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Wildlife);
        }

        /// <summary>
        /// Silent deactivation for MainTabWindowLink's reconcile: the vanilla tab
        /// window went away (a mouse click, another tab opening), so the screen goes
        /// with it. Close's sound and announcement belong to the user's own Escape.
        /// </summary>
        internal static void CloseSilent()
        {
            IsActive = false;
        }
    }
}
