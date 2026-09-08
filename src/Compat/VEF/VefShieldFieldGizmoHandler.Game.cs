using System;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for VEF.Apparels.Gizmo_EnergyShieldGeneratorStatus, the
    /// read-only status readout CompShieldField draws for its worn/built-in
    /// energy shield generator (VEF/Apparels/Comps/CompShieldField.cs lines
    /// 1044-1084). No VEF assembly reference: the comp field and its Energy/
    /// MaxEnergy properties are resolved once reflectively; parent is read
    /// through the vanilla Verse.ThingComp base the comp's runtime type
    /// derives from, so no reflection is needed for that hop.
    /// </summary>
    internal sealed class VefShieldFieldGizmoHandler : GizmoHandlerBase
    {
        private readonly Type gizmoType;
        private readonly FieldInfo shieldGeneratorField;
        private readonly PropertyInfo energyProperty;
        private readonly PropertyInfo maxEnergyProperty;
        private readonly bool ready;

        public VefShieldFieldGizmoHandler(Type gizmoType)
        {
            this.gizmoType = gizmoType;

            var surface = new ReflectionSurface("VefShieldFieldGizmoHandler");
            surface.Supplied("VEF.Apparels.Gizmo_EnergyShieldGeneratorStatus", gizmoType);

            shieldGeneratorField = surface.Field(gizmoType, "shieldGenerator");
            Type compType = shieldGeneratorField?.FieldType;

            energyProperty = surface.Property(compType, "Energy");
            maxEnergyProperty = surface.Property(compType, "MaxEnergy");
            ready = surface.Ready;
        }

        // The Tiny-font label vanilla draws for this gizmo (GizmoOnGUI line 1065).
        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            if (!ready || !gizmoType.IsInstanceOfType(gizmo))
                return false;

            try
            {
                if (!(shieldGeneratorField.GetValue(gizmo) is ThingComp comp) || comp.parent == null)
                    return false;

                label = comp.parent.LabelCap;
                return !string.IsNullOrEmpty(label);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefShieldFieldGizmoHandler.TryGetLabel failed: {ex.Message}");
                return false;
            }
        }

        // Mirrors the on-bar text (GizmoOnGUI line 1073): values are drawn ×100.
        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!ready || !gizmoType.IsInstanceOfType(gizmo))
                return false;

            try
            {
                object comp = shieldGeneratorField.GetValue(gizmo);
                if (comp == null)
                    return false;

                float energy = (float)energyProperty.GetValue(comp);
                float maxEnergy = (float)maxEnergyProperty.GetValue(comp);
                if (maxEnergy <= 0f)
                    return false;

                float percent = (energy / maxEnergy) * 100f;
                status = "RimWorldAccess.Inspection.Gizmo.Status.PercentEnergy".Translate(
                    percent.ToString("F0"),
                    (energy * 100f).ToString("F0"),
                    (maxEnergy * 100f).ToString("F0"));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefShieldFieldGizmoHandler.TryGetStatus failed: {ex.Message}");
                return false;
            }
        }

        // Read-only gizmo: no execute, no extra options, no description (vanilla
        // draws no tooltip for it either).
    }
}
