using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for vanilla's <see cref="Dialog_RewardPrefsConfig"/>
    /// (the "Choose Rewards..." dialog on the Quests tab) — DLC coverage
    /// remediation, finding G2. Reachable via the real
    /// <c>MainTabWindow_Quests.DoRewardsPrefsButton</c> click; our own
    /// accessible Quests screen never opens this window either — it redirects
    /// to the accessible quest screen's own Reward preferences content region
    /// (see <c>QuestMenuScope</c>), so this
    /// scope's job is purely to cover the real dialog whenever it IS opened
    /// (mouse, another mod, or a future accessible path).
    ///
    /// One content region ("Choose rewards"), one Checkbox row per faction
    /// preference — the SAME enumeration and filters vanilla's own
    /// <c>DoWindowContents</c> uses (<c>AllFactionsVisibleInViewOrder</c>,
    /// skip <c>IsPlayer</c>, <c>HasRoyalTitles</c> / <c>CanEverGiveGoodwillRewards</c>),
    /// reused from <see cref="QuestRewardHelper.GetRewardPreferenceItems"/>
    /// rather than re-derived, so the two surfaces can never drift on WHICH
    /// rows exist. Row labels are rebuilt here (not <c>RewardPrefItem.Label</c>,
    /// which bakes the checked/unchecked word into the string for a different
    /// caller's phrasing) from vanilla's OWN "AcceptRoyalFavor"/"AcceptGoodwill"
    /// keys so the label matches what this specific dialog actually renders;
    /// the checked state rides the Checkbox role's own Check fragment instead
    /// (the toggle doctrine — never speak the state twice).
    ///
    /// Toggling rides <see cref="QuestRewardHelper.ToggleRewardPreference"/>,
    /// already carrying its own MUTATION-C marker (mirrors
    /// <c>Dialog_RewardPrefsConfig.DoWindowContents</c>'s direct
    /// <c>Widgets.Checkbox(ref faction.allowXRewards)</c> — vanilla has no
    /// gated setter for either bool). The Buttons region captures the
    /// dialog's own Close button (default <see cref="ScreenScope.CaptureWindowButtons"/>;
    /// the goodwill relation label drawn beside each checkbox is
    /// <c>Widgets.Label</c>, not a button, so nothing else is over-captured).
    /// </summary>
    public sealed class RewardPrefsScope : ScreenScope
    {
        private const float RowHeight = 45f;
        private const int RewardsRegion = 0;

        private static readonly AccessTools.FieldRef<Dialog_RewardPrefsConfig, Vector2> ScrollPositionField =
            AccessTools.FieldRefAccess<Dialog_RewardPrefsConfig, Vector2>("scrollPosition");
        private static readonly Func<Window, float> MarginOf =
            AccessTools.MethodDelegate<Func<Window, float>>(AccessTools.PropertyGetter(typeof(Window), "Margin"));

        private readonly Dialog_RewardPrefsConfig dialog;
        private readonly List<RewardPrefItem> items = new List<RewardPrefItem>();
        private bool announcedOpen;

        public RewardPrefsScope(Dialog_RewardPrefsConfig dialog)
        {
            this.dialog = dialog;
        }

        public override string Name
        {
            get { return "reward-prefs"; }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            TolkHelper.SpeakData("ChooseRewardsDesc".Translate().ToString());
            AnnounceCurrentItem();
        }

        protected override void RefreshContent()
        {
            items.Clear();
            items.AddRange(QuestRewardHelper.GetRewardPreferenceItems());
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "ChooseRewards".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return items.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            RewardPrefItem item = items[index];
            ElementDescription d = new ElementDescription();
            d.Role = ElementRole.Checkbox;
            if (item.Type == RewardPrefType.RoyalFavor)
            {
                d.Label = item.Faction.Name + ": " + "AcceptRoyalFavor".Translate(item.Faction.Named("FACTION")).CapitalizeFirst();
                d.Check = item.Faction.allowRoyalFavorRewards ? CheckState.Checked : CheckState.Unchecked;
                d.Extras = FlattenNewlines("AcceptRoyalFavorDesc".Translate(item.Faction.Named("FACTION")).Resolve().StripTags());
            }
            else
            {
                string relation = "RimWorldAccess.Quests.Pref.RelationPhrase".Translate(
                    item.Faction.PlayerGoodwill.ToStringWithSign(),
                    item.Faction.PlayerRelationKind.GetLabelCap()).ToString();
                d.Label = item.Faction.Name + ": " + "AcceptGoodwill".Translate().CapitalizeFirst() + ". " + relation;
                d.Check = item.Faction.allowGoodwillRewards ? CheckState.Checked : CheckState.Unchecked;
                d.Extras = FlattenNewlines("AcceptGoodwillDesc".Translate(item.Faction.Named("FACTION")).Resolve().StripTags());
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= items.Count)
            {
                return;
            }
            RewardPrefItem item = items[index];
            QuestRewardHelper.ToggleRewardPreference(item);
            RefreshModel();

            bool nowChecked = item.Type == RewardPrefType.RoyalFavor
                ? item.Faction.allowRoyalFavorRewards
                : item.Faction.allowGoodwillRewards;
            ElementDescription d = new ElementDescription();
            d.Check = nowChecked ? CheckState.Checked : CheckState.Unchecked;
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        /// <summary>Vanilla's tooltip strings use literal "\n\n" paragraph breaks; announcements never use newlines as separators.</summary>
        private static string FlattenNewlines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.Replace("\n\n", " ").Replace("\n", " ");
        }

        // Row band top: each drawn row (royal favor and/or goodwill per faction,
        // in AllFactionsVisibleInViewOrder) advances curY by exactly RowHeight
        // (decompiled Dialog_RewardPrefsConfig.cs:74/:92) starting at 0 -- our
        // item ordinal equals the vanilla drawn-row ordinal one-for-one because
        // QuestRewardHelper.GetRewardPreferenceItems walks the same enumeration
        // and predicates in the same order.
        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || Model.RegionIndex != RewardsRegion
                || region.Index < 0 || region.Index >= items.Count)
            {
                return default(Rect);
            }
            if (CountVanillaRows() != items.Count)
            {
                return default(Rect);
            }

            Rect outRect = OutRect();
            float rel = region.Index * RowHeight;
            float y = outRect.y + rel - ScrollPositionField(dialog).y;
            float yMin = Mathf.Max(y, outRect.yMin);
            float yMax = Mathf.Min(y + RowHeight, outRect.yMax);
            if (yMax <= yMin)
            {
                return default(Rect);
            }
            return GuiSpace.ToScreen(new Rect(outRect.x, yMin, outRect.width - 16f, yMax - yMin));
        }

        // One corrective write per settle (never continuous) -- clamps the
        // vanilla scroll position just enough to bring the focused row's band
        // fully inside the visible outRect, mirroring the same Widgets.BeginScrollView
        // geometry FocusedContentRect reads.
        protected override void OnCursorSettled(int region, int index)
        {
            if (region != RewardsRegion || index < 0 || index >= items.Count)
            {
                return;
            }
            Rect outRect = OutRect();
            float rel = index * RowHeight;
            Vector2 scroll = ScrollPositionField(dialog);
            scroll.y = Mathf.Clamp(scroll.y, rel + RowHeight - outRect.height, rel);
            ScrollPositionField(dialog) = scroll;
        }

        /// <summary>
        /// Mirrors Dialog_RewardPrefsConfig.DoWindowContents' outRect derivation
        /// (decompiled :44-48): inRect is the window contracted by its own
        /// Margin (the RechargeSettingsScope precedent); the description label's
        /// height is recomputed under GameFont.Small the same way vanilla's own
        /// Text.CalcHeight call does, then outRect trims CloseButSize.y off the
        /// bottom and 44f + descHeight + 4f off the top.
        /// </summary>
        private Rect OutRect()
        {
            Rect inRect = WindowContentRect();
            GameFont font = Text.Font;
            Text.Font = GameFont.Small;
            float descHeight = Text.CalcHeight("ChooseRewardsDesc".Translate(), inRect.width);
            Text.Font = font;

            Rect outRect = new Rect(inRect);
            outRect.yMax -= Window.CloseButSize.y;
            outRect.yMin += 44f + descHeight + 4f;
            return outRect;
        }

        private Rect WindowContentRect()
        {
            float margin = MarginOf(dialog);
            return new Rect(0f, 0f, dialog.windowRect.width, dialog.windowRect.height)
                .ContractedBy(margin);
        }

        /// <summary>
        /// Cheap recount of vanilla's own row predicates (decompiled :53-93) --
        /// the degrade law: if this ever disagrees with items.Count (a mod
        /// changing faction defs mid-frame, e.g.), the ring returns
        /// default(Rect) rather than point at the wrong row.
        /// </summary>
        private static int CountVanillaRows()
        {
            int count = 0;
            foreach (Faction faction in Find.FactionManager.AllFactionsVisibleInViewOrder)
            {
                if (faction.IsPlayer)
                {
                    continue;
                }
                if (faction.def.HasRoyalTitles)
                {
                    count++;
                }
                if (faction.CanEverGiveGoodwillRewards)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
