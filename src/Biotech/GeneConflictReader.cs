using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Says on each gene row what Dialog_CreateXenotype's own gene tooltip says about conflicts:
    /// which gene of a mutually-exclusive group applies, which the group suppresses, which will be
    /// chosen at random per pawn, and which selected gene lacks its prerequisite. Reads the
    /// dialog's caches, which vanilla rebuilds on every gene change, rather than re-deriving the
    /// grouping.
    /// </summary>
    internal static class GeneConflictReader
    {
        /// <summary>
        /// A per-gene status describer for one section of the dialog, or null when the dialog is
        /// gone. Only the selected section carries group status, matching vanilla's own GeneTip.
        /// </summary>
        public static Func<GeneDef, string> StatusFor(GeneCreationDialogBase dialog, List<GeneDef> selectedGenes, bool selectedSection)
        {
            if (dialog == null)
                return null;

            var groups = XenotypeReflection.LeftChosenGroupsField.GetValue(dialog) as List<GeneLeftChosenGroup>;
            var randomPools = SymmetricRandomPools(
                XenotypeReflection.RandomChosenGroupsField.GetValue(dialog) as Dictionary<GeneDef, List<GeneDef>>);

            return gene => Describe(gene, selectedGenes, selectedSection ? groups : null, selectedSection ? randomPools : null);
        }

        /// <summary>
        /// Vanilla's randomChosenGroups keys each pool on its first gene and lists only the genes
        /// after it, so the last member of a pool gets no tooltip at all. Sighted play covers that
        /// with the hover highlight vanilla draws on everything conflicting with the hovered gene;
        /// merging the overlapping entries gives every member the same whole pool to speak.
        /// </summary>
        private static Dictionary<GeneDef, List<GeneDef>> SymmetricRandomPools(Dictionary<GeneDef, List<GeneDef>> vanillaGroups)
        {
            var byGene = new Dictionary<GeneDef, List<GeneDef>>();
            if (vanillaGroups == null)
                return byGene;

            var pools = new List<List<GeneDef>>();
            foreach (List<GeneDef> members in vanillaGroups.Values)
            {
                List<GeneDef> pool = pools.FirstOrDefault(p => p.Any(members.Contains));
                if (pool == null)
                {
                    pool = new List<GeneDef>();
                    pools.Add(pool);
                }
                foreach (GeneDef member in members)
                {
                    if (!pool.Contains(member))
                        pool.Add(member);
                }
            }

            foreach (List<GeneDef> pool in pools)
            {
                foreach (GeneDef member in pool)
                    byGene[member] = pool;
            }
            return byGene;
        }

        private static string Describe(GeneDef gene, List<GeneDef> selectedGenes,
            List<GeneLeftChosenGroup> groups, Dictionary<GeneDef, List<GeneDef>> randomPools)
        {
            var parts = new List<string>();

            if (groups != null)
            {
                if (groups.Any(g => g.leftChosen == gene))
                {
                    parts.Add(((string)"Active".Translate()).StripTags());
                }
                else
                {
                    GeneLeftChosenGroup suppressor = groups.FirstOrDefault(g => g.overriddenGenes.Contains(gene));
                    if (suppressor != null)
                        parts.Add("RimWorldAccess.Biotech.XenotypeEditor.GeneSuppressedBy".Translate(suppressor.leftChosen.LabelCap));
                }
            }

            if (randomPools != null && randomPools.TryGetValue(gene, out List<GeneDef> pool))
            {
                parts.Add("RimWorldAccess.Biotech.XenotypeEditor.GeneRandomlyChosen"
                    .Translate(pool.Select(g => (string)g.LabelCap).ToCommaList(useAnd: true)));
            }

            // Vanilla's own prerequisite line, which it shows in both sections.
            if (gene.prerequisite != null && selectedGenes != null
                && selectedGenes.Contains(gene) && !selectedGenes.Contains(gene.prerequisite))
            {
                parts.Add(((string)"MessageGeneMissingPrerequisite".Translate(gene.label)).StripTags()
                    + ": " + gene.prerequisite.LabelCap);
            }

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }
    }
}
