using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Character Development's Characters main tab, registered by
    /// <see cref="WqModule"/>; all reflection lives in <see cref="WqCompat"/>. The tab's center
    /// panel is a physics simulation: reward bubbles drawn with raw textures, dragged and
    /// clicked through raw mouse events — nothing any capture can see. This scope presents the
    /// same reward nodes as rows; Enter claims through the tab's own gated <c>ClaimReward</c>,
    /// which speaks its own reject messages and opens the recipient-picker dialog (a modal the
    /// generic reader handles).
    ///
    /// Three content regions: points (the progress bar's numbers and the tab's own explanation
    /// texts), rewards (one row per bubble), and the characters-with-wants list (Enter jumps to
    /// the pawn and opens the mod's Wants and Quirks inspect tab, as the sighted row click does).
    /// </summary>
    internal sealed class WqCharactersTabScope : ScreenScope
    {
        private const int PointsRegion = 0;
        private const int RewardsRegion = 1;
        private const int PawnsRegion = 2;

        private const int CharacterPointsRow = 0;
        private const int ProgressDescRow = 1;
        private const int QuirksInfoRow = 2;
        private const int PointsRowCount = 3;

        private readonly Window window;
        private readonly List<object> rewardNodes = new List<object>();
        private readonly List<Pawn> pawnsWithWants = new List<Pawn>();

        public WqCharactersTabScope(Window w)
        {
            window = w;
        }

        public override string Name => "wq-characters-tab";

        protected internal override Window OwnedWindow => window;

        protected override bool EnableTypeahead => true;

        /// <summary>The tab draws no ButtonText of its own (only dev-mode icon buttons).</summary>
        protected override bool CaptureWindowButtons => false;

        protected override string ComposeOpenAnnouncement()
        {
            return CompatText.JoinSentences(new List<string>
            {
                CompatText.ModText("WQ_CharacterPoints"),
                "RimWorldAccess.Compat.Wq.PointsProgress".Translate(
                    WqCompat.CharacterPoints(), WqCompat.PointsNeededForReward()),
                CompatText.ModArgs("WQ_AvailableRewards", WqCompat.RewardPoints()),
            });
        }

        protected override void RefreshContent()
        {
            rewardNodes.Clear();
            rewardNodes.AddRange(WqCompat.RewardNodes());
            pawnsWithWants.Clear();
            // The tab's own list: spawned player pawns that can have wants and have any.
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                List<Pawn> pawns = maps[i].mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int j = 0; j < pawns.Count; j++)
                {
                    if (WqCompat.CanHaveWants(pawns[j]) && WqCompat.ActiveWantCount(pawns[j]) > 0)
                    {
                        pawnsWithWants.Add(pawns[j]);
                    }
                }
            }
        }

        protected override int ContentRegionCount => 3;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case PointsRegion:
                    return CompatText.ModText("WQ_CharacterPoints");
                case RewardsRegion:
                    return "RimWorldAccess.Compat.Wq.RewardsRegion".Translate();
                default:
                    return CompatText.ModText("WQ_CharactersWithWants");
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case PointsRegion:
                    return PointsRowCount;
                case RewardsRegion:
                    return rewardNodes.Count;
                default:
                    return pawnsWithWants.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == PointsRegion)
            {
                d.ReadOnly = true;
                switch (index)
                {
                    case CharacterPointsRow:
                        d.Label = "RimWorldAccess.Compat.Wq.PointsProgress".Translate(
                            WqCompat.CharacterPoints(), WqCompat.PointsNeededForReward());
                        break;
                    case ProgressDescRow:
                        d.Label = CompatText.Flatten(CompatText.ModText("WQ_ProgressBarDesc"));
                        break;
                    case QuirksInfoRow:
                        d.Label = CompatText.Flatten(CompatText.ModText("WQ_QuirksInfoText"));
                        break;
                }
                return d;
            }

            if (region == RewardsRegion)
            {
                if (index < 0 || index >= rewardNodes.Count)
                {
                    return d;
                }
                object node = rewardNodes[index];
                d.Label = WqCompat.NodeLabel(node);
                d.Role = ElementRole.Button;
                d.Value = RarityWord(WqCompat.NodeRarity(node));
                d.Extras = CompatText.Flatten(WqCompat.NodeDescription(node));
                if (WqCompat.RewardPoints() <= 0)
                {
                    d.Disabled = true;
                    d.Extras = CompatText.JoinSentences(new List<string>
                    {
                        CompatText.ModText("WQ_NotEnoughRewardPoints"), d.Extras,
                    });
                }
                return d;
            }

            if (index < 0 || index >= pawnsWithWants.Count)
            {
                return d;
            }
            Pawn pawn = pawnsWithWants[index];
            d.Label = pawn.LabelShortCap;
            d.Role = ElementRole.Button;
            d.Value = CompatText.ModArgs("WQ_WantsCount", WqCompat.ActiveWantCount(pawn));
            d.Hint = "RimWorldAccess.Compat.Wq.PawnRowHint".Translate();
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == RewardsRegion)
            {
                if (index < 0 || index >= rewardNodes.Count)
                {
                    return;
                }
                // The tab's own gated claim: its reject messages speak on failure; on success
                // the recipient dialog opens and this list regenerates.
                WqCompat.ClaimReward(window, rewardNodes[index]);
                RefreshModel();
                return;
            }
            if (region == PawnsRegion && index >= 0 && index < pawnsWithWants.Count)
            {
                Pawn pawn = pawnsWithWants[index];
                // MUTATION-C: mirrors MainTabWindow_Characters.DrawLeftPanel's pawn-row click
                // (jump-select then open the mod's inspect tab); the handler is inline with no
                // callable method behind it.
                CameraJumper.TryJumpAndSelect(pawn);
                InspectPaneUtility.OpenTab(WqCompat.InspectTabType);
            }
        }

        private static string RarityWord(int rarity)
        {
            switch (rarity)
            {
                case 3:
                    return "RimWorldAccess.Compat.Wq.RarityLegendary".Translate();
                case 2:
                    return "RimWorldAccess.Compat.Wq.RarityRare".Translate();
                case 1:
                    return "RimWorldAccess.Compat.Wq.RarityUncommon".Translate();
                default:
                    return "RimWorldAccess.Compat.Wq.RarityCommon".Translate();
            }
        }
    }
}
