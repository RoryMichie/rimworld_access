using System;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reads DubsBadHygiene.Gizmo_BoilerStatus's own data reflectively: the
    /// public `boiler` field (a CompBoiler), its public `Props` property, and
    /// the public `GizmoLabel` / `PowerMode` / `PowerModes` members those
    /// expose (Gizmo_BoilerStatus.cs:36 for the label, :39/:47 for the fill
    /// fraction and its "N / N" readout). The gizmo returns
    /// GizmoResult(GizmoState.Clear) and overrides no ProcessInput, so no
    /// execution facet is claimed either: GizmoHandlerRegistry.HasActivation
    /// resolves false on its own and DescribeGizmo presents the row as a
    /// read-only status readout.
    /// </summary>
    internal sealed class DubsBoilerStatusGizmoHandler : GizmoHandlerBase
    {
        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            try
            {
                object props = GetProps(gizmo);
                if (props == null)
                    return false;

                FieldInfo gizmoLabelField = props.GetType().GetField("GizmoLabel", BindingFlags.Instance | BindingFlags.Public);
                string gizmoLabel = gizmoLabelField?.GetValue(props) as string;
                if (string.IsNullOrEmpty(gizmoLabel))
                    return false;

                label = gizmoLabel;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Exception reading DubsBadHygiene.Gizmo_BoilerStatus label: {ex.Message}");
                return false;
            }
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            try
            {
                object boiler = GetBoiler(gizmo);
                object props = GetProps(gizmo);
                if (boiler == null || props == null)
                    return false;

                FieldInfo powerModeField = boiler.GetType().GetField("PowerMode", BindingFlags.Instance | BindingFlags.Public);
                FieldInfo powerModesField = props.GetType().GetField("PowerModes", BindingFlags.Instance | BindingFlags.Public);
                if (powerModeField == null || powerModesField == null)
                    return false;

                object powerMode = powerModeField.GetValue(boiler);
                object powerModes = powerModesField.GetValue(props);
                status = $"{powerMode} / {powerModes}";
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Exception reading DubsBadHygiene.Gizmo_BoilerStatus status: {ex.Message}");
                return false;
            }
        }

        private static object GetBoiler(Gizmo gizmo)
        {
            FieldInfo boilerField = gizmo.GetType().GetField("boiler", BindingFlags.Instance | BindingFlags.Public);
            return boilerField?.GetValue(gizmo);
        }

        private static object GetProps(Gizmo gizmo)
        {
            object boiler = GetBoiler(gizmo);
            if (boiler == null)
                return null;

            PropertyInfo propsProperty = boiler.GetType().GetProperty("Props", BindingFlags.Instance | BindingFlags.Public);
            return propsProperty?.GetValue(boiler, null);
        }
    }
}
