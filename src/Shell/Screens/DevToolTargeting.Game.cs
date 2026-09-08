using System;
using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard bridge for RimWorld's dev-mode debug tools. Choosing one arms
    /// <see cref="DebugTools.curTool"/> and waits for a map MOUSE CLICK, whose <c>clickAction</c>
    /// reads the mouse position itself, so a player without a mouse can never reach it.
    ///
    /// Three pieces close that gap without re-implementing any tool. <see cref="Reconcile"/> tracks
    /// the armed tool by reference and announces each arm or disarm whatever caused it;
    /// <see cref="FireAtKeyboardCursor"/> and <see cref="CancelTool"/> resync it so they never
    /// double-announce over their own outcome. The apply and cancel claims on
    /// <see cref="MapToolScope"/> fire the tool at the keyboard cursor — repeatably, since vanilla
    /// keeps most tools armed after a click — or put it away. The three Harmony prefixes below make
    /// the mouse-position reads return the shell's keyboard cursor while a fire is in flight, so the
    /// tool's unmodified <c>clickAction</c> operates on the cell the player navigated to; the
    /// latches are null otherwise, keeping those hot paths one field read.
    ///
    /// Firing and cancelling fabricate the exact mouse-down event
    /// <see cref="DebugTool.DebugToolOnGUI"/> already handles and call it directly. The dispatcher
    /// runs inside a real OnGUI pass, so <see cref="Event.current"/> is a live event, temporarily
    /// restyled and restored.
    /// </summary>
    internal static class DevToolTargeting
    {
        private static readonly AccessTools.FieldRef<DebugTool, string> LabelRef =
            AccessTools.FieldRefAccess<DebugTool, string>("label");

        // Non-null only for the duration of one fire. overrideCellVector mirrors overrideCell for
        // the tools that read UI.MouseMapPosition instead of UI.MouseCell.
        private static IntVec3? overrideCell;
        private static Vector3 overrideCellVector;
        private static PlanetTile? overrideTile;

        // The tool the announcement mirror last spoke about, by reference.
        private static DebugTool lastSeenTool;

        private static bool ToolArmed()
        {
            return DebugTools.curTool != null;
        }

        /// <summary>
        /// Per-frame arm/disarm announcer, run from the dispatcher's mirror sequence. Speaks once
        /// whenever <see cref="DebugTools.curTool"/> changes reference, whatever the origin.
        /// </summary>
        public static void Reconcile()
        {
            DebugTool current = DebugTools.curTool;
            if (ReferenceEquals(current, lastSeenTool))
                return;

            if (current != null)
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.Dev.ToolArmed".Translate(LabelRef(current)));
            }
            else
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.Dev.ToolCancelled".Translate());
            }
            lastSeenTool = current;
        }

        /// <summary>
        /// Fires the armed tool at the shell's keyboard cursor by staging the cursor read and
        /// invoking vanilla's own left-click path. Repeatable, since vanilla keeps most tools armed.
        /// </summary>
        public static void FireAtKeyboardCursor()
        {
            DebugTool tool = DebugTools.curTool;
            if (tool == null)
                return;

            Event cur = Event.current;
            if (cur == null)
                return;

            string label = LabelRef(tool);
            bool world = WorldRendererUtility.WorldSelected;
            string announcement;

            EventType savedType = cur.type;
            int savedButton = cur.button;
            try
            {
                if (world)
                {
                    PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
                    overrideTile = tile;
                    cur.type = EventType.MouseDown;
                    cur.button = 0;
                    tool.DebugToolOnGUI();
                    announcement = "RimWorldAccess.Dev.ToolAppliedTile".Translate(label, tile.tileId);
                }
                else
                {
                    IntVec3 cell = MapNavigationState.CurrentCursorPosition;
                    overrideCell = cell;
                    overrideCellVector = cell.ToVector3Shifted();
                    cur.type = EventType.MouseDown;
                    cur.button = 0;
                    tool.DebugToolOnGUI();
                    announcement = "RimWorldAccess.Dev.ToolApplied".Translate(label, cell.x, cell.z);
                }
            }
            finally
            {
                cur.type = savedType;
                cur.button = savedButton;
                overrideCell = null;
                overrideTile = null;
            }

            // The tool may stay armed or have cleared itself, so sync the mirror to whatever the
            // invoke left, or it re-announces over the applied phrase spoken here.
            lastSeenTool = DebugTools.curTool;
            TolkHelper.SpeakData(announcement);
        }

        /// <summary>Puts the armed tool away by staging vanilla's own right-click path.</summary>
        public static void CancelTool()
        {
            DebugTool tool = DebugTools.curTool;
            if (tool == null)
                return;

            Event cur = Event.current;
            if (cur == null)
                return;

            EventType savedType = cur.type;
            int savedButton = cur.button;
            try
            {
                cur.type = EventType.MouseDown;
                cur.button = 1;
                tool.DebugToolOnGUI();
            }
            finally
            {
                cur.type = savedType;
                cur.button = savedButton;
            }

            // curTool is now null; keep the mirror from re-announcing the disarm.
            lastSeenTool = DebugTools.curTool;
            TolkHelper.SpeakData((string)"RimWorldAccess.Dev.ToolCancelled".Translate());
        }

        /// <summary>
        /// Runs <paramref name="body"/> with the map cursor reads forced to
        /// <paramref name="cell"/>, so vanilla code invoked inside reads the keyboard cursor rather
        /// than the mouse. Saves and restores the prior latch values, so it composes with an
        /// in-flight <see cref="FireAtKeyboardCursor"/> rather than clobbering it.
        /// </summary>
        internal static T WithCursorOverride<T>(IntVec3 cell, Func<T> body)
        {
            PushCursorOverride(cell);
            try
            {
                return body();
            }
            finally
            {
                PopCursorOverride();
            }
        }

        /// <summary>
        /// <see cref="WithCursorOverride"/> in two halves, for a Harmony prefix/finalizer bracket:
        /// a delegate cannot wrap a body Harmony calls itself. Nests the same way, and every push
        /// MUST be matched from a finally or a finalizer.
        /// </summary>
        internal static void PushCursorOverride(IntVec3 cell)
        {
            savedOverrideCells.Add(overrideCell);
            savedOverrideVectors.Add(overrideCellVector);
            overrideCell = cell;
            overrideCellVector = cell.ToVector3Shifted();
        }

        internal static void PopCursorOverride()
        {
            int last = savedOverrideCells.Count - 1;
            if (last < 0)
            {
                return;
            }
            overrideCell = savedOverrideCells[last];
            overrideCellVector = savedOverrideVectors[last];
            savedOverrideCells.RemoveAt(last);
            savedOverrideVectors.RemoveAt(last);
        }

        private static readonly List<IntVec3?> savedOverrideCells = new List<IntVec3?>();
        private static readonly List<Vector3> savedOverrideVectors = new List<Vector3>();

        [HarmonyPatch(typeof(UI), nameof(UI.MouseCell))]
        private static class MouseCellPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref IntVec3 __result)
            {
                if (overrideCell.HasValue)
                {
                    __result = overrideCell.Value;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(UI), nameof(UI.MouseMapPosition))]
        private static class MouseMapPositionPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref Vector3 __result)
            {
                if (overrideCell.HasValue)
                {
                    __result = overrideCellVector;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(GenWorld), nameof(GenWorld.MouseTile))]
        private static class MouseTilePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref PlanetTile __result)
            {
                if (overrideTile.HasValue)
                {
                    __result = overrideTile.Value;
                    return false;
                }
                return true;
            }
        }
    }
}
