using System.Reflection;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Verbatim port of the original ExecuteSelected ladder's branch 3b
    /// (GeneGizmo_ResourceHemogen, matched by GetType().Name: toggles
    /// hemogenPacksAllowed on Enter). Always handled once the name matches — both
    /// the success and could-not-toggle paths return without reaching the shared
    /// epilogue (no Close() call; the menu stays open).
    /// </summary>
    internal sealed class HemogenGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// GeneGizmo_Resource.gene is protected; cached once against the base
        /// type so it serves the hemogen subtype (and any future subtype) alike.
        /// </summary>
        private static readonly FieldInfo GeneField = typeof(GeneGizmo_Resource)
            .GetField("gene", BindingFlags.Instance | BindingFlags.NonPublic);

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;

            bool? newState = ToggleHemogenPacksAllowed(gizmo);
            if (newState.HasValue)
            {
                string stateStr = (newState.Value
                    ? "RimWorldAccess.Inspection.Gizmo.LimiterStateOn"
                    : "RimWorldAccess.Inspection.Gizmo.LimiterStateOff").Translate();
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.HemogenPacks".Loc(stateStr));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CouldNotToggleHemogen".Loc(), SpeechPriority.High);
            }
            return true;
        }

        public override bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            // Standard Gizmo_Slider plumbing; Enter toggles hemogen-pack use
            // (TryExecute above) and the arrows adjust the target, so the hint
            // says both.
            adapter = SliderGizmoHandler.BuildBaseAdapter(gizmo);
            if (adapter == null)
                return false;

            adapter.InteractionHint = "RimWorldAccess.Inspection.Gizmo.HintHemogenArrows".Translate();
            return true;
        }

        /// <summary>
        /// Toggles Gene_Hemogen.hemogenPacksAllowed (a public field, so the
        /// write is typed) and returns the new state. Null when the gene cannot
        /// be resolved or is not a hemogen gene (Biotech disabled, or a modded
        /// resource gene) — the caller announces the failure. Sounds mirror the
        /// vanilla checkbox (high tick on, low tick off).
        /// </summary>
        private static bool? ToggleHemogenPacksAllowed(Gizmo gizmo)
        {
            if (GeneField == null || !(gizmo is GeneGizmo_Resource))
                return null;

            if (!(GeneField.GetValue(gizmo) is Gene_Hemogen gene))
                return null;

            bool newValue = !gene.hemogenPacksAllowed;
            gene.hemogenPacksAllowed = newValue;

            if (newValue)
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            else
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();

            return newValue;
        }
    }
}
