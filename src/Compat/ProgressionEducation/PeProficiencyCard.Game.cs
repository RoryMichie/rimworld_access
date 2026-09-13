using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adds Progression: Education's Knowledge panel to the Character inspection category. The
    /// mod transpiles it into <c>CharacterCardUtility.DoLeftSection</c>, which our data-model Bio
    /// reading never draws, so this extender reads the same data: one line per enabled
    /// proficiency track, its tier, position, and the tier trait's description (the panel's tip).
    /// </summary>
    internal static class PeProficiencyCard
    {
        private static readonly Type settingsModType;
        private static readonly Type proficiencyUtilityType;
        private static readonly Type proficiencyDefType;
        private static readonly Type tierDefType;

        private static readonly FieldInfo settingsField;
        private static readonly FieldInfo enableKnowledgePanelField;
        private static readonly MethodInfo canHaveProficienciesMethod;
        private static readonly MethodInfo isTrackEnabledMethod;
        private static readonly MethodInfo getCurrentTierMethod;
        private static readonly FieldInfo trackTiersField;
        private static readonly FieldInfo tierTraitDefField;

        private static readonly bool ready;

        static PeProficiencyCard()
        {
            var surface = new ReflectionSurface("PeProficiencyCard");

            settingsModType = surface.Type("ProgressionEducation.EducationMod");
            proficiencyUtilityType = surface.Type("ProgressionEducation.ProficiencyUtility");
            proficiencyDefType = surface.Type("ProgressionEducation.ProficiencyDef");
            tierDefType = surface.Type("ProgressionEducation.ProficiencyTierDef");

            settingsField = surface.Field(settingsModType, "settings");
            enableKnowledgePanelField = surface.Field(
                surface.Type("ProgressionEducation.EducationSettings"), "enableKnowledgePanel");
            canHaveProficienciesMethod = surface.Method(proficiencyUtilityType, "CanHaveProficiencies");
            isTrackEnabledMethod = surface.Method(proficiencyUtilityType, "IsTrackEnabled");
            getCurrentTierMethod = surface.Method(proficiencyUtilityType, "GetCurrentTier");
            trackTiersField = surface.Field(proficiencyDefType, "tiers");
            tierTraitDefField = surface.Field(tierDefType, "traitDef");

            ready = surface.Ready;
        }

        public static bool Ready => ready;

        public static void Register()
        {
            InspectNodeRegistry.RegisterCategoryExtender("Character", AddKnowledgeSection);
        }

        private static void AddKnowledgeSection(InspectionTreeItem categoryItem, object obj)
        {
            if (!ready || !(obj is Pawn pawn))
            {
                return;
            }
            // The panel's own gates: the setting and the pawn kind.
            object settings = settingsField.GetValue(null);
            if (settings == null || !(bool)enableKnowledgePanelField.GetValue(settings))
            {
                return;
            }
            if (!(bool)canHaveProficienciesMethod.Invoke(null, new object[] { pawn }))
            {
                return;
            }

            var lines = new List<string>();
            foreach (Def track in GenDefDatabase.GetAllDefsInDatabaseForDef(proficiencyDefType))
            {
                if (!(bool)isTrackEnabledMethod.Invoke(null, new object[] { track }))
                {
                    continue;
                }
                var tiers = trackTiersField.GetValue(track) as IList;
                if (tiers == null || tiers.Count == 0)
                {
                    continue;
                }
                // The panel's own fallback for an ungranted track, minus its grant-on-draw write.
                Def tier = getCurrentTierMethod.Invoke(null, new object[] { pawn, track }) as Def
                    ?? tiers[0] as Def;
                if (tier == null)
                {
                    continue;
                }
                int position = tiers.IndexOf(tier) + 1;
                string description = TierDescription(tier);
                string line = "RimWorldAccess.Compat.Pe.ProficiencyLine".Translate(
                    track.LabelCap, tier.LabelCap, position, tiers.Count);
                if (!string.IsNullOrEmpty(description))
                {
                    line += " " + description;
                }
                lines.Add(line);
            }
            if (lines.Count == 0)
            {
                return;
            }

            string header = CompatText.ModText("PE_Knowledge");
            InspectNodeFactory.Section(categoryItem, header, pawn, delegate (InspectionTreeItem section)
            {
                foreach (string line in lines)
                {
                    InspectNodeFactory.DetailLine(section, line);
                }
            });
        }

        /// <summary>The panel's tooltip text: the tier trait's degree description.</summary>
        private static string TierDescription(Def tier)
        {
            try
            {
                var traitDef = tierTraitDefField.GetValue(tier) as TraitDef;
                if (traitDef == null)
                {
                    return null;
                }
                if (traitDef.degreeDatas != null && traitDef.degreeDatas.Count > 0)
                {
                    return CompatText.Flatten(traitDef.degreeDatas[0].description);
                }
                return CompatText.Flatten(traitDef.description);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Proficiency tier description", ex);
                return null;
            }
        }
    }
}
