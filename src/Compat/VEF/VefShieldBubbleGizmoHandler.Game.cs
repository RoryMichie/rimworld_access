using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for VEF.Apparels.Gizmo_EnergyCompShieldStatus, the
    /// personal-bubble-shield status readout (VEF/Apparels/Comps/
    /// CompShieldBubble.cs lines 13-54). Unlike the shield-field gizmo, this
    /// one draws its raw Energy/EnergyMax values (not ×100) and has a tooltip
    /// when CompProperties_ShieldBubble.tooltipKey is set.
    /// </summary>
    internal sealed class VefShieldBubbleGizmoHandler : GizmoHandlerBase
    {
        private readonly Type gizmoType;
        private readonly FieldInfo shieldField;
        private readonly PropertyInfo energyProperty;
        private readonly PropertyInfo energyMaxProperty;
        private readonly bool ready;

        // Resolved lazily from the comp's own Props runtime type the first time
        // TryGetDescription sees an instance; CompProperties_ShieldBubble is not
        // referenced directly, so this is the field's declaring type discovered
        // through the live object rather than a second TypeByName lookup.
        private FieldInfo tooltipKeyField;
        private Type tooltipKeyFieldOwnerType;

        public VefShieldBubbleGizmoHandler(Type gizmoType)
        {
            this.gizmoType = gizmoType;

            var surface = new ReflectionSurface("VefShieldBubbleGizmoHandler");
            surface.Supplied("VEF.Apparels.Gizmo_EnergyCompShieldStatus", gizmoType);

            shieldField = surface.Field(gizmoType, "shield");
            Type compType = shieldField?.FieldType;

            energyProperty = surface.Property(compType, "Energy");
            energyMaxProperty = surface.Property(compType, "EnergyMax");
            ready = surface.Ready;
        }

        // GizmoOnGUI line 39.
        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            if (!ready || !gizmoType.IsInstanceOfType(gizmo))
                return false;

            try
            {
                if (!(shieldField.GetValue(gizmo) is ThingComp comp) || comp.parent == null)
                    return false;

                label = comp.parent.LabelCap;
                return !string.IsNullOrEmpty(label);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefShieldBubbleGizmoHandler.TryGetLabel failed: {ex.Message}");
                return false;
            }
        }

        // GizmoOnGUI line 46 draws Energy.ToString("F0") + " / " + EnergyMax.ToString("F0")
        // — raw values, not ×100 like the shield-field gizmo. Same PercentEnergy
        // key, percent computed from the raw ratio.
        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!ready || !gizmoType.IsInstanceOfType(gizmo))
                return false;

            try
            {
                object comp = shieldField.GetValue(gizmo);
                if (comp == null)
                    return false;

                float energy = (float)energyProperty.GetValue(comp);
                float energyMax = (float)energyMaxProperty.GetValue(comp);
                if (energyMax <= 0f)
                    return false;

                float percent = (energy / energyMax) * 100f;
                status = "RimWorldAccess.Inspection.Gizmo.Status.PercentEnergy".Translate(
                    percent.ToString("F0"),
                    energy.ToString("F0"),
                    energyMax.ToString("F0"));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefShieldBubbleGizmoHandler.TryGetStatus failed: {ex.Message}");
                return false;
            }
        }

        // GizmoOnGUI lines 48-51: TooltipHandler.TipRegion only fires when
        // Props.tooltipKey is set. comp.props is the vanilla ThingComp field;
        // tooltipKey itself is declared on CompProperties_ShieldBubble, so it is
        // resolved (once) against the live props object's own runtime type.
        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            if (!ready || !gizmoType.IsInstanceOfType(gizmo))
                return false;

            try
            {
                if (!(shieldField.GetValue(gizmo) is ThingComp comp) || comp.props == null)
                    return false;

                Type propsType = comp.props.GetType();
                if (tooltipKeyField == null || tooltipKeyFieldOwnerType != propsType)
                {
                    tooltipKeyField = AccessTools.Field(propsType, "tooltipKey");
                    tooltipKeyFieldOwnerType = propsType;
                }
                if (tooltipKeyField == null)
                    return false;

                string tooltipKey = tooltipKeyField.GetValue(comp.props) as string;
                if (string.IsNullOrEmpty(tooltipKey))
                    return false;

                description = tooltipKey.Translate().ToString().StripTags();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefShieldBubbleGizmoHandler.TryGetDescription failed: {ex.Message}");
                return false;
            }
        }
    }
}
