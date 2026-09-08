using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    public enum PawnEditorContext
    {
        GameStart,
        Wanderer
    }

    /// <summary>
    /// Lifecycle flag + mutation vehicles for the starting-pawn editor, shared by both hosts
    /// (Page_ConfigureStartingPawns / Dialog_ChooseNewWanderers). All keyboard navigation and
    /// row presentation now live in <see cref="RimWorldAccess.Shell.StartingPawnScreenScope"/>;
    /// this class keeps only what the
    /// scope has no business owning itself: the IsActive/Context flags other systems gate on
    /// (PawnFilterState, RerollState, StartingSiteScreenScope, StartingPawnHelper's tree shape), and
    /// the pawn-mutation methods the scope's rows call into.
    ///
    /// The following no longer exist: the TreeNavigationHelper field and every
    /// tree-nav wrapper (TreeNavigatePrevious/Next/ExpandOrDrillDown/CollapseOrDrillUp/Home/End/
    /// Reannounce/ExpandAllSiblings/SearchBackspace/Typeahead), ToggleTab/OnTeamSkillsTab (Team
    /// Skills is now a plain content region, no separate sub-mode), HandleConfirm/HandleCancel
    /// (Enter/Escape are scope-claimed directly now), SwitchPawn/TryJumpToRelatedPawn/
    /// FindPawnNode*/CollapsePawnNode/GetAllNodes/SaveTreePosition/RestoreTreePosition (their
    /// position-preserving logic is reimplemented against the scope's own flat row model, which
    /// this class has no way to see), and RebuildTree (the scope calls
    /// StartingPawnHelper.BuildTree directly and flattens it itself).
    /// </summary>
    public static class StartingPawnState
    {
        private static bool isActive = false;
        private static bool awaitingRenameRebuild = false;

        public static bool IsActive => isActive;

        public static PawnEditorContext Context { get; private set; } = PawnEditorContext.GameStart;

        public static bool IsWandererContext => Context == PawnEditorContext.Wanderer;

        public static void Open(PawnEditorContext context = PawnEditorContext.GameStart)
        {
            Context = context;
            isActive = true;
        }

        public static void Close()
        {
            isActive = false;
            awaitingRenameRebuild = false;
        }

        /// <summary>Called by the rename dialog's PreOpen equivalent (RenamePawnAt) so a pending rebuild is armed.</summary>
        public static void CheckPendingRenameRebuild()
        {
            if (awaitingRenameRebuild && !Find.WindowStack.IsOpen<Dialog_NamePawn>())
            {
                awaitingRenameRebuild = false;
                Shell.StartingPawnScreenScope.Active?.RefreshAndReannounceAfterExternalChange();
            }
        }

        /// <summary>The pawn index Page_ConfigureStartingPawns.curPawnIndex / Dialog_ChooseNewWanderers.curPawnIndex must mirror, so the portrait/skills area renders the pawn the scope's cursor rests on.</summary>
        public static int GetSelectedPawnIndex()
        {
            return Shell.StartingPawnScreenScope.Active?.CurrentPawnIndex ?? 0;
        }

        // ===== ACTIONS (mutation vehicles the scope's rows call into) =====

        public static void RandomizePawnAt(int pawnIdx)
        {
            if (pawnIdx < 0) return;

            // If filters are active, PawnFilterRandomizePatch routes the call into RerollState,
            // which owns the batch loop and finishes via StartingPawnState.OnRerollComplete
            // (synchronously on first-attempt match, otherwise across frames). Don't duplicate
            // that work here.
            bool filtersActive = PawnFilterData.HasActiveFilters();
            StartingPawnUtility.RandomizePawn(pawnIdx);
            if (filtersActive)
                return;

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

            string randomizeAnnouncement = "Randomize".Translate();
            if (PawnFilterData.HasActiveFilters())
            {
                randomizeAnnouncement = PawnFilterData.LastRerollSucceeded
                    ? "RimWorldAccess.StartingPawn.RandomizeWithMatch".Translate(randomizeAnnouncement, PawnFilterData.LastRerollAttempts)
                    : "RimWorldAccess.StartingPawn.RandomizeNoMatch".Translate(randomizeAnnouncement, PawnFilterData.LastRerollAttempts);
            }
            TolkHelper.SpeakData(randomizeAnnouncement);
            Shell.StartingPawnScreenScope.Active?.RefreshAndReannounceAfterExternalChange();
        }

        /// <summary>Called by RerollState once its batch (synchronous or spread across frames) finishes.</summary>
        public static void OnRerollComplete(bool success, int attempts, bool cancelled)
        {
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

            string announcement;
            if (cancelled)
                announcement = "RimWorldAccess.StartingPawn.RerollStopped".Translate(attempts);
            else if (success)
                announcement = "RimWorldAccess.StartingPawn.RerollFound".Translate(attempts);
            else
                announcement = "RimWorldAccess.StartingPawn.RerollNoMatch".Translate(attempts);

            TolkHelper.SpeakData(announcement);
            Shell.StartingPawnScreenScope.Active?.RefreshAndReannounceAfterExternalChange();
        }

        public static void RenamePawnAt(int pawnIdx)
        {
            if (pawnIdx < 0) return;

            var pawn = StartingPawnHelper.GetPawnAtIndex(pawnIdx);
            if (pawn == null) return;

            var allFields = NameFilter.First | NameFilter.Nick | NameFilter.Last | NameFilter.Title;
            awaitingRenameRebuild = true;
            Find.WindowStack.Add(new Dialog_NamePawn(pawn, allFields, allFields, null));
        }

        /// <summary>
        /// Reorders pawnIdx by direction (matches Page_ConfigureStartingPawns.DrawPawnList's
        /// ReorderableWidget.NewGroup callback, Page_ConfigureStartingPawns.cs:196-211): the
        /// whole reorder is gated on TutorSystem.AllowAction, and Notify_Event fires the same
        /// two hooks vanilla's drag-reorder fires. Returns the composed neighbor-context
        /// announcement and the pawn's new index on success; the caller (the scope) owns
        /// repositioning its own cursor and the structural re-announce.
        /// </summary>
        public static bool ReorderPawnAt(int pawnIdx, int direction, out string announcement, out int newIndex)
        {
            announcement = null;
            newIndex = pawnIdx;

            var pawns = Find.GameInitData.startingAndOptionalPawns;
            int targetIdx = pawnIdx + direction;
            if (targetIdx < 0 || targetIdx >= pawns.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
            if (!TutorSystem.AllowAction("ReorderPawn")) return false;

            int startingCount = StartingPawnHelper.GetStartingPawnCount();

            var temp = pawns[pawnIdx];
            pawns[pawnIdx] = pawns[targetIdx];
            pawns[targetIdx] = temp;

            StartingPawnUtility.ReorderRequests(pawnIdx, targetIdx);

            TutorSystem.Notify_Event("ReorderPawn");
            if (targetIdx < startingCount && pawnIdx >= startingCount)
            {
                TutorSystem.Notify_Event("ReorderPawnOptionalToStarting");
            }

            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

            var movedPawn = pawns[targetIdx];
            string movedName = movedPawn.LabelShort;
            bool crossedBoundary = (pawnIdx < startingCount) != (targetIdx < startingCount);

            var parts = new List<string> { movedName };
            if (crossedBoundary)
            {
                string group = targetIdx < startingCount
                    ? "StartingPawnsSelected".Translate()
                    : "StartingPawnsLeftBehind".Translate();
                parts.Add(group);
            }
            string prevName = targetIdx > 0 ? pawns[targetIdx - 1].LabelShort : null;
            string nextName = targetIdx < pawns.Count - 1 ? pawns[targetIdx + 1].LabelShort : null;
            if (prevName != null && nextName != null)
                parts.Add("RimWorldAccess.StartingPawn.NeighborBetween".Translate(prevName, nextName));
            else if (prevName != null)
                parts.Add("RimWorldAccess.StartingPawn.NeighborAfter".Translate(prevName));
            else if (nextName != null)
                parts.Add("RimWorldAccess.StartingPawn.NeighborBefore".Translate(nextName));

            announcement = string.Join(", ", parts);
            newIndex = targetIdx;
            return true;
        }

        public static void OpenContextMenuFor(int pawnIndex, Action rebuildCallback)
        {
            try
            {
                if (pawnIndex < 0)
                {
                    TolkHelper.Speak("RimWorldAccess.StartingPawn.NoPawnSelected".Loc());
                    return;
                }

                var options = PawnContextMenuBuilder.GetContextMenuOptions(pawnIndex, rebuildCallback);
                if (options.Count > 0)
                {
                    WindowlessFloatMenuState.Open(options, colonistOrders: false);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in OpenContextMenuFor: {ex}");
            }
        }

        /// <summary>Escape's confirmation on the GameStart host (kept from the pre-retrofit UX — see the scope's class remarks for why this deliberately deviates from the generic flag+guard-twin shape).</summary>
        public static void RequestBackConfirm()
        {
            if (Find.WindowStack.WindowOfType<Dialog_MessageBox>() != null) return;

            if (Event.current != null && Event.current.type == EventType.KeyDown)
                Event.current.Use();

            string message = "RimWorldAccess.StartingPawn.GoBackConfirm".Translate();
            Action confirm = () => StartingPawnPatch.DoBack();
            Find.WindowStack.Add(new Dialog_MessageBox(
                message,
                buttonAText: "RimWorldAccess.StartingPawn.GoBackContinue".Translate(),
                buttonAAction: confirm,
                buttonBText: "RimWorldAccess.StartingPawn.GoBackCancel".Translate(),
                buttonBAction: null,
                title: null,
                buttonADestructive: true,
                acceptAction: confirm,
                cancelAction: delegate { }));
        }

        /// <summary>The "Start" button's confirmation (kept from the pre-retrofit UX).</summary>
        public static void ConfirmStartGame()
        {
            if (!StartingPawnPatch.CanDoNext())
                return;

            var pawns = Find.GameInitData.startingAndOptionalPawns;
            int startingCount = Find.GameInitData.startingPawnCount;
            var names = new List<string>();
            for (int i = 0; i < startingCount && i < pawns.Count; i++)
                names.Add(pawns[i].LabelShort);

            string pawnList = names.ToCommaList(useAnd: true);
            string message = "RimWorldAccess.StartingPawn.StartGameConfirm".Translate(pawnList);

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                message,
                () => StartingPawnPatch.DoNext(),
                destructive: false));
        }

        // ===== WANDERER-SPECIFIC ACTIONS =====

        /// <summary>The wanderer "Confirm" button's confirmation (kept from the pre-retrofit UX).</summary>
        public static void ConfirmWandererStart()
        {
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            var names = new List<string>();
            for (int i = 0; i < pawns.Count; i++)
                names.Add(pawns[i].LabelShort);

            string pawnList = names.ToCommaList(useAnd: true);
            string message = "RimWorldAccess.StartingPawn.WandererConfirm".Translate("Confirm".Translate(), pawnList);

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                message,
                () => WandererPatch.ConfirmWanderers(),
                destructive: false));
        }

        public static bool AddWandererPawn(out string announcement)
        {
            announcement = null;
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            // Mirrors vanilla's add-button gate (decompiled Dialog_ChooseNewWanderers.cs:125,
            // DrawPawnList's StartingAndOptionalPawns.Count < 6).
            if (pawns.Count >= 6)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.StartingPawn.MaximumPawns".Loc(6));
                return false;
            }

            WandererPatch.AddPawn();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            announcement = (string)"RimWorldAccess.StartingPawn.PawnAdded".Translate();
            return true;
        }

        public static bool RemoveWandererPawn(int pawnIdx, out string announcement)
        {
            announcement = null;
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            if (pawnIdx < 0 || pawnIdx >= pawns.Count) return false;
            // Mirrors vanilla's delete-button gate (decompiled
            // Dialog_ChooseNewWanderers.cs:153, DoPawnRow's Count > 1).
            if (pawns.Count <= 1)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.StartingPawn.MinimumPawns".Loc(1));
                return false;
            }

            string removedName = pawns[pawnIdx].LabelShort;
            WandererPatch.RemovePawn(pawnIdx);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            announcement = (string)"RimWorldAccess.StartingPawn.PawnRemoved".Translate(removedName);
            return true;
        }
    }
}
