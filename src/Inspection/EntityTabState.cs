using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin bridge for ITab_Entity (Anomaly DLC): <see cref="IsActive"/> is the
    /// flag <see cref="Shell.EntityTabScopeMirror"/> watches to push/pop
    /// <see cref="Shell.EntityTabScope"/> (the tab's entire row-building, cursor,
    /// typeahead, and announcement implementation now lives on that scope, per the table-model
    /// migration playbook, following the PawnAreaMenuState/AssignMenuState precedent). This class only
    /// owns what code OUTSIDE the scope reads: the two inspect-tab open call sites
    /// (<see cref="Adapters.EntityAdapter"/>) and the resolved
    /// <see cref="HeldPawn"/>/<see cref="PlatformThing"/> data the scope's
    /// OnPush reads once per open.
    /// </summary>
    public static class EntityTabState
    {
        public static bool IsActive { get; private set; }

        /// <summary>The entity itself. Set by <see cref="Open"/>; read once by the scope on push.</summary>
        public static Pawn HeldPawn { get; private set; }

        /// <summary>The Building_HoldingPlatform (or other holder); cached separately for CompEntityHolder lookups.</summary>
        public static Thing PlatformThing { get; private set; }

        /// <summary>
        /// Opens the entity menu for the given target. The target may be either a
        /// held Pawn or a Building_HoldingPlatform (or anything that has a HeldPawn
        /// via reflection).
        /// </summary>
        public static void Open(Thing target)
        {
            if (target == null) return;

            Pawn heldPawn = ResolveHeldPawn(target);
            if (heldPawn == null)
            {
                Log.Warning("[EntityTabState] No held pawn found for target.");
                return;
            }

            HeldPawn = heldPawn;
            // Cache the platform Thing for CompEntityHolder lookups (containment strength etc.)
            PlatformThing = heldPawn.ParentHolder as Thing ?? target;
            IsActive = true;
        }

        public static void Close()
        {
            IsActive = false;
            HeldPawn = null;
            PlatformThing = null;
        }

        private static Pawn ResolveHeldPawn(Thing target)
        {
            // Building_HoldingPlatform exposes HeldPawn directly.
            // Use reflection so we can compile without a hard reference to Anomaly types
            // (the file still compiles when Anomaly DLC isn't installed; method just no-ops).
            if (target is Pawn p && p.IsOnHoldingPlatform) return p;
            var heldPawnProp = AccessTools.Property(target.GetType(), "HeldPawn");
            return heldPawnProp?.GetValue(target) as Pawn;
        }
    }
}
