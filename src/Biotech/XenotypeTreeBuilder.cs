using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Builds the xenotype editor's Selected/Library gene trees, paralleling
    /// IdeologyHelper's stateless-builder shape — pure functions of their
    /// parameters, no static fields of its own.
    ///
    /// There is no Controls-tab list builder here: Controls-region rows are plain
    /// element-role rows described and activated directly by
    /// <see cref="RimWorldAccess.Shell.XenotypeEditorScope"/>, since
    /// <c>ScreenModel</c>'s <c>ListModel</c> already owns flat-list cursor
    /// arithmetic generically.
    /// </summary>
    internal static class XenotypeTreeBuilder
    {
        // ===== Gene Trees =====

        public static InspectionTreeItem BuildSelectedTree(List<GeneDef> selectedGenes, Func<GeneDef, string> geneStatus = null)
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = "Root",
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1
            };

            if (selectedGenes == null || selectedGenes.Count == 0)
                return root;

            // Sort with the game's own comparer (GeneUtility.SortGeneDefs, decompiled
            // GeneUtility.cs) so shared-priority categories order the same as vanilla's
            // Dialog_CreateXenotype, which sorts selectedGenes this way on every change.
            var sorted = new List<GeneDef>(selectedGenes);
            sorted.SortGeneDefs();

            foreach (var gene in sorted)
            {
                var geneNode = GeneTreeBuilder.CreateGeneNode(gene, root.IndentLevel + 1, includeCategory: false, status: geneStatus?.Invoke(gene));
                GeneTreeBuilder.AddChild(root, geneNode);
            }

            return root;
        }

        public static InspectionTreeItem BuildLibraryTree(List<GeneDef> selectedGenes, bool ignoreRestrictions, Func<GeneDef, string> geneStatus = null)
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = "Root",
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1
            };

            GeneCategoryDef currentCategory = null;
            InspectionTreeItem categoryNode = null;

            foreach (var gene in GeneUtility.GenesInOrder)
            {
                // Skip archite genes if restrictions not ignored
                if (!ignoreRestrictions && gene.biostatArc > 0)
                    continue;

                // New category?
                if (gene.displayCategory != currentCategory)
                {
                    currentCategory = gene.displayCategory;
                    string catLabel = currentCategory?.LabelCap ?? "RimWorldAccess.Biotech.XenotypeEditor.Uncategorized".Translate().ToString();

                    categoryNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.SubCategory,
                        Label = catLabel,
                        Data = currentCategory,
                        IsExpandable = true,
                        IsExpanded = false,
                        IndentLevel = 0
                    };

                    GeneTreeBuilder.AddChild(root, categoryNode);
                }

                // Build gene node under category
                var geneNode = GeneTreeBuilder.CreateGeneNode(gene, 1, includeCategory: false, status: geneStatus?.Invoke(gene));
                // Selected rows only: vanilla marks no others.
                if (selectedGenes != null && selectedGenes.Contains(gene))
                    geneNode.Selected = true;
                geneNode.Data = gene;

                GeneTreeBuilder.AddChild(categoryNode, geneNode);
            }

            return root;
        }
    }
}
