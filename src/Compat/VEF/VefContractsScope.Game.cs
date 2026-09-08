using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Vanilla Expanded Framework's
    /// <c>VEF.Storyteller.Window_Contracts</c> (the master/detail contract board opened from a
    /// comms console), registered through <see cref="ScopeForWindow.RegisterHierarchy"/> via
    /// <see cref="VefContractsCompat"/>. All reflection lives in the compat facade; this scope
    /// only reads its typed methods, matching the <see cref="VfAssignSeatsScope"/>/
    /// <see cref="VefHireScope"/> facade split.
    ///
    /// A master/detail scope: region 0 lists the available contracts (VEF's own selection kept in
    /// sync via <see cref="VefContractsCompat.Select"/>), region 1 is a read-only detail pane for
    /// the currently selected one. The detail pane REUSES the mod's existing vanilla-quest
    /// accessibility helpers -- <see cref="QuestMenuHelper.BuildDetailContentLines"/> and
    /// <see cref="QuestRewardHelper.BuildCompactRewardSummary"/> -- since a VEF QuestInfo just
    /// wraps a vanilla <see cref="Quest"/>; only the VEF-specific cost and unmet-requirement lines
    /// are built here.
    ///
    /// <see cref="CaptureWindowButtons"/> is false: the window draws its own per-quest Accept
    /// buttons, so blanket capture would collect them unpredictably. <see cref="DeclaredActions"/>
    /// supplies our own Accept/Close instead, both riding vanilla vehicles.
    /// </summary>
    internal sealed class VefContractsScope : ScreenScope
    {
        private const int ListRegion = 0;
        private const int DetailRegion = 1;

        private readonly Window dialog;
        private List<object> quests = new List<object>();
        private List<DetailLine> detailLines = new List<DetailLine>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public VefContractsScope(Window w)
        {
            dialog = w;
            // First-letter mnemonic on "AcceptQuest".Translate(). Unguarded: AcceptSelected
            // already no-ops (re-announces) when nothing is selected.
            Claim("vefContracts.accept", e => AcceptSelected());
        }

        public override string Name => "vef-contracts";

        /// <summary>Contract names are worth searching (table-model T4).</summary>
        protected override bool EnableTypeahead => true;

        protected internal override Window OwnedWindow => dialog;

        // ------------------------------------------------------------------
        // ScreenScope content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => 2;

        protected override void RefreshContent()
        {
            quests = VefContractsCompat.Quests(dialog);
            detailLines = BuildDetailLines();
        }

        protected override string ContentRegionName(int region)
        {
            if (region == ListRegion)
                return "RimWorldAccess.Compat.Vef.ContractsListRegion".Translate();
            return "RimWorldAccess.Compat.Vef.ContractsDetailRegion".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            if (region == ListRegion)
                return quests.Count;
            return detailLines.Count == 0 ? 1 : detailLines.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == ListRegion)
            {
                if (index < 0 || index >= quests.Count)
                    return d;
                object qi = quests[index];
                Quest quest = VefContractsCompat.QuestOf(qi);
                d.Label = BuildListRowLabel(qi, quest);
                d.PositionIndex = index + 1;
                d.PositionCount = quests.Count;
                // Single-selection list: every row is a radio button, exactly one selected -- the one
                // whose detail the detail region shows. Consistent role across the whole region.
                d.Role = ElementRole.RadioButton;
                d.Selected = ReferenceEquals(qi, VefContractsCompat.Selected(dialog));
                return d;
            }

            if (detailLines.Count == 0)
            {
                d.Label = "RimWorldAccess.Compat.Vef.ContractsNoSelection".Translate();
                d.ReadOnly = true;
                return d;
            }

            DetailLine line = detailLines[index];
            d.Label = line.Text;
            if (line.InfoCardThing != null || line.InfoCardFaction != null)
            {
                d.Hint = "RimWorldAccess.Compat.Vef.ContractsInfoCardHint".Translate();
                // Enter opens the info card on these rows, so they keep Enter (DefaultAcceptInertness).
                d.KeepsAccept = true;
            }
            else
                d.ReadOnly = true;
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == ListRegion)
            {
                if (index < 0 || index >= quests.Count)
                    return;
                object qi = quests[index];
                VefContractsCompat.Select(dialog, qi);
                RefreshModel();
                TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vef.ContractsSelected".Translate(
                    VefContractsCompat.QuestOf(qi)?.name.StripTags() ?? "", detailLines.Count));
                return;
            }

            if (detailLines.Count == 0)
            {
                AnnounceCurrentItem();
                return;
            }
            DetailLine line = detailLines[index];
            if (line.InfoCardThing != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(line.InfoCardThing));
                return;
            }
            if (line.InfoCardFaction != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(line.InfoCardFaction));
                return;
            }
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // Detail + list builders.
        // ------------------------------------------------------------------

        private List<DetailLine> BuildDetailLines()
        {
            var result = new List<DetailLine>();
            object sel = VefContractsCompat.Selected(dialog);
            if (sel == null)
                return result;
            Quest quest = VefContractsCompat.QuestOf(sel);
            if (quest == null)
                return result;

            // VEF cost first (the vanilla quest-detail helper doesn't know about it).
            string cost = VefContractsCompat.CostText(sel);
            if (!string.IsNullOrEmpty(cost))
                result.Add(new DetailLine("RimWorldAccess.Compat.Vef.ContractsCost".Translate(cost)));

            // VEF unmet acceptance requirements (only meaningful before acceptance).
            if (!quest.EverAccepted)
            {
                foreach (string req in VefContractsCompat.UnmetRequirements(dialog))
                {
                    if (!string.IsNullOrEmpty(req))
                        result.Add(new DetailLine("RimWorldAccess.Compat.Vef.ContractsRequirement".Translate(req)));
                }
            }

            // Reuse the vanilla quest detail (difficulty, time, description lines, reward rows w/ info-card targets).
            result.AddRange(QuestMenuHelper.BuildDetailContentLines(quest));
            return result;
        }

        private string BuildListRowLabel(object questInfo, Quest quest)
        {
            var segments = new List<string>();
            segments.Add(quest?.name.StripTags() ?? "");

            Faction asker = VefContractsCompat.AskerFaction(questInfo);
            if (asker != null)
                segments.Add("RimWorldAccess.Compat.Vef.ContractsFrom".Translate(asker.Name));

            string rewards = quest != null ? QuestRewardHelper.BuildCompactRewardSummary(quest) : "";
            if (!string.IsNullOrEmpty(rewards))
                segments.Add("RimWorldAccess.Compat.Vef.ContractsRewards".Translate(rewards));

            string cost = VefContractsCompat.CostText(questInfo);
            if (!string.IsNullOrEmpty(cost))
                segments.Add("RimWorldAccess.Compat.Vef.ContractsCost".Translate(cost));

            return string.Join(". ", segments);
        }

        // ------------------------------------------------------------------
        // Buttons region.
        // ------------------------------------------------------------------

        /// <summary>The window draws its own per-quest Accept buttons; blanket capture would collect them unpredictably.</summary>
        protected override bool CaptureWindowButtons => false;

        /// <summary>Shift+Enter presses Accept quest from anywhere on this screen: the one-chord proceed. No selection means the row is absent from DeclaredActions, so the arm/Shift+Enter path falls through to "No default button" -- matching the sighted UI, which hides the button too.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "vefContracts.accept"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                object sel = VefContractsCompat.Selected(dialog);
                if (sel != null)
                    actions.Add(new ScreenAction("AcceptQuest".Translate(), AcceptSelected, "vefContracts.accept"));
                actions.Add(new ScreenAction("CloseButton".Translate(), delegate { dialog.Close(); }, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        private void AcceptSelected()
        {
            object sel = VefContractsCompat.Selected(dialog);
            if (sel == null)
            {
                AnnounceCurrentItem();
                return;
            }
            Quest quest = VefContractsCompat.QuestOf(sel);
            if (!VefContractsCompat.CanAccept(quest))
            {
                // Mirror the window's own gate feedback without reimplementing the commit.
                Messages.Message("RimWorldAccess.Compat.Vef.ContractsCannotAccept".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            VefContractsCompat.AcceptSelected(dialog);
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        protected override string ComposeOpenAnnouncement()
        {
            return (string)"RimWorldAccess.Compat.Vef.ContractsOpen".Translate(quests.Count);
        }
    }
}
