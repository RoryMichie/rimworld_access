using System;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Compat for ilyvion's Laboratory framework, a shared dependency of Colony
    /// Manager Redux and other consumer mods.
    ///
    /// IlyvionWidgets.DisableableButtonText's disabled branch (enabled == false)
    /// never calls Widgets.ButtonText — it draws a greyed atlas plus a bare
    /// Widgets.Label — so capture sees only inert text with no sign that it is a
    /// disabled action (6 live Colony Manager Redux sites, e.g. Import/Export
    /// load/save). This bracket records that branch as a Button row with Disabled
    /// set, reusing the suppression idiom Widgets.CustomButtonText's own tap uses
    /// (EnterSelfCaptioned / ExitCustomButtonAndRecord) so the inner Widgets.Label
    /// call is not also captured as a separate row. GenericWindowScope already
    /// speaks Disabled rows.
    ///
    /// The enabled == true branch delegates straight to Widgets.ButtonText, which
    /// the existing tap covers, so the bracket stays out of it.
    /// </summary>
    internal static class LaboratoryCompat
    {
        public static void Register(Harmony harmony)
        {
            try
            {
                var surface = new ReflectionSurface("LaboratoryCompat");
                Type ilyvionWidgetsType = surface.Type("ilyvion.Laboratory.UI.IlyvionWidgets");
                MethodInfo disableableButtonText = surface.Method(ilyvionWidgetsType, "DisableableButtonText");
                if (!surface.Ready)
                {
                    return;
                }

                harmony.Patch(disableableButtonText,
                    prefix: new HarmonyMethod(typeof(LaboratoryCompat), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(LaboratoryCompat), nameof(Postfix)));
                ModLogger.Msg("Laboratory compat: DisableableButtonText disabled-branch bracket applied.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Laboratory compat registration failed: " + ex.Message);
            }
        }

        private static void Prefix(Rect rect, string label, bool enabled)
        {
            if (!enabled)
            {
                WidgetCapture.EnterSelfCaptioned(label);
            }
        }

        private static void Postfix(Rect rect, string label, bool enabled)
        {
            if (!enabled)
            {
                WidgetCapture.ExitCustomButtonAndRecord(rect, label, true);
            }
        }
    }
}
