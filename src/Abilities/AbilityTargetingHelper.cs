using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Extracts and formats ability targeting information.</summary>
    public static class AbilityTargetingHelper
    {
        /// <summary>
        /// The effective casting range, from the verb's EffectiveRange so equipment bonuses count.
        /// 0 for touch abilities, which the game resolves by reachability instead.
        /// </summary>
        public static float GetRange(Ability ability)
        {
            if (ability?.verb is Verb_CastAbility castVerb)
                return castVerb.EffectiveRange;

            if (ability?.def?.verbProperties == null)
                return 0f;

            return ability.def.verbProperties.range;
        }

        /// <summary>Whether the ability is touch range, so no distance check applies.</summary>
        public static bool IsTouchRange(Ability ability)
        {
            return ability?.verb is Verb_CastAbilityTouch || GetRange(ability) <= 0f;
        }

        /// <summary>The AOE radius, or 0 when the ability has none.</summary>
        public static float GetAOERadius(Ability ability)
        {
            if (ability?.def == null)
                return 0f;

            return ability.def.EffectRadius;
        }

        /// <summary>Whether the ability has an area of effect.</summary>
        public static bool HasAOE(Ability ability)
        {
            return ability?.def?.HasAreaOfEffect ?? false;
        }

        /// <summary>Whether the ability is a psycast.</summary>
        public static bool IsPsycast(Ability ability)
        {
            return ability is Psycast;
        }

        /// <summary>Whether the ability can target empty cells.</summary>
        public static bool CanTargetLocations(Ability ability)
        {
            return ability?.verb?.targetParams?.canTargetLocations ?? false;
        }

        /// <summary>Whether the ability requires a pawn target.</summary>
        public static bool RequiresPawnTarget(Ability ability)
        {
            var targetParams = ability?.verb?.targetParams;
            if (targetParams == null)
                return false;

            if (targetParams.canTargetLocations)
                return false;

            return targetParams.canTargetPawns;
        }

        /// <summary>What kind of target the ability requires.</summary>
        public static string GetTargetRequirementDescription(Ability ability)
        {
            var targetParams = ability?.verb?.targetParams;
            if (targetParams == null)
                return "RimWorldAccess.Abilities.TargetKind.Unknown".Translate();

            if (targetParams.canTargetLocations)
                return "RimWorldAccess.Abilities.TargetKind.LocationOrPawn".Translate();

            if (targetParams.canTargetPawns && targetParams.canTargetAnimals && targetParams.canTargetHumans)
                return "RimWorldAccess.Abilities.TargetKind.Pawn".Translate();

            if (targetParams.canTargetPawns && targetParams.canTargetHumans && !targetParams.canTargetAnimals)
                return "RimWorldAccess.Abilities.TargetKind.HumanlikePawn".Translate();

            if (targetParams.canTargetPawns && targetParams.canTargetAnimals && !targetParams.canTargetHumans)
                return "RimWorldAccess.Abilities.TargetKind.Animal".Translate();

            if (targetParams.canTargetBuildings)
                return "RimWorldAccess.Abilities.TargetKind.BuildingOrStructure".Translate();

            return "RimWorldAccess.Abilities.TargetKind.ValidTarget".Translate();
        }

        /// <summary>The psyfocus cost as a percentage string, or null when there is none.</summary>
        public static string GetPsyfocusCostString(Ability ability)
        {
            if (ability?.def == null)
                return null;

            float cost = ability.def.PsyfocusCost;
            if (cost <= float.Epsilon)
                return null;

            return $"{(cost * 100f):F0}%";
        }

        /// <summary>The entropy (neural heat) gain, or null when there is none.</summary>
        public static string GetEntropyGainString(Ability ability)
        {
            if (ability?.def == null)
                return null;

            float entropy = ability.def.EntropyGain;
            if (entropy <= float.Epsilon)
                return null;

            return $"{entropy:F0}";
        }

        /// <summary>The distance between two positions.</summary>
        public static float CalculateDistance(IntVec3 from, IntVec3 to)
        {
            return (to - from).LengthHorizontal;
        }

        /// <summary>Whether the target is within the ability's range.</summary>
        public static bool IsInRange(Ability ability, IntVec3 casterPos, IntVec3 targetPos)
        {
            // The game resolves touch range by reachability.
            if (IsTouchRange(ability))
                return true;

            float range = GetRange(ability);

            if (range <= float.Epsilon)
                return true;

            float distance = CalculateDistance(casterPos, targetPos);
            return distance <= range;
        }

        /// <summary>Whether the caster has line of sight to the target.</summary>
        public static bool HasLineOfSight(Pawn caster, IntVec3 targetPos)
        {
            if (caster?.Map == null)
                return false;

            return GenSight.LineOfSight(caster.Position, targetPos, caster.Map);
        }

        /// <summary>Whether the ability requires line of sight.</summary>
        public static bool RequiresLineOfSight(Ability ability)
        {
            return ability?.def?.verbProperties?.requireLineOfSight ?? true;
        }

        /// <summary>Whether a psycast can be applied, given psychic immunity.</summary>
        public static bool CanApplyPsycastTo(Ability ability, LocalTargetInfo target)
        {
            if (ability is Psycast psycast)
            {
                return psycast.CanApplyPsycastTo(target);
            }
            return true;
        }

        /// <summary>A target's label: pawn name, thing label, or cell position.</summary>
        public static string GetTargetLabel(LocalTargetInfo target)
        {
            if (!target.IsValid)
                return "RimWorldAccess.Abilities.Label.Invalid".Translate();

            if (target.HasThing)
            {
                return target.Thing.LabelShort ?? target.Thing.Label
                    ?? "RimWorldAccess.Abilities.Label.Unknown".Translate().ToString();
            }

            return target.Cell.ToString();
        }

        /// <summary>The pawns an AOE ability would affect at a position.</summary>
        public static List<Pawn> GetAffectedPawns(Ability ability, IntVec3 center, Map map)
        {
            var affected = new List<Pawn>();

            if (ability?.def == null || map == null)
                return affected;

            if (!ability.def.HasAreaOfEffect)
            {
                var pawn = center.GetFirstPawn(map);
                if (pawn != null)
                {
                    affected.Add(pawn);
                }
                return affected;
            }

            float radius = ability.def.EffectRadius;

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, radius, useCenter: true))
            {
                if (thing is Pawn pawn)
                {
                    var targetParams = ability.verb?.targetParams;
                    if (targetParams != null && !targetParams.CanTarget(pawn))
                        continue;

                    if (!CanApplyPsycastTo(ability, pawn))
                        continue;

                    affected.Add(pawn);
                }
            }

            return affected;
        }

        /// <summary>The opening targeting announcement: ability, range, AOE, costs and key hints.</summary>
        public static string BuildTargetingStartAnnouncement(Ability ability)
        {
            if (ability?.def == null)
                return "RimWorldAccess.Abilities.Start.Fallback".Translate();

            var sb = new StringBuilder();
            sb.Append("RimWorldAccess.Abilities.Start.Header".Translate(ability.def.LabelCap));

            if (IsTouchRange(ability))
            {
                sb.Append("RimWorldAccess.Abilities.Start.TouchRange".Translate());
            }
            else
            {
                float range = GetRange(ability);
                if (range > 0)
                {
                    sb.Append("RimWorldAccess.Abilities.Start.Range".Translate(range.ToString("F0")));
                }
            }

            if (HasAOE(ability))
            {
                float aoeRadius = GetAOERadius(ability);
                sb.Append("RimWorldAccess.Abilities.Start.AOE".Translate(aoeRadius.ToString("F0")));
            }

            string psyfocus = GetPsyfocusCostString(ability);
            if (psyfocus != null)
            {
                sb.Append("RimWorldAccess.Abilities.Start.Psyfocus".Translate(psyfocus));
            }

            string entropy = GetEntropyGainString(ability);
            if (entropy != null)
            {
                sb.Append("RimWorldAccess.Abilities.Start.NeuralHeat".Translate(entropy));
            }

            if (HasAOE(ability))
            {
                sb.Append("RimWorldAccess.Abilities.Start.HintAoe".Translate());
            }
            else
            {
                sb.Append("RimWorldAccess.Abilities.Start.HintSingle".Translate());
            }

            return sb.ToString();
        }

        /// <summary>The per-cursor range readout: distance, in or out of range, and line of sight.</summary>
        public static string BuildRangeInfoAnnouncement(Ability ability, IntVec3 casterPos, IntVec3 cursorPos, Map map)
        {
            if (ability?.def == null)
                return "RimWorldAccess.Abilities.Range.NoAbilityActive".Translate();

            var sb = new StringBuilder();

            float distance = CalculateDistance(casterPos, cursorPos);
            sb.Append("RimWorldAccess.Abilities.Range.Distance".Translate(distance.ToString("F0")));

            if (IsTouchRange(ability))
            {
                // Touch range is resolved by reachability, so only report the distance.
                bool adjacent = casterPos.AdjacentTo8WayOrInside(cursorPos);
                sb.Append(adjacent
                    ? "RimWorldAccess.Abilities.Range.InRangeTouch".Translate()
                    : "RimWorldAccess.Abilities.Range.MustBeAdjacent".Translate());
            }
            else
            {
                float range = GetRange(ability);
                if (range > 0)
                {
                    if (distance <= range)
                    {
                        sb.Append("RimWorldAccess.Abilities.Range.InRange".Translate());
                    }
                    else
                    {
                        sb.Append("RimWorldAccess.Abilities.Range.OutOfRange".Translate(range.ToString("F0")));
                    }
                }
            }

            if (RequiresLineOfSight(ability) && ability.pawn != null && map != null)
            {
                bool hasLOS = HasLineOfSight(ability.pawn, cursorPos);
                if (!hasLOS)
                {
                    sb.Append("RimWorldAccess.Abilities.Range.NoLineOfSight".Translate());
                }
            }

            if (map != null && !CanTargetLocations(ability))
            {
                var pawn = cursorPos.GetFirstPawn(map);
                if (pawn == null)
                {
                    string requirement = GetTargetRequirementDescription(ability);
                    sb.Append("RimWorldAccess.Abilities.Range.NoTargetAtCursor".Translate(requirement));
                }
            }

            return sb.ToString();
        }

        /// <summary>The affected-targets readout, or an explanation of what the ability needs.</summary>
        public static string BuildAffectedTargetsAnnouncement(Ability ability, IntVec3 cursorPos, Map map)
        {
            if (ability?.def == null || map == null)
                return "RimWorldAccess.Abilities.Range.NoAbilityActive".Translate();

            var affected = GetAffectedPawns(ability, cursorPos, map);

            if (affected.Count == 0)
            {
                if (!CanTargetLocations(ability))
                {
                    string requirement = GetTargetRequirementDescription(ability);
                    if (ability.def.HasAreaOfEffect)
                    {
                        return "RimWorldAccess.Abilities.Affected.NoValidTargetsAoe"
                            .Translate(requirement, ability.def.EffectRadius.ToString("F0"));
                    }
                    return "RimWorldAccess.Abilities.Affected.NoValidTargetSingle".Translate(requirement);
                }
                return "RimWorldAccess.Abilities.Affected.NoPawnsInRadius".Translate();
            }

            // Pawn-target abilities ignore a cell-only LocalTargetInfo, so wrap the affected pawn
            // directly and let the comp read target.Pawn.
            LocalTargetInfo warnTarget = affected.Count > 0
                ? new LocalTargetInfo(affected[0])
                : new LocalTargetInfo(cursorPos);
            string warnings = GetExtraTargetWarnings(ability, warnTarget);

            if (!ability.def.HasAreaOfEffect)
            {
                string targetLine = "RimWorldAccess.Abilities.Affected.TargetOne".Translate(affected[0].LabelShort).ToString();
                if (!string.IsNullOrEmpty(warnings))
                    targetLine += "RimWorldAccess.Abilities.Success.WarningsSuffix".Translate(warnings);
                return targetLine;
            }

            int maxNames = 5;
            var names = affected.Take(maxNames).Select(p => p.LabelShort);
            string nameList = string.Join(", ", names);

            var sb = new StringBuilder();
            sb.Append("RimWorldAccess.Abilities.Affected.CountPrefix".Translate(affected.Count, nameList));

            if (affected.Count > maxNames)
            {
                sb.Append("RimWorldAccess.Abilities.Affected.AndMore".Translate(affected.Count - maxNames));
            }

            if (!string.IsNullOrEmpty(warnings))
                sb.Append($". Warning: {warnings}");

            return sb.ToString();
        }

        /// <summary>
        /// The cursor-side warnings sighted players see, collected from the ability's effect comps
        /// and joined with periods. Null when no comp returns anything.
        /// </summary>
        public static string GetExtraTargetWarnings(Ability ability, LocalTargetInfo target)
        {
            if (ability?.comps == null || !target.IsValid)
                return null;

            var warnings = new List<string>();
            foreach (var comp in ability.comps)
            {
                // ExtraLabelMouseAttachment lives on CompAbilityEffect, not the base AbilityComp.
                if (!(comp is CompAbilityEffect effectComp)) continue;
                string text = null;
                try { text = effectComp.ExtraLabelMouseAttachment(target); }
                catch { continue; }
                if (string.IsNullOrEmpty(text)) continue;
                // Strip color tags and flatten any newlines the comp embedded.
                string clean = text.StripTags()
                    .Replace("\r", " ")
                    .Replace("\n", ". ")
                    .Trim();
                if (!string.IsNullOrEmpty(clean))
                    warnings.Add(clean);
            }
            if (warnings.Count == 0) return null;
            return string.Join(". ", warnings);
        }

        /// <summary>The immunity message when the target is immune, else null.</summary>
        public static string GetImmunityMessage(Ability ability, LocalTargetInfo target)
        {
            if (!target.HasThing || !(target.Thing is Pawn pawn))
                return null;

            if (ability is Psycast psycast)
            {
                if (!psycast.CanApplyPsycastTo(target))
                {
                    return "RimWorldAccess.Abilities.Immunity.Psychic".Translate(pawn.LabelShort);
                }
            }

            return null;
        }
    }
}
