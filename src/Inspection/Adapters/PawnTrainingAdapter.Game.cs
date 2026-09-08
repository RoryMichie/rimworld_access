using System;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the pawn Training tab ("Training" category). Builds
    /// trainability/wildness detail lines plus the master assignment, follow
    /// toggles, Odyssey behavior toggles, and trainable skills subtree.
    /// </summary>
    internal sealed class PawnTrainingAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Training";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;
            BuildTrainingChildren(categoryItem, pawn, mode);
        }

        /// <summary>
        /// Builds children for Training category with interactive controls.
        /// Shows trainability, wildness, master assignment, follow toggles, and trainable skills.
        /// </summary>
        private static void BuildTrainingChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            if (pawn?.training == null)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            // Trainability header
            TrainabilityDef trainability = TrainableUtility.GetTrainability(pawn);
            if (trainability != null)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "CreatureTrainability".Translate(pawn.def.label).CapitalizeFirst()
                            + ": " + trainability.LabelCap,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }

            // Wildness with stat explanation inline via ExpandedLabel
            float wildness = pawn.GetStatValue(StatDefOf.Wildness);
            string wildnessShort = ("CreatureWildness".Translate(pawn.def.label).CapitalizeFirst()
                        + ": " + wildness.ToStringPercent()).Resolve();
            string wildnessExplanation = StatDefOf.Wildness.Worker.GetExplanationFull(
                StatRequest.For(pawn), StatDefOf.Wildness.toStringNumberSense, wildness);
            // Flatten multiline explanation into sentence form
            string flatExplanation = string.Join(". ",
                wildnessExplanation.StripTags()
                    .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l)));
            InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = wildnessShort + ". " + flatExplanation,
                ExpandedLabel = wildnessShort,
                IndentLevel = indent,
                IsExpandable = false
            });

            // Master section and follow toggles (only if Obedience learned)
            if (pawn.training.HasLearned(TrainableDefOf.Obedience))
            {
                BuildTrainingMasterSection(parentItem, pawn, indent, isReadOnly);
                BuildTrainingFollowToggles(parentItem, pawn, indent, isReadOnly);
            }

            // Odyssey DLC behavior toggles
            if (ModsConfig.OdysseyActive)
            {
                BuildTrainingOdysseyToggles(parentItem, pawn, indent, isReadOnly);
            }

            // Trainable skills list
            if (pawn.RaceProps.showTrainables)
            {
                BuildTrainingSkillsList(parentItem, pawn, indent, isReadOnly);
            }
        }

        /// <summary>
        /// Builds the master assignment section for the training tab.
        /// </summary>
        private static void BuildTrainingMasterSection(
            InspectionTreeItem parentItem, Pawn pawn, int indent, bool isReadOnly)
        {
            bool canChangeMaster = pawn.RaceProps.playerCanChangeMaster || !ModsConfig.IdeologyActive;
            string masterLabel = TrainableUtility.MasterString(pawn);

            if (!canChangeMaster || isReadOnly)
            {
                string label = "Master".Translate() + ": " + masterLabel;
                string tooltip = null;
                if (!canChangeMaster && pawn.playerSettings?.Master != null)
                {
                    tooltip = "DryadCannotChangeMaster".Translate(
                        pawn.Named("ANIMAL"),
                        pawn.playerSettings.Master.Named("MASTER")).CapitalizeFirst();
                }
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = label,
                    Tooltip = tooltip,
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            // Interactive: expandable master selector
            var masterItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "Master".Translate() + ": " + masterLabel,
                Data = new InspectSectionDatum(pawn, InspectSectionKind.TrainingMaster),
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };

            masterItem.OnActivate = () =>
            {
                if (masterItem.Children.Count > 0)
                    return;

                int childIndent = masterItem.IndentLevel + 1;
                var candidates = TrainingTabHelper.GetMasterCandidates(pawn);

                foreach (var candidate in candidates)
                {
                    if (candidate.CanBeMaster)
                    {
                        if (candidate.IsCurrent)
                        {
                            // Current master shown as non-actionable
                            InspectNodeFactory.Attach(masterItem, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.DetailText,
                                Label = candidate.Label,
                                IndentLevel = childIndent,
                                IsExpandable = false
                            });
                        }
                        else
                        {
                            var capturedColonist = candidate.Colonist;
                            var optionItem = new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Action,
                                Label = candidate.Label,
                                IndentLevel = childIndent,
                                IsExpandable = false
                            };
                            optionItem.OnActivate = () =>
                            {
                                TrainingTabHelper.SetMaster(pawn, capturedColonist);
                                InspectionTreeBuilder.RebuildBranchInPlace(masterItem, () =>
                                {
                                    masterItem.Children.Clear();
                                    masterItem.IsExpanded = false;
                                    masterItem.Label = "Master".Translate() + ": "
                                                       + TrainableUtility.MasterString(pawn);
                                });
                            };
                            InspectNodeFactory.Attach(masterItem, optionItem);
                        }
                    }
                    else
                    {
                        string reasonSuffix = !string.IsNullOrEmpty(candidate.DisabledReason)
                            ? " (" + candidate.DisabledReason + ")"
                            : "";
                        InspectNodeFactory.Attach(masterItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = candidate.Label + reasonSuffix,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        });
                    }
                }
            };

            InspectNodeFactory.Attach(parentItem, masterItem);
        }

        /// <summary>
        /// Builds follow drafted/fieldwork toggle items.
        /// </summary>
        private static void BuildTrainingFollowToggles(
            InspectionTreeItem parentItem, Pawn pawn, int indent, bool isReadOnly)
        {
            // Follow Drafted
            string draftedState = pawn.playerSettings.followDrafted
                ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
            var draftedItem = new InspectionTreeItem
            {
                Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText
                                  : InspectionTreeItem.ItemType.Action,
                Label = "CreatureFollowDrafted".Translate() + ": " + draftedState,
                IndentLevel = indent,
                IsExpandable = false
            };
            if (!isReadOnly)
            {
                draftedItem.OnActivate = () =>
                {
                    TrainingTabHelper.ToggleFollowDrafted(pawn);
                    string newState = pawn.playerSettings.followDrafted
                        ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                    draftedItem.Label = "CreatureFollowDrafted".Translate() + ": " + newState;
                };
            }
            InspectNodeFactory.Attach(parentItem, draftedItem);

            // Follow Fieldwork
            string fieldworkState = pawn.playerSettings.followFieldwork
                ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
            var fieldworkItem = new InspectionTreeItem
            {
                Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText
                                  : InspectionTreeItem.ItemType.Action,
                Label = "CreatureFollowFieldwork".Translate() + ": " + fieldworkState,
                IndentLevel = indent,
                IsExpandable = false
            };
            if (!isReadOnly)
            {
                fieldworkItem.OnActivate = () =>
                {
                    TrainingTabHelper.ToggleFollowFieldwork(pawn);
                    string newState = pawn.playerSettings.followFieldwork
                        ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                    fieldworkItem.Label = "CreatureFollowFieldwork".Translate() + ": " + newState;
                };
            }
            InspectNodeFactory.Attach(parentItem, fieldworkItem);
        }

        /// <summary>
        /// Builds Odyssey DLC behavior toggles (forage, dig) if the skills are learned.
        /// </summary>
        private static void BuildTrainingOdysseyToggles(
            InspectionTreeItem parentItem, Pawn pawn, int indent, bool isReadOnly)
        {
            if (pawn.training.HasLearned(TrainableDefOf.Forage))
            {
                string forageState = pawn.playerSettings.animalForage
                    ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                var forageItem = new InspectionTreeItem
                {
                    Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText
                                      : InspectionTreeItem.ItemType.Action,
                    Label = "ForageEnabled".Translate() + ": " + forageState,
                    IndentLevel = indent,
                    IsExpandable = false
                };
                if (!isReadOnly)
                {
                    forageItem.OnActivate = () =>
                    {
                        TrainingTabHelper.ToggleForaging(pawn);
                        string newState = pawn.playerSettings.animalForage
                            ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                        forageItem.Label = "ForageEnabled".Translate() + ": " + newState;
                    };
                }
                InspectNodeFactory.Attach(parentItem, forageItem);
            }

            if (pawn.training.HasLearned(TrainableDefOf.Dig))
            {
                string digState = pawn.playerSettings.animalDig
                    ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                var digItem = new InspectionTreeItem
                {
                    Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText
                                      : InspectionTreeItem.ItemType.Action,
                    Label = "DigEnabled".Translate() + ": " + digState,
                    IndentLevel = indent,
                    IsExpandable = false
                };
                if (!isReadOnly)
                {
                    digItem.OnActivate = () =>
                    {
                        TrainingTabHelper.ToggleDigging(pawn);
                        string newState = pawn.playerSettings.animalDig
                            ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
                        digItem.Label = "DigEnabled".Translate() + ": " + newState;
                    };
                }
                InspectNodeFactory.Attach(parentItem, digItem);
            }
        }

        /// <summary>
        /// Builds the expandable trainable skills list with toggle capability.
        /// </summary>
        private static void BuildTrainingSkillsList(
            InspectionTreeItem parentItem, Pawn pawn, int indent, bool isReadOnly)
        {
            var skillsItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "RimWorldAccess.Inspection.Tree.SkillsSubcategory".Translate(),
                Data = new InspectSectionDatum(pawn, InspectSectionKind.TrainingSkills),
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };

            skillsItem.OnActivate = () =>
            {
                if (skillsItem.Children.Count > 0)
                    return;

                int childIndent = skillsItem.IndentLevel + 1;
                var trainables = TrainingTabHelper.GetTrainableInfos(pawn);

                if (trainables.Count == 0)
                {
                    InspectNodeFactory.Attach(skillsItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = "RimWorldAccess.Inspection.Tree.NoTrainableSkills".Translate(),
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                    return;
                }

                foreach (var info in trainables)
                {
                    string progress = info.CurrentSteps + " / " + info.TotalSteps;
                    string description = TrainingTabHelper.GetTrainableDescription(pawn, info);

                    if (!info.CanTrain)
                    {
                        // Cannot train — show reason with description inline.
                        // Vanilla draws the step count for every visible row,
                        // trainable or not (TrainingCardUtility.TryDrawTrainableRow).
                        string shortLabel = info.Def.LabelCap + ": " +
                            (!string.IsNullOrEmpty(info.DisabledReason) ? info.DisabledReason : "RimWorldAccess.Inspection.Training.CannotTrain".Translate().ToString())
                            + ", " + progress;
                        InspectNodeFactory.Attach(skillsItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = shortLabel + ". " + description,
                            ExpandedLabel = shortLabel,
                            Data = info.Def,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        });
                    }
                    else if (isReadOnly)
                    {
                        string status = (info.IsLearned ? "RimWorldAccess.Inspection.Training.StatusLearned"
                                       : info.IsWanted ? "RimWorldAccess.Inspection.Training.StatusWanted"
                                       : "RimWorldAccess.Inspection.Training.StatusNotWanted").Translate();
                        string shortLabel = info.Def.LabelCap + ": " + status + ", " + progress;
                        InspectNodeFactory.Attach(skillsItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = shortLabel + ". " + description,
                            ExpandedLabel = shortLabel,
                            Data = info.Def,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        });
                    }
                    else
                    {
                        // Interactive toggle with description inline
                        var capturedDef = info.Def;
                        string status = (info.IsLearned ? "RimWorldAccess.Inspection.Training.StatusLearned"
                                       : info.IsWanted ? "RimWorldAccess.Inspection.Training.StatusWanted"
                                       : "RimWorldAccess.Inspection.Training.StatusNotWanted").Translate();
                        string shortLabel = info.Def.LabelCap + ": " + status + ", " + progress;
                        var skillItem = new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Action,
                            Label = shortLabel + ". " + description,
                            ExpandedLabel = shortLabel,
                            Data = info.Def,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        };

                        skillItem.OnActivate = () =>
                        {
                            TrainingTabHelper.ToggleTrainable(pawn, capturedDef);
                            // Refresh all sibling labels since SetWantedRecursive cascades
                            RefreshTrainingSkillLabels(skillsItem, pawn);
                        };

                        InspectNodeFactory.Attach(skillsItem, skillItem);
                    }
                }
            };

            InspectNodeFactory.Attach(parentItem, skillsItem);
        }

        /// <summary>
        /// Refreshes all training skill labels after a toggle, since SetWantedRecursive
        /// cascades to prerequisites and dependents.
        /// </summary>
        private static void RefreshTrainingSkillLabels(InspectionTreeItem skillsItem, Pawn pawn)
        {
            foreach (var child in skillsItem.Children)
            {
                if (child.Data is TrainableDef td && child.Type == InspectionTreeItem.ItemType.Action)
                {
                    bool wanted = pawn.training.GetWanted(td);
                    bool learned = pawn.training.HasLearned(td);
                    string status = (learned ? "RimWorldAccess.Inspection.Training.StatusLearned"
                                   : wanted ? "RimWorldAccess.Inspection.Training.StatusWanted"
                                   : "RimWorldAccess.Inspection.Training.StatusNotWanted").Translate();
                    int steps = TrainingTabHelper.GetSteps(pawn, td);
                    string shortLabel = td.LabelCap + ": " + status + ", " + steps + " / " + td.steps;
                    // Rebuild full label with description
                    var info = new TrainingTabHelper.TrainableInfo
                    {
                        Def = td,
                        CanTrain = true,
                        DisabledReason = null
                    };
                    string description = TrainingTabHelper.GetTrainableDescription(pawn, info);
                    child.ExpandedLabel = shortLabel;
                    child.Label = shortLabel + ". " + description;
                }
            }
        }
    }
}
