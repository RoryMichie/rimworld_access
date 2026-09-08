using Verse;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin bridge for the pawn allowed-area assignment picker: <see cref="IsActive"/>
    /// is the flag <see cref="RimWorldAccess.Shell.PawnAreaMenuScopeMirror"/> watches
    /// to push/pop <see cref="RimWorldAccess.Shell.PawnAreaMenuScope"/> (the picker's
    /// entire keyboard/typeahead/announcement implementation now lives on that
    /// scope, per the table-model migration playbook,
    /// following the AnimalsMenuState/AssignMenuState precedent).
    /// This class only owns what code OUTSIDE the scope reads: the ambient Alt+A
    /// opener (<c>MapScope.QuickInfo</c>), the map-ambient guards (<c>ShellGuards</c>,
    /// <c>MapNavigationPatch</c>), and <see cref="TargetPawn"/> — the one piece of
    /// open-time data the public <see cref="Open"/> signature must keep accepting,
    /// read once by the scope's <c>OnPush</c>.
    /// </summary>
    public static class PawnAreaMenuState
    {
        public static bool IsActive { get; private set; } = false;

        /// <summary>The pawn this picker is editing. Set by <see cref="Open"/>; read once by the scope on push.</summary>
        public static Pawn TargetPawn { get; private set; }

        public static void Open(Pawn pawn)
        {
            if (IsActive) return;
            if (!GuardHelper.RequirePawn(pawn)) return;

            // Full vanilla gate (RimWorld/PawnColumnWorker_AllowedArea.cs
            // DoCell:32-34), not just SupportsAllowedAreas: refuses a
            // non-player-faction pawn, a mutant that doesn't respect allowed
            // areas, and an overseer-less mechanoid the same way vanilla's
            // own cell draws nothing for them.
            if (!PawnColumnMutationHelper.CanEditAllowedArea(pawn))
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Area.NoSupport".Loc(pawn.LabelShort));
                return;
            }

            Map map = Find.CurrentMap;
            if (map?.areaManager == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Area.NoMap".Loc());
                return;
            }

            TargetPawn = pawn;
            IsActive = true;
        }

        public static void Close()
        {
            IsActive = false;
            TargetPawn = null;
        }
    }
}
