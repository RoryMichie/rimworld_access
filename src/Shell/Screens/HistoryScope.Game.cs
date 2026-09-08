using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the real <see cref="MainTabWindow_History"/> window (registered
    /// through <see cref="ScopeForWindow"/>) and the screen for its Graph sub-tab. The Statistics and
    /// Messages sub-tabs are <see cref="HistoryStatsScope"/>/<see cref="HistoryMessagesScope"/>, pushed
    /// above this one by <see cref="HistorySubTabScopeMirror"/> and masking it as any modal scope masks
    /// the scope beneath.
    /// <para/>
    /// Two content regions plus the window's own buttons: "Graph", one row per
    /// <see cref="HistoryAutoRecorder"/> in the selected group summarized by
    /// <see cref="HistoryGraphState.Summarize"/>; "Graph data", a header row plus one read-only row per
    /// interval for the recorder under the cursor (<see cref="HistoryGraphState.BuildDetailLines"/>),
    /// entered with Enter and left with Escape, and out of the typeahead haystack because its rows are
    /// values rather than named items; and the captured Buttons region, vanilla's five real date-range
    /// and group buttons from its own DoGraphPage.
    /// <para/>
    /// Nothing about the graph's state is cached: the range and the group are writable both from a
    /// captured vanilla button and from the memorized
    /// <c>history.graph.selectGroup</c>/<c>history.graph.cycleDateRange</c> chords, so
    /// <see cref="RefreshContent"/> re-reads the live group and section and rebuilds the detail rows
    /// whenever either moved.
    /// <para/>
    /// Tab/Shift+Tab switch History sub-tabs while the recorder list has focus and cycle regions once
    /// the cursor is inside a recorder's data: the three sub-tabs are three stacked scopes, not regions
    /// of one screen.
    /// <para/>
    /// <see cref="OwnsCancel"/> is false. <see cref="HistoryPatch"/>'s own Cancel blocker blocks only
    /// when there is a search to clear or a detail view to back out of and otherwise lets vanilla close
    /// the window; returning true here would duplicate that blocker and break the deliberate
    /// common-case close. <see cref="OwnsAccept"/> keeps the chassis default, redundant but harmless
    /// beside HistoryPatch's Accept blocker. Orphaned windows are swept by
    /// <see cref="ScopeForWindow.ReconcileLiveness"/>; the <see cref="HistoryState.IsActive"/> half is
    /// cleared by HistoryPatch's prefix on <c>Window.PostClose</c> rather than the bypassable
    /// <c>Window.Close</c>.
    /// </summary>
    public sealed class HistoryScope : ScreenScope
    {
        private const int RecordersRegion = 0;
        private const int DetailRegion = 1;

        private readonly MainTabWindow_History window;
        private readonly List<string> detailLines = new List<string>();
        private HistoryAutoRecorder detailRecorder;
        private FloatRange detailSection;

        public HistoryScope(MainTabWindow_History window)
        {
            this.window = window;

            // Escape from the data region returns to the recorder list;
            // registered after the base ctor's search-clear Cancel claim, so an
            // active search clears first.
            Claim(SharedMenuGrammar.Cancel, delegate { ReturnToList(); }, when: IsDetailFocusedGate);

            // Region-aware Tab, the HistoryMessagesScope shape — see the class remarks.
            Claim("history.nextTab", delegate { HistoryState.NextTab(); }, when: ListHasFocusGate);
            Claim("history.previousTab", delegate { HistoryState.PreviousTab(); }, when: ListHasFocusGate);

            Claim("history.graph.selectGroup", delegate { SelectGroup(); });
            Claim("history.graph.cycleDateRange", delegate { CycleDateRange(); });
        }

        public override string Name
        {
            get { return "history"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return window; }
        }

        public override bool IsLive
        {
            get { return base.IsLive && HistoryState.IsActive; }
        }

        /// <summary>
        /// Overridden to FALSE — see the class remarks. HistoryPatch's own
        /// ad hoc blocker remains the sole active Cancel-blocking mechanism.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return false; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Read-only access into the scope's own live search state, for the facade's HasActiveSearch.</summary>
        public bool HasActiveSearch
        {
            get { return TypeaheadHasActiveSearch; }
        }

        /// <summary>
        /// Exposed for <see cref="HistoryGraphState.IsInDetailView"/>: with the data rows in their
        /// own region, "in detail view" means the cursor left the recorder list.
        /// </summary>
        public bool IsDetailFocused
        {
            get { return IsDetailFocusedGate(); }
        }

        private bool IsDetailFocusedGate()
        {
            RefreshModel();
            return Model.RegionIndex > RecordersRegion;
        }

        private bool ListHasFocusGate()
        {
            RefreshModel();
            return Model.RegionIndex == RecordersRegion;
        }

        /// <summary>
        /// The other half of the region-aware Tab grammar (see the ctor's
        /// history.nextTab claims): region cycling claims Tab/Shift+Tab only
        /// while the data or Buttons region has focus — in the recorder list
        /// those chords belong to History sub-tab switching.
        /// </summary>
        protected override bool EnableRegionCycling
        {
            get { return IsDetailFocusedGate(); }
        }

        /// <summary>
        /// The data rows are readings, not named items: they stay navigable and
        /// spoken, but typing searches the recorder names (this screen's only
        /// named things) from wherever the cursor sits.
        /// </summary>
        protected override bool ContentRegionSearchable(int region)
        {
            return region == RecordersRegion;
        }

        public override void OnPush()
        {
            base.OnPush();
            HistoryGraphState.Scope = this;
        }

        public override void OnPop()
        {
            if (ReferenceEquals(HistoryGraphState.Scope, this))
            {
                HistoryGraphState.Scope = null;
            }
            base.OnPop();
        }

        /// <summary>
        /// Fresh tab entry (<see cref="HistoryGraphState.Open"/>): back to the
        /// recorder list, at the top, with no search carried over. The entry
        /// announcement itself is the chassis's, spoken on the focus this tab
        /// switch produces once the Buttons region's capture pass has landed so
        /// its region count is honest.
        /// </summary>
        internal void ResetForOpen()
        {
            RefreshModel();
            ListModel list = Model.Region(RecordersRegion);
            if (list != null && !list.IsEmpty)
            {
                list.MoveTo(0);
            }
            MoveResult toList = Model.MoveToRegion(RecordersRegion);
            if (toList.Changed)
            {
                OnRegionChanged(toList);
            }
            TypeaheadReset();
        }

        // ------------------------------------------------------------------
        // Content model.
        // ------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the data rows whenever the browsed recorder or the drawn date
        /// range moved — including by a click on one of vanilla's own captured
        /// range buttons, which this scope is never told about directly.
        /// </summary>
        protected override void RefreshContent()
        {
            HistoryAutoRecorder current = CurrentRecorder();
            FloatRange section = HistoryHelper.GetGraphSection();
            if (ReferenceEquals(detailRecorder, current)
                && section.min == detailSection.min && section.max == detailSection.max)
            {
                return;
            }
            detailRecorder = current;
            detailSection = section;
            detailLines.Clear();
            detailLines.AddRange(HistoryGraphState.BuildDetailLines(current));
        }

        /// <summary>
        /// The recorder the list cursor sits on, or null. Reads the live model
        /// WITHOUT refreshing it first (the QuestMenuScope/HistoryMessagesScope
        /// pattern): <see cref="ScreenScope.RefreshModel"/> asks
        /// <see cref="ContentItemCount"/> for the data region's size, which calls
        /// back here, so a refresh on this path would recurse forever.
        /// </summary>
        private HistoryAutoRecorder CurrentRecorder()
        {
            IReadOnlyList<HistoryAutoRecorder> recorders = HistoryGraphState.Recorders;
            if (Model.RegionCount <= RecordersRegion)
            {
                return null;
            }
            ListModel list = Model.Region(RecordersRegion);
            if (list == null || list.IsEmpty || list.Index < 0 || list.Index >= recorders.Count)
            {
                return null;
            }
            return recorders[list.Index];
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == RecordersRegion
                ? "RimWorldAccess.History.Tab.Graph".Translate().ToString()
                : "RimWorldAccess.History.Graph.DetailRegion".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            if (region == RecordersRegion)
            {
                return HistoryGraphState.Recorders.Count;
            }
            return detailRecorder == null ? 0 : 1 + detailLines.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == RecordersRegion)
            {
                IReadOnlyList<HistoryAutoRecorder> recorders = HistoryGraphState.Recorders;
                if (index < 0 || index >= recorders.Count)
                {
                    return d;
                }
                HistoryGraphState.RecorderSummary summary = HistoryGraphState.Summarize(recorders[index]);
                d.Label = recorders[index].def.LabelCap;
                d.Value = summary.Value;
                d.Extras = summary.Range;
                return d;
            }

            if (detailRecorder == null || index < 0)
            {
                return d;
            }
            d.ReadOnly = true;
            if (index == 0)
            {
                d.Label = HistoryGraphState.BuildDetailHeader(detailRecorder);
                return d;
            }
            int line = index - 1;
            if (line < detailLines.Count)
            {
                d.Label = detailLines[line];
            }
            return d;
        }

        /// <summary>Recorders are searched by name; the stat fragments the row also speaks stay out of the haystack.</summary>
        protected override string ContentRowSearchText(int region, int index)
        {
            IReadOnlyList<HistoryAutoRecorder> recorders = HistoryGraphState.Recorders;
            if (region == RecordersRegion && index >= 0 && index < recorders.Count)
            {
                return recorders[index].def.LabelCap;
            }
            return base.ContentRowSearchText(region, index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == RecordersRegion)
            {
                EnterDetail();
            }
            // Data rows have no activation of their own.
        }

        private void EnterDetail()
        {
            if (CurrentRecorder() == null)
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
            MoveResult result = Model.MoveToRegion(RecordersRegion);
            if (result.Changed)
            {
                OnRegionChanged(result);
            }
            AnnounceRegion();
        }

        /// <summary>Forces the data region's cursor back to its header row on every entry — a stale index from a longer previous recorder could sit out of range against a shorter one.</summary>
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

        // ------------------------------------------------------------------
        // The two memorized chords (the same commands the Buttons region shows).
        // ------------------------------------------------------------------

        private void SelectGroup()
        {
            HistoryGraphState.OpenGroupPicker(AnnounceGroupChanged);
        }

        private void AnnounceGroupChanged()
        {
            string groupLabel = HistoryGraphState.GroupLabel;
            RefreshModel();
            ListModel list = Model.Region(RecordersRegion);
            if (list != null && !list.IsEmpty)
            {
                list.MoveTo(0);
            }
            if (HistoryGraphState.Recorders.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.History.Graph.GroupSelectedNone".Loc(groupLabel));
                return;
            }
            TolkHelper.Speak("RimWorldAccess.History.Graph.GroupSelected".Loc(groupLabel));
            AnnounceCurrentItem();
        }

        private void CycleDateRange()
        {
            TolkHelper.Speak(HistoryGraphState.CycleDateRange());
            RefreshModel();
            AnnounceCurrentItem();
        }
    }
}
