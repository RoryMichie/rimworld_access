using System.Collections;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for ISEKAI RPG LEVELING's Window_Mastery: the pawn's weapon
    /// mastery ledger. Two read-only regions, mastered weapons (XP descending, the window's own
    /// order) and unmastered ones (alphabetical), each row carrying what the window's hover
    /// tooltip shows: tier, XP toward the next tier, and the bonuses in force. The god-mode dev
    /// buttons are ordinary ButtonText calls, so the Buttons region captures them as drawn.
    /// </summary>
    internal sealed class IsekaiMasteryScope : ScreenScope
    {
        private sealed class Row
        {
            public ThingDef Def;
            public int Xp;
            public object Tier;
            public bool Equipped;
        }

        private readonly Window window;
        private readonly List<Row> mastered = new List<Row>();
        private readonly List<ThingDef> unmastered = new List<ThingDef>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private object tracker;
        private bool announcedOpen;

        public IsekaiMasteryScope(Window window)
        {
            this.window = window;
        }

        public override string Name => "isekai-mastery";

        protected internal override Window OwnedWindow => window;

        protected override bool EnableTypeahead => true;

        protected override int ContentRegionCount => 2;

        protected override string ContentRegionName(int region)
        {
            return region == 0
                ? "RimWorldAccess.Compat.Isekai.MasteredRegion".Translate(mastered.Count)
                : "RimWorldAccess.Compat.Isekai.UnmasteredRegion".Translate(unmastered.Count);
        }

        private bool Ready => IsekaiWindowCompat.MasteryGate.Ensure() && IsekaiForgeCompat.Gate.Ensure() && IsekaiCompat.CoreGate.Ensure();

        protected override void RefreshContent()
        {
            mastered.Clear();
            unmastered.Clear();
            tracker = null;
            if (!Ready)
                return;
            Pawn pawn = IsekaiWindowCompat.MasteryPawn(window);
            object comp = IsekaiCompat.ComponentOf(pawn);
            if (pawn == null || comp == null)
                return;
            tracker = IsekaiCompat.WeaponMasteryOf(comp);
            if (tracker == null)
                return;
            string equipped = pawn.equipment?.Primary?.def.defName;

            // The window's own split: any XP at all is "mastered"; sort mirrors DoWindowContents.
            IList defs = IsekaiWindowCompat.AllWeaponDefs(window);
            if (defs == null)
                return;
            foreach (object obj in defs)
            {
                if (!(obj is ThingDef def))
                    continue;
                int xp = IsekaiForgeCompat.MasteryXp(tracker, def.defName);
                if (xp > 0)
                    mastered.Add(new Row { Def = def, Xp = xp, Tier = IsekaiForgeCompat.MasteryTier(tracker, def.defName), Equipped = def.defName == equipped });
                else
                    unmastered.Add(def);
            }
            mastered.Sort((a, b) => b.Xp.CompareTo(a.Xp));
            unmastered.Sort((a, b) => string.Compare(a.label, b.label, System.StringComparison.OrdinalIgnoreCase));
        }

        protected override int ContentItemCount(int region)
        {
            return region == 0 ? mastered.Count : unmastered.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription { ReadOnly = true };
            if (region == 1)
            {
                if (index >= 0 && index < unmastered.Count)
                {
                    ThingDef def = unmastered[index];
                    d.Label = def.LabelCap;
                    d.Extras = "RimWorldAccess.Compat.Isekai.UnmasteredRow".Translate(WeaponType(def));
                }
                return d;
            }
            if (index < 0 || index >= mastered.Count)
                return d;

            Row row = mastered[index];
            string tier = IsekaiForgeCompat.MasteryTierLabel(row.Tier);
            int next = IsekaiForgeCompat.MasteryXpForNextTier(tracker, row.Def.defName);
            d.Label = row.Def.LabelCap;
            if (row.Equipped)
                d.Label += ", " + "RimWorldAccess.Compat.Isekai.Equipped".Translate();
            d.Value = next < 0 || IsekaiForgeCompat.MasteryTierIsMax(row.Tier)
                ? (string)"RimWorldAccess.Compat.Isekai.MasteredRowMax".Translate(tier, row.Xp)
                : (string)"RimWorldAccess.Compat.Isekai.MasteredRow".Translate(tier, row.Xp, next);

            IsekaiForgeCompat.MasteryBonuses(tracker, row.Def.defName, out float hit, out float speed, out float damage);
            var parts = new List<string> { WeaponType(row.Def) };
            if (hit > 0f || speed > 0f || damage > 0f)
            {
                parts.Add("RimWorldAccess.Compat.Isekai.MasteryBonuses".Translate(
                    (hit * 100f).ToString("F0"), (speed * 100f).ToString("F0"), (damage * 100f).ToString("F0")));
            }
            else
            {
                parts.Add("RimWorldAccess.Compat.Isekai.MasteryNoBonus".Translate());
            }
            d.Extras = CompatText.JoinSentences(parts);
            return d;
        }

        private static string WeaponType(ThingDef def)
        {
            return def.IsRangedWeapon
                ? (string)"RimWorldAccess.Compat.Isekai.WeaponTypeRanged".Translate()
                : (string)"RimWorldAccess.Compat.Isekai.WeaponTypeMelee".Translate();
        }

        protected override void ActivateContentItem(int region, int index)
        {
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(CompatText.ModText("Isekai_Close"), delegate { window.Close(); }, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen || !Ready)
                return;
            announcedOpen = true;
            Pawn pawn = IsekaiWindowCompat.MasteryPawn(window);
            ThingWithComps weapon = pawn?.equipment?.Primary;
            string equipped = weapon != null
                ? (string)"RimWorldAccess.Compat.Isekai.EquippedLine".Translate(weapon.LabelCapNoCount)
                : (string)"RimWorldAccess.Compat.Isekai.NoWeaponEquipped".Translate();
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Isekai.MasteryOpen".Translate(pawn != null ? pawn.LabelShortCap : "", equipped));
        }
    }
}
