using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Multi-selection of map pawns. Find.Selector is the source of truth for the selected set;
    /// this class owns only the navigation cursor (which in multi-select mode moves independently
    /// of the selection) and the mode semantics around toggling, group recall, contiguous selection
    /// and announcements. Reading the selection live means stale state cannot survive a save load,
    /// but focusedPawn is static, so it is validated on access and cleared in <see cref="Reset"/>.
    /// </summary>
    public static class MultiSelectState
    {
        private static Pawn focusedPawn = null;

        /// <summary>
        /// The anchor for contiguous range selection; the range is
        /// [min(anchor, focus), max(anchor, focus)]. Moving focus away from the anchor extends the
        /// range, moving toward it deselects the pawn being left behind. Cleared whenever the
        /// selection context changes.
        /// </summary>
        private static Pawn rangeAnchorPawn = null;

        /// <summary>
        /// True once the user has explicitly entered multi-select and not yet left it. Distinct from
        /// <see cref="IsMultiSelectActive"/>, which counts selected pawns: a user can be in
        /// multi-select mode with one pawn, and Alt+Space on that lone pawn removes it and exits
        /// rather than merely deselecting.
        /// </summary>
        private static bool inMultiSelectMode = false;

        /// <summary>Whether more than one pawn is selected in Find.Selector.</summary>
        public static bool IsMultiSelectActive
            => (Find.Selector?.SelectedPawns?.Count ?? 0) > 1;

        /// <summary>
        /// Whether the user is in multi-select MODE: more than one pawn selected, or an explicit
        /// entry with a non-empty selection. Decides whether Alt+Space toggles membership or starts
        /// multi-select; use <see cref="IsMultiSelectActive"/> to decide whether an action applies
        /// to several pawns at once.
        /// </summary>
        public static bool IsMultiSelectMode
        {
            get
            {
                int count = SelectedCount;
                if (count > 1)
                    return true;
                if (count == 0)
                    return false;
                return inMultiSelectMode;
            }
        }

        /// <summary>
        /// The focused pawn, which in multi-select mode moves without changing the selection. Null
        /// once the stored focus is destroyed, dead, despawned, or on another map.
        /// </summary>
        public static Pawn FocusedPawn
        {
            get
            {
                if (focusedPawn == null)
                    return null;
                if (!IsPawnValid(focusedPawn))
                {
                    focusedPawn = null;
                    return null;
                }
                return focusedPawn;
            }
        }

        /// <summary>How many pawns are selected.</summary>
        public static int SelectedCount
            => Find.Selector?.SelectedPawns?.Count ?? 0;

        /// <summary>A fresh list of the selected pawns; Find.Selector.SelectedPawns reuses a shared static buffer.</summary>
        public static IReadOnlyCollection<Pawn> SelectedPawns
        {
            get
            {
                var sel = Find.Selector?.SelectedPawns;
                return sel == null
                    ? (IReadOnlyCollection<Pawn>)System.Array.Empty<Pawn>()
                    : sel.ToList();
            }
        }

        /// <summary>Whether a pawn is in the game's current selection.</summary>
        public static bool IsPawnSelected(Pawn pawn)
        {
            return pawn != null && Find.Selector != null && Find.Selector.IsSelected(pawn);
        }

        private static bool IsPawnValid(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                return false;
            Map currentMap = Find.CurrentMap;
            return currentMap == null || pawn.Map == currentMap;
        }

        /// <summary>
        /// Toggles a pawn in or out of the multi-selection. Outside multi-select mode this ENTERS
        /// the mode with the focused pawn as its first item, adding it if it was not selected;
        /// inside the mode it toggles membership, and removing the last pawn exits.
        /// </summary>
        /// <summary>
        /// Whether a map targeting session is active, in which case this announces a polite refusal
        /// and the caller must abandon a Selector-modifying operation. Reads
        /// ExternalMapTargeting.MapTargetingActive directly so a stale belief cannot strand the
        /// user. Changing the Selector mid-cast triggers vanilla's ConfirmStillValid logic, emits
        /// stray gizmo-broadcast messages, and can kill the cast outright.
        /// </summary>
        private static bool BlockedByActiveTargeting(string actionVerbKey)
        {
            if (!ExternalMapTargeting.MapTargetingActive)
                return false;
            TolkHelper.Speak(
                "RimWorldAccess.Pawns.MultiSelect.BlockedByTargeting".Loc((string)actionVerbKey.Translate()),
                SpeechPriority.High);
            return true;
        }

        public static void TogglePawn(Pawn pawn)
        {
            if (pawn == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.MultiSelect.NoFocus".Loc());
                return;
            }

            if (BlockedByActiveTargeting("RimWorldAccess.Pawns.MultiSelect.ActionChangeSelection"))
                return;

            ValidateAndCleanupSelection();

            var selector = Find.Selector;
            if (selector == null)
                return;

            // Outside the mode: enter it with this pawn, deselecting nothing.
            if (!IsMultiSelectMode)
            {
                bool alreadySelected = selector.IsSelected(pawn);
                if (!alreadySelected)
                    selector.Select(pawn, forceDesignatorDeselect: false);

                focusedPawn = pawn;
                inMultiSelectMode = true;
                rangeAnchorPawn = null;
                GizmoNavigationState.PawnJustSelected = true;
                ColonistBarState.SyncBarPosition(pawn);

                int count = selector.SelectedPawns.Count;
                TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.AddedStarted".Translate(pawn.LabelShort, count.ToString()).ToString());
                return;
            }

            if (selector.IsSelected(pawn))
            {
                selector.Deselect(pawn);
                int remaining = selector.SelectedPawns.Count;

                if (remaining == 0)
                {
                    inMultiSelectMode = false;
                    focusedPawn = null;
                    rangeAnchorPawn = null;
                    TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.RemovedDisabled".Translate(pawn.LabelShort).ToString());
                }
                else if (remaining == 1)
                {
                    // Back to one pawn, so the session is over. Without this the mode flag keeps
                    // IsMultiSelectMode true and the pawn-cycling hotkeys take the focus-only
                    // branch instead of jumping to the pawn.
                    Pawn lone = selector.SelectedPawns.First();
                    inMultiSelectMode = false;
                    focusedPawn = lone;
                    rangeAnchorPawn = null;
                    GizmoNavigationState.PawnJustSelected = true;
                    TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.RemovedDisabledThenSingle".Translate(pawn.LabelShort, lone.LabelShort).ToString());
                }
                else
                {
                    // Focus stays on the toggled pawn: warping it to the first-selected
                    // would make every following press eat the selection from the front.
                    GizmoNavigationState.PawnJustSelected = true;
                    TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.Removed".Translate(pawn.LabelShort, remaining.ToString()).ToString());
                }
            }
            else
            {
                selector.Select(pawn, forceDesignatorDeselect: false);
                focusedPawn = pawn;
                GizmoNavigationState.PawnJustSelected = true;
                int count = selector.SelectedPawns.Count;
                TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.Added".Translate(pawn.LabelShort, count.ToString()).ToString());
            }
        }

        /// <summary>Adds a pawn to the selection without toggling; used by contiguous selection.</summary>
        public static void AddPawn(Pawn pawn)
        {
            if (pawn == null)
                return;
            var selector = Find.Selector;
            if (selector == null || selector.IsSelected(pawn))
                return;

            selector.Select(pawn, forceDesignatorDeselect: false);
            GizmoNavigationState.PawnJustSelected = true;
        }

        /// <summary>
        /// Extends or shrinks the contiguous range rightward, Explorer-style: the range is anchored
        /// where the Shift selection began, so moving right extends on the anchor's right side and
        /// shrinks on its left.
        /// </summary>
        public static void SelectContiguousNext()
        {
            if (Find.CurrentMap == null)
                return;

            if (BlockedByActiveTargeting("RimWorldAccess.Pawns.MultiSelect.ActionExtendSelection"))
                return;

            ValidateAndCleanupSelection();

            var selector = Find.Selector;
            if (selector == null)
                return;

            if (!EnsureRangeAnchor(selector))
                return;

            ExtendRange(selector, moveRight: true);
        }

        /// <summary>Extends or shrinks the contiguous range leftward; see <see cref="SelectContiguousNext"/> for the anchor semantics.</summary>
        public static void SelectContiguousPrevious()
        {
            if (Find.CurrentMap == null)
                return;

            if (BlockedByActiveTargeting("RimWorldAccess.Pawns.MultiSelect.ActionExtendSelection"))
                return;

            ValidateAndCleanupSelection();

            var selector = Find.Selector;
            if (selector == null)
                return;

            if (!EnsureRangeAnchor(selector))
                return;

            ExtendRange(selector, moveRight: false);
        }

        /// <summary>
        /// Establishes or validates the range anchor, re-anchoring to the single-selected pawn or
        /// the focused pawn when the stored anchor has left the selection. Returns whether an
        /// anchor is in place and the caller may proceed.
        /// </summary>
        private static bool EnsureRangeAnchor(Selector selector)
        {
            if (rangeAnchorPawn != null &&
                IsPawnValid(rangeAnchorPawn) &&
                selector.IsSelected(rangeAnchorPawn))
            {
                return true;
            }

            Pawn anchor = selector.SingleSelectedThing as Pawn;
            if (anchor == null || !IsPawnValid(anchor))
                anchor = IsPawnValid(focusedPawn) ? focusedPawn : null;

            if (anchor == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoPawnSelected".Loc());
                rangeAnchorPawn = null;
                return false;
            }

            if (!selector.IsSelected(anchor))
                selector.Select(anchor, forceDesignatorDeselect: false);

            focusedPawn = anchor;
            rangeAnchorPawn = anchor;
            inMultiSelectMode = true;
            ColonistBarState.SyncBarPosition(anchor);
            return true;
        }

        /// <summary>
        /// Moves focus one step and adjusts the selection: extending away from the anchor, and
        /// deselecting the pawn left behind when moving toward it.
        /// </summary>
        private static void ExtendRange(Selector selector, bool moveRight)
        {
            Pawn oldFocus = focusedPawn;
            int oldIdx = ColonistBarState.GetGlobalBarIndex(oldFocus);
            int anchorIdx = ColonistBarState.GetGlobalBarIndex(rangeAnchorPawn);

            Pawn newFocus = moveRight
                ? ColonistBarState.NavigateFocusRight()
                : ColonistBarState.NavigateFocusLeft();

            if (newFocus == null || newFocus == oldFocus)
                return;

            focusedPawn = newFocus;

            bool haveIndices = oldIdx >= 0 && anchorIdx >= 0;
            bool shrinking = haveIndices && oldFocus != null &&
                (moveRight ? oldIdx < anchorIdx : oldIdx > anchorIdx);

            if (shrinking)
            {
                if (selector.IsSelected(oldFocus))
                    selector.Deselect(oldFocus);
                GizmoNavigationState.PawnJustSelected = true;
                int count = selector.SelectedPawns.Count;

                // Back to one pawn, so the session is over; see TogglePawn's remaining==1 branch.
                if (count == 1)
                {
                    inMultiSelectMode = false;
                    rangeAnchorPawn = null;
                    TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.RemovedDisabledThenSingle".Translate(oldFocus.LabelShort, newFocus.LabelShort).ToString());
                }
                else
                {
                    TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.RemovedFocusChanged".Translate(oldFocus.LabelShort, newFocus.LabelShort, count.ToString()).ToString());
                }
            }
            else
            {
                bool alreadySelected = selector.IsSelected(newFocus);
                AddPawn(newFocus);
                int count = selector.SelectedPawns.Count;

                if (!alreadySelected)
                    TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.Added".Translate(newFocus.LabelShort, count.ToString()).ToString());
                else
                    TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.AlreadySelected".Translate(newFocus.LabelShort, count.ToString()).ToString());
            }
        }

        /// <summary>Moves focus to the next pawn without changing the selection.</summary>
        public static void NavigateFocusNext()
        {
            Pawn pawn = ColonistBarState.NavigateFocusRight();
            if (pawn == null)
                return;

            focusedPawn = pawn;
            AnnounceFocusedPawn(pawn);
        }

        /// <summary>Moves focus to the previous pawn without changing the selection.</summary>
        public static void NavigateFocusPrevious()
        {
            Pawn pawn = ColonistBarState.NavigateFocusLeft();
            if (pawn == null)
                return;

            focusedPawn = pawn;
            AnnounceFocusedPawn(pawn);
        }

        /// <summary>Clears multi-select and single-selects the focused pawn.</summary>
        public static void ClearMultiSelect()
        {
            if (BlockedByActiveTargeting("RimWorldAccess.Pawns.MultiSelect.ActionClearSelection"))
                return;

            if (!IsMultiSelectMode)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.MultiSelect.Inactive".Loc());
                return;
            }

            rangeAnchorPawn = null;
            inMultiSelectMode = false;
            SingleSelectFocusedPawn();

            if (focusedPawn != null)
            {
                string task = focusedPawn.GetJobReport();
                if (string.IsNullOrEmpty(task))
                    task = "RimWorldAccess.Pawns.MultiSelect.Idle".Translate().ToString();
                TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.SelectionClearedFocus".Translate(focusedPawn.LabelShort, task).ToString());
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.MultiSelect.SelectionCleared".Loc());
            }
        }

        /// <summary>Selects every colonist on the current map.</summary>
        public static void SelectAllColonists(List<Pawn> allColonists)
        {
            if (BlockedByActiveTargeting("RimWorldAccess.Pawns.MultiSelect.ActionSelectAll"))
                return;

            if (allColonists == null || allColonists.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.MultiSelect.NoColonists".Loc());
                return;
            }

            var selector = Find.Selector;
            if (selector == null)
                return;

            rangeAnchorPawn = null;

            selector.ClearSelection();
            foreach (var pawn in allColonists)
            {
                if (IsPawnValid(pawn))
                    selector.Select(pawn, forceDesignatorDeselect: false);
            }

            var selected = selector.SelectedPawns.ToList();
            if (selected.Count > 0)
            {
                focusedPawn = selected.First();
                ColonistBarState.SyncBarPosition(focusedPawn);
                inMultiSelectMode = true;
            }

            GizmoNavigationState.PawnJustSelected = true;

            if (selected.Count <= 5)
            {
                string names = MenuHelper.FormatNameList(
                    selected.Select(p => p.LabelShort).ToList());
                TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.AllSelectedNamed".Translate(names, selected.Count.ToString()).ToString());
            }
            else
            {
                TolkHelper.SpeakData("RimWorldAccess.Pawns.MultiSelect.AllSelectedCount".Translate(selected.Count.ToString()).ToString());
            }
        }

        /// <summary>Activates multi-select with a given set of pawns; used by group recall.</summary>
        public static void SetSelection(IEnumerable<Pawn> pawns)
        {
            var selector = Find.Selector;
            if (selector == null)
                return;

            rangeAnchorPawn = null;

            selector.ClearSelection();
            foreach (var pawn in pawns)
            {
                if (IsPawnValid(pawn))
                    selector.Select(pawn, forceDesignatorDeselect: false);
            }

            var selected = selector.SelectedPawns.ToList();
            if (selected.Count > 0)
            {
                focusedPawn = selected.First();
                ColonistBarState.SyncBarPosition(focusedPawn);
                GizmoNavigationState.PawnJustSelected = true;
                inMultiSelectMode = true;
            }
            else
            {
                inMultiSelectMode = false;
            }
        }

        /// <summary>
        /// Deselects any dead, destroyed, despawned or off-map pawn from Find.Selector, and
        /// invalidates focusedPawn if it no longer refers to a valid pawn.
        /// </summary>
        public static void ValidateAndCleanupSelection()
        {
            var selector = Find.Selector;
            if (selector == null)
                return;

            var snapshot = selector.SelectedPawns.ToList();
            if (snapshot.Count == 0)
            {
                if (focusedPawn != null && !IsPawnValid(focusedPawn))
                    focusedPawn = null;
                inMultiSelectMode = false;
                return;
            }

            foreach (var pawn in snapshot)
            {
                if (!IsPawnValid(pawn))
                    selector.Deselect(pawn);
            }

            if (focusedPawn != null && !IsPawnValid(focusedPawn))
                focusedPawn = null;

            if (rangeAnchorPawn != null &&
                (!IsPawnValid(rangeAnchorPawn) || !selector.IsSelected(rangeAnchorPawn)))
            {
                rangeAnchorPawn = null;
            }

            if (focusedPawn == null && selector.SelectedPawns.Count == 1)
                focusedPawn = selector.SelectedPawns.First();
        }

        /// <summary>
        /// Tracks a normal single-select: moves the focus cursor to the pawn and exits multi-select
        /// mode, single-selecting being the canonical way out.
        /// </summary>
        public static void NotifySingleSelect(Pawn pawn)
        {
            focusedPawn = pawn;
            rangeAnchorPawn = null;
            inMultiSelectMode = false;
        }

        /// <summary>
        /// Clears the focus cursor at a session boundary so a stale Pawn reference cannot leak into
        /// the new game. Find.Selector is already fresh on load, so no selection clearing is needed.
        /// </summary>
        public static void Reset()
        {
            focusedPawn = null;
            rangeAnchorPawn = null;
            inMultiSelectMode = false;
        }

        /// <summary>Single-selects the focused pawn in RimWorld's Selector, for the exit from multi-select.</summary>
        private static void SingleSelectFocusedPawn()
        {
            var selector = Find.Selector;
            if (focusedPawn == null || selector == null)
                return;

            if (!focusedPawn.Destroyed && focusedPawn.Spawned)
            {
                selector.ClearSelection();
                selector.Select(focusedPawn, forceDesignatorDeselect: false);
                GizmoNavigationState.PawnJustSelected = true;
            }
        }

        /// <summary>Sets the focused pawn without changing the selection.</summary>
        public static void SetFocusedPawn(Pawn pawn)
        {
            focusedPawn = pawn;
        }

        /// <summary>Announces the focused pawn as "{name}, selected/not selected, {job}, X of Y".</summary>
        public static void AnnounceFocusedPawn(Pawn pawn)
        {
            string selectedStatus = (IsPawnSelected(pawn)
                ? "RimWorldAccess.Pawns.MultiSelect.SelectedStatus"
                : "RimWorldAccess.Pawns.MultiSelect.NotSelectedStatus").Translate();

            string task = pawn.GetJobReport();
            if (string.IsNullOrEmpty(task))
                task = "RimWorldAccess.Pawns.MultiSelect.Idle".Translate();

            var list = ColonistBarState.GetCurrentSectionPawns();

            int total = list?.Count ?? 0;
            string positionPart = MenuHelper.FormatPosition(ColonistBarState.BarPosition, total);

            string announcement = !string.IsNullOrEmpty(positionPart)
                ? "RimWorldAccess.Pawns.MultiSelect.PawnSummaryWithPos".Translate(pawn.LabelShort, selectedStatus, task, positionPart).ToString()
                : "RimWorldAccess.Pawns.MultiSelect.PawnSummary".Translate(pawn.LabelShort, selectedStatus, task).ToString();

            TolkHelper.SpeakData(announcement);
        }
    }
}
