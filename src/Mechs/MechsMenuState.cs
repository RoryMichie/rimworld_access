using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin bridge for the mechanoid table: <see cref="IsActive"/> is the flag
    /// <see cref="RimWorldAccess.Shell.MechsScopeMirror"/> watches to push/pop
    /// <see cref="RimWorldAccess.Shell.MechsScope"/> (the table's entire
    /// keyboard/row/column/submenu/paint/typeahead implementation now lives on that scope, per the
    /// table-model migration). This class only owns what code OUTSIDE the scope reads:
    /// <see cref="MechsMenuPatch"/>'s window interception, the map-ambient
    /// guards (<c>ShellGuards</c>, <c>MapNavigationPatch</c>), the F4
    /// Animals/Mechs picker (<c>MapScope</c>), and the pre-open
    /// Biotech-required/empty-roster checks (kept here, not on the scope, so
    /// the scope never gets pushed for an unusable table — matching the
    /// retired handler's refuse-to-open behavior exactly).
    /// </summary>
    public static class MechsMenuState
    {
        public static bool IsActive { get; private set; } = false;

        public static void Open()
        {
            if (IsActive)
                return;

            if (!ModsConfig.BiotechActive)
            {
                TolkHelper.Speak("RimWorldAccess.Mechs.Menu.BiotechRequired".Loc());
                return;
            }

            if (!GuardHelper.RequireMap())
                return;

            bool anyMechs = Find.CurrentMap.mapPawns.PawnsInFaction(Faction.OfPlayer)
                .Any(p => p.RaceProps.IsMechanoid && p.OverseerSubject != null);
            if (!anyMechs)
            {
                TolkHelper.Speak("RimWorldAccess.Mechs.Menu.NoMechsFound".Loc());
                return;
            }

            IsActive = true;
        }

        public static void Close(bool silent = false)
        {
            IsActive = false;

            if (!silent)
            {
                SoundDefOf.TabClose.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Mechs.Menu.Closed".Loc());
            }
            Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Mechs);
        }
    }
}
