using System.Collections.Generic;
using System.Linq;

namespace RimWorldAccess.Shell
{
    /// <summary>One row's geometry as <see cref="PaneOrder"/> needs it, described without any GUI/game types.</summary>
    public struct PaneOrderRow
    {
        /// <summary>Opaque identity of the row's clipping context — only equality is used, exactly like <see cref="BandLabelCandidate.ClipId"/>.</summary>
        public int ClipId;

        public float X;
        public float Y;
    }

    /// <summary>
    /// Groups presentation rows by their capture clip (Colony Manager
    /// Redux's three side-by-side scroll panes read zipper-fashion because the existing sort
    /// orders every row by raw screen Y-then-X across the WHOLE window, with no notion that
    /// different clips are different visual panes). Computes, for each row, the RANK of the
    /// pane (clip) it belongs to — panes ordered top-to-bottom then left-to-right by the
    /// minimum (Y, X) among the rows captured in that clip.
    ///
    /// <see cref="GuiSpace.ClipKey.VisibleRect"/> is in the clip's own LOCAL space (see that
    /// class's remarks), not screen space, so a clip cannot supply its own screen-space
    /// position directly — the member rows' own (already screen-space) rects are the only
    /// reliable source of where a pane sits, which is why this never looks past them.
    ///
    /// Callers combine the returned rank as the PRIMARY sort key ahead of their existing
    /// Y-then-X visual key; two rows sharing a clip always tie here, so their relative order is
    /// decided entirely by that secondary key — today's sort is unchanged for a single-pane
    /// window and unchanged WITHIN any one pane of a multi-pane window.
    /// </summary>
    public static class PaneOrder
    {
        /// <summary>
        /// Computes one pane rank per row in <paramref name="rows"/>, index-aligned with the
        /// input. Never null. A single distinct <see cref="PaneOrderRow.ClipId"/> across every
        /// row (including the "no clip info at all" case, where a caller assigns every row the
        /// same id) yields rank 0 for all of them — the no-op case that leaves a caller's
        /// existing secondary sort as the sole ordering, matching today's behavior exactly.
        /// </summary>
        public static IReadOnlyList<int> ComputePaneRanks(IReadOnlyList<PaneOrderRow> rows)
        {
            int n = rows == null ? 0 : rows.Count;
            var ranks = new int[n];
            if (n == 0)
            {
                return ranks;
            }

            var clipMinY = new Dictionary<int, float>();
            var clipMinX = new Dictionary<int, float>();
            for (int i = 0; i < n; i++)
            {
                PaneOrderRow row = rows[i];
                float y;
                if (!clipMinY.TryGetValue(row.ClipId, out y) || row.Y < y)
                {
                    clipMinY[row.ClipId] = row.Y;
                }
                float x;
                if (!clipMinX.TryGetValue(row.ClipId, out x) || row.X < x)
                {
                    clipMinX[row.ClipId] = row.X;
                }
            }

            List<int> paneOrder = clipMinY.Keys
                .OrderBy(id => clipMinY[id])
                .ThenBy(id => clipMinX[id])
                .ToList();

            var rank = new Dictionary<int, int>();
            for (int i = 0; i < paneOrder.Count; i++)
            {
                rank[paneOrder[i]] = i;
            }

            for (int i = 0; i < n; i++)
            {
                ranks[i] = rank[rows[i].ClipId];
            }
            return ranks;
        }
    }
}
