using System.Reflection;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Verbatim port of the original ExecuteSelected ladder's branch 3c
    /// (ActivityGizmo (Anomaly), matched by GetType().Name: toggles
    /// auto-suppression on Enter). Always handled once the name matches — both
    /// the success and could-not-toggle paths return without reaching the shared
    /// epilogue (no Close() call; the menu stays open).
    /// </summary>
    internal sealed class ActivityGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// ActivityGizmo.thing is private readonly; cached once since the
        /// registry pins this handler to that exact type.
        /// </summary>
        private static readonly FieldInfo ThingField = typeof(ActivityGizmo)
            .GetField("thing", BindingFlags.Instance | BindingFlags.NonPublic);

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;

            bool? newState = ToggleActivitySuppression(gizmo);
            if (newState.HasValue)
            {
                string stateStr = (newState.Value
                    ? "RimWorldAccess.Inspection.Gizmo.LimiterStateOn"
                    : "RimWorldAccess.Inspection.Gizmo.LimiterStateOff").Translate();
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.AutoSuppression".Loc(stateStr));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CouldNotToggleSuppression".Loc(), SpeechPriority.High);
            }
            return true;
        }

        public override bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            // Standard Gizmo_Slider plumbing; Enter toggles auto-suppression
            // (TryExecute above) and the arrows adjust the threshold, so the
            // hint says both.
            adapter = SliderGizmoHandler.BuildBaseAdapter(gizmo);
            if (adapter == null)
                return false;

            adapter.InteractionHint = "RimWorldAccess.Inspection.Gizmo.HintActivitySuppressionArrows".Translate();
            return true;
        }

        /// <summary>
        /// Toggles CompActivity.suppressionEnabled (public members throughout;
        /// only the gizmo's thing field needs reflection) and returns the new
        /// state. Null when the entity cannot currently be suppressed or the
        /// backing component cannot be reached — the caller announces the
        /// failure. Sounds mirror the vanilla checkbox (high tick on, low off).
        /// </summary>
        private static bool? ToggleActivitySuppression(Gizmo gizmo)
        {
            if (ThingField == null || !(gizmo is ActivityGizmo))
                return null;

            var thing = ThingField.GetValue(gizmo) as ThingWithComps;
            CompActivity comp = thing?.GetComp<CompActivity>();
            if (comp == null || !comp.CanBeSuppressed)
                return null;

            // MUTATION-C: mirrors ActivityGizmo.DrawHeader's inline suppression-icon
            // Widgets.ButtonInvisible handler (decompiled RimWorld/ActivityGizmo.cs) —
            // vanilla's own toggle is a raw IMGUI button flipping the public
            // CompActivity.suppressionEnabled field directly, gated on the same public
            // CanBeSuppressed check performed above; there is no Try*/AcceptanceReport
            // method (B) or invokable delegate (A) to ride instead.
            bool newValue = !comp.suppressionEnabled;
            comp.suppressionEnabled = newValue;

            if (newValue)
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            else
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();

            return newValue;
        }
    }
}
