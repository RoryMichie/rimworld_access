using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the Genes tab (rework §D2). Serves both pawns (via their
    /// <see cref="Pawn.genes"/> component) and standalone gene-set holders
    /// (embryos, genepacks, xenogerms), dispatching to whichever gene tree
    /// builder matches the inspected object.
    /// </summary>
    internal sealed class PawnGenesAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Genes";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        /// <summary>
        /// Genes expands for pawns with gene data or for GeneSetHolderBase
        /// items (embryos, genepacks, xenogerms) when Biotech is active
        /// (verbatim from InspectionTreeBuilder.IsExpandableCategory, D3).
        /// </summary>
        public override bool CanExpand(object obj)
        {
            Pawn genePawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (genePawn?.genes != null && ModsConfig.BiotechActive)
                return true;

            // Also expandable for GeneSetHolderBase items (embryos, genepacks, xenogerms)
            if (obj is GeneSetHolderBase holder && holder.GeneSet != null && ModsConfig.BiotechActive)
                return true;

            return false;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (obj is GeneSetHolderBase holder)
            {
                BuildGeneSetHolderGenesChildren(categoryItem, holder);
                return;
            }

            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;

            BuildGenesChildren(categoryItem, pawn);
        }

        /// <summary>
        /// Builds children for Genes category using GeneTreeBuilder.
        /// </summary>
        private static void BuildGenesChildren(InspectionTreeItem categoryItem, Pawn pawn)
        {
            if (categoryItem.Children.Count > 0)
                return; // Already built

            if (pawn?.genes == null || !ModsConfig.BiotechActive)
            {
                InspectNodeFactory.Attach(categoryItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoGeneInfo".Translate(),
                    IndentLevel = categoryItem.IndentLevel + 1,
                    IsExpandable = false
                });
                return;
            }

            var geneTree = GeneTreeBuilder.BuildAdultGeneTree(pawn);

            // Copy children from the gene tree root into our category item
            foreach (var child in geneTree.Children)
            {
                child.Parent = categoryItem;
                child.IndentLevel = categoryItem.IndentLevel + 1;
                AdjustChildIndents(child, categoryItem.IndentLevel + 1);
                categoryItem.Children.Add(child);
            }

            // Build collapsed summary from xenotype info and children
            string genesLabel = "TabGenes".Translate().ToString();
            categoryItem.ExpandedLabel = genesLabel;
            string xenotypeLabel = pawn.genes.XenotypeLabelCap;
            var geneChildLabels = categoryItem.Children.Select(c => c.Label).ToList();
            if (geneChildLabels.Count > 0)
                categoryItem.Label = $"{genesLabel}, {xenotypeLabel}: {string.Join(". ", geneChildLabels)}";
            else
                categoryItem.Label = $"{genesLabel}: {xenotypeLabel}";
        }

        /// <summary>
        /// Builds children for Genes category for GeneSetHolderBase items (embryos, genepacks, xenogerms).
        /// Uses GeneTreeBuilder.BuildTree() to create the gene tree from the item's GeneSet.
        /// </summary>
        private static void BuildGeneSetHolderGenesChildren(InspectionTreeItem categoryItem, GeneSetHolderBase holder)
        {
            if (categoryItem.Children.Count > 0)
                return; // Already built

            if (holder.GeneSet == null || !ModsConfig.BiotechActive)
            {
                InspectNodeFactory.Attach(categoryItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoGeneInfo".Translate(),
                    IndentLevel = categoryItem.IndentLevel + 1,
                    IsExpandable = false
                });
                return;
            }

            // Get parent names for embryos (HumanEmbryo has Mother/Father via CompHasPawnSources)
            string motherName = null;
            string fatherName = null;
            if (holder is HumanEmbryo embryo)
            {
                try
                {
                    motherName = embryo.Mother?.LabelShort;
                    fatherName = embryo.Father?.LabelShort;
                }
                catch { /* CompHasPawnSources may not be available */ }
            }

            var geneTree = GeneTreeBuilder.BuildTree(holder.GeneSet, motherName, fatherName);

            // Copy children from the gene tree root into our category item
            foreach (var child in geneTree.Children)
            {
                child.Parent = categoryItem;
                child.IndentLevel = categoryItem.IndentLevel + 1;
                AdjustChildIndents(child, categoryItem.IndentLevel + 1);
                categoryItem.Children.Add(child);
            }

            // Build collapsed summary from children
            string holderGenesLabel = "TabGenes".Translate().ToString();
            categoryItem.ExpandedLabel = holderGenesLabel;
            string xenotype = holder.GeneSet.Label;
            var holderGeneChildLabels = categoryItem.Children.Select(c => c.Label).ToList();
            if (holderGeneChildLabels.Count > 0)
            {
                string prefix = !string.IsNullOrEmpty(xenotype) && xenotype != "ERR"
                    ? $"{holderGenesLabel}, {xenotype}"
                    : holderGenesLabel;
                categoryItem.Label = $"{prefix}: {string.Join(". ", holderGeneChildLabels)}";
            }
            else if (!string.IsNullOrEmpty(xenotype) && xenotype != "ERR")
            {
                categoryItem.Label = $"{holderGenesLabel}: {xenotype}";
            }
        }

        /// <summary>
        /// Recursively adjusts indent levels of children relative to a new base indent.
        /// </summary>
        private static void AdjustChildIndents(InspectionTreeItem item, int baseIndent)
        {
            item.IndentLevel = baseIndent;
            foreach (var child in item.Children)
            {
                AdjustChildIndents(child, baseIndent + 1);
            }
        }
    }
}
