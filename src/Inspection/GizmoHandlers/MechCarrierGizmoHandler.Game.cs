using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Port of the original announcement ladders' "MechCarrierGizmo" branches
    /// (GetMechCarrierLabel and GetMechCarrierStatus), upgraded from per-call
    /// reflection walks to a cached FieldInfo plus typed CompMechCarrier reads.
    /// Vanilla draws an ingredient title, a 24-increment draggable autofill bar
    /// with a "count / max" overlay, and an autofill tooltip
    /// (MechCarrierGizmo.GizmoOnGUI / GetResourceBarTip):
    /// - Label mirrors the title (Props.fixedIngredient.LabelCap).
    /// - Status mirrors the bar overlay (IngredientCount / Props.maxIngredientCount),
    ///   which is exactly what the legacy status announced — nothing was dropped.
    /// - Description is new relative to the legacy ladder: it surfaces the
    ///   vanilla resource-bar tooltip, whose inputs (maxToFill, fixedIngredient,
    ///   parent def, spawnPawnKind) are all plain data reads with no render-time
    ///   state. Keys verified in Biotech Keyed/Misc_Gameplay.xml.
    /// Execution has no branch here — the original ladder had no execute-side
    /// counterpart for this type (the bar is adjusted via the slider path).
    /// </summary>
    internal sealed class MechCarrierGizmoHandler : GizmoHandlerBase
    {
        // MechCarrierGizmo.carrier is private (MechCarrierGizmo.cs:11); resolve once.
        private static readonly FieldInfo CarrierField = typeof(MechCarrierGizmo).GetField(
            "carrier", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Resolves the gizmo's private carrier comp, or null when the gizmo is
        /// not a MechCarrierGizmo or the field cannot be read.
        /// </summary>
        private static CompMechCarrier GetCarrier(Gizmo gizmo)
        {
            if (!(gizmo is MechCarrierGizmo) || CarrierField == null)
                return null;
            return CarrierField.GetValue(gizmo) as CompMechCarrier;
        }

        // MechCarrierGizmo.targetValue is the gizmo's private drag store; vanilla
        // drags it in 24 bands and commits carrier.maxToFill from it.
        private static readonly FieldInfo TargetValueField = typeof(MechCarrierGizmo).GetField(
            "targetValue", BindingFlags.Instance | BindingFlags.NonPublic);

        public override bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            adapter = null;

            var carrier = GetCarrier(gizmo);
            if (carrier?.Props == null || TargetValueField == null
                || carrier.Props.maxIngredientCount <= 0)
            {
                return false;
            }

            try
            {
                int maxCount = carrier.Props.maxIngredientCount;
                adapter = new GizmoSliderAdapter
                {
                    Title = carrier.Props.fixedIngredient != null
                        ? carrier.Props.fixedIngredient.LabelCap.ToString()
                        : "RimWorldAccess.Inspection.Gizmo.Type.CarrierFallback".Translate().ToString(),
                    Value = (float)TargetValueField.GetValue(gizmo),
                    Min = 0f,
                    Max = 1f,
                    // Vanilla's DraggableBar call uses 24 bands (MechCarrierGizmo.cs:71).
                    Step = 1f / 24f,
                    Write = value =>
                    {
                        TargetValueField.SetValue(gizmo, value);
                        carrier.maxToFill = UnityEngine.Mathf.RoundToInt(value * maxCount);
                    },
                    // Spoken as the resource count the fill target represents,
                    // matching the count vanilla draws over the bar.
                    DescribeValue = value =>
                        "RimWorldAccess.Inspection.Gizmo.Status.SliderTargetCount".Translate(
                            UnityEngine.Mathf.RoundToInt(value * maxCount), maxCount),
                };
                return true;
            }
            catch
            {
                adapter = null;
                return false;
            }
        }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;

            if (!(gizmo is MechCarrierGizmo))
                return false;

            // Legacy GetMechCarrierLabel: ingredient-specific title when the
            // fixed ingredient resolves (vanilla titles the gizmo with
            // Props.fixedIngredient.LabelCap), generic fallback otherwise.
            ThingDef ingredient = GetCarrier(gizmo)?.Props?.fixedIngredient;
            if (ingredient?.label != null)
                label = "RimWorldAccess.Inspection.Gizmo.Type.MechCarrierWithIngredient".Translate(ingredient.label.CapitalizeFirst());
            else
                label = "RimWorldAccess.Inspection.Gizmo.Type.MechCarrier".Translate();
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;

            CompMechCarrier carrier = GetCarrier(gizmo);
            if (carrier?.Props == null)
                return false;

            // Vanilla's bar overlay: IngredientCount + " / " + Props.maxIngredientCount
            // (MechCarrierGizmo.GizmoOnGUI) — the legacy status announced the same pair.
            status = "RimWorldAccess.Inspection.Gizmo.Status.UsedTotal".Translate(
                carrier.IngredientCount, carrier.Props.maxIngredientCount);
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;

            CompMechCarrier carrier = GetCarrier(gizmo);
            if (carrier?.Props?.fixedIngredient == null
                || carrier.Props.spawnPawnKind == null
                || carrier.parent?.def == null)
                return false;

            // Vanilla resource-bar tooltip (MechCarrierGizmo.GetResourceBarTip),
            // composed verbatim except vanilla's newline glue becomes sentence
            // glue for speech. Keys verified in Biotech Keyed/Misc_Gameplay.xml.
            string autofillLine = "MechCarrierAutofillResources".Translate() + " "
                + carrier.Props.fixedIngredient.label + ": " + carrier.maxToFill;
            string clickHint = "MechCarrierClickToSetAutofillAmount".Translate();
            string autofillDesc = "MechCarrierAutofillDesc".Translate(
                carrier.parent.def.label, carrier.Props.spawnPawnKind.labelPlural);
            description = (autofillLine + ". " + clickHint + " " + autofillDesc).StripTags();
            return true;
        }
    }
}
