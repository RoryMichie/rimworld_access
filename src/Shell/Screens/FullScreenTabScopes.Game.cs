using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Names the faction the keyboard is on, for the real faction list's ring and
    /// auto-scroll. Two scopes present that one list (the Factions tab and the
    /// starting-site landing dialog, both drawn by <see cref="FactionUIUtility"/>),
    /// so the patches ask the topmost rather than either one by name.
    /// </summary>
    internal interface IFactionRowFocusSource
    {
        Faction FocusedFaction { get; }
    }

    /// <summary>
    /// Keyboard focus scope for the windowless in-game Factions tab
    /// (<see cref="FactionTabState"/>). The mod owns no window — FactionTabPatch's
    /// DoWindowContents hijack is the opener — so the scope rides the focus stack
    /// through <see cref="FullScreenTabScopeMirror"/>. One
    /// <see cref="TreeRegionScope"/> content region holds the flattened faction tree,
    /// so navigation, typeahead, expand/collapse and row announcements come from the
    /// shared base.
    /// <c>factionTab.firstAbsolute</c>/<c>lastAbsolute</c>/<c>expandAllSiblings</c>
    /// are DORMANT: <see cref="TreeRegionScope"/> claims the canonical <c>tree.*</c>
    /// ids on the identical chords, so claiming the per-screen twins would register
    /// two claims for one chord; their inventory registrations stay so their
    /// rebindable defaults survive. Typeahead matches each node's bare
    /// <see cref="InspectionTreeItem.Label"/>, not the composed row text.
    /// Escape clears an active search first (the base's typeahead-gated Cancel claim
    /// is registered ahead of this scope's), else closes the tab. Delete stays
    /// unclaimed: no faction node wires <c>OnDelete</c>.
    /// </summary>
    public sealed class FactionTabScope : TreeRegionScope, IFactionRowFocusSource
    {
        /// <summary>
        /// The pushed scope, so <see cref="FactionTabState"/>'s statics can reach the
        /// tree. Null between <c>Open</c> (a full dispatcher pass before the mirror
        /// pushes this scope) and the push: the state writes its root first and
        /// <see cref="OnPush"/> seeds from there.
        /// </summary>
        private static FactionTabScope live;

        public FactionTabScope()
        {
            // Page Up/Down: TreeRegionScope deliberately does not claim these, so this
            // screen claims both. The faction tree sets no IsSectionBoundary, so both
            // always reject.
            Claim("factionTab.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("factionTab.jumpToNextSection", e => PerformJumpToAdjacentSection(true));

            Claim(SharedMenuGrammar.Info, e => PerformInfoCard());

            // Dev-only, so a non-dev RightBracket falls through.
            Claim("factionTab.contextMenu", e => FactionTabState.OpenDevContextMenu(),
                when: () => Prefs.DevMode);

            // Only once no search is active: the base's typeahead claim, registered
            // ahead of this one, clears the search first.
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                FactionTabState.Close();
            }, when: () => !TypeaheadHasActiveSearch);
        }

        /// <summary>The scope driving the open Factions tab, or null before its push / after its pop.</summary>
        internal static FactionTabScope Live
        {
            get { return live; }
        }

        public override string Name
        {
            get { return "faction-tab"; }
        }

        /// <summary>No owning <c>Window</c> exists for this windowless tab — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.Factions.Tree.Root".Translate(); }
        }

        public override void OnPush()
        {
            base.OnPush();
            live = this;
            LoadTree(FactionTabState.CurrentTreeRoot);
        }

        public override void OnPop()
        {
            base.OnPop();
            if (ReferenceEquals(live, this))
            {
                live = null;
            }
            ResetTree();
        }

        /// <summary>
        /// Adopt the state's current tree root, resetting both cursors to the first row.
        /// The region cursor has to be moved explicitly: the region model does not
        /// track the tree's own selection.
        /// </summary>
        internal void LoadTree(InspectionTreeItem root)
        {
            TypeaheadReset();
            SetTreeRoot(root);
            RefreshModel();
            SyncRegionFromCurrentTree();
        }

        /// <summary>
        /// The dev "Show all" toggle's rebuild: replaces the tree root while preserving
        /// expansion state and landing the cursor back on the same LOGICAL faction row
        /// rather than resetting to row 0 as <see cref="LoadTree"/> does.
        /// </summary>
        internal void ReloadTreePreservingState(InspectionTreeItem newRoot)
        {
            TypeaheadReset();
            RefreshModel();
            ListModel region = Model.Region(0);
            int before = region != null && !region.IsEmpty ? region.Index : 0;
            int restored = SetTreeRootPreservingState(newRoot, before);
            RefreshModel();
            ListModel after = Model.Region(0);
            if (after != null && !after.IsEmpty)
            {
                after.MoveTo(Mathf.Clamp(restored >= 0 ? restored : before, 0, after.Count - 1));
            }
        }

        /// <summary>Drop the tree on close.</summary>
        internal void ClearTree()
        {
            TypeaheadReset();
            ResetTree();
        }

        /// <summary>Re-speak the focused row.</summary>
        internal void AnnounceCurrentRow()
        {
            AnnounceCurrentItem();
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            ElementDescription d = new ElementDescription();

            // The short faction name once expanded: the collapsed label carries the
            // faction's whole summary inline.
            string label = item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel)
                ? item.ExpandedLabel
                : item.Label.TrimEnd('.', '!', '?');

            // Expansion rides the LABEL channel because this screen speaks the child
            // count with it ("expanded, 7 items") while the composer's Expanded field
            // carries the bare word; Role/Expanded stay unset so it is spoken once.
            d.Label = label + TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true);

            return d;
        }

        /// <summary>
        /// Typeahead matches a node's own label rather than its composed announcement,
        /// whose label channel also carries the expansion suffix (see
        /// <see cref="DescribeTreeNode"/>).
        /// </summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            if (region == 0)
            {
                int treeIndex = row - PrefixRowCount;
                if (treeIndex >= 0 && treeIndex < Tree.Count)
                {
                    return Tree.Visible[treeIndex].Label ?? "";
                }
            }
            return base.ContentRowSearchText(region, row);
        }

        /// <summary>
        /// Enter/Space on a tree row: the node's own callback if it has one, else an
        /// expandable node toggles expand/collapse (the one place Enter also
        /// collapses), else silence.
        /// </summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.OnActivate != null)
            {
                item.OnActivate();
                return;
            }

            if (item.IsExpandable)
            {
                PerformActivateExpandToggle(item);
            }
        }

        /// <summary>
        /// Alt+I: vanilla's own Dialog_InfoCard for the row's faction, walking up from
        /// the focused row (a goodwill or relations child belongs to the faction above).
        /// </summary>
        private void PerformInfoCard()
        {
            Faction faction = FactionOfCurrentRow();
            if (faction == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Factions.Tab.NoFactionSelected".Loc());
                return;
            }
            Find.WindowStack.Add(new Dialog_InfoCard(faction));
        }

        private Faction FactionOfCurrentRow()
        {
            for (InspectionTreeItem item = CurrentTreeItem(); item != null; item = item.Parent)
            {
                Faction faction = item.Data as Faction;
                if (faction != null)
                {
                    return faction;
                }
            }
            return null;
        }

        /// <summary>
        /// The faction under the keyboard cursor, for the scroll and focus-ring
        /// patches at the bottom of this file.
        /// </summary>
        Faction IFactionRowFocusSource.FocusedFaction
        {
            get { return FactionOfCurrentRow(); }
        }

        /// <summary>Alt+Shift+J routes off the real Factions tab this windowless scope reads.</summary>
        protected override Window PointerSurface
        {
            get { return FactionRowFocusRingPatch.HostWindow; }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            TreePanel panel = PanelFor(TreeRegionIndex);
            if (panel != null)
            {
                FactionRowFocusRingPatch.AddTreeRouteCandidates(panel.Tree.Visible,
                    TreeRegionIndex, PrefixRowCountFor(TreeRegionIndex), candidates, targets);
            }
        }
    }

    /// <summary>
    /// Keyboard focus scope for the windowless mechanitor control-group detail view
    /// (<see cref="MechControlGroupState"/>, opened with Enter on the group's gizmo).
    /// The mod owns no window, so the scope rides the focus stack through
    /// <see cref="FullScreenTabScopeMirror"/>. Two regions: Settings (work mode,
    /// recharge thresholds, select all — vanilla's own order) and Members, one row
    /// per mech. Work mode is a single ComboBox row, not a radio group, because
    /// vanilla's control is a float-menu picker; Enter opens vanilla's own options.
    /// <c>mech.nextPage</c>/<c>previousPage</c> are DORMANT — the base's shared
    /// region claims own the same Tab/Shift+Tab chords.
    /// The inline recharge-range editor
    /// (<see cref="MechControlGroupState.IsEditingRange"/>) is a sub-mode of the row
    /// that opens it, neutralised at the base's virtual seams rather than by racing
    /// its claims: <see cref="MoveItem"/> retargets Up/Down onto the min/max bound
    /// (so <c>mech.range.toggleBound</c> is DORMANT), <see cref="MoveItemEdge"/> and
    /// <see cref="MoveRegion"/> go inert, and <see cref="CanAdjustContentItem"/> stays
    /// false throughout so the shared Left/Right claims never fire and the editor's
    /// own increase/decrease claims — the only ones carrying the Shift step tiering —
    /// receive them. CharSink stays gated off during the edit so a typed character
    /// cannot move the selection out from under it.
    /// An empty Members region is not navigable: Tab keeps the cursor in Settings.
    /// </summary>
    public sealed class MechControlGroupScope : ScreenScope
    {
        private enum SettingsRow
        {
            WorkMode,
            RechargeRange,
            SelectAll,
        }

        private static MechControlGroupScope live;

        private bool announcedOpen;

        public MechControlGroupScope()
        {
            Claim(SharedMenuGrammar.Cancel, e => HandleCancelKey());

            Claim("mech.reassignMech", e => MechControlGroupState.OpenReassignMenu(CurrentMemberIndex()),
                when: InMembersRegion);

            Claim("mech.range.increase", e => AdjustRangeBound(1, e.Shift), when: RangeMode);
            Claim("mech.range.decrease", e => AdjustRangeBound(-1, e.Shift), when: RangeMode);
            Claim("mech.range.reset", e => MechControlGroupState.RangeReset(), when: RangeMode);
        }

        /// <summary>The scope driving the open control group, or null before its push / after its pop.</summary>
        internal static MechControlGroupScope Live
        {
            get { return live; }
        }

        public override string Name
        {
            get { return "mech-control-group"; }
        }

        /// <summary>Windowless: this scope owns Escape itself (range cancel, search clear, or close).</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        private static bool RangeMode()
        {
            return MechControlGroupState.IsEditingRange;
        }

        private bool InMembersRegion()
        {
            RefreshModel();
            return Model.RegionIndex == 1;
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return (region == 0
                ? "RimWorldAccess.Biotech.Mech.Settings"
                : "RimWorldAccess.Biotech.Mech.Members").Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            if (region == 0)
            {
                return MechControlGroupState.IsActive ? 3 : 0;
            }
            return MechControlGroupState.MemberCount;
        }

        protected override void RefreshContent()
        {
            MechControlGroupState.RefreshMembers();
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return region == 0 ? DescribeSettingsRow(index) : DescribeMemberRow(index);
        }

        private ElementDescription DescribeSettingsRow(int index)
        {
            MechanitorControlGroup group = MechControlGroupState.Group;
            if (group == null)
            {
                return new ElementDescription();
            }
            switch ((SettingsRow)index)
            {
                case SettingsRow.WorkMode:
                    return new ElementDescription
                    {
                        Label = "CurrentMechWorkMode".Translate().ToString(),
                        Role = ElementRole.ComboBox,
                        Value = group.WorkMode.LabelCap,
                        Extras = group.WorkMode.description,
                    };
                case SettingsRow.RechargeRange:
                    return new ElementDescription
                    {
                        Label = "MechRechargeSettingsTitle".Translate().ToString(),
                        Role = ElementRole.Button,
                        Value = MechControlGroupState.FormatRange(group.mechRechargeThresholds),
                        Extras = "MechRechargeSettingsExplanation".Translate().ToString(),
                    };
                default:
                    return new ElementDescription
                    {
                        Label = MechControlGroupState.SelectAllLabel(),
                        Role = ElementRole.Button,
                        Extras = "CommandSelectAllMechsDesc".Translate().ToString(),
                    };
            }
        }

        private ElementDescription DescribeMemberRow(int index)
        {
            Pawn mech = MechControlGroupState.MemberAt(index);
            if (mech == null)
            {
                return new ElementDescription();
            }
            return new ElementDescription
            {
                Label = mech.LabelCap,
                Role = ElementRole.Button,
                Extras = MechControlGroupState.MemberStatus(mech),
            };
        }

        /// <summary>
        /// Enter: confirms the edit in the range sub-mode, else runs the focused row's
        /// action, every one of which rides a vanilla vehicle or a gated state method.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (MechControlGroupState.IsEditingRange)
            {
                // RangeConfirm speaks the saved range itself: one announcement per action.
                MechControlGroupState.RangeConfirm();
                return;
            }
            if (region == 1)
            {
                MechControlGroupState.JumpToMember(index);
                return;
            }
            switch ((SettingsRow)index)
            {
                case SettingsRow.WorkMode:
                    MechControlGroupState.OpenWorkModeMenu();
                    break;
                case SettingsRow.RechargeRange:
                    MechControlGroupState.OpenRangeEditor();
                    AnnounceRangeBound();
                    break;
                case SettingsRow.SelectAll:
                    MechControlGroupState.SelectAllMechs();
                    break;
            }
        }

        /// <summary>
        /// False in both modes: no browse-mode row is arrow-adjustable, and inside the
        /// range editor the shared claims must stand down so this scope's own
        /// <c>mech.range.increase</c>/<c>decrease</c> claims receive Left/Right.
        /// </summary>
        protected override bool CanAdjustContentItem(int region, int index)
        {
            return false;
        }

        public override void OnPush()
        {
            base.OnPush();
            live = this;
            announcedOpen = false;
            GizmoRingRequest.Provider = RingedGizmo;
            GizmoRingRequest.ElementRectProvider = RingedElementRect;
        }

        public override void OnPop()
        {
            base.OnPop();
            if (ReferenceEquals(live, this))
            {
                live = null;
            }
            GizmoRingRequest.Provider = null;
            GizmoRingRequest.ElementRectProvider = null;
        }

        /// <summary>
        /// The control-group gizmo this view drills into, and the ring's fallback:
        /// <see cref="RingedElementRect"/> narrows it onto the focused element whenever
        /// vanilla drew that element this frame. The drill-in is mutually exclusive with
        /// the map-controls scopes sharing the slot.
        /// </summary>
        private static Gizmo RingedGizmo()
        {
            return MechControlGroupState.SourceGizmo;
        }

        /// <summary>
        /// The focused row's own rect inside the gizmo: a settings row is one of
        /// vanilla's three top-strip controls (the group label doubles as select-all), a
        /// member row that mech's portrait tile. MechanitorControlGroupGizmo.GizmoOnGUI
        /// computes them all as locals, so the rects come from MechGizmoElementPatch's
        /// recording of that body, never from geometry of our own.
        /// </summary>
        private Rect? RingedElementRect()
        {
            // The provider outlives focus (unset only on OnPop), so it must answer only
            // while this scope IS the focus, or another scope sharing the ring slot would
            // get its gizmo narrowed onto mech element rects.
            if (!ReferenceEquals(FocusStack.Top, this))
            {
                return null;
            }
            // Drift gate: refresh only when the cached member region is stale against the
            // live roster, not every ring frame.
            if (Model.Region(1) == null || Model.Region(1).Count != MechControlGroupState.MemberCount)
            {
                RefreshModel();
            }
            ListModel region = Model.CurrentRegion;
            if (region == null)
            {
                return null;
            }
            if (Model.RegionIndex == 1)
            {
                return MechGizmoElementRects.TryGetTile(MechControlGroupState.MemberAt(region.Index), out Rect tile)
                    ? tile
                    : (Rect?)null;
            }
            MechGizmoElement element;
            switch ((SettingsRow)region.Index)
            {
                case SettingsRow.WorkMode:
                    element = MechGizmoElement.WorkMode;
                    break;
                case SettingsRow.RechargeRange:
                    element = MechGizmoElement.RechargeSettings;
                    break;
                default:
                    element = MechGizmoElement.SelectAll;
                    break;
            }
            return MechGizmoElementRects.TryGetFixed(element, out Rect rect) ? rect : (Rect?)null;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            string tabCount = TabCountFragment();
            string opening = "RimWorldAccess.Biotech.Mech.GroupSettingsHeader"
                .Translate(MechControlGroupState.GroupLabel()).ToString();
            TolkHelper.SpeakData(string.IsNullOrEmpty(tabCount) ? opening : tabCount + ". " + opening);
            AnnounceCurrentItem();
        }

        /// <summary>Re-speak the focused row.</summary>
        internal void AnnounceCurrentRow()
        {
            AnnounceCurrentItem();
        }

        /// <summary>Up/Down: the min/max bound while the range editor is open, plain row movement otherwise.</summary>
        protected override void MoveItem(int delta)
        {
            if (MechControlGroupState.IsEditingRange)
            {
                MechControlGroupState.RangeToggleBound();
                AnnounceRangeBound();
                return;
            }
            base.MoveItem(delta);
        }

        /// <summary>Home/End: silently inert while the range editor is open.</summary>
        protected override void MoveItemEdge(bool first)
        {
            if (MechControlGroupState.IsEditingRange)
                return;
            base.MoveItemEdge(first);
        }

        /// <summary>Tab/Shift+Tab: silently inert while the range editor is open.</summary>
        protected override void MoveRegion(bool forward)
        {
            if (MechControlGroupState.IsEditingRange)
                return;
            base.MoveRegion(forward);
        }

        /// <summary>Escape: cancel the range edit, else clear a search (the base's own claim wins that case), else close.</summary>
        private void HandleCancelKey()
        {
            ShellFrameStamps.MarkCancelConsumed();
            if (MechControlGroupState.IsEditingRange)
            {
                MechControlGroupState.RangeCancel();
                return;
            }
            // The base's typeahead-active Escape claim, registered ahead of this one,
            // clears a live search first, so reaching here means none is active.
            MechControlGroupState.CloseAndAnnounce();
        }

        /// <summary>Characters never reach the shared typeahead while the range editor owns the keyboard.</summary>
        public override bool HandleChar(char c)
        {
            if (MechControlGroupState.IsEditingRange)
                return false;
            return base.HandleChar(c);
        }

        private void AdjustRangeBound(int direction, bool shiftHeld)
        {
            MechControlGroupState.RangeAdjust(direction, shiftHeld);
            AnnounceRangeBound();
        }

        /// <summary>
        /// The focused bound of the inline range editor as a stepper element: name,
        /// value, position within the two bounds, and the working range as the tail.
        /// </summary>
        private void AnnounceRangeBound()
        {
            FloatRange range = MechControlGroupState.EditingRange;
            bool editingMin = MechControlGroupState.EditingMinimum;
            var d = new ElementDescription
            {
                Label = (editingMin
                    ? "RimWorldAccess.Biotech.Mech.RangeMinimum"
                    : "RimWorldAccess.Biotech.Mech.RangeMaximum").Translate().ToString(),
                Role = ElementRole.Stepper,
                Value = MechControlGroupState.FormatPercent(editingMin ? range.min : range.max),
                AtMinimum = editingMin ? range.min <= 0f : range.max <= range.min,
                AtMaximum = editingMin ? range.min >= range.max : range.max >= 1f,
                Extras = MechControlGroupState.FormatRange(range),
                PositionIndex = editingMin ? 1 : 2,
                PositionCount = 2,
            };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeFocus(
                d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
        }

        private int CurrentMemberIndex()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            return region == null ? -1 : region.Index;
        }
    }

    /// <summary>
    /// Reconciles the windowless full-screen-tab family scopes. Mech's gate is bare
    /// IsActive (its interactive paths route through WindowlessFloatMenuState, which adds
    /// no window); FactionTab additionally stands down while a real dialog with its own
    /// attached scope is up, because its Alt+I opens the faction info card while
    /// FactionTabState stays active and an unconditional per-frame Push would re-float
    /// this scope above the card. The two are mutually exclusive by construction, so push
    /// order between them is arbitrary.
    /// </summary>
    internal static class FullScreenTabScopeMirror
    {
        private static readonly FactionTabScope factionTab = new FactionTabScope();
        private static readonly MechControlGroupScope mechControlGroup = new MechControlGroupScope();

        public static void Reconcile()
        {
            ReconcileOne(factionTab, FactionTabState.IsActive
                && !ShellGuards.ForeignDialogWindowAbove());
            ReconcileOne(mechControlGroup, MechControlGroupState.IsActive);
        }

        private static void ReconcileOne(FocusScope scope, bool live)
        {
            if (live)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }

    /// <summary>
    /// Scrolls the real vanilla Factions window to the keyboard-focused faction via
    /// <see cref="MainTabWindow_Factions.ScrollToFaction"/>. Change-gated: vanilla
    /// consumes the request by pinning scrollPosition.y for exactly one frame then
    /// clearing it (decompiled FactionUIUtility.cs:74-77), so calling it every frame
    /// would freeze the scroll view in place.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Factions), nameof(MainTabWindow_Factions.DoWindowContents))]
    internal static class FactionWindowScrollPatch
    {
        private static Faction lastScrolled;

        [HarmonyPrefix]
        public static void Prefix(MainTabWindow_Factions __instance)
        {
            try
            {
                IFactionRowFocusSource scope = FocusStackLookup.TopmostOfType<IFactionRowFocusSource>();
                if (scope == null)
                {
                    lastScrolled = null;
                    return;
                }
                Faction focused = scope.FocusedFaction;
                if (focused == lastScrolled)
                {
                    return;
                }
                lastScrolled = focused;
                if (focused != null)
                {
                    __instance.ScrollToFaction(focused);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Faction window scroll error", ex);
            }
        }
    }

    /// <summary>
    /// Supplies the landing dialog's missing scroll target:
    /// <see cref="FactionUIUtility.DoWindowContents"/> scrolls to a faction the caller
    /// names, and <c>Dialog_FactionDuringLanding</c> never names one, so its list stays
    /// put while the keyboard walks past the fold. The Factions tab reaches the same
    /// parameter through its own <c>ScrollToFaction</c> and is excluded here.
    /// Change-gated for the reason <see cref="FactionWindowScrollPatch"/> gives.
    /// </summary>
    [HarmonyPatch(typeof(FactionUIUtility), nameof(FactionUIUtility.DoWindowContents))]
    internal static class FactionLandingScrollPatch
    {
        private static Faction lastScrolled;

        [HarmonyPrefix]
        public static void Prefix(ref Faction scrollToFaction)
        {
            try
            {
                FactionLandingScope scope =
                    FocusStackLookup.TopmostOfType<IFactionRowFocusSource>() as FactionLandingScope;
                if (scope == null)
                {
                    lastScrolled = null;
                    return;
                }
                Faction focused = ((IFactionRowFocusSource)scope).FocusedFaction;
                if (focused == lastScrolled)
                {
                    return;
                }
                lastScrolled = focused;
                scrollToFaction = focused;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Faction landing scroll error", ex);
            }
        }
    }

    /// <summary>
    /// Paints the shared keyboard focus ring over vanilla's own faction row, taking the
    /// row height from <c>__result</c> — the height vanilla just consumed — never a
    /// hardcoded one. Serves both hosts of that list, the Factions tab and
    /// Dialog_FactionDuringLanding, which share this <see cref="FactionUIUtility"/> body.
    /// </summary>
    [HarmonyPatch(typeof(FactionUIUtility), "DrawFactionRow")]
    internal static class FactionRowFocusRingPatch
    {
        // One cache for both hosts: they never draw in the same frame, and the recorded
        // host window follows whichever one drew.
        private static readonly RowGeometryCache rowGeometry = new RowGeometryCache();

        /// <summary>The window whose GUI pass last drew the faction list, for pointer routing.</summary>
        internal static Window HostWindow
        {
            get { return rowGeometry.HostWindow; }
        }

        /// <summary>
        /// Contributes the drawn faction rows of a flattened faction tree, for either
        /// host's scope. A drilled-in goodwill or relations child is ours alone and has
        /// no rect, so only the faction rows contribute.
        /// </summary>
        internal static void AddTreeRouteCandidates(IReadOnlyList<InspectionTreeItem> visible,
            int region, int prefix, List<PointerHitCandidate> candidates, List<ScreenScope.RouteTarget> targets)
        {
            for (int i = 0; i < visible.Count; i++)
            {
                rowGeometry.AddCandidate(visible[i].Data as Faction, region, prefix + i, candidates, targets);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Faction faction, float rowY, Rect fillRect, float __result)
        {
            try
            {
                IFactionRowFocusSource scope = FocusStackLookup.TopmostOfType<IFactionRowFocusSource>();
                if (scope == null)
                {
                    return;
                }
                Rect row = new Rect(fillRect.x, rowY, fillRect.width, __result);
                rowGeometry.Record(faction, row);
                if (Event.current.type != EventType.Repaint || faction != scope.FocusedFaction)
                {
                    return;
                }
                FocusRing.Draw(row.ContractedBy(1f));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Faction row focus ring error", ex);
            }
        }
    }
}
