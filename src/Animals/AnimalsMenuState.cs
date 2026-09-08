using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin bridge for the colony animals table: <see cref="IsActive"/> is the flag
    /// <see cref="RimWorldAccess.Shell.AnimalsScopeMirror"/> watches to push/pop
    /// <see cref="RimWorldAccess.Shell.AnimalsScope"/> (the table's entire
    /// keyboard/row/column/submenu/paint/typeahead implementation now lives on that scope, per the
    /// table-model migration). This class only owns what code OUTSIDE the scope reads:
    /// <see cref="AnimalsMenuPatch"/>'s window interception, the map-ambient guards
    /// (<c>ShellGuards</c>, <c>MapNavigationPatch</c>),
    /// <see cref="AutoSlaughterState"/>'s Close(silent:) callback, and the
    /// pre-open empty-roster check (kept here, not on the scope, so the
    /// scope never gets pushed for an empty table — matching the retired
    /// handler's refuse-to-open behavior exactly).
    /// </summary>
    public static class AnimalsMenuState
    {
        public static bool IsActive { get; private set; } = false;

        public static void Open()
        {
            if (IsActive)
                return;
            if (!GuardHelper.RequireMap())
                return;

            if (!Find.CurrentMap.mapPawns.ColonyAnimals.Any())
            {
                TolkHelper.Speak("RimWorldAccess.Animals.Menu.NoColonyAnimals".Loc());
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
                TolkHelper.Speak("RimWorldAccess.Animals.Menu.Closed".Loc());
            }
            Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Animals);
        }
    }
}
