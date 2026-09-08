using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>Categories, designators, and materials of RimWorld's architect system.</summary>
    public static class ArchitectHelper
    {
        /// <summary>The visible designation categories, in vanilla's order.</summary>
        public static List<DesignationCategoryDef> GetAllCategories()
        {
            List<DesignationCategoryDef> categories = new List<DesignationCategoryDef>();

            foreach (DesignationCategoryDef categoryDef in DefDatabase<DesignationCategoryDef>.AllDefsListForReading)
            {
                if (categoryDef.Visible)
                {
                    categories.Add(categoryDef);
                }
            }

            categories.SortBy(c => c.order);

            return categories;
        }

        /// <summary>The allowed designators of a category, with dropdowns flattened into their elements.</summary>
        public static List<Designator> GetDesignatorsForCategory(DesignationCategoryDef category)
        {
            if (category == null)
                return new List<Designator>();

            List<Designator> designators = new List<Designator>();

            try
            {
                ModLogger.Dev($"Getting designators for category: {category.defName}");

                foreach (Designator designator in category.ResolvedAllowedDesignators)
                {
                    if (designator is Designator_Dropdown dropdown)
                    {
                        if (dropdown.Elements != null)
                        {
                            foreach (Designator element in dropdown.Elements)
                            {
                                if (element.Visible)
                                {
                                    designators.Add(element);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (designator.Visible)
                        {
                            designators.Add(designator);
                        }
                    }
                }

                ModLogger.Dev($"After filtering: {designators.Count} designators available");


            }
            catch (System.Exception ex)
            {
                Log.Error($"Error getting designators for category {category.defName}: {ex}");
            }

            return designators;
        }

        /// <summary>The valid stuff for a buildable that is made from stuff.</summary>
        public static List<ThingDef> GetMaterialsForBuildable(BuildableDef buildable)
        {
            List<ThingDef> materials = new List<ThingDef>();

            Map map = Find.CurrentMap;
            if (map == null)
                return materials;

            if (buildable is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                // Fallback mirror of Designator_Build.ProcessInput's stuff filter, for when harvesting
                // the real menu is impossible (no map, or a mod's ProcessInput threw); the live path
                // is MaterialMenuHarvest.
                foreach (ThingDef item in from d in map.resourceCounter.AllCountedAmounts.Keys
                    orderby d.stuffProps?.commonality ?? float.PositiveInfinity descending, d.BaseMarketValue
                    select d)
                {
                    if (item.IsStuff && item.stuffProps.CanMake(thingDef) && (DebugSettings.godMode || map.listerThings.ThingsOfDef(item).Count > 0))
                    {
                        materials.Add(item);
                    }
                }
            }

            return materials;
        }

        /// <summary>
        /// The designator label with RimWorld's trailing "..." (added when no material is selected)
        /// stripped, so pluralization cannot produce "wall...s".
        /// </summary>
        public static string GetSanitizedLabel(Designator designator, string fallback = "Unknown")
        {
            string label = designator?.Label ?? fallback;
            if (label.EndsWith("..."))
            {
                label = label.Substring(0, label.Length - 3);
            }
            return label;
        }

        /// <summary>Pluralizes a label, preserving any parenthetical suffix: "grand stele (61%)" -> "grand steles (61%)".</summary>
        public static string PluralizePreservingParentheses(string label, int count)
        {
            if (string.IsNullOrEmpty(label) || count <= 1)
                return label;

            int parenIndex = label.IndexOf('(');
            if (parenIndex <= 0)
                return Find.ActiveLanguageWorker.Pluralize(label, count);

            string baseNoun = label.Substring(0, parenIndex).TrimEnd();
            string suffix = label.Substring(parenIndex);
            string pluralNoun = Find.ActiveLanguageWorker.Pluralize(baseNoun, count);
            return $"{pluralNoun} {suffix}";
        }

        /// <summary>The default stuff for a buildable, falling back to its first available material.</summary>
        public static ThingDef GetDefaultMaterial(BuildableDef buildable)
        {
            if (buildable is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                ThingDef defaultStuff = GenStuff.DefaultStuffFor(thingDef);
                if (defaultStuff != null)
                    return defaultStuff;

                List<ThingDef> materials = GetMaterialsForBuildable(buildable);
                if (materials.Count > 0)
                    return materials[0];
            }

            return null;
        }

        /// <summary>Whether a buildable requires material selection.</summary>
        public static bool RequiresMaterialSelection(BuildableDef buildable)
        {
            if (buildable is ThingDef thingDef)
            {
                return thingDef.MadeFromStuff;
            }
            return false;
        }

        /// <summary>A buildable's materials as FloatMenuOptions, each labelled with its stock count.</summary>
        public static List<FloatMenuOption> CreateMaterialOptions(BuildableDef buildable, Action<ThingDef> onSelected)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<ThingDef> materials = GetMaterialsForBuildable(buildable);

            foreach (ThingDef material in materials)
            {
                int availableCount = 0;
                if (Find.CurrentMap != null)
                {
                    availableCount = Find.CurrentMap.resourceCounter.GetCount(material);
                }

                string label = material.LabelCap;
                if (availableCount > 0)
                {
                    label += " " + "RimWorldAccess.Building.Architect.MaterialAvailableCount".Translate(availableCount);
                }
                else
                {
                    label += " " + "RimWorldAccess.Building.Architect.MaterialNoneAvailable".Translate();
                }

                options.Add(new FloatMenuOption(label, () => onSelected(material)));
            }

            return options;
        }

        /// <summary>Designators as FloatMenuOptions, each labelled with its cost or description.</summary>
        public static List<FloatMenuOption> CreateDesignatorOptions(List<Designator> designators, Action<Designator> onSelected)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (Designator designator in designators)
            {
                string label = designator.LabelCap;

                if (designator is Designator_Build buildDesignator)
                {
                    string extraInfo = GetBuildableExtraInfo(buildDesignator.PlacingDef);
                    if (!string.IsNullOrEmpty(extraInfo))
                    {
                        label += extraInfo;
                    }
                }
                else
                {
                    string description = GetDesignatorDescriptionText(designator);
                    if (!string.IsNullOrEmpty(description))
                    {
                        label += $" ({description})";
                    }
                }

                options.Add(new FloatMenuOption(label, () => onSelected(designator)));
            }

            return options;
        }

        /// <summary>
        /// A buildable's cost, skill requirement and description, formatted as the tree view does:
        /// ": {cost}, requires Construction {level} ({description})".
        /// </summary>
        private static string GetBuildableExtraInfo(BuildableDef buildable)
        {
            if (buildable == null)
                return "";

            string costInfo = GetBriefCostInfo(buildable);
            string skillInfo = GetSkillRequirement(buildable);
            string description = GetDescription(buildable);

            var infoParts = new List<string>();
            if (!string.IsNullOrEmpty(costInfo))
                infoParts.Add(costInfo);
            if (!string.IsNullOrEmpty(skillInfo))
                infoParts.Add(skillInfo);

            string combinedInfo = string.Join(", ", infoParts);

            if (!string.IsNullOrEmpty(combinedInfo) && !string.IsNullOrEmpty(description))
            {
                return $": {combinedInfo} ({description})";
            }
            else if (!string.IsNullOrEmpty(combinedInfo))
            {
                return $": {combinedInfo}";
            }
            else if (!string.IsNullOrEmpty(description))
            {
                return $" ({description})";
            }

            return "";
        }

        /// <summary>A buildable's construction skill requirement, or "" when it has none.</summary>
        public static string GetSkillRequirement(BuildableDef buildable)
        {
            if (buildable is ThingDef thingDef && thingDef.constructionSkillPrerequisite > 0)
            {
                return "RimWorldAccess.Building.Architect.RequiresConstruction".Translate(thingDef.constructionSkillPrerequisite);
            }
            return "";
        }

        /// <summary>A buildable's costs as a comma-separated list, with no "Cost:" prefix.</summary>
        public static string GetBriefCostInfo(BuildableDef buildable)
        {
            if (buildable == null)
                return "";

            List<string> costParts = new List<string>();

            if (buildable is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                int stuffCount = buildable.CostStuffCount;
                if (stuffCount > 0)
                {
                    // The shared localized "material" term, so this readout follows the player's
                    // language as the fixed costs below already do via thingDef.label.
                    costParts.Add($"{stuffCount} {(string)"RimWorldAccess.Common.Material".Translate()}");
                }
            }

            List<ThingDefCountClass> costs = buildable.CostList;
            if (costs != null)
            {
                foreach (ThingDefCountClass cost in costs)
                {
                    costParts.Add($"{cost.count} {cost.thingDef.label}");
                }
            }

            return string.Join(", ", costParts);
        }

        /// <summary>Description text with newlines removed and whitespace collapsed.</summary>
        private static string CleanupDescription(string description)
        {
            if (string.IsNullOrEmpty(description))
                return "";

            description = description.Replace("\n", " ").Replace("\r", " ");
            description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ").Trim();
            return description;
        }

        /// <summary>A buildable's cleaned-up description.</summary>
        public static string GetDescription(BuildableDef buildable)
        {
            if (buildable == null)
                return "";
            return CleanupDescription(buildable.description);
        }

        /// <summary>An order designator's cleaned-up description.</summary>
        public static string GetDesignatorDescriptionText(Designator designator)
        {
            if (designator == null)
                return "";
            return CleanupDescription(designator.Desc);
        }

        /// <summary>Categories as FloatMenuOptions.</summary>
        public static List<FloatMenuOption> CreateCategoryOptions(List<DesignationCategoryDef> categories, Action<DesignationCategoryDef> onSelected)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (DesignationCategoryDef category in categories)
            {
                string label = category.LabelCap;
                options.Add(new FloatMenuOption(label, () => onSelected(category)));
            }

            return options;
        }
    }
}
