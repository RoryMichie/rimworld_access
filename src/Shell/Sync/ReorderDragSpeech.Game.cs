using System;
using HarmonyLib;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Speaks where a dragged row would land, which is what the insertion line
    /// (<c>ReorderableWidget.DrawLine</c>) tells a sighted player. One utterance per change of
    /// drop position, the same edge at which the drawn line moves.
    ///
    /// The observation point is <c>CurrentInsertNear</c>, because
    /// <c>ReorderableWidgetOnGUI_AfterWindowStack</c> clears the registered-row lists at the end of
    /// every frame: after it returns there is nothing left to read.
    ///
    /// Announce-only. Every index comes from the widget's own bookkeeping and its own
    /// index helpers; nothing here reorders anything.
    /// </summary>
    [HarmonyPatch(typeof(ReorderableWidget), "CurrentInsertNear")]
    internal static class ReorderDragSpeech
    {
        private static readonly AccessTools.FieldRef<int> DraggingReorderable =
            AccessTools.StaticFieldRefAccess<int>(
                AccessTools.Field(typeof(ReorderableWidget), "draggingReorderable"));

        private static readonly Func<int, int> IndexWithinGroup =
            AccessTools.MethodDelegate<Func<int, int>>(
                AccessTools.Method(typeof(ReorderableWidget), "GetIndexWithinGroup"));

        private static readonly Func<int, int> LastIndexInGroup =
            AccessTools.MethodDelegate<Func<int, int>>(
                AccessTools.Method(typeof(ReorderableWidget), "FindLastReorderableIndexWithinGroup"));

        private static bool spokenValid;
        private static int spokenPosition;

        [HarmonyPostfix]
        public static void Postfix(int __result, ref bool toTheLeft)
        {
            try
            {
                if (!ReorderableWidget.Dragging || __result < 0)
                {
                    spokenValid = false;
                    return;
                }

                int groupID = ReorderableWidget.GetDraggedFromGroupID;
                int from = ReorderableWidget.GetDraggedIndex;
                int nearIndex = IndexWithinGroup(__result);
                if (from < 0 || nearIndex < 0)
                {
                    spokenValid = false;
                    return;
                }

                // Vanilla's own destination arithmetic (ReorderableWidget.cs:153): an
                // insert-before index, which the reorder callbacks then resolve against the
                // pre-removal list. A drop vanilla treats as a no-op reports the row as it stands.
                int to = (__result == DraggingReorderable()) ? from : (toTheLeft ? nearIndex : nearIndex + 1);
                int position = (to == from || to == from + 1) ? from : (from < to ? to - 1 : to);
                int total = IndexWithinGroup(LastIndexInGroup(groupID)) + 1;
                if (total <= 0)
                {
                    spokenValid = false;
                    return;
                }

                if (spokenValid && position == spokenPosition)
                {
                    return;
                }
                spokenValid = true;
                spokenPosition = position;

                TolkHelper.Speak(
                    "RimWorldAccess.Common.Drag.ReorderPosition".Loc(position + 1, total),
                    SpeechPriority.High);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Reorder drag speech error", ex);
            }
        }
    }
}
