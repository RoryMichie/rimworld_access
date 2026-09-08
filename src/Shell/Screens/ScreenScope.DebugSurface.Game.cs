#if DEBUG
using System;
using System.Text;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Dev-bridge screen dump. Enumerates every content region, the captured-extras region, and the
    /// Buttons region — every row described through the SAME path a real focus announcement uses
    /// (<see cref="ScreenScope.DescribeContentItem"/>/<see cref="ScreenScope.DescribeExtrasRow"/>/
    /// <see cref="ScreenScope.DescribeActionRow"/> composed via
    /// <see cref="AnnouncementComposer.ComposeFocus"/> with
    /// <see cref="TextDialogShared.StandardComposeOptions"/>, mirroring
    /// <see cref="ScreenScope.AnnounceCurrent"/>'s own non-table branch exactly) — so the dump
    /// text matches what would actually be spoken. Never moves the model cursor and never calls
    /// TolkHelper; the only side effect is the same <see cref="ScreenScope.RefreshModel"/> every
    /// navigation handler already calls to keep the model in sync with live game state.
    /// </summary>
    public abstract partial class ScreenScope
    {
        private const int DebugSurfaceRowCap = 500;

        internal override string DebugDescribeSurface()
        {
            try
            {
                RefreshModel();
            }
            catch (Exception ex)
            {
                return "(scope " + Name + " RefreshModel threw: " + ex.GetType().Name + ")";
            }

            int contentRegions = ContentRegionCount;
            bool hasExtras = HasExtrasRegion();
            bool hasActions = HasActionsRegion();
            int actionsCount = capturedButtons.Count + DeclaredActionCount();

            int totalRows = 0;
            for (int region = 0; region < contentRegions; region++)
            {
                totalRows += ContentItemCount(region);
            }
            if (hasExtras)
            {
                totalRows += extrasRows.Count;
            }
            if (hasActions)
            {
                totalRows += actionsCount;
            }

            var sb = new StringBuilder();
            sb.Append("scope ").Append(Name);
            int rowsWritten = 0;
            bool truncated = false;

            for (int region = 0; region < contentRegions && !truncated; region++)
            {
                int items = ContentItemCount(region);
                AppendRegionHeader(sb, region, ContentRegionName(region), ContentRegionSearchable(region), items);
                for (int index = 0; index < items; index++)
                {
                    if (rowsWritten >= DebugSurfaceRowCap)
                    {
                        truncated = true;
                        break;
                    }
                    int capturedRegion = region;
                    int capturedIndex = index;
                    bool current = Model.RegionIndex == capturedRegion
                        && Model.CurrentRegion != null && Model.CurrentRegion.Index == capturedIndex;
                    AppendDescribedRow(sb, current, () => DescribeContentItem(capturedRegion, capturedIndex));
                    rowsWritten++;
                }
            }

            if (!truncated && hasExtras)
            {
                AppendRegionHeader(sb, contentRegions, ExtrasRegionName, true, extrasRows.Count);
                for (int index = 0; index < extrasRows.Count; index++)
                {
                    if (rowsWritten >= DebugSurfaceRowCap)
                    {
                        truncated = true;
                        break;
                    }
                    int capturedIndex = index;
                    bool current = Model.RegionIndex == contentRegions
                        && Model.CurrentRegion != null && Model.CurrentRegion.Index == capturedIndex;
                    AppendDescribedRow(sb, current, () => DescribeExtrasRow(capturedIndex));
                    rowsWritten++;
                }
            }

            if (!truncated && hasActions)
            {
                int actionsRegion = ActionsRegionIndex();
                AppendRegionHeader(sb, actionsRegion, ActionsRegionName, true, actionsCount);
                for (int index = 0; index < actionsCount; index++)
                {
                    if (rowsWritten >= DebugSurfaceRowCap)
                    {
                        truncated = true;
                        break;
                    }
                    int capturedIndex = index;
                    bool current = Model.RegionIndex == actionsRegion
                        && Model.CurrentRegion != null && Model.CurrentRegion.Index == capturedIndex;
                    AppendDescribedRow(sb, current, () => DescribeActionRow(capturedIndex));
                    rowsWritten++;
                }
            }

            if (rowsWritten < totalRows)
            {
                sb.Append("\n(truncated: ").Append(totalRows - rowsWritten).Append(" more rows)");
            }

            string extra = DebugDescribeSurfaceExtra();
            if (!string.IsNullOrEmpty(extra))
            {
                sb.Append('\n').Append(extra);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Opt-in extra trailing line for a subclass's own dump-worthy state that isn't a region
        /// (e.g. <see cref="TreeRegionScope"/>'s visible-row count and submenu mode). Null (the
        /// default) adds nothing.
        /// </summary>
        protected virtual string DebugDescribeSurfaceExtra()
        {
            return null;
        }

        /// <summary>
        /// The current row's composed announcement text alone (no region header, no other rows) —
        /// for <see cref="ShellDev.InjectText"/>'s trailing "what does the row say now" line.
        /// Mirrors <see cref="AnnounceCurrent"/>'s non-table branch exactly, minus actually
        /// speaking it.
        /// </summary>
        internal string DebugDescribeCurrentRow()
        {
            try
            {
                RefreshModel();
            }
            catch (Exception ex)
            {
                string refreshFailure = "(RefreshModel threw: " + ex.GetType().Name + ")";
                return refreshFailure;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || Model.CurrentRegionIsEmpty)
            {
                string empty = "(no current row)";
                return empty;
            }
            try
            {
                ElementDescription d = DescribeCurrent(region);
                return AnnouncementComposer.ComposeFocus(
                    d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions());
            }
            catch (Exception ex)
            {
                string describeFailure = "(describe threw: " + ex.GetType().Name + ")";
                return describeFailure;
            }
        }

        private static void AppendRegionHeader(StringBuilder sb, int region, string name, bool searchable, int itemCount)
        {
            sb.Append('\n').Append("region ").Append(region).Append(": ").Append(name ?? "")
              .Append(" [searchable=").Append(searchable).Append("] (").Append(itemCount).Append(" items)");
        }

        /// <summary>
        /// Appends one described row, marking the model's own cursor row with "» ". Guarded so one
        /// bad row's describe throwing can't kill the whole dump.
        /// </summary>
        private static void AppendDescribedRow(StringBuilder sb, bool current, Func<ElementDescription> describe)
        {
            sb.Append('\n').Append(current ? "  » " : "    ");
            try
            {
                ElementDescription d = describe() ?? new ElementDescription();
                sb.Append(AnnouncementComposer.ComposeFocus(
                    d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
            }
            catch (Exception ex)
            {
                sb.Append("(describe threw: ").Append(ex.GetType().Name).Append(")");
            }
        }
    }
}
#endif
