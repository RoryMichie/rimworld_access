using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the Feeding tab (Biotech babies). Builds the auto-breastfeed
    /// feeder list and the baby food consumables sections, matching vanilla
    /// ITab_Pawn_Feeding.
    /// </summary>
    internal sealed class PawnFeedingAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Feeding";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null) return;
            BuildFeedingChildren(categoryItem, pawn, mode);
        }

        /// <summary>
        /// Builds children for the Feeding tab (Biotech babies).
        /// Two sections: auto-breastfeed feeder list and baby food consumables.
        /// Matches vanilla ITab_Pawn_Feeding exactly.
        /// </summary>
        private static void BuildFeedingChildren(InspectionTreeItem parentItem, Pawn baby, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return;

            if (!ModsConfig.BiotechActive)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            // === Auto-breastfeed section ===
            string autoHeader = "AutofeedSectionHeader".Translate().CapitalizeFirst();
            var autoSection = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = autoHeader,
                ExpandedLabel = autoHeader,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };
            autoSection.OnActivate = () => BuildAutobreastfeedChildren(autoSection, baby, isReadOnly);
            InspectNodeFactory.Attach(parentItem, autoSection);

            // === Baby Food Consumables section ===
            string foodHeader = "BabyFoodConsumables".Translate().CapitalizeFirst();
            var foodSection = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = foodHeader,
                ExpandedLabel = foodHeader,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };
            foodSection.OnActivate = () => BuildBabyFoodChildren(foodSection, baby, isReadOnly);
            InspectNodeFactory.Attach(parentItem, foodSection);
        }

        /// <summary>
        /// Builds the auto-breastfeed feeder list, matching vanilla's filtering and sorting.
        /// </summary>
        private static void BuildAutobreastfeedChildren(InspectionTreeItem parentItem, Pawn baby, bool isReadOnly)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;

            // Gather feeders using vanilla's exact filtering logic
            var feeders = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction
                .Where(f => f != baby
                    && f.RaceProps.Humanlike
                    && !ChildcareUtility.CanSuckle(f, out _)
                    && !f.IsWorkTypeDisabledByAge(WorkTypeDefOf.Childcare, out _))
                .ToList();

            if (feeders.Count == 0)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "AutofeedNone".Translate(),
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            // Sort using vanilla's exact order: lactating first, then mother, father, surrogate
            Pawn mother = baby.GetMother();
            Pawn father = baby.GetFather();
            Pawn surrogate = baby.GetBirthParent();

            feeders.Sort((lhs, rhs) =>
            {
                int cmp = rhs.health.hediffSet.HasHediff(HediffDefOf.Lactating)
                    .CompareTo(lhs.health.hediffSet.HasHediff(HediffDefOf.Lactating));
                if (cmp != 0) return cmp;
                if (lhs == mother) return -1;
                if (rhs == mother) return 1;
                if (lhs == father) return -1;
                if (rhs == father) return 1;
                if (lhs == surrogate) return -1;
                if (rhs == surrogate) return 1;
                return 0;
            });

            foreach (var feeder in feeders)
            {
                var localFeeder = feeder;
                AutofeedMode currentMode = baby.mindState.AutofeedSetting(localFeeder);

                // Build feeder name with lactation status
                string feederName = localFeeder.LabelShortCap;
                var lactatingHediff = localFeeder.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Lactating);
                if (lactatingHediff != null)
                    feederName += $" ({lactatingHediff.LabelBaseCap})";

                // Build relation label
                string relation = "";
                if (localFeeder == mother)
                    relation = $", {PawnRelationDefOf.Parent.labelFemale.CapitalizeFirst()}";
                else if (localFeeder == father)
                    relation = $", {PawnRelationDefOf.Parent.label.CapitalizeFirst()}";
                else if (localFeeder == surrogate)
                    relation = $", {PawnRelationDefOf.ParentBirth.GetGenderSpecificLabelCap(localFeeder)}";

                string modeLabel = currentMode.Translate().CapitalizeFirst();
                string fullLabel = $"{feederName}: {modeLabel}{relation}";

                var feederItem = new InspectionTreeItem
                {
                    Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText : InspectionTreeItem.ItemType.Action,
                    Label = fullLabel,
                    Data = localFeeder,
                    IndentLevel = indent,
                    IsExpandable = false
                };

                if (!isReadOnly)
                {
                    feederItem.OnActivate = () =>
                    {
                        // Open float menu with all three mode options
                        var options = new List<FloatMenuOption>();
                        foreach (AutofeedMode modeOption in System.Enum.GetValues(typeof(AutofeedMode)))
                        {
                            var localMode = modeOption;
                            string optLabel = localMode.Translate().CapitalizeFirst();
                            string tooltip = localMode.GetTooltip(baby, localFeeder);
                            options.Add(new FloatMenuOption(
                                $"{optLabel}. {tooltip}",
                                () =>
                                {
                                    baby.mindState.SetAutofeeder(localFeeder, localMode);
                                    string newModeLabel = localMode.Translate().CapitalizeFirst();
                                    feederItem.Label = $"{feederName}: {newModeLabel}{relation}";
                                }));
                        }
                        WindowlessFloatMenuState.Open(options, false);
                    };
                }

                InspectNodeFactory.Attach(parentItem, feederItem);
            }
        }

        /// <summary>
        /// Builds the baby food consumables list with toggleable food allowances.
        /// </summary>
        private static void BuildBabyFoodChildren(InspectionTreeItem parentItem, Pawn baby, bool isReadOnly)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;

            var foods = ITab_Pawn_Feeding.BabyConsumableFoods;
            if (foods == null || foods.Count == 0)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "NoneLower".Translate(),
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            string allowedStr = "On".Translate().ToString();
            string notAllowedStr = "Off".Translate().ToString();

            foreach (var food in foods)
            {
                var localFood = food;
                bool allowed = baby.foodRestriction?.BabyFoodAllowed(localFood) ?? true;
                string stateStr = allowed ? allowedStr : notAllowedStr;

                var foodItem = new InspectionTreeItem
                {
                    Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText : InspectionTreeItem.ItemType.Action,
                    Label = $"{localFood.LabelCap}: {stateStr}",
                    IndentLevel = indent,
                    IsExpandable = false
                };

                if (!isReadOnly)
                {
                    foodItem.OnActivate = () =>
                    {
                        if (baby.foodRestriction == null) return;
                        bool current = baby.foodRestriction.BabyFoodAllowed(localFood);
                        baby.foodRestriction.SetBabyFoodAllowed(localFood, !current);
                        string newState = !current ? allowedStr : notAllowedStr;
                        foodItem.Label = $"{localFood.LabelCap}: {newState}";
                        TolkHelper.SpeakData(newState);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    };
                }

                InspectNodeFactory.Attach(parentItem, foodItem);
            }
        }
    }
}
