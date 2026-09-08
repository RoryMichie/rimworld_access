using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    internal enum MaterialHarvestOutcome
    {
        /// <summary>ProcessInput produced a stuff menu; options are ready.</summary>
        Menu,
        /// <summary>ProcessInput ran but opened no menu: vanilla already messaged
        /// "NoStuffsToBuildWith" (spoken by NotificationAccessibilityPatch) or the
        /// tutor gate refused the interaction, matching the sighted no-op.</summary>
        NoMenu,
        /// <summary>Harvest could not run (no map) or threw; callers use the legacy
        /// hand-copied list.</summary>
        Fallback,
    }

    /// <summary>
    /// Builds the accessible material menu by invoking the real
    /// Designator_Build.ProcessInput and capturing the FloatMenu it adds to the
    /// WindowStack, so any mod prefix that widens or replaces the stuff list flows
    /// through unchanged. While InFlight: DialogInterceptionPatch swallows the menu,
    /// DesignatorManagerPatch skips placement routing for the transient Select, and
    /// DesignatorDeselectedPatch skips cleanup for the restoring Deselect.
    /// </summary>
    internal static class MaterialMenuHarvest
    {
        internal static bool InFlight { get; private set; }

        private static List<FloatMenuOption> captured;
        private static Action capturedOnClose;

        private static readonly FieldInfo floatMenuOptionsField =
            AccessTools.Field(typeof(FloatMenu), "options");

        /// <summary>DialogInterceptionPatch hands over the FloatMenu it suppressed.</summary>
        internal static void NotifyIntercepted(FloatMenu menu)
        {
            captured = floatMenuOptionsField?.GetValue(menu) as List<FloatMenuOption>;
            capturedOnClose = menu.onCloseCallback;
        }

        /// <summary>
        /// Harvests the designator's own stuff menu and rewires each option for the
        /// windowless menu: labels gain the availability count, and each action
        /// pre-sets the picked stuff (so the placement announcement fired during the
        /// vanilla delegate's Select reads the right material) before handing the
        /// original delegate to onPick. Options with no action are left untouched.
        /// vanillaOnClose is the captured menu's own close callback (vanilla latches
        /// writeStuff there); callers invoke it when the windowless menu closes.
        /// </summary>
        internal static MaterialHarvestOutcome TryBuildOptions(
            Designator_Build designator,
            Action<ThingDef, Action> onPick,
            out List<FloatMenuOption> options,
            out Action vanillaOnClose)
        {
            options = null;
            vanillaOnClose = null;

            Map map = Find.CurrentMap;
            if (map == null)
                return MaterialHarvestOutcome.Fallback;

            captured = null;
            capturedOnClose = null;
            Designator before = Find.DesignatorManager?.SelectedDesignator;
            bool threw = false;
            InFlight = true;
            try
            {
                // The event's contents are ignored on this path (Command.ProcessInput
                // only plays a sound), and a fresh Event avoids Unity's reuse of
                // Event.current inside the delegates that close over it.
                designator.ProcessInput(new Event());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Material menu harvest failed for {designator.PlacingDef?.defName}: {ex}");
                captured = null;
                capturedOnClose = null;
                threw = true;
            }
            finally
            {
                // ProcessInput selects the designator after adding its menu; undo that
                // while still InFlight so the deselect cleanup patch stays quiet and
                // the manager is back where the caller's menu flow expects it.
                DesignatorManager manager = Find.DesignatorManager;
                if (manager != null && manager.SelectedDesignator == designator && before != designator)
                {
                    if (before != null)
                        manager.Select(before);
                    else
                        manager.Deselect();
                }
                InFlight = false;
            }

#if DEBUG
            ShellDev.QARecord("material-harvest",
                designator.PlacingDef?.defName + " -> "
                + (threw ? "threw" : captured == null ? "no menu" : captured.Count + " options"));
#endif

            if (threw)
                return MaterialHarvestOutcome.Fallback;
            if (captured == null || captured.Count == 0)
                return MaterialHarvestOutcome.NoMenu;

            foreach (FloatMenuOption option in captured)
            {
                if (option.action == null)
                    continue;
                ThingDef material = BuildingReflection.GetShownItem(option);
                if (material != null)
                {
                    int count = map.resourceCounter.GetCount(material);
                    option.Label += " " + (count > 0
                        ? (string)"RimWorldAccess.Building.Architect.MaterialAvailableCount".Translate(count)
                        : (string)"RimWorldAccess.Building.Architect.MaterialNoneAvailable".Translate());
                }
                Action original = option.action;
                option.action = () =>
                {
                    if (material != null)
                    {
                        // Pre-set so the Select inside the vanilla delegate announces
                        // the picked material; the delegate re-assigns the same values.
                        designator.SetStuffDef(material);
                        BuildingReflection.SetWriteStuff(designator, true);
                    }
                    onPick(material, original);
                };
            }

            options = captured;
            vanillaOnClose = capturedOnClose;
            captured = null;
            capturedOnClose = null;
            return MaterialHarvestOutcome.Menu;
        }
    }
}
