using System.Collections;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for ISEKAI RPG LEVELING's Window_Forge, the refinement bench.
    /// Three regions mirroring the window's three panels: the equipment list (Enter selects the
    /// item, the window's own list-click), the selected item's details (read-only lines in the
    /// window's own words), and the actions: refine at the next level, with its material lines
    /// and outcome chances, and repair when the item is damaged. Refine rides the window's own
    /// runner and, past the mod's own threshold, its own confirmation dialog; repair rides
    /// ForgeUtility.RepairItem's gate. The window's result text is spoken after each.
    /// </summary>
    internal sealed class IsekaiForgeScope : ScreenScope
    {
        private const int ListRegion = 0;
        private const int DetailsRegion = 1;
        private const int ActionsRegion = 2;

        private sealed class ActionRow
        {
            public string Label;
            public string Extras;
            public bool ReadOnly;
            public bool Disabled;
            public string DisabledReason;
            public System.Action Activate;
        }

        private readonly Window window;
        private readonly List<Thing> items = new List<Thing>();
        private readonly List<string> details = new List<string>();
        private readonly List<ActionRow> actionRows = new List<ActionRow>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        public IsekaiForgeScope(Window window)
        {
            this.window = window;
        }

        public override string Name => "isekai-forge";

        protected internal override Window OwnedWindow => window;

        protected override bool EnableTypeahead => true;

        /// <summary>Vanilla-mode Refine/Repair are ButtonText calls this scope presents itself as action rows.</summary>
        protected override bool CaptureWindowButtons => false;

        protected override int ContentRegionCount => 3;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case ListRegion: return "RimWorldAccess.Compat.Isekai.EquipmentRegion".Translate();
                case DetailsRegion: return "RimWorldAccess.Compat.Isekai.DetailsRegion".Translate();
                default: return "RimWorldAccess.Compat.Isekai.ActionsRegion".Translate();
            }
        }

        private bool Ready => IsekaiForgeCompat.Gate.Ensure() && IsekaiCompat.CoreGate.Ensure();

        protected override void RefreshContent()
        {
            items.Clear();
            details.Clear();
            actionRows.Clear();
            if (!Ready)
                return;
            if (!IsekaiCompat.ForgeEnabled)
            {
                details.Add(CompatText.ModText("Isekai_Forge_Disabled"));
                return;
            }
            IList equipment = IsekaiForgeCompat.ForgeEquipment(window);
            if (equipment != null)
            {
                foreach (object obj in equipment)
                {
                    if (obj is Thing thing)
                        items.Add(thing);
                }
            }
            Thing selected = IsekaiForgeCompat.ForgeSelectedItem(window);
            BuildDetails(selected);
            BuildActions(selected);
        }

        private void BuildDetails(Thing selected)
        {
            if (selected == null)
            {
                details.Add(CompatText.ModText("Isekai_Forge_SelectFromList"));
                return;
            }
            object comp = IsekaiForgeCompat.EnhancementOf(selected);
            if (comp == null)
            {
                details.Add(CompatText.ModText("Isekai_Forge_CannotEnhance"));
                return;
            }
            details.Add(selected.LabelCapNoCount);
            if (selected.TryGetQuality(out QualityCategory quality))
                details.Add(CompatText.ModArgs("Isekai_Forge_Quality", quality.GetLabel().CapitalizeFirst()));
            int level = IsekaiForgeCompat.RefinementLevel(comp);
            details.Add(level > 0
                ? CompatText.ModArgs("Isekai_Forge_RefinementLevel", level.ToString())
                : CompatText.ModText("Isekai_Forge_RefinementNone"));
            if (level > 0)
            {
                if (selected.def.IsMeleeWeapon)
                {
                    details.Add(CompatText.ModArgs("Isekai_Forge_MeleeDmgBonus", Percent("GetMeleeDamageBonus", level, "F0")));
                    details.Add(CompatText.ModArgs("Isekai_Forge_MeleeCdBonus", Percent("GetMeleeSpeedBonus", level, "F0")));
                    details.Add(CompatText.ModArgs("Isekai_Forge_WeaponMassBonus", Percent("GetWeaponMassReduction", level, "F1")));
                }
                else if (selected.def.IsRangedWeapon)
                {
                    details.Add(CompatText.ModArgs("Isekai_Forge_RangedDmgBonus", Percent("GetRangedDamageBonus", level, "F0")));
                    details.Add(CompatText.ModArgs("Isekai_Forge_CooldownBonus", Percent("GetRangedCooldownReduction", level, "F0")));
                    details.Add(CompatText.ModArgs("Isekai_Forge_AccuracyBonus", Percent("GetRangedAccuracyBonus", level, "F0")));
                    details.Add(CompatText.ModArgs("Isekai_Forge_WeaponMassBonus2", Percent("GetWeaponMassReduction", level, "F1")));
                }
                else if (selected.def.IsApparel)
                {
                    details.Add(CompatText.ModArgs("Isekai_Forge_ArmorRatingBonus", Percent("GetArmorBonus", level, "F0")));
                    details.Add(CompatText.ModArgs("Isekai_Forge_MoveSpeedBonus", Percent("GetArmorMoveSpeedBonus", level, "F1")));
                    details.Add(CompatText.ModArgs("Isekai_Forge_ArmorMassBonus", Percent("GetArmorMassReduction", level, "F0")));
                }
            }
            details.Add(CompatText.ModArgs("Isekai_Forge_RuneSlotsInfo",
                IsekaiForgeCompat.UsedRuneSlots(comp).ToString(), IsekaiForgeCompat.MaxRuneSlots(comp).ToString()));
            foreach (KeyValuePair<Def, int> rune in IsekaiForgeCompat.AppliedRunes(comp))
                details.Add(rune.Key.LabelCap + ": " + IsekaiForgeCompat.RuneDescriptionForRank(rune.Key, rune.Value));
            string result = IsekaiForgeCompat.ForgeLastResult(window);
            if (!string.IsNullOrEmpty(result))
                details.Add(result);
        }

        private static string Percent(string bonusMethod, int level, string format)
        {
            return (IsekaiForgeCompat.Bonus(bonusMethod, level) * 100f).ToString(format);
        }

        private void BuildActions(Thing selected)
        {
            if (selected == null)
                return;
            object comp = IsekaiForgeCompat.EnhancementOf(selected);
            if (comp == null)
                return;
            Map map = IsekaiForgeCompat.ForgeMap(window);
            bool godMode = Prefs.DevMode && DebugSettings.godMode;

            if (!IsekaiForgeCompat.CanRefine(comp))
            {
                actionRows.Add(new ActionRow { Label = CompatText.ModText("Isekai_Forge_MaxRefinement"), ReadOnly = true });
            }
            else
            {
                int target = IsekaiForgeCompat.RefinementLevel(comp) + 1;
                IsekaiForgeCompat.RefineCost cost = IsekaiForgeCompat.GetRefineCost(target);
                float success = IsekaiForgeCompat.SuccessChance(target) * 100f;
                float downgrade = UnityEngine.Mathf.Min(IsekaiForgeCompat.DowngradeChance(target) * 100f, UnityEngine.Mathf.Max(0f, 100f - success));
                float nothing = UnityEngine.Mathf.Max(0f, 100f - success - downgrade);
                bool hasMaterials = godMode || IsekaiForgeCompat.HasMaterials(map, cost);

                var chance = new List<string> { CompatText.ModArgs("Isekai_Forge_SuccessLabel", success.ToString("F0")) };
                if (nothing > 0f)
                    chance.Add(CompatText.ModArgs("Isekai_Forge_NothingLabel", nothing.ToString("F0")));
                if (downgrade > 0f)
                    chance.Add(CompatText.ModArgs("Isekai_Forge_DowngradeLabel", downgrade.ToString("F0")));

                actionRows.Add(new ActionRow
                {
                    Label = CompatText.ModArgs("Isekai_Forge_RefineTo", target.ToString()),
                    Extras = CompatText.JoinSentences(chance),
                    Disabled = !hasMaterials,
                    DisabledReason = hasMaterials ? null : CompatText.ModText("Isekai_Forge_MissingMaterials"),
                    Activate = delegate { Refine(comp, target, success); },
                });
                actionRows.Add(new ActionRow { Label = CompatText.ModText("Isekai_Forge_MaterialsLabel"), ReadOnly = true });
                AddCostLine(map, cost.CoreDef, cost.CoreCount);
                if (cost.SecondaryCoreDef != null && cost.SecondaryCoreCount > 0)
                    AddCostLine(map, cost.SecondaryCoreDef, cost.SecondaryCoreCount);
                if (cost.Steel > 0)
                    AddCostLine(map, ThingDefOf.Steel, cost.Steel);
                if (cost.Components > 0)
                    AddCostLine(map, IsekaiForgeCompat.RefinementComponent, cost.Components);
            }

            if (IsekaiForgeCompat.NeedsRepair(selected))
            {
                int maxHp = IsekaiForgeCompat.LiveMaxHitPoints(selected);
                float percent = (float)selected.HitPoints / maxHp * 100f;
                int essenceCost = IsekaiForgeCompat.RepairEssenceCost(selected);
                ThingDef essence = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_ManaEssence");
                bool canAfford = godMode || (essence != null && IsekaiForgeCompat.CountOnMap(map, essence) >= essenceCost);
                actionRows.Add(new ActionRow
                {
                    Label = CompatText.ModText("Isekai_Forge_Repair"),
                    Extras = CompatText.ModArgs("Isekai_Forge_Durability", selected.HitPoints.ToString(), maxHp.ToString(), percent.ToString("F0")),
                    Disabled = !canAfford,
                    DisabledReason = canAfford ? null : CompatText.ModText("Isekai_Forge_NeedEssence"),
                    Activate = delegate { Repair(selected, map); },
                });
                if (essence != null)
                    AddCostLine(map, essence, essenceCost);
            }
        }

        private void AddCostLine(Map map, ThingDef def, int count)
        {
            if (def == null)
                return;
            int available = IsekaiForgeCompat.CountOnMap(map, def);
            actionRows.Add(new ActionRow
            {
                Label = "RimWorldAccess.Compat.Isekai.CostLine".Translate(def.LabelCap, count, available),
                ReadOnly = true,
            });
        }

        private void Refine(object comp, int target, float success)
        {
            // The window's own click handler: the mod's confirmation past its own threshold, then its runner.
            if (target >= 6)
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    CompatText.ModArgs("Isekai_Forge_ConfirmRefine", target.ToString(), success.ToString("F0")),
                    delegate { RunRefinement(comp, target); }));
                return;
            }
            RunRefinement(comp, target);
        }

        private void RunRefinement(object comp, int target)
        {
            IsekaiForgeCompat.DoRefinement(window, comp, target);
            RefreshModel();
            string result = IsekaiForgeCompat.ForgeLastResult(window);
            if (!string.IsNullOrEmpty(result))
                TolkHelper.SpeakData(result);
        }

        private void Repair(Thing item, Map map)
        {
            if (!IsekaiForgeCompat.RepairItem(item, map))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            // The window's own success feedback: its quest sound and result line.
            SoundDefOf.Quest_Concluded.PlayOneShotOnCamera();
            RefreshModel();
            TolkHelper.SpeakData(CompatText.ModText("Isekai_Forge_Repaired"));
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case ListRegion: return items.Count;
                case DetailsRegion: return details.Count;
                default: return actionRows.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == ListRegion)
            {
                if (index < 0 || index >= items.Count)
                    return d;
                Thing item = items[index];
                d.Label = item.LabelCapNoCount;
                d.Role = ElementRole.RadioButton;
                d.Selected = ReferenceEquals(IsekaiForgeCompat.ForgeSelectedItem(window), item);
                d.Extras = OwnerLine(item);
                return d;
            }
            if (region == DetailsRegion)
            {
                if (index >= 0 && index < details.Count)
                {
                    d.Label = details[index];
                    d.ReadOnly = true;
                }
                return d;
            }
            if (index < 0 || index >= actionRows.Count)
                return d;
            ActionRow row = actionRows[index];
            d.Label = row.Label;
            d.Extras = row.Extras;
            if (row.ReadOnly)
            {
                d.ReadOnly = true;
            }
            else
            {
                d.Role = ElementRole.Button;
                d.Disabled = row.Disabled;
                if (row.Disabled && !string.IsNullOrEmpty(row.DisabledReason))
                    d.Extras = string.IsNullOrEmpty(d.Extras) ? row.DisabledReason : row.DisabledReason + ". " + d.Extras;
            }
            return d;
        }

        internal static string OwnerLine(Thing item)
        {
            Pawn owner = (item.ParentHolder as Pawn_EquipmentTracker)?.pawn ?? (item.ParentHolder as Pawn_ApparelTracker)?.pawn;
            return owner != null
                ? (string)"RimWorldAccess.Compat.Isekai.ItemOwnedBy".Translate(owner.LabelShortCap)
                : CompatText.ModText("Isekai_UI_InStorage");
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == ListRegion)
            {
                if (index < 0 || index >= items.Count)
                    return;
                IsekaiForgeCompat.ForgeSelect(window, items[index]);
                SoundDefOf.Click.PlayOneShotOnCamera();
                RefreshModel();
                AnnounceCurrentItem();
                return;
            }
            if (region != ActionsRegion || index < 0 || index >= actionRows.Count)
                return;
            ActionRow row = actionRows[index];
            if (row.ReadOnly || row.Activate == null)
                return;
            if (row.Disabled)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                if (!string.IsNullOrEmpty(row.DisabledReason))
                    TolkHelper.SpeakData(row.DisabledReason);
                return;
            }
            row.Activate();
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
            if (announcedOpen)
                return;
            announcedOpen = true;
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Isekai.ForgeOpen".Translate(CompatText.ModText("Isekai_Forge_Title"), items.Count));
        }
    }
}
