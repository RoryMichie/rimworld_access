using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The quest menu's data/mutation backend, behind
    /// <see cref="RimWorldAccess.Shell.QuestMenuScope"/>, whose three content regions are built from
    /// the data this class owns: session lifecycle, the tab and dev-mode session fields, every quest
    /// mutation, and the reward-choice float menu with its item-inspection sub-flow (shared with
    /// <see cref="RimWorldAccess.Shell.QuestRewardOverlayScope"/> and
    /// <see cref="RimWorldAccess.Shell.FloatMenuOverlayScope"/>).
    /// </summary>
    public static class QuestMenuState
    {
        private static bool isActive = false;
        private static QuestsTab currentTab = QuestsTab.Available;

        // DEV-mode toggles mirroring MainTabWindow_Quests' embedded dev checkboxes. Reset on every
        // Open() to match vanilla's per-window-instance defaults.
        private static bool devShowAll = false;
        private static bool devShowDebugInfo = false;

        /// <summary>
        /// Effective "show all" state, mirroring MainTabWindow_Quests.DoQuestsList: god mode forces
        /// it on, non-dev forces it off, otherwise the toggle wins.
        /// </summary>
        internal static bool EffectiveShowAll =>
            DebugSettings.godMode || (Prefs.DevMode && devShowAll);

        /// <summary>Effective "show debug info" state, mirroring MainTabWindow_Quests.DoDebugInfoToggle's godMode/DevMode gating.</summary>
        internal static bool EffectiveShowDebugInfo =>
            DebugSettings.godMode || (Prefs.DevMode && devShowDebugInfo);

        // Reward choice float menu
        private static bool hasActiveRewardMenu = false;
        private static bool isInItemInspectionMenu = false;
        private static Quest rewardMenuQuest = null;
        private static List<QuestPart_Choice.Choice> rewardChoices = null;
        private static List<List<(Thing thing, Faction faction)>> choiceInspectables = null;
        private static List<(Thing thing, Faction faction)> currentInspectionItems = null;
        private static int savedChoiceIndex = -1;

        internal enum QuestsTab
        {
            Available,
            Active,
            Historical
        }


        public static bool IsActive => isActive;

        /// <summary>The currently displayed quest tab; session data QuestMenuScope reads and advances directly.</summary>
        internal static QuestsTab CurrentTab
        {
            get => currentTab;
            set => currentTab = value;
        }

        public static bool HasActiveRewardMenu => hasActiveRewardMenu;
        public static bool IsInItemInspectionMenu => isInItemInspectionMenu;

        /// <summary>
        /// One-shot hand-off from <see cref="OpenAndSelectQuest"/> to QuestMenuScope's OnFocus: the
        /// quest to find and select once the scope is live, in place of the normal open
        /// announcement. Cleared by <see cref="Open"/>, consumed and cleared by the scope.
        /// </summary>
        internal static Quest PendingSelectQuest;

        /// <summary>
        /// Wired once by QuestMenuScope's constructor and fired after any mutation that changes
        /// which quests are listed. The scope's hook returns focus to the Quests region and
        /// re-announces the current row.
        /// </summary>
        internal static Action PostQuestListChangeHook;

        /// <summary>Flips the session live and resets the tab and dev-mode fields; QuestMenuScope.OnFocus speaks the opening announcement.</summary>
        public static void Open()
        {
            isActive = true;
            currentTab = QuestsTab.Available;
            devShowAll = false;
            devShowDebugInfo = false;
            PendingSelectQuest = null;
        }

        /// <summary>
        /// Opens the quest menu targeting a specific quest ("View Quest" from a letter). The
        /// tab-scan, selection and announcement happen in QuestMenuScope.OnFocus via
        /// <see cref="PendingSelectQuest"/>; this method only runs the "quest is already gone" guard
        /// (which never sets isActive), the session-field reset, and the tab-window pairing.
        /// </summary>
        public static void OpenAndSelectQuest(Quest quest)
        {
            if (quest == null)
            {
                TolkHelper.Speak("RimWorldAccess.Quests.Menu.QuestNoLongerAvailable".Loc());
                return;
            }

            isActive = true;
            currentTab = QuestMenuHelper.GetTabForQuest(quest);
            devShowAll = false;
            devShowDebugInfo = false;
            PendingSelectQuest = quest;
            // Unlike Open(), this opener runs outside the window: without its half of the
            // MainTabWindowLink pairing, the link's reconcile reads "state active, window closed"
            // and closes the session again before QuestMenuScope can push.
            Shell.MainTabWindowLink.EnsureTabOpen(Shell.MainTabWindowLink.Quests);
        }

        /// <summary>
        /// Closes the quest menu. Pass <paramref name="announce"/> false to close silently when
        /// another flow is taking over the screen (e.g. the Archonexus relocation chain) and the
        /// "Quest menu closed" line would just step on that flow's own announcement.
        /// </summary>
        public static void Close(bool announce = true)
        {
            isActive = false;
            CleanupRewardMenu();
            if (announce)
                TolkHelper.Speak("RimWorldAccess.Quests.Menu.Closed".Loc());
            Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Quests);
        }

        /// <summary>Advances or retreats the current tab by one, wrapping. Pure data mutation; QuestMenuScope rebuilds and announces.</summary>
        public static void AdvanceTab(int direction)
        {
            currentTab = (QuestsTab)(((int)currentTab + direction + 3) % 3);
        }

        /// <summary>Accepts <paramref name="quest"/> if available, handling the multi-choice and RequiresAccepter cases.</summary>
        public static void AcceptQuest(Quest quest)
        {
            if (currentTab != QuestsTab.Available)
            {
                TolkHelper.Speak("RimWorldAccess.Quests.Action.CannotAccept".Loc(), SpeechPriority.High);
                return;
            }

            if (quest.State != QuestState.NotYetAccepted)
            {
                TolkHelper.Speak("RimWorldAccess.Quests.Action.NotAvailableToAccept".Loc(), SpeechPriority.High);
                return;
            }

            AcceptanceReport canAccept = QuestUtility.CanAcceptQuest(quest);
            if (!canAccept.Accepted)
            {
                TolkHelper.Speak("RimWorldAccess.Quests.Action.CannotAcceptReason".Loc(canAccept.Reason), SpeechPriority.High);
                return;
            }

            if (QuestRewardHelper.HasMultipleChoices(quest))
            {
                OpenRewardChoiceMenu(quest);
                return;
            }

            if (quest.RequiresAccepter)
            {
                AcceptQuestWithPawnSelection(quest, null);
                return;
            }

            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            quest.Accept(null);
            TolkHelper.Speak("RimWorldAccess.Quests.Action.AcceptedQuest".Loc(quest.name.StripTags()));
            PostQuestListChangeHook?.Invoke();
        }

        /// <summary>
        /// Dismisses or resumes <paramref name="quest"/>, or deletes it when Historical. Every
        /// dismiss call site shares this one method, and so the MUTATION-C markers below.
        /// </summary>
        public static void ToggleDismissQuest(Quest quest)
        {
            if (quest.Historical)
            {
                // MUTATION-C: mirrors MainTabWindow_Quests.DoDismissButton's Historical branch
                // (decompiled ~513-518) -- vanilla writes hiddenInUI directly with no gated setter.
                quest.hiddenInUI = true;
                TolkHelper.Speak("RimWorldAccess.Quests.Action.DeletedQuest".Loc(quest.name.StripTags()));
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            else
            {
                // MUTATION-C: mirrors MainTabWindow_Quests.DoDismissButton's dismissed toggle +
                // subquest cascade (decompiled ~520-524) -- vanilla writes both fields directly
                // with no gated setter.
                quest.dismissed = !quest.dismissed;
                foreach (Quest subquest in quest.GetSubquests())
                {
                    subquest.dismissed = quest.dismissed;
                }
                string actionKey = quest.dismissed
                    ? "RimWorldAccess.Quests.Action.DismissedQuest"
                    : "RimWorldAccess.Quests.Action.ResumedQuest";
                TolkHelper.Speak(actionKey.Loc(quest.name.StripTags()));
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            PostQuestListChangeHook?.Invoke();
        }

        /// <summary>
        /// Accepts a quest with a specific reward choice. RequiresAccepter quests defer
        /// <see cref="QuestPart_Choice.Choose"/> until a colonist is confirmed (vanilla's own
        /// preAcceptAction deferral, MainTabWindow_Quests.cs:1021-1025), so backing out of accepter
        /// selection leaves the reward choice uncommitted.
        /// </summary>
        internal static void AcceptQuestWithChoice(Quest quest, QuestPart_Choice choicePart,
            QuestPart_Choice.Choice choice)
        {
            // Mirrors MainTabWindow_Quests.DoChoices' per-choice RequiresAccepter derivation
            // (~992-1020): parts belonging exclusively to OTHER, unchosen choices are excluded
            // first, because Quest.RequiresAccepter scans every part regardless of owner and would
            // wrongly force accepter selection for a choice whose own parts need none.
            var remainingParts = new List<QuestPart>(quest.PartsListForReading);
            foreach (QuestPart_Choice.Choice otherChoice in choicePart.choices)
            {
                if (otherChoice == choice)
                    continue;
                foreach (QuestPart part in otherChoice.questParts)
                {
                    if (!choice.questParts.Contains(part))
                        remainingParts.Remove(part);
                }
            }
            bool requiresAccepter = remainingParts.Any(p => p.RequiresAccepter);

            if (requiresAccepter)
            {
                AcceptQuestWithPawnSelection(quest, () => choicePart.Choose(choice));
                return;
            }

            choicePart.Choose(choice);
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            quest.Accept(null);
            string rewardDesc = QuestRewardHelper.BuildRewardDescription(choice.rewards);
            TolkHelper.Speak("RimWorldAccess.Quests.Action.AcceptedWithRewards".Loc(rewardDesc));

            PostQuestListChangeHook?.Invoke();
        }

        /// <summary>
        /// Opens the pawn-selection float menu for RequiresAccepter quests, closing QuestMenuState
        /// first to avoid routing conflicts. <paramref name="preAcceptAction"/> runs immediately
        /// before <see cref="Quest.Accept"/> once a colonist is confirmed and never on back-out,
        /// mirroring vanilla's own preAcceptAction parameter.
        /// </summary>
        // MUTATION-C: the royal-favor confirmation gate below mirrors
        // MainTabWindow_Quests.AcceptQuestByInterface's requiresAccepter branch (decompiled
        // ~1356-1447) verbatim -- the accepter float menu and its warning dialog are private UI
        // with no callable vehicle, so the branch conditions and vanilla translation keys
        // (RoyalIncapableOfSocial / RoyalWithConceitedTrait / RoyalWithTraitAffectingPsylinkNegatively /
        // QuestGivesRoyalFavor / WantToContinue / Confirm / GoBack) are copied verbatim, including the
        // CanPawnAcceptQuest recheck inside the option action.
        private static void AcceptQuestWithPawnSelection(Quest quest, Action preAcceptAction)
        {
            var eligiblePawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoSuspended
                .Where(p => QuestUtility.CanPawnAcceptQuest(p, quest))
                .ToList();

            if (eligiblePawns.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Quests.Action.NoEligibleColonists".Loc(), SpeechPriority.High);
                return;
            }

            var options = new List<FloatMenuOption>();
            foreach (Pawn pawn in eligiblePawns)
            {
                Pawn localPawn = pawn;
                string label = localPawn.LabelShort;
                if (localPawn.royalty != null && localPawn.royalty.AllTitlesInEffectForReading.Any())
                {
                    label = "RimWorldAccess.Quests.Action.PawnWithTitle".Translate(
                        label,
                        localPawn.royalty.MostSeniorTitle.def.GetLabelFor(localPawn));
                }

                options.Add(new FloatMenuOption(label, () =>
                {
                    if (!QuestUtility.CanPawnAcceptQuest(localPawn, quest))
                        return;

                    void AcceptAction()
                    {
                        SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                        preAcceptAction?.Invoke();
                        quest.Accept(localPawn);
                        TolkHelper.Speak("RimWorldAccess.Quests.Action.AcceptedWithPawn".Loc(localPawn.LabelShort));
                    }

                    QuestPart_GiveRoyalFavor royalFavorPart = quest.PartsListForReading
                        .OfType<QuestPart_GiveRoyalFavor>().FirstOrDefault();
                    if (royalFavorPart != null && royalFavorPart.giveToAccepter)
                    {
                        IEnumerable<Trait> conceitedTraits = RoyalTitleUtility.GetConceitedTraits(localPawn);
                        IEnumerable<Trait> psylinkTraits = RoyalTitleUtility.GetTraitsAffectingPsylinkNegatively(localPawn);
                        bool totallyDisabled = localPawn.skills.GetSkill(SkillDefOf.Social).TotallyDisabled;
                        bool hasConceited = conceitedTraits.Any();
                        bool hurtsPsylink = !localPawn.HasPsylink && psylinkTraits.Any();

                        if (totallyDisabled || hasConceited || hurtsPsylink)
                        {
                            NamedArgument pawnArg = localPawn.Named("PAWN");
                            NamedArgument factionArg = royalFavorPart.faction.Named("FACTION");
                            TaggedString warningText = "QuestGivesRoyalFavor".Translate(pawnArg, factionArg);
                            if (totallyDisabled)
                                warningText += "\n\n" + "RoyalIncapableOfSocial".Translate(pawnArg, factionArg);
                            if (hasConceited)
                                warningText += "\n\n" + "RoyalWithConceitedTrait".Translate(pawnArg, factionArg,
                                    conceitedTraits.Select(t => t.Label).ToCommaList(useAnd: true));
                            if (hurtsPsylink)
                                warningText += "\n\n" + "RoyalWithTraitAffectingPsylinkNegatively".Translate(pawnArg, factionArg,
                                    psylinkTraits.Select(t => t.Label).ToCommaList(useAnd: true));
                            warningText += "\n\n" + "WantToContinue".Translate();

                            Find.WindowStack.Add(new Dialog_MessageBox(warningText, "Confirm".Translate(), AcceptAction, "GoBack".Translate()));
                            return;
                        }
                    }

                    AcceptAction();
                }));
            }

            // Close the quest menu BEFORE opening the float menu, or the two contend for keys.
            Close();
            TolkHelper.Speak("RimWorldAccess.Quests.Action.SelectColonist".Loc());
            WindowlessFloatMenuState.Open(options, false);
        }

        /// <summary>Builds <paramref name="quest"/>'s dev-mode Buttons-region entries, mirroring MainTabWindow_Quests' embedded dev widgets.</summary>
        internal static bool DevAcceptButtonVisible(Quest quest) => Prefs.DevMode && quest.State == QuestState.NotYetAccepted;
        internal static bool DevToggleButtonsVisible => Prefs.DevMode && !DebugSettings.godMode;

        /// <summary>
        /// Mirrors MainTabWindow_Quests' "DEV: Accept instantly" button (~611-625): picks a random
        /// reward choice, accepts with a random eligible colonist, and un-dismisses the quest.
        /// </summary>
        public static void DevAcceptInstantly(Quest quest)
        {
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();

            QuestPart_Choice choicePart = QuestRewardHelper.GetChoicePart(quest);
            if (choicePart != null && choicePart.choices.Any())
            {
                choicePart.Choose(choicePart.choices.RandomElement());
            }

            quest.Accept(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoSuspended
                .Where(p => QuestUtility.CanPawnAcceptQuest(p, quest)).RandomElementWithFallback());

            // MUTATION-C: mirrors MainTabWindow_Quests' "DEV: Accept instantly"
            // dismissed clear (decompiled ~623) -- vanilla writes the field
            // directly with no gated setter.
            quest.dismissed = false;

            TolkHelper.Speak("RimWorldAccess.Quests.Action.AcceptedQuest".Loc(quest.name.StripTags()));
            PostQuestListChangeHook?.Invoke();
        }

        /// <summary>Toggles the DEV "Show all" filter; the scope's next refresh picks up the new value.</summary>
        public static void ToggleDevShowAll()
        {
            devShowAll = !devShowAll;
            TolkHelper.SpeakData(DevToggleAnnouncement("DEV: Show all", devShowAll));
        }

        /// <summary>Toggles the DEV "Show debug info" filter; the Details region is rebuilt live from the Quests cursor.</summary>
        public static void ToggleDevShowDebugInfo()
        {
            devShowDebugInfo = !devShowDebugInfo;
            TolkHelper.SpeakData(DevToggleAnnouncement("DEV: Show debug info", devShowDebugInfo));
        }

        private static string DevToggleAnnouncement(string label, bool on)
        {
            string state = (on
                ? "RimWorldAccess.Shell.State.Checked"
                : "RimWorldAccess.Shell.State.Unchecked").Translate().ToString();
            return label + ". " + state + ".";
        }

        // The reward-choice float menu and its item-inspection sub-mode, shared with
        // QuestRewardOverlayScope and FloatMenuOverlayScope.

        /// <summary>Opens a float menu of reward choices for a multi-choice quest; this state stays active while it is open.</summary>
        private static void OpenRewardChoiceMenu(Quest quest)
        {
            QuestPart_Choice choicePart = QuestRewardHelper.GetChoicePart(quest);
            if (choicePart == null || choicePart.choices.Count < 2)
                return;

            rewardMenuQuest = quest;
            rewardChoices = choicePart.choices;
            hasActiveRewardMenu = true;
            isInItemInspectionMenu = false;

            choiceInspectables = new List<List<(Thing thing, Faction faction)>>();
            var options = new List<FloatMenuOption>();

            for (int i = 0; i < rewardChoices.Count; i++)
            {
                int choiceIdx = i;
                var choice = rewardChoices[choiceIdx];
                string rewardDesc = QuestRewardHelper.BuildRewardDescription(choice.rewards);
                string label = rewardDesc;

                var inspectables = new List<(Thing thing, Faction faction)>();
                foreach (Reward reward in choice.rewards)
                {
                    if (reward is Reward_Items rewardItems && rewardItems.ItemsListForReading != null)
                    {
                        foreach (Thing item in rewardItems.ItemsListForReading)
                        {
                            if (item != null)
                                inspectables.Add((item, null));
                        }
                    }
                    else if (reward is Reward_Goodwill rg && rg.faction != null)
                    {
                        inspectables.Add((null, rg.faction));
                    }
                    else if (reward is Reward_RoyalFavor rf && rf.faction != null)
                    {
                        inspectables.Add((null, rf.faction));
                    }
                    else if (reward is Reward_Pawn rp && rp.pawn != null && !rp.detailsHidden)
                    {
                        inspectables.Add((rp.pawn, null));
                    }
                }
                choiceInspectables.Add(inspectables);

                options.Add(new FloatMenuOption(label, () =>
                {
                    AcceptQuestWithChoice(rewardMenuQuest, choicePart, choice);
                    CleanupRewardMenu();
                }));
            }

            TolkHelper.Speak("RimWorldAccess.Quests.RewardChoice.Prompt".Loc());
            WindowlessFloatMenuState.Open(options, false);
        }

        /// <summary>Clears the reward-choice float-menu state; called however that menu closes.</summary>
        public static void CleanupRewardMenu()
        {
            hasActiveRewardMenu = false;
            isInItemInspectionMenu = false;
            rewardMenuQuest = null;
            rewardChoices = null;
            choiceInspectables = null;
            currentInspectionItems = null;
            savedChoiceIndex = -1;
        }

        /// <summary>Opens the item-inspection sub-menu for the selected reward choice (Alt+I in the reward-choice menu).</summary>
        public static void OpenItemInspectionForCurrentChoice()
        {
            if (choiceInspectables == null) return;

            int choiceIdx = WindowlessFloatMenuState.SelectedIndex;
            if (choiceIdx < 0 || choiceIdx >= choiceInspectables.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Quests.RewardChoice.NoItemsToInspect".Loc());
                return;
            }

            var inspectables = choiceInspectables[choiceIdx];
            if (inspectables.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Quests.RewardChoice.NoItemsToInspect".Loc());
                return;
            }

            // Consolidate by label: five stacks of plasteel become one "375x Plasteel" entry.
            var consolidated = new List<(string label, Thing thing, Faction faction)>();
            var grouped = new Dictionary<string, (int totalCount, Thing representative)>();
            var factionEntries = new List<(string label, Faction faction)>();

            foreach (var inspectable in inspectables)
            {
                if (inspectable.thing != null)
                {
                    string key = inspectable.thing.LabelNoCount;
                    if (grouped.ContainsKey(key))
                    {
                        var existing = grouped[key];
                        grouped[key] = (existing.totalCount + inspectable.thing.stackCount, existing.representative);
                    }
                    else
                    {
                        grouped[key] = (inspectable.thing.stackCount, inspectable.thing);
                    }
                }
                else if (inspectable.faction != null)
                {
                    factionEntries.Add((inspectable.faction.Name, inspectable.faction));
                }
            }

            foreach (var kvp in grouped)
            {
                int count = kvp.Value.totalCount;
                Thing rep = kvp.Value.representative;
                string itemName = rep.LabelNoCount.CapitalizeFirst();
                string itemLabel = count > 1 ? "RimWorldAccess.Quests.RewardChoice.ItemWithCount".Loc(count, itemName).ToString() : itemName;
                consolidated.Add((itemLabel, rep, null));
            }
            foreach (var entry in factionEntries)
            {
                consolidated.Add((entry.label, null, entry.faction));
            }

            // Re-checked after consolidation: a single item opens its info card directly.
            if (consolidated.Count == 1)
            {
                var item = consolidated[0];
                if (item.thing != null)
                    Find.WindowStack.Add(new Dialog_InfoCard(item.thing));
                else if (item.faction != null)
                    Find.WindowStack.Add(new Dialog_InfoCard(item.faction));
                return;
            }

            if (consolidated.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Quests.RewardChoice.NoItemsToInspect".Loc());
                return;
            }

            savedChoiceIndex = choiceIdx;
            isInItemInspectionMenu = true;

            currentInspectionItems = new List<(Thing thing, Faction faction)>();
            var options = new List<FloatMenuOption>();
            foreach (var entry in consolidated)
            {
                currentInspectionItems.Add((entry.thing, entry.faction));
                options.Add(new FloatMenuOption(entry.label, () => { }));
            }

            WindowlessFloatMenuState.Close();
            TolkHelper.Speak("RimWorldAccess.InfoCard.ChooseItemToInspect".Loc());
            WindowlessFloatMenuState.Open(options, false);
        }

        /// <summary>Opens the info card for the selected item in the inspection sub-menu (Enter in item-inspection mode).</summary>
        public static void InspectCurrentItem()
        {
            if (currentInspectionItems == null) return;

            int idx = WindowlessFloatMenuState.SelectedIndex;
            if (idx < 0 || idx >= currentInspectionItems.Count)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }

            var item = currentInspectionItems[idx];
            if (item.thing != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(item.thing));
            }
            else if (item.faction != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(item.faction));
            }
            else
            {
                InfoCardState.SpeakNoInfoCardAvailable();
            }
        }

        /// <summary>Returns from the item-inspection sub-menu to the reward-choice float menu (Escape).</summary>
        public static void ReturnToRewardChoiceMenu()
        {
            isInItemInspectionMenu = false;
            currentInspectionItems = null;

            WindowlessFloatMenuState.Close();

            // Rebuild and re-open the choice menu at the saved position.
            if (rewardMenuQuest == null || rewardChoices == null)
            {
                CleanupRewardMenu();
                TolkHelper.Speak("RimWorldAccess.Quests.RewardChoice.BackToList".Loc());
                PostQuestListChangeHook?.Invoke();
                return;
            }

            QuestPart_Choice choicePart = QuestRewardHelper.GetChoicePart(rewardMenuQuest);
            if (choicePart == null)
            {
                CleanupRewardMenu();
                TolkHelper.Speak("RimWorldAccess.Quests.RewardChoice.BackToList".Loc());
                PostQuestListChangeHook?.Invoke();
                return;
            }

            var options = new List<FloatMenuOption>();
            for (int i = 0; i < rewardChoices.Count; i++)
            {
                int choiceIdx = i;
                var choice = rewardChoices[choiceIdx];
                string rewardDesc = QuestRewardHelper.BuildRewardDescription(choice.rewards);
                string label = rewardDesc;

                options.Add(new FloatMenuOption(label, () =>
                {
                    AcceptQuestWithChoice(rewardMenuQuest, choicePart, choice);
                    CleanupRewardMenu();
                }));
            }

            int restoreIndex = savedChoiceIndex >= 0 && savedChoiceIndex < options.Count
                ? savedChoiceIndex : 0;
            WindowlessFloatMenuState.Open(options, false, restoreIndex);
        }
    }
}
