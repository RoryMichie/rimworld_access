using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Cursor over a tab strip — the switcher the legacy screens hand-roll.
    /// Tab cycling wraps by default (Tab past the last tab returns to the
    /// first, the majority behavior and vanilla's own); ScreenModel drives
    /// <see cref="Wrap"/> from screen policy so a non-wrapping screen is a
    /// one-line override, not a divergent copy. Tab labels, contents, and the
    /// per-tab child model stay with the owning scope; announcements use the
    /// Tab element role ("Hair. tab, selected. 1 of 4").
    ///
    /// PURE: links into the test project.
    /// </summary>
    public sealed class TabSetModel
    {
        private int count;
        private int index;

        /// <summary>Wrap from last to first and vice versa. Default true (the tab-strip convention).</summary>
        public bool Wrap = true;

        public TabSetModel(int count)
        {
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "A tab set needs at least one tab.");
            this.count = count;
        }

        public int Count
        {
            get { return count; }
        }

        /// <summary>Current 0-based tab index.</summary>
        public int Index
        {
            get { return index; }
        }

        /// <summary>1-based position for announcements ("2 of 4").</summary>
        public int Position
        {
            get { return index + 1; }
        }

        /// <summary>Tab set changed (conditional tabs appear/disappear); cursor clamps.</summary>
        public void SetCount(int newCount)
        {
            if (newCount < 1)
                throw new ArgumentOutOfRangeException(nameof(newCount));
            count = newCount;
            if (index >= count)
                index = count - 1;
        }

        public MoveResult Next()
        {
            if (count == 1)
                return new MoveResult(MoveKind.AtEdge, index);
            bool atLast = index == count - 1;
            if (atLast && !Wrap)
                return new MoveResult(MoveKind.AtEdge, index);
            index = (index + 1) % count;
            return new MoveResult(atLast ? MoveKind.Wrapped : MoveKind.Moved, index);
        }

        public MoveResult Previous()
        {
            if (count == 1)
                return new MoveResult(MoveKind.AtEdge, index);
            bool atFirst = index == 0;
            if (atFirst && !Wrap)
                return new MoveResult(MoveKind.AtEdge, index);
            index = (index - 1 + count) % count;
            return new MoveResult(atFirst ? MoveKind.Wrapped : MoveKind.Moved, index);
        }

        /// <summary>Direct jump (per-screen number keys, mouse click on a real tab).</summary>
        public MoveResult MoveTo(int target)
        {
            if (target < 0 || target >= count)
                throw new ArgumentOutOfRangeException(nameof(target));
            if (target == index)
                return new MoveResult(MoveKind.AtEdge, index);
            index = target;
            return new MoveResult(MoveKind.Moved, index);
        }
    }
}
