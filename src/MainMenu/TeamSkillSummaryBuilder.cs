using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Builds the Team Skills tab's summary rows, extracted from StartingPawnHelper.
    /// </summary>
    public static class TeamSkillSummaryBuilder
    {
        /// <summary>
        /// Builds the Team Skills summary rows shown on the second tab — one row per skill that
        /// is visible in the pawn-creator summary, naming the best starting pawn for that skill.
        /// Mirrors StartingPawnUtility.DrawSkillSummaries / FindBestSkillOwner exactly.
        /// </summary>
        public static List<string> BuildTeamSkillSummary()
        {
            var rows = new List<string>();
            var gid = Find.GameInitData;
            if (gid?.startingAndOptionalPawns == null || gid.startingPawnCount <= 0)
                return rows;

            foreach (var skillDef in DefDatabase<SkillDef>.AllDefsListForReading)
            {
                if (!skillDef.pawnCreatorSummaryVisible)
                    continue;

                Pawn best = FindBestSkillOwner(skillDef);
                if (best?.skills == null)
                    continue;

                SkillRecord rec = best.skills.GetSkill(skillDef);
                string skillLabel = skillDef.skillLabel.CapitalizeFirst();
                string name = best.Name?.ToStringShort ?? (string)best.LabelShort;

                string row;
                if (rec.TotallyDisabled)
                {
                    row = $"{skillLabel}: {name}, {"DisabledLower".Translate()}";
                }
                else
                {
                    row = $"{skillLabel}: {name}, {rec.Level}";
                    string passion = StartingPawnHelper.GetPassionLabel(rec.passion);
                    if (!string.IsNullOrEmpty(passion))
                        row += $", {passion}";
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Returns the starting pawn best at the given skill, mirroring
        /// StartingPawnUtility.FindBestSkillOwner (highest level, passion breaks ties, skips
        /// disabled). Considers only the selected starting pawns, not optional/left-behind ones.
        /// </summary>
        private static Pawn FindBestSkillOwner(SkillDef skill)
        {
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            int count = Find.GameInitData.startingPawnCount;
            if (pawns == null || pawns.Count == 0 || count <= 0)
                return null;

            Pawn best = pawns[0];
            SkillRecord bestRec = best.skills.GetSkill(skill);
            for (int i = 1; i < count && i < pawns.Count; i++)
            {
                SkillRecord rec = pawns[i].skills.GetSkill(skill);
                if (!rec.TotallyDisabled
                    && (bestRec.TotallyDisabled
                        || rec.Level > bestRec.Level
                        || (rec.Level == bestRec.Level && (int)rec.passion > (int)bestRec.passion)))
                {
                    best = pawns[i];
                    bestRec = rec;
                }
            }
            return best;
        }
    }
}
