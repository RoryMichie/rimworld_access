using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Port of the original announcement ladders' "Gizmo_GrowthTier" branches
    /// (GetGrowthTierLabel / GetGrowthTierStatus), split across the handler
    /// facets to mirror what a sighted player actually sees on the gizmo
    /// (Gizmo_GrowthTier.GizmoOnGUI):
    ///
    ///  - Label:  the tier line ("Growth tier: N") — legacy port, unchanged.
    ///  - Status: the two at-a-glance bar readouts drawn on the gizmo — growth
    ///    points "X / next-tier requirement" (or max) with the per-day gain
    ///    rate, plus the Learning need bar percentage.
    ///  - Description: the gizmo's hover tooltip (GrowthTierTooltip), rebuilt
    ///    verbatim from the decompiled draw code with the game's own keys:
    ///    tier + StatsReport_GrowthTierDesc, max-tier / progress-to-next line,
    ///    next growth moment age, and the passion/trait rewards at this tier
    ///    and the next. Newlines are flattened to periods for speech.
    ///
    /// The legacy status had folded the tooltip-derived parts (next growth
    /// moment, tier rewards) into the status because no description facet
    /// existed then; they now live in the description so a single announcement
    /// does not speak the rewards twice. No information is dropped — the
    /// description is a superset of what the legacy status carried.
    ///
    /// Execution has no branch here — the original ladder had no execute-side
    /// counterpart for this type.
    /// </summary>
    internal sealed class GrowthTierGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// Gizmo_GrowthTier keeps its pawn in a private field; cached FieldInfo
        /// is the only access path (same approach as the legacy ladder, hoisted
        /// out of the per-call lookup).
        /// </summary>
        private static readonly FieldInfo ChildField = typeof(Gizmo_GrowthTier).GetField(
            "child", BindingFlags.Instance | BindingFlags.NonPublic);

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;

            if (!(gizmo is Gizmo_GrowthTier))
                return false;

            Pawn child = GetChild(gizmo);
            if (child?.ageTracker == null)
            {
                // Legacy fallback: child unavailable → generic type name.
                label = "RimWorldAccess.Inspection.Gizmo.Type.GrowthTier".Translate();
                return true;
            }

            int tier = child.ageTracker.GrowthTier;
            label = "RimWorldAccess.Inspection.Gizmo.Type.GrowthTierWithLevel".Translate(tier);
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;

            if (!(gizmo is Gizmo_GrowthTier))
                return false;

            Pawn child = GetChild(gizmo);
            if (child?.ageTracker == null)
                return false;

            int tier = child.ageTracker.GrowthTier;
            if (tier < 0 || tier >= GrowthUtility.GrowthTiers.Length)
                return false;

            var parts = new List<string>();

            // Growth points bar text (Gizmo_GrowthTier.DrawGrowthTier): the bar
            // shows "X / next tier requirement", or "max / max" at the top tier.
            if (child.ageTracker.AtMaxGrowthTier)
            {
                float maxPoints = GrowthUtility.GrowthTiers[GrowthUtility.GrowthTiers.Length - 1].pointsRequirement;
                parts.Add("RimWorldAccess.Inspection.Gizmo.Status.GrowthPointsMaxTier".Translate(maxPoints));
            }
            else
            {
                int currentPoints = Mathf.FloorToInt(child.ageTracker.growthPoints);
                float nextTierPoints = GrowthUtility.GrowthTiers[tier + 1].pointsRequirement;
                string pointsText = "RimWorldAccess.Inspection.Gizmo.Status.GrowthPointsCurrentOfNext".Translate(
                    currentPoints, nextTierPoints);
                if (child.ageTracker.canGainGrowthPoints)
                {
                    string perDay = "PerDay".Translate(
                        child.ageTracker.GrowthPointsPerDay.ToStringByStyle(ToStringStyle.FloatMaxTwo));
                    pointsText += "RimWorldAccess.Inspection.Gizmo.Status.GrowthPointsPerDaySuffix".Translate(perDay);
                }
                parts.Add(pointsText);
            }

            // Learning need bar percentage (Gizmo_GrowthTier.DrawLearning) — the
            // second bar sighted players see on this gizmo.
            if (child.needs?.learning != null)
                parts.Add("RimWorldAccess.Inspection.Gizmo.Status.GrowthLearningProgress".Translate(
                    child.needs.learning.CurLevelPercentage.ToStringPercent()));

            if (parts.Count == 0)
                return false;

            // Trim trailing periods so joining never produces double periods.
            for (int i = 0; i < parts.Count; i++)
                parts[i] = parts[i].TrimEnd('.');
            status = string.Join(". ", parts);
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;

            if (!(gizmo is Gizmo_GrowthTier))
                return false;

            Pawn child = GetChild(gizmo);
            if (child?.ageTracker == null)
                return false;

            int tier = child.ageTracker.GrowthTier;
            if (tier < 0 || tier >= GrowthUtility.GrowthTiers.Length)
                return false;

            bool atMaxTier = child.ageTracker.AtMaxGrowthTier;
            var parts = new List<string>();

            // Header lines: "Growth tier: N" plus the explanatory paragraph
            // (Gizmo_GrowthTier.GrowthTierTooltip, first segment).
            parts.Add("StatsReport_GrowthTier".Translate().Resolve().StripTags() + ": " + tier);
            parts.Add("StatsReport_GrowthTierDesc".Translate().Resolve().StripTags());

            // Max-tier note, or progress toward the next tier with the daily
            // gain rate — the same branch vanilla's tooltip takes.
            if (atMaxTier)
            {
                parts.Add("MaxTier".Translate().Resolve().StripTags() + ": "
                    + "MaxTierDesc".Translate(child.Named("PAWN")).Resolve().StripTags());
            }
            else
            {
                float pointsRequirement = GrowthUtility.GrowthTiers[tier + 1].pointsRequirement;
                string progress = "ProgressToNextGrowthTier".Translate().Resolve().StripTags() + ": "
                    + Mathf.FloorToInt(child.ageTracker.growthPoints) + " / " + pointsRequirement;
                if (child.ageTracker.canGainGrowthPoints)
                {
                    progress += string.Format(" (+{0})", "PerDay".Translate(
                        child.ageTracker.GrowthPointsPerDay.ToStringByStyle(ToStringStyle.FloatMaxTwo)).Resolve().StripTags());
                }
                parts.Add(progress);
            }

            // Next growth moment age (growth birthdays at 7, 10, 13 — read from
            // GrowthUtility.IsGrowthBirthday, never hardcoded per moment).
            if (child.ageTracker.AgeBiologicalYears < 13)
            {
                for (int age = child.ageTracker.AgeBiologicalYears + 1; age <= 13; age++)
                {
                    if (GrowthUtility.IsGrowthBirthday(age))
                    {
                        parts.Add("NextGrowthMomentAt".Translate().Resolve().StripTags() + ": " + age);
                        break;
                    }
                }
            }

            // Passion/trait rewards at this tier, then at the next tier — the
            // vanilla bullet lists, joined for speech instead of newlined.
            parts.Add(FormatTierRewards(
                "ThisGrowthTier".Translate(tier).Resolve().StripTags(),
                GrowthUtility.GrowthTiers[tier]));
            if (!atMaxTier)
            {
                parts.Add(FormatTierRewards(
                    "NextGrowthTier".Translate(tier + 1).Resolve().StripTags(),
                    GrowthUtility.GrowthTiers[tier + 1]));
            }

            // Trim trailing periods so joining never produces double periods.
            for (int i = 0; i < parts.Count; i++)
                parts[i] = parts[i].TrimEnd('.');
            description = string.Join(". ", parts);
            return true;
        }

        /// <summary>
        /// Formats one tier's reward bullets ("Choose N passions from M options",
        /// "Choose N trait from M options") using the game's own keys, with the
        /// vanilla passion-line visibility rule (only when the tier can grant
        /// passions). Bullets join via the localized connective for natural
        /// speech flow, matching the legacy FormatTierRewards.
        /// </summary>
        private static string FormatTierRewards(string header, GrowthUtility.GrowthTier tier)
        {
            var rewards = new List<string>();
            if (tier.passionGainsRange.TrueMax > 0)
                rewards.Add("NumPassionsFromOptions".Translate(
                    tier.passionGainsRange.ToString(), tier.passionChoices).Resolve().StripTags().TrimEnd('.'));
            rewards.Add("NumTraitsFromOptions".Translate(
                tier.traitGains, tier.traitChoices).Resolve().StripTags().TrimEnd('.'));
            string connective = "RimWorldAccess.Inspection.Gizmo.Status.GrowthRewardsAnd".Translate();
            return "RimWorldAccess.Inspection.Gizmo.Status.GrowthTierRewardsLine".Translate(
                header, string.Join(connective, rewards));
        }

        /// <summary>Resolves the gizmo's private child pawn via the cached FieldInfo.</summary>
        private static Pawn GetChild(Gizmo gizmo)
        {
            try
            {
                return ChildField?.GetValue(gizmo) as Pawn;
            }
            catch
            {
                return null;
            }
        }
    }
}
