using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The read-only tree/list/announcement source shared by every in-game ideology surface:
    /// data extraction, announcement building and tree construction.
    /// </summary>
    public static class IdeologyHelper
    {
        /// <summary>Every visible ideology, in the game's standard view order.</summary>
        public static List<Ideo> BuildIdeologyList()
        {
            return Find.IdeoManager.IdeosInViewOrder.ToList();
        }

        /// <summary>The list-panel announcement for an ideology: its name and the factions holding it.</summary>
        public static string BuildIdeoListAnnouncement(Ideo ideo)
        {
            var sb = new StringBuilder();
            sb.Append(ideo.name);

            var factionParts = new List<string>();
            if (Find.World != null)
            {
                foreach (Faction faction in Find.FactionManager.AllFactionsInViewOrder)
                {
                    if (faction.Hidden || faction.ideos == null)
                        continue;

                    if (faction.ideos.IsPrimary(ideo))
                        factionParts.Add("RimWorldAccess.Ideology.List.FactionPrimarySuffix".Translate(faction.Name));
                    else if (faction.ideos.IsMinor(ideo))
                        factionParts.Add("RimWorldAccess.Ideology.List.FactionMinorSuffix".Translate(faction.Name));
                }
            }

            if (factionParts.Count > 0)
                AppendSentence(sb, "IdeoligionOf".Translate() + " " + string.Join(", ", factionParts));

            return sb.ToString();
        }

        /// <summary>
        /// The full tree for one ideology's details. The root is hidden (IndentLevel -1), level 0
        /// nodes are section headers, and each section aggregates its children's text into its label.
        /// </summary>
        public static InspectionTreeItem BuildIdeologyTree(Ideo ideo)
        {
            var root = new InspectionTreeItem
            {
                Label = ideo.name,
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true
            };

            BuildOverviewSection(root, ideo);
            BuildStyleCategoriesSection(root, ideo);
            BuildFactionsSection(root, ideo);

            if (ideo.Fluid && ideo.development != null)
                BuildFluidSection(root, ideo);

            BuildMemesSection(root, ideo);
            BuildNarrativeSection(root, ideo);
            BuildDeitiesSection(root, ideo);

            // Precept categories in vanilla's DoPrecepts order.
            BuildPreceptCategorySection(root, ideo, "Precepts".Translate(),
                p => p.preceptClass == typeof(Precept));
            BuildPreceptCategorySection(root, ideo, "IdeoRoles".Translate(),
                p => typeof(Precept_Role).IsAssignableFrom(p.preceptClass));
            BuildPreceptCategorySection(root, ideo, "Rituals".Translate(),
                p => p.preceptClass == typeof(Precept_Ritual));
            BuildPreceptCategorySection(root, ideo, "IdeoBuildings".Translate(),
                p => p.preceptClass == typeof(Precept_Building) || p.preceptClass == typeof(Precept_RitualSeat));
            BuildPreceptCategorySection(root, ideo, "IdeoRelics".Translate(),
                p => p.preceptClass == typeof(Precept_Relic));
            BuildPreceptCategorySection(root, ideo, "IdeoWeapons".Translate(),
                p => p.preceptClass == typeof(Precept_Weapon));
            BuildPreceptCategorySection(root, ideo, "VeneratedAnimals".Translate(),
                p => p.preceptClass == typeof(Precept_Animal));

            if (ModsConfig.BiotechActive)
            {
                BuildPreceptCategorySection(root, ideo, "PreferredXenotypes".Translate(),
                    p => p.preceptClass == typeof(Precept_Xenotype));
            }

            BuildPreceptCategorySection(root, ideo, "IdeoApparel".Translate(),
                p => p.preceptClass == typeof(Precept_Apparel));

            // Residual bucket so a visible PreceptDef whose preceptClass no section above claims
            // (a modded class) still lands somewhere. Vanilla's own ladder has no catch-all, so
            // this is empty for any vanilla-only ideology.
            BuildPreceptCategorySection(root, ideo, "RimWorldAccess.Ideology.OtherPrecepts".Translate(),
                p => !IsClaimedByStandardPreceptSection(p));

            BuildAppearanceSection(root, ideo);

            return root;
        }

        #region Section Builders

        private static void BuildOverviewSection(InspectionTreeItem root, Ideo ideo)
        {
            var children = new List<string>();
            // Kept in lockstep with children: the two facts vanilla gives an icon of their own ring
            // that icon; the rest inherit the section's block below.
            var ringTargets = new List<object>();
            void AddOverviewChild(string text, object ringTarget)
            {
                children.Add(text);
                ringTargets.Add(ringTarget);
            }

            AddOverviewChild("Adjective".Translate().CapitalizeFirst() + ": " + ideo.adjective.CapitalizeFirst(), null);
            AddOverviewChild("IdeoMembers".Translate().CapitalizeFirst() + ": " + ideo.memberName.CapitalizeFirst(), null);

            MemeDef structureMeme = ideo.StructureMeme;
            if (structureMeme != null)
                AddOverviewChild("StructureMeme".Translate().CapitalizeFirst() + ": " + structureMeme.LabelCap, structureMeme);

            if (ideo.culture != null)
                AddOverviewChild("Culture".Translate().CapitalizeFirst() + ": " + ideo.culture.LabelCap, ideo.culture);

            if (!string.IsNullOrEmpty(ideo.leaderTitleMale))
            {
                string leaderEntry = "LeaderTitle".Translate().CapitalizeFirst() + ": " + ideo.leaderTitleMale.CapitalizeFirst();
                if (!string.IsNullOrEmpty(ideo.leaderTitleFemale) && ideo.leaderTitleFemale != ideo.leaderTitleMale)
                    leaderEntry += " (" + ideo.leaderTitleFemale.CapitalizeFirst() + ")";
                AddOverviewChild(leaderEntry, null);
            }

            AddOverviewChild("WorshipRoom".Translate().CapitalizeFirst() + ": " + ideo.WorshipRoomLabel.CapitalizeFirst(), null);

            var sectionNode = CreateSectionNode(root, ideo.name, children);
            // The name/symbol block's hover region is the finest-grained box vanilla has for the
            // remaining facts.
            sectionNode.RingTarget = Shell.IdeoBoxDrawPatch.NameSymbolKey;
            for (int i = 0; i < ringTargets.Count && i < sectionNode.Children.Count; i++)
            {
                if (ringTargets[i] != null)
                    sectionNode.Children[i].RingTarget = ringTargets[i];
            }
            root.Children.Add(sectionNode);
        }

        private static void BuildStyleCategoriesSection(InspectionTreeItem root, Ideo ideo)
        {
            if (ideo.thingStyleCategories.NullOrEmpty())
                return;

            var selectedStyles = ideo.thingStyleCategories;

            var catDataList = new List<(StyleCategoryDef def, string name, List<string> childTexts, bool hasSound)>();
            foreach (var styleCatWithPriority in selectedStyles)
            {
                StyleCategoryDef cat = styleCatWithPriority.category;
                var childTexts = new List<string>();
                var overrides = new Dictionary<StyleCategoryDef, List<string>>();

                var buildables = new List<string>();
                if (!cat.addDesignators.NullOrEmpty())
                    foreach (var bd in cat.addDesignators)
                        buildables.Add(bd.LabelCap.Resolve());
                if (!cat.addDesignatorGroups.NullOrEmpty())
                    foreach (var group in cat.addDesignatorGroups)
                        buildables.Add(group.LabelCap.Resolve());
                buildables.Sort();
                if (buildables.Count > 0)
                    childTexts.Add("IdeoMakesBuildingBuildable".Translate() + ": " + string.Join(", ", buildables));

                var styledThings = new List<string>();
                foreach (var tds in cat.thingDefStyles)
                {
                    ThingDef td = tds.ThingDef;
                    if (!td.canGenerateDefaultDesignator && (cat.addDesignators.NullOrEmpty() || !cat.addDesignators.Contains(td)))
                        continue;

                    StyleCategoryDef overriddenBy = null;
                    foreach (var higherStyle in selectedStyles)
                    {
                        if (higherStyle.category == cat)
                            break;
                        if (higherStyle.category.thingDefStyles.Any(s => s.ThingDef == td))
                        {
                            overriddenBy = higherStyle.category;
                            break;
                        }
                    }

                    if (overriddenBy != null)
                    {
                        if (!overrides.ContainsKey(overriddenBy))
                            overrides[overriddenBy] = new List<string>();
                        overrides[overriddenBy].Add(td.LabelCap.Resolve());
                    }
                    else
                    {
                        styledThings.Add(td.LabelCap.Resolve());
                    }
                }
                styledThings.Sort();
                if (styledThings.Count > 0)
                    childTexts.Add("StyleCategoryDetails".Translate(cat.LabelCap).Resolve() + ": " + string.Join(", ", styledThings));

                bool hasActiveRitualSound = cat.soundOngoingRitual != null
                    && cat.soundOngoingRitual == ideo.SoundOngoingRitual;

                foreach (var kvp in overrides.OrderBy(o => selectedStyles.ToList().FindIndex(s => s.category == o.Key)))
                {
                    kvp.Value.Sort();
                    childTexts.Add("OverriddenByStyle".Translate(kvp.Key.LabelCap).Resolve() + ": " + string.Join(", ", kvp.Value));
                }

                catDataList.Add((cat, cat.LabelCap.Resolve(), childTexts, hasActiveRitualSound));
            }

            // Single style: children go directly under the section node.
            if (catDataList.Count == 1)
            {
                var (def, name, childTexts, hasSound) = catDataList[0];
                var sectionLabel = new StringBuilder("Styles".Translate() + ": " + name);
                foreach (string childText in childTexts)
                    AppendSentence(sectionLabel, childText);

                var sectionNode = new InspectionTreeItem
                {
                    Label = sectionLabel.ToString(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root,
                    Type = InspectionTreeItem.ItemType.Category,
                    RingTarget = def
                };

                foreach (string childText in childTexts)
                    AddChildNode(sectionNode, childText);
                if (hasSound)
                    AddChildNode(sectionNode, "RitualAmbienceSound".Translate().Resolve() + "RimWorldAccess.Ideology.Styles.EnterToPreview".Translate(), ideo.SoundOngoingRitual);

                root.Children.Add(sectionNode);
                return;
            }

            // Multiple styles: nested.
            var multiSectionNode = new InspectionTreeItem
            {
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category,
                // Vanilla lays the tiles out in selection order, so the first selected category is
                // the topmost tile and the honest target for the section as a whole.
                RingTarget = selectedStyles[0].category
            };

            var catLabels = new List<string>();
            var ritualSoundStyleNames = new List<string>();

            for (int i = 0; i < catDataList.Count; i++)
            {
                var (def, name, childTexts, hasSound) = catDataList[i];
                var catHeader = new StringBuilder(name);
                foreach (string childText in childTexts)
                    AppendSentence(catHeader, childText);

                var catNode = new InspectionTreeItem
                {
                    Label = catHeader.ToString(),
                    IndentLevel = 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = multiSectionNode,
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    RingTarget = def
                };

                foreach (string childText in childTexts)
                    AddChildNode(catNode, childText);
                if (hasSound)
                {
                    AddChildNode(catNode, "RitualAmbienceSound".Translate().Resolve() + "RimWorldAccess.Ideology.Styles.EnterToPreview".Translate(), ideo.SoundOngoingRitual);
                    ritualSoundStyleNames.Add(name);
                }

                multiSectionNode.Children.Add(catNode);
                catLabels.Add(catHeader.ToString());
            }

            var sb = new StringBuilder("Styles".Translate());
            foreach (string catLabel in catLabels)
                AppendSentence(sb, catLabel);

            if (ritualSoundStyleNames.Count > 0)
            {
                string names = string.Join(", ", ritualSoundStyleNames);
                int count = ritualSoundStyleNames.Count;
                string hint = count == 1
                    ? "RimWorldAccess.Ideology.Styles.RitualSoundHintOne".Translate(names).ToString()
                    : "RimWorldAccess.Ideology.Styles.RitualSoundHintMany".Translate(names, count).ToString();
                AppendSentence(sb, hint);
            }

            multiSectionNode.Label = sb.ToString();
            root.Children.Add(multiSectionNode);
        }

        private static void BuildFactionsSection(InspectionTreeItem root, Ideo ideo)
        {
            if (Find.World == null)
                return;

            var factionEntries = new List<string>();
            foreach (Faction faction in Find.FactionManager.AllFactionsInViewOrder)
            {
                if (faction.Hidden || faction.ideos == null)
                    continue;

                if (faction.ideos.IsPrimary(ideo))
                    factionEntries.Add("RimWorldAccess.Ideology.FactionsSection.PrimaryEntry".Translate(faction.Name));
                else if (faction.ideos.IsMinor(ideo))
                    factionEntries.Add("RimWorldAccess.Ideology.FactionsSection.MinorEntry".Translate(faction.Name));
            }

            if (factionEntries.Count == 0)
                return;

            var sectionNode = CreateSectionNode(root, "IdeoligionOf".Translate().CapitalizeFirst(), factionEntries);
            // Vanilla draws one icon per faction with no per-faction seam a row could key off, so
            // the whole row is the unit.
            sectionNode.RingTarget = Shell.IdeoBoxDrawPatch.FactionsRowKey;
            root.Children.Add(sectionNode);
        }

        private static void BuildFluidSection(InspectionTreeItem root, Ideo ideo)
        {
            string points = ideo.development.Points + " / " + ideo.development.NextReformationDevelopmentPoints;
            bool canReform = ideo.development.CanReformNow;

            // The section title carries the live points and, when reformable, the action hint, so
            // landing on the node leads with the actionable info; the tip and the ways to earn
            // points are children. Enter reforms directly when possible. The points are NOT
            // repeated as a child, which doubled them.
            string title = "CurrentDevelopmentPoints".Translate().CapitalizeFirst() + ": " + points;
            if (canReform)
                title += ". " + (string)"RimWorldAccess.Ideology.PressEnterToReform".Translate();

            var children = new List<string>
            {
                "FluidIdeoTip".Translate().Resolve().StripTags(),
            };

            // Ways to earn development points.
            var earnMethods = new StringBuilder();
            earnMethods.Append("FluidIdeoTipGetPoints".Translate().Resolve().StripTags() + ": ");
            earnMethods.Append("FluidIdeoTipGetPoinsByConversion".Translate().Resolve().StripTags());

            var rituals = new List<Precept_Ritual>();
            IdeoDevelopmentUtility.GetAllRitualsThatGiveDevelopmentPoints(ideo, rituals);
            foreach (var ritual in rituals)
                earnMethods.Append(", " + ritual.LabelCap + " (" + "Ritual".Translate() + ")");

            var questEvents = new List<HistoryEventDef>();
            IdeoDevelopmentUtility.GetAllQuestSuccessEventsThatGiveDevelopmentPoints(ideo, questEvents);
            foreach (var questEvent in questEvents)
                earnMethods.Append(", " + questEvent.LabelCap + " (" + "QuestLower".Translate() + ")");

            children.Add(earnMethods.ToString());

            var sectionNode = CreateSectionNode(root, title, children);

            // Once enough points are earned the node itself becomes the Reform action (Enter
            // activates it through IdeoDetailsTreeRegion.TryActivateSpecial) and Right still
            // expands to the tip; below the threshold it is a plain info node.
            if (canReform)
                sectionNode.Data = new IdeoReformState.ReformActionMarker { Ideo = ideo };

            root.Children.Add(sectionNode);
        }

        private static void BuildMemesSection(InspectionTreeItem root, Ideo ideo)
        {
            var nonStructureMemes = ideo.memes.Where(m => m.category != MemeCategory.Structure).ToList();
            if (nonStructureMemes.Count == 0)
                return;

            var sectionNode = new InspectionTreeItem
            {
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category
            };

            var memeLabels = new List<string>();

            foreach (MemeDef meme in nonStructureMemes)
            {
                // The name alone: impact arrives with vanilla's own tip lines below.
                var memeHeader = new StringBuilder(meme.LabelCap.Resolve());
                var childLines = IdeoMemeSelectionHelper.GetMemeTipDetailLines(ideo, meme);

                if (childLines.Count > 0)
                {
                    var memeLabel = new StringBuilder(memeHeader.ToString());
                    foreach (string child in childLines)
                        AppendSentence(memeLabel, child);

                    var memeNode = new InspectionTreeItem
                    {
                        Label = memeLabel.ToString(),
                        IndentLevel = 1,
                        IsExpandable = true,
                        IsExpanded = false,
                        Parent = sectionNode,
                        Type = InspectionTreeItem.ItemType.SubCategory,
                        // Carries the MemeDef for identity lookups elsewhere in this tree. Vanilla
                        // never opens Dialog_InfoCard for a MemeDef, so the Alt+I walker excludes
                        // MemeDef the same way it excludes SoundDef: this node yields no target.
                        Data = meme,
                        RingTarget = meme
                    };

                    foreach (string child in childLines)
                        AddChildNode(memeNode, child);

                    sectionNode.Children.Add(memeNode);
                    memeLabels.Add(memeLabel.ToString());
                }
                else
                {
                    AddChildNode(sectionNode, memeHeader.ToString(), meme).RingTarget = meme;
                    memeLabels.Add(memeHeader.ToString());
                }
            }

            var sb = new StringBuilder("Memes".Translate());
            foreach (string memeLabel in memeLabels)
                AppendSentence(sb, memeLabel);
            sectionNode.Label = sb.ToString();

            root.Children.Add(sectionNode);
        }


        private static void BuildNarrativeSection(InspectionTreeItem root, Ideo ideo)
        {
            if (string.IsNullOrEmpty(ideo.description))
                return;

            string narrativeLabel = "CoreNarrative".Translate().CapitalizeFirst();
            string[] lines = ideo.description.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length <= 1)
            {
                var sectionNode = CreateSectionNode(root, narrativeLabel,
                    new List<string> { ideo.description.Trim() });
                root.Children.Add(sectionNode);
            }
            else
            {
                var sectionNode = new InspectionTreeItem
                {
                    Label = narrativeLabel + ". " + ideo.description.Replace("\r", "").Replace("\n", " ").Trim(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root,
                    Type = InspectionTreeItem.ItemType.Category
                };

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        AddChildNode(sectionNode, trimmed);
                }

                root.Children.Add(sectionNode);
            }
        }

        private static void BuildDeitiesSection(InspectionTreeItem root, Ideo ideo)
        {
            if (!(ideo.foundation is IdeoFoundation_Deity deityFoundation))
                return;

            var deities = deityFoundation.DeitiesListForReading;
            if (deities == null || deities.Count == 0)
                return;

            var sectionNode = new InspectionTreeItem
            {
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category
            };

            var aggregatedParts = new List<string>();

            foreach (var deity in deities)
            {
                string deityInfo = BuildDeityAnnouncement(deity);
                aggregatedParts.Add(deityInfo);
                AddChildNode(sectionNode, deityInfo).RingTarget = deity;
            }

            // Section label: "Deities. [deity1 info]. [deity2 info]..."
            var sb = new StringBuilder("Deities".Translate());
            foreach (string part in aggregatedParts)
                AppendSentence(sb, part);
            sectionNode.Label = sb.ToString();

            root.Children.Add(sectionNode);
        }

        private static string BuildDeityAnnouncement(IdeoFoundation_Deity.Deity deity)
        {
            var sb = new StringBuilder();
            sb.Append(deity.name);

            if (!string.IsNullOrEmpty(deity.type))
                AppendSentence(sb, deity.type);

            if (deity.gender != Gender.None)
                AppendSentence(sb, deity.gender.GetLabel().CapitalizeFirst());

            if (deity.relatedMeme != null)
                AppendSentence(sb, "RelatedToMeme".Translate() + ": " + deity.relatedMeme.LabelCap);

            return sb.ToString();
        }

        /// <summary>Precomputed presentation data for one precept, shared by every renderer of a precept node.</summary>
        private struct PreceptNodeData
        {
            public Precept Precept;
            public Def PreceptDef;
            public string FlatTipLabel;
            public string CombinedText;
            public List<PreceptTipLine> TipLines;
        }

        private static PreceptNodeData ComputePreceptNodeData(Precept precept)
        {
            string tipLabel = precept.TipLabel.StripTags();
            string fullTip = precept.GetTip().StripTags();

            if (!precept.def.grantedAbilities.NullOrEmpty())
                fullTip = EnhanceWithAbilityDescriptions(precept.def.grantedAbilities, precept.ideo, fullTip);

            Def preceptDef = GetPreceptDef(precept);

            // Flatten first: TipLabel can be multi-line for rituals.
            string flatTipLabel = tipLabel.Replace("\r", "").Replace("\n", " ").Trim();
            string flatFullTip = fullTip.Replace("\r", "").Replace("\n", " ").Trim();
            var combinedSb = new StringBuilder(flatTipLabel);
            AppendSentence(combinedSb, flatFullTip);
            string combinedText = combinedSb.ToString();

            string[] rawTipLines = fullTip.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var tipLines = ConsolidateTipLines(rawTipLines, precept);

            return new PreceptNodeData
            {
                Precept = precept,
                PreceptDef = preceptDef,
                FlatTipLabel = flatTipLabel,
                CombinedText = combinedText,
                TipLines = tipLines
            };
        }

        /// <summary>An expandable node for a multi-line precept tip, one child per consolidated line.</summary>
        private static InspectionTreeItem BuildPreceptNode(PreceptNodeData data, int indentLevel,
            InspectionTreeItem parent, InspectionTreeItem.ItemType nodeType)
        {
            var node = new InspectionTreeItem
            {
                Label = data.CombinedText,
                IndentLevel = indentLevel,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent,
                Type = nodeType,
                Data = data.PreceptDef,
                RingTarget = data.Precept
            };

            foreach (PreceptTipLine tipLine in data.TipLines)
            {
                string trimmed = tipLine.Text.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                object childData = tipLine.Data;
                string label = trimmed.StripTags();

                if (childData is ThingDef td && td.IsApparel && !string.IsNullOrEmpty(td.description))
                    label += ". " + td.description;

                AddChildNode(node, label, childData);
            }

            return node;
        }

        /// <summary>
        /// Whether one of the nine standard precept sections claims this PreceptDef. Mirrors that
        /// ladder's conditions exactly; used only to compute the residual "other precepts" bucket.
        /// </summary>
        private static bool IsClaimedByStandardPreceptSection(PreceptDef p)
        {
            return p.preceptClass == typeof(Precept)
                || typeof(Precept_Role).IsAssignableFrom(p.preceptClass)
                || p.preceptClass == typeof(Precept_Ritual)
                || p.preceptClass == typeof(Precept_Building) || p.preceptClass == typeof(Precept_RitualSeat)
                || p.preceptClass == typeof(Precept_Relic)
                || p.preceptClass == typeof(Precept_Weapon)
                || p.preceptClass == typeof(Precept_Animal)
                || p.preceptClass == typeof(Precept_Xenotype)
                || p.preceptClass == typeof(Precept_Apparel);
        }

        private static void BuildPreceptCategorySection(InspectionTreeItem root, Ideo ideo,
            string categoryLabel, Func<PreceptDef, bool> filter)
        {
            var matchingPrecepts = new List<Precept>();
            foreach (Precept precept in ideo.PreceptsListForReading)
            {
                if (precept.def.visible && filter(precept.def))
                    matchingPrecepts.Add(precept);
            }

            if (matchingPrecepts.Count == 0)
                return;

            // Flatten a single-precept section: "Weapons -> Weapons: Noble and despised" reads as
            // "Weapons: Noble and despised" at level 0 instead.
            if (matchingPrecepts.Count == 1)
            {
                var precept = matchingPrecepts[0];
                var data = ComputePreceptNodeData(precept);

                if (data.TipLines.Count > 1)
                {
                    var flatNode = BuildPreceptNode(data, 0, root, InspectionTreeItem.ItemType.Category);
                    root.Children.Add(flatNode);
                    return;
                }
            }

            var sectionNode = new InspectionTreeItem
            {
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category
            };

            var aggregatedParts = new List<string>();

            foreach (Precept precept in matchingPrecepts)
            {
                var data = ComputePreceptNodeData(precept);

                aggregatedParts.Add(data.FlatTipLabel);

                if (data.TipLines.Count > 1)
                {
                    var preceptNode = BuildPreceptNode(data, 1, sectionNode, InspectionTreeItem.ItemType.SubCategory);
                    sectionNode.Children.Add(preceptNode);
                }
                else
                {
                    AddChildNode(sectionNode, data.CombinedText, data.PreceptDef).RingTarget = data.Precept;
                }
            }

            // Section label: "CategoryLabel. [precept1 tipLabel]. [precept2 tipLabel]..."
            var sb = new StringBuilder(categoryLabel);
            foreach (string part in aggregatedParts)
                AppendSentence(sb, part);
            sectionNode.Label = sb.ToString();

            root.Children.Add(sectionNode);
        }

        private static void BuildAppearanceSection(InspectionTreeItem root, Ideo ideo)
        {
            if (ideo.style == null)
                return;

            int hairCount = ideo.style.NumHairAndBeardStylesAvailable;
            int tattooCount = ideo.style.NumTattooStylesAvailable;

            string hairSummary = "HairAndBeards".Translate() + ": " + "NumAvailable".Translate(hairCount.ToString())
                + ". " + "HairAndBeardsDesc".Translate();
            string tattooSummary = "Tattoos".Translate() + ": " + "NumAvailable".Translate(tattooCount.ToString())
                + ". " + "TattoosDesc".Translate();

            // The section label is summary counts, not all style names.
            var sectionNode = new InspectionTreeItem
            {
                Label = "Appearance".Translate().CapitalizeFirst() + ". " + hairSummary + ". " + tattooSummary,
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category,
                // The section spans both boxes; the leftmost is where its cursor reads as being.
                RingTarget = Shell.IdeoBoxDrawPatch.HairAndBeardKey
            };

            var hairNode = new InspectionTreeItem
            {
                Label = hairSummary,
                IndentLevel = 1,
                IsExpandable = true,
                IsExpanded = false,
                Parent = sectionNode,
                Type = InspectionTreeItem.ItemType.SubCategory,
                RingTarget = Shell.IdeoBoxDrawPatch.HairAndBeardKey
            };
            BuildStyleItemTypeNode(hairNode, ideo, "Hair".Translate(),
                DefDatabase<HairDef>.AllDefs.Cast<StyleItemDef>(), 2);
            BuildStyleItemTypeNode(hairNode, ideo, "Beard".Translate(),
                DefDatabase<BeardDef>.AllDefs.Cast<StyleItemDef>(), 2);
            sectionNode.Children.Add(hairNode);

            var tattooNode = new InspectionTreeItem
            {
                Label = tattooSummary,
                IndentLevel = 1,
                IsExpandable = true,
                IsExpanded = false,
                Parent = sectionNode,
                Type = InspectionTreeItem.ItemType.SubCategory,
                RingTarget = Shell.IdeoBoxDrawPatch.TattooKey
            };
            BuildStyleItemTypeNode(tattooNode, ideo, "TattooFace".Translate(),
                DefDatabase<TattooDef>.AllDefs.Where(t => t.tattooType == TattooType.Face).Cast<StyleItemDef>(), 2);
            BuildStyleItemTypeNode(tattooNode, ideo, "TattooBody".Translate(),
                DefDatabase<TattooDef>.AllDefs.Where(t => t.tattooType == TattooType.Body).Cast<StyleItemDef>(), 2);
            sectionNode.Children.Add(tattooNode);

            root.Children.Add(sectionNode);
        }

        /// <summary>A sub-node for one style item type, grouped by category, items sorted most-used first.</summary>
        private static void BuildStyleItemTypeNode(InspectionTreeItem parent, Ideo ideo,
            string typeLabel, IEnumerable<StyleItemDef> allDefs, int baseIndent)
        {
            var typeNode = new InspectionTreeItem
            {
                Label = typeLabel,
                IndentLevel = baseIndent,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent,
                Type = InspectionTreeItem.ItemType.SubCategory
            };

            var byCategory = allDefs
                .Where(d => d.StyleItemCategory != null)
                .GroupBy(d => d.StyleItemCategory)
                .OrderBy(g => g.Key.LabelCap.Resolve())
                .ToList();

            // Frequency order: Frequent(5) > Common(4) > Normal(3) > Uncommon(2) > Rare(1) > Never(0).
            foreach (var group in byCategory)
            {
                var catNode = new InspectionTreeItem
                {
                    Label = group.Key.LabelCap.Resolve(),
                    IndentLevel = baseIndent + 1,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = typeNode,
                    Type = InspectionTreeItem.ItemType.SubCategory
                };

                var sortedItems = group
                    .OrderByDescending(d => (int)ideo.style.GetFrequency(d))
                    .ThenBy(d => d.LabelCap.Resolve())
                    .ToList();

                foreach (var item in sortedItems)
                {
                    string itemLabel = BuildStyleItemLabel(ideo, item);
                    catNode.Children.Add(new InspectionTreeItem
                    {
                        Label = itemLabel,
                        IndentLevel = baseIndent + 2,
                        IsExpandable = false,
                        IsExpanded = false,
                        Parent = catNode,
                        Type = InspectionTreeItem.ItemType.DetailText
                    });
                }

                typeNode.Children.Add(catNode);
            }

            // Items with no category.
            var uncategorized = allDefs.Where(d => d.StyleItemCategory == null).ToList();
            if (uncategorized.Count > 0)
            {
                foreach (var item in uncategorized
                    .OrderByDescending(d => (int)ideo.style.GetFrequency(d))
                    .ThenBy(d => d.LabelCap.Resolve()))
                {
                    string itemLabel = BuildStyleItemLabel(ideo, item);
                    typeNode.Children.Add(new InspectionTreeItem
                    {
                        Label = itemLabel,
                        IndentLevel = baseIndent + 1,
                        IsExpandable = false,
                        IsExpanded = false,
                        Parent = typeNode,
                        Type = InspectionTreeItem.ItemType.DetailText
                    });
                }
            }

            parent.Children.Add(typeNode);
        }

        private static string BuildStyleItemLabel(Ideo ideo, StyleItemDef item)
        {
            var sb = new StringBuilder(item.LabelCap.Resolve());
            sb.Append(", " + ideo.style.GetFrequency(item).GetLabel());

            // Beards have no gender restriction; hair and tattoos do.
            if (!(item is BeardDef))
            {
                StyleGender gender = ideo.style.GetGender(item);
                if (gender == StyleGender.Male || gender == StyleGender.MaleUsually)
                    sb.Append(", " + Gender.Male.GetLabel().CapitalizeFirst());
                else if (gender == StyleGender.Female || gender == StyleGender.FemaleUsually)
                    sb.Append(", " + Gender.Female.GetLabel().CapitalizeFirst());
            }

            // Trailing visual description so the player knows what the style looks like.
            string description = StyleDescriptionHelper.Describe(item);
            if (!string.IsNullOrEmpty(description))
                sb.Append(". " + description);

            return sb.ToString();
        }

        #endregion

        #region Tree Building Utilities

        /// <summary>A level-0 section node whose label aggregates its children, which are added as level-1 leaves.</summary>
        private static InspectionTreeItem CreateSectionNode(InspectionTreeItem root,
            string sectionName, List<string> childTexts)
        {
            var node = new InspectionTreeItem
            {
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = root,
                Type = InspectionTreeItem.ItemType.Category,
                ExpandedLabel = sectionName
            };

            var sb = new StringBuilder(sectionName);
            foreach (string text in childTexts)
                AppendSentence(sb, text);
            node.Label = sb.ToString();

            foreach (string text in childTexts)
            {
                AddChildNode(node, text);
            }

            return node;
        }

        /// <summary>Returns the node it added, so a caller can tag it further (see <see cref="InspectionTreeItem.RingTarget"/>).</summary>
        private static InspectionTreeItem AddChildNode(InspectionTreeItem parent, string label, object data = null)
        {
            var node = new InspectionTreeItem
            {
                Label = label,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = false,
                IsExpanded = false,
                Parent = parent,
                Type = InspectionTreeItem.ItemType.DetailText,
                Data = data
            };
            parent.Children.Add(node);
            return node;
        }

        /// <summary>
        /// The Def behind a precept for info-card display, restricted to the types whose info cards
        /// say something the description does not already (ThingDef, XenotypeDef); null otherwise.
        /// </summary>
        private static Def GetPreceptDef(Precept precept)
        {
            if (precept is Precept_ThingDef ptd && ptd.ThingDef != null)
                return ptd.ThingDef;
            if (precept is Precept_Apparel pa && pa.apparelDef != null)
                return pa.apparelDef;
            if (precept is Precept_Xenotype px && px.xenotype != null)
                return px.xenotype;
            if (precept is Precept_Weapon)
                return null;
            return null;
        }

        /// <summary>
        /// Injects each granted ability's description inline with its bullet line in a role/ritual
        /// precept tip (the vanilla tip lists ability NAMES only). Shared with the IdeoBuilder typed-
        /// precept editor (<see cref="IdeoTypedPreceptState"/>) so the editor and the read-only viewer
        /// present abilities identically. Preserves every non-ability line unchanged.
        /// </summary>
        internal static string EnhanceWithAbilityDescriptions(List<AbilityDef> abilities, Ideo ideo, string tip)
        {
            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var abilityDef in abilities)
            {
                if (string.IsNullOrEmpty(abilityDef.description))
                    continue;

                var ritualComp = abilityDef.comps?.FirstOrDefault(c => c is CompProperties_AbilityStartRitual)
                    as CompProperties_AbilityStartRitual;

                if (ritualComp != null && ideo != null)
                {
                    // Ritual ability: the tip line shows the ritual precept label.
                    foreach (Precept p in ideo.PreceptsListForReading)
                    {
                        if (p is Precept_Ritual ritual && ritual.def == ritualComp.ritualDef)
                        {
                            string ritualLabel = ritual.LabelCap.StripTags();
                            descriptions[ritualLabel] = abilityDef.description;
                            break;
                        }
                    }
                }
                else
                {
                    // Normal ability: the tip line shows the ability label.
                    string label = abilityDef.LabelCap.Resolve().StripTags();
                    descriptions[label] = abilityDef.description;
                }
            }

            if (descriptions.Count == 0)
                return tip;

            var lines = tip.Split('\n');
            var result = new StringBuilder();
            string abilitiesHeader = "RoleGrantedAbilitiesLabel".Translate().Resolve().StripTags();
            bool inAbilitiesSection = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) result.Append('\n');
                string line = lines[i];
                string trimmed = line.TrimStart();

                // Only enhance under the "Abilities:" header.
                string strippedLine = trimmed.StripTags().TrimEnd();
                if (strippedLine.EndsWith(":") && !strippedLine.StartsWith("- "))
                {
                    string sectionName = strippedLine.TrimEnd(':').Trim();
                    inAbilitiesSection = sectionName == abilitiesHeader;
                }

                if (inAbilitiesSection && trimmed.StartsWith("- "))
                {
                    string name = trimmed.Substring(2).Trim();
                    if (descriptions.TryGetValue(name, out string desc))
                    {
                        result.Append(line + ". " + desc.Replace("\r", "").Replace("\n", " ").Trim());
                        continue;
                    }
                }
                result.Append(line);
            }
            return result.ToString();
        }

        /// <summary>
        /// One display-ready precept tip line with the structured Def data backing it. Data is
        /// attached at construction from the members vanilla's own tip builder reads, never by
        /// parsing the rendered text back apart.
        /// </summary>
        private struct PreceptTipLine
        {
            public string Text;
            public object Data;
        }

        /// <summary>
        /// Consolidates split tip lines by merging section headers with their items, pairing each
        /// line with the Def it is about. Single-item sections become "Header: Item", "Has role in
        /// rituals" always comma-separates, non-bullet text after a header merges into it, and
        /// multi-item sections stay header plus bullets.
        ///
        /// Data comes from the precept's own structured members, mirroring the exact loops vanilla
        /// uses to build the text, and never from reverse-parsing the tip string (which broke on
        /// labels containing ":" or ", " and on duplicate display names): granted abilities line up
        /// 1:1 with <c>precept.def.grantedAbilities</c>, rituals come from
        /// <see cref="GetRitualAbilityDefs"/>, required apparel lines up 1:1 with
        /// <c>Precept_Role.apparelRequirements</c>, and weapon dispositions pair with
        /// <c>Precept_Weapon.noble</c>/<c>despised</c>.
        /// </summary>
        private static List<PreceptTipLine> ConsolidateTipLines(string[] lines, Precept precept)
        {
            var result = new List<PreceptTipLine>();
            string ritualsHeader = "RoleRitualsLabel".Translate().Resolve().StripTags();
            string abilitiesHeader = "RoleGrantedAbilitiesLabel".Translate().Resolve().StripTags();
            string apparelHeader = "RoleRequiredApparelLabel".Translate().Resolve().StripTags();
            // Precept_Weapon.GetTip builds these headers without a Resolve() call, unlike the role
            // headers above; mirrored exactly so the comparison matches in locales where the raw
            // translation is not already capitalized.
            string nobleHeader = "Noble".Translate().Resolve().StripTags().CapitalizeFirst();
            string despisedHeader = "Despised".Translate().Resolve().StripTags().CapitalizeFirst();

            List<AbilityDef> grantedAbilities = precept.def.grantedAbilities;
            List<PreceptApparelRequirement> apparelRequirements = (precept as Precept_Role)?.apparelRequirements;
            List<Def> ritualAbilityDefs = grantedAbilities.NullOrEmpty()
                ? null
                : GetRitualAbilityDefs(precept.def, precept.ideo, grantedAbilities);

            int i = 0;
            while (i < lines.Length)
            {
                string current = lines[i].Trim();
                if (string.IsNullOrEmpty(current)) { i++; continue; }

                if (current.StripTags().TrimEnd().EndsWith(":"))
                {
                    string headerName = current.StripTags().TrimEnd().TrimEnd(':').Trim();

                    var bulletItems = new List<string>();
                    string nonBulletMerge = null;
                    int j = i + 1;

                    while (j < lines.Length)
                    {
                        string next = lines[j].Trim();
                        if (string.IsNullOrEmpty(next)) { j++; continue; }

                        string nextTrimStart = next.TrimStart();
                        if (nextTrimStart.StartsWith("- "))
                        {
                            bulletItems.Add(nextTrimStart.Substring(2).Trim());
                            j++;
                        }
                        else if (bulletItems.Count > 0 && !next.StripTags().TrimEnd().EndsWith(":"))
                        {
                            // Continuation text after a bullet item merges with that item.
                            bulletItems[bulletItems.Count - 1] += ". " + next.Trim();
                            j++;
                        }
                        else if (bulletItems.Count == 0 && !next.StripTags().TrimEnd().EndsWith(":"))
                        {
                            nonBulletMerge = next;
                            j++;
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (nonBulletMerge != null)
                    {
                        object mergedData = null;
                        if (precept is Precept_Weapon pw)
                        {
                            if (headerName == nobleHeader && pw.noble != null)
                                mergedData = GetWeaponThingDefs(pw.noble);
                            else if (headerName == despisedHeader && pw.despised != null)
                                mergedData = GetWeaponThingDefs(pw.despised);
                        }
                        result.Add(new PreceptTipLine { Text = headerName + ": " + nonBulletMerge, Data = mergedData });
                    }
                    else if (bulletItems.Count == 0)
                    {
                        result.Add(new PreceptTipLine { Text = current, Data = null });
                    }
                    else if (headerName == ritualsHeader)
                    {
                        result.Add(new PreceptTipLine
                        {
                            Text = headerName + ": " + string.Join(", ", bulletItems),
                            Data = ritualAbilityDefs
                        });
                    }
                    else if (bulletItems.Count == 1)
                    {
                        object itemData = GetTipItemData(headerName, 0, abilitiesHeader, apparelHeader,
                            grantedAbilities, apparelRequirements);
                        result.Add(new PreceptTipLine { Text = headerName + ": " + bulletItems[0], Data = itemData });
                    }
                    else
                    {
                        result.Add(new PreceptTipLine { Text = current, Data = null });
                        for (int k = 0; k < bulletItems.Count; k++)
                        {
                            object itemData = GetTipItemData(headerName, k, abilitiesHeader, apparelHeader,
                                grantedAbilities, apparelRequirements);
                            result.Add(new PreceptTipLine { Text = "- " + bulletItems[k], Data = itemData });
                        }
                    }

                    i = j;
                }
                else
                {
                    result.Add(new PreceptTipLine { Text = current, Data = null });
                    i++;
                }
            }

            return result;
        }

        /// <summary>
        /// The Def behind the index-th bullet under a tip section header, found by structural
        /// position rather than by matching rendered text. Only the two sections whose items line up
        /// 1:1 with a list of Defs come through here; every other section has no per-item data.
        /// </summary>
        private static object GetTipItemData(string headerName, int index, string abilitiesHeader, string apparelHeader,
            List<AbilityDef> grantedAbilities, List<PreceptApparelRequirement> apparelRequirements)
        {
            if (headerName == abilitiesHeader && !grantedAbilities.NullOrEmpty() && index < grantedAbilities.Count)
                return grantedAbilities[index];

            if (headerName == apparelHeader && !apparelRequirements.NullOrEmpty() && index < apparelRequirements.Count)
                return GetApparelRequirementThingDef(apparelRequirements[index].requirement);

            return null;
        }

        /// <summary>
        /// Mirrors Precept_Role.GetTip's "RoleRitualsLabel" loop exactly to find the same ritual
        /// precepts vanilla lists, then attaches each one's AbilityDef whenever one of this role's
        /// granted abilities starts that ritual — matched by def identity, never by re-parsing
        /// label text.
        /// </summary>
        private static List<Def> GetRitualAbilityDefs(PreceptDef def, Ideo ideo, List<AbilityDef> grantedAbilities)
        {
            if (ideo == null)
                return null;

            var result = new List<Def>();
            var seenLabels = new HashSet<string>();
            foreach (Precept item in ideo.PreceptsListForReading)
            {
                if (!(item is Precept_Ritual ritual) || !item.def.listedForRoles)
                    continue;
                if (ritual.behavior?.def.stages == null || ritual.behavior.def.roles == null)
                    continue;
                if (!ritual.behavior.def.roles.Any(r => r.precept == def))
                    continue;
                if (!seenLabels.Add(ritual.LabelCap))
                    continue;

                AbilityDef abilityDef = grantedAbilities?.FirstOrDefault(a =>
                    (a.comps?.FirstOrDefault(c => c is CompProperties_AbilityStartRitual)
                        as CompProperties_AbilityStartRitual)?.ritualDef == ritual.def);
                if (abilityDef != null)
                    result.Add(abilityDef);
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// The ThingDef to attach as Alt+I data for a required-apparel line. Mirrors
        /// Precept_Role.AllApparelRequirementLabels, which names single-item requirements using
        /// Gender.Male regardless of the assigned pawn; group requirements have no single named
        /// apparel in vanilla's text, so the group's first item stands in.
        /// </summary>
        private static ThingDef GetApparelRequirementThingDef(ApparelRequirement requirement)
        {
            if (!requirement.groupLabel.NullOrEmpty())
                return requirement.AllRequiredApparel().FirstOrDefault();
            return requirement.AllRequiredApparel(Gender.Male).FirstOrDefault();
        }

        /// <summary>Every weapon ThingDef in a WeaponClassDef, using Precept_Weapon.GetTip's own filter.</summary>
        private static List<Def> GetWeaponThingDefs(WeaponClassDef weaponClass)
        {
            return DefDatabase<ThingDef>.AllDefs
                .Where(x => x.IsWeapon && !x.weaponClasses.NullOrEmpty()
                    && x.weaponClasses.Contains(weaponClass)
                    && x.canGenerateDefaultDesignator)
                .Cast<Def>()
                .ToList();
        }

        /// <summary>Appends text as a new sentence, without doubling periods.</summary>
        public static void AppendSentence(StringBuilder sb, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            text = text.TrimStart('.', ' ');
            if (string.IsNullOrEmpty(text))
                return;

            if (sb.Length > 0)
            {
                char lastChar = sb[sb.Length - 1];
                if (lastChar != '.' && lastChar != '!' && lastChar != '?')
                    sb.Append('.');
                sb.Append(' ');
            }

            sb.Append(text);
        }

        #endregion
    }
}
