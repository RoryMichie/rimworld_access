using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the animal pen "Pen Food" category. Builds the nutrition
    /// balance summary, stockpiled food breakdown, animals-in-pen listing,
    /// and the cross-linked example-animal add/remove subtrees.
    /// </summary>
    internal sealed class PenFoodAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Pen Food";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        /// <summary>
        /// Pen Food expands only when the building carries a pen marker comp
        /// (verbatim from InspectionTreeBuilder.IsExpandableCategory, D3).
        /// </summary>
        public override bool CanExpand(object obj)
        {
            return obj is Building building && building.TryGetComp<CompAnimalPenMarker>() != null;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(obj is Building building))
                return;
            BuildPenFoodChildren(categoryItem, building);
        }

        /// <summary>
        /// Builds children for Pen Food category showing nutrition info.
        /// </summary>
        private static void BuildPenFoodChildren(InspectionTreeItem parentItem, Building building)
        {
            var penMarker = building.TryGetComp<CompAnimalPenMarker>();
            if (penMarker == null)
                return;

            int indent = parentItem.IndentLevel + 1;
            var calculator = penMarker.PenFoodCalculator;

            // Unenclosed check - game shows only this message when pen is not enclosed
            if (calculator.Unenclosed)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Inspection.Tree.PenNotEnclosedDescription".Translate(
                        "AutocutUnenclosedPen".Translate()),
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            // Pen size description
            string penSize = calculator.PenSizeDescription();
            if (!string.IsNullOrEmpty(penSize))
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Inspection.Tree.PenSize".Translate(penSize),
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }

            // Nutrition balance summary
            float growth = calculator.NutritionPerDayToday;
            float consumption = calculator.SumNutritionConsumptionPerDay;
            float balance = growth - consumption;
            string balanceStr = balance >= 0 ? $"+{balance:F1}" : $"{balance:F1}";
            string summaryText = "RimWorldAccess.Inspection.Tree.PenBalance".Translate(
                balanceStr, growth.ToString("F1"), consumption.ToString("F1"));

            InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = summaryText,
                IndentLevel = indent,
                IsExpandable = false
            });

            // Stockpiled food
            if (calculator.sumStockpiledNutritionAvailableNow > 0)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Inspection.Tree.PenStockpiled".Translate(
                        calculator.sumStockpiledNutritionAvailableNow.ToString("F1")),
                    IndentLevel = indent,
                    IsExpandable = false
                });

                // Days until stockpile is empty (only when in deficit)
                if (balance < 0)
                {
                    float daysUntilEmpty = calculator.sumStockpiledNutritionAvailableNow / (-balance);
                    InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = "RimWorldAccess.Inspection.Tree.PenStockpileLasts".Translate(daysUntilEmpty.ToString("F1")),
                        IndentLevel = indent,
                        IsExpandable = false
                    });
                }
            }

            // Animals in pen
            var animalInfos = calculator.ActualAnimalInfos;
            if (animalInfos != null && animalInfos.Count > 0)
            {
                var animalsCategory = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "RimWorldAccess.Inspection.Tree.PenAnimalsHeader".Translate(animalInfos.Count),
                    IndentLevel = indent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                animalsCategory.OnActivate = () =>
                {
                    if (animalsCategory.Children.Count == 0)
                    {
                        string unknownAnimal = "RimWorldAccess.Inspection.Tree.PenUnknownAnimal".Translate();
                        foreach (var info in animalInfos)
                        {
                            string animalLabel = info.animalDef?.label?.CapitalizeFirst() ?? unknownAnimal;
                            float animalConsumption = info.nutritionConsumptionPerDay;
                            int count = info.count;
                            string animalText = "RimWorldAccess.Inspection.Tree.PenAnimalRow".Translate(
                                animalLabel, count, animalConsumption.ToString("F2"));

                            InspectNodeFactory.Attach(animalsCategory, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Item,
                                Label = animalText,
                                IndentLevel = indent + 1,
                                IsExpandable = false
                            });
                        }
                    }
                };
                InspectNodeFactory.Attach(parentItem, animalsCategory);
            }

            // Example Animals and Add Example Animal — these cross-reference each other
            // so changes in one rebuild both sections and refresh the visible list.
            var examplesCategory = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "RimWorldAccess.Inspection.Tree.PenExamplesHeader".Translate(),
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };

            var addExampleCategory = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "RimWorldAccess.Inspection.Tree.PenAddExamplesHeader".Translate(),
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };

            // Shared helper to rebuild both sections' children from current data
            System.Action rebuildExampleSections = null;
            rebuildExampleSections = () =>
            {
                // Rebuild "Example Animals" children
                examplesCategory.Children.Clear();
                var currentDefs = penMarker.ForceDisplayedAnimalDefs;
                if (currentDefs != null && currentDefs.Count > 0)
                {
                    var infos = calculator.ComputeExampleAnimals(currentDefs);
                    Quadrum bestQuadrum = calculator.GetSummerOrBestQuadrum();
                    examplesCategory.Label = "RimWorldAccess.Inspection.Tree.PenExamplesHeaderWithCount".Translate(infos?.Count ?? 0);
                    if (infos != null)
                    {
                        string unknownAnimal = "RimWorldAccess.Inspection.Tree.PenUnknownAnimal".Translate();
                        foreach (var info in infos)
                        {
                            if (info.animalDef == null) continue;
                            string label = info.animalDef.label?.CapitalizeFirst() ?? unknownAnimal;
                            float capacity = calculator.CapacityOf(bestQuadrum, info.animalDef);
                            float perAnimal = info.nutritionConsumptionPerDay;
                            string text = "RimWorldAccess.Inspection.Tree.PenExampleEntry".Translate(
                                label, capacity.ToString("F0"), perAnimal.ToString("F2"));

                            var exampleItem = new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Action,
                                Label = text,
                                IndentLevel = indent + 1,
                                IsExpandable = false
                            };
                            ThingDef capturedDef = info.animalDef;
                            exampleItem.OnActivate = () =>
                            {
                                penMarker.RemoveForceDisplayedAnimal(capturedDef);
                                string removedName = capturedDef.label?.CapitalizeFirst() ?? unknownAnimal;
                                TolkHelper.Speak("RimWorldAccess.Inspection.Tree.PenRemovedExample".Loc(removedName));
                                // Both sections attach to parentItem — the memo over it covers the
                                // pair, so the cursor survives the cross-section rebuild.
                                InspectionTreeBuilder.RebuildBranchInPlace(parentItem, rebuildExampleSections);
                            };
                            InspectNodeFactory.Attach(examplesCategory, exampleItem);
                        }
                    }
                }
                else
                {
                    examplesCategory.Label = "RimWorldAccess.Inspection.Tree.PenExamplesHeaderWithCount".Translate(0);
                }

                // Rebuild "Add Example Animal" children
                addExampleCategory.Children.Clear();
                var map = building.Map;
                if (map != null)
                {
                    var grazingAnimals = map.plantGrowthRateCalculator.GrazingAnimals;
                    var currentExamples = penMarker.ForceDisplayedAnimalDefs ?? new List<ThingDef>();
                    var available = new List<ThingDef>();
                    foreach (var animal in grazingAnimals)
                    {
                        if (!currentExamples.Contains(animal))
                            available.Add(animal);
                    }

                    if (available.Count == 0)
                    {
                        InspectNodeFactory.Attach(addExampleCategory, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Item,
                            Label = "RimWorldAccess.Inspection.Tree.PenNoMoreAnimalsAvailable".Translate(),
                            IndentLevel = indent + 1,
                            IsExpandable = false
                        });
                    }
                    else
                    {
                        string unknownAnimal = "RimWorldAccess.Inspection.Tree.PenUnknownAnimal".Translate();
                        foreach (var animal in available)
                        {
                            string animalName = animal.label?.CapitalizeFirst() ?? unknownAnimal;
                            ThingDef capturedAnimal = animal;
                            var animalChoice = new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Action,
                                Label = animalName,
                                IndentLevel = indent + 1,
                                IsExpandable = false
                            };
                            animalChoice.OnActivate = () =>
                            {
                                penMarker.AddForceDisplayedAnimal(capturedAnimal);
                                TolkHelper.Speak("RimWorldAccess.Inspection.Tree.PenAddedExample".Loc(animalName));
                                InspectionTreeBuilder.RebuildBranchInPlace(parentItem, rebuildExampleSections);
                            };
                            InspectNodeFactory.Attach(addExampleCategory, animalChoice);
                        }
                    }
                }
            };

            // Wire up OnActivate to lazy-load via the shared rebuild
            examplesCategory.OnActivate = () =>
            {
                if (examplesCategory.Children.Count == 0)
                    rebuildExampleSections();
            };
            addExampleCategory.OnActivate = () =>
            {
                if (addExampleCategory.Children.Count == 0)
                    rebuildExampleSections();
            };

            // Update the example animals label with current count
            var initialDefs = penMarker.ForceDisplayedAnimalDefs;
            int initialCount = (initialDefs != null) ? initialDefs.Count : 0;
            examplesCategory.Label = "RimWorldAccess.Inspection.Tree.PenExamplesHeaderWithCount".Translate(initialCount);

            InspectNodeFactory.Attach(parentItem, examplesCategory);
            InspectNodeFactory.Attach(parentItem, addExampleCategory);

            // Stockpiled items breakdown
            var stockpileInfos = calculator.AllStockpiledInfos;
            if (stockpileInfos != null && stockpileInfos.Count > 0)
            {
                var foodCategory = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "RimWorldAccess.Inspection.Tree.PenStockpiledItemsHeader".Translate(stockpileInfos.Count),
                    IndentLevel = indent,
                    IsExpandable = true,
                    IsExpanded = false
                };
                foodCategory.OnActivate = () =>
                {
                    if (foodCategory.Children.Count == 0)
                    {
                        string unknownFoodItem = "RimWorldAccess.Inspection.Tree.PenUnknownAnimal".Translate();
                        foreach (var info in stockpileInfos)
                        {
                            string foodLabel = info.itemDef?.label?.CapitalizeFirst() ?? unknownFoodItem;
                            float nutrition = info.totalNutritionAvailable;
                            string foodText = "RimWorldAccess.Inspection.Tree.PenStockpiledItemRow".Translate(
                                foodLabel, nutrition.ToString("F1"));

                            InspectNodeFactory.Attach(foodCategory, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.Item,
                                Label = foodText,
                                IndentLevel = indent + 1,
                                IsExpandable = false
                            });
                        }
                    }
                };
                InspectNodeFactory.Attach(parentItem, foodCategory);
            }
        }
    }
}
