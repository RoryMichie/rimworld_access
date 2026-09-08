using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin bridge for the Health tab's Operations / Health Settings
    /// sub-menus: <see cref="IsActive"/> is the flag
    /// <see cref="RimWorldAccess.Shell.HealthTabScopeMirror"/> watches to
    /// push/pop <see cref="RimWorldAccess.Shell.HealthTabScope"/> (the tab's
    /// entire keyboard/typeahead/announcement/mutation implementation now
    /// lives on that scope, per the table-model migration playbook,
    /// following the AnimalsMenuState/AssignMenuState precedent). This class only owns what code OUTSIDE the
    /// scope reads: <c>PawnHealthAdapter</c>'s two entry points
    /// (<see cref="OpenOperations"/>/<see cref="OpenMedicalSettings"/>), the
    /// map-ambient guards (<c>ShellGuards</c>, <c>MapNavigationPatch</c>,
    /// <c>MapScope.Inspect</c>), and <see cref="TargetPawn"/>/
    /// <see cref="OpenToOperations"/> — the open-time data the two public
    /// entry points must keep accepting, read once by the scope's OnPush.
    /// </summary>
    public static class HealthTabState
    {
        public static bool IsActive { get; private set; }

        /// <summary>The pawn whose Health tab is open. Set by an Open* method; read once by the scope on push.</summary>
        public static Pawn TargetPawn { get; private set; }

        /// <summary>True to open into the Operations list, false for the Medical Settings list. Read once by the scope on push.</summary>
        public static bool OpenToOperations { get; private set; }

        public static void OpenOperations(Pawn pawn)
        {
            if (pawn == null)
                return;
            TargetPawn = pawn;
            OpenToOperations = true;
            IsActive = true;
        }

        public static void OpenMedicalSettings(Pawn pawn)
        {
            if (pawn == null)
                return;
            TargetPawn = pawn;
            OpenToOperations = false;
            IsActive = true;
        }

        /// <summary>
        /// Closes the tab. Called only from the scope's own GoBack (grep-verified
        /// no external caller) — kept public for parity with the mod's other thin
        /// bridges. No re-announcement here; the scope's GoBack follows this with
        /// WindowlessInspectionState.ReannounceCurrentSelection(), exactly as the
        /// retired GoBack did.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            TargetPawn = null;
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }
    }
}
