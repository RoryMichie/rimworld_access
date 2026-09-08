using System.Collections.Generic;
using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class TreeModelTests
{
    // ===== Test fixture: a minimal tree node + shape =====

    private sealed class TestNode
    {
        public string Label;
        public int IndentLevel;
        public bool IsExpandable;
        public bool IsExpanded;
        public List<TestNode> Children = new List<TestNode>();
        public TestNode Parent;

        public override string ToString()
        {
            return Label;
        }
    }

    private sealed class TestShape : ITreeShape<TestNode>
    {
        public IReadOnlyList<TestNode> ChildrenOf(TestNode node) => node.Children;
        public TestNode ParentOf(TestNode node) => node.Parent;
        public bool IsExpandable(TestNode node) => node.IsExpandable;
        public bool IsExpanded(TestNode node) => node.IsExpanded;
        public void SetExpanded(TestNode node, bool value) => node.IsExpanded = value;
        public int IndentLevelOf(TestNode node) => node.IndentLevel;
    }

    private static TestNode MakeNode(string label, int indent, bool expandable)
    {
        return new TestNode { Label = label, IndentLevel = indent, IsExpandable = expandable };
    }

    private static void AddChild(TestNode parent, TestNode child)
    {
        parent.Children.Add(child);
        child.Parent = parent;
    }

    // Sample tree:
    //   Root (level 0)
    //     A (level 1, expandable)
    //       A1 (level 2)
    //       A2 (level 2, expandable)
    //         A2a (level 3)
    //     B (level 1, expandable)
    //       B1 (level 2)
    //       B2 (level 2)
    //     C (level 1, leaf)
    private sealed class SampleTree
    {
        public TestNode Root, A, A1, A2, A2a, B, B1, B2, C;
    }

    private static SampleTree BuildSampleTree()
    {
        var root = MakeNode("Root", 0, expandable: false);
        var a = MakeNode("A", 1, expandable: true);
        var a1 = MakeNode("A1", 2, expandable: false);
        var a2 = MakeNode("A2", 2, expandable: true);
        var a2a = MakeNode("A2a", 3, expandable: false);
        var b = MakeNode("B", 1, expandable: true);
        var b1 = MakeNode("B1", 2, expandable: false);
        var b2 = MakeNode("B2", 2, expandable: false);
        var c = MakeNode("C", 1, expandable: false);

        AddChild(root, a);
        AddChild(root, b);
        AddChild(root, c);
        AddChild(a, a1);
        AddChild(a, a2);
        AddChild(a2, a2a);
        AddChild(b, b1);
        AddChild(b, b2);

        return new SampleTree
        {
            Root = root,
            A = a,
            A1 = a1,
            A2 = a2,
            A2a = a2a,
            B = b,
            B1 = b1,
            B2 = b2,
            C = c,
        };
    }

    private static TreeModel<TestNode> NewModel()
    {
        return new TreeModel<TestNode>(new TestShape());
    }

    // ===== Flatten =====

    [Fact]
    public void Flatten_SkipRoot_CollapsedSubtrees_ShowsTopLevelOnly()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root);

        Assert.Equal(new[] { tree.A, tree.B, tree.C }, model.Visible);
    }

    [Fact]
    public void Flatten_SkipRoot_ExpandedSubtree_IncludesChildren()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        var model = NewModel();
        model.SetRoot(tree.Root);

        Assert.Equal(new[] { tree.A, tree.A1, tree.A2, tree.B, tree.C }, model.Visible);
    }

    [Fact]
    public void Flatten_SkipRoot_NestedExpansion_WalksMultipleLevels()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        tree.A2.IsExpanded = true;
        var model = NewModel();
        model.SetRoot(tree.Root);

        Assert.Equal(new[] { tree.A, tree.A1, tree.A2, tree.A2a, tree.B, tree.C }, model.Visible);
    }

    [Fact]
    public void Flatten_FullRoot_IncludesRootNode()
    {
        var tree = BuildSampleTree();
        // GetVisibleItems recurses purely on IsExpanded && children present — it does
        // NOT check IsExpandable, even for the root. Preserve that here.
        tree.Root.IsExpanded = true;
        var model = NewModel();
        model.SkipRoot = false;
        model.SetRoot(tree.Root);

        Assert.Equal(new[] { tree.Root, tree.A, tree.B, tree.C }, model.Visible);
    }

    [Fact]
    public void Flatten_FullRoot_RootNotExpanded_ShowsOnlyRoot()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SkipRoot = false;
        model.SetRoot(tree.Root);

        Assert.Equal(new[] { tree.Root }, model.Visible);
    }

    [Fact]
    public void Flatten_Submenu_ExpandedParentHidden_ChildrenShown()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        var model = NewModel();
        model.SubmenuMode = true;
        model.SetRoot(tree.Root);

        Assert.Equal(new[] { tree.A1, tree.A2, tree.B, tree.C }, model.Visible);
    }

    [Fact]
    public void Flatten_Submenu_CollapsedNode_ShownItselfNotChildren()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true; // A2 stays collapsed
        var model = NewModel();
        model.SubmenuMode = true;
        model.SetRoot(tree.Root);

        Assert.Equal(new[] { tree.A1, tree.A2, tree.B, tree.C }, model.Visible);

        tree.A2.IsExpanded = true;
        model.Reflatten();

        Assert.Equal(new[] { tree.A1, tree.A2a, tree.B, tree.C }, model.Visible);
    }

    [Fact]
    public void Flatten_Submenu_SuppressSubmenuHiding_ExpandedParentStaysVisible()
    {
        // The typeahead search window (TreeRegionScope.EnsureSearchExpansion) sets this so an
        // auto-expanded header keeps its own row instead of vanishing behind its children --
        // otherwise the header itself is never a valid typeahead match/landing target
        // (typing "soc" found the Social skill and Social log, never Social).
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        var model = NewModel();
        model.SubmenuMode = true;
        model.SuppressSubmenuHiding = true;
        model.SetRoot(tree.Root);

        Assert.Equal(new[] { tree.A, tree.A1, tree.A2, tree.B, tree.C }, model.Visible);
    }

    [Fact]
    public void Flatten_Submenu_SuppressSubmenuHiding_ClearedRestoresNormalHiding()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        var model = NewModel();
        model.SubmenuMode = true;
        model.SuppressSubmenuHiding = true;
        model.SetRoot(tree.Root);
        Assert.Contains(tree.A, model.Visible); // suppressed: A's own row is present

        model.SuppressSubmenuHiding = false;
        model.Reflatten();

        // Hiding resumes once the search window closes: A disappears behind its children again.
        Assert.Equal(new[] { tree.A1, tree.A2, tree.B, tree.C }, model.Visible);
    }

    // ===== MoveNext / MovePrevious =====

    [Fact]
    public void MoveNext_StopsAtEdge_WithoutWrap()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root); // Visible = [A, B, C]

        model.MoveNext();
        var last = model.MoveNext();
        Assert.Equal(MoveKind.Moved, last.Kind);
        Assert.Equal(2, model.SelectedIndex);

        var atEdge = model.MoveNext();
        Assert.Equal(MoveKind.AtEdge, atEdge.Kind);
        Assert.Equal(2, model.SelectedIndex);
        Assert.False(atEdge.Changed);
    }

    [Fact]
    public void MoveNext_Wraps_WhenEnabled()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.Wrap = true;
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(2); // C, last

        var wrapped = model.MoveNext();
        Assert.Equal(MoveKind.Wrapped, wrapped.Kind);
        Assert.Equal(0, model.SelectedIndex);
        Assert.True(wrapped.Changed);
    }

    [Fact]
    public void MovePrevious_StopsAtEdge_ThenWrapsWhenEnabled()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root);

        var atEdge = model.MovePrevious();
        Assert.Equal(MoveKind.AtEdge, atEdge.Kind);
        Assert.Equal(0, model.SelectedIndex);

        model.Wrap = true;
        var wrapped = model.MovePrevious();
        Assert.Equal(MoveKind.Wrapped, wrapped.Kind);
        Assert.Equal(2, model.SelectedIndex);
    }

    [Fact]
    public void Moves_OnEmptyTree_ReportEmpty()
    {
        var model = NewModel();

        Assert.Equal(MoveKind.Empty, model.MoveNext().Kind);
        Assert.Equal(MoveKind.Empty, model.MovePrevious().Kind);
        Assert.Equal(MoveKind.Empty, model.HomeKey(false).Kind);
        Assert.Equal(MoveKind.Empty, model.EndKey(false).Kind);
    }

    // ===== HomeKey / EndKey =====

    [Fact]
    public void HomeKey_SiblingRelative_And_Absolute()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        tree.A2.IsExpanded = true;
        tree.B.IsExpanded = true;
        var model = NewModel();
        model.SetRoot(tree.Root);
        // Visible: A, A1, A2, A2a, B, B1, B2, C

        model.SetSelectedIndex(model.IndexOf(tree.B2));
        var sibling = model.HomeKey(absolute: false);
        Assert.True(sibling.Changed);
        Assert.Equal(tree.B1, model.SelectedItem);

        model.SetSelectedIndex(model.IndexOf(tree.B2));
        var absolute = model.HomeKey(absolute: true);
        Assert.True(absolute.Changed);
        Assert.Equal(tree.A, model.SelectedItem);
    }

    [Fact]
    public void EndKey_SiblingRelative_And_Absolute()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        tree.A2.IsExpanded = true;
        tree.B.IsExpanded = true;
        var model = NewModel();
        model.SetRoot(tree.Root);
        // Visible: A, A1, A2, A2a, B, B1, B2, C

        model.SetSelectedIndex(model.IndexOf(tree.B1));
        var sibling = model.EndKey(absolute: false);
        Assert.True(sibling.Changed);
        Assert.Equal(tree.B2, model.SelectedItem);

        model.SetSelectedIndex(model.IndexOf(tree.B1));
        var absolute = model.EndKey(absolute: true);
        Assert.True(absolute.Changed);
        Assert.Equal(tree.C, model.SelectedItem);
    }

    [Fact]
    public void EndKey_OnExpandedNode_JumpsToLastDescendant()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        tree.A2.IsExpanded = true;
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));

        var result = model.EndKey(absolute: false);
        Assert.True(result.Changed);
        Assert.Equal(tree.A2a, model.SelectedItem);
    }

    [Fact]
    public void HomeKey_AlwaysReportsChanged_EvenWhenAlreadyAtFirstSibling()
    {
        // MenuHelper.HandleTreeHomeKey invokes onNavigate unconditionally whenever the
        // list is non-empty, regardless of whether the index actually moved.
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root); // selectedIndex 0 = A, already first sibling

        var result = model.HomeKey(absolute: false);
        Assert.Equal(MoveKind.Moved, result.Kind);
        Assert.True(result.Changed);
        Assert.Equal(0, model.SelectedIndex);
    }

    // ===== ExpandOrDrillDown =====

    [Fact]
    public void ExpandOrDrillDown_NotExpandable_ReturnsRejected()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.C)); // leaf, not expandable

        var result = model.ExpandOrDrillDown();
        Assert.Equal(TreeActionKind.Rejected, result.Kind);
        Assert.Equal(tree.C, result.Node);
        Assert.Equal(3, model.Count);
    }

    [Fact]
    public void ExpandOrDrillDown_Collapsed_ExpandsInPlace_GrowsVisible()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));

        var result = model.ExpandOrDrillDown();
        Assert.Equal(TreeActionKind.Expanded, result.Kind);
        Assert.Equal(tree.A, result.Node);
        Assert.True(tree.A.IsExpanded);
        Assert.Equal(5, model.Count); // A, A1, A2, B, C
        Assert.Equal(tree.A, model.SelectedItem); // cursor stays on A
    }

    [Fact]
    public void ExpandOrDrillDown_AlreadyExpanded_DrillsToFirstChild()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));

        var result = model.ExpandOrDrillDown();
        Assert.Equal(TreeActionKind.DrilledToChild, result.Kind);
        Assert.Equal(tree.A1, model.SelectedItem);
    }

    [Fact]
    public void ExpandOrDrillDown_NoChildren_IsSilentNoOp()
    {
        // A node can be marked expandable with an empty Children list (e.g. lazily
        // populated elsewhere). Old code sets IsExpanded=true regardless, then the
        // "already expanded" branch's `item.Children.Count > 0` guard silently no-ops.
        var tree = BuildSampleTree();
        var lonelyExpandable = MakeNode("Lonely", 1, expandable: true);
        AddChild(tree.Root, lonelyExpandable);
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(lonelyExpandable));

        model.ExpandOrDrillDown(); // expands in place (no children to show)
        var result = model.ExpandOrDrillDown(); // already expanded, but Children is empty
        Assert.Equal(TreeActionKind.None, result.Kind);
    }

    [Fact]
    public void ExpandOrDrillDown_SubmenuMode_LandsOnFirstChild()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SubmenuMode = true;
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));

        var result = model.ExpandOrDrillDown();
        Assert.Equal(TreeActionKind.ExpandedSubmenu, result.Kind);
        Assert.Equal(tree.A, result.Node);
        Assert.Equal(tree.A1, model.SelectedItem);
    }

    [Fact]
    public void ExpandOrDrillDown_SubmenuMode_RemembersLastChildAfterCollapseAndReexpand()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SubmenuMode = true;
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));

        model.ExpandOrDrillDown(); // lands on A1 (first child)
        Assert.Equal(tree.A1, model.SelectedItem);

        model.MoveNext(); // -> A2
        Assert.Equal(tree.A2, model.SelectedItem);

        var collapse = model.CollapseOrDrillUp(); // collapses A, remembers A2, lands on A
        Assert.Equal(TreeActionKind.CollapsedToParent, collapse.Kind);
        Assert.Equal(tree.A, model.SelectedItem);

        var reexpand = model.ExpandOrDrillDown();
        Assert.Equal(TreeActionKind.ExpandedSubmenu, reexpand.Kind);
        Assert.Equal(tree.A2, model.SelectedItem); // remembered child, not A1
    }

    [Fact]
    public void ExpandOrDrillDown_OnEmptyTree_ReturnsNone()
    {
        var model = NewModel();
        var result = model.ExpandOrDrillDown();
        Assert.Equal(TreeActionKind.None, result.Kind);
        Assert.Null(result.Node);
    }

    // ===== CollapseOrDrillUp =====

    [Fact]
    public void CollapseOrDrillUp_Standard_ExpandedNode_CollapsesInPlace()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));

        var result = model.CollapseOrDrillUp();
        Assert.Equal(TreeActionKind.Collapsed, result.Kind);
        Assert.Equal(tree.A, result.Node);
        Assert.False(tree.A.IsExpanded);
        Assert.Equal(3, model.Count);
        Assert.Equal(tree.A, model.SelectedItem);
    }

    [Fact]
    public void CollapseOrDrillUp_Standard_LeafNode_DrillsToParent()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A1));

        var result = model.CollapseOrDrillUp();
        Assert.Equal(TreeActionKind.DrilledToParent, result.Kind);
        Assert.Equal(tree.A, model.SelectedItem);
    }

    [Fact]
    public void CollapseOrDrillUp_Standard_TopLevelNode_Rejected()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.C)); // top-level, parent == root

        var result = model.CollapseOrDrillUp();
        Assert.Equal(TreeActionKind.Rejected, result.Kind);
    }

    [Fact]
    public void CollapseOrDrillUp_Submenu_CollapsesParent_LandsOnParent()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SubmenuMode = true;
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));
        model.ExpandOrDrillDown(); // -> A1 visible, A hidden

        var result = model.CollapseOrDrillUp();
        Assert.Equal(TreeActionKind.CollapsedToParent, result.Kind);
        Assert.Equal(tree.A, result.Node);
        Assert.False(tree.A.IsExpanded);
        Assert.Equal(tree.A, model.SelectedItem);
    }

    [Fact]
    public void CollapseOrDrillUp_Submenu_TopLevelNode_Rejected()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SubmenuMode = true;
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.C));

        var result = model.CollapseOrDrillUp();
        Assert.Equal(TreeActionKind.Rejected, result.Kind);
    }

    [Fact]
    public void CollapseOrDrillUp_OnEmptyTree_ReturnsNone()
    {
        var model = NewModel();
        var result = model.CollapseOrDrillUp();
        Assert.Equal(TreeActionKind.None, result.Kind);
    }

    // ===== ExpandAllSiblings =====

    [Fact]
    public void ExpandAllSiblings_ExpandsAllExpandableSiblings_ReturnsCount()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A)); // siblings = A, B, C

        var result = model.ExpandAllSiblings();
        Assert.Equal(2, result.ExpandedCount); // A and B (C isn't expandable)
        Assert.True(result.AnyExpandable);
        Assert.True(tree.A.IsExpanded);
        Assert.True(tree.B.IsExpanded);
    }

    [Fact]
    public void ExpandAllSiblings_AllAlreadyExpanded_ReturnsZero_ButAnyExpandableTrue()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        tree.B.IsExpanded = true;
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));

        var result = model.ExpandAllSiblings();
        Assert.Equal(0, result.ExpandedCount);
        Assert.True(result.AnyExpandable); // A/B are expandable, just already expanded
    }

    [Fact]
    public void ExpandAllSiblings_NoneExpandable_ReturnsZero_AnyExpandableFalse()
    {
        var tree = BuildSampleTree();
        tree.B.IsExpanded = true; // reveal B1, B2 (both leaves)
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.B1));

        var result = model.ExpandAllSiblings();
        Assert.Equal(0, result.ExpandedCount);
        Assert.False(result.AnyExpandable);
    }

    [Fact]
    public void ExpandAllSiblings_SubmenuMode_LandsOnCurrentItemsFirstChild()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SubmenuMode = true;
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));

        var result = model.ExpandAllSiblings();
        Assert.Equal(2, result.ExpandedCount);
        Assert.Equal(tree.A1, model.SelectedItem);
        Assert.Equal(result.Index, model.SelectedIndex);
    }

    [Fact]
    public void ExpandAllSiblings_SubmenuMode_LandsOnRememberedChild_WhenAvailable()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SubmenuMode = true;
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));
        model.ExpandOrDrillDown(); // land on A1
        model.MoveNext(); // -> A2
        model.CollapseOrDrillUp(); // collapse A, remember A2, land on A

        var result = model.ExpandAllSiblings();
        Assert.True(result.ExpandedCount >= 1);
        Assert.Equal(tree.A2, model.SelectedItem); // remembered child wins over first child
    }

    // ===== GetSiblingPosition =====

    [Fact]
    public void GetSiblingPosition_AmongTopLevelSiblings()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root);

        Assert.Equal((1, 3), model.GetSiblingPosition(tree.A));
        Assert.Equal((2, 3), model.GetSiblingPosition(tree.B));
        Assert.Equal((3, 3), model.GetSiblingPosition(tree.C));
    }

    [Fact]
    public void GetSiblingPosition_AmongChildren()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root);

        Assert.Equal((1, 2), model.GetSiblingPosition(tree.A1));
        Assert.Equal((2, 2), model.GetSiblingPosition(tree.A2));
    }

    // ===== TrackLastChild =====

    [Fact]
    public void GetLastChild_RecordsChildOnDrillToParent_StandardMode()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        var model = NewModel();
        model.TrackLastChild = true;
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A2));

        Assert.Null(model.GetLastChild(tree.A));
        var result = model.CollapseOrDrillUp(); // A2 not expanded -> drills up to A
        Assert.Equal(TreeActionKind.DrilledToParent, result.Kind);
        Assert.Equal(tree.A2, model.GetLastChild(tree.A));
    }

    [Fact]
    public void TrackLastChild_Disabled_DoesNotRecordChild_StandardMode()
    {
        var tree = BuildSampleTree();
        tree.A.IsExpanded = true;
        var model = NewModel(); // TrackLastChild defaults false, SubmenuMode false
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A2));

        model.CollapseOrDrillUp();
        Assert.Null(model.GetLastChild(tree.A));
    }

    [Fact]
    public void GetLastChild_ReturnsNull_WhenNotTracked()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root);

        Assert.Null(model.GetLastChild(tree.A));
    }

    // ===== OnBeforeExpand =====

    [Fact]
    public void OnBeforeExpand_InvokedOnce_BeforeNodeBecomesVisible()
    {
        var tree = BuildSampleTree();
        var invoked = new List<TestNode>();
        var model = NewModel();
        model.OnBeforeExpand = node =>
        {
            invoked.Add(node);
            Assert.False(node.IsExpanded); // not yet flipped when the hook runs
        };
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));

        model.ExpandOrDrillDown();

        Assert.Equal(new[] { tree.A }, invoked);
        Assert.True(tree.A.IsExpanded);
    }

    [Fact]
    public void OnBeforeExpand_InvokedPerSibling_InExpandAllSiblings()
    {
        var tree = BuildSampleTree();
        var invoked = new List<TestNode>();
        var model = NewModel();
        model.OnBeforeExpand = node => invoked.Add(node);
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(model.IndexOf(tree.A));

        model.ExpandAllSiblings();

        Assert.Equal(new[] { tree.A, tree.B }, invoked); // C skipped (not expandable)
    }

    // ===== SetSelectedIndex / IndexOf =====

    [Fact]
    public void SetSelectedIndex_ClampsToValidRange()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root); // Count = 3

        model.SetSelectedIndex(-5);
        Assert.Equal(0, model.SelectedIndex);

        model.SetSelectedIndex(99);
        Assert.Equal(2, model.SelectedIndex);

        model.SetSelectedIndex(1);
        Assert.Equal(1, model.SelectedIndex);
    }

    [Fact]
    public void SetSelectedIndex_OnEmptyTree_ClampsToZero()
    {
        var model = NewModel();
        model.SetSelectedIndex(5);
        Assert.Equal(0, model.SelectedIndex);
    }

    [Fact]
    public void IndexOf_FindsNodeOrReturnsNegativeOne()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root);

        Assert.Equal(0, model.IndexOf(tree.A));
        Assert.Equal(-1, model.IndexOf(tree.A1)); // not visible (A collapsed)
    }

    // ===== Lifecycle sanity =====

    [Fact]
    public void SetRoot_ClampsInitialIndex()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root, initialIndex: 10);

        Assert.Equal(2, model.SelectedIndex); // clamped to last valid index (Count=3)
        Assert.Equal(tree.Root, model.Root);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var tree = BuildSampleTree();
        var model = NewModel();
        model.SetRoot(tree.Root);
        model.SetSelectedIndex(2);

        model.Reset();

        Assert.Null(model.Root);
        Assert.Equal(0, model.Count);
        Assert.Equal(0, model.SelectedIndex);
        Assert.Null(model.SelectedItem);
    }

    [Fact]
    public void SelectedItem_NullWhenEmpty()
    {
        var model = NewModel();
        Assert.Null(model.SelectedItem);
    }
}
