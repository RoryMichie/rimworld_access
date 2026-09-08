using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for temperature control device settings (coolers, heaters, etc.).
    /// Allows adjusting target temperature via keyboard shortcuts.
    /// </summary>
    public static class TempControlMenuState
    {
        private static CompTempControl tempControl = null;
        private static Building building = null;
        private static bool isActive = false;

        public static bool IsActive => isActive;

        /// <summary>Live target temperature for TempControlScope's stepper row (read fresh each announce, never cached).</summary>
        public static float TargetTemperature => tempControl?.TargetTemperature ?? 0f;

        public static void Open(Building targetBuilding)
        {
            if (!GuardHelper.RequireBuilding(targetBuilding)) return;

            CompTempControl comp = targetBuilding.TryGetComp<CompTempControl>();
            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Temp.NoTempComponent".Loc());
                return;
            }

            building = targetBuilding;
            tempControl = comp;
            isActive = true;
            MapNavigationState.SuppressMapNavigation = true;

            AnnounceCurrentSettings();
        }

        public static void Close()
        {
            tempControl = null;
            building = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        // DOCTRINE FIX: the five adjust/reset methods below now ride MUTATION vehicle A instead of the
        // old raw TargetTemperature +=/= writes. CompTempControl.CompGetGizmosExtra (decompiled
        // RimWorld/CompTempControl.cs:55-121) yields exactly five Command_Action gizmos in this fixed
        // order: -10, -1, reset, +1, +10 — base ThingComp.CompGetGizmosExtra
        // (Verse/ThingComp.cs:108-111) yields nothing, so the position is exact and stable, not
        // label-matched. Invoking each gizmo's own ProcessInput (not the bare .action delegate) rides
        // the same real vanilla command RefuelableComponentState.OpenTargetFuelDialog already uses this
        // pattern for, restoring vanilla's own sound cues (SoundDefOf.DragSlider on adjust, Tick_Tiny
        // on reset, both currently silent below the level of Command.ProcessInput's CurActivateSound
        // check since neither gizmo sets activateSound -- the sound comes from
        // InterfaceChangeTargetTemperature/the reset delegate themselves, called by action()) and
        // vanilla's own clamp (InterfaceChangeTargetTemperature's Mathf.Clamp) instead of a
        // hand-duplicated one.
        private static void InvokeGizmoAt(int index)
        {
            if (tempControl == null) return;
            var gizmos = new List<Gizmo>(tempControl.CompGetGizmosExtra());
            if (index < 0 || index >= gizmos.Count) return;
            gizmos[index].ProcessInput(null);
            AnnounceCurrentSettings();
        }

        public static void DecreaseTemperatureLarge() => InvokeGizmoAt(0); // -10
        public static void DecreaseTemperatureSmall() => InvokeGizmoAt(1); // -1
        public static void ResetTemperature() => InvokeGizmoAt(2);        // reset to 21C
        public static void IncreaseTemperatureSmall() => InvokeGizmoAt(3); // +1
        public static void IncreaseTemperatureLarge() => InvokeGizmoAt(4); // +10

        public static void AnnounceCurrentSettings()
        {
            if (tempControl == null || building == null)
                return;

            string targetTemp = MenuHelper.FormatTemperature(tempControl.TargetTemperature, "F0");

            string powerSuffix = "";
            if (tempControl.PowerTrader != null)
            {
                if (tempControl.PowerTrader.Off)
                {
                    powerSuffix = "RimWorldAccess.Building.Temp.PowerModeOffSuffix".Translate();
                }
                else if (tempControl.operatingAtHighPower)
                {
                    powerSuffix = "RimWorldAccess.Building.Temp.PowerModeHighSuffix".Translate();
                }
                else
                {
                    powerSuffix = "RimWorldAccess.Building.Temp.PowerModeLowSuffix".Translate();
                }
            }

            TolkHelper.Speak("RimWorldAccess.Building.Temp.LabelTarget".Loc(building.LabelCap, targetTemp, powerSuffix));
        }
    }
}
