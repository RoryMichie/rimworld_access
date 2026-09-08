using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>Social-tab data and interactions: relations, opinions, ideology and romance.</summary>
    public static class SocialTabHelper
    {
        /// <summary>A relation with another pawn.</summary>
        public class RelationInfo
        {
            public Pawn OtherPawn { get; set; }
            public string OtherPawnName { get; set; }
            public List<string> Relations { get; set; }
            public int MyOpinion { get; set; }
            public int TheirOpinion { get; set; }
            public List<string> DetailLines { get; set; }
            public int RelationshipLineIndex { get; set; }
            public bool CanChangePregnancyApproach { get; set; }
            public RimWorld.PregnancyApproach CurrentPregnancyApproach { get; set; }

            public RelationInfo()
            {
                Relations = new List<string>();
                DetailLines = new List<string>();
                RelationshipLineIndex = -1;
            }
        }

        /// <summary>Ideology and role information.</summary>
        public class IdeologyInfo
        {
            public Ideo Ideo { get; set; }
            public string IdeoName { get; set; }
            public float Certainty { get; set; }
            public Precept_Role Role { get; set; }
            public string RoleName { get; set; }
        }


        #region Ideology & Role

        public static IdeologyInfo GetIdeologyInfo(Pawn pawn)
        {
            if (pawn?.ideo == null || !ModsConfig.IdeologyActive)
                return null;

            string noneLabel = "RimWorldAccess.Pawns.Social.Ideology.None".Translate();
            var info = new IdeologyInfo
            {
                Ideo = pawn.Ideo,
                IdeoName = pawn.Ideo?.name ?? noneLabel,
                Certainty = pawn.ideo.Certainty
            };

            if (pawn.Ideo != null)
            {
                info.Role = pawn.Ideo.GetRole(pawn);
                info.RoleName = info.Role?.LabelCap ?? noneLabel;
            }

            return info;
        }

        /// <summary>
        /// Every role vanilla's "Choose role..." menu would list, inactive and ineligible ones
        /// included so they stay navigable with a reason. Mirrors SocialCardUtility's cachedRoles:
        /// RitualUtility.AllRolesForPawn, which does not filter by Active, sorted by
        /// displayOrderInImpact.
        /// </summary>
        /// <summary>Compat gates over the roles section: a mod that Harmony-hides vanilla's role
        /// dropdown registers the same condition here, so this data-model reading stays in parity.</summary>
        private static readonly List<Func<Pawn, bool>> roleSelectionSuppressors =
            new List<Func<Pawn, bool>>();

        public static void RegisterRoleSelectionSuppressor(Func<Pawn, bool> suppressor)
        {
            if (suppressor != null)
                roleSelectionSuppressors.Add(suppressor);
        }

        public static List<Precept_Role> GetAvailableRoles(Pawn pawn)
        {
            if (pawn?.Ideo == null || !ModsConfig.IdeologyActive)
                return new List<Precept_Role>();

            for (int i = 0; i < roleSelectionSuppressors.Count; i++)
            {
                try
                {
                    if (roleSelectionSuppressors[i](pawn))
                        return new List<Precept_Role>();
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Role selection suppressor error", ex);
                }
            }

            return RitualUtility.AllRolesForPawn(pawn)
                .OrderBy(r => r.def.displayOrderInImpact)
                .ToList();
        }

        /// <summary>
        /// Opens the RoleChange ritual dialog for <paramref name="newRole"/>, or null to remove the
        /// current role — the flow vanilla's "Choose role..." button starts. The assignment happens
        /// through the ritual, so its requirement, confirmation text and quality gates all apply.
        /// </summary>
        public static bool OpenRoleChangeRitual(Pawn pawn, Precept_Role newRole)
        {
            try
            {
                if (pawn?.Ideo == null)
                    return false;

                var roleChangeRitual = (Precept_Ritual)pawn.Ideo.GetPrecept(PreceptDefOf.RoleChange);
                if (roleChangeRitual == null)
                    return false;

                TargetInfo ritualTarget = roleChangeRitual.targetFilter.BestTarget(pawn, TargetInfo.Invalid);
                if (!ritualTarget.IsValid)
                {
                    Messages.Message((Find.IdeoManager.classicMode
                        ? "AbilityDisabledNoRitualSpot"
                        : "AbilityDisabledNoAltarIdeogramOrRitualsSpot").Translate(), pawn, MessageTypeDefOf.RejectInput);
                    return false;
                }

                var dialog = (Dialog_BeginRitual)roleChangeRitual.GetRitualBeginWindow(
                    ritualTarget, null, null, pawn, new Dictionary<string, Pawn> { { "role_changer", pawn } });
                dialog.SetRoleToChangeTo(newRole);
                Find.WindowStack.Add(dialog);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error opening role change ritual: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.ErrorAssignRole".Loc(), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Mirrors the option gate in vanilla's Choose-role float menu: the role must be active with
        /// its requirements met, and leader roles are restricted to the primary ideoligion.
        /// </summary>
        public static bool IsEligibleForRole(Precept_Role role, Pawn pawn)
        {
            if (role == null || pawn == null)
                return false;

            return role.Active
                && role.RequirementsMet(pawn)
                && (!role.def.leaderRole || pawn.Ideo == Faction.OfPlayer.ideos.PrimaryIdeo);
        }

        #endregion

        #region Relations

        public static List<RelationInfo> GetRelations(Pawn pawn)
        {
            var relations = new List<RelationInfo>();

            if (pawn?.relations == null)
                return relations;

            var relatedPawns = pawn.relations.RelatedPawns;

            foreach (var otherPawn in relatedPawns)
            {
                var relationInfo = BuildRelationInfo(pawn, otherPawn, pawn.relations.OpinionOf(otherPawn));
                relations.Add(relationInfo);
            }

            // Pawns with a non-zero opinion count even without a direct relation.
            if (pawn.Map != null)
            {
                var allPawns = pawn.Map.mapPawns.AllPawnsSpawned;
                foreach (var otherPawn in allPawns)
                {
                    if (otherPawn == pawn || relatedPawns.Contains(otherPawn))
                        continue;

                    int opinion = pawn.relations.OpinionOf(otherPawn);
                    if (opinion != 0)
                    {
                        relations.Add(BuildRelationInfo(pawn, otherPawn, opinion));
                    }
                }
            }

            relations = relations.OrderByDescending(r => r.MyOpinion).ToList();

            return relations;
        }

        private static List<string> GetRelationLabels(Pawn pawn, Pawn otherPawn)
        {
            var labels = new List<string>();

            // GetRelations catches implied relations: Parent stores Child as an inverse, not in
            // DirectRelations.
            foreach (var def in pawn.GetRelations(otherPawn))
            {
                labels.Add(def.GetGenderSpecificLabelCap(otherPawn));
            }

            // Friendship and rivalry labels when no family relation applies, under vanilla's own
            // keys of the same names.
            if (labels.Count == 0)
            {
                int opinion = pawn.relations.OpinionOf(otherPawn);
                if (opinion >= 20)
                    labels.Add("Friend".Translate());
                else if (opinion <= -20)
                    labels.Add("Rival".Translate());
                else
                    labels.Add("Acquaintance".Translate());
            }

            return labels;
        }

        private static RelationInfo BuildRelationInfo(Pawn pawn, Pawn otherPawn, int myOpinion)
        {
            var info = new RelationInfo
            {
                OtherPawn = otherPawn,
                OtherPawnName = otherPawn.LabelShort.StripTags(),
                Relations = GetRelationLabels(pawn, otherPawn),
                MyOpinion = myOpinion,
                TheirOpinion = otherPawn.relations?.OpinionOf(pawn) ?? 0
            };

            PopulateDetailLines(info, pawn, otherPawn);

            if (ModsConfig.BiotechActive && LovePartnerRelationUtility.LovePartnerRelationExists(pawn, otherPawn))
            {
                info.CanChangePregnancyApproach = true;
                info.CurrentPregnancyApproach = GetPregnancyApproach(pawn, otherPawn);
            }

            return info;
        }

        private static void PopulateDetailLines(RelationInfo info, Pawn pawn, Pawn otherPawn)
        {
            var lines = info.DetailLines;

            lines.Add("RimWorldAccess.Pawns.Social.Relation.Header"
                .Translate(otherPawn.LabelShort.StripTags()));

            if (info.Relations.Count > 0)
            {
                info.RelationshipLineIndex = lines.Count;
                lines.Add("RimWorldAccess.Pawns.Social.Relation.Relationships"
                    .Translate(string.Join(", ", info.Relations)));
            }

            lines.Add("RimWorldAccess.Pawns.Social.Relation.MyOpinion"
                .Translate(info.MyOpinion.ToString("+0;-0;0")));
            lines.Add("RimWorldAccess.Pawns.Social.Relation.TheirOpinion"
                .Translate(info.TheirOpinion.ToString("+0;-0;0")));

            lines.Add("RimWorldAccess.Pawns.Social.Relation.OpinionFactorsHeader".Translate());

            bool hasFactors = false;

            // Relation opinion modifiers, mirroring Pawn_RelationsTracker.OpinionExplanation.
            foreach (var def in pawn.GetRelations(otherPawn))
            {
                if (def.opinionOffset != 0)
                {
                    lines.Add("RimWorldAccess.Pawns.Social.Relation.OpinionFactor"
                        .Translate(def.GetGenderSpecificLabelCap(otherPawn),
                            def.opinionOffset.ToString("+0;-0;0")));
                    hasFactors = true;
                }
            }

            if (pawn.RaceProps.Humanlike && pawn.needs?.mood?.thoughts != null)
            {
                var thoughts = pawn.needs.mood.thoughts;
                var socialThoughts = new List<ISocialThought>();
                thoughts.GetDistinctSocialThoughtGroups(otherPawn, socialThoughts);

                foreach (var socialThought in socialThoughts)
                {
                    int opinionOffset = thoughts.OpinionOffsetOfGroup(socialThought, otherPawn);
                    if (opinionOffset != 0)
                    {
                        Thought thought = (Thought)socialThought;
                        string label = thought.LabelCapSocial.StripTags();

                        int count = 1;
                        if (thought.def.IsMemory && socialThought is Thought_MemorySocial memorySocial)
                        {
                            count = thoughts.memories.NumMemoriesInGroup(memorySocial);
                        }

                        if (count > 1)
                        {
                            label += $" x{count}";
                        }

                        lines.Add("RimWorldAccess.Pawns.Social.Relation.OpinionFactor"
                            .Translate(label, opinionOffset.ToString("+0;-0;0")));
                        hasFactors = true;
                    }
                }
            }

            if (!hasFactors)
            {
                lines.Add("RimWorldAccess.Pawns.Social.Relation.NoFactors".Translate());
            }

            if (LovePartnerRelationUtility.LovePartnerRelationExists(pawn, otherPawn))
            {
                lines.Add("RimWorldAccess.Pawns.Social.Relation.LovePartners".Translate());
            }

            // Mirrors the two debug tooltip lines SocialCardUtility.GetPawnRowTooltip appends under
            // Prefs.DevMode, in vanilla's own "F2" format.
            if (Prefs.DevMode)
            {
                lines.Add("RimWorldAccess.Dev.Info.SocialRelation".Translate(
                    pawn.relations.CompatibilityWith(otherPawn).ToString("F2"),
                    pawn.relations.SecondaryRomanceChanceFactor(otherPawn).ToString("F2")));
            }
        }

        private static RimWorld.PregnancyApproach GetPregnancyApproach(Pawn pawn, Pawn partner)
        {
            if (pawn?.relations == null || partner == null)
                return RimWorld.PregnancyApproach.Normal;

            return pawn.relations.GetPregnancyApproachForPartner(partner);
        }

        /// <summary>Sets the pregnancy approach through Pawn_RelationsTracker, which writes it on both pawns.</summary>
        public static bool SetPregnancyApproach(Pawn pawn, Pawn partner, RimWorld.PregnancyApproach approach)
        {
            try
            {
                if (pawn?.relations == null || partner == null)
                    return false;

                pawn.relations.SetPregnancyApproach(partner, approach);

                string approachLabel = approach.GetLabel().CapitalizeFirst();

                TolkHelper.Speak("RimWorldAccess.Pawns.Social.PregnancyApproachSet".Loc(approachLabel));
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error setting pregnancy approach: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.ErrorSetPregnancyApproach".Loc(), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        #endregion

        #region Romance

        /// <summary>A potential romance target, with its eligibility and chance.</summary>
        public class RomanceTargetInfo
        {
            public Pawn Target { get; set; }
            public string TargetName { get; set; }
            public bool IsViable { get; set; }
            public float Chance { get; set; }
            public string Reason { get; set; }
        }

        /// <summary>Mirrors SocialCardUtility.CanDrawTryRomance.</summary>
        public static bool CanTryRomance(Pawn pawn)
        {
            return ModsConfig.BiotechActive
                && pawn.ageTracker.AgeBiologicalYearsFloat >= 16f
                && pawn.Spawned
                && pawn.IsFreeColonist;
        }

        /// <summary>The translated cooldown message while the pawn is on romance cooldown.</summary>
        public static bool IsRomanceOnCooldown(Pawn pawn, out string cooldownText)
        {
            if (pawn.relations.IsTryRomanceOnCooldown)
            {
                int numTicks = pawn.relations.romanceEnableTick - Find.TickManager.TicksGame;
                cooldownText = "CantRomanceInitiateMessageCooldown".Translate(pawn, numTicks.ToStringTicksToPeriod());
                return true;
            }
            cooldownText = null;
            return false;
        }

        /// <summary>Wraps RelationsUtility.RomanceEligible.</summary>
        public static AcceptanceReport GetRomanceInitiatorEligibility(Pawn pawn)
        {
            return RelationsUtility.RomanceEligible(pawn, initiator: true, forOpinionExplanation: false);
        }

        /// <summary>
        /// Romance targets sorted as vanilla sorts them — viable descending by chance, then
        /// non-viable alphabetically. Mirrors SocialCardUtility.RomanceOptions.
        /// </summary>
        public static List<RomanceTargetInfo> GetRomanceTargets(Pawn romancer)
        {
            var viable = new List<(float chance, RomanceTargetInfo info)>();
            var nonViable = new List<RomanceTargetInfo>();

            foreach (Pawn target in romancer.Map.mapPawns.FreeColonistsSpawned)
            {
                if (target == romancer)
                    continue;

                // Vanilla's own attraction filter, from RelationsUtility.RomanceOption.
                if (!RelationsUtility.AttractedToGender(romancer, target.gender))
                    continue;

                var eligibility = RelationsUtility.RomanceEligiblePair(romancer, target, forOpinionExplanation: false);

                if (eligibility.Accepted)
                {
                    float chance = InteractionWorker_RomanceAttempt.SuccessChance(romancer, target, 1f);

                    viable.Add((chance, new RomanceTargetInfo
                    {
                        Target = target,
                        TargetName = target.LabelShort.StripTags(),
                        IsViable = true,
                        Chance = chance
                    }));
                }
                else if (!eligibility.Reason.NullOrEmpty())
                {
                    nonViable.Add(new RomanceTargetInfo
                    {
                        Target = target,
                        TargetName = target.LabelShort.StripTags(),
                        IsViable = false,
                        Reason = eligibility.Reason
                    });
                }
            }

            var result = new List<RomanceTargetInfo>();
            result.AddRange(viable.OrderByDescending(v => v.chance).Select(v => v.info));
            result.AddRange(nonViable.OrderBy(nv => nv.TargetName));
            return result;
        }

        /// <summary>
        /// The romance factor breakdown for StatBreakdownState: the game's own factors, with its
        /// visual "x" multiplier notation spelled out.
        /// </summary>
        public static string BuildRomanceBreakdown(Pawn romancer, Pawn target)
        {
            string factors = InteractionWorker_RomanceAttempt.RomanceFactors(romancer, target).StripTags();
            // The "x" in ": x22%" is visual shorthand that reads poorly aloud.
            factors = System.Text.RegularExpressions.Regex.Replace(factors, @": x(\d)", ": $1");
            // The leading " - " would read as indent level 1 and collapse the lines under a parent
            // node, so each factor line is flattened to a root item.
            factors = System.Text.RegularExpressions.Regex.Replace(factors, @"(?m)^ - ", "");

            return factors;
        }

        private static MethodInfo giveRomanceJobWithWarningMethod;

        /// <summary>
        /// Starts a romance attempt through the private RelationsUtility.GiveRomanceJobWithWarning,
        /// which warns about existing relationships. False when a romance job is already queued.
        /// </summary>
        public static bool InitiateRomance(Pawn romancer, Pawn target)
        {
            try
            {
                // Vanilla's float menu closes after selection; this tree keeps the action available,
                // so duplicate romance jobs need guarding here.
                if (romancer.CurJob?.def == JobDefOf.TryRomance ||
                    romancer.jobs.jobQueue.Any(j => j.job.def == JobDefOf.TryRomance))
                {
                    return false;
                }

                if (giveRomanceJobWithWarningMethod == null)
                {
                    giveRomanceJobWithWarningMethod = AccessTools.Method(
                        typeof(RelationsUtility), "GiveRomanceJobWithWarning");
                }

                giveRomanceJobWithWarningMethod.Invoke(null, new object[] { romancer, target });
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error initiating romance: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Social.ErrorInitiateRomance".Loc(), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        #endregion
    }
}
