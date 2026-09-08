using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Shared faction-data builders for the pre-game landing surface and the in-game tab.</summary>
    public static class FactionHelper
    {
        // FactionUIUtility's private static "DEV: Show all" flag: the enemy-of filter needs it
        // directly, since vanilla's DrawFactionRow reads the field in-class.
        private static readonly FieldInfo ShowAllField = AccessTools.Field(typeof(FactionUIUtility), "showAll");

        /// <summary>
        /// The live "DEV: Show all" state. No Prefs.DevMode gate: vanilla's DoWindowContents forces
        /// the field false on every draw pass outside dev mode, and it keeps drawing underneath both
        /// surfaces that read this.
        /// </summary>
        internal static bool DevShowAll => ShowAllField != null && (bool)ShowAllField.GetValue(null);

        /// <summary>Flips "DEV: Show all" for the keyboard surfaces mirroring vanilla's checkbox.</summary>
        internal static void SetDevShowAll(bool value)
        {
            // MUTATION-C: mirrors FactionUIUtility's "DEV: Show all" CheckboxLabeled,
            // which writes the private static showAll by ref (decompiled :49); no gated
            // setter exists.
            ShowAllField?.SetValue(null, value);
        }

        /// <summary>
        /// Visible non-player, non-hidden factions in RimWorld's own view order: defeated ascending,
        /// then listOrderPriority descending. <paramref name="showAll"/> mirrors vanilla's
        /// <c>(!item.IsPlayer &amp;&amp; !item.Hidden) || showAll</c> gate.
        /// </summary>
        public static List<Faction> BuildFactionList(bool showAll = false)
        {
            var result = new List<Faction>();
            foreach (Faction faction in Find.FactionManager.AllFactionsInViewOrder)
            {
                if ((!faction.IsPlayer && !faction.Hidden) || showAll)
                {
                    result.Add(faction);
                }
            }
            return result;
        }

        /// <summary>The full spoken line for a faction, from its name through its enemy-of list.</summary>
        public static string BuildFactionAnnouncement(Faction faction)
        {
            var sb = new StringBuilder();

            sb.Append(faction.Name.CapitalizeFirst());

            if (faction.defeated)
            {
                sb.Append(", ");
                sb.Append("RimWorldAccess.Factions.Status.Defeated".Translate());
            }

            AppendSentence(sb, faction.def.LabelCap.Resolve());

            if (faction.leader != null)
            {
                string leaderTitle = faction.LeaderTitle.CapitalizeFirst();
                string leaderName = faction.leader.Name.ToStringFull;
                AppendSentence(sb, "RimWorldAccess.Factions.Leader".Translate(leaderTitle, leaderName));
            }

            string relation = faction.PlayerRelationKind.GetLabelCap();
            if (faction.HasGoodwill && !faction.def.permanentEnemy)
            {
                AppendSentence(sb, "RimWorldAccess.Factions.Goodwill".Translate(
                    relation,
                    faction.PlayerGoodwill.ToStringWithSign(),
                    faction.NaturalGoodwill.ToStringWithSign()));

                string ongoing = BuildOngoingEvents(faction);
                if (!string.IsNullOrEmpty(ongoing))
                    AppendSentence(sb, ongoing);

                string recent = BuildRecentEvents(faction);
                if (!string.IsNullOrEmpty(recent))
                    AppendSentence(sb, recent);

                // Vanilla shows the relation-kind meaning and natural-goodwill breakdown on hover only.
                string goodwillTip = BuildGoodwillTooltipDetail(faction);
                if (!string.IsNullOrEmpty(goodwillTip))
                    AppendSentence(sb, goodwillTip);
            }
            else if (faction.def.permanentEnemy)
            {
                AppendSentence(sb, "RimWorldAccess.Factions.RelationPermanentEnemy".Translate(relation));

                string relationTip = BuildRelationKindTip(faction);
                if (!string.IsNullOrEmpty(relationTip))
                    AppendSentence(sb, relationTip);
            }
            else
            {
                AppendSentence(sb, relation);
            }

            if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && faction.ideos != null)
            {
                if (faction.ideos.PrimaryIdeo != null)
                {
                    AppendSentence(sb, "RimWorldAccess.Factions.Ideology.Primary".Translate(faction.ideos.PrimaryIdeo.name));
                }

                var minor = faction.ideos.IdeosMinorListForReading;
                if (minor != null && minor.Count > 0)
                {
                    var minorNames = minor.Select(i => i.name);
                    AppendSentence(sb, "RimWorldAccess.Factions.Ideology.Minor".Translate(string.Join(", ", minorNames)));
                }
            }

            // Mirrors vanilla's own showAll term, so "DEV: Show all" reveals hidden/player enemies
            // here too, not just in the faction list.
            var enemies = Find.FactionManager.AllFactionsInViewOrder
                .Where(f => f != faction && f.HostileTo(faction) && ((!f.IsPlayer && !f.Hidden) || DevShowAll))
                .ToArray();

            if (enemies.Length > 0)
            {
                var enemyNames = enemies.Select(f => f.Name).ToArray();
                AppendSentence(sb, "RimWorldAccess.Factions.EnemyOf".Translate(string.Join(", ", enemyNames)));
            }

            string description = faction.def.Description;
            if (!string.IsNullOrEmpty(description))
                AppendSentence(sb, description);

            return sb.ToString();
        }

        /// <summary>Appends text as a new sentence, adding a separator only where one is missing.</summary>
        public static void AppendSentence(StringBuilder sb, string text)
        {
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

        /// <summary>Ongoing goodwill situations that cap max goodwill below 100, or null if none.</summary>
        internal static string BuildOngoingEvents(Faction faction)
        {
            var situations = Find.GoodwillSituationManager.GetSituations(faction);
            var parts = new List<string>();

            for (int i = 0; i < situations.Count; i++)
            {
                if (situations[i].maxGoodwill < 100)
                {
                    string label = situations[i].def.Worker.GetPostProcessedLabelCap(faction);
                    parts.Add("RimWorldAccess.Factions.Ongoing.EntryMaxSuffix".Translate(
                        label,
                        situations[i].maxGoodwill.ToStringWithSign()).ToString());
                }
            }

            if (parts.Count == 0)
                return null;

            return "RimWorldAccess.Factions.Ongoing.Summary".Translate(string.Join(", ", parts)).ToString();
        }

        /// <summary>Goodwill-affecting events from the last ~60 in-game days, or null if none.</summary>
        internal static string BuildRecentEvents(Faction faction)
        {
            var allEventDefs = DefDatabase<HistoryEventDef>.AllDefsListForReading;
            var tmpTicks = new List<int>();
            var tmpCustomGoodwill = new List<int>();
            var parts = new List<string>();

            for (int i = 0; i < allEventDefs.Count; i++)
            {
                int recentCount = Find.HistoryEventsManager.GetRecentCountWithinTicks(
                    allEventDefs[i], 3600000, faction);

                if (recentCount <= 0)
                    continue;

                tmpTicks.Clear();
                tmpCustomGoodwill.Clear();
                Find.HistoryEventsManager.GetRecent(
                    allEventDefs[i], 3600000, tmpTicks, tmpCustomGoodwill, faction);

                int totalGoodwill = 0;
                for (int j = 0; j < tmpCustomGoodwill.Count; j++)
                {
                    totalGoodwill += tmpCustomGoodwill[j];
                }

                if (totalGoodwill != 0)
                {
                    string entry = allEventDefs[i].LabelCap.ToString();
                    if (recentCount != 1)
                        entry = "RimWorldAccess.Factions.Recent.EntryCountSuffix".Translate(entry, recentCount).ToString();
                    entry = "RimWorldAccess.Factions.Recent.EntrySignedSuffix".Translate(entry, totalGoodwill.ToStringWithSign()).ToString();
                    parts.Add(entry);
                }
            }

            if (parts.Count == 0)
                return null;

            return "RimWorldAccess.Factions.Recent.Summary".Translate(string.Join(", ", parts)).ToString();
        }

        /// <summary>
        /// A faction treeview: hidden root, one collapsible node per faction labelled with its full
        /// announcement, section children beneath, and the description expanding line by line.
        /// </summary>
        public static InspectionTreeItem BuildFactionTree(List<Faction> factions)
        {
            var root = new InspectionTreeItem
            {
                Label = "RimWorldAccess.Factions.Tree.Root".Translate(),
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true
            };

            foreach (var faction in factions)
            {
                var factionNode = BuildFactionNode(faction);
                factionNode.Parent = root;
                root.Children.Add(factionNode);
            }

            return root;
        }

        private static InspectionTreeItem BuildFactionNode(Faction faction)
        {
            var node = new InspectionTreeItem
            {
                Label = BuildFactionAnnouncement(faction),
                ExpandedLabel = faction.Name,
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Data = faction,
                Type = InspectionTreeItem.ItemType.Category
            };

            AddChildNode(node, faction.def.LabelCap.Resolve());

            if (faction.defeated)
                AddChildNode(node, "RimWorldAccess.Factions.Status.DefeatedCap".Translate());

            if (faction.leader != null)
            {
                string leaderTitle = faction.LeaderTitle.CapitalizeFirst();
                string leaderName = faction.leader.Name.ToStringFull;
                AddChildNode(node, "RimWorldAccess.Factions.Leader".Translate(leaderTitle, leaderName));
            }

            string relation = faction.PlayerRelationKind.GetLabelCap();
            if (faction.HasGoodwill && !faction.def.permanentEnemy)
            {
                AddChildNode(node, "RimWorldAccess.Factions.Goodwill".Translate(
                    relation,
                    faction.PlayerGoodwill.ToStringWithSign(),
                    faction.NaturalGoodwill.ToStringWithSign()));

                string ongoing = BuildOngoingEvents(faction);
                if (!string.IsNullOrEmpty(ongoing))
                    AddChildNode(node, ongoing);

                string recent = BuildRecentEvents(faction);
                if (!string.IsNullOrEmpty(recent))
                    AddChildNode(node, recent);

                string goodwillTip = BuildGoodwillTooltipDetail(faction);
                if (!string.IsNullOrEmpty(goodwillTip))
                    AddChildNode(node, goodwillTip);
            }
            else if (faction.def.permanentEnemy)
            {
                AddChildNode(node, "RimWorldAccess.Factions.RelationPermanentEnemy".Translate(relation));

                string relationTip = BuildRelationKindTip(faction);
                if (!string.IsNullOrEmpty(relationTip))
                    AddChildNode(node, relationTip);
            }
            else
            {
                AddChildNode(node, relation);
            }

            if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && faction.ideos != null)
            {
                if (faction.ideos.PrimaryIdeo != null)
                    AddChildNode(node, "RimWorldAccess.Factions.Ideology.Primary".Translate(faction.ideos.PrimaryIdeo.name));

                var minor = faction.ideos.IdeosMinorListForReading;
                if (minor != null && minor.Count > 0)
                {
                    var minorNames = minor.Select(i => i.name);
                    AddChildNode(node, "RimWorldAccess.Factions.Ideology.Minor".Translate(string.Join(", ", minorNames)));
                }
            }

            // Mirrors vanilla's own showAll term, so "DEV: Show all" reveals hidden/player enemies
            // here too.
            var enemies = Find.FactionManager.AllFactionsInViewOrder
                .Where(f => f != faction && f.HostileTo(faction) && ((!f.IsPlayer && !f.Hidden) || DevShowAll))
                .ToArray();
            if (enemies.Length > 0)
            {
                var enemyNames = enemies.Select(f => f.Name).ToArray();
                AddChildNode(node, "RimWorldAccess.Factions.EnemyOf".Translate(string.Join(", ", enemyNames)));
            }

            string description = faction.def.Description;
            if (!string.IsNullOrEmpty(description))
            {
                string[] lines = description.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (lines.Length <= 1)
                {
                    AddChildNode(node, description.Trim());
                }
                else
                {
                    var descNode = new InspectionTreeItem
                    {
                        Label = description.Replace("\r", "").Replace("\n", " ").Trim(),
                        IndentLevel = 1,
                        IsExpandable = true,
                        IsExpanded = false,
                        Parent = node,
                        Type = InspectionTreeItem.ItemType.SubCategory
                    };

                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            AddChildNode(descNode, trimmed);
                    }

                    if (descNode.Children.Count > 0)
                        node.Children.Add(descNode);
                }
            }

            return node;
        }

        /// <summary>
        /// The "what this relation kind means" explanation vanilla shows only on hover over the
        /// goodwill number. The thresholds (0, -75, 75) are vanilla's own literal switch-branch
        /// arguments, mirrored verbatim.
        /// </summary>
        internal static string BuildRelationKindTip(Faction faction)
        {
            if (faction.def.permanentEnemy)
                return "CurrentGoodwillTip_PermanentEnemy".Translate().ToString();

            if (!faction.HasGoodwill)
                return null;

            switch (faction.PlayerRelationKind)
            {
                case FactionRelationKind.Ally:
                    return "CurrentGoodwillTip_Ally".Translate(0.ToString("F0")).ToString();
                case FactionRelationKind.Neutral:
                    return "CurrentGoodwillTip_Neutral".Translate((-75).ToString("F0"), 75.ToString("F0")).ToString();
                case FactionRelationKind.Hostile:
                    return "CurrentGoodwillTip_Hostile".Translate(0.ToString("F0")).ToString();
                default:
                    return null;
            }
        }

        /// <summary>
        /// Vanilla's "Affected by:" natural-goodwill offsets as a flat comma list rather than newline
        /// bullets; null when no situation offsets natural goodwill.
        /// </summary>
        private static string BuildAffectedByEntries(Faction faction)
        {
            var situations = Find.GoodwillSituationManager.GetSituations(faction);
            var parts = new List<string>();
            for (int i = 0; i < situations.Count; i++)
            {
                if (situations[i].naturalGoodwillOffset != 0)
                {
                    string label = situations[i].def.Worker.GetPostProcessedLabelCap(faction);
                    parts.Add(label + ": " + situations[i].naturalGoodwillOffset.ToStringWithSign());
                }
            }
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        /// <summary>
        /// Vanilla's natural-goodwill badge tooltip: the range, the "Affected by" breakdown and the
        /// decay-rate description. The 1.25 decay multiplier is vanilla's own hardcoded literal at
        /// that call site, not a def value reflection could harvest.
        /// </summary>
        private static string BuildNaturalGoodwillBreakdown(Faction faction)
        {
            var parts = new List<string>();

            int rangeMin = Mathf.Clamp(faction.NaturalGoodwill - 50, -100, 100);
            int rangeMax = Mathf.Clamp(faction.NaturalGoodwill + 50, -100, 100);
            parts.Add("RimWorldAccess.Factions.NaturalGoodwillRange".Translate(rangeMin, rangeMax).ToString());

            string affectedBy = BuildAffectedByEntries(faction);
            if (!string.IsNullOrEmpty(affectedBy))
                parts.Add("RimWorldAccess.Factions.AffectedBy".Translate(affectedBy).ToString());

            parts.Add("NaturalGoodwillDescription".Translate(1.25f.ToStringPercent()).ToString());

            return string.Join(" ", parts);
        }

        /// <summary>
        /// The supplementary suffix after the Goodwill line: vanilla splits this across two hover
        /// tooltips, but the announcement model has one combined goodwill element, so both are
        /// appended to it in vanilla's reading order.
        /// </summary>
        internal static string BuildGoodwillTooltipDetail(Faction faction)
        {
            var parts = new List<string>();

            string relationTip = BuildRelationKindTip(faction);
            if (!string.IsNullOrEmpty(relationTip))
                parts.Add(relationTip);

            string naturalBreakdown = BuildNaturalGoodwillBreakdown(faction);
            if (!string.IsNullOrEmpty(naturalBreakdown))
                parts.Add(naturalBreakdown);

            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }

        private static void AddChildNode(InspectionTreeItem parent, string label)
        {
            parent.Children.Add(new InspectionTreeItem
            {
                Label = label,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = false,
                IsExpanded = false,
                Parent = parent,
                Type = InspectionTreeItem.ItemType.DetailText
            });
        }
    }
}
