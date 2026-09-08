using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reads RimWorld's Dialog_InfoCard and related utilities by reflection, producing the
    /// structured data InfoCardTreeBuilder consumes.
    /// </summary>
    public static class InfoCardDataExtractor
    {
        private static FieldInfo cachedDrawEntriesField;
        private static MethodInfo statsToDrawForThingMethod;
        private static FieldInfo dialogThingField;
        private static FieldInfo dialogTabField;
        private static FieldInfo dialogDefField;
        private static FieldInfo dialogWorldObjectField;
        private static FieldInfo dialogHediffField;
        private static FieldInfo dialogTitleDefField;
        private static FieldInfo dialogFactionField;
        private static FieldInfo dialogStuffField;

        static InfoCardDataExtractor()
        {
            cachedDrawEntriesField = typeof(StatsReportUtility).GetField(
                "cachedDrawEntries",
                BindingFlags.NonPublic | BindingFlags.Static
            );

            // The private generator behind DrawStatsReport(Rect, Thing); see GetStatEntriesFor.
            statsToDrawForThingMethod = typeof(StatsReportUtility).GetMethod(
                "StatsToDraw",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Thing) },
                null
            );

            dialogThingField = typeof(Dialog_InfoCard).GetField(
                "thing",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogTabField = typeof(Dialog_InfoCard).GetField(
                "tab",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogDefField = typeof(Dialog_InfoCard).GetField(
                "def",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogWorldObjectField = typeof(Dialog_InfoCard).GetField(
                "worldObject",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogHediffField = typeof(Dialog_InfoCard).GetField(
                "hediff",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogTitleDefField = typeof(Dialog_InfoCard).GetField(
                "titleDef",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogFactionField = typeof(Dialog_InfoCard).GetField(
                "faction",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogStuffField = typeof(Dialog_InfoCard).GetField(
                "stuff",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
        }

        public static List<StatDrawEntry> GetStatEntries()
        {
            try
            {
                if (cachedDrawEntriesField == null)
                {
                    Log.Warning("[InfoCardDataExtractor] cachedDrawEntries field not found");
                    return new List<StatDrawEntry>();
                }

                var entries = cachedDrawEntriesField.GetValue(null) as List<StatDrawEntry>;
                return entries ?? new List<StatDrawEntry>();
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting stat entries: {ex.Message}");
                return new List<StatDrawEntry>();
            }
        }

        /// <summary>
        /// Stat entries for one specific thing, independent of
        /// <see cref="StatsReportUtility"/>'s shared cache — which <see cref="GetStatEntries"/>
        /// reads, and which is only right while the info card that filled it is the surface
        /// being read. Reproduces vanilla's <c>DrawStatsReport(Rect, Thing)</c> population step
        /// (public <c>SpecialDisplayStats</c> plus the private <c>StatsToDraw(Thing)</c>
        /// generator, same predicate), then applies <c>FinalizeCachedDrawEntries</c>' ordering
        /// locally rather than calling it, since that method would clobber an open info card's
        /// cache. Returns an empty list when the private generator cannot be resolved.
        /// </summary>
        public static List<StatDrawEntry> GetStatEntriesFor(Thing thing)
        {
            var entries = new List<StatDrawEntry>();

            if (thing == null)
                return entries;

            try
            {
                if (statsToDrawForThingMethod == null)
                {
                    Log.Warning("[InfoCardDataExtractor] StatsReportUtility.StatsToDraw(Thing) not found");
                    return entries;
                }

                entries.AddRange(thing.def.SpecialDisplayStats(StatRequest.For(thing)));
                var generated = statsToDrawForThingMethod.Invoke(null, new object[] { thing })
                    as IEnumerable<StatDrawEntry>;
                if (generated != null)
                    entries.AddRange(generated);

                entries.RemoveAll(de => (de.stat != null && !de.stat.showNonAbstract) || !de.ShouldDisplay(thing));

                // Mirrors StatsReportUtility.FinalizeCachedDrawEntries' ordering.
                entries = entries
                    .OrderBy(de => de.category.displayOrder)
                    .ThenByDescending(de => de.DisplayPriorityWithinCategory)
                    .ThenBy(de => de.LabelCap)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error building stat entries: {ex.Message}");
            }

            return entries;
        }

        public static Thing GetThing(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogThingField == null)
                    return null;

                return dialogThingField.GetValue(dialog) as Thing;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting thing: {ex.Message}");
                return null;
            }
        }

        /// <summary>The displayed pawn, or null when the displayed thing is not one.</summary>
        public static Pawn GetPawn(Dialog_InfoCard dialog)
        {
            return GetThing(dialog) as Pawn;
        }

        public static Def GetDef(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogDefField == null)
                    return null;

                return dialogDefField.GetValue(dialog) as Def;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting def: {ex.Message}");
                return null;
            }
        }

        public static WorldObject GetWorldObject(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogWorldObjectField == null)
                    return null;

                return dialogWorldObjectField.GetValue(dialog) as WorldObject;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting worldObject: {ex.Message}");
                return null;
            }
        }

        public static Hediff GetHediff(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogHediffField == null)
                    return null;

                return dialogHediffField.GetValue(dialog) as Hediff;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting hediff: {ex.Message}");
                return null;
            }
        }

        public static RoyalTitleDef GetTitleDef(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogTitleDefField == null)
                    return null;

                return dialogTitleDefField.GetValue(dialog) as RoyalTitleDef;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting titleDef: {ex.Message}");
                return null;
            }
        }

        public static Faction GetFaction(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogFactionField == null)
                    return null;

                return dialogFactionField.GetValue(dialog) as Faction;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting faction: {ex.Message}");
                return null;
            }
        }

        public static ThingDef GetStuff(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogStuffField == null)
                    return null;

                return dialogStuffField.GetValue(dialog) as ThingDef;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting stuff: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// A hyperlink's display name, mirroring vanilla's <c>Dialog_InfoCard.Hyperlink.Label</c>
        /// getter (Verse/Dialog_InfoCard.cs ~73-124) including its hidden-item substitution, so
        /// an undiscovered item's real name is never spoken. Null only when the hyperlink
        /// carries none of its known shapes.
        /// </summary>
        public static string GetHyperlinkLabel(Dialog_InfoCard.Hyperlink link)
        {
            if (link.IsHidden)
                return "(" + "NotYetDiscovered".Translate() + ")";
            if (link.worldObject != null)
                return link.worldObject.Label;
            if (link.def != null && link.def is ThingDef thingDef && link.stuff != null)
                return thingDef.label;
            if (link.def != null)
                return link.def.label;
            if (link.thing != null && !link.thingIsGeneOwner)
                return link.thing.Label;
            if (link.titleDef != null)
                return link.titleDef.GetLabelCapForBothGenders();
            if (link.quest != null)
                return link.quest.name;
            if (link.ideo != null)
                return link.ideo.name;
            if (link.researchProject != null)
                return link.researchProject.label;
            if (link.faction != null)
                return link.faction.Name;
            if (link.HasGeneOwnerThing)
                return (string)"InspectGenes".Translate();
            return null;
        }

        public static Dialog_InfoCard.InfoCardTab GetCurrentTab(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogTabField == null)
                    return Dialog_InfoCard.InfoCardTab.Stats;

                return (Dialog_InfoCard.InfoCardTab)dialogTabField.GetValue(dialog);
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting tab: {ex.Message}");
                return Dialog_InfoCard.InfoCardTab.Stats;
            }
        }

        public static List<Dialog_InfoCard.InfoCardTab> GetAvailableTabs(Dialog_InfoCard dialog)
        {
            var tabs = new List<Dialog_InfoCard.InfoCardTab>();

            // Stats always available
            tabs.Add(Dialog_InfoCard.InfoCardTab.Stats);

            var pawn = GetPawn(dialog);
            if (pawn != null)
            {
                // Character only for humanlike
                if (pawn.RaceProps.Humanlike)
                {
                    tabs.Add(Dialog_InfoCard.InfoCardTab.Character);
                }

                // Health for all pawns
                tabs.Add(Dialog_InfoCard.InfoCardTab.Health);

                // Permits for Royalty DLC + humanlike + player faction
                // Must also check selectedFaction != null (RimWorld bug: crashes if null)
                // And exclude quest lodgers (per RimWorld's own logic)
                if (ModsConfig.RoyaltyActive &&
                    pawn.RaceProps.Humanlike &&
                    pawn.Faction == Faction.OfPlayer &&
                    !pawn.IsQuestLodger() &&
                    pawn.royalty != null &&
                    PermitsCardUtility.selectedFaction != null)
                {
                    tabs.Add(Dialog_InfoCard.InfoCardTab.Permits);
                }

                // Records for all pawns
                tabs.Add(Dialog_InfoCard.InfoCardTab.Records);
            }

            return tabs;
        }

        public static List<(string title, string description)> GetBackstoryInfo(Pawn pawn)
        {
            var info = new List<(string, string)>();

            if (pawn?.story == null)
                return info;

            try
            {
                if (pawn.story.Childhood != null)
                {
                    string title = pawn.story.Childhood.TitleCapFor(pawn.gender);
                    string desc = pawn.story.Childhood.FullDescriptionFor(pawn).Resolve();
                    info.Add(($"{"Childhood".Translate()}: {title}", desc));
                }

                if (pawn.story.Adulthood != null)
                {
                    string title = pawn.story.Adulthood.TitleCapFor(pawn.gender);
                    string desc = pawn.story.Adulthood.FullDescriptionFor(pawn).Resolve();
                    info.Add(($"{"Adulthood".Translate()}: {title}", desc));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting backstory: {ex.Message}");
            }

            return info;
        }

        public static List<(string label, string description, bool suppressed)> GetTraitsInfo(Pawn pawn)
        {
            var traits = new List<(string, string, bool)>();

            if (pawn?.story?.traits == null)
                return traits;

            try
            {
                foreach (var trait in pawn.story.traits.allTraits)
                {
                    string label = trait.LabelCap;
                    string desc = trait.TipString(pawn);
                    bool suppressed = trait.Suppressed;
                    traits.Add((label, desc, suppressed));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting traits: {ex.Message}");
            }

            return traits;
        }

        public static List<(SkillDef def, int level, Passion passion, bool disabled, string levelDesc)> GetSkillsInfo(Pawn pawn)
        {
            var skills = new List<(SkillDef, int, Passion, bool, string)>();

            if (pawn?.skills == null)
                return skills;

            try
            {
                foreach (var skillDef in DefDatabase<SkillDef>.AllDefsListForReading)
                {
                    var skill = pawn.skills.GetSkill(skillDef);
                    if (skill != null)
                    {
                        skills.Add((
                            skillDef,
                            skill.Level,
                            skill.passion,
                            skill.TotallyDisabled,
                            skill.LevelDescriptor
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting skills: {ex.Message}");
            }

            return skills;
        }

        /// <summary>
        /// Gets age display lines for a pawn: a summary line (biological age, with the
        /// chronological age in parentheses when they differ) followed by the birth date and
        /// the chronological/biological breakdown. Mirrors the vanilla character card's age
        /// field and its hover tooltip. The debug tail that <c>AgeTooltipString</c> appends
        /// when <c>Prefs.DevMode</c> is enabled is stripped out.
        /// </summary>
        public static List<string> GetAgeInfo(Pawn pawn)
        {
            var lines = new List<string>();
            if (pawn?.ageTracker == null)
                return lines;

            try
            {
                lines.Add("RimWorldAccess.Inspection.Pawn.Age".Translate(pawn.ageTracker.AgeNumberString));

                string tooltip = pawn.ageTracker.AgeTooltipString;
                if (!string.IsNullOrEmpty(tooltip))
                {
                    int devIdx = tooltip.IndexOf("\n\nDev mode info:", StringComparison.Ordinal);
                    if (devIdx >= 0)
                        tooltip = tooltip.Substring(0, devIdx);

                    foreach (var line in tooltip.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = line.StripTags().Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            lines.Add(trimmed);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting age info: {ex.Message}");
            }

            return lines;
        }

        /// <summary>
        /// Incapable work tags, each with its label (inline causes included) and affected work
        /// type defs, in the vanilla CharacterCardUtility tooltip's structure.
        /// </summary>
        public static List<(string tagLabel, List<WorkTypeDef> affectedWorkTypes)> GetIncapableWorkTagsInfo(Pawn pawn)
        {
            var result = new List<(string, List<WorkTypeDef>)>();

            if (pawn?.story == null)
                return result;

            try
            {
                WorkTags disabled = pawn.CombinedDisabledWorkTags;
                if (disabled == WorkTags.None)
                    return result;

                foreach (WorkTags tag in disabled.GetAllSelectedItems<WorkTags>())
                {
                    if (tag == WorkTags.None)
                        continue;

                    string tagLabel = tag.LabelTranslated().CapitalizeFirst();

                    // Build inline cause string
                    string causeStr = GetCauseString(pawn, tag);
                    if (!string.IsNullOrEmpty(causeStr))
                        tagLabel += " (" + causeStr + ")";

                    var affectedWorkTypes = new List<WorkTypeDef>();
                    foreach (WorkTypeDef workTypeDef in DefDatabase<WorkTypeDef>.AllDefs)
                    {
                        if ((workTypeDef.workTags & tag) > WorkTags.None)
                        {
                            affectedWorkTypes.Add(workTypeDef);
                        }
                    }

                    result.Add((tagLabel, affectedWorkTypes));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting incapable work tags: {ex.Message}");
            }

            return result;
        }

        private static string GetCauseString(Pawn pawn, WorkTags tag)
        {
            // Shared with PawnCharacterAdapter's CausedByRoyalTitle through VanillaAccess's
            // cache, so the same private method is resolved once.
            var getWorkTypeDisableCausesMethod = VanillaAccess.GetMethod(typeof(CharacterCardUtility), "GetWorkTypeDisableCauses");
            if (getWorkTypeDisableCausesMethod == null)
                return null;

            try
            {
                var causeObjects = getWorkTypeDisableCausesMethod.Invoke(
                    null, new object[] { pawn, tag }) as List<object>;
                if (causeObjects == null || causeObjects.Count == 0)
                    return null;

                var parts = new List<string>();
                foreach (var cause in causeObjects)
                {
                    string formatted = FormatWorkTagDisableCause(pawn, cause);
                    if (!string.IsNullOrEmpty(formatted))
                        parts.Add(formatted);
                }
                return parts.Count > 0 ? string.Join(", ", parts) : null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[InfoCardDataExtractor] Error getting disable causes for {tag}: {ex.Message}");
                return null;
            }
        }

        private static string FormatWorkTagDisableCause(Pawn pawn, object cause)
        {
            if (cause is BackstoryDef backstory)
                return "IncapableOfTooltipBackstory".Translate() + ": " + backstory.TitleFor(pawn.gender).CapitalizeFirst();
            if (cause is Trait trait)
                return "IncapableOfTooltipTrait".Translate() + ": " + trait.LabelCap;
            if (cause is Hediff hediff)
                return "IncapableOfTooltipHediff".Translate() + ": " + hediff.LabelCap;
            if (cause is RoyalTitle royalTitle)
                return "IncapableOfTooltipTitle".Translate() + ": " + royalTitle.def.GetLabelFor(pawn);
            if (cause is Quest quest)
                return "IncapableOfTooltipQuest".Translate() + ": " + quest.name;
            if (cause is Precept_Role role)
                return "IncapableOfTooltipRole".Translate() + ": " + role.LabelForPawn(pawn);
            if (cause is Gene gene)
                return "IncapableOfTooltipGene".Translate() + ": " + gene.LabelCap;
            if (cause is MutantDef mutantDef)
                return "IncapableOfTooltipMutant".Translate() + ": " + mutantDef.LabelCap;
            return cause?.ToString() ?? "";
        }

        public static List<(string title, string faction, string description)> GetRoyalTitlesInfo(Pawn pawn)
        {
            var titles = new List<(string, string, string)>();

            if (!ModsConfig.RoyaltyActive || pawn?.royalty == null)
                return titles;

            try
            {
                foreach (var title in pawn.royalty.AllTitlesForReading)
                {
                    string titleLabel = title.def.GetLabelCapFor(pawn);
                    string factionName = title.faction?.Name ?? "Unknown";
                    string desc = title.def.description ?? "";
                    titles.Add((titleLabel, factionName, desc));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting royal titles: {ex.Message}");
            }

            return titles;
        }

        /// <summary>
        /// Gets ideology role information for a pawn, including the role's full tip
        /// (<c>Precept_Role.GetTip()</c>) as separate lines. Vanilla's own Character-tab hover
        /// shows this tip (CharacterCardUtility.cs:916) rather than the role def's bare
        /// description -- required apparel, granted abilities, work restrictions and mood
        /// effects only appear there, never in <c>def.description</c>. Shared by the InfoCard
        /// Character tab and the inspection-panel Character category so both read identically.
        /// </summary>
        public static (string roleName, string ideoName, List<string> tipLines)? GetIdeologyRoleInfo(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive || pawn?.Ideo == null)
                return null;

            try
            {
                var role = pawn.Ideo.GetRole(pawn);
                if (role != null)
                {
                    string roleName = role.LabelForPawn(pawn);
                    string ideoName = pawn.Ideo.name;
                    string tip = role.GetTip() ?? "";
                    List<string> tipLines = tip.StripTags()
                        .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Trim())
                        .Where(line => !string.IsNullOrEmpty(line))
                        .ToList();
                    return (roleName, ideoName, tipLines);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting ideology role: {ex.Message}");
            }

            return null;
        }

        public static List<(string label, string description)> GetAbilitiesInfo(Pawn pawn)
        {
            var abilities = new List<(string, string)>();

            if (pawn?.abilities == null)
                return abilities;

            try
            {
                foreach (var ability in pawn.abilities.AllAbilitiesForReading)
                {
                    if (ability.def.showOnCharacterCard)
                    {
                        string label = ability.def.LabelCap;
                        string desc = ability.def.description ?? "";
                        abilities.Add((label, desc));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting abilities: {ex.Message}");
            }

            return abilities;
        }

        public static (string xenotypeName, string description, List<(string name, GeneDef def)> genes)? GetXenotypeInfo(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || pawn?.genes == null)
                return null;

            try
            {
                string xenotypeName = pawn.genes.XenotypeLabelCap;
                string desc = pawn.genes.XenotypeDescShort ?? "";

                var genes = new List<(string, GeneDef)>();
                foreach (var gene in pawn.genes.GenesListForReading)
                {
                    genes.Add((GeneTreeBuilder.GetGeneDisplayLabel(gene.def), gene.def));
                }

                return (xenotypeName, desc, genes);
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting xenotype: {ex.Message}");
            }

            return null;
        }

        public static List<(string label, float efficiency, string tip)> GetCapacitiesInfo(Pawn pawn)
        {
            var capacities = new List<(string, float, string)>();

            if (pawn?.health?.capacities == null)
                return capacities;

            try
            {
                foreach (var capacityDef in DefDatabase<PawnCapacityDef>.AllDefsListForReading
                    .Where(c => c.CanShowOnPawn(pawn))
                    .OrderBy(c => c.listOrder))
                {
                    if (!PawnCapacityUtility.BodyCanEverDoCapacity(pawn.RaceProps.body, capacityDef))
                        continue;

                    float efficiency = pawn.health.capacities.GetLevel(capacityDef);
                    string label = capacityDef.LabelCap;

                    // capacityDef.description is always empty in vanilla; the tooltip carries
                    // the impactors (hediffs, body parts, genes).
                    string tip = "";
                    try
                    {
                        string fullTip = HealthCardUtility.GetPawnCapacityTip(pawn, capacityDef);
                        // Strip the first line (capacity name + qualitative assessment - already in our label)
                        int firstNewline = fullTip.IndexOf('\n');
                        if (firstNewline >= 0)
                            tip = fullTip.Substring(firstNewline + 1).TrimStart('\r', '\n');
                    }
                    catch { }

                    capacities.Add((label, efficiency, tip));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting capacities: {ex.Message}");
            }

            return capacities;
        }

        public static List<(string label, string partLabel, string severity, string tip)> GetHediffsInfo(Pawn pawn)
        {
            var hediffs = new List<(string, string, string, string)>();

            if (pawn?.health?.hediffSet == null)
                return hediffs;

            try
            {
                foreach (var hediff in pawn.health.hediffSet.hediffs.Where(h => h.Visible))
                {
                    string label = hediff.LabelCap;
                    string partLabel = hediff.Part?.LabelCap ?? "WholeBody".Translate();
                    string severity = hediff.SeverityLabel ?? "";
                    string tip = hediff.GetTooltip(pawn, false);
                    hediffs.Add((label, partLabel, severity, tip));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting hediffs: {ex.Message}");
            }

            return hediffs;
        }

        public static List<(string label, string value)> GetTimeRecords(Pawn pawn)
        {
            var records = new List<(string, string)>();

            if (pawn?.records == null)
                return records;

            try
            {
                // Vanilla draws EVERY record, zero values included
                // (RecordsCardUtility.DrawTimeRecords) — no filtering.
                foreach (var recordDef in DefDatabase<RecordDef>.AllDefsListForReading
                    .Where(r => r.type == RecordType.Time)
                    .OrderBy(r => r.displayOrder))
                {
                    string label = recordDef.LabelCap;
                    string value = pawn.records.GetAsInt(recordDef).ToStringTicksToPeriod();
                    records.Add((label, value));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting time records: {ex.Message}");
            }

            return records;
        }

        public static List<(string label, string value)> GetMiscRecords(Pawn pawn)
        {
            var records = new List<(string, string)>();

            if (pawn?.records == null)
                return records;

            try
            {
                // Vanilla draws EVERY record, zero values included
                // (RecordsCardUtility.DrawMiscRecords) — no filtering.
                foreach (var recordDef in DefDatabase<RecordDef>.AllDefsListForReading
                    .Where(r => r.type == RecordType.Int || r.type == RecordType.Float)
                    .OrderBy(r => r.displayOrder))
                {
                    string label = recordDef.LabelCap;
                    string valueStr = pawn.records.GetValue(recordDef).ToString("0.##");
                    records.Add((label, valueStr));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting misc records: {ex.Message}");
            }

            return records;
        }

        public static List<(string permitName, Faction faction, string status, string description, string requiredTitle, RoyalTitlePermitDef def)> GetPermitsInfo(Pawn pawn)
        {
            var permits = new List<(string, Faction, string, string, string, RoyalTitlePermitDef)>();

            if (!ModsConfig.RoyaltyActive || pawn?.royalty == null)
                return permits;

            try
            {
                // Mirrors PermitsCardUtility.CanDrawPermit: only permits with a positive
                // permitPointCost are ever drawn (zero-cost permits are hidden); a permit
                // tied to a specific faction only shows under that faction, while a
                // faction-less permit shows under whichever faction is selected.
                foreach (var faction in Find.FactionManager.AllFactionsVisible)
                {
                    if (faction.IsPlayer || faction.def.permanentEnemy || faction.temporary)
                        continue;

                    var factionPermits = DefDatabase<RoyalTitlePermitDef>.AllDefs
                        .Where(d => d.permitPointCost > 0 && (d.faction == null || d.faction == faction.def))
                        .OrderBy(d => d.uiPosition.y).ThenBy(d => d.uiPosition.x);

                    if (!factionPermits.Any())
                        continue;

                    foreach (var permitDef in factionPermits)
                    {
                        string status;
                        bool isUnlocked = IsPermitUnlocked(permitDef, pawn, faction);

                        if (isUnlocked)
                        {
                            if (pawn.royalty.HasPermit(permitDef, faction))
                            {
                                var factionPermit = pawn.royalty.AllFactionPermits
                                    .FirstOrDefault(fp => fp.Permit == permitDef && fp.Faction == faction);
                                status = (string)((factionPermit != null && factionPermit.OnCooldown)
                                    ? "RimWorldAccess.Inspection.Permit.GrantedOnCooldown".Translate()
                                    : "RimWorldAccess.Inspection.Permit.Granted".Translate());
                            }
                            else
                            {
                                // Unlocked via upgrade chain (prerequisite of a held permit)
                                status = "RimWorldAccess.Inspection.Permit.Granted".Translate();
                            }
                        }
                        else if (permitDef.AvailableForPawn(pawn, faction))
                        {
                            status = "RimWorldAccess.Inspection.Permit.AvailableWithPoints".Translate(permitDef.permitPointCost).ToString();
                        }
                        else
                        {
                            if (permitDef.prerequisite != null && !IsPermitUnlocked(permitDef.prerequisite, pawn, faction))
                                status = "RimWorldAccess.Inspection.Permit.LockedWithReason".Translate("UpgradeFrom".Translate(permitDef.prerequisite.LabelCap)).ToString();
                            else if (permitDef.minTitle != null)
                                status = "RimWorldAccess.Inspection.Permit.LockedWithReason".Translate("RequiresTitle".Translate(permitDef.minTitle.GetLabelForBothGenders())).ToString();
                            else
                                status = "RimWorldAccess.Inspection.Permit.Locked".Translate();
                        }

                        string requiredTitle = permitDef.minTitle?.GetLabelFor(pawn).CapitalizeFirst() ?? (string)"None".Translate();
                        permits.Add((permitDef.LabelCap, faction, status,
                            permitDef.description ?? "", requiredTitle, permitDef));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting permits: {ex.Message}");
            }

            return permits;
        }

        /// <summary>
        /// Whether a permit counts as unlocked for display: directly held, or a prerequisite of
        /// a held permit (the pawn upgraded past it).
        /// </summary>
        // MUTATION-C: mirrors RimWorld.PermitsCardUtility.PermitUnlocked; private, no callable vehicle.
        public static bool IsPermitUnlocked(RoyalTitlePermitDef permit, Pawn pawn, Faction faction)
        {
            if (pawn.royalty.HasPermit(permit, faction))
                return true;

            var allFactionPermits = pawn.royalty.AllFactionPermits;
            for (int i = 0; i < allFactionPermits.Count; i++)
            {
                if (allFactionPermits[i].Permit.prerequisite == permit && allFactionPermits[i].Faction == faction)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Total favor cost to return all permits: a base of 8 plus the favor cost of any
        /// on-cooldown permit with royalAid.
        /// </summary>
        // MUTATION-C: mirrors RimWorld.PermitsCardUtility.TotalReturnPermitsCost; private, no callable vehicle.
        public static int TotalReturnPermitsCost(Pawn pawn)
        {
            int cost = 8;
            var allFactionPermits = pawn.royalty.AllFactionPermits;
            for (int i = 0; i < allFactionPermits.Count; i++)
            {
                if (allFactionPermits[i].OnCooldown && allFactionPermits[i].Permit.royalAid != null)
                {
                    cost += allFactionPermits[i].Permit.royalAid.favorCost;
                }
            }
            return cost;
        }
    }
}
