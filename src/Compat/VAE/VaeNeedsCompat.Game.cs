using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers the Needs-tree detail extender that surfaces Vanilla Aspirations
    /// Expanded's per-pawn aspirations under the Fulfillment need. See
    /// <see cref="VaeCompat"/> for the underlying reflection surface.
    /// </summary>
    internal static class VaeNeedsCompat
    {
        public static void RegisterNeedExtenders()
        {
            if (!VaeCompat.Ready)
                return;

            PawnNeedsAdapter.RegisterDetailExtender(BuildAspirationChildren);
            Log.Message("[RimWorld Access] VAE compat: registered fulfillment-need extender");
        }

        /// <summary>
        /// Adds one "Aspirations: N of M completed" SubCategory to the Fulfillment
        /// need, with one child per aspiration (its completion state, and its full
        /// tooltip text as expandable detail lines).
        /// </summary>
        private static void BuildAspirationChildren(InspectionTreeItem needItem, Pawn pawn, Need need)
        {
            if (!VaeCompat.IsFulfillmentNeed(need))
                return;

            try
            {
                List<VaeCompat.AspirationEntry> entries = VaeCompat.GetAspirations(need, pawn);
                if (entries == null || entries.Count == 0)
                    return;

                int completedCount = entries.Count(e => e.Complete);
                string header = "RimWorldAccess.Compat.Vae.AspirationsProgress".Translate(completedCount, entries.Count);

                var subCategory = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = header,
                    ExpandedLabel = header,
                    IndentLevel = needItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };

                int childIndent = subCategory.IndentLevel + 1;
                var perAspirationLabels = new List<string>();
                foreach (VaeCompat.AspirationEntry entry in entries)
                {
                    string label = entry.Label;
                    if (entry.Complete)
                        label += "RimWorldAccess.Compat.Vae.StateCompleted".Translate();
                    perAspirationLabels.Add(label);

                    var item = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = label,
                        ExpandedLabel = label,
                        IndentLevel = childIndent,
                        IsExpandable = true,
                        IsExpanded = false
                    };

                    InspectNodeFactory.DetailLines(item, entry.Tooltip, redundantWithLabel: entry.Label);
                    if (item.Children.Count == 0)
                        item.IsExpandable = false;

                    InspectNodeFactory.Attach(subCategory, item);
                }

                // Fold only the per-aspiration labels (not their detail lines) into the
                // SubCategory's collapsed label, JoyTolerances-style — keeping the
                // Fulfillment need's own collapsed-label fold (done by the caller) to a
                // names summary rather than five full descriptions.
                subCategory.Label += ": " + string.Join(". ", perAspirationLabels);

                InspectNodeFactory.Attach(needItem, subCategory);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VaeNeedsCompat.BuildAspirationChildren failed: {ex.Message}");
            }
        }
    }
}
