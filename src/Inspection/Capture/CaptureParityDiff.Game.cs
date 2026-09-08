using System;
using System.Collections.Generic;
using System.Text;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The drift net over hand-adapted inspect tabs: captures a known tab, diffs what vanilla drew
    /// against what the accessible tree presents, and returns the visual rows nothing in the tree
    /// mirrors, for the tree builder's "Also shown on this tab" section. Capture is read-only, so
    /// those rows are audible but not operable; the fix for a persistently-missing row is to teach
    /// the tab's adapter to present it richly. Shares <see cref="CaptureTextNormalization"/> with
    /// the DEBUG <see cref="InspectTabCaptureOracle"/> so their verdicts never disagree.
    /// </summary>
    internal static class CaptureParityDiff
    {
        private static bool isBuildingShadowTree;
        private static InspectTabBase shadowTreeTab;

        /// <summary>
        /// True while <see cref="BuildNormalizedTreeText"/> expands a throwaway tree purely to
        /// read its text. The tree builder skips capture requests and parity sections during this
        /// window; without the guard the diff recurses into itself.
        /// </summary>
        internal static bool IsBuildingShadowTree
        {
            get { return isBuildingShadowTree; }
        }

        /// <summary>
        /// The tab whose containment basis the current shadow build is producing. The tree builder
        /// keeps THIS tab's category and every non-tab node and omits other tabs' categories: a row
        /// hand-authored under a different tab must not vouch for this tab's missing one.
        /// </summary>
        internal static InspectTabBase ShadowTreeTab
        {
            get { return shadowTreeTab; }
        }

        /// <summary>
        /// The accessible tree for <paramref name="obj"/> reduced to <paramref name="forTab"/>'s
        /// containment basis (the tab's own category plus every node no tab owns), fully expanded
        /// and folded to canonical form. Synthetic categories belong to the basis because vanilla
        /// cards paint content the tree files under them. Sets the shadow-tree flag for the build.
        /// </summary>
        internal static string BuildNormalizedTreeText(object obj, InspectTabBase forTab)
        {
            isBuildingShadowTree = true;
            shadowTreeTab = forTab;
            try
            {
                InspectionTreeItem root = InspectionTreeBuilder.BuildTree(new List<object> { obj });
                ExpandAll(root, 0);
                var sb = new StringBuilder();
                CollectText(root, sb);
                return CaptureTextNormalization.NormalizeForContainment(sb.ToString().StripTags());
            }
            finally
            {
                isBuildingShadowTree = false;
                shadowTreeTab = null;
            }
        }

        /// <summary>
        /// The visual rows <paramref name="tab"/> drew for <paramref name="obj"/> that the
        /// accessible tree does not present anywhere, in draw order. A cold capture cache yields
        /// an empty list.
        /// </summary>
        internal static List<FoldedRow> ComputeUnmirroredRows(object obj, InspectTabBase tab)
        {
            var result = new List<FoldedRow>();
            if (obj == null || tab == null)
            {
                return result;
            }
            if (!InspectTabCaptureService.TryGetParityCapture(obj, tab,
                    out List<CapturedWidget> widgets, out List<string> widgetTips)
                || widgets.Count == 0)
            {
                return result;
            }

            string treeText = BuildNormalizedTreeText(obj, tab);

            // Fold ONLY each band's unmirrored cells, with only their own tooltips: mirrored cells
            // are already presented somewhere in the tree, and their tooltips would drag those
            // rows' verbose tails into the section.
            int bandIndex = -1;
            foreach (List<int> band in CapturedRowFolder.BandsFor(widgets))
            {
                bandIndex++;
                List<int> unmirrored = null;
                List<int> fillableBarsInBand = null;
                bool bandHasUnmirroredCaption = false;
                foreach (int index in band)
                {
                    CapturedWidget widget = widgets[index];
                    if (widget.Kind == WidgetKind.FillableBar)
                    {
                        // Decided after the loop, once the band's caption verdict is known.
                        (fillableBarsInBand = fillableBarsInBand ?? new List<int>()).Add(index);
                        continue;
                    }
                    if (WidgetIsUnmirrored(widget, treeText))
                    {
                        (unmirrored = unmirrored ?? new List<int>()).Add(index);
                        bandHasUnmirroredCaption = true;
                    }
                }
                // A bare FillableBar never counts on its own: a bar carries a percentage, not
                // text, and a known tab's adapter usually words that value itself. A mod tab has
                // no adapter wording it anywhere, so dropping the bar would erase the only
                // rendering of its value. Ride along whenever this band's own caption is
                // unmirrored; stay silent otherwise.
                if (fillableBarsInBand != null && bandHasUnmirroredCaption)
                {
                    unmirrored = unmirrored ?? new List<int>();
                    unmirrored.AddRange(fillableBarsInBand);
                    unmirrored.Sort();
                }
                if (unmirrored == null)
                {
                    continue;
                }
                FoldedRow row = CapturedRowFolder.FoldSubset(widgets, unmirrored, widgetTips);
                if (row != null)
                {
                    // The band this row's cells came from, not its position in the section, so it
                    // rings off the same live banding as every other captured row.
                    row.BandIndex = bandIndex;
                    result.Add(row);
                }
            }
            return result;
        }

        /// <summary>
        /// A widget is unmirrored when at least one of its comparable fragment lines is absent
        /// from the tree text. FillableBar widgets are decided by their band's caption in
        /// <see cref="ComputeUnmirroredRows"/> instead, since a bar carries no text.
        /// </summary>
        private static bool WidgetIsUnmirrored(CapturedWidget widget, string treeText)
        {
            // SpokenLabel, not Label: an icon-only button named from its tooltip must be judged
            // against the name the tree would present, never against the texture asset its
            // capture label holds — that name matches nothing and would keep the row forever.
            string text = widget.Kind == WidgetKind.TextField ? widget.Text : widget.SpokenLabel;
            return CaptureTextNormalization.IsUnmirrored(text, treeText, s => s.StripTags());
        }

        /// <summary>
        /// Expands every expandable non-Action, non-overlay node to depth 10, swallowing builder
        /// exceptions. Shared with the DEBUG oracle so both read the same tree.
        /// </summary>
        internal static void ExpandAll(InspectionTreeItem item, int depth)
        {
            if (depth > 10)
            {
                return;
            }
            if (item.IsExpandable && item.Children.Count == 0
                && item.OnActivate != null && item.Type != InspectionTreeItem.ItemType.Action
                && !item.OpensOverlayMenu)
            {
                try
                {
                    item.OnActivate();
                }
                catch (Exception)
                {
                    // A builder that throws just contributes no text.
                }
            }
            foreach (InspectionTreeItem child in item.Children)
            {
                ExpandAll(child, depth + 1);
            }
        }

        /// <summary>
        /// Concatenates every node's Label, distinct ExpandedLabel and Description into
        /// <paramref name="sb"/>. Shared with the DEBUG oracle.
        /// </summary>
        internal static void CollectText(InspectionTreeItem item, StringBuilder sb)
        {
            sb.Append(item.Label).Append('\n');
            if (!string.IsNullOrEmpty(item.ExpandedLabel) && item.ExpandedLabel != item.Label)
            {
                sb.Append(item.ExpandedLabel).Append('\n');
            }
            if (!string.IsNullOrEmpty(item.Description))
            {
                sb.Append(item.Description).Append('\n');
            }
            foreach (InspectionTreeItem child in item.Children)
            {
                CollectText(child, sb);
            }
        }
    }
}
