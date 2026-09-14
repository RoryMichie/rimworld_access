using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Data/lifecycle facade for the treeview-style architect menu. Level 1: Categories
    /// (Orders, Structure, etc.) - expandable. Level 2:
    /// Designators (Wall, Door, etc.) - activates tool.
    ///
    /// Owns none of the cursor/typeahead/announcement machinery any more — that lives on
    /// <see cref="RimWorldAccess.Shell.ArchitectTreeScope"/> (a
    /// <see cref="RimWorldAccess.Shell.TreeRegionScope"/> subclass) via
    /// <see cref="RimWorldAccess.Shell.TreeModel{T}"/>. This class still owns
    /// <see cref="isActive"/> (a <c>ShellGuards.MenuOwnsInput</c> and
    /// <c>MapNavigationPatch</c> arrow-suppression member — design-locked), tree construction,
    /// designator-label formatting, the designator-activation callback, and the three bridge
    /// callbacks the mirror wires once so <see cref="Open"/>/<see cref="Close"/>/
    /// <see cref="GetSelectedDesignator"/>/<see cref="GetSelectedCategory"/> drive the scope's
    /// tree synchronously regardless of whether the scope happens to be pushed yet.
    /// </summary>
    public static class ArchitectTreeState
    {
        private static bool isActive = false;

        // Callback for when a designator is activated
        private static Action<Designator> onDesignatorActivated;

        public static bool IsActive => isActive;

        /// <summary>True while a designator pick would have somewhere to go.</summary>
        internal static bool HasPendingActivation => onDesignatorActivated != null;

        /// <summary>Wired once by ArchitectTreeScopeMirror's static constructor to ArchitectTreeScope.LoadTree.</summary>
        internal static Func<InspectionTreeItem, int> LoadTreeCallback;

        /// <summary>Wired once by ArchitectTreeScopeMirror's static constructor to ArchitectTreeScope.ClearTree.</summary>
        internal static Action ClearTreeCallback;

        /// <summary>Wired once by ArchitectTreeScopeMirror's static constructor to ArchitectTreeScope.SelectedTreeItem.</summary>
        internal static Func<InspectionTreeItem> SelectedItemCallback;

        /// <summary>
        /// Opens the architect tree menu.
        /// </summary>
        /// <param name="onActivated">Callback when a designator is selected for activation.</param>
        public static void Open(Action<Designator> onActivated)
        {
            onDesignatorActivated = onActivated;
            isActive = true;

            var root = BuildTree();

            if (root.Children.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.ArchitectTree.NoCategories".Loc());
                Close();
                return;
            }

            TolkHelper.Speak("RimWorldAccess.Building.ArchitectTree.MenuOpened".Loc());

            int visibleCount = LoadTreeCallback != null ? LoadTreeCallback(root) : 0;

        }

        /// <summary>
        /// Closes the architect tree menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            onDesignatorActivated = null;
            if (ClearTreeCallback != null)
            {
                ClearTreeCallback();
            }
        }

        /// <summary>
        /// Hands the picked designator to the stored placement callback. Deactivating BEFORE the
        /// callback runs is load-bearing: it routes into still-legacy placement territory
        /// (zone placement, the material float menu, ArchitectState.EnterPlacementMode), which
        /// assumes this menu is already closed. See ArchitectTreeScope's own remarks.
        /// </summary>
        internal static void CompleteDesignatorActivation(Designator designator)
        {
            isActive = false;
            var callback = onDesignatorActivated;
            onDesignatorActivated = null;
            if (callback != null)
            {
                callback(designator);
            }
        }

        /// <summary>
        /// Gets the currently selected designator, or null if a category is selected.
        /// </summary>
        public static Designator GetSelectedDesignator()
        {
            var item = SelectedItemCallback != null ? SelectedItemCallback() : null;
            if (item != null && item.Data is Designator designator)
                return designator;
            return null;
        }

        /// <summary>
        /// Gets the category the cursor is inside: the focused row's own category when a
        /// category row is focused, otherwise the parent category of the focused designator.
        /// </summary>
        public static DesignationCategoryDef GetSelectedCategory()
        {
            var item = SelectedItemCallback != null ? SelectedItemCallback() : null;
            while (item != null)
            {
                if (item.Data is DesignationCategoryDef category)
                    return category;
                item = item.Parent;
            }
            return null;
        }

        #region Tree Building

        /// <summary>
        /// Builds the InspectionTreeItem tree from categories and their designators.
        /// </summary>
        private static InspectionTreeItem BuildTree()
        {
            var root = new InspectionTreeItem
            {
                Label = "Architect",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            List<DesignationCategoryDef> categories = ArchitectHelper.GetAllCategories();

            foreach (DesignationCategoryDef category in categories)
            {
                var catItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = category.LabelCap,
                    Data = category,
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };

                // Pre-populate children (designators are lightweight)
                List<Designator> designators = ArchitectHelper.GetDesignatorsForCategory(category);
                foreach (Designator designator in designators)
                {
                    string label = GetDesignatorLabel(designator);
                    var desItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = label,
                        Data = designator,
                        IndentLevel = 1,
                        IsExpandable = false,
                        IsExpanded = false,
                        Parent = catItem
                    };
                    catItem.Children.Add(desItem);
                }

                root.Children.Add(catItem);
            }

            return root;
        }

        /// <summary>
        /// Gets a label for a designator including cost and description.
        /// Format: "Name: cost. description" for build designators.
        /// Format: "Name. description" for order designators.
        /// If the designator has right-click options, adds "(right bracket for more options)" hint after the name.
        /// </summary>
        private static string GetDesignatorLabel(Designator designator)
        {
            string label = designator.LabelCap;

            // Check if designator has right-click options and add hint
            bool hasRightClickOptions = designator.RightClickFloatMenuOptions.Any()
                || DesignatorContextMenuRouter.HasOptions(designator);
            string rightClickHint = hasRightClickOptions ? " " + "RimWorldAccess.Building.Architect.MoreOptionsHint".Translate().ToString() : "";

            // Add cost, skill, and description for build designators
            if (designator is Designator_Build buildDesignator)
            {
                BuildableDef buildable = buildDesignator.PlacingDef;
                if (buildable != null)
                {
                    string costInfo = ArchitectHelper.GetBriefCostInfo(buildable);
                    string skillInfo = ArchitectHelper.GetSkillRequirement(buildable);
                    string description = ArchitectHelper.GetDescription(buildable);

                    // Build combined info (cost, skill)
                    var infoParts = new List<string>();
                    if (!string.IsNullOrEmpty(costInfo))
                        infoParts.Add(costInfo);
                    if (!string.IsNullOrEmpty(skillInfo))
                        infoParts.Add(skillInfo);
                    string combinedInfo = string.Join(", ", infoParts);

                    // Format: "Name (right bracket hint): cost, skill. description"
                    label += rightClickHint;
                    if (!string.IsNullOrEmpty(combinedInfo) && !string.IsNullOrEmpty(description))
                    {
                        label += $": {combinedInfo}. {description}";
                    }
                    else if (!string.IsNullOrEmpty(combinedInfo))
                    {
                        label += $": {combinedInfo}";
                    }
                    else if (!string.IsNullOrEmpty(description))
                    {
                        label += $". {description}";
                    }
                }
                else
                {
                    label += rightClickHint;
                }
            }
            else
            {
                // For non-build designators (orders), add hint and description if available
                label += rightClickHint;
                string description = ArchitectHelper.GetDesignatorDescriptionText(designator);
                if (!string.IsNullOrEmpty(description))
                {
                    label += $". {description}";
                }
            }

            return label;
        }

        #endregion
    }
}
