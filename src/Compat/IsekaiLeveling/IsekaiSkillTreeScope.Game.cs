using System;
using System.Collections;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for ISEKAI RPG LEVELING's Window_SkillTree, the pan-and-zoom
    /// constellation editor. Three regions: the class list (the window's own class tabs), the
    /// passive tree, and a summary. The tree is the mod's node graph walked breadth-first from
    /// its Start node over the def's own connections, so every node appears once under the
    /// neighbor closest to Start and cross-links are spoken as "also connects to". Enter on a
    /// node runs the window's one unlock entry point (direct unlock, else the chain unlock with
    /// the mod's own messages); the window's selected node and pan offset follow the cursor so
    /// a viewer sees the detail panel and the node the keyboard is on. Respec and Close are
    /// declared actions.
    /// </summary>
    internal sealed class IsekaiSkillTreeScope : TreeRegionScope
    {
        private const int ClassesRegion = 0;
        private const int NodesRegion = 1;
        private const int SummaryRegion = 2;

        private readonly Window window;
        private readonly List<string> classes = new List<string>();
        private readonly List<string> summaryLines = new List<string>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly Dictionary<string, List<string>> crossLinks = new Dictionary<string, List<string>>();
        private object builtTree;
        private bool announcedOpen;

        public IsekaiSkillTreeScope(Window window)
        {
            this.window = window;
        }

        public override string Name => "isekai-constellation";

        protected internal override Window OwnedWindow => window;

        protected override bool EnableTypeahead => true;

        /// <summary>The window's own buttons are custom-drawn in themed mode and captured only in vanilla mode; both are declared here instead.</summary>
        protected override bool CaptureWindowButtons => false;

        protected override int ContentRegionCount => 3;

        protected override int TreeRegionIndex => NodesRegion;

        protected override string TreeRegionLabel => "RimWorldAccess.Compat.Isekai.TreeRegion".Translate();

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case ClassesRegion: return "RimWorldAccess.Compat.Isekai.ClassesRegion".Translate();
                case SummaryRegion: return "RimWorldAccess.Compat.Isekai.SummaryRegion".Translate();
                default: return TreeRegionLabel;
            }
        }

        private bool Ready => IsekaiWindowCompat.SkillTreeGate.Ensure();
        private object Component => Ready ? IsekaiWindowCompat.TreeComponent(window) : null;
        private object Tracker => Component == null ? null : IsekaiCompat.PassiveTreeOf(Component);
        private Pawn Pawn => Ready ? IsekaiWindowCompat.TreePawn(window) : null;
        private object CurrentTree => Ready ? IsekaiWindowCompat.CurrentTree(window) : null;

        // ------------------------------------------------------------------
        // Refresh.
        // ------------------------------------------------------------------

        protected override void RefreshContent()
        {
            base.RefreshContent();
            classes.Clear();
            summaryLines.Clear();
            if (!Ready)
                return;

            string[] all = IsekaiWindowCompat.AllClasses();
            if (all != null)
                classes.AddRange(all);

            object tree = CurrentTree;
            if (!ReferenceEquals(tree, builtTree))
            {
                builtTree = tree;
                RebuildTree(tree);
            }
            BuildSummary(tree);
        }

        private void RebuildTree(object tree)
        {
            crossLinks.Clear();
            var root = new InspectionTreeItem { IsExpandable = true, IsExpanded = true, IndentLevel = -1 };
            if (tree != null)
            {
                object start = IsekaiTreeCompat.StartNode(tree);
                if (start != null)
                {
                    var visited = new HashSet<string> { IsekaiTreeCompat.NodeId(start) };
                    var queue = new Queue<InspectionTreeItem>();
                    InspectionTreeItem startItem = MakeNodeItem(root, start);
                    queue.Enqueue(startItem);
                    while (queue.Count > 0)
                    {
                        InspectionTreeItem item = queue.Dequeue();
                        string id = IsekaiTreeCompat.NodeId(item.Data);
                        List<string> neighbors = IsekaiTreeCompat.Neighbors(tree, id) ?? new List<string>();
                        foreach (string neighborId in neighbors)
                        {
                            object neighbor = IsekaiTreeCompat.GetNode(tree, neighborId);
                            if (neighbor == null)
                                continue;
                            if (visited.Add(neighborId))
                            {
                                queue.Enqueue(MakeNodeItem(item, neighbor));
                            }
                            else if (!(item.Parent?.Data != null && IsekaiTreeCompat.NodeId(item.Parent.Data) == neighborId))
                            {
                                // A link the spanning walk did not take: note it on both ends.
                                AddCrossLink(id, neighborId);
                                AddCrossLink(neighborId, id);
                            }
                        }
                    }
                }
            }
            SetTreeRoot(root);
        }

        private static InspectionTreeItem MakeNodeItem(InspectionTreeItem parent, object node)
        {
            var item = new InspectionTreeItem
            {
                Label = IsekaiTreeCompat.NodeLabel(node),
                Data = node,
                IndentLevel = parent.IndentLevel + 1,
                IsExpanded = true,
            };
            InspectNodeFactory.Attach(parent, item);
            parent.IsExpandable = parent.Children.Count > 0;
            return item;
        }

        private void AddCrossLink(string from, string to)
        {
            if (!crossLinks.TryGetValue(from, out List<string> list))
            {
                list = new List<string>();
                crossLinks[from] = list;
            }
            if (!list.Contains(to))
                list.Add(to);
        }

        private void BuildSummary(object tree)
        {
            object tracker = Tracker;
            if (tracker == null)
                return;
            int available = IsekaiTreeCompat.AvailablePoints(tracker);
            summaryLines.Add(CompatText.ModArgs("Isekai_PassivePoints", available.ToString()));

            int total = 0, unlocked = 0, keystones = 0, unlockedKeystones = 0;
            if (tree != null)
            {
                IList nodes = IsekaiTreeCompat.Nodes(tree);
                total = nodes != null ? nodes.Count : 0;
                unlocked = IsekaiTreeCompat.UnlockedCountInTree(tracker, tree);
                if (nodes != null)
                {
                    foreach (object node in nodes)
                    {
                        if (IsekaiTreeCompat.NodeKindOrdinal(node) != 3)
                            continue;
                        keystones++;
                        if (IsekaiTreeCompat.IsUnlocked(tracker, IsekaiTreeCompat.NodeId(node)))
                            unlockedKeystones++;
                    }
                }
            }
            summaryLines.Add(CompatText.ModText("Isekai_DetailNodes") + " " + unlocked + " / " + total);
            summaryLines.Add(CompatText.ModText("Isekai_DetailKeystones") + " " + unlockedKeystones + " / " + keystones);
            summaryLines.Add(CompatText.ModText("Isekai_DetailPointsSpent") + " " + IsekaiTreeCompat.TotalAllocatedPoints(tracker));
            summaryLines.Add(CompatText.ModArgs("Isekai_PassiveAllocated", IsekaiTreeCompat.TotalAllocatedPoints(tracker).ToString()));

            summaryLines.Add(CompatText.ModText("Isekai_DetailActiveBonuses"));
            int bonusLines = 0;
            if (!string.IsNullOrEmpty(IsekaiTreeCompat.AssignedTree(tracker)))
            {
                foreach (object bonusType in Enum.GetValues(IsekaiTreeCompat.BonusTypeEnum))
                {
                    // ClassGimmickTier is internal bookkeeping the window also skips here.
                    if (bonusType.ToString() == "ClassGimmickTier")
                        continue;
                    float value = IsekaiTreeCompat.TotalBonus(tracker, bonusType);
                    if (value == 0f)
                        continue;
                    float display = value * 100f;
                    if (IsekaiWindowCompat.IsInvertedStat(window, bonusType))
                        display = -display;
                    string sign = display >= 0 ? "+" : "";
                    summaryLines.Add(sign + display.ToString("F0") + "% " + IsekaiWindowCompat.BonusTypeName(window, bonusType));
                    bonusLines++;
                }
            }
            if (bonusLines == 0)
                summaryLines.Add(CompatText.ModText("Isekai_DetailNoBonuses"));

            if (tree != null && !IsekaiTreeCompat.GimmickIsNone(IsekaiTreeCompat.ClassGimmick(tree)))
            {
                object gimmick = IsekaiTreeCompat.ClassGimmick(tree);
                string name = IsekaiTreeCompat.ClassGimmickName(tree);
                if (string.IsNullOrEmpty(name))
                    name = IsekaiTreeCompat.GimmickName(gimmick);
                summaryLines.Add(name + ", " + CompatText.ModText("Isekai_DetailClassPassive"));
                string description = IsekaiTreeCompat.ClassGimmickDescription(tree);
                if (!string.IsNullOrEmpty(description))
                    summaryLines.Add(CompatText.Flatten(description));
                if (IsekaiTreeCompat.HasEnteredTree(tracker, tree))
                {
                    int tier = IsekaiTreeCompat.GimmickTierFor(tracker, gimmick);
                    if (tier <= 0)
                    {
                        summaryLines.Add("RimWorldAccess.Compat.Isekai.GimmickNotUnlocked".Translate(name));
                    }
                    else
                    {
                        string status = IsekaiTreeCompat.GimmickStatus(Pawn, Component, gimmick, tier, out bool _);
                        summaryLines.Add("RimWorldAccess.Compat.Isekai.GimmickLine".Translate(name, tier, CompatText.Flatten(status)));
                    }
                }
                else
                {
                    summaryLines.Add(CompatText.ModText("Isekai_DetailGimmickLocked"));
                }
            }
        }

        // ------------------------------------------------------------------
        // Prefix rows: the class list (region 0) and the summary (region 2).
        // ------------------------------------------------------------------

        protected override int PrefixRowCountFor(int region)
        {
            if (region == ClassesRegion)
                return classes.Count;
            if (region == SummaryRegion)
                return summaryLines.Count;
            return 0;
        }

        protected override ElementDescription DescribePrefixRow(int region, int index)
        {
            var d = new ElementDescription();
            if (region == SummaryRegion)
            {
                if (index >= 0 && index < summaryLines.Count)
                {
                    d.Label = summaryLines[index];
                    d.ReadOnly = true;
                }
                return d;
            }
            if (index < 0 || index >= classes.Count)
                return d;

            string cls = classes[index];
            object tracker = Tracker;
            object def = IsekaiTreeCompat.TreeDefForClass(cls);
            string assigned = tracker == null ? null : IsekaiTreeCompat.AssignedTree(tracker);
            d.Label = cls;
            d.Role = ElementRole.RadioButton;
            d.Selected = IsekaiWindowCompat.SelectedClass(window) == cls;
            if (def == null)
            {
                d.Extras = CompatText.ModText("Isekai_TreeComingSoon");
                d.Disabled = true;
            }
            else if (!string.IsNullOrEmpty(assigned))
            {
                if (assigned == cls)
                    d.Extras = "RimWorldAccess.Compat.Isekai.ClassAssigned".Translate();
                else if (IsekaiTreeCompat.HasEnteredTree(tracker, def))
                    d.Extras = "RimWorldAccess.Compat.Isekai.ClassEntered".Translate();
                else
                    d.Extras = "RimWorldAccess.Compat.Isekai.ClassLocked".Translate();
            }
            return d;
        }

        protected override void ActivatePrefixRow(int region, int index)
        {
            if (region != ClassesRegion || index < 0 || index >= classes.Count)
                return;
            string cls = classes[index];
            if (IsekaiTreeCompat.TreeDefForClass(cls) == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(CompatText.ModText("Isekai_TreeComingSoon"));
                return;
            }
            IsekaiWindowCompat.SelectClass(window, cls);
            SoundDefOf.Click.PlayOneShotOnCamera();
            RefreshModel();
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // Tree nodes.
        // ------------------------------------------------------------------

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription { Label = item.Label };
            object node = item.Data;
            object tracker = Tracker;
            if (node == null || tracker == null)
                return d;

            string id = IsekaiTreeCompat.NodeId(node);
            bool unlocked = IsekaiTreeCompat.IsUnlocked(tracker, id);
            var parts = new List<string> { IsekaiWindowCompat.NodeTypeLabel(window, IsekaiTreeCompat.NodeKind(node)) };
            int cost = IsekaiTreeCompat.NodeCost(node);
            if (unlocked)
            {
                parts.Add(CompatText.ModText("Isekai_NodeAllocated"));
            }
            else
            {
                if (cost > 0)
                    parts.Add(CompatText.ModArgs("Isekai_NodeCost", cost.ToString()));
                string reason = LockReason(node, tracker);
                parts.Add(reason ?? (string)"RimWorldAccess.Compat.Isekai.NodeAvailable".Translate());
            }
            d.Value = string.Join(", ", parts);

            var extras = new List<string>();
            string description = IsekaiTreeCompat.NodeDescription(node);
            if (!string.IsNullOrEmpty(description))
                extras.Add(CompatText.Flatten(description));
            IList bonuses = IsekaiTreeCompat.NodeBonuses(node);
            if (bonuses != null)
            {
                foreach (object bonus in bonuses)
                    extras.Add(IsekaiWindowCompat.FormatBonus(window, bonus));
            }
            if (crossLinks.TryGetValue(id, out List<string> links) && links.Count > 0)
            {
                var names = new List<string>();
                foreach (string linkId in links)
                {
                    object linked = IsekaiTreeCompat.GetNode(CurrentTree, linkId);
                    if (linked != null)
                        names.Add(IsekaiTreeCompat.NodeLabel(linked));
                }
                if (names.Count > 0)
                    extras.Add("RimWorldAccess.Compat.Isekai.NodeAlsoConnects".Translate(string.Join(", ", names)));
            }
            d.Extras = CompatText.JoinSentences(extras);
            return d;
        }

        /// <summary>
        /// Why the node cannot be unlocked right now, in the window's own words and order
        /// (DrawDetailSection_SelectedNode), or null when it can.
        /// </summary>
        private string LockReason(object node, object tracker)
        {
            if (IsekaiTreeCompat.CanUnlock(tracker, IsekaiTreeCompat.NodeId(node), Pawn))
                return null;
            bool isStart = IsekaiTreeCompat.IsStartNode(node);
            string assigned = IsekaiTreeCompat.AssignedTree(tracker);
            object tree = CurrentTree;
            bool crossTreeStart = isStart && !string.IsNullOrEmpty(assigned) && tree != null
                && IsekaiTreeCompat.TreeClass(tree) != assigned;
            bool firstClass = isStart && string.IsNullOrEmpty(assigned);
            if (IsekaiTreeCompat.AvailablePoints(tracker) < IsekaiTreeCompat.NodeCost(node))
                return CompatText.ModText("Isekai_DetailNotEnoughPoints");
            if (firstClass && (Component == null || IsekaiCompat.Level(Component) < 11))
                return CompatText.ModText("Isekai_RequiresDRank");
            if (crossTreeStart && !IsekaiTreeCompat.PawnHasStarFragment(Pawn))
                return CompatText.ModText("Isekai_StarFragment_RequiresFragment");
            return CompatText.ModText("Isekai_DetailNotConnected");
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            object node = item.Data;
            object tracker = Tracker;
            if (node == null || tracker == null)
                return;
            string id = IsekaiTreeCompat.NodeId(node);
            if (IsekaiTreeCompat.IsUnlocked(tracker, id))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(CompatText.ModText("Isekai_NodeAllocated"));
                return;
            }

            long messagesBefore = NotificationAccessibilityPatch.MessageEmissionCount;
            IsekaiWindowCompat.TryUnlockWithChain(window, node);
            RefreshModel();
            if (IsekaiTreeCompat.IsUnlocked(tracker, id))
            {
                // A chain unlock messages its own count; a single unlock only plays a sound.
                if (NotificationAccessibilityPatch.MessageEmissionCount == messagesBefore)
                {
                    TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Isekai.NodeUnlocked".Translate(
                        item.Label, IsekaiTreeCompat.AvailablePoints(tracker)));
                }
                return;
            }
            if (NotificationAccessibilityPatch.MessageEmissionCount == messagesBefore)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Isekai.CannotUnlock".Translate(
                    item.Label, LockReason(node, tracker) ?? ""));
            }
        }

        /// <summary>The window's selected node and pan follow the cursor: the detail panel and the node stay on screen for a viewer.</summary>
        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            if (region != NodesRegion || !Ready)
                return;
            int treeIndex = index - PrefixRowCountFor(region);
            if (treeIndex < 0 || treeIndex >= Tree.Count)
                return;
            object node = Tree.Visible[treeIndex].Data;
            if (node == null)
                return;
            IsekaiWindowCompat.SelectNode(window, IsekaiTreeCompat.NodeId(node));
            IsekaiWindowCompat.PanToNode(window, IsekaiTreeCompat.NodeX(node), IsekaiTreeCompat.NodeY(node));
        }

        // ------------------------------------------------------------------
        // Buttons.
        // ------------------------------------------------------------------

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                object tracker = Tracker;
                if (tracker != null)
                {
                    bool canRespec = IsekaiTreeCompat.CanRespec(tracker);
                    bool hasNodes = IsekaiTreeCompat.HasNonStartNodes(tracker);
                    string remaining = canRespec
                        ? CompatText.ModArgs("Isekai_TreeRespecRemaining", IsekaiTreeCompat.RespecsRemaining(tracker).ToString())
                        : CompatText.ModText("Isekai_TreeRespecNone");
                    actions.Add(new ScreenAction(CompatText.ModText("Isekai_PassiveRespec") + ", " + remaining, delegate
                    {
                        // The window's Respec button: tracker.Respec then a click.
                        IsekaiTreeCompat.Respec(tracker);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        RefreshModel();
                        TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Isekai.RespecDone".Translate(IsekaiTreeCompat.AvailablePoints(tracker)));
                    }, null, null, !(hasNodes && canRespec),
                        canRespec ? null : CompatText.ModText("Isekai_TreeRespecNone")));
                }
                actions.Add(new ScreenAction(CompatText.ModText("Isekai_Close"), delegate { window.Close(); }, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen || !Ready)
                return;
            announcedOpen = true;
            Pawn pawn = Pawn;
            object tracker = Tracker;
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Isekai.TreeWindowOpen".Translate(
                pawn != null ? pawn.LabelShortCap : "",
                IsekaiWindowCompat.SelectedClass(window) ?? "",
                tracker != null ? IsekaiTreeCompat.AvailablePoints(tracker) : 0));
        }
    }
}
