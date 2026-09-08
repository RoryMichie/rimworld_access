using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;
using RimWorld;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin bridge for the Work menu's FOCUSED view. <see cref="IsActive"/> is the flag
    /// <see cref="WorkMenuScopeMirror"/> watches to push/pop <see cref="WorkMenuScope"/>, which owns
    /// the whole screen — regions, rows, cursor, typeahead and announcements. This class keeps the
    /// lifecycle surface external callers drive, the <see cref="PendingInitialPawn"/> swap/reopen
    /// hand-off, the eligible-colonist list and priority vocabulary the scope names regions with, and
    /// the MUTATION-C write side. Each write takes the pawn and entry it acts on as arguments rather
    /// than reading a cursor, and reports its outcome (<see cref="PriorityWriteResult"/>) rather than
    /// moving one; refusals speak for themselves here because they belong to the gate, while the
    /// scope speaks the landing only it can see.
    /// </summary>
    public static class WorkMenuState
    {
        private static bool isActive;

        public static bool IsActive
        {
            get { return isActive; }
        }

        /// <summary>What one priority write did, for the caller that has to react to it.</summary>
        public enum PriorityWriteResult
        {
            /// <summary>The gate said no and announced why; nothing was written.</summary>
            Refused,

            /// <summary>Nothing to change (a cycle already at the end of its range); silent.</summary>
            Unchanged,

            /// <summary>The priority was written through vanilla's own setter.</summary>
            Written,
        }

        /// <summary>
        /// The pawn to land the pawn cursor on, set by <see cref="Open"/> and consumed once by the
        /// scope's first OnFocus after each open; re-set on every open, so a stale value never
        /// survives into a later session.
        /// </summary>
        public static Pawn PendingInitialPawn { get; private set; }

        /// <summary>Manual vs. basic priority mode — pure passthrough to the vanilla setting, unrelated to any menu state.</summary>
        public static bool IsManualMode
        {
            get { return Find.PlaySettings.useWorkPriorities; }
        }

        private static WorkMenuScope Scope
        {
            get { return WorkMenuScopeMirror.Instance; }
        }

        /// <summary>
        /// Opens the work menu for the specified pawn. Only eligibility resolves here — the row build
        /// and opening announcement happen on the scope's first OnFocus, one dispatcher pass later.
        /// </summary>
        public static void Open(Pawn pawn)
        {
            if (Find.CurrentMap == null)
                return;

            List<Pawn> eligible = BuildEligibleColonists() ?? new List<Pawn>();
            if (eligible.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Input.WorkMenu.NoColonistsAvailable".Loc());
                return;
            }

            PendingInitialPawn = pawn;
            isActive = true;
        }

        /// <summary>Closes the menu. Changes apply in real time, so this only finalizes the work-giver cache and announces.</summary>
        public static void Confirm()
        {
            Pawn pawn = Scope.CurrentPawn;
            if (pawn != null && pawn.workSettings != null)
                pawn.workSettings.Notify_UseWorkPrioritiesChanged();

            RefreshAllPawnsWorkGivers();

            CleanupState();
            TolkHelper.Speak("RimWorldAccess.Work.Saved".Loc());
            Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Work);
        }

        /// <summary>Closes without announcing (swapping to the table view); changes already persist.</summary>
        public static void CloseForSwap()
        {
            if (!isActive) return;
            Pawn pawn = Scope.CurrentPawn;
            if (pawn?.workSettings != null)
                pawn.workSettings.Notify_UseWorkPrioritiesChanged();
            RefreshAllPawnsWorkGivers();
            CleanupState();
        }

        /// <summary>
        /// Deactivates without the Confirm/CloseForSwap finalization when the scope's first focus finds
        /// the eligible roster emptied in the one-pass window since <see cref="Open"/>'s own check.
        /// Nothing was populated, so nothing needs finalizing; the mirror pops the scope next pass.
        /// </summary>
        internal static void AbortEmptyFocus()
        {
            isActive = false;
        }

        /// <summary>
        /// Silent deactivation for MainTabWindowLink's reconcile: the vanilla tab window went away, so
        /// the screen goes with it. Confirm's sound and announcement belong to the user's own
        /// Escape/Enter, so this runs only the shared cleanup.
        /// </summary>
        internal static void CloseSilent()
        {
            CleanupState();
        }

        private static void CleanupState()
        {
            // The scope's own instance state is left as-is: inert while popped, and wiped by
            // WorkMenuScope.OnPush on the next Open().
            isActive = false;
        }

        private static void RefreshAllPawnsWorkGivers()
        {
            if (Find.CurrentMap != null)
            {
                foreach (Pawn pawn in Find.CurrentMap.mapPawns.FreeColonists)
                {
                    if (pawn.workSettings != null)
                    {
                        pawn.workSettings.Notify_UseWorkPrioritiesChanged();
                    }
                }
            }
        }

        /// <summary>
        /// The live list of eligible colonists (free colonists, excluding babies) in display order, or
        /// null with no current map. Shared by <see cref="Open"/>'s gate and the scope's refresh.
        /// </summary>
        internal static List<Pawn> BuildEligibleColonists()
        {
            if (Find.CurrentMap == null) return null;
            return PlayerPawnsDisplayOrderUtility.InOrder(
                    Find.CurrentMap.mapPawns.FreeColonists
                        .Where(p => !p.DevelopmentalStage.Baby() && p.workSettings != null))
                .ToList();
        }

        /// <summary>The pawn whose work givers <see cref="Confirm"/> finalizes, and the swap-to-table hand-off's subject.</summary>
        public static Pawn CurrentPawn
        {
            get { return Scope.CurrentPawn; }
        }

        // -------------------------------------------------------------------
        // Pure priority/level helpers: the vocabulary the scope names regions and announcements with.
        // -------------------------------------------------------------------

        /// <summary>Priority value (0-4) to level index: priority 1-4 map to levels 0-3, priority 0 (disabled) to level 4.</summary>
        internal static int PriorityToColumnIndex(int priority)
        {
            if (priority == 0) return 4; // Disabled
            return priority - 1; // Priority 1-4 -> level 0-3
        }

        internal static string GetColumnName(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0: return "RimWorldAccess.Work.Column.Priority1".Translate();
                case 1: return "RimWorldAccess.Work.Column.Priority2".Translate();
                case 2: return "RimWorldAccess.Work.Column.Priority3".Translate();
                case 3: return "RimWorldAccess.Work.Column.Priority4".Translate();
                case 4: return "RimWorldAccess.Work.Column.Disabled".Translate();
                default: return "RimWorldAccess.Work.Column.Unknown".Translate();
            }
        }

        /// <summary>The bare priority word for announcements: "1".."4" or "disabled".</summary>
        internal static string PriorityWord(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0: return "RimWorldAccess.Work.PriorityWord.1".Translate();
                case 1: return "RimWorldAccess.Work.PriorityWord.2".Translate();
                case 2: return "RimWorldAccess.Work.PriorityWord.3".Translate();
                case 3: return "RimWorldAccess.Work.PriorityWord.4".Translate();
                case 4: return "RimWorldAccess.Work.PriorityWord.Disabled".Translate();
                default: return "";
            }
        }

        #region Task Operations (MUTATION-C write-side — untouched logic, cursor-free interface)

        /// <summary>
        /// Sets one work type's priority for one pawn: in manual mode this moves
        /// it to another priority level, in basic mode it writes the value
        /// directly. Announces its own refusals; the caller announces the
        /// landing.
        /// </summary>
        internal static PriorityWriteResult SetPriority(Pawn pawn, WorkTypeEntry entry, int priority)
        {
            if (priority < 0 || priority > 4) return PriorityWriteResult.Refused;
            if (pawn == null || pawn.workSettings == null || entry == null) return PriorityWriteResult.Refused;

            if (entry.IsPermanentlyDisabled)
            {
                AnnounceCannotEnable(pawn, entry);
                return PriorityWriteResult.Refused;
            }

            bool wasActive = pawn.workSettings.WorkIsActive(entry.WorkType);

            if (IsManualMode)
            {
                int newColumnIndex = PriorityToColumnIndex(priority);
                if (newColumnIndex == PriorityToColumnIndex(entry.CurrentPriority))
                {
                    TolkHelper.Speak("RimWorldAccess.Work.SetPriority.AlreadyAtPriority".Loc(
                        entry.WorkType.labelShort, PriorityWord(newColumnIndex)));
                    return PriorityWriteResult.Refused;
                }

                pawn.workSettings.SetPriority(entry.WorkType, priority);

                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                PlayActivationSounds(pawn, entry.WorkType, wasActive);
                return PriorityWriteResult.Written;
            }

            pawn.workSettings.SetPriority(entry.WorkType, priority);

            bool nowActive = pawn.workSettings.WorkIsActive(entry.WorkType);
            if (!wasActive && nowActive)
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            else if (wasActive && !nowActive)
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            PlayActivationSounds(pawn, entry.WorkType, wasActive);
            return PriorityWriteResult.Written;
        }

        /// <summary>
        /// Plays vanilla's warning sounds when work goes inactive to active: Crunch for low-skill
        /// pawns, DislikedWorkTypeActivated for ideo-opposed work. Mirrors WidgetsWork.DrawWorkBoxFor.
        /// </summary>
        private static void PlayActivationSounds(Pawn pawn, WorkTypeDef workType, bool wasActive)
        {
            if (wasActive) return;
            if (!pawn.workSettings.WorkIsActive(workType)) return;

            if (workType.relevantSkills.Any() && pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
            {
                SoundDefOf.Crunch.PlayOneShotOnCamera();
            }
            if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
            {
                Messages.Message("MessageIdeoOpposedWorkTypeSelected".Translate(pawn, workType.gerundLabel), pawn, MessageTypeDefOf.CautionInput, historical: false);
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Sets one work type's priority across ALL eligible colonists, mirroring vanilla's
        /// shift-click header behaviour from PawnColumnWorker_WorkPriority. Manual mode only. Speaks
        /// the bulk report itself; returns whether <paramref name="current"/>'s own priority changed.
        /// </summary>
        internal static bool SetPriorityForAllPawns(List<Pawn> pawns, Pawn current, WorkTypeEntry entry, int priority)
        {
            if (priority < 0 || priority > 4) return false;
            if (!IsManualMode || pawns == null || entry == null) return false;

            if (entry.IsPermanentlyDisabled)
            {
                AnnounceCannotEnable(current, entry);
                return false;
            }

            WorkTypeDef workType = entry.WorkType;
            string jobName = workType.labelShort;

            var changed = new List<Pawn>();
            var alreadySetNames = new List<string>();
            var incapableNames = new List<string>();
            bool anyLowSkillActivated = false;
            bool anyIdeoOpposedActivated = false;
            var ideoOpposedPawns = new List<Pawn>();

            foreach (Pawn pawn in pawns)
            {
                // Eligibility check matching vanilla's HeaderClicked logic.
                if (pawn.workSettings == null || !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType))
                {
                    incapableNames.Add(pawn.LabelShort);
                    continue;
                }

                int currentPriority = pawn.workSettings.GetPriority(workType);
                if (currentPriority == priority)
                {
                    alreadySetNames.Add(pawn.LabelShort);
                    continue;
                }

                bool wasActive = pawn.workSettings.WorkIsActive(workType);
                pawn.workSettings.SetPriority(workType, priority);
                changed.Add(pawn);

                if (!wasActive && pawn.workSettings.WorkIsActive(workType))
                {
                    if (workType.relevantSkills.Any() && pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
                        anyLowSkillActivated = true;
                    if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
                    {
                        anyIdeoOpposedActivated = true;
                        ideoOpposedPawns.Add(pawn);
                    }
                }
            }

            if (changed.Count > 0)
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            if (anyLowSkillActivated)
                SoundDefOf.Crunch.PlayOneShotOnCamera();
            if (anyIdeoOpposedActivated)
            {
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
                foreach (var p in ideoOpposedPawns)
                    Messages.Message("MessageIdeoOpposedWorkTypeSelected".Translate(p, workType.gerundLabel), p, MessageTypeDefOf.CautionInput, historical: false);
            }

            List<string> changedNames = changed.Select(p => p.LabelShort).ToList();
            TolkHelper.SpeakData(BuildBulkSetAnnouncement(
                pawns.Count, jobName, priority, changedNames, alreadySetNames, incapableNames));

            return changed.Contains(current);
        }

        /// <summary>Builds the bulk-set announcement; "already at target priority" counts as success, only incapable pawns as exceptions.</summary>
        private static string BuildBulkSetAnnouncement(
            int totalPawns,
            string jobName,
            int priority,
            List<string> changedNames,
            List<string> alreadySetNames,
            List<string> incapableNames)
        {
            string priorityLabel = priority == 0
                ? "RimWorldAccess.Work.PriorityWord.Disabled".Translate().ToString()
                : "RimWorldAccess.Work.PriorityLabel".Translate(priority).ToString();
            int successCount = changedNames.Count + alreadySetNames.Count;

            if (successCount == 0)
            {
                return "RimWorldAccess.Work.BulkSet.NoCapableColonists".Translate(jobName);
            }

            if (changedNames.Count == 0)
            {
                if (incapableNames.Count == 0)
                {
                    return "RimWorldAccess.Work.BulkSet.AlreadyForAll".Translate(jobName, priorityLabel);
                }
                return "RimWorldAccess.Work.BulkSet.AlreadyForAllCapable".Translate(jobName, priorityLabel);
            }

            if (incapableNames.Count == 0)
            {
                return "RimWorldAccess.Work.BulkSet.SetForAll".Translate(jobName, priorityLabel);
            }

            if (incapableNames.Count <= 5 && incapableNames.Count <= totalPawns / 2)
            {
                string exceptList = FormatNameList(incapableNames);
                return "RimWorldAccess.Work.BulkSet.SetForAllExcept".Translate(jobName, priorityLabel, exceptList);
            }
            else
            {
                string changedList = FormatNameList(changedNames);
                if (alreadySetNames.Count > 0)
                {
                    string key = alreadySetNames.Count == 1
                        ? "RimWorldAccess.Work.BulkSet.SetForListWithAlreadySetOne"
                        : "RimWorldAccess.Work.BulkSet.SetForListWithAlreadySetMany";
                    return key.Translate(jobName, priorityLabel, changedList, alreadySetNames.Count);
                }
                return "RimWorldAccess.Work.BulkSet.SetForList".Translate(jobName, priorityLabel, changedList);
            }
        }

        /// <summary>
        /// Cycles one entry's priority a single step, matching vanilla HeaderClicked semantics:
        /// decrease lowers the number (more important), increase raises it. In basic mode, decrease
        /// enables (3) and increase disables (0).
        /// </summary>
        internal static PriorityWriteResult CyclePriority(Pawn pawn, WorkTypeEntry entry, bool decrease)
        {
            if (entry == null) return PriorityWriteResult.Refused;
            if (entry.IsPermanentlyDisabled)
            {
                AnnounceCannotEnable(pawn, entry);
                return PriorityWriteResult.Refused;
            }
            if (!TryComputeCyclePriority(entry.CurrentPriority, decrease, out int next))
            {
                return PriorityWriteResult.Unchanged;
            }
            return SetPriority(pawn, entry, next);
        }

        /// <summary>
        /// Applies the same priority cycle to every eligible colonist for this work type (vanilla's
        /// shift-click header). Speaks its own report; returns whether
        /// <paramref name="current"/>'s own priority changed.
        /// </summary>
        internal static bool CycleAllPawnsPriorityForCurrent(List<Pawn> pawns, Pawn current, WorkTypeEntry entry, bool decrease)
        {
            if (pawns == null || entry == null) return false;

            WorkTypeDef workType = entry.WorkType;
            var changed = new List<Pawn>();
            bool anyLowSkillActivated = false;
            bool anyIdeoOpposedActivated = false;
            var ideoOpposedPawns = new List<Pawn>();

            foreach (Pawn pawn in pawns)
            {
                if (pawn.workSettings == null || !pawn.workSettings.EverWork) continue;
                if (pawn.WorkTypeIsDisabled(workType)) continue;

                int currentPriority = pawn.workSettings.GetPriority(workType);
                if (!TryComputeCyclePriority(currentPriority, decrease, out int next)) continue;
                if (next == currentPriority) continue;

                bool wasActive = pawn.workSettings.WorkIsActive(workType);
                pawn.workSettings.SetPriority(workType, next);
                changed.Add(pawn);

                if (!wasActive && pawn.workSettings.WorkIsActive(workType))
                {
                    if (workType.relevantSkills.Any() &&
                        pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
                        anyLowSkillActivated = true;
                    if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
                    {
                        anyIdeoOpposedActivated = true;
                        ideoOpposedPawns.Add(pawn);
                    }
                }
            }

            if (changed.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(
                    (string)"RimWorldAccess.Work.Menu.NoChangeFor".Translate(workType.labelShort));
                return false;
            }

            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            if (anyLowSkillActivated) SoundDefOf.Crunch.PlayOneShotOnCamera();
            if (anyIdeoOpposedActivated)
            {
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
                foreach (var p in ideoOpposedPawns)
                    Messages.Message(
                        "MessageIdeoOpposedWorkTypeSelected".Translate(p, workType.gerundLabel),
                        p, MessageTypeDefOf.CautionInput, historical: false);
            }

            string nameList = FormatNameList(changed.Select(p => p.LabelShort).ToList());
            string cycleAnnouncement = decrease
                ? (string)"RimWorldAccess.Work.Table.RaisedForList".Translate(workType.labelShort, nameList)
                : (string)"RimWorldAccess.Work.Table.LoweredForList".Translate(workType.labelShort, nameList);
            TolkHelper.SpeakData(cycleAnnouncement);

            return changed.Contains(current);
        }

        /// <summary>
        /// Vanilla's HeaderClicked cycle: decrease skips 1, else num-1 with 0 wrapping to 4; increase
        /// skips 0, else num+1 with 4 wrapping to 0. Basic mode: decrease enables (3), increase
        /// disables (0).
        /// </summary>
        private static bool TryComputeCyclePriority(int current, bool decrease, out int next)
        {
            if (!Find.PlaySettings.useWorkPriorities)
            {
                if (decrease)
                {
                    if (current == 0) { next = 3; return true; }
                    next = current; return false;
                }
                if (current > 0) { next = 0; return true; }
                next = current; return false;
            }

            if (decrease)
            {
                if (current == 1) { next = 1; return false; }
                int n = current - 1;
                if (n < 0) n = 4;
                next = n;
                return true;
            }
            else
            {
                if (current == 0) { next = 0; return false; }
                int n = current + 1;
                if (n > 4) n = 0;
                next = n;
                return true;
            }
        }

        /// <summary>Toggles one entry between enabled (priority 3) and disabled (priority 0).</summary>
        internal static PriorityWriteResult ToggleSelected(Pawn pawn, WorkTypeEntry entry)
        {
            if (entry == null) return PriorityWriteResult.Refused;

            if (entry.IsPermanentlyDisabled)
            {
                AnnounceCannotEnable(pawn, entry);
                return PriorityWriteResult.Refused;
            }

            return SetPriority(pawn, entry, entry.CurrentPriority == 0 ? 3 : 0);
        }

        /// <summary>Toggles between basic and manual priority modes. The caller rebuilds and announces.</summary>
        internal static void ToggleMode()
        {
            // MUTATION-C: mirrors MainTabWindow_Work.DoManualPrioritiesCheckbox's bare ref-flip
            // (RimWorld/MainTabWindow_Work.cs:42) — no CanX/TryX gate exists on this checkbox.
            Find.PlaySettings.useWorkPriorities = !Find.PlaySettings.useWorkPriorities;

            // Vanilla's own flip-time sweep (MainTabWindow_Work.DoManualPrioritiesCheckbox:45-51)
            // invalidates every player pawn's cached work-giver ordering everywhere — other maps,
            // caravans, temporary maps — not just the pawn this menu shows.
            RefreshUseWorkPrioritiesForAllPlayerPawns();
        }

        /// <summary>Verbatim match of MainTabWindow_Work.DoManualPrioritiesCheckbox's flip-time sweep (decompiled :45-51): every player pawn with a workSettings tracker, anywhere (current map, other maps, caravans, temporary maps).</summary>
        private static void RefreshUseWorkPrioritiesForAllPlayerPawns()
        {
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (pawn.Faction == Faction.OfPlayer && pawn.workSettings != null)
                {
                    pawn.workSettings.Notify_UseWorkPrioritiesChanged();
                }
            }
        }

        #endregion

        #region Helpers (pure — no state dependency)

        /// <summary>Formats a name list with English grammar: "Alice", "Alice and Bob", "Alice, Bob, and Carol".</summary>
        private static string FormatNameList(List<string> names)
        {
            if (names.Count == 0) return "";
            if (names.Count == 1) return names[0];
            if (names.Count == 2) return "RimWorldAccess.Work.NameList.Two".Translate(names[0], names[1]);
            string separator = "RimWorldAccess.Work.NameList.Separator".Translate();
            string lastJoiner = "RimWorldAccess.Work.NameList.ThreePlusLastJoiner".Translate();
            return string.Join(separator, names.Take(names.Count - 1)) + lastJoiner + names[names.Count - 1];
        }

        private static void AnnounceCannotEnable(Pawn pawn, WorkTypeEntry entry)
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            var reasons = pawn.GetReasonsForDisabledWorkType(entry.WorkType);
            string reasonText = string.Join(", ", reasons.Select(r => r.ToString()));
            TolkHelper.Speak("RimWorldAccess.Work.CannotEnable".Loc(reasonText), SpeechPriority.High);
        }

        #endregion

        /// <summary>A work type entry in the menu.</summary>
        public class WorkTypeEntry
        {
            public WorkTypeDef WorkType { get; set; }
            public bool IsPermanentlyDisabled { get; set; }
            public int CurrentPriority { get; set; }
        }
    }
}
