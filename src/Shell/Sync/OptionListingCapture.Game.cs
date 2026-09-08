using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One option the game actually drew through OptionListingUtility: the
    /// live ListableOption (label + click action) and the rect it occupied,
    /// in window-content coordinates (the column's offset plus the option's
    /// position inside the column's GUI group).
    /// </summary>
    internal sealed class CapturedListOption
    {
        public readonly ListableOption Option;
        public readonly Rect Rect;
        public readonly bool LeftColumn;

        public CapturedListOption(ListableOption option, Rect rect, bool leftColumn)
        {
            Option = option;
            Rect = rect;
            LeftColumn = leftColumn;
        }
    }

    /// <summary>
    /// Captures the option lists MainMenuDrawer.DoMainMenuControls draws, so a
    /// scope can keyboard-drive the REAL buttons: labels and actions come
    /// straight from the live ListableOptions and each rect is recorded during
    /// the surface's own draw pass — exact for any font, language, or future
    /// layout change, with no re-derived layout math.
    ///
    /// Arm() may also inject extra options: appended to the first (main)
    /// column and prepended to the second (links) column. Injection mutates
    /// the freshly built per-pass lists inside DoMainMenuControls, so vanilla
    /// itself draws the injected options as ordinary buttons — mouse clicks
    /// run their actions, and the capture below records them like any other
    /// option. That is how the pause menu shows the mod's own menu items.
    ///
    /// Dormant until a scope arms it (the pause menu today; the Entry main
    /// menu can reuse it when that screen migrates). While disarmed every
    /// hook is a single boolean check.
    /// </summary>
    internal static class OptionListingCapture
    {
        private static bool armed;
        private static readonly List<CapturedListOption> items = new List<CapturedListOption>();
        private static List<ListableOption> injectMainColumn;
        private static List<ListableOption> injectLinksColumn;
        private static Rect currentColumn;
        private static int columnIndex;
        private static bool insideListing;

        public static bool Armed
        {
            get { return armed; }
        }

        /// <summary>Options captured on the most recent draw pass, main column first.</summary>
        public static IReadOnlyList<CapturedListOption> Items
        {
            get { return items; }
        }

        public static void Arm(List<ListableOption> mainColumnExtras, List<ListableOption> linksColumnExtras)
        {
            armed = true;
            injectMainColumn = mainColumnExtras;
            injectLinksColumn = linksColumnExtras;
            items.Clear();
            columnIndex = 0;
            insideListing = false;
        }

        public static void Disarm()
        {
            armed = false;
            injectMainColumn = null;
            injectLinksColumn = null;
            items.Clear();
            insideListing = false;
        }

        internal static void BeginPass()
        {
            if (!armed)
            {
                return;
            }
            items.Clear();
            columnIndex = 0;
        }

        internal static void BeginColumn(Rect columnRect, List<ListableOption> optList)
        {
            if (!armed)
            {
                return;
            }
            columnIndex++;
            currentColumn = columnRect;
            insideListing = true;
            if (columnIndex == 1 && injectMainColumn != null)
            {
                optList.AddRange(injectMainColumn);
            }
            else if (columnIndex == 2 && injectLinksColumn != null)
            {
                optList.InsertRange(0, injectLinksColumn);
            }
        }

        internal static void EndColumn()
        {
            insideListing = false;
        }

        internal static void Record(ListableOption option, Vector2 pos, float width, float height)
        {
            if (!armed || !insideListing)
            {
                return;
            }
            items.Add(new CapturedListOption(
                option,
                new Rect(currentColumn.x + pos.x, currentColumn.y + pos.y, width, height),
                columnIndex == 1));
        }
    }

    /// <summary>
    /// Resets the capture at the top of each menu draw pass, so the recorded
    /// list always reflects exactly one pass.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuDrawer), "DoMainMenuControls")]
    public static class OptionListingCapturePassPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            OptionListingCapture.BeginPass();
            // Same surface, same pass bracket: the tooltip channel records
            // only inside this surface's own draw (see TooltipCapture remarks).
            TooltipCapture.BeginPass();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            TooltipCapture.EndPass();
        }
    }

    /// <summary>
    /// Marks which column is being drawn and injects the armed extras into the
    /// per-pass option list before vanilla draws it.
    /// </summary>
    [HarmonyPatch(typeof(OptionListingUtility), "DrawOptionListing")]
    public static class OptionListingCaptureColumnPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, List<ListableOption> optList)
        {
            OptionListingCapture.BeginColumn(rect, optList);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            OptionListingCapture.EndColumn();
        }
    }

    /// <summary>
    /// Records the exact rect of every drawn option. DrawOption is virtual and
    /// ListableOption_WebLink overrides it, so both declarations are patched —
    /// a patch on the base method alone never fires for the override.
    /// </summary>
    [HarmonyPatch(typeof(ListableOption), "DrawOption")]
    public static class OptionListingCaptureBaseDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ListableOption __instance, Vector2 pos, float width, float __result)
        {
            OptionListingCapture.Record(__instance, pos, width, __result);
        }
    }

    /// <summary>See <see cref="OptionListingCaptureBaseDrawPatch"/>.</summary>
    [HarmonyPatch(typeof(ListableOption_WebLink), "DrawOption")]
    public static class OptionListingCaptureWebLinkDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ListableOption_WebLink __instance, Vector2 pos, float width, float __result)
        {
            OptionListingCapture.Record(__instance, pos, width, __result);
        }
    }
}
