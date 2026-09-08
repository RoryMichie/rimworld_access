using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The History window's Statistics tab: a single-region screen of read-only
    /// <see cref="HistoryHelper.StatisticEntry"/> rows with nothing to activate. Pushed above the
    /// window-attached <see cref="HistoryScope"/> dispatcher by
    /// <see cref="HistorySubTabScopeMirror"/>.
    ///
    /// SNAPSHOT SEMANTICS (deliberate): stats are collected once per tab-entry, in
    /// <see cref="OnPush"/>, and never refreshed while the tab stays focused, so a player who
    /// leaves it open while wealth changes will not hear updated numbers.
    /// </summary>
    public sealed class HistoryStatsScope : ScreenScope
    {
        private readonly List<HistoryHelper.StatisticEntry> statistics = new List<HistoryHelper.StatisticEntry>();
        private bool collected;
        private bool announcedOpen;

        public override string Name
        {
            get { return "history-statistics"; }
        }

        /// <summary>The stat names are named items worth searching.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Read-only access into the scope's own live search state, for the facade's HasActiveSearch.</summary>
        public bool HasActiveSearch
        {
            get { return TypeaheadHasActiveSearch; }
        }

        public HistoryStatsScope()
        {
            // Tab cycling must feel identical on all three History tabs, so this modal sub-scope
            // re-claims the dispatcher's tab-switch ids it would otherwise mask.
            Claim("history.nextTab", delegate { HistoryState.NextTab(); });
            Claim("history.previousTab", delegate { HistoryState.PreviousTab(); });
        }

        /// <summary>A single-region screen has nothing to cycle; Tab/Shift+Tab belong to History tab switching here.</summary>
        protected override bool EnableRegionCycling
        {
            get { return false; }
        }

        public override void OnPush()
        {
            base.OnPush();
            // Singleton scope reused across tab-entries. Resetting on push is safe here only because
            // Statistics has no Activate claim and so can never open a child window (unlike its
            // Messages sibling, whose reset lives in ResetForOpen).
            collected = false;
            announcedOpen = false;
            TypeaheadReset();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            if (statistics.Count == 0)
            {
                TolkHelper.SpeakData("RimWorldAccess.History.Statistics.None".Translate().ToString());
                return;
            }
            AnnounceCurrentItem();
        }

        protected override void RefreshContent()
        {
            // Snapshot semantics: collect once per OnPush, never again while this tab stays focused.
            if (collected)
            {
                return;
            }
            statistics.Clear();
            statistics.AddRange(HistoryHelper.CollectStatistics());
            collected = true;
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.History.Tab.Statistics".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return statistics.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= statistics.Count)
            {
                return d;
            }
            HistoryHelper.StatisticEntry stat = statistics[index];
            d.Label = stat.Name;
            d.Value = stat.Value;
            // No current entry sets a Tooltip; carried for future and modded stat entries.
            d.Extras = stat.Tooltip;
            d.ReadOnly = true;
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            // Rows are read-only: Enter re-announces and nothing more.
            AnnounceCurrentItem();
        }
    }

    /// <summary>
    /// The History window's Messages/Archive tab, as three regions: "Messages" (one row per
    /// <see cref="HistoryHelper.ArchiveItemWrapper"/>), "Message detail" (a header row plus one
    /// read-only row per <see cref="HistoryHelper.ArchiveItemWrapper.TooltipLines"/>, read live off
    /// the Messages cursor via <see cref="CurrentItem"/>, so nothing rebuilds on selection change),
    /// and the automatic Buttons region from <see cref="DeclaredActions"/>. Pushed above the
    /// window-attached <see cref="HistoryScope"/> dispatcher by
    /// <see cref="HistorySubTabScopeMirror"/>.
    ///
    /// SESSION-RESET TIMING (load-bearing, read before touching OnPush): this scope must NOT reset
    /// its item cache, selection, or filters in <c>OnPush</c>. Opening an item pushes a real window,
    /// which the mirror's foreign-window stand-down Pops this scope for, and the later re-Push calls
    /// OnPush again — a reset there would silently return the player to the top of the list after
    /// closing a message. The fresh-session reset lives in <see cref="ResetForOpen"/>, called only
    /// by <see cref="HistoryMessagesState.Open"/>.
    ///
    /// "In detail view" collapses to <see cref="IsDetailFocused"/>: Enter on a Messages row moves
    /// focus into the detail region, Escape returns it to the list. <see cref="OnRegionChanged"/>
    /// forces the detail cursor to its header row on every entry, since a stale index from a longer
    /// previous message could sit out of range against a shorter one.
    ///
    /// The four Alt hotkeys are claimed scope-wide with no region gate; the pin and jump ones are
    /// also Buttons-region rows through the SAME handler methods, so hotkey and button always agree.
    /// Typeahead is left at the engine default, so detail rows join the cross-region search space.
    /// </summary>
    public sealed class HistoryMessagesScope : ScreenScope
    {
        private const int MessagesRegion = 0;
        private const int DetailRegion = 1;

        private readonly List<HistoryHelper.ArchiveItemWrapper> items = new List<HistoryHelper.ArchiveItemWrapper>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>(3);
        private bool showLetters = true;
        private bool showMessages;
        private bool pendingOpenAnnouncement;

        public HistoryMessagesScope()
        {
            // Registered AFTER the base ctor's search-clear Cancel claim, so a search that is active
            // while detail-focused clears the search first.
            Claim(SharedMenuGrammar.Cancel, delegate { ReturnToList(); }, when: IsDetailFocusedGate);

            // Region-aware Tab: in the LIST region Tab/Shift+Tab switch History tabs; inside a
            // message's detail these gates go false and the base ctor's region-cycle claims (gated
            // the opposite way via EnableRegionCycling) take the same chords. Exactly one claimant
            // is live per chord at any moment.
            Claim("history.nextTab", delegate { HistoryState.NextTab(); },
                when: ListHasFocusGate);
            Claim("history.previousTab", delegate { HistoryState.PreviousTab(); },
                when: ListHasFocusGate);

            // Scope-wide (no region gate): see the class remarks.
            Claim("history.messages.toggleLettersFilter", delegate { ToggleLettersFilter(); });
            Claim("history.messages.toggleMessagesFilter", delegate { ToggleMessagesFilter(); });
            Claim("history.messages.togglePin", delegate { TogglePinCurrent(); });
            Claim("history.messages.jumpToLocation", delegate { JumpToLocationCurrent(); });
        }

        public override string Name
        {
            get { return "history-messages"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// Vanilla draws archive rows via custom ButtonInvisible rects, not ButtonText, so there is
        /// nothing to scrape — and this mirror-driven scope owns no window anyway. DeclaredActions
        /// supplies the Buttons region instead.
        /// </summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Read-only access into the scope's own live search state, for the facade's HasActiveSearch.</summary>
        public bool HasActiveSearch
        {
            get { return TypeaheadHasActiveSearch; }
        }

        /// <summary>Whether focus has left the Messages list. Exposed for <see cref="HistoryMessagesState.IsInDetailView"/>.</summary>
        public bool IsDetailFocused
        {
            get
            {
                RefreshModel();
                return Model.RegionIndex > MessagesRegion;
            }
        }

        public override bool OwnsCancel
        {
            get { return base.OwnsCancel || IsDetailFocusedGate(); }
        }

        private bool IsDetailFocusedGate()
        {
            RefreshModel();
            return Model.RegionIndex > MessagesRegion;
        }

        private bool ListHasFocusGate()
        {
            RefreshModel();
            return Model.RegionIndex == MessagesRegion;
        }

        /// <summary>The other half of the region-aware Tab grammar: cycling claims Tab only outside the list region.</summary>
        protected override bool EnableRegionCycling
        {
            get { return IsDetailFocusedGate(); }
        }

        /// <summary>
        /// The message-reader arrow flow: Down past the last detail line continues into
        /// Open/Jump/Pin and Up from the first button returns to the text. The list keeps its edge.
        /// </summary>
        protected override bool ContentFlowsToActions(int region)
        {
            return region == DetailRegion;
        }

        /// <summary>Open/Jump/Pin finish the message being read; Down reaches them, Tab does not number them.</summary>
        protected override bool ActionsRegionInTabCycle
        {
            get { return false; }
        }

        public override void OnPush()
        {
            base.OnPush();
            // Deliberately touches no items/selection/filters — see SESSION-RESET TIMING above.
            ArchivableRowRingPatch.CurrentProvider = FocusedArchivable;
        }

        public override void OnPop()
        {
            ArchivableRowRingPatch.CurrentProvider = null;
            base.OnPop();
        }

        /// <summary>
        /// The archive entry the list cursor sits on, or null. This scope owns no window, so the
        /// shared focused-rect presenter never reaches it and the ring is painted from the row
        /// method's own postfix, where the rect and the scrolled clip are both live.
        /// </summary>
        private IArchivable FocusedArchivable()
        {
            ListModel region = Model.CurrentRegion;
            int index = Model.RegionIndex == MessagesRegion && region != null ? region.Index : -1;
            return index >= 0 && index < items.Count ? items[index].Source : null;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (!pendingOpenAnnouncement)
            {
                return;
            }
            pendingOpenAnnouncement = false;
            string hotkeyHint = "RimWorldAccess.History.Messages.HotkeyHint".Translate().ToString();
            if (items.Count == 0)
            {
                TolkHelper.SpeakData("RimWorldAccess.History.Messages.NoneWithHint".Translate(hotkeyHint).ToString());
                return;
            }
            AnnounceCurrentItem();
            TolkHelper.SpeakData(hotkeyHint);
        }

        /// <summary>The fresh-session reset, called only by <see cref="HistoryMessagesState.Open"/> and never by <see cref="OnPush"/>.</summary>
        internal void ResetForOpen()
        {
            var filters = HistoryHelper.GetFilterStates();
            showLetters = filters.showLetters;
            showMessages = filters.showMessages;

            items.Clear();
            items.AddRange(HistoryHelper.CollectArchiveItems(showLetters, showMessages));

            RefreshModel();
            ListModel list = Model.Region(MessagesRegion);
            if (list != null && !list.IsEmpty)
            {
                list.MoveTo(0);
            }
            MoveResult toList = Model.MoveToRegion(MessagesRegion);
            if (toList.Changed)
            {
                OnRegionChanged(toList);
            }
            TypeaheadReset();
            pendingOpenAnnouncement = true;
        }

        /// <summary>Close-time hygiene; not load-bearing, since a popped scope is never read.</summary>
        internal void ResetForClose()
        {
            items.Clear();
            pendingOpenAnnouncement = false;
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == MessagesRegion
                ? "RimWorldAccess.History.Tab.Messages".Translate().ToString()
                : "RimWorldAccess.History.Messages.DetailRegion".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            if (region == MessagesRegion)
            {
                return items.Count;
            }
            HistoryHelper.ArchiveItemWrapper item = CurrentItem();
            return item == null ? 0 : 1 + item.TooltipLines.Length;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == MessagesRegion)
            {
                if (index < 0 || index >= items.Count)
                {
                    return d;
                }
                d.Label = items[index].BuildListAnnouncementLabel();
                return d;
            }

            HistoryHelper.ArchiveItemWrapper item = CurrentItem();
            if (item == null || index < 0)
            {
                return d;
            }
            if (index == 0)
            {
                d.Label = "RimWorldAccess.History.Messages.DetailHeader".Translate(item.TypeLabel, item.Label).ToString();
                d.ReadOnly = true;
                return d;
            }
            string[] lines = item.TooltipLines;
            int lineIndex = index - 1;
            if (lineIndex < 0 || lineIndex >= lines.Length)
            {
                return d;
            }
            d.Label = lines[lineIndex];
            d.ReadOnly = true;
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == MessagesRegion)
            {
                EnterDetail();
            }
            // Detail region: Enter on the header or a content line does nothing.
        }

        /// <summary>Forces the detail region's cursor back to its header row on every entry.</summary>
        protected override void OnRegionChanged(MoveResult result)
        {
            if (Model.RegionIndex != DetailRegion)
            {
                return;
            }
            ListModel detail = Model.Region(DetailRegion);
            if (detail != null && !detail.IsEmpty)
            {
                detail.MoveFirst();
            }
        }

        private void EnterDetail()
        {
            if (items.Count == 0)
            {
                return;
            }
            MoveResult result = Model.MoveToRegion(DetailRegion);
            if (result.Changed)
            {
                OnRegionChanged(result);
            }
            AnnounceRegion();
        }

        private void ReturnToList()
        {
            MoveResult result = Model.MoveToRegion(MessagesRegion);
            if (result.Changed)
            {
                OnRegionChanged(result);
            }
            AnnounceRegion();
        }

        /// <summary>The Messages region's selected item, or null — read live off that region's cursor whichever region has focus.</summary>
        private HistoryHelper.ArchiveItemWrapper CurrentItem()
        {
            // Reads the live model WITHOUT refreshing it first: RefreshModel asks ContentItemCount
            // for the detail region's size, which calls back here — a RefreshModel() on this path is
            // unconditional mutual recursion.
            if (Model.RegionCount <= MessagesRegion)
            {
                return null;
            }
            ListModel list = Model.Region(MessagesRegion);
            if (list == null || list.IsEmpty || list.Index < 0 || list.Index >= items.Count)
            {
                return null;
            }
            return items[list.Index];
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                HistoryHelper.ArchiveItemWrapper item = CurrentItem();
                if (item == null)
                {
                    return actions;
                }
                actions.Add(new ScreenAction(
                    "RimWorldAccess.History.Button.Open".Translate().ToString(),
                    delegate { item.Open(); }));
                if (item.HasValidTarget)
                {
                    actions.Add(new ScreenAction(
                        "RimWorldAccess.History.Button.JumpToLocation".Translate().ToString(),
                        delegate { JumpToLocationCurrent(); },
                        "history.messages.jumpToLocation"));
                }
                actions.Add(new ScreenAction(
                    (item.IsPinned ? "RimWorldAccess.History.Button.Unpin" : "RimWorldAccess.History.Button.Pin")
                        .Translate().ToString(),
                    delegate { TogglePinCurrent(); },
                    "history.messages.togglePin"));
                return actions;
            }
        }

        private void ToggleLettersFilter()
        {
            showLetters = !showLetters;
            ApplyFilterToggle(showLetters
                ? "RimWorldAccess.History.Messages.ShowingLetters"
                : "RimWorldAccess.History.Messages.HidingLetters");
        }

        private void ToggleMessagesFilter()
        {
            showMessages = !showMessages;
            ApplyFilterToggle(showMessages
                ? "RimWorldAccess.History.Messages.ShowingMessages"
                : "RimWorldAccess.History.Messages.HidingMessages");
        }

        /// <summary>
        /// The filter-toggle chain, in order: write the filter states, rebuild the items, clamp the
        /// selection, force focus back to the list, announce the filter status, re-announce the row.
        /// The detail region and the Buttons region need no rebuild step — both read live off
        /// <see cref="CurrentItem"/> whenever queried.
        /// </summary>
        private void ApplyFilterToggle(string statusKey)
        {
            HistoryHelper.SetFilterStates(showLetters, showMessages);

            RefreshModel();
            ListModel before = Model.Region(MessagesRegion);
            int oldIndex = before != null && !before.IsEmpty ? before.Index : 0;

            items.Clear();
            items.AddRange(HistoryHelper.CollectArchiveItems(showLetters, showMessages));
            RefreshModel();

            ListModel after = Model.Region(MessagesRegion);
            if (after != null && !after.IsEmpty)
            {
                int clamped = System.Math.Min(oldIndex, items.Count - 1);
                after.MoveTo(clamped);
            }

            MoveResult toList = Model.MoveToRegion(MessagesRegion);
            if (toList.Changed)
            {
                OnRegionChanged(toList);
            }

            AnnounceFilterStatus(statusKey.Translate().ToString());
            if (items.Count > 0)
            {
                AnnounceCurrentItem();
            }
        }

        private void AnnounceFilterStatus(string status)
        {
            if (items.Count == 1)
            {
                TolkHelper.Speak("RimWorldAccess.History.Messages.FilterStatusOne".Loc(status));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.History.Messages.FilterStatusMany".Loc(status, items.Count));
            }
        }

        private void TogglePinCurrent()
        {
            HistoryHelper.ArchiveItemWrapper item = CurrentItem();
            if (item == null)
            {
                return;
            }
            item.TogglePin();
            TolkHelper.Speak(item.IsPinned
                ? "RimWorldAccess.History.Pinned".Loc()
                : "RimWorldAccess.History.Unpinned".Loc());
        }

        private void JumpToLocationCurrent()
        {
            HistoryHelper.ArchiveItemWrapper item = CurrentItem();
            if (item == null)
            {
                return;
            }
            if (!item.HasValidTarget)
            {
                TolkHelper.Speak("RimWorldAccess.History.NoLocation".Loc());
                return;
            }

            // Closes the whole History window before jumping — the family's only self-initiated
            // window-close path, reached from both the hotkey and the Buttons-region row.
            HistoryState.Close();
            Window window = HistoryHelper.GetOpenHistoryWindow();
            if (window != null)
            {
                Find.WindowStack.TryRemove(window);
            }

            item.JumpTo();
        }
    }

    /// <summary>
    /// Keeps <see cref="HistoryStatsScope"/> and <see cref="HistoryMessagesScope"/> in lockstep with
    /// their states every OnGUI pass. Both are windowless overlays, so a Push lands them above the
    /// window-attached <see cref="HistoryScope"/> dispatcher, which was pushed once at the History
    /// window's Add-time. The two sub-states are mutually exclusive by construction, so this
    /// entry's position in <see cref="MirrorReconcileOrder"/> is chain-independent.
    ///
    /// FOREIGN-WINDOW STAND-DOWN (Messages only, load-bearing): opening a message pushes a real
    /// window over the History window at Add-time, while
    /// <see cref="HistoryMessagesState.IsActive"/> stays true — an unguarded re-Push would re-float
    /// this scope above that dialog the very next frame and mask it.
    /// <see cref="TextDialogShared.ForeignWindowAbove"/> detects the scopeless window and suppresses
    /// the re-Push until it closes. Statistics needs no guard: with no Activate claim it can never
    /// open a window.
    /// </summary>
    internal static class HistorySubTabScopeMirror
    {
        public static void Reconcile()
        {
            if (HistoryStatisticsState.IsActive)
            {
                FocusStack.Push(HistoryStatisticsState.Scope);
            }
            else
            {
                FocusStack.Pop(HistoryStatisticsState.Scope);
            }

            bool messagesWantsFocus = HistoryMessagesState.IsActive
                && !TextDialogShared.ForeignWindowAbove(HistoryHelper.GetOpenHistoryWindow());
            if (messagesWantsFocus)
            {
                FocusStack.Push(HistoryMessagesState.Scope);
            }
            else
            {
                FocusStack.Pop(HistoryMessagesState.Scope);
            }
        }
    }

    /// <summary>Rings the history list's focused row from vanilla's own row method, the one place the rect and the scroll clip are both live.</summary>
    [HarmonyPatch(typeof(MainTabWindow_History), "DoArchivableRow")]
    internal static class ArchivableRowRingPatch
    {
        /// <summary>Supplied by <see cref="HistoryMessagesScope"/> while it drives; null leaves the postfix a single static read.</summary>
        internal static Func<IArchivable> CurrentProvider;

        [HarmonyPostfix]
        public static void Postfix(Rect rect, IArchivable archivable)
        {
            try
            {
                Func<IArchivable> provider = CurrentProvider;
                if (provider == null || archivable == null || !ReferenceEquals(archivable, provider()))
                {
                    return;
                }
                FocusRing.Draw(rect);
                UiPointerFollow.NotifyFocusedRect(GuiSpace.VisibleScreenRect(rect));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("History row ring draw error", ex);
            }
        }
    }
}
