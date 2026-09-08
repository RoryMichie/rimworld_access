using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reads the stat window's "affected stats" list off the mod's own draw routine. The
    /// window computes thirty-odd effect lines from its settings constants inline in
    /// DrawAffectedStatsContent, so re-deriving them here would be a second copy of every
    /// formula. Instead a Repaint postfix on the window's DoWindowContents re-runs that routine
    /// under the offscreen capture bracket with a rect tall enough that nothing scrolls out of
    /// view, and the scope reads the folded rows. Captured once per opening and again after
    /// each pending-value change, not every frame.
    /// </summary>
    internal static class IsekaiAffectedStatsCapture
    {
        private static readonly Dictionary<Window, List<string>> rowsByWindow = new Dictionary<Window, List<string>>();
        private static readonly HashSet<Window> dirty = new HashSet<Window>();
        private static MethodInfo drawMethod;
        private static FieldInfo scrollField;

        private const float CaptureWidth = 400f;
        private const float CaptureHeight = 4000f;

        public static void Register(Harmony harmony)
        {
            Type windowType = AccessTools.TypeByName("IsekaiLeveling.UI.Window_StatsAttribution");
            if (windowType == null)
                return;
            drawMethod = AccessTools.Method(windowType, "DrawAffectedStatsContent", new[] { typeof(Rect) });
            scrollField = AccessTools.Field(windowType, "statsScrollPosition");
            MethodInfo target = AccessTools.Method(windowType, "DoWindowContents", new[] { typeof(Rect) });
            if (drawMethod == null || scrollField == null || target == null)
            {
                ModLogger.Error("Isekai compat: affected-stats draw surface not found; the list stays unread");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(IsekaiAffectedStatsCapture), nameof(Postfix)));
        }

        internal static void MarkDirty(Window window)
        {
            if (window != null)
                dirty.Add(window);
        }

        internal static List<string> RowsFor(Window window)
        {
            return window != null && rowsByWindow.TryGetValue(window, out List<string> rows) ? rows : null;
        }

        internal static void Forget(Window window)
        {
            if (window == null)
                return;
            rowsByWindow.Remove(window);
            dirty.Remove(window);
        }

        public static void Postfix(Window __instance)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            if (rowsByWindow.ContainsKey(__instance) && !dirty.Contains(__instance))
                return;

            var widgets = new List<CapturedWidget>();
            object savedScroll = scrollField.GetValue(__instance);
            try
            {
                // MUTATION-C: mirrors InspectTabCaptureHarness's scroll zeroing for a capture pass;
                // DrawAffectedStatsContent feeds statsScrollPosition to BeginScrollView, and a zero
                // offset under an oversized rect keeps every line inside the capture clip. View
                // state only, restored below.
                scrollField.SetValue(__instance, Vector2.zero);
                bool ok = InspectTabCaptureHarness.TryCaptureDraw(
                    () => drawMethod.Invoke(__instance, new object[] { new Rect(0f, 0f, CaptureWidth, CaptureHeight) }),
                    new Vector2(CaptureWidth, CaptureHeight), widgets, out string error);
                if (!ok)
                {
                    ModLogger.LimitedError("Isekai affected-stats capture", new InvalidOperationException(error));
                    return;
                }
            }
            finally
            {
                // MUTATION-C: the restore half of the scroll zeroing above.
                scrollField.SetValue(__instance, savedScroll);
            }

            var rows = new List<string>();
            foreach (FoldedRow row in CapturedRowFolder.Fold(widgets))
            {
                if (!string.IsNullOrEmpty(row.Text))
                    rows.Add(row.Text);
            }
            rowsByWindow[__instance] = rows;
            dirty.Remove(__instance);
        }
    }
}
