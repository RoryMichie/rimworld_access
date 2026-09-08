using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>One presentation row inside an embedded table's body, described without GUI/game types.</summary>
    public struct TableCellCandidate
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;

        /// <summary>True for a row a player can operate (button, checkbox, radio…) — the fused row activates through its first interactive member.</summary>
        public bool Interactive;

        public string Label;
    }

    /// <summary>
    /// One fused visual row: the candidate indexes it absorbs (left-to-right),
    /// which member carries activation, and the spoken label composed from the
    /// members' own texts.
    /// </summary>
    public sealed class TableRowGroup
    {
        /// <summary>Members in visual left-to-right order (indexes into the caller's candidate list).</summary>
        public List<int> Members = new List<int>();

        /// <summary>The member activation rides on: the leftmost interactive member, or the leftmost member when none is interactive.</summary>
        public int PrimaryIndex;

        /// <summary>Member labels joined left-to-right with ". ", minus any label another member's label already contains.</summary>
        public string FusedLabel;
    }

    /// <summary>
    /// Fuses the per-cell fragments a vanilla <c>PawnTable</c> body draws into
    /// one presentation row per visual pawn row. The table draws COLUMN-major
    /// (every pawn's cell for column 1, then column 2…), so fragments arrive
    /// interleaved; grouping is purely geometric — rows whose vertical ranges
    /// overlap by more than half the shorter one's height share a band, the
    /// same "sits on the same visual row" test the presentation sort and
    /// <see cref="BandLabelInheritance"/> already apply.
    ///
    /// Label composition drops a member whose text another member's text
    /// already contains (the vanilla label column draws the pawn's short name
    /// as a click target right under its own full "Name, Title" label —
    /// "Sense" inside "Sense, Scientist"), so the fused row speaks each fact
    /// once. Callers decide WHICH candidates belong to a table body (by the
    /// body's own scroll container); this class never sees anything else, so
    /// no unrelated layout can be reshaped by it.
    /// </summary>
    public static class EmbeddedTableFusion
    {
        public static IReadOnlyList<TableRowGroup> Compute(IReadOnlyList<TableCellCandidate> cells)
        {
            var groups = new List<TableRowGroup>();
            int n = cells == null ? 0 : cells.Count;
            if (n == 0)
            {
                return groups;
            }

            // Order by top edge, then left edge, keeping original indexes.
            var order = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                order.Add(i);
            }
            order.Sort((a, b) =>
            {
                int byY = cells[a].Y.CompareTo(cells[b].Y);
                return byY != 0 ? byY : cells[a].X.CompareTo(cells[b].X);
            });

            // Chained banding: a new band starts when the next row no longer
            // overlaps the previous one — transitive, the standard shape.
            TableRowGroup current = null;
            TableCellCandidate previous = default;
            for (int k = 0; k < order.Count; k++)
            {
                int index = order[k];
                TableCellCandidate cell = cells[index];
                if (current == null || !SharesBand(cell, previous))
                {
                    current = new TableRowGroup();
                    groups.Add(current);
                }
                current.Members.Add(index);
                previous = cell;
            }

            foreach (TableRowGroup group in groups)
            {
                group.Members.Sort((a, b) => cells[a].X.CompareTo(cells[b].X));
                group.PrimaryIndex = group.Members[0];
                for (int m = 0; m < group.Members.Count; m++)
                {
                    if (cells[group.Members[m]].Interactive)
                    {
                        group.PrimaryIndex = group.Members[m];
                        break;
                    }
                }
                group.FusedLabel = ComposeLabel(cells, group.Members);
            }
            return groups;
        }

        private static bool SharesBand(TableCellCandidate a, TableCellCandidate b)
        {
            float overlap = System.Math.Min(a.Y + a.Height, b.Y + b.Height) - System.Math.Max(a.Y, b.Y);
            float shorter = System.Math.Min(a.Height, b.Height);
            return overlap > shorter * 0.5f;
        }

        private static string ComposeLabel(IReadOnlyList<TableCellCandidate> cells, List<int> members)
        {
            var parts = new List<string>(members.Count);
            for (int m = 0; m < members.Count; m++)
            {
                string label = (cells[members[m]].Label ?? "").Trim().TrimEnd('.');
                if (label.Length == 0)
                {
                    continue;
                }
                bool contained = false;
                for (int other = 0; other < members.Count && !contained; other++)
                {
                    if (other == m)
                    {
                        continue;
                    }
                    string otherLabel = (cells[members[other]].Label ?? "").Trim().TrimEnd('.');
                    if (otherLabel.Length <= label.Length)
                    {
                        continue;
                    }
                    contained = otherLabel.IndexOf(label, System.StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (contained)
                {
                    continue;
                }
                bool duplicate = false;
                for (int p = 0; p < parts.Count && !duplicate; p++)
                {
                    duplicate = string.Equals(parts[p], label, System.StringComparison.OrdinalIgnoreCase);
                }
                if (!duplicate)
                {
                    parts.Add(label);
                }
            }
            return string.Join(". ", parts);
        }
    }
}
