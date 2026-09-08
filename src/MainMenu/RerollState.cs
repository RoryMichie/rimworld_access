using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// DOCUMENTED EXCEPTION: no row grammar
    /// applies here. This is a modal progress/status surface, not a list of focusable rows —
    /// <see cref="RerollScope"/> (src/Shell/Screens/PawnFilterScopes.Game.cs) claims only
    /// Cancel, and the batch loop's own periodic "Searching, attempt N" status
    /// (<see cref="ProcessBatch"/>) is a progress ticker, the same category as a loading
    /// spinner's announcement, not an <see cref="ElementDescription"/>-describable control.
    /// </summary>
    public static class RerollState
    {
        private static bool isActive;
        private static int pawnIndex;
        private static PawnFilter filter;
        private static Pawn pawn;
        private static int attempts;
        private static int limit;
        private static PawnGenerationRequest request;
        private static Faction resolvedFaction;
        private static XenotypeDef xenotype;
        private static Stopwatch stopwatch;
        private static long nextAnnounceMs;
        private static bool needsHealthCheck;
        private static bool needsWorkCheck;

        private static bool useFullPipeline;
        private static bool foreignPatchChecked;
        private static bool foreignGeneratePawnPatches;

        private static MethodInfo randomAgeMethodInfo;
        private static MethodInfo randomTraitMethodInfo;
        private static MethodInfo randomSkillMethodInfo;
        private static MethodInfo randomHealthMethodInfo;
        private static MethodInfo randomBodyTypeMethodInfo;
        private static MethodInfo randomGeneMethodInfo;
        private static bool reflectionInitialized;
        private static bool reflectionAvailable;

        public static bool IsActive => isActive;

        private static void InitializeReflection()
        {
            if (reflectionInitialized) return;
            reflectionInitialized = true;

            try
            {
                randomAgeMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateRandomAge", BindingFlags.NonPublic | BindingFlags.Static);
                randomTraitMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateTraits", BindingFlags.NonPublic | BindingFlags.Static);
                randomSkillMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateSkills", BindingFlags.NonPublic | BindingFlags.Static);
                randomHealthMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateInitialHediffs", BindingFlags.NonPublic | BindingFlags.Static);
                randomBodyTypeMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateBodyType", BindingFlags.NonPublic | BindingFlags.Static);
                randomGeneMethodInfo = typeof(PawnGenerator)
                    .GetMethod("GenerateGenes", BindingFlags.NonPublic | BindingFlags.Static);

                reflectionAvailable = randomAgeMethodInfo != null
                    && randomTraitMethodInfo != null
                    && randomSkillMethodInfo != null
                    && randomHealthMethodInfo != null
                    && randomBodyTypeMethodInfo != null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Failed to initialize reroll reflection: {ex.Message}");
                reflectionAvailable = false;
            }
        }

        public static void Start(int index)
        {
            InitializeReflection();

            pawnIndex = index;
            filter = PawnFilterData.ActiveFilter;
            limit = filter.RerollLimit;
            attempts = 0;
            needsHealthCheck = filter.Health != HealthFilterMode.AllowAll;
            needsWorkCheck = filter.Work != WorkFilterMode.AllowAll;

            var pawns = Find.GameInitData.startingAndOptionalPawns;
            pawn = pawns[pawnIndex];
            pawn = RandomizeCandidateInPlace(pawn);
            attempts++;

            if (filter.Evaluate(pawn) && StartingPawnUtility.WorkTypeRequirementsSatisfied())
            {
                PawnFilterData.LastRerollAttempts = attempts;
                PawnFilterData.LastRerollSucceeded = true;
                TutorSystem.Notify_Event("RandomizePawn");
                StartingPawnState.OnRerollComplete(true, attempts, false);
                return;
            }

            // The fast loop never calls GeneratePawn, so foreign patches there (Gender Works grants
            // organs in one) force full-fidelity rerolls; a fresh pawn per attempt also stops
            // discarded-attempt mod state leaking into the match.
            useFullPipeline = !reflectionAvailable || ForeignGeneratePawnPatchesPresent();

            if (!useFullPipeline)
            {
                request = StartingPawnUtility.GetGenerationRequest(pawnIndex);
                request.ValidateAndFix();

                if (filter.Gender.HasValue)
                    pawn.gender = filter.Gender.Value;

                Faction faction;
                resolvedFaction = request.Faction ??
                    (!Find.FactionManager.TryGetRandomNonColonyHumanlikeFaction(out faction, false, true)
                        ? Faction.OfAncients : faction);

                xenotype = ModsConfig.BiotechActive ? PawnGenerator.GetXenotypeForGeneratedPawn(request) : null;
            }

            isActive = true;
            stopwatch = Stopwatch.StartNew();
            nextAnnounceMs = 1500;
        }

        private static bool ForeignGeneratePawnPatchesPresent()
        {
            if (foreignPatchChecked) return foreignGeneratePawnPatches;
            foreignPatchChecked = true;
            try
            {
                var method = AccessTools.Method(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
                    new[] { typeof(PawnGenerationRequest) });
                var patches = Harmony.GetPatchInfo(method);
                var owners = patches == null
                    ? new List<string>()
                    : patches.Prefixes.Concat(patches.Postfixes)
                        .Concat(patches.Transpilers).Concat(patches.Finalizers)
                        .Select(p => p.owner).Distinct().OrderBy(o => o).ToList();
                foreignGeneratePawnPatches = owners.Count > 0;
                if (foreignGeneratePawnPatches)
                    Log.Message("[RimWorld Access] Pawn filter rerolls use the full generation pipeline; PawnGenerator.GeneratePawn is patched by: " + string.Join(", ", owners));
            }
            catch
            {
                foreignGeneratePawnPatches = true;
            }
            return foreignGeneratePawnPatches;
        }

        public static void ProcessBatch()
        {
            if (!isActive) return;

            var batchStopwatch = Stopwatch.StartNew();
            const long batchTimeMs = 10; // yield after ~10ms to keep UI responsive

            if (useFullPipeline)
            {
                ProcessFullPipelineBatch(batchStopwatch, batchTimeMs);
                return;
            }

            while (attempts < limit && batchStopwatch.ElapsedMilliseconds < batchTimeMs)
            {
                try
                {
                    attempts++;

                    // Generate age (skip RedressPawn — only appearance/gear)
                    // Reuse existing age tracker to avoid LifeStageWorker crash mid-game
                    // (Notify_LifeStageStarted accesses pawn.Drawer which is null for unspawned pawns)
                    randomAgeMethodInfo.Invoke(null, new object[] { pawn, request });

                    if (!filter.CheckAgeForFastReroll(pawn))
                        continue;

                    pawn.story.traits = new TraitSet(pawn);
                    pawn.skills = new Pawn_SkillTracker(pawn);
                    PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo(pawn, resolvedFaction.def, request, xenotype);
                    randomTraitMethodInfo.Invoke(null, new object[] { pawn, request });
                    randomSkillMethodInfo.Invoke(null, new object[] { pawn, request });

                    if (!filter.CheckSkillsAndTraitsForFastReroll(pawn))
                        continue;

                    // Generate health — only if health filter is active
                    if (needsHealthCheck)
                    {
                        bool healthGenSuccess = false;
                        for (int i = 0; i < 100 && !healthGenSuccess; i++)
                        {
                            pawn.health.Reset();
                            try
                            {
                                // Vanilla order (Verse.PawnGenerator.GeneratePawn): GenerateInitialHediffs
                                // runs before Notify_NewPawnGenerating.
                                randomHealthMethodInfo.Invoke(null, new object[] { pawn, request });
                                Find.Scenario.Notify_NewPawnGenerating(pawn, request.Context);

                                if (!(pawn.Dead || pawn.Destroyed || pawn.Downed))
                                    healthGenSuccess = true;
                            }
                            catch
                            {
                                continue;
                            }
                        }

                        if (!filter.CheckHealthForFastReroll(pawn))
                            continue;
                    }

                    // Work check — only if work filter is active
                    if (needsWorkCheck)
                    {
                        pawn.workSettings?.EnableAndInitialize();
                        if (!filter.CheckWorkForFastReroll(pawn))
                            continue;
                    }
                    Find.Scenario.Notify_PawnGenerated(pawn, request.Context, true);
                    if (!filter.Evaluate(pawn))
                        continue;
                    FinalizeMatch();
                    return;
                }
                catch (Exception ex)
                {
                    var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException : ex;
                    Log.Warning($"[RimWorld Access] Error during reroll (attempt {attempts}): {inner?.Message ?? ex.Message}");
                    try
                    {
                        DiscardIfWorldPawn(pawn);
                        pawn = RandomizeCandidateInPlace(pawn);
                    }
                    catch (Exception ex2)
                    {
                        Log.Error($"[RimWorld Access] Critical error in pawn cleanup: {ex2.Message}");
                        Finalize(false, false);
                        return;
                    }
                }
            }

            FinishBatchSlice();
        }

        /// <summary>A fresh pawn per attempt via the randomize button's own RandomizeInPlace, so
        /// every mod hook fires; at least one attempt runs per frame.</summary>
        private static void ProcessFullPipelineBatch(Stopwatch batchStopwatch, long batchTimeMs)
        {
            while (attempts < limit && batchStopwatch.ElapsedMilliseconds < batchTimeMs)
            {
                attempts++;
                try
                {
                    pawn = RandomizeCandidateInPlace(pawn);
                    if (filter.Evaluate(pawn) && StartingPawnUtility.WorkTypeRequirementsSatisfied())
                    {
                        Finalize(true, false);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimWorld Access] Error during reroll (attempt {attempts}): {ex.Message}");
                    DiscardIfWorldPawn(pawn);
                }
            }

            FinishBatchSlice();
        }

        /// <summary>The randomize button's own regeneration, with tale recording gated off
        /// for the duration (see <see cref="RerollTaleSuppressionPatch"/> for why).</summary>
        private static Pawn RandomizeCandidateInPlace(Pawn candidate)
        {
            SpouseRelationUtility.Notify_PawnRegenerated(candidate);
            RerollTaleSuppressionPatch.Active = true;
            try
            {
                return StartingPawnUtility.RandomizeInPlace(candidate);
            }
            finally
            {
                RerollTaleSuppressionPatch.Active = false;
            }
        }

        /// <summary>An attempt that throws mid-regeneration can leave the candidate passed to
        /// the world pool; the next RandomizeInPlace would then error out of its own PassToWorld
        /// and leak the pawn. Discarding is only valid for pawns the pool actually holds.</summary>
        private static void DiscardIfWorldPawn(Pawn candidate)
        {
            if (candidate != null && Find.WorldPawns.Contains(candidate))
                Find.WorldPawns.RemoveAndDiscardPawnViaGC(candidate);
        }

        private static void FinishBatchSlice()
        {
            if (attempts >= limit)
            {
                Finalize(false, false);
                return;
            }

            if (stopwatch.ElapsedMilliseconds >= nextAnnounceMs)
            {
                TolkHelper.Speak("RimWorldAccess.Reroll.SearchingAttempts".Loc(attempts), SpeechPriority.High);
                nextAnnounceMs = stopwatch.ElapsedMilliseconds + 2000;
            }
        }

        public static void Cancel()
        {
            if (!isActive) return;
            Finalize(false, true);
        }

        private static void FinalizeMatch()
        {
            PawnGenerator.RedressPawn(pawn, request);

            // Generate genes and body type (deferred expensive ops)
            if (ModsConfig.BiotechActive && randomGeneMethodInfo != null)
            {
                pawn.genes = new Pawn_GeneTracker(pawn);
                randomGeneMethodInfo.Invoke(null, new object[] { pawn, xenotype, request });
            }

            randomBodyTypeMethodInfo.Invoke(null, new object[] { pawn, request });

            // MUTATION-C: mirrors GeneratePawn's Humanlike style block (inline in the generator,
            // no callable seam); without it the match keeps attempt 1's hair/beard/tattoos.
            if (pawn.RaceProps.Humanlike)
            {
                pawn.story.hairDef = PawnStyleItemChooser.RandomHairFor(pawn);
                if (pawn.style != null)
                {
                    pawn.style.beardDef = PawnStyleItemChooser.RandomBeardFor(pawn);
                    if (ModsConfig.IdeologyActive && !pawn.DevelopmentalStage.Baby())
                    {
                        pawn.style.FaceTattoo = PawnStyleItemChooser.RandomTattooFor(pawn, TattooType.Face);
                        pawn.style.BodyTattoo = PawnStyleItemChooser.RandomTattooFor(pawn, TattooType.Body);
                    }
                    else
                    {
                        pawn.style.SetupTattoos_NoIdeology();
                    }
                }
            }

            // Attempts 2+ (the fast batch loop above) mutate this same pawn object's health/
            // traits/skills in place without ever calling StartingPawnUtility.GeneratePossessions
            // or SpouseRelationUtility.Notify_PawnRegenerated the way
            // StartingPawnUtility.NewGeneratedStartingPawn / RandomizePawn do on every attempt.
            // Regenerate possessions (they're keyed off the final hediffs/traits — e.g. drugs
            // for an addiction attempt 1 rolled but the matched attempt didn't) and refresh
            // spouse-naming state here so the matched pawn ends up byte-equivalent to one
            // produced by the full vanilla pipeline.
            StartingPawnUtility.GeneratePossessions(pawn);
            SpouseRelationUtility.Notify_PawnRegenerated(pawn);

            Finalize(true, false);
        }

        private static void Finalize(bool success, bool cancelled)
        {
            isActive = false;
            PawnFilterData.LastRerollAttempts = attempts;
            PawnFilterData.LastRerollSucceeded = success;
            TutorSystem.Notify_Event("RandomizePawn");
            StartingPawnState.OnRerollComplete(success, attempts, cancelled);
        }
    }

    /// <summary>
    /// Drops tales recorded while a filter-reroll candidate rolls: pre-game generation can
    /// divorce a rolled spouse (SetIdeo → RemoveSpousesAsForbiddenByIdeo → DoDivorce →
    /// RecordTale), which errors on TicksAbs and archives a garbage-dated tale per unlucky
    /// roll. Vanilla's own randomize button keeps vanilla behavior — the gate is up only
    /// inside the batch's rolls.
    /// </summary>
    [HarmonyPatch(typeof(TaleRecorder), nameof(TaleRecorder.RecordTale))]
    public static class RerollTaleSuppressionPatch
    {
        internal static bool Active;

        public static bool Prefix(ref Tale __result)
        {
            if (!Active)
            {
                return true;
            }
            __result = null;
            return false;
        }
    }
}
