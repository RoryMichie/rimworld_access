using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Verbatim port of the original announcement ladders' "Gizmo_EnergyShieldStatus"
    /// branches (GetEnergyShieldLabel and GetEnergyShieldStatus), upgraded from
    /// reflection to typed access — the gizmo's `shield` field and CompShield's
    /// Energy/IsApparel/parent members are all public. The description facet is
    /// new relative to the legacy ladder: it surfaces the vanilla ShieldPersonalTip
    /// tooltip the gizmo draws (Gizmo_EnergyShieldStatus.GizmoOnGUI), which the
    /// legacy code never announced. Execution has no branch here — the original
    /// ladder had no execute-side counterpart for this type.
    /// </summary>
    internal sealed class EnergyShieldGizmoHandler : GizmoHandlerBase
    {
        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;

            if (!(gizmo is Gizmo_EnergyShieldStatus shieldGizmo))
                return false;

            CompShield shield = shieldGizmo.shield;
            if (shield == null)
            {
                // Legacy fallback: shield component missing → generic type name.
                label = "RimWorldAccess.Inspection.Gizmo.Type.EnergyShield".Translate();
                return true;
            }

            // Vanilla shows the apparel's own label for worn shields and the
            // ShieldInbuilt string otherwise (Gizmo_EnergyShieldStatus.GizmoOnGUI).
            if (shield.IsApparel && shield.parent != null)
                label = "RimWorldAccess.Inspection.Gizmo.Type.ShieldApparel".Translate(shield.parent.LabelCap);
            else
                label = "RimWorldAccess.Inspection.Gizmo.Type.ShieldInbuilt".Translate();
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;

            if (!(gizmo is Gizmo_EnergyShieldStatus shieldGizmo))
                return false;

            CompShield shield = shieldGizmo.shield;
            if (shield?.parent == null)
                return false;

            float maxEnergy = shield.parent.GetStatValue(StatDefOf.EnergyShieldEnergyMax);
            if (maxEnergy <= 0f)
                return false;

            // Vanilla renders the bar values ×100 (Gizmo_EnergyShieldStatus.GizmoOnGUI);
            // the legacy status added the percentage up front.
            float percent = (shield.Energy / maxEnergy) * 100f;
            status = "RimWorldAccess.Inspection.Gizmo.Status.PercentEnergy".Translate(
                percent.ToString("F0"),
                (shield.Energy * 100f).ToString("F0"),
                (maxEnergy * 100f).ToString("F0"));
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;

            if (!(gizmo is Gizmo_EnergyShieldStatus))
                return false;

            // Vanilla tooltip for this gizmo (Gizmo_EnergyShieldStatus.GizmoOnGUI);
            // key verified in Core Keyed/Misc_Gameplay.xml.
            description = "ShieldPersonalTip".Translate().ToString().StripTags();
            return true;
        }
    }
}
