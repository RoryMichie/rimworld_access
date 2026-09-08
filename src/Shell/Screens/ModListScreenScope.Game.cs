using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the mod list (Page_ModsConfig), driving the
    /// <see cref="RimWorldAccess.ModListState"/> family. Window-attached via ShellBootstrap.
    /// RefreshContent re-derives both regions every cycle so IMGUI-fresh game state and the model
    /// never drift.
    ///
    /// <b>Mods region</b> — one Checkbox row per mod in the current column
    /// (<see cref="ModListNavigation.CurrentColumn"/>), in vanilla's own filtered/sorted order, with
    /// the Inactive column appending the WorkshopItem_Downloading placeholder rows. Left/Right
    /// switches column in either direction (<see cref="OnSwitchColumn"/>), gated to this region
    /// because a table or Buttons region claims Left/Right for its own purposes. Enter and Space both
    /// toggle the mod through <see cref="ModListActions.QuickToggle"/>, which already rides the
    /// vanilla enable/disable vehicle and announces; on a downloading row they speak a refusal rather
    /// than doing nothing. Ctrl+Up/Ctrl+Down reorder the focused mod in the active load order.
    ///
    /// <b>Details region</b> — a LIVE view of whichever mod region 0's cursor rests on, not a
    /// separate selection: a name-header row, then
    /// <see cref="ModListDetailView.BuildContentLines"/>'s lines (a requirement line with an
    /// actionable OnClicked stays a MenuItem; every other line is read-only), then
    /// <see cref="ModListActions.PopulateButtons"/>'s per-mod buttons as trailing Button rows in the
    /// same flat row model. The button rows still interact as the horizontal strip they visually are:
    /// Left/Right move between buttons only, End from anywhere in the region jumps to the first
    /// button, and each speaks its position AMONG THE BUTTONS. Up/Down walk the whole region.
    /// MEMORY TRAP: this region's content derives PURELY from region 0's settled cursor
    /// (<see cref="ResolveDetailMod"/>/<see cref="ResolveDetailDownloading"/>) inside RefreshContent,
    /// never from a side effect of DescribeContentItem, and RefreshContent never calls RefreshModel.
    ///
    /// The automatic Buttons region declares the page-global toolbar as
    /// <see cref="DeclaredActions"/>; <see cref="CaptureWindowButtons"/> is false because the page's
    /// own content also draws non-toolbar buttons (the per-mod buttons in DoModInfo and the
    /// downloading-row checkbox).
    ///
    /// <b>Selection mirroring.</b> Vanilla's detail panel follows <c>primarySelectedMod</c>;
    /// <see cref="MirrorVanillaSelectionToCursor"/> keeps it in sync with region 0's cursor after
    /// every explicit cursor move this scope drives. A typeahead jump landing in this region is the
    /// one gap — the shared engine repositions the cursor through a path this scope cannot hook — so
    /// the vanilla highlight catches up on the next arrow move or region switch.
    ///
    /// <b>Modality.</b> <see cref="IsModal"/> stands down while a foreign, scopeless real window sits
    /// above the page (<see cref="TextDialogShared.ForeignWindowAbove"/>): a confirmation dialog gets
    /// its own scope and masks this through ordinary stack modality anyway, but a bare ImmediateWindow
    /// (drag-preview) or tooltip must never count. <see cref="OwnsGameInput"/> stays unconditionally
    /// true while live. The window-pass twin guard in ModListPatch.cs keys off
    /// <see cref="RimWorldAccess.ModListState.IsActive"/>, which this scope's lifecycle drives.
    /// </summary>
    public sealed class ModListScreenScope : ScreenScope
    {
        private const int ModsRegion = 0;
        private const int DetailsRegion = 1;

        private readonly Page_ModsConfig page;
        private readonly List<ModMetaData> currentModRows = new List<ModMetaData>();
        private readonly List<WorkshopItem_Downloading> currentDownloadingRows = new List<WorkshopItem_Downloading>();
        private readonly List<ButtonInfo> detailButtons = new List<ButtonInfo>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private ModMetaData lastMirroredMod;
        private bool announcedOpen;

        public ModListScreenScope(Page_ModsConfig page)
        {
            this.page = page;

            // The base's own NextHorizontal/PreviousHorizontal claims are gated on a condition never
            // true for a plain Mods row, so this later claim is the one that runs.
            Claim(SharedMenuGrammar.NextHorizontal, e => OnSwitchColumn(), when: ModsRegionLive);
            Claim(SharedMenuGrammar.PreviousHorizontal, e => OnSwitchColumn(), when: ModsRegionLive);

            // Load-order reorder, Mods region only. The base's own claim on these ids is gated on
            // CanReorderContentItem, left at false here, so this later claim runs instead.
            Claim(SharedMenuGrammar.ReorderUp, e => ReorderUp(), when: ModsRegionLive);
            Claim(SharedMenuGrammar.ReorderDown, e => ReorderDown(), when: ModsRegionLive);

            Claim("modList.autoSort", e => ModListActions.AutoSortMods());
            Claim("modList.saveChanges", e => ModListActions.SaveChanges());
            Claim("modList.getMods", e => ModListActions.OpenGetModsMenu());
            Claim("modList.unsubscribeMultiple", e => ModListActions.OpenUnsubscribeMultipleMenu());
            Claim("modList.saveLoadList", e => ModListActions.OpenSaveLoadListMenu());

            // Consume-only: swallows '*' in the Mods region to prevent passthrough.
            Claim("modList.blockStar", e => { }, when: ModsRegionLive);
        }

        public override string Name
        {
            get { return "mod-list"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected internal override Window OwnedWindow
        {
            get { return page; }
        }

        /// <summary>The page's own content draws non-toolbar buttons (per-mod Enable/Disable/MoreActions, the downloading-row checkbox) — declare the toolbar instead.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>
        /// State-based: a confirmation dialog above this page masks it through ordinary stack
        /// modality anyway, but a bare ImmediateWindow (drag-preview) or tooltip must never count.
        /// </summary>
        public override bool IsModal
        {
            get { return !TextDialogShared.ForeignWindowAbove(page); }
        }

        /// <summary>Unconditionally true while live — no foreign-window condition.</summary>
        public override bool OwnsGameInput
        {
            get { return true; }
        }

        // ------------------------------------------------------------------
        // Content model.
        // ------------------------------------------------------------------

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            for (int i = 0; i < currentModRows.Count; i++)
            {
                ModRowRingPatch.RowGeometry.AddCandidate(currentModRows[i], ModsRegion, i, candidates, targets);
            }
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            if (region == ModsRegion)
            {
                return ModListNavigation.CurrentColumn == ModListColumn.Active
                    ? "Enabled".Translate().ToString()
                    : "Disabled".Translate().ToString();
            }
            return "RimWorldAccess.ModList.DetailsRegion".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return region == ModsRegion
                ? currentModRows.Count + currentDownloadingRows.Count
                : DetailRowCount();
        }

        /// <summary>
        /// Rebuilds the Mods region off the live column and the Details region off whichever mod
        /// region 0's cursor rests on. The resolvers read region 0's OWN cursor directly, never
        /// ModListNavigation's selected-row field, which other methods here write and would race.
        /// </summary>
        protected override void RefreshContent()
        {
            currentModRows.Clear();
            List<ModMetaData> list = ModListNavigation.GetCurrentList();
            if (list != null)
            {
                currentModRows.AddRange(list);
            }

            currentDownloadingRows.Clear();
            if (ModListNavigation.CurrentColumn == ModListColumn.Inactive)
            {
                currentDownloadingRows.AddRange(ModListVanillaBridge.GetDownloadingItems());
            }

            ModMetaData detailMod = ResolveDetailMod();
            WorkshopItem_Downloading detailDownloading = detailMod == null ? ResolveDetailDownloading() : null;
            ModListDetailView.BuildContentLines(detailMod, detailDownloading);
            detailButtons.Clear();
            ModListActions.PopulateButtons(detailMod, detailDownloading, detailButtons, ReturnFocusToModsRegion);
        }

        /// <summary>The mod at region 0's OWN settled cursor (from the end of the last completed cycle), or null on a downloading row/empty region.</summary>
        private ModMetaData ResolveDetailMod()
        {
            ListModel modsRegion = Model.RegionCount > 0 ? Model.Region(ModsRegion) : null;
            if (modsRegion == null || modsRegion.IsEmpty)
            {
                return currentModRows.Count > 0 ? currentModRows[0] : null;
            }
            int row = modsRegion.Index;
            return row >= 0 && row < currentModRows.Count ? currentModRows[row] : null;
        }

        private WorkshopItem_Downloading ResolveDetailDownloading()
        {
            ListModel modsRegion = Model.RegionCount > 0 ? Model.Region(ModsRegion) : null;
            if (modsRegion == null || modsRegion.IsEmpty)
            {
                return null;
            }
            int row = modsRegion.Index;
            int index = row - currentModRows.Count;
            return index >= 0 && index < currentDownloadingRows.Count ? currentDownloadingRows[index] : null;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return region == ModsRegion ? DescribeModRow(index) : DescribeDetailRow(index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == ModsRegion)
            {
                ActivateModRow(index);
            }
            else
            {
                ActivateDetailRow(index);
            }
        }

        /// <summary>
        /// The mod under the keyboard cursor, for <see cref="ModRowRingPatch"/>, or null
        /// while the cursor is outside the Mods region or on a downloading row. Strict
        /// where <see cref="ResolveDetailMod"/> falls back to the first row: a ring must
        /// never point at a row the cursor is not on.
        /// </summary>
        internal ModMetaData FocusedMod
        {
            get
            {
                if (Model.RegionIndex != ModsRegion)
                    return null;
                ListModel modsRegion = Model.RegionCount > 0 ? Model.Region(ModsRegion) : null;
                int row = modsRegion == null || modsRegion.IsEmpty ? -1 : modsRegion.Index;
                return row >= 0 && row < currentModRows.Count ? currentModRows[row] : null;
            }
        }

        /// <summary>The vanilla list our Mods region is showing, so a ring never crosses to the other column.</summary>
        internal List<ModMetaData> FocusedList
        {
            get { return ModListNavigation.GetCurrentList(); }
        }

        private bool ModsRegionLive()
        {
            RefreshModel();
            return Model.RegionIndex == ModsRegion;
        }

        // ------------------------------------------------------------------
        // Mods region.
        // ------------------------------------------------------------------

        private ElementDescription DescribeModRow(int row)
        {
            int total = currentModRows.Count + currentDownloadingRows.Count;
            if (row < currentModRows.Count)
            {
                return ModListAnnouncements.BuildModDescription(currentModRows[row], row + 1, total);
            }
            int downloadingIndex = row - currentModRows.Count;
            if (downloadingIndex >= 0 && downloadingIndex < currentDownloadingRows.Count)
            {
                return ModListAnnouncements.BuildDownloadingDescription(row + 1, total);
            }
            return new ElementDescription();
        }

        /// <summary>Enter/Space on a Mods row: toggles a mod via the existing QuickToggle vehicle; a downloading row speaks a refusal instead of silently doing nothing.</summary>
        private void ActivateModRow(int row)
        {
            if (row < currentModRows.Count)
            {
                ModListNavigation.SetSelectedIndex(row);
                ModListActions.QuickToggle();
                // QuickToggle already announced and may have reindexed within the column; resync the
                // cursor without a second announcement.
                RefreshModel();
                ListModel modsRegion = Model.Region(ModsRegion);
                if (modsRegion != null && !modsRegion.IsEmpty)
                {
                    modsRegion.MoveTo(Math.Min(Math.Max(ModListNavigation.SelectedIndex, 0), modsRegion.Count - 1));
                }
                return;
            }
            int downloadingIndex = row - currentModRows.Count;
            if (downloadingIndex >= 0 && downloadingIndex < currentDownloadingRows.Count)
            {
                TolkHelper.Speak("RimWorldAccess.ModList.CannotToggleDownloading".Loc());
            }
        }

        /// <summary>Left/Right on a Mods row: flips the active column (ModListNavigation.SwitchColumn already speaks its own announcement) and resets this region's cursor to its first row.</summary>
        private void OnSwitchColumn()
        {
            ModListNavigation.SwitchColumn();
            TypeaheadReset();
            RefreshModel();
            ListModel modsRegion = Model.Region(ModsRegion);
            if (modsRegion != null && !modsRegion.IsEmpty)
            {
                modsRegion.MoveTo(0);
            }
            MirrorVanillaSelectionToCursor();
        }

        private void ReorderUp()
        {
            RefreshModel();
            ListModel modsRegion = Model.Region(ModsRegion);
            if (modsRegion == null || modsRegion.IsEmpty) return;
            ModListNavigation.SetSelectedIndex(modsRegion.Index);
            ModListActions.MoveUp();
            RefreshModel();
            modsRegion = Model.Region(ModsRegion);
            if (modsRegion != null && !modsRegion.IsEmpty)
            {
                modsRegion.MoveTo(Math.Min(Math.Max(ModListNavigation.SelectedIndex, 0), modsRegion.Count - 1));
            }
        }

        private void ReorderDown()
        {
            RefreshModel();
            ListModel modsRegion = Model.Region(ModsRegion);
            if (modsRegion == null || modsRegion.IsEmpty) return;
            ModListNavigation.SetSelectedIndex(modsRegion.Index);
            ModListActions.MoveDown();
            RefreshModel();
            modsRegion = Model.Region(ModsRegion);
            if (modsRegion != null && !modsRegion.IsEmpty)
            {
                modsRegion.MoveTo(Math.Min(Math.Max(ModListNavigation.SelectedIndex, 0), modsRegion.Count - 1));
            }
        }

        // ------------------------------------------------------------------
        // Details region.
        // ------------------------------------------------------------------

        private int DetailRowCount()
        {
            return 1 + ModListDetailView.ContentLineCount + detailButtons.Count;
        }

        private ElementDescription DescribeDetailRow(int row)
        {
            var d = new ElementDescription();
            if (row == 0)
            {
                ModMetaData mod = ResolveDetailMod();
                if (mod != null)
                {
                    d.Label = mod.Name;
                }
                else if (ResolveDetailDownloading() != null)
                {
                    d.Label = "Downloading".Translate().ToString();
                }
                d.Role = ElementRole.None;
                d.ReadOnly = true;
                return d;
            }

            int lineIndex = row - 1;
            int lineCount = ModListDetailView.ContentLineCount;
            if (lineIndex < lineCount)
            {
                d.Label = ModListDetailView.ContentLine(lineIndex);
                if (ModListDetailView.RequirementAt(lineIndex) != null)
                {
                    d.Role = ElementRole.MenuItem;
                }
                else
                {
                    d.Role = ElementRole.None;
                    d.ReadOnly = true;
                }
                return d;
            }

            int buttonIndex = lineIndex - lineCount;
            if (buttonIndex >= 0 && buttonIndex < detailButtons.Count)
            {
                ButtonInfo button = detailButtons[buttonIndex];
                d.Label = button.Label;
                d.Role = ElementRole.Button;
                d.Disabled = button.IsDisabled;
                // Position among the BUTTONS, not the flat region: the base fills PositionIndex/
                // PositionCount only when the describer leaves them null.
                d.PositionIndex = buttonIndex + 1;
                d.PositionCount = detailButtons.Count;
                if (button.IsDisabled && !string.IsNullOrEmpty(button.DisabledReason))
                {
                    d.Extras = button.DisabledReason;
                }
            }
            return d;
        }

        /// <summary>Whether Details row <paramref name="row"/> is a trailing button, and if so its index among <see cref="detailButtons"/> — the seam the button-strip Left/Right/End behavior below shares.</summary>
        private bool TryGetDetailButtonIndex(int row, out int buttonIndex)
        {
            buttonIndex = row - 1 - ModListDetailView.ContentLineCount;
            return buttonIndex >= 0 && buttonIndex < detailButtons.Count;
        }

        /// <summary>
        /// Gate for the shared Left/Right claims: true only for a Details button row, so the Mods
        /// region's own column-switch claim stays disjoint. The base's HorizontalClaimable predicate
        /// calls this first and simply never matches a Mods row or a non-button Details row.
        /// </summary>
        protected override bool CanAdjustContentItem(int region, int index)
        {
            int buttonIndex;
            return region == DetailsRegion && TryGetDetailButtonIndex(index, out buttonIndex);
        }

        /// <summary>Left/Right between Details buttons, clamped at the strip's edges; an edge press re-announces the row already there.</summary>
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            int buttonIndex;
            if (region != DetailsRegion || !TryGetDetailButtonIndex(index, out buttonIndex))
                return;
            ListModel detailsRegionModel = Model.Region(DetailsRegion);
            if (detailsRegionModel == null || detailsRegionModel.IsEmpty)
                return;
            int target = Math.Min(Math.Max(buttonIndex + direction, 0), detailButtons.Count - 1);
            int targetRow = 1 + ModListDetailView.ContentLineCount + target;
            detailsRegionModel.MoveTo(targetRow);
            NotifyCursorSettled();
            AnnounceCurrentItem();
            MirrorVanillaSelectionToCursor();
        }

        /// <summary>Enter on a Details row: re-announces the header/a plain line, runs a requirement's OnClicked, or activates a trailing button.</summary>
        private void ActivateDetailRow(int row)
        {
            if (row == 0)
            {
                AnnounceCurrentItem();
                return;
            }

            int lineIndex = row - 1;
            int lineCount = ModListDetailView.ContentLineCount;
            if (lineIndex < lineCount)
            {
                ModRequirement req = ModListDetailView.RequirementAt(lineIndex);
                if (req == null)
                {
                    AnnounceCurrentItem();
                    return;
                }
                ModMetaData selectedMod;
                ModListDetailView.ActivateRequirement(req, out selectedMod);
                if (selectedMod != null)
                {
                    ReturnFocusToModsRegion();
                }
                return;
            }

            int buttonIndex = lineIndex - lineCount;
            if (buttonIndex < 0 || buttonIndex >= detailButtons.Count) return;
            ButtonInfo button = detailButtons[buttonIndex];
            if (button.IsDisabled)
            {
                string refusal = "RimWorldAccess.Shell.GenericWindow.Disabled".Loc(button.Label).ToString();
                if (!string.IsNullOrEmpty(button.DisabledReason))
                {
                    refusal = refusal + " " + button.DisabledReason;
                }
                TolkHelper.SpeakData(refusal);
                return;
            }
            try
            {
                button.Action?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Warning("[RimWorld Access] Failed to activate mod button: " + ex.Message);
                TolkHelper.Speak("RimWorldAccess.ModList.FailedToActivateButton".Loc());
            }
        }

        /// <summary>
        /// The "return to list" tail of a Details action: moves the cursor to the Mods region at
        /// ModListNavigation's current row and announces the switch. An empty Mods region is handled
        /// by the base's ReconcileEmptyRegion on the next refresh.
        /// </summary>
        private void ReturnFocusToModsRegion()
        {
            RefreshModel();
            ListModel modsRegion = Model.Region(ModsRegion);
            if (modsRegion == null || modsRegion.IsEmpty) return;
            int target = Math.Min(Math.Max(ModListNavigation.SelectedIndex, 0), modsRegion.Count - 1);
            MoveResult result = Model.MoveToRegion(ModsRegion);
            if (result.Changed)
            {
                OnRegionChanged(result);
            }
            modsRegion.MoveTo(target);
            AnnounceRegion();
        }

        // ------------------------------------------------------------------
        // Buttons region (page-global toolbar).
        // ------------------------------------------------------------------

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("ResolveModOrder".Translate(), ModListActions.AutoSortMods, "modList.autoSort"));
                actions.Add(new ScreenAction("SaveModChanges".Translate(), ModListActions.SaveChanges, "modList.saveChanges"));
                actions.Add(new ScreenAction("GetMods".Translate(), ModListActions.OpenGetModsMenu, "modList.getMods"));
                actions.Add(new ScreenAction("UnsubscribeMultiple".Translate(), ModListActions.OpenUnsubscribeMultipleMenu, "modList.unsubscribeMultiple"));
                actions.Add(new ScreenAction("SaveLoadList".Translate(), ModListActions.OpenSaveLoadListMenu, "modList.saveLoadList"));
                return actions;
            }
        }

        // ------------------------------------------------------------------
        // Selection mirroring.
        // ------------------------------------------------------------------

        /// <summary>Keeps vanilla's own primarySelectedMod (and its DoModInfo panel) in sync with region 0's cursor.</summary>
        private void MirrorVanillaSelectionToCursor()
        {
            if (Model.RegionIndex != ModsRegion) return;
            ListModel modsRegion = Model.Region(ModsRegion);
            if (modsRegion == null || modsRegion.IsEmpty) return;
            int row = modsRegion.Index;
            if (row < 0 || row >= currentModRows.Count) return;
            ModMetaData mod = currentModRows[row];
            if (ReferenceEquals(mod, lastMirroredMod)) return;
            lastMirroredMod = mod;
            ModListVanillaBridge.SelectMod(page, mod);
        }

        protected override void MoveItem(int delta)
        {
            base.MoveItem(delta);
            MirrorVanillaSelectionToCursor();
        }

        /// <summary>
        /// Home/End, plus the button strip's End-from-anywhere landing: End in the Details region is
        /// an absolute jump to the first button when the mod has any, so it works from the header row
        /// or any content line. Home is untouched. An active typeahead search keeps owning End for
        /// its own match jump, checked through <see cref="TypeaheadHasActiveSearch"/> — the only
        /// Typeahead seam a subclass can reach.
        /// </summary>
        protected override void MoveItemEdge(bool first)
        {
            if (!first && !TypeaheadHasActiveSearch)
            {
                RefreshModel();
                if (Model.RegionIndex == DetailsRegion && detailButtons.Count > 0)
                {
                    ListModel detailsRegionModel = Model.Region(DetailsRegion);
                    int firstButtonRow = 1 + ModListDetailView.ContentLineCount;
                    if (detailsRegionModel != null && !detailsRegionModel.IsEmpty && firstButtonRow < detailsRegionModel.Count)
                    {
                        detailsRegionModel.MoveTo(firstButtonRow);
                        NotifyCursorSettled();
                        AnnounceCurrentItem();
                        MirrorVanillaSelectionToCursor();
                        return;
                    }
                }
            }
            base.MoveItemEdge(first);
            MirrorVanillaSelectionToCursor();
        }

        protected override void OnRegionChanged(MoveResult result)
        {
            MirrorVanillaSelectionToCursor();
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            lastMirroredMod = null;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen) return;
            announcedOpen = true;
            string tabCount = TabCountFragment();
            string title = "Mods".Translate().ToString();
            string columnName = ModListNavigation.CurrentColumn == ModListColumn.Active
                ? "Enabled".Translate().ToString()
                : "Disabled".Translate().ToString();
            string columnHeader = "RimWorldAccess.ModList.ColumnHeader"
                .Translate(columnName, ContentItemCount(ModsRegion), ModListAnnouncements.ColumnPositionText()).ToString();
            string opening = string.IsNullOrEmpty(tabCount) ? title : tabCount + ". " + title;
            TolkHelper.SpeakData(opening + ". " + columnHeader);
            AnnounceCurrentItem();
        }
    }

    /// <summary>
    /// Paints the shared focus ring on vanilla's own mod row, reusing the rect
    /// <c>Page_ModsConfig.DoModRow</c> was handed — the same rect its <c>DrawHighlightSelected</c>
    /// paints.
    /// Identity is the <see cref="ModMetaData"/> plus the list it was handed with: the page draws
    /// both columns through this one method, so matching the list by reference keeps the ring in the
    /// column the cursor is in. Dragged rows are skipped because <c>DoDraggedMods</c> repaints the
    /// same mod through the same method and the ring would double.
    /// The enclosing <c>DoModList</c> culls rows outside the visible band, so the ring disappears
    /// while the focused row is scrolled past the fold; no scroll-follow is available here.
    /// </summary>
    [HarmonyPatch(typeof(Page_ModsConfig), "DoModRow")]
    internal static class ModRowRingPatch
    {
        /// <summary>Every visible row's own rect, so Alt+Shift+J can route to the mod the mouse is on.</summary>
        internal static readonly RowGeometryCache RowGeometry = new RowGeometryCache();

        [HarmonyPostfix]
        public static void Postfix(Rect r, ModMetaData mod, List<ModMetaData> list, bool isDragged)
        {
            try
            {
                if (isDragged || Event.current.type != EventType.Repaint)
                {
                    return;
                }
                ModListScreenScope scope = FocusStackLookup.TopmostOfType<ModListScreenScope>();
                if (scope == null || mod == null || !ReferenceEquals(list, scope.FocusedList))
                {
                    return;
                }
                RowGeometry.Record(mod, r);
                if (mod != scope.FocusedMod)
                {
                    return;
                }
                FocusRing.Draw(r.ContractedBy(1f));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Mod row ring error", ex);
            }
        }
    }
}
