using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static partial class ScenarioBuilderState
    {
        /// <summary>All defs of an arbitrary Def-derived type, via reflection over DefDatabase&lt;T&gt;.AllDefsListForReading.</summary>
        internal static List<(string label, object value)> GetGenericDefOptions(Type defType, bool includeNone = false)
        {
            var options = new List<(string, object)>();
            var dbType = typeof(DefDatabase<>).MakeGenericType(defType);
            var allDefsProp = dbType.GetProperty("AllDefsListForReading", BindingFlags.Public | BindingFlags.Static);
            if (allDefsProp?.GetValue(null) is System.Collections.IEnumerable allDefs)
            {
                foreach (var def in allDefs)
                {
                    string label = (def as Def)?.LabelCap ?? def.ToString();
                    options.Add((label, def));
                }
            }
            var sorted = options.OrderBy(o => o.Item1).ToList();
            if (includeNone)
                sorted.Insert(0, ((string)"None".Translate(), (object)null));
            return sorted;
        }

        #region Option Lists for Dropdowns

        private static List<(string label, object value)> GetPlayerFactionOptions()
        {
            var options = new List<(string, object)>();
            foreach (var faction in DefDatabase<FactionDef>.AllDefs.Where(f => f.isPlayer).OrderBy(f => f.label))
            {
                options.Add((faction.LabelCap, faction));
            }
            return options;
        }

        private static List<(string label, object value)> GetPlanetLayerOptions()
        {
            var options = new List<(string, object)>();
            foreach (var layer in DefDatabase<PlanetLayerDef>.AllDefs.OrderBy(l => l.label))
            {
                options.Add((layer.LabelCap, layer));
            }
            return options;
        }

        private static List<(string label, object value)> GetPlanetLayerSettingsOptions()
        {
            var options = new List<(string, object)>();
            var settingsDefType = AccessTools.TypeByName("RimWorld.PlanetLayerSettingsDef");
            if (settingsDefType != null)
            {
                var defDatabaseType = typeof(DefDatabase<>).MakeGenericType(settingsDefType);
                var allDefsProperty = defDatabaseType.GetProperty("AllDefs", BindingFlags.Public | BindingFlags.Static);
                if (allDefsProperty != null)
                {
                    var allDefs = allDefsProperty.GetValue(null) as System.Collections.IEnumerable;
                    if (allDefs != null)
                    {
                        foreach (var def in allDefs)
                        {
                            var labelProp = def.GetType().GetProperty("LabelCap");
                            string label = labelProp?.GetValue(def)?.ToString() ?? def.ToString();
                            options.Add((label, def));
                        }
                    }
                }
            }
            return options.OrderBy(o => o.Item1).ToList();
        }

        /// <summary>
        /// The caption ScenPart_StartingAnimal's own animal button shows
        /// (ScenPart_StartingAnimal.cs:54,100-107), reflected off the actual part so a subclass
        /// overriding it speaks its own wording. Falls back to the same values vanilla's helper
        /// returns when the method is absent.
        /// </summary>
        private static string CurrentAnimalLabel(ScenPart part, PawnKindDef currentKind)
        {
            var labelMethod = part?.GetType().GetMethod("CurrentAnimalLabel",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (labelMethod != null && labelMethod.GetParameters().Length == 0)
            {
                try
                {
                    string label = labelMethod.Invoke(part, null) as string;
                    if (!label.NullOrEmpty()) return label.CapitalizeFirst();
                }
                catch
                {
                }
            }
            return currentKind?.LabelCap ?? RandomPetLabel();
        }

        /// <summary>
        /// Vanilla's own wording for the no-specific-animal choice: the same "RandomPet" key its
        /// FloatMenu option and CurrentAnimalLabel() read, never a duplicate of ours.
        /// </summary>
        private static string RandomPetLabel() => (string)"RandomPet".Translate().CapitalizeFirst();

        /// <summary>
        /// Rides ScenPart_StartingAnimal's own private PossibleAnimals (:63,74-77) by reflection on
        /// the actual part, the exact source its animal FloatMenu draws from, so a patched or
        /// overridden candidate set stays in step. Falls back to the RaceProps.Animal filter only
        /// when the method is absent.
        /// </summary>
        private static List<(string label, object value)> GetAnimalOptions(ScenPart part)
        {
            var options = new List<(string, object)>();
            options.Add((RandomPetLabel(), null));

            IEnumerable<PawnKindDef> kinds = PossibleAnimals(part)
                ?? DefDatabase<PawnKindDef>.AllDefs.Where(k => k.RaceProps.Animal);

            foreach (var kind in kinds.Where(k => k != null).OrderBy(k => k.label))
            {
                options.Add((kind.LabelCap, kind));
            }
            return options;
        }

        private static List<PawnKindDef> PossibleAnimals(ScenPart part)
        {
            var method = part?.GetType().GetMethod("PossibleAnimals",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (method == null) return null;

            // checkForTamer:false is what vanilla's own editor menu passes: the true branch reads
            // Find.GameInitData, which the scenario editor has no business requiring (:79-84).
            ParameterInfo[] parameters = method.GetParameters();
            object[] args;
            if (parameters.Length == 0) args = null;
            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool)) args = new object[] { false };
            else return null;

            try
            {
                // Materialized inside the guard: the helper returns a lazy Where, so a modded
                // override's failure would otherwise surface at enumeration.
                return (method.Invoke(part, args) as IEnumerable<PawnKindDef>)?.ToList();
            }
            catch
            {
                return null;
            }
        }

        private static List<(string label, object value)> GetTraitOptions()
        {
            var options = new List<(string, object)>();
            foreach (var trait in DefDatabase<TraitDef>.AllDefs.OrderBy(t => t.label))
            {
                options.Add((trait.label.CapitalizeFirst(), trait));
            }
            return options;
        }

        private static List<(string label, object value)> GetTraitWithDegreeOptions()
        {
            var options = new List<(string, object)>();
            foreach (var trait in DefDatabase<TraitDef>.AllDefs.OrderBy(t => t.label))
            {
                foreach (var degreeData in trait.degreeDatas)
                {
                    string label = degreeData.LabelCap;
                    if (string.IsNullOrEmpty(label))
                        label = trait.LabelCap;
                    options.Add((label, degreeData));
                }
            }
            return options.OrderBy(o => o.Item1).ToList();
        }

        private static List<(string label, object value)> GetIncidentDefOptions()
        {
            var options = new List<(string, object)>();
            // Every incident is available, matching DoIncidentEditInterface.
            foreach (var incident in DefDatabase<IncidentDef>.AllDefs.OrderBy(i => i.label))
            {
                options.Add((incident.LabelCap, incident));
            }
            return options;
        }

        /// <summary>
        /// Rides ScenPart_StartingResearch's own private NonRedundantResearchProjects (:28-31) by
        /// reflection on the actual part, so a faction with startingResearchTags excludes the same
        /// projects vanilla's picker does. Falls back to the unfiltered list only when absent.
        /// </summary>
        private static List<(string label, object value)> GetResearchOptions(ScenPart part)
        {
            var options = new List<(string, object)>();

            var nonRedundantMethod = part?.GetType().GetMethod("NonRedundantResearchProjects",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            IEnumerable<ResearchProjectDef> projects =
                nonRedundantMethod?.Invoke(part, null) as IEnumerable<ResearchProjectDef>;
            if (projects == null)
                projects = DefDatabase<ResearchProjectDef>.AllDefs;

            foreach (var project in projects.OrderBy(r => r.label))
            {
                options.Add((project.LabelCap, project));
            }
            return options;
        }

        private static List<(string label, object value)> GetArriveMethodOptions()
        {
            var options = new List<(string, object)>();
            foreach (PlayerPawnsArriveMethod method in Enum.GetValues(typeof(PlayerPawnsArriveMethod)))
            {
                if (method == PlayerPawnsArriveMethod.Gravship && !ModsConfig.OdysseyActive)
                    continue;
                options.Add((method.ToStringHuman(), method));
            }
            return options;
        }

        private static List<(string label, object value)> GetStartingThingOptions(ScenPart part)
        {
            var options = new List<(string, object)>();

            // ScenPart_ThingCount has PossibleThingDefs; reflect it when present.
            var possibleThingsMethod = part.GetType().GetMethod("PossibleThingDefs",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            IEnumerable<ThingDef> possibleThings;
            if (possibleThingsMethod != null)
            {
                possibleThings = (IEnumerable<ThingDef>)possibleThingsMethod.Invoke(part, null);
            }
            else
            {
                // Fallback matching the default PossibleThingDefs: items and minifiable buildings.
                possibleThings = DefDatabase<ThingDef>.AllDefs
                    .Where(d => (d.category == ThingCategory.Item && d.scatterableOnMapGen && !d.destroyOnDrop)
                             || (d.category == ThingCategory.Building && d.Minifiable));
            }

            foreach (var thing in possibleThings.OrderBy(t => t.label))
            {
                options.Add((thing.LabelCap, thing));
            }
            return options;
        }

        private static List<(string label, object value)> GetStuffOptions(ThingDef thingDef)
        {
            var options = new List<(string, object)>();
            if (thingDef == null || !thingDef.MadeFromStuff)
                return options;

            foreach (var stuff in GenStuff.AllowedStuffsFor(thingDef).OrderBy(s => s.label))
            {
                options.Add((stuff.LabelCap, stuff));
            }
            return options;
        }

        private static List<(string label, object value)> GetQualityOptions()
        {
            var options = new List<(string, object)>();
            options.Add(((string)"Default".Translate(), null));

            foreach (QualityCategory quality in QualityUtility.AllQualityCategories)
            {
                options.Add((quality.GetLabel().CapitalizeFirst(), quality));
            }
            return options;
        }

        /// <summary>
        /// Reflects ScenPart_ForcedHediff's private MaxSeverity rather than hand-copying its
        /// formula, so the bound survives game updates. Returns 1f when it cannot be read.
        /// </summary>
        private static float GetForcedHediffMaxSeverity(ScenPart part, Type partType)
        {
            var maxSeverityProp = partType.GetProperty("MaxSeverity", BindingFlags.NonPublic | BindingFlags.Instance);
            if (maxSeverityProp != null && maxSeverityProp.PropertyType == typeof(float))
            {
                return (float)maxSeverityProp.GetValue(part);
            }
            return 1f;
        }

        private static List<(string label, object value)> GetHediffOptions()
        {
            var options = new List<(string, object)>();
            // The game's own filter: scenarioCanAdd only.
            foreach (var hediff in DefDatabase<HediffDef>.AllDefs
                .Where(h => h.scenarioCanAdd)
                .OrderBy(h => h.label))
            {
                options.Add((hediff.LabelCap, hediff));
            }
            return options;
        }

        private static List<(string label, object value)> GetPawnGenerationContextOptions()
        {
            var options = new List<(string, object)>();
            foreach (PawnGenerationContext context in Enum.GetValues(typeof(PawnGenerationContext)))
            {
                options.Add((context.ToStringHuman(), context));
            }
            return options;
        }

        /// <summary>Localized "Yes"/"No" for a boolean checkbox value, through vanilla's own keys.</summary>
        internal static string BoolDisplay(bool value) =>
            value ? (string)"Yes".Translate() : (string)"No".Translate();

        /// <summary>
        /// Localized word for a planet-layer-connection zoom mode, taking the language-independent
        /// enum name. Prefers the game's own key over ours.
        /// </summary>
        internal static string ZoomModeDisplay(string enumName)
        {
            string vanillaKey = "ScenPart_PlanetLayerConnections_" + enumName;
            if (vanillaKey.CanTranslate())
                return (string)vanillaKey.Translate();
            switch (enumName)
            {
                case "ZoomIn": return (string)"RimWorldAccess.ScenarioBuilder.ZoomIn".Translate();
                case "ZoomOut": return (string)"RimWorldAccess.ScenarioBuilder.ZoomOut".Translate();
                default: return (string)"None".Translate();
            }
        }

        private static List<(string label, object value)> GetStartingMechOptions()
        {
            var options = new List<(string, object)>();
            options.Add(((string)"RandomMech".Translate(), null));

            // ScenPart_StartingMech.PossibleMechs is mechanoids with a CompProperties_OverseerSubject,
            // not every mechanoid: a plain hostile mech cannot be a starting colony mech.
            foreach (var kind in DefDatabase<PawnKindDef>.AllDefs
                .Where(k => k.RaceProps != null && k.RaceProps.IsMechanoid
                    && k.race.GetCompProperties<CompProperties_OverseerSubject>() != null)
                .OrderBy(k => k.label))
            {
                options.Add((kind.LabelCap, kind));
            }
            return options;
        }

        private static List<(string label, object value)> GetStatDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var stat in DefDatabase<StatDef>.AllDefs
                .Where(s => !s.forInformationOnly && s.CanShowWithLoadedMods())
                .OrderBy(s => s.label))
            {
                // Matches ScenPart_StatFactor.DoEditInterface's own option label (:34): the picker's
                // OPTIONS use LabelForFullStatListCap, not the LabelCap its button shows.
                options.Add((stat.LabelForFullStatListCap, stat));
            }
            return options;
        }

        private static List<(string label, object value)> GetPermanentGameConditionOptions()
        {
            var options = new List<(string, object)>();
            foreach (var condition in DefDatabase<GameConditionDef>.AllDefs
                .Where(c => c.canBePermanent)
                .OrderBy(c => c.label))
            {
                options.Add((condition.LabelCap, condition));
            }
            return options;
        }

        private static List<(string label, object value)> GetBuildableThingDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var thing in DefDatabase<ThingDef>.AllDefs
                .Where(t => t.category == ThingCategory.Building && t.BuildableByPlayer)
                .OrderBy(t => t.label))
            {
                options.Add((thing.LabelCap, thing));
            }
            return options;
        }

        private static List<(string label, object value)> GetNeedDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var need in DefDatabase<NeedDef>.AllDefs
                .Where(n => n.major)
                .OrderBy(n => n.label))
            {
                options.Add((need.LabelCap, need));
            }
            return options;
        }

        /// <summary>
        /// Rides ScenPart_OnPawnDeathExplode's own private PossibleDamageDefs (:55-59) by reflection
        /// on the actual part, the same source its damage-type FloatMenu draws from, rather than
        /// hand-picked defNames. Falls back to the vanilla Bomb/Flame pair only when absent.
        /// </summary>
        private static List<(string label, object value)> GetExplosionDamageDefOptions(ScenPart part)
        {
            var options = new List<(string, object)>();

            var possibleDamageDefsMethod = part?.GetType().GetMethod("PossibleDamageDefs",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            IEnumerable<DamageDef> damageDefs =
                possibleDamageDefsMethod?.Invoke(part, null) as IEnumerable<DamageDef>;
            if (damageDefs == null)
                damageDefs = new[] { DamageDefOf.Bomb, DamageDefOf.Flame };

            foreach (var damage in damageDefs.Where(d => d != null))
            {
                options.Add((damage.LabelCap, damage));
            }
            return options;
        }

        private static List<(string label, object value)> GetQuestScriptDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var quest in DefDatabase<QuestScriptDef>.AllDefs
                .Where(q => q.IsRootAny)
                .OrderBy(q => q.label ?? q.defName))
            {
                // LabelCap is null for an empty label, so fall back to the defName.
                string displayLabel;
                if (quest.label.NullOrEmpty())
                {
                    displayLabel = GenText.SplitCamelCase(quest.defName).Replace("_", " ");
                }
                else
                {
                    displayLabel = (string)quest.LabelCap;
                }
                options.Add((displayLabel, quest));
            }
            return options;
        }

        private static List<(string label, object value)> GetMapGeneratorDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var mapGen in DefDatabase<MapGeneratorDef>.AllDefs
                .Where(m => m.validScenarioMap)
                .OrderBy(m => m.label))
            {
                options.Add((mapGen.LabelCap, mapGen));
            }
            return options;
        }

        private static List<(string label, object value)> GetMonolithGenerationMethodOptions()
        {
            var options = new List<(string, object)>();

            var enumType = AccessTools.TypeByName("RimWorld.MonolithGenerationMethod");
            if (enumType != null)
            {
                foreach (var value in Enum.GetValues(enumType))
                {
                    string label = value.ToString();
                    string translationKey = $"MonolithGenerationMethod_{label}";
                    if (translationKey.CanTranslate())
                        label = translationKey.Translate();
                    options.Add((label, value));
                }
            }
            return options;
        }

        /// <summary>Starting pawn kind options: humanlike kinds from player factions only.</summary>
        internal static List<(string label, object value)> GetPawnKindDefOptions()
        {
            var options = new List<(string, object)>();
            foreach (var kind in DefDatabase<PawnKindDef>.AllDefs
                .Where(k => k.RaceProps.Humanlike && k.defaultFactionDef != null && k.defaultFactionDef.isPlayer)
                .OrderBy(k => k.label))
            {
                options.Add((kind.LabelCap, kind));
            }
            return options;
        }

        /// <summary>Xenotype options (Biotech).</summary>
        internal static List<(string label, object value)> GetXenotypeDefOptions()
        {
            var options = new List<(string, object)>();
            if (!ModsConfig.BiotechActive)
                return options;

            foreach (var xenotype in DefDatabase<XenotypeDef>.AllDefs.OrderBy(x => x.label))
            {
                options.Add((xenotype.LabelCap, xenotype));
            }
            return options;
        }

        /// <summary>Mutant options (Anomaly): "None" plus every mutant with showInScenarioEditor.</summary>
        internal static List<(string label, object value)> GetMutantDefOptions()
        {
            var options = new List<(string, object)>();
            if (!ModsConfig.AnomalyActive)
                return options;

            options.Add(("None".Translate().CapitalizeFirst(), null));

            foreach (var mutant in DefDatabase<MutantDef>.AllDefs
                .Where(m => m.showInScenarioEditor)
                .OrderBy(m => m.label))
            {
                options.Add((mutant.LabelCap, mutant));
            }
            return options;
        }

        /// <summary>
        /// MUTATION-C: mirrors ScenPart_PlanetLayer.IsTagValid/GetAllTags
        /// (RimWorld/ScenPart_PlanetLayer.cs:115-155) — vanilla reddens the tag row's caption when
        /// the tag is empty or collides with another layer's, advisory only. Not reflected because
        /// vanilla's version walks Find.Scenario.AllParts, which need not be the scenario under
        /// edit; this walks <see cref="currentScenario"/>. Returns "" when valid, else vanilla's own
        /// translated error phrase.
        /// </summary>
        private static string PlanetLayerTagValidityNote(ScenPart_PlanetLayer part, string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return "";
            if (currentScenario != null)
            {
                foreach (var otherPart in currentScenario.AllParts)
                {
                    if (otherPart == part || !(otherPart is ScenPart_PlanetLayer)) continue;
                    var otherTagField = VanillaAccess.GetField(otherPart.GetType(), "tag");
                    if ((otherTagField?.GetValue(otherPart) as string) == tag)
                        return (string)"ScenPart_PlanetLayerTag_Error".Translate();
                }
            }
            return "";
        }

        /// <summary>Tag options for planet-layer connections: tags from other PlanetLayer parts that are not already connected.</summary>
        internal static List<(string label, object value)> GetAvailableTagOptions(ScenPart part, List<object> existingConnections)
        {
            var options = new List<(string, object)>();
            var usedTags = new HashSet<string>();

            if (existingConnections != null)
            {
                foreach (var conn in existingConnections)
                {
                    var tagField = VanillaAccess.GetField(conn.GetType(), "tag");
                    if (tagField != null)
                    {
                        var tag = tagField.GetValue(conn) as string;
                        if (!string.IsNullOrEmpty(tag))
                            usedTags.Add(tag);
                    }
                }
            }

            options.Add(("Remove".Translate().CapitalizeFirst(), "__REMOVE__"));

            if (currentScenario != null)
            {
                foreach (var otherPart in currentScenario.AllParts)
                {
                    if (otherPart == part || !(otherPart is ScenPart_PlanetLayer)) continue;

                    var tagField = VanillaAccess.GetField(otherPart.GetType(), "tag");
                    if (tagField != null)
                    {
                        var tag = tagField.GetValue(otherPart) as string;
                        if (!string.IsNullOrEmpty(tag) && !usedTags.Contains(tag))
                        {
                            options.Add((tag, tag));
                        }
                    }
                }
            }

            return options;
        }

        internal static List<(string label, object value)> GetZoomModeOptions()
        {
            var options = new List<(string, object)>();

            var zoomModeType = AccessTools.TypeByName("RimWorld.LayerConnection+ZoomMode");
            if (zoomModeType != null)
            {
                foreach (var value in Enum.GetValues(zoomModeType))
                {
                    options.Add((ZoomModeDisplay(value.ToString()), value));
                }
            }
            return options;
        }

        #endregion


        /// <summary>Strips trailing punctuation, so concatenation never yields ". :" or ": :".</summary>
        internal static string StripTrailingPunctuation(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.TrimEnd('.', ':', '!', '?', ',', ';');
        }

    }
}
