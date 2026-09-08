using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Port of the announcement ladders' "Gizmo_MechResurrectionCharges"
    /// branches (the Type.ResurrectionCharges label case and
    /// GetMechResurrectionStatus). The label now prefers the game's own key
    /// ("MechResurrectionCharges", Biotech Keyed/Misc_Gameplay.xml — the exact
    /// string Gizmo_MechResurrectionCharges.GizmoOnGUI draws), falling back to
    /// the legacy mod key when the game key is unavailable. The status is fixed
    /// relative to the legacy port: the old code reflected a nonexistent "gene"
    /// field (the gizmo's actual field is the private `ability`,
    /// CompAbilityEffect_ResurrectMech), so it always returned empty. Vanilla
    /// draws a bare charge count with no maximum (the comp exposes only
    /// ChargesRemaining), so the status is the count alone — language-neutral,
    /// no translation needed. The gizmo draws no tooltip, so the description
    /// facet defers to the generic resolution. Execution has no branch here —
    /// the original ladder had no execute-side counterpart for this type.
    /// </summary>
    internal sealed class MechResurrectionChargesGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// Gizmo_MechResurrectionCharges keeps its comp in a private field;
        /// cache the FieldInfo once and do typed reads downstream.
        /// </summary>
        private static readonly FieldInfo AbilityField =
            typeof(Gizmo_MechResurrectionCharges).GetField(
                "ability", BindingFlags.Instance | BindingFlags.NonPublic);

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;

            if (!(gizmo is Gizmo_MechResurrectionCharges))
                return false;

            // Prefer the exact string vanilla draws on the gizmo
            // (Gizmo_MechResurrectionCharges.GizmoOnGUI); the key ships with
            // Biotech, so fall back to our legacy key if it is missing.
            if ("MechResurrectionCharges".TryTranslate(out TaggedString gameLabel))
                label = gameLabel.ToString();
            else
                label = "RimWorldAccess.Inspection.Gizmo.Type.ResurrectionCharges".Translate();
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;

            if (!(gizmo is Gizmo_MechResurrectionCharges))
                return false;

            if (AbilityField == null)
                return false;

            var comp = AbilityField.GetValue(gizmo) as CompAbilityEffect_ResurrectMech;
            if (comp == null)
                return false;

            // Vanilla renders only the remaining count, no maximum
            // (Gizmo_MechResurrectionCharges.GizmoOnGUI); a bare number is
            // language-neutral, and the label already names the unit.
            status = comp.ChargesRemaining.ToString();
            return true;
        }

        // TryGetDescription: intentionally not overridden. The decompiled gizmo
        // draws no tooltip, so the base returns false and the generic
        // description resolution takes over.
    }
}
