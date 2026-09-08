using System;

namespace RimWorldAccess
{
    /// <summary>
    /// Describes one adjustable slider on a gizmo, produced by
    /// IGizmoHandler.TryGetSliderAdapter for GizmoNavigationState's shared
    /// adjustment step math (Left/Right arrows, Shift for larger steps). A
    /// handler only claims the facet when the gizmo is
    /// adjustable RIGHT NOW (draggable, player-controlled owner, ...), so a
    /// resolved adapter doubles as the "is adjustable" predicate. Pure data +
    /// delegates: no game dependencies in this type itself.
    /// </summary>
    public sealed class GizmoSliderAdapter
    {
        /// <summary>Spoken name of the value being adjusted (e.g. "Chemfuel",
        /// "Psyfocus").</summary>
        public string Title;

        /// <summary>Current target value, in [Min, Max].</summary>
        public float Value;

        public float Min;
        public float Max;

        /// <summary>One arrow-key step (vanilla's drag increment).</summary>
        public float Step;

        /// <summary>Writes a new value back to the gizmo/comp — the same store
        /// vanilla's bar drag writes.</summary>
        public Action<float> Write;

        /// <summary>Optional richer readout for the value just written (e.g.
        /// "75 / 200 fuel", "40%, 2.5 pruning hours"). Null: the session speaks
        /// the plain percent.</summary>
        public Func<float, string> DescribeValue;

        /// <summary>Optional pre-translated interaction hint appended to the
        /// gizmo's announcement (e.g. "Enter toggles the limiter, arrows adjust
        /// psyfocus"). Null: the generic adjust hint is spoken.</summary>
        public string InteractionHint;
    }
}
