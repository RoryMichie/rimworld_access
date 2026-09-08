using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for a pawn's Mood tab: the Break Thresholds sub-node (when the
    /// pawn can have mental breaks) followed by the leading thought in each
    /// mood-affecting thought group, with offset and expiry.
    /// </summary>
    internal sealed class PawnMoodAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Mood";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        /// <summary>
        /// Decorates the collapsed Mood row with the live mood percentage and descriptor (verbatim from
        /// InspectionTreeBuilder.GetCategoryLabel).
        /// </summary>
        public override string CategoryLabel(object obj, string displayName)
        {
            if (obj is Pawn pawn && pawn.needs?.mood != null)
            {
                float moodPercentage = pawn.needs.mood.CurLevelPercentage * 100f;
                string moodDescriptor = pawn.needs.mood.MoodString;
                return "RimWorldAccess.Inspection.CategoryName.MoodLabel".Translate(displayName, moodPercentage.ToString("F0"), moodDescriptor);
            }
            return displayName;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;
            BuildMoodChildren(categoryItem, pawn);
        }

        /// <summary>
        /// Builds children for Mood category.
        /// </summary>
        private static void BuildMoodChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            if (pawn.needs?.mood == null)
            {
                var noMoodItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoMoodInfo".Translate(),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                InspectNodeFactory.Attach(parentItem, noMoodItem);
                return;
            }

            Need_Mood mood = pawn.needs.mood;

            // Add Break Thresholds as expandable subcategory if pawn can have mental breaks
            if (pawn.mindState?.mentalBreaker != null &&
                pawn.mindState.mentalBreaker.CanDoRandomMentalBreaks)
            {
                var breakThresholdsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = "RimWorldAccess.Inspection.Tree.BreakThresholds".Translate(),
                    Data = new InspectSectionDatum(pawn, InspectSectionKind.MoodBreakThresholds),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };
                breakThresholdsItem.OnActivate = () => BuildBreakThresholdsChildren(breakThresholdsItem, pawn);
                InspectNodeFactory.Attach(parentItem, breakThresholdsItem);
            }

            // Get thoughts affecting mood
            BuildThoughtRows(parentItem, mood);
        }

        /// <summary>
        /// Builds the per-thought-group listing shared by the Mood category and any other
        /// caller presenting a pawn's mood/thoughts (VfPassengersTabAdapter's specific-needs
        /// mirror): leading thought label, stack count, mood offset, and expiry — exactly
        /// what NeedsCardUtility.DrawThoughtGroup draws for its own thought list. Internal for
        /// that reuse.
        /// </summary>
        internal static void BuildThoughtRows(InspectionTreeItem parentItem, Need_Mood mood)
        {
            List<Thought> thoughtGroups = new List<Thought>();
            PawnNeedsUIUtility.GetThoughtGroupsInDisplayOrder(mood, thoughtGroups);

            if (thoughtGroups.Count == 0)
            {
                var noThoughtsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoThoughts".Translate(),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                InspectNodeFactory.Attach(parentItem, noThoughtsItem);
            }

            // Process each thought group
            List<Thought> thoughtGroup = new List<Thought>();
            foreach (Thought group in thoughtGroups)
            {
                mood.thoughts.GetMoodThoughts(group, thoughtGroup);

                if (thoughtGroup.Count == 0)
                    continue;

                // Get the leading thought (most severe in the group)
                Thought leadingThought = PawnNeedsUIUtility.GetLeadingThoughtInGroup(thoughtGroup);

                if (leadingThought == null || !leadingThought.VisibleInNeedsTab)
                    continue;

                // Get mood offset for this thought group
                float moodOffset = mood.thoughts.MoodOffsetOfGroup(group);

                // Get the flavor text from thought Description (properly formatted with weapon names, etc.)
                string thoughtLabel = leadingThought.LabelCap.StripTags();
                string flavorText = "";
                if (leadingThought.CurStage != null && !string.IsNullOrEmpty(leadingThought.CurStage.description))
                {
                    // Use Description property which resolves placeholders like {WEAPON_indefinite}
                    // Extract just the first paragraph (before precept/nullified info)
                    string fullDescription = leadingThought.Description;
                    int splitIndex = fullDescription.IndexOf("\n\n");
                    string resolvedDescription = splitIndex > 0 ? fullDescription.Substring(0, splitIndex) : fullDescription;
                    flavorText = $"\"{resolvedDescription.StripTags()}\" ";
                }

                if (thoughtGroup.Count > 1)
                {
                    thoughtLabel = $"{thoughtLabel} x{thoughtGroup.Count}";
                }

                // Format mood offset with sign
                string offsetText = moodOffset.ToString("+0;-0;0");

                // Build expiry info if this is a memory-based thought
                string expiryText = "";
                int durationTicks = group.DurationTicks;
                if (durationTicks > 5 && leadingThought is Thought_Memory)
                {
                    if (thoughtGroup.Count == 1)
                    {
                        // Single thought - simple expiry
                        Thought_Memory memory = (Thought_Memory)leadingThought;
                        int remaining = durationTicks - memory.age;
                        expiryText = " " + "RimWorldAccess.Inspection.Thought.ExpiresIn".Translate(remaining.ToStringTicksToPeriod());
                    }
                    else
                    {
                        // Multiple stacked thoughts - show range
                        int minAge = int.MaxValue;
                        int maxAge = int.MinValue;
                        foreach (Thought thought in thoughtGroup)
                        {
                            if (thought is Thought_Memory mem)
                            {
                                minAge = Math.Min(minAge, mem.age);
                                maxAge = Math.Max(maxAge, mem.age);
                            }
                        }
                        int firstExpires = durationTicks - maxAge;
                        int lastExpires = durationTicks - minAge;
                        expiryText = " " + "RimWorldAccess.Inspection.Thought.ExpiresInRange".Translate(firstExpires.ToStringTicksToPeriod(), lastExpires.ToStringTicksToPeriod());
                    }
                }

                var thoughtItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"{flavorText}{thoughtLabel}: {offsetText}{expiryText}.",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };

                InspectNodeFactory.Attach(parentItem, thoughtItem);

                thoughtGroup.Clear();
            }
        }

        /// <summary>
        /// Builds children for Break Thresholds subcategory.
        /// </summary>
        internal static void BuildBreakThresholdsChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            if (pawn.mindState?.mentalBreaker == null)
                return;

            var breaker = pawn.mindState.mentalBreaker;

            float minor = breaker.BreakThresholdMinor * 100f;
            float major = breaker.BreakThresholdMajor * 100f;
            float extreme = breaker.BreakThresholdExtreme * 100f;

            string MakeBreakLine(string vanillaIntensityKey, float percent) =>
                "RimWorldAccess.Inspection.Tree.BreakThresholdLine".Translate(
                    vanillaIntensityKey.Translate().ToString().CapitalizeFirst(),
                    percent.ToString("F0"));

            var minorItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = MakeBreakLine("MentalBreakIntensityMinor", minor),
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false
            };
            InspectNodeFactory.Attach(parentItem, minorItem);

            var majorItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = MakeBreakLine("MentalBreakIntensityMajor", major),
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false
            };
            InspectNodeFactory.Attach(parentItem, majorItem);

            var extremeItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = MakeBreakLine("MentalBreakIntensityExtreme", extreme),
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = false
            };
            InspectNodeFactory.Attach(parentItem, extremeItem);
        }
    }
}
