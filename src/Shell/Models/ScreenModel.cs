using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One region's shape for <see cref="ScreenModel.SetRegions(IReadOnlyList{RegionSpec}, bool)"/>:
    /// its item (row) count, plus a positive ColumnCount for a TABLE region; 0 keeps it a flat
    /// list. A table region's item count already INCLUDES the header row — the model is
    /// header-agnostic, and the scope owns the row-0-is-headers convention. AlwaysNavigable
    /// exempts the region from the empty-region rule
    /// (<see cref="ScreenModel.IsRegionNavigable"/>).
    /// </summary>
    public readonly struct RegionSpec
    {
        public readonly int ItemCount;
        public readonly int ColumnCount;
        public readonly bool AlwaysNavigable;

        public RegionSpec(int itemCount, int columnCount = 0, bool alwaysNavigable = false)
        {
            if (itemCount < 0)
                throw new ArgumentOutOfRangeException(nameof(itemCount));
            if (columnCount < 0)
                throw new ArgumentOutOfRangeException(nameof(columnCount));
            ItemCount = itemCount;
            ColumnCount = columnCount;
            AlwaysNavigable = alwaysNavigable;
        }

        public bool IsTable
        {
            get { return ColumnCount > 0; }
        }
    }

    /// <summary>
    /// The standard screen shape; the ScreenScope header is its other half. A screen is an ordered
    /// set of REGIONS — tabs, sections, panels or columns — and each region is a flat list of
    /// items, or a rows × columns grid whose row cursor is that same ListModel and whose
    /// column/sort state rides a <see cref="TableModel"/>. Tab/Shift+Tab move the region cursor;
    /// Up/Down move the item cursor inside the current region.
    ///
    /// Every region keeps its own <see cref="ListModel"/> across region switches, so "remember my
    /// place in each tab" is inherent. Items, labels, and announcements stay with the owning scope;
    /// this type holds cursors only.
    ///
    /// Policy flags (<see cref="WrapRegions"/>, <see cref="RememberPositions"/>) are written each
    /// refresh by the owning ScreenScope from its virtual policy properties, so behavior
    /// differences between screens are named overrides, never divergent copies of the cursor math.
    ///
    /// PURE: links into the test project.
    /// </summary>
    public sealed class ScreenModel
    {
        /// <summary>Region cycling wraps last-to-first (the tab-strip convention).</summary>
        public bool WrapRegions = true;

        /// <summary>Keep each region's item cursor across region switches; off rewinds a region to its first item on every entry.</summary>
        public bool RememberPositions = true;

        /// <summary>
        /// A region Tab/Shift+Tab step over and announcements do not number, or -1 for none. It
        /// stays fully navigable by direct jump, which is how reader flow reaches it, since that
        /// goes through <see cref="MoveToRegion"/> rather than the cycle.
        ///
        /// Written each refresh by the owning ScreenScope, which points it at the Buttons region on
        /// screens whose window draws its buttons inside the content box rather than a footer:
        /// those buttons are part of what the player is reading, not a tab beside it.
        /// </summary>
        public int CycleSkipRegion = -1;

        private readonly List<ListModel> lists = new List<ListModel>();
        private readonly List<TableModel> tables = new List<TableModel>();
        private readonly List<TableModel> parkedTables = new List<TableModel>();
        private readonly List<bool> alwaysNavigable = new List<bool>();
        private TabSetModel regions;

        /// <summary>
        /// Structural region count, empty regions included: the number scopes index against, since
        /// a region's slot never moves. Announcements use <see cref="NonEmptyRegionCount"/>
        /// instead, which numbers only the regions Tab can reach.
        /// </summary>
        public int RegionCount
        {
            get { return lists.Count; }
        }

        /// <summary>Current 0-based region index, or -1 while the screen has no regions.</summary>
        public int RegionIndex
        {
            get { return regions == null ? -1 : regions.Index; }
        }

        /// <summary>1-based structural region position, 0 when the screen has no regions. Announcements use <see cref="NonEmptyRegionPosition"/>.</summary>
        public int RegionPosition
        {
            get { return regions == null ? 0 : regions.Position; }
        }

        /// <summary>
        /// A region the user cannot enter because it holds nothing: navigation skips it and
        /// announced positions do not count it. A table region's header row is not an item, so a
        /// header-only table region is empty too. Out-of-range indexes read as empty, so callers
        /// can probe freely.
        /// </summary>
        public bool IsRegionEmpty(int index)
        {
            if (index < 0 || index >= lists.Count)
                return true;
            return lists[index].Count <= (tables[index] != null ? 1 : 0);
        }

        /// <summary>True while the region cursor rests on an empty region: what the owning scope reconciles after every refresh, and a state it can only be left in when EVERY region is empty or the region is always-navigable.</summary>
        public bool CurrentRegionIsEmpty
        {
            get { return regions == null || IsRegionEmpty(regions.Index); }
        }

        /// <summary>Whether any region holds an item, regardless of navigability.</summary>
        public bool AnyRegionHasItems
        {
            get
            {
                for (int i = 0; i < lists.Count; i++)
                {
                    if (!IsRegionEmpty(i))
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Whether a region exists for navigation: it holds something, or was declared
        /// always-navigable, which is how a switch-style screen keeps a pane reachable before its
        /// selection fills it (<c>ScreenScope.ContentRegionAlwaysNavigable</c>). Tab cycling,
        /// announced positions and direct jumps ask this; relocation off an emptied region
        /// deliberately does not, so the cursor is never sent into an empty region on its own.
        /// </summary>
        public bool IsRegionNavigable(int index)
        {
            if (index < 0 || index >= lists.Count)
                return false;
            return !IsRegionEmpty(index) || alwaysNavigable[index];
        }

        /// <summary>How many regions the tab cycle actually visits — the "of y" every announcement says aloud.</summary>
        public int NonEmptyRegionCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < lists.Count; i++)
                {
                    if (IsRegionInCycle(i))
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// 1-based position of the current region among those the tab cycle visits; 0 when the
        /// current region is empty or skipped, which is how a screen announces its skipped Buttons
        /// region by name with no tab ordinal.
        /// </summary>
        public int NonEmptyRegionPosition
        {
            get
            {
                if (regions == null || !IsRegionInCycle(regions.Index))
                    return 0;
                int position = 0;
                for (int i = 0; i <= regions.Index; i++)
                {
                    if (IsRegionInCycle(i))
                        position++;
                }
                return position;
            }
        }

        /// <summary>A region Tab reaches: it exists for navigation and is not the skipped one.</summary>
        private bool IsRegionInCycle(int index)
        {
            return index != CycleSkipRegion && IsRegionNavigable(index);
        }

        /// <summary>The current region's item cursor, or null while the screen has no regions.</summary>
        public ListModel CurrentRegion
        {
            get { return regions == null ? null : lists[regions.Index]; }
        }

        public ListModel Region(int index)
        {
            return lists[index];
        }

        /// <summary>
        /// The current region's grid state — column cursor and sort cycle — or null for a flat
        /// list. Its Rows IS <see cref="CurrentRegion"/>: one row cursor per region, always.
        /// </summary>
        public TableModel CurrentTable
        {
            get { return regions == null ? null : tables[regions.Index]; }
        }

        public TableModel Table(int index)
        {
            return tables[index];
        }

        /// <summary>
        /// Declares the screen's regions by item count, in region order, all as flat lists. Safe
        /// to call every refresh; the RegionSpec overload carries the full semantics.
        /// </summary>
        public void SetRegions(IReadOnlyList<int> itemCounts, bool itemWrap)
        {
            if (itemCounts == null)
                throw new ArgumentNullException(nameof(itemCounts));
            var specs = new RegionSpec[itemCounts.Count];
            for (int i = 0; i < itemCounts.Count; i++)
            {
                specs[i] = new RegionSpec(itemCounts[i]);
            }
            SetRegions(specs, itemWrap);
        }

        /// <summary>
        /// Declares the screen's regions, in region order. Safe to call every refresh: existing
        /// regions keep their item and column cursors under
        /// <see cref="ListModel.SetCount"/> clamp semantics, the region cursor clamps when regions
        /// disappear, and <paramref name="itemWrap"/> is re-applied to every region, rows and
        /// columns alike, so a live settings change takes effect immediately. A region changing
        /// kind between refreshes never loses its row cursor, and its grid state is PARKED rather
        /// than dropped: the submenu pattern flips a table region to a flat option list and back,
        /// and the table the user returns to must still be the table they left.
        /// </summary>
        public void SetRegions(IReadOnlyList<RegionSpec> specs, bool itemWrap)
        {
            if (specs == null)
                throw new ArgumentNullException(nameof(specs));

            for (int i = 0; i < specs.Count; i++)
            {
                if (i < lists.Count)
                {
                    lists[i].SetCount(specs[i].ItemCount);
                }
                else
                {
                    lists.Add(new ListModel(specs[i].ItemCount));
                    tables.Add(null);
                    parkedTables.Add(null);
                    alwaysNavigable.Add(false);
                }
                lists[i].Wrap = itemWrap;
                alwaysNavigable[i] = specs[i].AlwaysNavigable;

                if (!specs[i].IsTable)
                {
                    if (tables[i] != null)
                    {
                        parkedTables[i] = tables[i];
                        tables[i] = null;
                    }
                }
                else if (tables[i] == null)
                {
                    if (parkedTables[i] != null)
                    {
                        tables[i] = parkedTables[i];
                        parkedTables[i] = null;
                        tables[i].SetColumnCount(specs[i].ColumnCount);
                        tables[i].WrapColumns = itemWrap;
                    }
                    else
                    {
                        tables[i] = new TableModel(lists[i], specs[i].ColumnCount, itemWrap);
                    }
                }
                else
                {
                    tables[i].SetColumnCount(specs[i].ColumnCount);
                    tables[i].WrapColumns = itemWrap;
                }
            }
            if (lists.Count > specs.Count)
            {
                lists.RemoveRange(specs.Count, lists.Count - specs.Count);
                tables.RemoveRange(specs.Count, tables.Count - specs.Count);
                parkedTables.RemoveRange(specs.Count, parkedTables.Count - specs.Count);
                alwaysNavigable.RemoveRange(specs.Count, alwaysNavigable.Count - specs.Count);
            }

            if (lists.Count == 0)
            {
                regions = null;
            }
            else if (regions == null)
            {
                regions = new TabSetModel(lists.Count);
            }
            else
            {
                regions.SetCount(lists.Count);
            }
        }

        public MoveResult NextRegion()
        {
            return MoveRegion(true);
        }

        public MoveResult PreviousRegion()
        {
            return MoveRegion(false);
        }

        /// <summary>
        /// Direct jump, for per-screen region hotkeys, a drill-in flow, or a click on a real tab.
        /// An empty target refuses the jump and reports <see cref="MoveKind.Empty"/>, leaving the
        /// cursor where it is; an always-navigable region is a legitimate target even while empty.
        /// </summary>
        public MoveResult MoveToRegion(int target)
        {
            if (regions == null)
                return new MoveResult(MoveKind.Empty, -1);
            if (!IsRegionNavigable(target))
                return new MoveResult(MoveKind.Empty, regions.Index);
            return JumpToRegion(target);
        }

        /// <summary>
        /// Moves the cursor off an empty region onto the nearest one holding something: the
        /// previous non-empty region, else the next, which is how the trailing extras and Buttons
        /// regions get picked up. Reports <see cref="MoveKind.Empty"/> when every region is empty,
        /// leaving the cursor in place so the screen still has somewhere to speak from. Emptiness,
        /// not navigability, is the test: an always-navigable region is somewhere the user can go,
        /// never somewhere the cursor is sent to stand with nothing in it.
        /// </summary>
        public MoveResult MoveToNearestNonEmptyRegion()
        {
            if (regions == null)
                return new MoveResult(MoveKind.Empty, -1);
            int from = regions.Index;
            for (int i = from - 1; i >= 0; i--)
            {
                if (!IsRegionEmpty(i))
                    return JumpToRegion(i);
            }
            for (int i = from + 1; i < lists.Count; i++)
            {
                if (!IsRegionEmpty(i))
                    return JumpToRegion(i);
            }
            return new MoveResult(MoveKind.Empty, from);
        }

        private MoveResult JumpToRegion(int target)
        {
            MoveResult result = regions.MoveTo(target);
            ApplyPositionPolicy(result);
            return result;
        }

        private MoveResult MoveRegion(bool forward)
        {
            if (regions == null)
                return new MoveResult(MoveKind.Empty, -1);
            regions.Wrap = WrapRegions;
            bool wrapped;
            int target = NextNonEmptyRegion(regions.Index, forward, out wrapped);
            if (target < 0)
                return new MoveResult(MoveKind.AtEdge, regions.Index);
            regions.MoveTo(target);
            MoveResult result = new MoveResult(wrapped ? MoveKind.Wrapped : MoveKind.Moved, target);
            ApplyPositionPolicy(result);
            return result;
        }

        /// <summary>
        /// The next reachable region in the given direction: regions outside the cycle — empty
        /// without the always-navigable flag, or the skipped one — are stepped over. Returns -1
        /// when there is nowhere to go — no other reachable region, or the strip's edge with
        /// wrapping off — which the caller reports as AtEdge.
        /// </summary>
        private int NextNonEmptyRegion(int from, bool forward, out bool wrapped)
        {
            wrapped = false;
            int step = forward ? 1 : -1;
            int index = from;
            for (int i = 0; i < lists.Count - 1; i++)
            {
                int next = index + step;
                if (next < 0 || next >= lists.Count)
                {
                    if (!WrapRegions)
                        return -1;
                    next = forward ? 0 : lists.Count - 1;
                    wrapped = true;
                }
                index = next;
                if (IsRegionInCycle(index))
                    return index;
            }
            return -1;
        }

        private void ApplyPositionPolicy(MoveResult result)
        {
            if (RememberPositions || result.Kind == MoveKind.AtEdge || result.Kind == MoveKind.Empty)
                return;
            ListModel entered = CurrentRegion;
            if (entered != null && !entered.IsEmpty)
            {
                entered.MoveFirst();
            }
            TableModel enteredTable = CurrentTable;
            if (enteredTable != null && enteredTable.ColumnIndex != 0)
            {
                enteredTable.MoveToColumn(0);
            }
        }
    }
}
