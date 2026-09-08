using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The shared grammar for "what is selected now": label, location context, and for a pawn
    /// its cover and current job. Used by the pawn-cycling keys and by the map click bracket so
    /// the two never drift apart.
    /// </summary>
    internal static class MapSelectionAnnouncer
    {
        /// <summary>
        /// Describes a single selected object, or null when the object has nothing to say.
        /// </summary>
        internal static string Describe(object selected)
        {
            if (selected is Pawn pawn)
                return DescribePawn(pawn);
            if (selected is Zone zone)
                return zone.label;
            if (selected is Thing thing)
                return DescribeThing(thing);
            if (selected is IRenameable renameable)
                return renameable.RenamableLabel;
            return null;
        }

        /// <summary>
        /// Describes a whole selection as one announcement: the object itself when there is one,
        /// a count when there are several, the cleared phrase when there are none.
        /// </summary>
        internal static string DescribeSelection(IReadOnlyList<object> selection)
        {
            if (selection == null || selection.Count == 0)
                return "RimWorldAccess.Map.Selection.Cleared".Translate().ToString();
            if (selection.Count == 1)
                return Describe(selection[0]);
            return "RimWorldAccess.Map.Selection.Multiple".Translate(selection.Count).ToString();
        }

        private static string DescribePawn(Pawn pawn)
        {
            string currentTask = pawn.GetJobReport();
            if (string.IsNullOrEmpty(currentTask))
                currentTask = (string)"RimWorldAccess.Map.Pawn.Idle".Translate();

            string subject = pawn.LabelShort;
            if (pawn.Spawned && pawn.Map != null)
            {
                string location = TileInfoHelper.GetLocationContextPlain(pawn.Position, pawn.Map);
                if (!string.IsNullOrEmpty(location))
                    subject += $", {location}";
            }
            string coverInfo = (RimWorldAccessMod_Settings.Settings?.ShowCoverInfo ?? true)
                ? CoverHelper.GetCoverInfo(pawn)
                : null;
            return !string.IsNullOrEmpty(coverInfo)
                ? "RimWorldAccess.Map.Pawn.SelectionWithCover".Translate(subject, coverInfo, currentTask).ToString()
                : "RimWorldAccess.Map.Pawn.Selection".Translate(subject, currentTask).ToString();
        }

        private static string DescribeThing(Thing thing)
        {
            string label = thing.LabelCap;
            if (string.IsNullOrEmpty(label))
                return null;
            if (thing.Spawned && thing.Map != null)
            {
                string location = TileInfoHelper.GetLocationContextPlain(thing.Position, thing.Map);
                if (!string.IsNullOrEmpty(location))
                    return "RimWorldAccess.Map.Thing.Selection".Translate(label, location).ToString();
            }
            return label;
        }
    }
}
