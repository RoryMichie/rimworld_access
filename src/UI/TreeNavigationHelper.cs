using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Treeview keyboard handling, typeahead search, expand/collapse, level tracking, and
    /// announcements. Owns a <see cref="TreeModel{T}"/> for the flatten/cursor/expand index
    /// math and adds everything render-related (sounds, speech, search, level tracking).
    ///
    /// LEGACY: the only consumers are FactionTabState (via FactionTreeNavigation) and
    /// IdeosDuringLandingScope (via IdeologyTreeNavigation). Do not add new consumers; new
    /// trees subclass TreeRegionScope. Delete this class when those two migrate.
    ///
    /// Callers construct with a level-tracking key, set the callbacks, call Initialize, then
    /// route keys through the public router methods (NavigateNext, ActivateCurrent, ...).
    /// </summary>
    public class TreeNavigationHelper
    {
        private readonly string levelTrackingKey;
        private readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private readonly InspectionTreeItemShape shape = new InspectionTreeItemShape();
        private readonly TreeModel<InspectionTreeItem> model;

        private InspectionTreeItem lastAnnouncedParent;
        // Snapshot of pre-search expansion state — non-null while a typeahead search
        // is auto-expanding the tree to surface matches across collapsed nodes.
        private Dictionary<InspectionTreeItem, bool> preSearchExpansion;

        #region Configuration

        /// <summary>
        /// Announcement formatter for normal navigation. Null uses the default format:
        /// "Label, expanded, 3 items. 1 of 5. level 2".
        /// </summary>
        public Func<InspectionTreeItem, string> FormatItemAnnouncement { get; set; }

        /// <summary>
        /// Announcement formatter for expand/collapse state changes, for a shorter form than
        /// full navigation. Null falls back to FormatItemAnnouncement.
        /// </summary>
        public Func<InspectionTreeItem, string> FormatStateChangeAnnouncement { get; set; }

        /// <summary>
        /// Announcement formatter for typeahead search. Null uses the default format:
        /// "Label, expanded, 2 of 5 matches for 'w'".
        /// </summary>
        public Func<InspectionTreeItem, TypeaheadSearchHelper, string> FormatSearchAnnouncement { get; set; }

        /// <summary>
        /// Enter handler. Return true if handled; false falls back to toggle expand/collapse.
        /// </summary>
        public Func<InspectionTreeItem, bool> OnActivate { get; set; }

        /// <summary>Delete handler. Return true if handled.</summary>
        public Func<InspectionTreeItem, bool> OnDelete { get; set; }

        /// <summary>
        /// Alt+I handler. Return true if handled; null falls back to item.OnInfo, then the
        /// info card for item.LinkedDef.
        /// </summary>
        public Func<InspectionTreeItem, bool> OnInfo { get; set; }

        /// <summary>
        /// Invoked by the model immediately before a node is set expanded. Populate
        /// item.Children here for lazy loading.
        /// </summary>
        public Action<InspectionTreeItem> OnBeforeExpand
        {
            get => model.OnBeforeExpand;
            set => model.OnBeforeExpand = value;
        }

        /// <summary>
        /// Nodes matching this predicate are auto-expanded for the duration of a typeahead
        /// search so matches inside collapsed nodes are reachable; the original expansion state
        /// is restored when the search ends. Null scopes search to currently-visible items.
        /// </summary>
        public Func<InspectionTreeItem, bool> ShouldExpandForSearch { get; set; }

        /// <summary>
        /// Text typeahead matches each visible item against; null/empty excludes the item.
        /// Fully replaces the default selection (which ties matchability to
        /// <see cref="ShouldExpandForSearch"/>), so a consumer can index an arbitrary subset
        /// independently of what the search auto-expanded. Indexes line up with VisibleItems.
        /// </summary>
        public Func<InspectionTreeItem, string> SearchableLabelSelector { get; set; }

        /// <summary>
        /// Items Page Up/Down jump between. Null leaves Page Up/Down inert (consumed, no
        /// movement).
        /// </summary>
        public Func<InspectionTreeItem, bool> IsSectionBoundary { get; set; }

        /// <summary>
        /// Whether expand/collapse announcements include child counts ("expanded, 3 items"
        /// versus just "expanded").
        /// </summary>
        public bool AnnounceChildCounts { get; set; } = true;

        /// <summary>
        /// Whether the root node is hidden so its children are the top-level visible items.
        /// </summary>
        public bool SkipRootInVisibleList
        {
            get => model.SkipRoot;
            set => model.SkipRoot = value;
        }

        /// <summary>
        /// Whether returning to a parent restores the previously visited child.
        /// </summary>
        public bool TrackLastChild
        {
            get => model.TrackLastChild;
            set => model.TrackLastChild = value;
        }

        /// <summary>In submenu mode, expanded parents are hidden from the visible list.</summary>
        private bool IsSubmenuMode =>
            RimWorldAccessMod_Settings.Settings?.SubmenuTreeNavigation ?? false;

        /// <summary>
        /// Pushes the settings-derived config (wrap, submenu mode) onto the model, which caches
        /// it in fields. Must run before any model op that depends on it; cheap enough to call
        /// at the top of every method that touches the model.
        /// </summary>
        private void SyncModelConfig()
        {
            model.Wrap = (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true);
            model.SubmenuMode = IsSubmenuMode;
        }

        #endregion

        #region Read-Only State

        public bool HasActiveSearch => typeahead.HasActiveSearch;
        public bool HasNoMatches => typeahead.HasNoMatches;
        public int SelectedIndex => model.SelectedIndex;

        public InspectionTreeItem SelectedItem => model.SelectedItem;

        public IReadOnlyList<InspectionTreeItem> VisibleItems => model.Visible;
        public InspectionTreeItem RootItem => model.Root;
        public int Count => model.Count;
        public TypeaheadSearchHelper Typeahead => typeahead;

        #endregion

        public TreeNavigationHelper(string levelTrackingKey)
        {
            this.levelTrackingKey = levelTrackingKey;
            model = new TreeModel<InspectionTreeItem>(shape);
        }

        #region Lifecycle

        /// <summary>
        /// Sets the root and resets selection, search, and level tracking. Does NOT announce —
        /// the caller announces opening in its own format.
        /// </summary>
        public void Initialize(InspectionTreeItem root, int initialIndex = 0)
        {
            SyncModelConfig();
            typeahead.ClearSearch();
            MenuHelper.ResetLevel(levelTrackingKey);
            lastAnnouncedParent = null;
            model.SetRoot(root, initialIndex);
        }

        /// <summary>Resets all tree state.</summary>
        public void Reset()
        {
            typeahead.ClearSearch();
            MenuHelper.ResetLevel(levelTrackingKey);
            lastAnnouncedParent = null;
            preSearchExpansion = null;
            model.Reset();
        }

        /// <summary>
        /// Snapshots expansion state and expands every node ShouldExpandForSearch accepts.
        /// No-op if a snapshot is already active. Returns true if the visible list changed.
        /// </summary>
        private bool EnsureSearchExpansion()
        {
            if (ShouldExpandForSearch == null) return false;
            if (preSearchExpansion != null) return false;
            if (model.Root == null) return false;

            SyncModelConfig();
            preSearchExpansion = new Dictionary<InspectionTreeItem, bool>();
            bool changed = SnapshotAndExpand(model.Root);
            if (changed)
                model.Reflatten();
            return changed;
        }

        private bool SnapshotAndExpand(InspectionTreeItem node)
        {
            bool changed = false;
            foreach (var child in node.Children)
            {
                if (child.IsExpandable)
                {
                    preSearchExpansion[child] = child.IsExpanded;
                    if (!child.IsExpanded && ShouldExpandForSearch(child))
                    {
                        OnBeforeExpand?.Invoke(child);
                        child.IsExpanded = true;
                        changed = true;
                    }
                }
                if (SnapshotAndExpand(child)) changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Restores the pre-search expansion snapshot, repositioning the cursor onto the
        /// previously-selected item or its nearest visible ancestor.
        /// </summary>
        private void RestorePreSearchExpansion()
        {
            if (preSearchExpansion == null) return;

            SyncModelConfig();
            var prevSelected = model.SelectedItem;

            foreach (var kv in preSearchExpansion)
                kv.Key.IsExpanded = kv.Value;
            preSearchExpansion = null;

            model.Reflatten();

            int newIdx = -1;
            var target = prevSelected;
            while (target != null && newIdx < 0)
            {
                newIdx = model.IndexOf(target);
                if (newIdx < 0) target = target.Parent;
            }
            if (newIdx >= 0)
                model.SetSelectedIndex(newIdx);
            else
                model.SetSelectedIndex(model.SelectedIndex);
        }

        #endregion

        #region Fine-Grained Navigation

        public void SelectNext()
        {
            if (model.Count == 0) return;
            SyncModelConfig();
            StepAndAnnounce(forward: true);
        }

        public void SelectPrevious()
        {
            if (model.Count == 0) return;
            SyncModelConfig();
            StepAndAnnounce(forward: false);
        }

        /// <summary>One plain row step; an unwrapped end answers with the edge tone alone.</summary>
        private void StepAndAnnounce(bool forward)
        {
            MoveResult result = forward ? model.MoveNext() : model.MovePrevious();
            if (!MenuHelper.SoundMove(result))
                return;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Up arrow: steps between typeahead matches while a search has any, otherwise moves
        /// like <see cref="SelectPrevious"/>, which never consults the search.
        /// </summary>
        public void NavigatePrevious()
        {
            if (model.Count == 0) return;
            SyncModelConfig();
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int prev = typeahead.GetPreviousMatch(model.SelectedIndex);
                if (prev >= 0)
                {
                    MenuHelper.SoundMatchMove(model.SelectedIndex, prev, -1);
                    model.SetSelectedIndex(prev);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceWithSearch();
                }
            }
            else
            {
                StepAndAnnounce(forward: false);
            }
        }

        /// <summary>Down arrow: the <see cref="NavigatePrevious"/> twin.</summary>
        public void NavigateNext()
        {
            if (model.Count == 0) return;
            SyncModelConfig();
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int next = typeahead.GetNextMatch(model.SelectedIndex);
                if (next >= 0)
                {
                    MenuHelper.SoundMatchMove(model.SelectedIndex, next, 1);
                    model.SetSelectedIndex(next);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceWithSearch();
                }
            }
            else
            {
                StepAndAnnounce(forward: true);
            }
        }

        /// <summary>
        /// Home: first sibling, or (<paramref name="ctrl"/>) absolute first. During an active
        /// search with matches, jumps to the first match and KEEPS the search, unlike
        /// <see cref="JumpToFirst"/>, which always clears it first.
        /// </summary>
        public void HandleHomeKey(bool ctrl)
        {
            if (model.Count == 0) return;
            SyncModelConfig();
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int first = typeahead.GetFirstMatch();
                if (first >= 0)
                {
                    model.SetSelectedIndex(first);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceWithSearch();
                }
                return;
            }
            typeahead.ClearSearch();
            RestorePreSearchExpansion();
            var homeResult = model.HomeKey(ctrl);
            if (homeResult.Changed) PlayTickAndAnnounce();
        }

        /// <summary>End: the <see cref="HandleHomeKey"/> twin.</summary>
        public void HandleEndKey(bool ctrl)
        {
            if (model.Count == 0) return;
            SyncModelConfig();
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int last = typeahead.GetLastMatch();
                if (last >= 0)
                {
                    model.SetSelectedIndex(last);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceWithSearch();
                }
                return;
            }
            typeahead.ClearSearch();
            RestorePreSearchExpansion();
            var endResult = model.EndKey(ctrl);
            if (endResult.Changed) PlayTickAndAnnounce();
        }

        public void ExpandOrDrillDown()
        {
            // Out of bounds must return before any search orchestration runs.
            if (model.Count == 0 || model.SelectedIndex < 0 || model.SelectedIndex >= model.Count)
                return;

            SyncModelConfig();
            typeahead.ClearSearch();
            // Interacting with a search result commits its auto-expansion.
            preSearchExpansion = null;

            var result = model.ExpandOrDrillDown();
            switch (result.Kind)
            {
                case TreeActionKind.Rejected:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;

                case TreeActionKind.Expanded:
                    SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                    AnnounceStateChange();
                    break;

                case TreeActionKind.ExpandedSubmenu:
                    SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                    // The user just expanded this node, so suppress the parent prefix.
                    lastAnnouncedParent = result.Node;
                    AnnounceCurrentItem();
                    break;

                case TreeActionKind.DrilledToChild:
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                    break;
            }
        }

        public void CollapseOrDrillUp()
        {
            // Out of bounds must return before any search orchestration runs.
            if (model.Count == 0 || model.SelectedIndex < 0 || model.SelectedIndex >= model.Count)
                return;

            SyncModelConfig();
            typeahead.ClearSearch();
            preSearchExpansion = null;

            var result = model.CollapseOrDrillUp();
            switch (result.Kind)
            {
                case TreeActionKind.Rejected:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;

                case TreeActionKind.Collapsed:
                    SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                    AnnounceStateChange();
                    break;

                case TreeActionKind.CollapsedToParent:
                    SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                    break;

                case TreeActionKind.DrilledToParent:
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                    break;
            }
        }

        public void ExpandAllSiblings()
        {
            // Checked here as well as in the model: an out-of-range no-op must stay silent,
            // and the model's own guard is indistinguishable from "nothing to expand".
            if (model.Count == 0 || model.SelectedIndex < 0 || model.SelectedIndex >= model.Count)
                return;

            SyncModelConfig();
            var result = model.ExpandAllSiblings();

            if (result.ExpandedCount > 0)
            {
                typeahead.ClearSearch();
                // An explicit expand commits; the pre-search snapshot is not restored.
                preSearchExpansion = null;

                EmbeddedAudioHelper.PlaySoundDefWithReverb(SoundDefOf.FloatMenu_Open);
                TolkHelper.Speak(
                    (result.ExpandedCount == 1
                        ? "RimWorldAccess.Tree.ExpandedCountOne"
                        : "RimWorldAccess.Tree.ExpandedCountMany").Loc(result.ExpandedCount));

                if (IsSubmenuMode)
                    AnnounceCurrentItem();
            }
            else
            {
                // Nothing expanded: say why rather than staying silent.
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak((result.AnyExpandable
                    ? "RimWorldAccess.Tree.AllAlreadyExpanded"
                    : "RimWorldAccess.Tree.NoneToExpand").Loc());
            }
        }

        /// <summary>
        /// Escape: clears an active typeahead search and restores pre-search expansion, or
        /// restores expansion alone when a no-match already cleared the search. Returns false
        /// when there is nothing search-related to unwind, leaving close behavior to the caller.
        /// </summary>
        public bool HandleEscape()
        {
            SyncModelConfig();

            if (typeahead.HasActiveSearch)
            {
                typeahead.ClearSearchAndAnnounce();
                RestorePreSearchExpansion();
                AnnounceCurrentItem();
                return true;
            }
            if (preSearchExpansion != null)
            {
                RestorePreSearchExpansion();
                AnnounceCurrentItem();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Moves the cursor to the previous/next item satisfying <see cref="IsSectionBoundary"/>.
        /// Rejects with a sound when no boundary predicate is configured, a search is active, or
        /// there is no further section. With <paramref name="wrap"/>, an unsuccessful scan
        /// continues from the opposite end back toward the cursor instead of rejecting.
        /// </summary>
        public void JumpToAdjacentSection(bool forward, bool wrap = false)
        {
            SyncModelConfig();

            if (IsSectionBoundary == null || model.Count == 0 || typeahead.HasActiveSearch)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            int step = forward ? 1 : -1;
            int start = model.SelectedIndex;

            for (int i = start + step; i >= 0 && i < model.Count; i += step)
            {
                if (IsSectionBoundary(model.Visible[i]))
                {
                    model.SetSelectedIndex(i);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                    return;
                }
            }

            if (wrap)
            {
                for (int i = forward ? 0 : model.Count - 1; i != start && i >= 0 && i < model.Count; i += step)
                {
                    if (IsSectionBoundary(model.Visible[i]))
                    {
                        MenuHelper.PlayWrapTone();
                        model.SetSelectedIndex(i);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceCurrentItem();
                        return;
                    }
                }
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        public void JumpToFirst(bool absolute)
        {
            if (model.Count == 0) return;
            SyncModelConfig();
            typeahead.ClearSearch();
            RestorePreSearchExpansion();
            var result = model.HomeKey(absolute);
            if (result.Changed) PlayTickAndAnnounce();
        }

        public void JumpToLast(bool absolute)
        {
            if (model.Count == 0) return;
            SyncModelConfig();
            typeahead.ClearSearch();
            RestorePreSearchExpansion();
            var result = model.EndKey(absolute);
            if (result.Changed) PlayTickAndAnnounce();
        }

        /// <summary>Re-announces the current item in the standard format.</summary>
        public void ReannounceCurrentItem()
        {
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Rebuilds the flattened visible list. Call after adding or removing tree nodes
        /// externally.
        /// </summary>
        public void RebuildVisibleList()
        {
            SyncModelConfig();
            model.Reflatten();
        }

        /// <summary>Sets the selected index, clamped to the valid range.</summary>
        public void SetSelectedIndex(int index)
        {
            model.SetSelectedIndex(index);
        }

        /// <summary>
        /// The last visited child of a parent, or null when TrackLastChild is off or nothing
        /// was tracked.
        /// </summary>
        public InspectionTreeItem GetLastChild(InspectionTreeItem parent)
        {
            return model.GetLastChild(parent);
        }

        /// <summary>
        /// Moves the cursor to <paramref name="target"/> after a structural tree change,
        /// re-expanding its ancestor chain. Unlike a plain IndexOf over
        /// <see cref="VisibleItems"/> this works in submenu mode, where an expanded target is
        /// itself invisible and the cursor lands on its first visible descendant. Returns false
        /// when the target is no longer attached to the tree — a rebuilt subtree orphans its old
        /// items, so callers walk up item.Parent and retry with the nearest surviving ancestor.
        /// </summary>
        public bool TryRevealAndSelect(InspectionTreeItem target)
        {
            if (target == null || model.Root == null)
                return false;

            // Orphans keep stale Parent pointers after a Children.Clear(), so attachment must be
            // checked hop by hop up to the current root.
            var cur = target;
            while (cur.Parent != null)
            {
                if (!cur.Parent.Children.Contains(cur))
                    return false;
                cur = cur.Parent;
            }
            if (!ReferenceEquals(cur, model.Root))
                return false;

            for (var ancestor = target.Parent; ancestor != null; ancestor = ancestor.Parent)
                ancestor.IsExpanded = true;

            SyncModelConfig();
            model.Reflatten();

            var landing = target;
            int index = model.IndexOf(landing);
            while (index < 0 && landing.IsExpanded && landing.Children.Count > 0)
            {
                landing = landing.Children[0];
                index = model.IndexOf(landing);
            }
            if (index < 0)
                return false;

            model.SetSelectedIndex(index);
            return true;
        }

        /// <summary>
        /// Marks the selected item's parent as already announced, suppressing the submenu
        /// parent prefix. Call before ReannounceCurrentItem when the user already knows which
        /// section they are in.
        /// </summary>
        public void MarkCurrentParentAsAnnounced()
        {
            var item = model.SelectedItem;
            if (item != null)
                lastAnnouncedParent = item.Parent;
        }

        #endregion

        #region Announcements

        private void AnnounceCurrentItem()
        {
            var item = model.SelectedItem;
            if (item == null) return;

            string announcement = FormatItemAnnouncement != null
                ? FormatItemAnnouncement(item)
                : DefaultFormatItemAnnouncement(item);
            announcement = GetSubmenuParentPrefix(item) + announcement;
            TolkHelper.SpeakData(announcement);
        }

        private void AnnounceStateChange()
        {
            var item = model.SelectedItem;
            if (item == null) return;

            if (FormatStateChangeAnnouncement != null)
            {
                TolkHelper.SpeakData(FormatStateChangeAnnouncement(item));
                return;
            }

            // The short label keeps the full summary out of every expand/collapse.
            if (!string.IsNullOrEmpty(item.ExpandedLabel))
            {
                TolkHelper.SpeakData(item.ExpandedLabel + FormatExpansionSuffix(item, AnnounceChildCounts));
                return;
            }

            AnnounceCurrentItem();
        }

        private void AnnounceWithSearch()
        {
            var item = model.SelectedItem;
            if (item == null) return;

            string announcement = FormatSearchAnnouncement != null
                ? FormatSearchAnnouncement(item, typeahead)
                : DefaultFormatSearchAnnouncement(item);
            announcement = GetSubmenuParentPrefix(item) + announcement;
            TolkHelper.SpeakData(announcement);
        }

        #endregion

        #region Default Announcement Formats

        /// <summary>
        /// Default item announcement: "Label, expanded, 3 items. 1 of 5. level 2". An item with
        /// an ExpandedLabel uses the short form while expanded.
        /// </summary>
        public string DefaultFormatItemAnnouncement(InspectionTreeItem item)
        {
            string label;
            if (item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel))
                label = item.ExpandedLabel;
            else
                label = item.Label.TrimEnd('.', '!', '?');

            string stateIndicator = FormatExpansionSuffix(item, AnnounceChildCounts);

            var (position, total) = GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string positionSection = string.IsNullOrEmpty(positionPart)
                ? "" : $". {positionPart}";

            string levelSuffix = MenuHelper.GetLevelSuffix(levelTrackingKey, item.IndentLevel);

            return $"{label}{stateIndicator}{positionSection}{levelSuffix}";
        }

        /// <summary>
        /// Default search announcement: "Label, expanded, 2 of 5 matches for 'w'"
        /// </summary>
        public string DefaultFormatSearchAnnouncement(InspectionTreeItem item)
        {
            string label;
            if (item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel))
                label = item.ExpandedLabel;
            else
                label = item.Label.TrimEnd('.', '!', '?');

            string stateIndicator = FormatExpansionSuffix(item);

            return typeahead.BuildItemAnnouncement($"{label}{stateIndicator}");
        }

        #endregion

        #region Expansion State Formatting

        /// <summary>
        /// The bare state word ("expanded" or "collapsed"), or empty for a non-expandable item.
        /// Keeps the localizable vocabulary in one place.
        /// </summary>
        public static string GetExpansionStateWord(InspectionTreeItem item)
        {
            if (item == null || !item.IsExpandable) return "";
            return (item.IsExpanded
                ? "RimWorldAccess.Tree.StateExpanded"
                : "RimWorldAccess.Tree.StateCollapsed").Translate().ToString();
        }

        /// <summary>
        /// An appendable state suffix — ", expanded" or ", collapsed", plus the child count on
        /// both states when <paramref name="includeChildCount"/> is set. Empty for a
        /// non-expandable item.
        /// </summary>
        public static string FormatExpansionSuffix(InspectionTreeItem item, bool includeChildCount = false)
        {
            string state = GetExpansionStateWord(item);
            if (string.IsNullOrEmpty(state)) return "";
            if (includeChildCount)
            {
                int n = item.Children.Count;
                return (n == 1
                    ? "RimWorldAccess.Tree.ExpansionSuffixWithCountOne"
                    : "RimWorldAccess.Tree.ExpansionSuffixWithCountMany").Translate(state, n).ToString();
            }
            return "RimWorldAccess.Tree.ExpansionSuffix".Translate(state).ToString();
        }

        /// <summary>
        /// A space-prefixed state suffix, " expanded" or " collapsed"; empty for a
        /// non-expandable item.
        /// </summary>
        public static string FormatExpansionSpaceSuffix(InspectionTreeItem item)
        {
            string state = GetExpansionStateWord(item);
            if (string.IsNullOrEmpty(state)) return "";
            return "RimWorldAccess.Tree.ExpansionSpaceSuffix".Translate(state).ToString();
        }

        #endregion

        #region Internal Helpers

        /// <summary>The node's 1-indexed position among its siblings, and their total.</summary>
        public (int position, int total) GetSiblingPosition(InspectionTreeItem item)
        {
            return model.GetSiblingPosition(item);
        }

        private List<string> GetVisibleLabels()
        {
            return model.Visible.Select(item => item.Label).ToList();
        }

        /// <summary>
        /// Labels typeahead matches against, index-aligned with VisibleItems. Under
        /// ShouldExpandForSearch the auto-expanded structural nodes get an empty string, which
        /// never matches, so the cursor lands on the leaves the expansion exposed.
        /// </summary>
        private List<string> GetSearchableLabels()
        {
            if (SearchableLabelSelector != null)
            {
                var selected = new List<string>(model.Count);
                foreach (var item in model.Visible)
                    selected.Add(SearchableLabelSelector(item) ?? "");
                return selected;
            }

            if (ShouldExpandForSearch == null)
                return GetVisibleLabels();

            var result = new List<string>(model.Count);
            foreach (var item in model.Visible)
            {
                bool skip = item.IsExpandable && ShouldExpandForSearch(item);
                result.Add(skip ? "" : item.Label);
            }
            return result;
        }

        public void HandleTypeahead(char c)
        {
            SyncModelConfig();

            if (!typeahead.HasActiveSearch)
                EnsureSearchExpansion();

            var labels = GetSearchableLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                model.SetSelectedIndex(newIndex);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceWithSearch();
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                typeahead.SpeakNoMatches();
                // Snapshot remains so the next typed character reuses the expanded view.
            }
        }

        /// <summary>
        /// Typeahead-character entry point for states that drive their own keyboard routing.
        /// </summary>
        public void HandleTypeaheadCharacter(char c) => HandleTypeahead(c);

        /// <summary>
        /// Backspace handler for states that drive their own keyboard routing. Restores
        /// pre-search expansion once the buffer empties.
        /// </summary>
        public void HandleTypeaheadBackspace()
        {
            if (!typeahead.HasActiveSearch) return;

            SyncModelConfig();
            var labels = GetSearchableLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                    model.SetSelectedIndex(newIndex);
                SoundDefOf.Click.PlayOneShotOnCamera();
                if (!typeahead.HasActiveSearch)
                {
                    RestorePreSearchExpansion();
                    AnnounceCurrentItem();
                }
                else
                {
                    AnnounceWithSearch();
                }
            }
        }

        /// <summary>
        /// Clears any active search and restores the pre-search expansion snapshot. Use after
        /// the user commits to an item via Enter or Space.
        /// </summary>
        public void CommitAndClearSearch()
        {
            if (typeahead.HasActiveSearch)
            {
                typeahead.ClearSearch();
                RestorePreSearchExpansion();
            }
            else if (preSearchExpansion != null)
            {
                RestorePreSearchExpansion();
            }
        }

        /// <summary>
        /// Clears the search buffer and collapses back to the pre-search shape, but keeps the
        /// selected item's ancestor chain expanded so the cursor stays on it. Use when the user
        /// picks a search result and remains positioned there.
        /// </summary>
        public void CommitSearchKeepingPath()
        {
            if (!typeahead.HasActiveSearch && preSearchExpansion == null)
                return;

            SyncModelConfig();
            var target = model.SelectedItem;

            typeahead.ClearSearch();

            if (preSearchExpansion != null)
            {
                foreach (var kv in preSearchExpansion)
                    kv.Key.IsExpanded = kv.Value;
                preSearchExpansion = null;
            }

            // Re-expand only the target's ancestor chain, so it stays visible.
            var ancestor = target?.Parent;
            while (ancestor != null)
            {
                if (ancestor.IsExpandable)
                    ancestor.IsExpanded = true;
                ancestor = ancestor.Parent;
            }

            model.Reflatten();

            int idx = target != null ? model.IndexOf(target) : -1;
            if (idx >= 0)
                model.SetSelectedIndex(idx);
            else
                model.SetSelectedIndex(model.SelectedIndex);
        }

        /// <summary>
        /// Enter/KeypadEnter: custom activate, then fall back to toggle expand/collapse.
        /// </summary>
        public void ActivateCurrent()
        {
            var item = model.SelectedItem;
            if (item == null) return;

            // Committing to an item drops the search-expansion snapshot, so a later Escape
            // does not restore it.
            if (OnActivate != null && OnActivate(item))
            {
                typeahead.ClearSearch();
                preSearchExpansion = null;
                return;
            }

            if (item.OnActivate != null)
            {
                typeahead.ClearSearch();
                preSearchExpansion = null;
                item.OnActivate();
                return;
            }

            if (item.IsExpandable)
            {
                if (IsSubmenuMode)
                {
                    ExpandOrDrillDown();
                }
                else
                {
                    typeahead.ClearSearch();
                    preSearchExpansion = null;
                    if (!item.IsExpanded)
                        OnBeforeExpand?.Invoke(item);
                    item.IsExpanded = !item.IsExpanded;
                    SyncModelConfig();
                    model.Reflatten();
                    if (item.IsExpanded)
                        SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                    else
                        SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                    AnnounceStateChange();
                }
            }
        }

        /// <summary>Delete: custom delete handler, else the item's own OnDelete callback.</summary>
        public void DeleteCurrent()
        {
            var item = model.SelectedItem;
            if (item == null) return;

            if (OnDelete != null && OnDelete(item))
                return;

            if (item.OnDelete != null)
            {
                item.OnDelete();
                return;
            }
        }

        /// <summary>
        /// Alt+I: custom info handler, else the item's own OnInfo callback, else the LinkedDef
        /// walk up the ancestor chain.
        /// </summary>
        public void InfoCurrent()
        {
            var item = model.SelectedItem;
            if (item == null)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }

            if (OnInfo != null && OnInfo(item))
                return;

            if (item.OnInfo != null)
            {
                item.OnInfo();
                return;
            }

            if (item.LinkedDef != null)
            {
                InfoCardState.OpenInfoCardForDef(item.LinkedDef);
                return;
            }

            var parent = item.Parent;
            while (parent != null && parent != model.Root)
            {
                if (parent.LinkedDef != null)
                {
                    InfoCardState.OpenInfoCardForDef(parent.LinkedDef);
                    return;
                }
                parent = parent.Parent;
            }

            InfoCardState.SpeakNoInfoCardAvailable();
        }

        /// <summary>
        /// A parent label prefix announcing a boundary crossing, when the item's parent differs
        /// from the last announced one. Submenu mode only. The section leads so the user hears
        /// the context first ("Gear. Steel sword", not "Steel sword. Gear").
        /// </summary>
        private string GetSubmenuParentPrefix(InspectionTreeItem item)
        {
            if (!IsSubmenuMode) return "";

            var parent = item.Parent;
            if (parent == null || parent == model.Root)
            {
                if (lastAnnouncedParent != null)
                    lastAnnouncedParent = null;
                return "";
            }

            if (parent == lastAnnouncedParent) return "";

            lastAnnouncedParent = parent;
            string parentLabel = !string.IsNullOrEmpty(parent.ExpandedLabel)
                ? parent.ExpandedLabel
                : parent.Label;
            return $"{parentLabel}. ";
        }

        private void PlayTickAndAnnounce()
        {
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        #endregion

        #region Smart Label Utilities

        /// <summary>
        /// Builds aggregated "smart labels" for expandable nodes with informational children:
        /// the node's current Label moves to ExpandedLabel (the short title) and Label becomes
        /// the title followed by every child label. <paramref name="shouldAggregate"/> selects
        /// which nodes qualify, defaulting to those with no Action-type children. Treeviews with
        /// their own summarization skip this and set ExpandedLabel themselves.
        /// </summary>
        public static void BuildSmartLabels(InspectionTreeItem root, Func<InspectionTreeItem, bool> shouldAggregate = null)
        {
            if (root == null) return;

            if (shouldAggregate == null)
            {
                shouldAggregate = node =>
                    node.IsExpandable &&
                    node.Children.Count > 0 &&
                    !node.Children.Exists(c => c.Type == InspectionTreeItem.ItemType.Action);
            }

            BuildSmartLabelsRecursive(root, shouldAggregate);
        }

        private static void BuildSmartLabelsRecursive(InspectionTreeItem node, Func<InspectionTreeItem, bool> shouldAggregate)
        {
            // Bottom-up, so child labels are finalized before they are aggregated.
            foreach (var child in node.Children)
            {
                BuildSmartLabelsRecursive(child, shouldAggregate);
            }

            if (!shouldAggregate(node))
                return;

            node.ExpandedLabel = node.Label;

            var sb = new System.Text.StringBuilder(node.Label);
            foreach (var child in node.Children)
            {
                string childText = child.Label;
                if (string.IsNullOrEmpty(childText))
                    continue;

                // ". " separates sentences, without doubling an existing terminator.
                if (sb.Length > 0)
                {
                    char lastChar = sb[sb.Length - 1];
                    if (lastChar == '.' || lastChar == '!' || lastChar == '?')
                        sb.Append(' ');
                    else
                        sb.Append(". ");
                }
                sb.Append(childText);
            }

            node.Label = sb.ToString();
        }

        #endregion
    }
}
