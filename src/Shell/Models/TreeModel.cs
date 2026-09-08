using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Topology accessor <see cref="TreeModel{T}"/> uses to flatten and navigate a tree; an
    /// adapter maps it onto the real node type. PURE: links into the test project.
    /// </summary>
    public interface ITreeShape<T> where T : class
    {
        /// <summary>Node's direct children. Never null; empty is fine.</summary>
        IReadOnlyList<T> ChildrenOf(T node);

        /// <summary>Node's parent, or null if top-level.</summary>
        T ParentOf(T node);

        bool IsExpandable(T node);
        bool IsExpanded(T node);
        void SetExpanded(T node, bool value);

        /// <summary>0-indexed indent level, needed for Home/End tree semantics.</summary>
        int IndentLevelOf(T node);
    }

    /// <summary>What an expand/collapse/drill attempt did. Standard-mode expand/collapse leave the cursor in place.</summary>
    public enum TreeActionKind
    {
        /// <summary>Guard hit (empty list / out-of-range cursor) — adapter no-ops.</summary>
        None,
        /// <summary>Not expandable, or no eligible parent to act on.</summary>
        Rejected,
        Expanded,
        /// <summary>Submenu mode: expanded; cursor moved onto a child (the parent disappears from Visible).</summary>
        ExpandedSubmenu,
        Collapsed,
        /// <summary>Submenu mode: collapsed the parent; cursor moved onto the parent.</summary>
        CollapsedToParent,
        DrilledToChild,
        DrilledToParent,
    }

    /// <summary>Outcome of one expand/collapse/drill. For pure cursor moves <c>Node</c> is the node left behind.</summary>
    public readonly struct TreeActionResult<T> where T : class
    {
        public readonly TreeActionKind Kind;

        public readonly int Index;

        /// <summary>Null only for Kind == None.</summary>
        public readonly T Node;

        public TreeActionResult(TreeActionKind kind, int index, T node)
        {
            Kind = kind;
            Index = index;
            Node = node;
        }
    }

    /// <summary>Result of an ExpandAllSiblings ('*' key) attempt.</summary>
    public readonly struct ExpandSiblingsResult
    {
        public readonly int ExpandedCount;

        /// <summary>Whether any sibling was expandable at all — separates "all already expanded" from "nothing to expand".</summary>
        public readonly bool AnyExpandable;

        public readonly int Index;

        public ExpandSiblingsResult(int expandedCount, bool anyExpandable, int index)
        {
            ExpandedCount = expandedCount;
            AnyExpandable = anyExpandable;
            Index = index;
        }
    }

    /// <summary>
    /// Render-agnostic tree navigation state machine: the flattened visible list, the cursor, and the
    /// index math behind it. Mutates state and reports what happened; the adapter decides what to say.
    /// PURE: links into the test project.
    /// </summary>
    public sealed class TreeModel<T> where T : class
    {
        private readonly ITreeShape<T> shape;
        private readonly List<T> visible = new List<T>();
        private T root;
        private int selectedIndex;
        private Dictionary<T, T> lastChildPerParent;

        /// <summary>Wrap from last to first and vice versa (the WrapNavigation setting). Used by MoveNext/MovePrevious only.</summary>
        public bool Wrap;

        public bool SkipRoot = true;

        /// <summary>Submenu-style navigation: expanded parents are hidden from the visible list (IsSubmenuMode).</summary>
        public bool SubmenuMode;

        /// <summary>
        /// Forces the standard (non-hiding) flatten regardless of <see cref="SubmenuMode"/>, so an
        /// auto-expanded header keeps its row and stays a typeahead match and landing target.
        /// ONLY a typeahead search's auto-expansion bracket may set this, for exactly the bracket's
        /// duration.
        /// </summary>
        public bool SuppressSubmenuHiding;

        public bool TrackLastChild;

        /// <summary>Invoked right before a node is set expanded (lazy-load hook). May be null.</summary>
        public Action<T> OnBeforeExpand;

        public TreeModel(ITreeShape<T> shape)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            this.shape = shape;
        }

        public T Root
        {
            get { return root; }
        }

        public IReadOnlyList<T> Visible
        {
            get { return visible; }
        }

        public int Count
        {
            get { return visible.Count; }
        }

        /// <summary>Bumped whenever <see cref="Visible"/> is rebuilt or dropped; Count alone cannot tell a re-flatten from a redraw.</summary>
        public int Version { get; private set; }

        public int SelectedIndex
        {
            get { return selectedIndex; }
        }

        public T SelectedItem
        {
            get
            {
                return (selectedIndex >= 0 && selectedIndex < visible.Count)
                    ? visible[selectedIndex]
                    : null;
            }
        }

        public void SetRoot(T root, int initialIndex = 0)
        {
            this.root = root;
            selectedIndex = initialIndex;
            if (TrackLastChild || SubmenuMode)
                lastChildPerParent = new Dictionary<T, T>();
            Reflatten();
            if (selectedIndex >= visible.Count)
                selectedIndex = Math.Max(0, visible.Count - 1);
        }

        public void Reset()
        {
            root = null;
            visible.Clear();
            selectedIndex = 0;
            lastChildPerParent?.Clear();
            Version++;
        }

        /// <summary>Rebuilds Visible per SkipRoot/SubmenuMode. Does NOT clamp SelectedIndex; callers do.</summary>
        public void Reflatten()
        {
            visible.Clear();
            Version++;
            if (root == null)
                return;

            if (SubmenuMode && !SuppressSubmenuHiding)
            {
                foreach (var child in shape.ChildrenOf(root))
                    FlattenSubmenu(child);
            }
            else if (SkipRoot)
            {
                foreach (var child in shape.ChildrenOf(root))
                    FlattenStandard(child);
            }
            else
            {
                FlattenStandard(root);
            }
        }

        // Recursion deliberately ignores IsExpandable; IsExpanded plus children is the test.
        private void FlattenStandard(T node)
        {
            visible.Add(node);
            var children = shape.ChildrenOf(node);
            if (shape.IsExpanded(node) && children.Count > 0)
            {
                foreach (var child in children)
                    FlattenStandard(child);
            }
        }

        private void FlattenSubmenu(T node)
        {
            var children = shape.ChildrenOf(node);
            if (shape.IsExpandable(node) && shape.IsExpanded(node) && children.Count > 0)
            {
                foreach (var child in children)
                    FlattenSubmenu(child);
            }
            else
            {
                visible.Add(node);
            }
        }

        public void SetSelectedIndex(int index)
        {
            selectedIndex = ClampIndex(index);
        }

        private int ClampIndex(int index)
        {
            return Math.Max(0, Math.Min(index, visible.Count - 1));
        }

        public int IndexOf(T node)
        {
            return visible.IndexOf(node);
        }

        public MoveResult MoveNext()
        {
            if (visible.Count == 0)
                return new MoveResult(MoveKind.Empty, selectedIndex);

            if (selectedIndex < visible.Count - 1)
            {
                selectedIndex++;
                return new MoveResult(MoveKind.Moved, selectedIndex);
            }

            if (Wrap)
            {
                selectedIndex = 0;
                return new MoveResult(MoveKind.Wrapped, selectedIndex);
            }

            return new MoveResult(MoveKind.AtEdge, selectedIndex);
        }

        public MoveResult MovePrevious()
        {
            if (visible.Count == 0)
                return new MoveResult(MoveKind.Empty, selectedIndex);

            if (selectedIndex > 0)
            {
                selectedIndex--;
                return new MoveResult(MoveKind.Moved, selectedIndex);
            }

            if (Wrap)
            {
                selectedIndex = visible.Count - 1;
                return new MoveResult(MoveKind.Wrapped, selectedIndex);
            }

            return new MoveResult(MoveKind.AtEdge, selectedIndex);
        }

        /// <summary>Jumps to the first sibling at the current indent level, or to index 0 when
        /// absolute. Reports Moved on every non-empty call even if the index did not change.</summary>
        public MoveResult HomeKey(bool absolute)
        {
            if (visible.Count == 0)
                return new MoveResult(MoveKind.Empty, selectedIndex);

            selectedIndex = absolute ? 0 : JumpToFirstSibling(selectedIndex);
            return new MoveResult(MoveKind.Moved, selectedIndex);
        }

        /// <summary>Jumps to the last visible descendant of an expanded node, else the last sibling at
        /// the current indent level; absolute jumps to the last item. Always-Moved like <see cref="HomeKey"/>.</summary>
        public MoveResult EndKey(bool absolute)
        {
            if (visible.Count == 0)
                return new MoveResult(MoveKind.Empty, selectedIndex);

            if (absolute)
            {
                selectedIndex = visible.Count - 1;
            }
            else
            {
                T currentItem = visible[selectedIndex];
                if (shape.IsExpanded(currentItem) && shape.IsExpandable(currentItem) && shape.ChildrenOf(currentItem).Count > 0)
                {
                    int currentLevel = shape.IndentLevelOf(currentItem);
                    int lastDescendantIndex = selectedIndex;
                    for (int i = selectedIndex + 1; i < visible.Count; i++)
                    {
                        if (shape.IndentLevelOf(visible[i]) <= currentLevel)
                            break;
                        lastDescendantIndex = i;
                    }
                    selectedIndex = lastDescendantIndex;
                }
                else
                {
                    selectedIndex = JumpToLastSibling(selectedIndex);
                }
            }

            return new MoveResult(MoveKind.Moved, selectedIndex);
        }

        private int JumpToFirstSibling(int currentIndex)
        {
            int indentLevel = shape.IndentLevelOf(visible[currentIndex]);

            for (int i = currentIndex - 1; i >= 0; i--)
            {
                if (shape.IndentLevelOf(visible[i]) < indentLevel)
                    return i + 1;
            }

            for (int i = 0; i < visible.Count; i++)
            {
                if (shape.IndentLevelOf(visible[i]) == indentLevel)
                    return i;
            }

            return 0;
        }

        private int JumpToLastSibling(int currentIndex)
        {
            int indentLevel = shape.IndentLevelOf(visible[currentIndex]);
            int lastSibling = currentIndex;

            for (int i = currentIndex + 1; i < visible.Count; i++)
            {
                int level = shape.IndentLevelOf(visible[i]);
                if (level < indentLevel)
                    break;
                if (level == indentLevel)
                    lastSibling = i;
            }

            return lastSibling;
        }

        /// <summary>Expands the cursor node, or drills into its first child when already expanded.</summary>
        public TreeActionResult<T> ExpandOrDrillDown()
        {
            if (visible.Count == 0 || selectedIndex < 0 || selectedIndex >= visible.Count)
                return new TreeActionResult<T>(TreeActionKind.None, selectedIndex, null);

            var item = visible[selectedIndex];

            if (!shape.IsExpandable(item))
                return new TreeActionResult<T>(TreeActionKind.Rejected, selectedIndex, item);

            if (!shape.IsExpanded(item))
            {
                OnBeforeExpand?.Invoke(item);
                shape.SetExpanded(item, true);

                if (SubmenuMode)
                {
                    // Parent disappears — land on remembered or first child.
                    T targetChild = null;
                    if (lastChildPerParent != null && lastChildPerParent.TryGetValue(item, out var remembered))
                        targetChild = remembered;
                    var children = shape.ChildrenOf(item);
                    if (targetChild == null && children.Count > 0)
                        targetChild = children[0];

                    Reflatten();

                    if (targetChild != null)
                    {
                        int idx = visible.IndexOf(targetChild);
                        selectedIndex = idx >= 0 ? idx : ClampIndex(selectedIndex);
                    }
                    else
                    {
                        selectedIndex = ClampIndex(selectedIndex);
                    }

                    return new TreeActionResult<T>(TreeActionKind.ExpandedSubmenu, selectedIndex, item);
                }
                else
                {
                    Reflatten();
                    return new TreeActionResult<T>(TreeActionKind.Expanded, selectedIndex, item);
                }
            }
            else
            {
                // Standard mode only: submenu mode never shows an expanded item.
                var children = shape.ChildrenOf(item);
                if (children.Count > 0)
                {
                    int childIndex = visible.IndexOf(children[0]);
                    if (childIndex >= 0)
                    {
                        if (TrackLastChild && lastChildPerParent != null)
                            lastChildPerParent[item] = children[0];
                        selectedIndex = childIndex;
                        return new TreeActionResult<T>(TreeActionKind.DrilledToChild, selectedIndex, item);
                    }
                }

                return new TreeActionResult<T>(TreeActionKind.None, selectedIndex, item);
            }
        }

        /// <summary>Collapses the cursor node, or drills up to its parent.</summary>
        public TreeActionResult<T> CollapseOrDrillUp()
        {
            if (visible.Count == 0 || selectedIndex < 0 || selectedIndex >= visible.Count)
                return new TreeActionResult<T>(TreeActionKind.None, selectedIndex, null);

            var item = visible[selectedIndex];

            if (SubmenuMode)
            {
                // Expanded parents are never visible here: Left collapses the parent and lands on it.
                var parent = shape.ParentOf(item);
                if (parent != null && parent != root)
                {
                    if (lastChildPerParent != null)
                        lastChildPerParent[parent] = item;

                    shape.SetExpanded(parent, false);
                    Reflatten();

                    int parentIndex = visible.IndexOf(parent);
                    selectedIndex = parentIndex >= 0 ? parentIndex : ClampIndex(selectedIndex);

                    return new TreeActionResult<T>(TreeActionKind.CollapsedToParent, selectedIndex, parent);
                }

                return new TreeActionResult<T>(TreeActionKind.Rejected, selectedIndex, item);
            }

            if (shape.IsExpandable(item) && shape.IsExpanded(item))
            {
                shape.SetExpanded(item, false);
                Reflatten();
                if (selectedIndex >= visible.Count)
                    selectedIndex = ClampIndex(selectedIndex);
                return new TreeActionResult<T>(TreeActionKind.Collapsed, selectedIndex, item);
            }

            var itemParent = shape.ParentOf(item);
            if (itemParent != null && itemParent != root)
            {
                int parentIndex = visible.IndexOf(itemParent);
                if (parentIndex >= 0)
                {
                    if (TrackLastChild && lastChildPerParent != null)
                        lastChildPerParent[itemParent] = item;
                    selectedIndex = parentIndex;
                    return new TreeActionResult<T>(TreeActionKind.DrilledToParent, selectedIndex, item);
                }

                return new TreeActionResult<T>(TreeActionKind.None, selectedIndex, item);
            }

            return new TreeActionResult<T>(TreeActionKind.Rejected, selectedIndex, item);
        }

        /// <summary>Expands every sibling of the cursor node ('*' key). Never writes the last-child memory.</summary>
        public ExpandSiblingsResult ExpandAllSiblings()
        {
            if (visible.Count == 0 || selectedIndex < 0 || selectedIndex >= visible.Count)
                return new ExpandSiblingsResult(0, false, selectedIndex);

            var currentItem = visible[selectedIndex];
            var parent = shape.ParentOf(currentItem);
            IReadOnlyList<T> siblings = (parent == null || parent == root)
                ? shape.ChildrenOf(root)
                : shape.ChildrenOf(parent);

            int expandedCount = 0;
            bool anyExpandable = false;
            foreach (var sibling in siblings)
            {
                if (shape.IsExpandable(sibling))
                {
                    anyExpandable = true;
                    if (!shape.IsExpanded(sibling))
                    {
                        OnBeforeExpand?.Invoke(sibling);
                        shape.SetExpanded(sibling, true);
                        expandedCount++;
                    }
                }
            }

            if (expandedCount > 0)
            {
                if (SubmenuMode)
                {
                    // All expanded siblings disappear — land on the current item's child.
                    T targetChild = null;
                    var currentChildren = shape.ChildrenOf(currentItem);
                    if (shape.IsExpandable(currentItem) && currentChildren.Count > 0)
                    {
                        if (lastChildPerParent != null && lastChildPerParent.TryGetValue(currentItem, out var remembered))
                            targetChild = remembered;
                        if (targetChild == null)
                            targetChild = currentChildren[0];
                    }

                    Reflatten();

                    if (targetChild != null)
                    {
                        int idx = visible.IndexOf(targetChild);
                        selectedIndex = idx >= 0 ? idx : ClampIndex(selectedIndex);
                    }
                    else
                    {
                        selectedIndex = ClampIndex(selectedIndex);
                    }
                }
                else
                {
                    Reflatten();
                }
            }

            return new ExpandSiblingsResult(expandedCount, anyExpandable, selectedIndex);
        }

        /// <summary>1-based position among the node's siblings (or top-level roots), and the total sibling count.</summary>
        public (int position, int total) GetSiblingPosition(T node)
        {
            var parent = shape.ParentOf(node);
            IReadOnlyList<T> siblings = (parent == null || parent == root)
                ? shape.ChildrenOf(root)
                : shape.ChildrenOf(parent);
            int pos = IndexOfInReadOnlyList(siblings, node) + 1;
            return (pos, siblings.Count);
        }

        /// <summary>Last visited child for a parent (if TrackLastChild/SubmenuMode is enabled). Null if none tracked.</summary>
        public T GetLastChild(T parent)
        {
            if (lastChildPerParent != null && lastChildPerParent.TryGetValue(parent, out var child))
                return child;
            return null;
        }

        private static int IndexOfInReadOnlyList(IReadOnlyList<T> list, T item)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(list[i], item))
                    return i;
            }
            return -1;
        }
    }
}
