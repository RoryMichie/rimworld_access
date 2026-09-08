using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the real <see cref="Dialog_AutoSlaughter"/> window (Tab from the
    /// Animals table), registered through <see cref="ScopeForWindow"/>. One TABLE REGION of 7
    /// columns (5 numeric limits + 2 allow-checkboxes, matching <c>Dialog_AutoSlaughter.NumColumns</c>)
    /// plus the automatic Buttons region, which captures vanilla's own Close button and carries one
    /// declared action for "return to the Animals menu". <see cref="RimWorldAccess.AutoSlaughterState"/>
    /// holds the config list, cached counts, and mutations; this scope syncs the cursor into it via
    /// <see cref="RimWorldAccess.AutoSlaughterState.SetCursor"/> before every call.
    ///
    /// NUMERIC sub-mode (<see cref="RimWorldAccess.AutoSlaughterState.IsNumericInputMode"/>):
    /// <see cref="ContentColumnCount"/> reports 0, flattening the region to a single-axis list.
    /// That is load-bearing, not cosmetic — <c>ScreenScope</c>'s Left/Right column-movement claim has
    /// no per-screen override seam, so a columnless region is the only way to keep Left/Right inert
    /// while a numeric buffer is open. <see cref="MoveItem"/>/<see cref="MoveItemEdge"/>/
    /// <see cref="MoveRegion"/> are overridden for the same reason on the other axes. The row+column
    /// the buffer applies to is captured at mode entry and restored via
    /// <see cref="RestoreTablePosition"/> on both confirm and cancel; the header-row offset is not
    /// otherwise recoverable once the column count changes.
    ///
    /// Tab is the universal region-cycle key, so the return-to-Animals action is declared with NO
    /// actionId — the announcement must not teach a Tab shortcut that no longer fires it.
    ///
    /// Escape (menus.cancel) closes BOTH the dialog and the parent Animals menu via
    /// <see cref="RimWorldAccess.AutoSlaughterState.CloseEverything"/>. The base's typeahead-active
    /// Escape claim wins whenever a search is live; numeric mode never coexists with a search.
    ///
    /// <b>Focus ring.</b> Vanilla draws each animal as one 24pt listing band and the header as two
    /// (decompiled RimWorld/Dialog_AutoSlaughter.cs:112, :117). Editable cells ring exactly:
    /// <see cref="AutoSlaughterCellPatch"/> reads each extent off the row's WidgetRow and
    /// <see cref="FocusedContentRect"/> hands the focused cell to the ring arm while
    /// <see cref="IListingRingClient.CurrentListingFocus"/> stands down, so only one ring is drawn.
    /// The header and read-only count cells have no per-cell geometry and keep the row band.
    /// </summary>
    public sealed class AutoSlaughterScope : ScreenScope, IListingRingClient
    {
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        public AutoSlaughterScope(Dialog_AutoSlaughter dialog)
        {
            Claim(SharedMenuGrammar.Cancel, delegate { HandleCancelKey(); });
            Claim(SharedMenuGrammar.SearchBackspace, delegate { AutoSlaughterState.HandleNumericBackspace(); },
                when: () => AutoSlaughterState.IsNumericInputMode);

            Claim("autoSlaughter.setToZero", delegate { MutateCurrentCell(AutoSlaughterState.SetToZero, requireNumeric: true); }, when: BrowseMode);
            Claim("autoSlaughter.setToUnlimited", delegate { MutateCurrentCell(AutoSlaughterState.SetToUnlimited, requireNumeric: true); }, when: BrowseMode);
            Claim("autoSlaughter.decreaseBy10", delegate { MutateCurrentCell(() => AutoSlaughterState.AdjustValue(-10)); }, when: BrowseMode);
            Claim("autoSlaughter.decreaseBy100", delegate { MutateCurrentCell(() => AutoSlaughterState.AdjustValue(-100)); }, when: BrowseMode);
            Claim("autoSlaughter.increaseBy10", delegate { MutateCurrentCell(() => AutoSlaughterState.AdjustValue(10)); }, when: BrowseMode);
            Claim("autoSlaughter.increaseBy100", delegate { MutateCurrentCell(() => AutoSlaughterState.AdjustValue(100)); }, when: BrowseMode);
            Claim("autoSlaughter.increment", delegate { MutateCurrentCell(() => AutoSlaughterState.AdjustValue(1)); }, when: BrowseMode);
            Claim("autoSlaughter.decrement", delegate { MutateCurrentCell(() => AutoSlaughterState.AdjustValue(-1)); }, when: BrowseMode);
            Claim("autoSlaughter.toggleCheckbox", delegate { MutateCurrentCell(AutoSlaughterState.ToggleBoolean, requireBoolean: true); }, when: BrowseMode);
        }

        public override string Name
        {
            get { return "auto-slaughter"; }
        }

        /// <summary>Tracks AutoSlaughterState.IsActive: Open() can leave IsActive false on a zero-config map, and ScopeForWindow attaches by window TYPE alone.</summary>
        public override bool IsLive
        {
            get { return AutoSlaughterState.IsActive; }
        }

        /// <summary>Windowless-equivalent modal: this scope owns Escape itself (numeric cancel, or the dialog+menu close), not just while a search is active.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Shared cross-region typeahead; numeric-mode characters are intercepted before it ever sees them (see <see cref="HandleChar"/>).</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>No header-click sort: vanilla's own fixed count-descending-then-name order stands.</summary>
        protected override bool EnableSortChord
        {
            get { return false; }
        }

        /// <summary>The grid's per-row cell buttons capture as blank ButtonText(""); the table rows already model them.</summary>
        protected override bool KeepCapturedButton(string rawLabel)
        {
            return !string.IsNullOrEmpty(rawLabel);
        }

        private static bool BrowseMode()
        {
            return !AutoSlaughterState.IsNumericInputMode;
        }

        // ScreenScope content contract.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses vanilla's own "Manage auto-slaughter..." button label — no new translation key.</summary>
        protected override string ContentRegionName(int region)
        {
            return "ManageAutoSlaughter".Translate().ToString();
        }

        /// <summary>0 while composing a numeric value (flat single-axis list); the full 7-column grid otherwise.</summary>
        protected override int ContentColumnCount(int region)
        {
            return AutoSlaughterState.IsNumericInputMode ? 0 : AutoSlaughterState.ColumnCount;
        }

        protected override int ContentItemCount(int region)
        {
            return AutoSlaughterState.ConfigCount;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < AutoSlaughterState.ConfigCount)
            {
                d.Label = AutoSlaughterState.GetConfig(index).animal.LabelCap.ToString();
            }
            return d;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            return new TableColumnInfo(AutoSlaughterState.ColumnName(column), HeaderTooltipFor(column), sortable: false);
        }

        /// <summary>
        /// Vanilla's own header hover text for this column's "max"/"allow" sub-header, from
        /// Dialog_AutoSlaughter.DoAnimalHeader (Core's Dialogs_Various.xml). Both spellings of the
        /// key prefix are genuine vanilla data, not a typo.
        /// </summary>
        private static string HeaderTooltipFor(int column)
        {
            switch ((AutoSlaughterState.Column)column)
            {
                case AutoSlaughterState.Column.MaxTotal:
                    return "AutoSlaugtherHeaderTooltipMaxTotal".Translate().ToString();
                case AutoSlaughterState.Column.MaxMales:
                    return "AutoSlaugtherHeaderTooltipMaxMales".Translate().ToString();
                case AutoSlaughterState.Column.MaxMalesYoung:
                    return "AutoSlaughterHeaderTooltipMaxMalesYoung".Translate().ToString();
                case AutoSlaughterState.Column.MaxFemales:
                    return "AutoSlaugtherHeaderTooltipMaxFemales".Translate().ToString();
                case AutoSlaughterState.Column.MaxFemalesYoung:
                    return "AutoSlaughterHeaderTooltipMaxFemalesYoung".Translate().ToString();
                case AutoSlaughterState.Column.AllowPregnant:
                    return "AutoSlaughterHeaderTooltipAllowSlaughterPregnant".Translate().ToString();
                case AutoSlaughterState.Column.AllowBonded:
                    return "AutoSlaughterHeaderTooltipAllowSlaughterBonded".Translate().ToString();
                default:
                    return null;
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (row < 0 || row >= AutoSlaughterState.ConfigCount)
                return "";
            return AutoSlaughterState.GetColumnValueString(
                AutoSlaughterState.GetConfig(row), (AutoSlaughterState.Column)column);
        }

        /// <summary>
        /// Enter on a data cell in browse mode: boolean columns toggle in place, numeric columns
        /// start numeric entry. Both sync the cursor into AutoSlaughterState first.
        /// </summary>
        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (row < 0 || row >= AutoSlaughterState.ConfigCount)
                return false;
            AutoSlaughterState.SetCursor(row, column);
            if (!AutoSlaughterState.IsNumericColumn((AutoSlaughterState.Column)column))
            {
                AutoSlaughterState.ToggleBoolean();
                AnnounceCurrentCellStateChange();
                return true;
            }
            AutoSlaughterState.EnterNumericMode(); // speaks its own prompt; nothing else to announce
            return true;
        }

        /// <summary>
        /// Only reached while numeric mode has flattened the region: confirms the typed buffer,
        /// restores the row/column it applied to, and speaks the state change.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            AutoSlaughterState.ConfirmNumericInput();
            RestoreTablePosition();
            AnnounceCurrentCellStateChange();
        }

        /// <summary>Return to the parent Animals menu — the one action beyond vanilla's Close button.</summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                // No actionId: Tab means "cycle regions" at the ScreenScope level, so speaking it
                // as this button's hotkey would be misleading.
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Animals.Menu.ReturnTitle".Translate().ToString(),
                    AutoSlaughterState.ReturnToAnimalsMenu));
                return actions;
            }
        }

        // Lifecycle.

        public override void OnPush()
        {
            base.OnPush();
            ScreenScopeDrawPatch.RegisterListingClient(this);
            announcedOpen = false;
        }

        public override void OnPop()
        {
            ScreenScopeDrawPatch.UnregisterListingClient(this);
            base.OnPop();
        }

        Window IListingRingClient.ListingRingWindow
        {
            get { return OwnedWindow; }
        }

        /// <summary>
        /// Row 0 is our header; vanilla splits its header across two bands and puts the column names
        /// in the second, which is the band this rings. Data rows ring their own
        /// <c>AutoSlaughterConfig</c> — AutoSlaughterState re-sorts vanilla's own <c>configs</c>
        /// list rather than copying it. Numeric sub-mode keeps ringing the row the buffer applies
        /// to; the Buttons region rings nothing (its Close button has its own indicator).
        /// </summary>
        ListingRingFocus IListingRingClient.CurrentListingFocus()
        {
            if (Model.RegionIndex != 0)
                return ListingRingFocus.None;
            // When the focused cell has vanilla geometry its ring is the whole indicator: two
            // rings at once would be noise.
            UnityEngine.Rect cell = FocusedCellRect();
            if (cell.width > 0f && cell.height > 0f)
                return ListingRingFocus.None;
            TableModel table = Model.CurrentTable;
            int row = table != null
                ? table.Rows.Index - 1
                : AutoSlaughterState.CurrentRowIndex;
            if (row < 0)
            {
                // A raw GetRect band carries no outer tap, so its drawn label is null and any
                // non-null tripwire passes — but the '#' token still demands one.
                return new ListingRingFocus { RowKey = AutoSlaughterRowKeys.ColumnHeader, LabelTripwire = "header" };
            }
            if (row >= AutoSlaughterState.ConfigCount)
                return ListingRingFocus.None;
            return new ListingRingFocus { RowObject = AutoSlaughterState.GetConfig(row) };
        }

        protected internal override UnityEngine.Rect FocusedContentRect()
        {
            return FocusedCellRect();
        }

        /// <summary>
        /// The focused cell's rect, or empty on the header, a read-only count cell, or a row
        /// scrolled out of view. In numeric sub-mode the cell comes from the cursor
        /// AutoSlaughterState captured at mode entry, so the ring stays on the cell being typed into.
        /// </summary>
        private UnityEngine.Rect FocusedCellRect()
        {
            if (Model.RegionIndex != 0)
                return default(UnityEngine.Rect);
            TableModel table = Model.CurrentTable;
            int row = table != null ? table.Rows.Index - 1 : AutoSlaughterState.CurrentRowIndex;
            int column = table != null ? table.ColumnIndex : AutoSlaughterState.CurrentColumnIndex;
            if (row < 0 || row >= AutoSlaughterState.ConfigCount)
                return default(UnityEngine.Rect);
            return AutoSlaughterCellPatch.CellRect(AutoSlaughterState.GetConfig(row), column);
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            if (AutoSlaughterState.ConfigCount == 0)
                return; // AutoSlaughterState.Open() already refused/announced the empty-map case.

            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            string tabCount = TabCountFragment();
            string opening = "RimWorldAccess.Animals.AutoSlaughter.Menu.OpeningTitle".Translate(AutoSlaughterState.ConfigCount).ToString();
            TolkHelper.SpeakData(string.IsNullOrEmpty(tabCount) ? opening : tabCount + ". " + opening);
            AnnounceCurrentItem();
        }

        // Escape / typeahead / numeric mode.

        private void HandleCancelKey()
        {
            if (AutoSlaughterState.IsNumericInputMode)
            {
                AutoSlaughterState.CancelNumericInput();
                RestoreTablePosition(); // silent repositioning only — CancelNumericInput's own "Cancelled" is the only thing spoken, matching the retired handler.
                return;
            }
            // The base's typeahead-active Escape claim wins while a search is live, so reaching
            // here means no search.
            AutoSlaughterState.CloseEverything();
        }

        /// <summary>
        /// Numeric-mode characters are intercepted before the shared typeahead engine sees them;
        /// browse mode defers to it unchanged.
        /// </summary>
        public override bool HandleChar(char c)
        {
            if (AutoSlaughterState.IsNumericInputMode)
            {
                if (c == '-')
                {
                    AutoSlaughterState.HandleNumericMinusSign();
                    return true;
                }
                if (char.IsDigit(c))
                {
                    AutoSlaughterState.HandleNumericDigit(c);
                    return true;
                }
                return false;
            }
            return base.HandleChar(c);
        }

        /// <summary>Up/Down: inert during numeric mode (Left/Right are blocked structurally instead — see <see cref="ContentColumnCount"/>).</summary>
        protected override void MoveItem(int delta)
        {
            if (AutoSlaughterState.IsNumericInputMode)
                return;
            base.MoveItem(delta);
        }

        /// <summary>Home/End: inert during numeric mode, same rationale as <see cref="MoveItem"/>.</summary>
        protected override void MoveItemEdge(bool first)
        {
            if (AutoSlaughterState.IsNumericInputMode)
                return;
            base.MoveItemEdge(first);
        }

        /// <summary>Tab/Shift+Tab: inert during numeric mode, same rationale as <see cref="MoveItem"/>.</summary>
        protected override void MoveRegion(bool forward)
        {
            if (AutoSlaughterState.IsNumericInputMode)
                return;
            base.MoveRegion(forward);
        }

        /// <summary>
        /// Re-lands the table cursor on the row/column the numeric buffer applied to once the region
        /// flips back to the full table; the header-row offset is not otherwise recoverable.
        /// </summary>
        private void RestoreTablePosition()
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            int dataRow = AutoSlaughterState.CurrentRowIndex;
            if (dataRow >= 0 && dataRow < AutoSlaughterState.ConfigCount)
            {
                table.Rows.MoveTo(dataRow + 1);
            }
            table.MoveToColumn(AutoSlaughterState.CurrentColumnIndex);
        }

        /// <summary>
        /// Resolves the current data row/column, syncs it into AutoSlaughterState, invokes the
        /// mutation, and speaks the state change — but only when the mutation could actually apply,
        /// so a chord aimed at the wrong column KIND stays a silent no-op rather than gaining an
        /// announcement.
        /// </summary>
        private void MutateCurrentCell(Action mutate, bool requireNumeric = false, bool requireBoolean = false)
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
                return;
            int row = table.Rows.Index - 1;
            if (row < 0 || row >= AutoSlaughterState.ConfigCount)
                return;
            var column = (AutoSlaughterState.Column)table.ColumnIndex;
            if (requireNumeric && !AutoSlaughterState.IsNumericColumn(column))
                return;
            if (requireBoolean && AutoSlaughterState.IsNumericColumn(column))
                return;
            AutoSlaughterState.SetCursor(row, table.ColumnIndex);
            mutate();
            AnnounceCurrentCellStateChange();
        }
    }

    /// <summary>
    /// Tokens the auto-slaughter header bands are identified by. Both are SYNTHETIC and can be
    /// nothing else: the bands are bare <c>GetRect</c> allocations handed to <c>DoAnimalHeader</c>,
    /// with no row call and no literal near them. Animal rows carry the <c>AutoSlaughterConfig</c>
    /// itself and need no token.
    /// </summary>
    internal static class AutoSlaughterRowKeys
    {
        /// <summary>The group-name band (Total / Male adult / …), Dialog_AutoSlaughter.cs:112, first argument.</summary>
        internal const string GroupHeader = "Dialog_AutoSlaughter.DoWindowContents#header0";

        /// <summary>The column-name band (Label / Current / Max / …), :112 second argument — what our header row stands for.</summary>
        internal const string ColumnHeader = "Dialog_AutoSlaughter.DoWindowContents#header1";
    }

    /// <summary>
    /// The three listing bands <c>DoWindowContents</c> allocates, in IL order
    /// (decompiled RimWorld/Dialog_AutoSlaughter.cs:112 twice, then :117 per config). The dialog
    /// draws no <c>Listing_Standard</c> row widgets at all — every cell is a hand-placed
    /// <c>WidgetRow</c> inside the band — so <c>GetRect</c> is the only row call to mark. Both
    /// header bands complete before <c>DoAnimalHeader</c> runs, the positional pairing the injector
    /// assumes.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_AutoSlaughter), "DoWindowContents")]
    internal static class AutoSlaughterRowMarkerPatch
    {
        private static readonly MethodInfo GetRect =
            AccessTools.Method(typeof(Listing), "GetRect", new[] { typeof(float), typeof(float) });

        private static readonly MethodInfo ConfigCurrent =
            AccessTools.Method(typeof(List<AutoSlaughterConfig>.Enumerator), "get_Current");

        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { GetRect },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = GetRect,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = AutoSlaughterRowKeys.GroupHeader,
                },
                new MarkerSite
                {
                    RowCall = GetRect,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = AutoSlaughterRowKeys.ColumnHeader,
                },
                new MarkerSite
                {
                    RowCall = GetRect,
                    Payload = MarkerPayload.LoopLocal,
                    LocalSource = ConfigCurrent,
                },
            },
        };

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }
}
