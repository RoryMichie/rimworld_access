using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Builds InspectionTreeItem trees from GeneSet and Pawn_GeneTracker data.</summary>
    public static class GeneTreeBuilder
    {
        /// <summary>The complete tree for a pregnancy gene set, genes as children of the root.</summary>
        public static InspectionTreeItem BuildTree(GeneSet geneSet, string motherName = null, string fatherName = null)
        {
            if (geneSet == null)
            {
                return CreateEmptyTree();
            }

            var genes = geneSet.GenesListForReading;
            if (genes == null || genes.Count == 0)
            {
                return CreateEmptyTree();
            }

            string rootLabel = BuildRootLabel(geneSet, motherName, fatherName);

            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = rootLabel,
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1
            };

            // Mirrors GeneUtility.SortGeneDefs: displayPriorityInXenotype descending, then
            // displayOrderInCategory, then label.
            var sortedGenes = genes
                .OrderByDescending(g => g.displayCategory?.displayPriorityInXenotype ?? 0)
                .ThenBy(g => g.displayOrderInCategory)
                .ThenBy(g => g.label)
                .ToList();

            foreach (var gene in sortedGenes)
            {
                var geneNode = CreateGeneNode(gene, root.IndentLevel + 1);
                AddChild(root, geneNode);
            }

            AddBiostatsSummary(root, geneSet);

            return root;
        }

        /// <summary>The root label: gene count plus xenotype.</summary>
        private static string BuildRootLabel(GeneSet geneSet, string motherName, string fatherName)
        {
            var sb = new StringBuilder();

            string xenotype = geneSet.Label;
            bool hasXenotype = !string.IsNullOrEmpty(xenotype) && xenotype != "ERR";
            if (hasXenotype)
                sb.Append("RimWorldAccess.Biotech.Gene.RootBabyGenesWithXenotype".Translate(xenotype));
            else
                sb.Append("RimWorldAccess.Biotech.Gene.RootBabyGenes".Translate());

            int geneCount = geneSet.GenesListForReading?.Count ?? 0;
            sb.Append(" ");
            sb.Append(GeneCountSuffix(geneCount));

            return sb.ToString();
        }

        /// <summary>The localized parenthetical gene-count suffix, e.g. "(5 genes)".</summary>
        public static string GeneCountSuffix(int count)
        {
            if (count == 1)
                return "RimWorldAccess.Biotech.Gene.CountSuffixOne".Translate();
            return "RimWorldAccess.Biotech.Gene.CountSuffixMany".Translate(count);
        }

        /// <summary>
        /// A tree node for one gene: the name and any conflict status are the label, biostats and
        /// description ride the detail channel, and expanding turns that detail into children.
        /// </summary>
        public static InspectionTreeItem CreateGeneNode(GeneDef gene, int indent, bool includeCategory = true, string status = null)
        {
            var geneNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = AppendStatus(BuildGeneShortLabel(gene, includeCategory), status),
                Detail = BuildGeneDetailText(gene),
                Data = gene,
                LinkedDef = gene,
                IsExpandable = true,
                IsExpanded = false,
                IndentLevel = indent
            };

            geneNode.OnActivate = () => BuildGeneDetails(geneNode, gene);

            return geneNode;
        }

        // Status sits right after the gene name, ahead of biostats and description.
        private static string AppendStatus(string label, string status)
        {
            return string.IsNullOrEmpty(status) ? label : $"{label}, {status}";
        }

        /// <summary>The short label — name, color, optional category — used when expanded and as the base for the collapsed one.</summary>
        private static string BuildGeneShortLabel(GeneDef gene, bool includeCategory)
        {
            string label = GetGeneDisplayLabel(gene);

            if (includeCategory && gene.displayCategory != null)
            {
                label = $"{label} ({gene.displayCategory.LabelCap})";
            }

            return label;
        }

        /// <summary>The collapsed row's detail: biostats and description, so browsing needs no expansion.</summary>
        private static string BuildGeneDetailText(GeneDef gene)
        {
            var parts = new List<string>();

            if (gene.biostatCpx != 0)
            {
                string cpxLabel = ((string)"Complexity".Translate()).CapitalizeFirst();
                parts.Add($"{cpxLabel}: {gene.biostatCpx.ToStringWithSign()}");
            }
            if (gene.biostatMet != 0)
            {
                string metLabel = ((string)"Metabolism".Translate()).CapitalizeFirst();
                parts.Add($"{metLabel}: {gene.biostatMet.ToStringWithSign()}");
            }
            if (gene.biostatArc != 0)
            {
                string arcLabel = ((string)"ArchitesRequired".Translate()).CapitalizeFirst();
                parts.Add($"{arcLabel}: {gene.biostatArc.ToStringWithSign()}");
            }

            // Def.description, not DescriptionFull, which would repeat the biostats.
            if (!string.IsNullOrEmpty(gene.description))
            {
                string desc = gene.description.StripTags().TrimEnd();
                if (!desc.EndsWith(".") && !desc.EndsWith("!") && !desc.EndsWith("?"))
                {
                    desc += ".";
                }
                parts.Add(desc);
            }

            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        // The melanin ladder's nine genes all carry the label "skin color" and differ on screen
        // only by their swatch. Every other color gene names its own color.
        private static string GetColorDescription(GeneDef gene)
        {
            if (gene.endogeneCategory != EndogeneCategory.Melanin || !gene.skinColorBase.HasValue)
                return null;

            return DescribeSkinShade(gene.skinColorBase.Value);
        }

        private static string DescribeSkinShade(UnityEngine.Color color)
        {
            float luminance = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
            if (luminance > 0.85f) return "RimWorldAccess.Biotech.Color.SkinVeryLight".Translate();
            if (luminance > 0.7f) return "RimWorldAccess.Biotech.Color.SkinLight".Translate();
            if (luminance > 0.55f) return "RimWorldAccess.Biotech.Color.SkinFair".Translate();
            if (luminance > 0.45f) return "RimWorldAccess.Biotech.Color.SkinMedium".Translate();
            if (luminance > 0.35f) return "RimWorldAccess.Biotech.Color.SkinTan".Translate();
            if (luminance > 0.2f) return "RimWorldAccess.Biotech.Color.SkinBrown".Translate();
            return "RimWorldAccess.Biotech.Color.SkinDarkBrown".Translate();
        }

        /// <summary>
        /// The detail children for a gene node, from DescriptionFull. Biostats become separate
        /// expandable items carrying their own tooltip descriptions.
        /// </summary>
        private static void BuildGeneDetails(InspectionTreeItem geneNode, GeneDef gene)
        {
            if (geneNode.Children.Count > 0)
                return; // Already built

            int childIndent = geneNode.IndentLevel + 1;

            AddBiostatItems(geneNode, gene, childIndent);

            var biostatPrefixes = new List<string>();
            biostatPrefixes.Add(((string)"Complexity".Translate()).StripTags());
            biostatPrefixes.Add(((string)"Metabolism".Translate()).StripTags());
            biostatPrefixes.Add(((string)"ArchitesRequired".Translate()).StripTags());

            string fullDescription = gene.DescriptionFull;
            if (!string.IsNullOrEmpty(fullDescription))
            {
                fullDescription = fullDescription.StripTags();

                var sections = fullDescription.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var section in sections)
                {
                    var lines = section.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                    if (lines.Length == 1)
                    {
                        string line = lines[0].Trim();
                        if (!string.IsNullOrEmpty(line) && !IsBiostatLine(line, biostatPrefixes))
                        {
                            AddChild(geneNode, CreateInfoItem(line, childIndent));
                        }
                    }
                    else if (lines.Length > 1)
                    {
                        string firstLine = lines[0].Trim();
                        if (firstLine.EndsWith(":"))
                        {
                            var sectionNode = new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.SubCategory,
                                Label = firstLine.TrimEnd(':'),
                                IsExpandable = true,
                                IsExpanded = false,
                                IndentLevel = childIndent
                            };

                            for (int i = 1; i < lines.Length; i++)
                            {
                                string subLine = lines[i].Trim().TrimStart('-', ' ');
                                if (!string.IsNullOrEmpty(subLine))
                                {
                                    AddChild(sectionNode, CreateInfoItem(subLine, childIndent + 1));
                                }
                            }

                            if (sectionNode.Children.Count > 0)
                            {
                                AddChild(geneNode, sectionNode);
                            }
                        }
                        else
                        {
                            // Biostat lines are already explicit items above.
                            foreach (var rawLine in lines)
                            {
                                string line = rawLine.Trim();
                                if (!string.IsNullOrEmpty(line) && !IsBiostatLine(line, biostatPrefixes))
                                {
                                    AddChild(geneNode, CreateInfoItem(line, childIndent));
                                }
                            }
                        }
                    }
                }
            }

            if (geneNode.Children.Count == 0)
            {
                if (IsCosmeticGene(gene))
                {
                    AddChild(geneNode, CreateInfoItem("RimWorldAccess.Biotech.Gene.CosmeticNoEffects".Translate(), childIndent));
                }
                else
                {
                    AddChild(geneNode, CreateInfoItem("RimWorldAccess.Biotech.Gene.NoDetails".Translate(), childIndent));
                }
            }
        }

        /// <summary>Whether a gene is purely cosmetic: no biostats, no effects.</summary>
        private static bool IsCosmeticGene(GeneDef gene)
        {
            return gene.biostatCpx == 0 &&
                   gene.biostatMet == 0 &&
                   gene.biostatArc == 0 &&
                   (gene.statOffsets == null || gene.statOffsets.Count == 0) &&
                   (gene.statFactors == null || gene.statFactors.Count == 0) &&
                   (gene.capMods == null || gene.capMods.Count == 0) &&
                   (gene.abilities == null || gene.abilities.Count == 0) &&
                   (gene.forcedTraits == null || gene.forcedTraits.Count == 0);
        }

        /// <summary>Adds complexity, metabolism and archites as expandable items with their tooltip descriptions.</summary>
        private static void AddBiostatItems(InspectionTreeItem parent, GeneDef gene, int indent)
        {
            if (gene.biostatCpx != 0)
            {
                string label = ((string)"Complexity".Translate()).CapitalizeFirst();
                var node = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"{label}: {gene.biostatCpx.ToStringWithSign()}",
                    IsExpandable = true,
                    IsExpanded = false,
                    IndentLevel = indent
                };
                node.OnActivate = () =>
                {
                    if (node.Children.Count > 0) return;
                    string desc = ((string)"ComplexityDesc".Translate()).StripTags();
                    AddChild(node, CreateInfoItem(desc, indent + 1));
                };
                AddChild(parent, node);
            }

            if (gene.biostatMet != 0)
            {
                string label = ((string)"Metabolism".Translate()).CapitalizeFirst();
                var node = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"{label}: {gene.biostatMet.ToStringWithSign()}",
                    IsExpandable = true,
                    IsExpanded = false,
                    IndentLevel = indent
                };
                node.OnActivate = () =>
                {
                    if (node.Children.Count > 0) return;
                    string desc = ((string)"MetabolismDesc".Translate()).StripTags();
                    AddChild(node, CreateInfoItem(desc, indent + 1));
                };
                AddChild(parent, node);
            }

            if (gene.biostatArc != 0)
            {
                string label = ((string)"ArchitesRequired".Translate()).CapitalizeFirst();
                var node = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"{label}: {gene.biostatArc.ToStringWithSign()}",
                    IsExpandable = true,
                    IsExpanded = false,
                    IndentLevel = indent
                };
                node.OnActivate = () =>
                {
                    if (node.Children.Count > 0) return;
                    string desc = ((string)"ArchitesRequiredDesc".Translate()).StripTags();
                    AddChild(node, CreateInfoItem(desc, indent + 1));
                };
                AddChild(parent, node);
            }
        }

        /// <summary>Whether a DescriptionFull line is a biostat line, already shown as its own item.</summary>
        private static bool IsBiostatLine(string line, List<string> biostatPrefixes)
        {
            foreach (var prefix in biostatPrefixes)
            {
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>A gene's display label, with a shade word where the game's own label is ambiguous.</summary>
        public static string GetGeneDisplayLabel(GeneDef gene)
        {
            string label = gene.LabelCap;
            string colorDesc = GetColorDescription(gene);
            return string.IsNullOrEmpty(colorDesc) ? label : $"{label}: {colorDesc}";
        }

        /// <summary>Adds the biostats summary section at the end of the tree.</summary>
        private static void AddBiostatsSummary(InspectionTreeItem root, GeneSet geneSet)
        {
            int complexity = geneSet.ComplexityTotal;
            int metabolism = geneSet.MetabolismTotal;
            int archites = geneSet.ArchitesTotal;

            // The game's own "total" keys, so the summary needs no mod-authored English glue.
            string cpxLabel = ((string)"ComplexityTotal".Translate()).CapitalizeFirst();
            string metLabel = ((string)"MetabolismTotal".Translate()).CapitalizeFirst();
            var parts = new List<string>();
            parts.Add("RimWorldAccess.Biotech.Gene.BiostatComplexityValue".Translate(complexity).ToString());
            parts.Add("RimWorldAccess.Biotech.Gene.BiostatMetabolismValue".Translate(metabolism.ToStringWithSign()).ToString());
            if (archites > 0)
            {
                parts.Add("RimWorldAccess.Biotech.Gene.BiostatArchitesValue".Translate(archites).ToString());
            }

            string summaryLabel = "RimWorldAccess.Biotech.Gene.TotalBiostats".Translate(string.Join(", ", parts)).ToString();

            var summaryNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = summaryLabel,
                IsExpandable = true,
                IsExpanded = false,
                IndentLevel = 0
            };

            summaryNode.OnActivate = () =>
            {
                if (summaryNode.Children.Count > 0) return;

                string complexityDesc = ((string)"ComplexityDesc".Translate()).StripTags();
                AddChild(summaryNode, CreateInfoItem(
                    "RimWorldAccess.Biotech.Gene.BiostatComplexityWithDesc".Translate(complexity, complexityDesc),
                    summaryNode.IndentLevel + 1));

                string metabolismDesc = ((string)"MetabolismDesc".Translate()).StripTags();
                AddChild(summaryNode, CreateInfoItem(
                    "RimWorldAccess.Biotech.Gene.BiostatMetabolismWithDesc".Translate(metabolism.ToStringWithSign(), metabolismDesc),
                    summaryNode.IndentLevel + 1));

                if (archites > 0)
                {
                    string architesDesc = ((string)"ArchitesRequiredDesc".Translate()).StripTags();
                    AddChild(summaryNode, CreateInfoItem(
                        "RimWorldAccess.Biotech.Gene.BiostatArchitesWithDesc".Translate(archites, architesDesc),
                        summaryNode.IndentLevel + 1));
                }
            };

            AddChild(root, summaryNode);
        }

        /// <summary>
        /// The gene tree for an adult pawn's Pawn_GeneTracker, grouped into Endogenes and
        /// Xenogenes with active/overridden status.
        /// </summary>
        public static InspectionTreeItem BuildAdultGeneTree(Pawn pawn)
        {
            if (pawn?.genes == null || !ModsConfig.BiotechActive)
            {
                return CreateEmptyTree();
            }

            var geneTracker = pawn.genes;
            var endogenes = geneTracker.Endogenes;
            var xenogenes = geneTracker.Xenogenes;
            int totalCount = (endogenes?.Count ?? 0) + (xenogenes?.Count ?? 0);

            if (totalCount == 0)
            {
                return CreateEmptyTree();
            }

            string xenotypeLabel = geneTracker.XenotypeLabelCap;
            string countSuffix = GeneCountSuffix(totalCount);
            string rootLabel = "RimWorldAccess.Biotech.Gene.RootAdultGenes".Translate(xenotypeLabel, countSuffix);

            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = rootLabel,
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1
            };

            if (endogenes != null && endogenes.Count > 0)
            {
                string endoLabel = "Endogenes".Translate().CapitalizeFirst();
                var endoGroup = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = endoLabel,
                    ExpandedLabel = endoLabel,
                    IsExpandable = true,
                    IsExpanded = false,
                    IndentLevel = 0
                };
                BuildGeneGroupChildren(endoGroup, endogenes);
                var endoChildLabels = endoGroup.Children.Select(c => c.Label).ToList();
                if (endoChildLabels.Count > 0)
                    endoGroup.Label += $": {string.Join(", ", endoChildLabels)}";
                AddChild(root, endoGroup);
            }

            if (xenogenes != null && xenogenes.Count > 0)
            {
                string xenoLabel = "Xenogenes".Translate().CapitalizeFirst();
                var xenoGroup = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Label = xenoLabel,
                    ExpandedLabel = xenoLabel,
                    IsExpandable = true,
                    IsExpanded = false,
                    IndentLevel = 0
                };
                BuildGeneGroupChildren(xenoGroup, xenogenes);
                var xenoChildLabels = xenoGroup.Children.Select(c => c.Label).ToList();
                if (xenoChildLabels.Count > 0)
                    xenoGroup.Label += $": {string.Join(", ", xenoChildLabels)}";
                AddChild(root, xenoGroup);
            }

            AddAdultBiostatsSummary(root, geneTracker);

            return root;
        }

        /// <summary>Children for one gene group.</summary>
        private static void BuildGeneGroupChildren(InspectionTreeItem groupItem, List<Gene> genes)
        {
            // Mirrors GeneUtility.SortGenes: active first, then displayPriorityInXenotype
            // descending, then displayOrderInCategory. No label tiebreak, unlike SortGeneDefs.
            var sorted = genes
                .OrderBy(g => !g.Active)
                .ThenByDescending(g => g.def.displayCategory?.displayPriorityInXenotype ?? 0)
                .ThenBy(g => g.def.displayOrderInCategory)
                .ToList();

            foreach (var gene in sorted)
            {
                var geneNode = CreateActiveGeneNode(gene, groupItem.IndentLevel + 1);
                AddChild(groupItem, geneNode);
            }
        }

        /// <summary>A live Gene: active/overridden status in the label, biostats and description in the detail.</summary>
        private static InspectionTreeItem CreateActiveGeneNode(Gene gene, int indent)
        {
            var parts = new List<string>();

            parts.Add(GetGeneDisplayLabel(gene.def));

            if (gene.def.displayCategory != null)
            {
                parts.Add($"({gene.def.displayCategory.LabelCap})");
            }

            if (gene.Overridden)
            {
                // Vanilla's own wording, which names the winning gene and calls out an identical twin.
                string key = gene.overriddenByGene.def != gene.def ? "OverriddenByGene" : "OverriddenByIdenticalGene";
                parts.Add($"({((string)key.Translate()).StripTags()}: {gene.overriddenByGene.LabelCap})");
            }
            else if (!gene.Active)
            {
                parts.Add("RimWorldAccess.Biotech.Gene.StatusInactive".Translate().ToString());
            }

            var geneNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = string.Join(" ", parts),
                Detail = BuildGeneDetailText(gene.def),
                Data = gene,
                LinkedDef = gene.def,
                IsExpandable = true,
                IsExpanded = false,
                IndentLevel = indent
            };

            geneNode.OnActivate = () => BuildGeneDetails(geneNode, gene.def);

            return geneNode;
        }

        /// <summary>Adds the biostats summary for an adult pawn's gene tracker.</summary>
        private static void AddAdultBiostatsSummary(InspectionTreeItem root, Pawn_GeneTracker geneTracker)
        {
            int complexity = 0;
            int metabolism = 0;
            int archites = 0;

            foreach (var gene in geneTracker.GenesListForReading)
            {
                complexity += gene.def.biostatCpx;
                metabolism += gene.def.biostatMet;
                archites += gene.def.biostatArc;
            }

            var summaryParts = new List<string>();
            summaryParts.Add("RimWorldAccess.Biotech.Gene.BiostatComplexityValue".Translate(complexity).ToString());
            summaryParts.Add("RimWorldAccess.Biotech.Gene.BiostatMetabolismValue".Translate(metabolism.ToStringWithSign()).ToString());
            if (archites > 0)
            {
                summaryParts.Add("RimWorldAccess.Biotech.Gene.BiostatArchitesValue".Translate(archites).ToString());
            }

            string summaryLabel = "RimWorldAccess.Biotech.Gene.TotalBiostats".Translate(string.Join(", ", summaryParts)).ToString();

            var summaryNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = summaryLabel,
                IsExpandable = true,
                IsExpanded = false,
                IndentLevel = 0
            };

            summaryNode.OnActivate = () =>
            {
                if (summaryNode.Children.Count > 0) return;

                string complexityDesc = ((string)"ComplexityDesc".Translate()).StripTags();
                AddChild(summaryNode, CreateInfoItem(
                    "RimWorldAccess.Biotech.Gene.BiostatComplexityWithDesc".Translate(complexity, complexityDesc),
                    summaryNode.IndentLevel + 1));

                string metabolismDesc = ((string)"MetabolismDesc".Translate()).StripTags();
                AddChild(summaryNode, CreateInfoItem(
                    "RimWorldAccess.Biotech.Gene.BiostatMetabolismWithDesc".Translate(metabolism.ToStringWithSign(), metabolismDesc),
                    summaryNode.IndentLevel + 1));

                if (archites > 0)
                {
                    string architesDesc = ((string)"ArchitesRequiredDesc".Translate()).StripTags();
                    AddChild(summaryNode, CreateInfoItem(
                        "RimWorldAccess.Biotech.Gene.BiostatArchitesWithDesc".Translate(archites, architesDesc),
                        summaryNode.IndentLevel + 1));
                }
            };

            AddChild(root, summaryNode);
        }

        /// <summary>The tree shown when there are no genes.</summary>
        private static InspectionTreeItem CreateEmptyTree()
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = "RimWorldAccess.Biotech.Gene.RootBabyGenesNone".Translate(),
                IsExpandable = false,
                IsExpanded = false,
                IndentLevel = -1
            };

            return root;
        }

        /// <summary>A non-expandable detail-text item.</summary>
        private static InspectionTreeItem CreateInfoItem(string label, int indent)
        {
            return new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = label,
                IsExpandable = false,
                IsExpanded = false,
                IndentLevel = indent
            };
        }

        /// <summary>Adds a child and sets its parent reference.</summary>
        public static void AddChild(InspectionTreeItem parent, InspectionTreeItem child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }
    }
}
