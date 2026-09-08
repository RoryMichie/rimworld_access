using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Per-execution data a ported ladder branch reads from GizmoNavigationState.
    /// Built once by GizmoNavigationState.ExecuteSelected before dispatch (both
    /// for the directly-invoked Designator special case and for the registry-
    /// resolved branches) and passed through unchanged. Handlers call back into
    /// GizmoNavigationState's (now internal) helper methods — Close,
    /// PropagateToGroupedGizmos, GetAbilityCostInfo, etc. — for logic shared with
    /// code that stays there, rather than duplicating it here.
    /// </summary>
    public sealed class GizmoHandlerContext
    {
        /// <summary>
        /// The gizmo's owner, if any — an unconditional lookup in gizmoOwners
        /// (the same lookup every ladder branch that needed an owner performed
        /// independently, regardless of PawnJustSelected/multi-select state).
        /// </summary>
        public ISelectable Owner { get; }

        /// <summary>The gizmo's resolved label, for error/disabled announcements.</summary>
        public string GizmoLabel { get; }

        /// <summary>The synthetic EventType.Used event passed to Gizmo.ProcessInput.</summary>
        public Event FakeEvent { get; }

        /// <summary>Whether a pawn was just selected via , or . (owner-sync skip flag).</summary>
        public bool PawnJustSelected { get; }

        public GizmoHandlerContext(ISelectable owner, string gizmoLabel, Event fakeEvent, bool pawnJustSelected)
        {
            Owner = owner;
            GizmoLabel = gizmoLabel;
            FakeEvent = fakeEvent;
            PawnJustSelected = pawnJustSelected;
        }
    }
}
