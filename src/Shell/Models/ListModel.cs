using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Cursor over a flat list: the navigation math 72 files currently
    /// hand-roll. Holds position only — items, labels, and announcements stay
    /// with the owning scope. Wrap mirrors the WrapNavigation setting (off =
    /// stop at boundaries and report AtEdge so the scope can speak the
    /// canonical edge phrase).
    ///
    /// PURE: links into the test project.
    /// </summary>
    public sealed class ListModel
    {
        private int count;
        private int index;

        /// <summary>Wrap from last to first and vice versa (the WrapNavigation setting).</summary>
        public bool Wrap;

        public ListModel(int count = 0, bool wrap = false)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            this.count = count;
            Wrap = wrap;
            index = count > 0 ? 0 : -1;
        }

        public int Count
        {
            get { return count; }
        }

        /// <summary>Current 0-based index, or -1 when the list is empty.</summary>
        public int Index
        {
            get { return index; }
        }

        /// <summary>1-based position for announcements ("3 of 7"), 0 when empty.</summary>
        public int Position
        {
            get { return index + 1; }
        }

        public bool IsEmpty
        {
            get { return count == 0; }
        }

        /// <summary>
        /// Item count changed (filter, refresh). Keeps the cursor on the same
        /// index where possible, clamping to the new end.
        /// </summary>
        public void SetCount(int newCount)
        {
            if (newCount < 0)
                throw new ArgumentOutOfRangeException(nameof(newCount));
            count = newCount;
            if (count == 0)
                index = -1;
            else if (index < 0)
                index = 0;
            else if (index >= count)
                index = count - 1;
        }

        public MoveResult MoveNext()
        {
            return MoveBy(1);
        }

        public MoveResult MovePrevious()
        {
            return MoveBy(-1);
        }

        /// <summary>Relative move; PageUp/PageDown pass ±pageSize.</summary>
        public MoveResult MoveBy(int delta)
        {
            if (count == 0)
                return new MoveResult(MoveKind.Empty, -1);
            int target = index + delta;
            if (target >= count)
            {
                if (Wrap && delta == 1)
                {
                    index = 0;
                    return new MoveResult(MoveKind.Wrapped, index);
                }
                if (index == count - 1)
                    return new MoveResult(MoveKind.AtEdge, index);
                index = count - 1;
                return new MoveResult(MoveKind.Moved, index);
            }
            if (target < 0)
            {
                if (Wrap && delta == -1)
                {
                    index = count - 1;
                    return new MoveResult(MoveKind.Wrapped, index);
                }
                if (index == 0)
                    return new MoveResult(MoveKind.AtEdge, index);
                index = 0;
                return new MoveResult(MoveKind.Moved, index);
            }
            if (target == index)
                return new MoveResult(MoveKind.AtEdge, index);
            index = target;
            return new MoveResult(MoveKind.Moved, index);
        }

        public MoveResult MoveFirst()
        {
            if (count == 0)
                return new MoveResult(MoveKind.Empty, -1);
            if (index == 0)
                return new MoveResult(MoveKind.AtEdge, index);
            index = 0;
            return new MoveResult(MoveKind.Moved, index);
        }

        public MoveResult MoveLast()
        {
            if (count == 0)
                return new MoveResult(MoveKind.Empty, -1);
            if (index == count - 1)
                return new MoveResult(MoveKind.AtEdge, index);
            index = count - 1;
            return new MoveResult(MoveKind.Moved, index);
        }

        /// <summary>Absolute jump (typeahead match, mouse click on a row).</summary>
        public MoveResult MoveTo(int target)
        {
            if (count == 0)
                return new MoveResult(MoveKind.Empty, -1);
            if (target < 0 || target >= count)
                throw new ArgumentOutOfRangeException(nameof(target));
            if (target == index)
                return new MoveResult(MoveKind.AtEdge, index);
            index = target;
            return new MoveResult(MoveKind.Moved, index);
        }
    }
}
