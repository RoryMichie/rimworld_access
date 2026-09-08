using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Surfaces RimWorld Access documentation chapters (and enriched vanilla concepts) at the moment
    /// they become relevant — the contextual layer over the browsable Learning Helper.
    /// Teaching routes through <see cref="LessonAutoActivator.TeachOpportunity"/>, which respects the
    /// player's AdaptiveTraining setting, never re-teaches a learned concept, and de-duplicates
    /// against active lessons. The explicit-teach path has NO <c>gameMode</c>/ProgramState gate —
    /// that lives only in the background desire engine — so a chapter can be taught during world/site
    /// selection as well as in-game. Activation is announced by <see cref="LearningHelperPatch"/>.
    /// A once-per-session guard lets a per-open hook call <see cref="Teach"/> every time without
    /// re-announcing; the guard resets on game start/load, so each game gets a fresh teaching pass
    /// while the knowledge database still suppresses learned lessons.
    /// </summary>
    public static class DocsTeacher
    {
        private static readonly HashSet<string> offeredThisSession = new HashSet<string>();

        // Tile-by-tile cursor moves before we suggest the faster jump modes.
        private const int JumpModesMoveThreshold = 12;
        private static int cursorMoveCount;

        // At game start the colonists are still descending in drop pods, so FreeColonistsCount is
        // non-zero while none are on the map yet. Arm this flag and teach once one is actually
        // present (see PollDeferred).
        private static bool selectingColonistsPending;

        // The scanner and forbid/allow lessons both wait until ALL starting colonists have landed:
        // the scanner surveys the whole map, and pod cargo arrives forbidden, so teaching "free
        // everything" early would have the player free the map and then watch fresh forbidden items
        // rain down. Both teach in the same poll frame so the announcement coalesces; forbidding
        // stays gated on enough forbidden items, so a clean late-game load does not trigger it.
        private static bool scannerPending;
        private static bool forbiddingPending;

        /// <summary>
        /// Offers a lesson for the concept with the given defName. No-op when adaptive training is off,
        /// the scripted tutorial is running, the tutor is unavailable, the concept does not exist (a
        /// DLC concept whose DLC is absent), or it was already offered this session.
        /// </summary>
        public static void Teach(string conceptDefName, OpportunityType opportunity = OpportunityType.Important)
        {
            if (!TutorSystem.AdaptiveTrainingEnabled || TutorSystem.TutorialMode)
                return;
            if (Find.Tutor?.learningReadout == null)
                return;
            if (!offeredThisSession.Add(conceptDefName))
                return;

            ConceptDef conc = DefDatabase<ConceptDef>.GetNamedSilentFail(conceptDefName);
            if (conc == null)
                return;

            ReteachOverriddenConceptIfAlreadyLearned(conc);

            LessonAutoActivator.TeachOpportunity(conc, opportunity);
        }

        /// <summary>
        /// A concept the player completed long ago keeps its "learned" flag, which makes
        /// <see cref="LessonAutoActivator.TeachOpportunity"/> skip it — so our re-authored version of
        /// an overridden concept would never surface for a veteran player. The first time such a
        /// concept is taught contextually, clear that one concept's knowledge. Recorded in settings so
        /// it happens exactly once per player per concept; concepts without an override are untouched.
        /// </summary>
        private static void ReteachOverriddenConceptIfAlreadyLearned(ConceptDef conc)
        {
            if (ConceptHelpOverrides.GetHelpText(conc) == null)
                return;

            var settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null || settings.RetaughtOverriddenConcepts.Contains(conc.defName))
                return;

            settings.RetaughtOverriddenConcepts.Add(conc.defName);
            settings.Write();

            if (PlayerKnowledgeDatabase.IsComplete(conc))
                PlayerKnowledgeDatabase.SetKnowledge(conc, 0f);
        }

        /// <summary>
        /// Fires the cursor-landing contextual lessons for wherever the map cursor arrived, regardless
        /// of HOW it got there (arrowing, a scanner jump). Safe to call repeatedly.
        /// </summary>
        public static void NotifyCursorLanded(IntVec3 position, Map map)
        {
            if (map == null)
                return;

            // Cursor landing on a pawn is the moment to learn it can be inspected.
            if (position.GetFirstPawn(map) != null)
            {
                Teach("RWA_InspectingThings");
            }
            // The context menu gives a selected colonist orders about what is under the cursor, so
            // teach it the moment such a cursor reaches one. (Frame is a Building too.)
            else if (Find.Selector?.SingleSelectedThing is Pawn selectedColonist && selectedColonist.IsColonist
                     && position.GetThingList(map).Any(t =>
                            t.def.IsWeapon || t.def.IsApparel || t is Blueprint || t is Building))
            {
                Teach("RWA_ContextMenu");
            }
        }

        /// <summary>Counts tile-by-tile cursor movement and suggests the jump-modes chapter once the player has nudged enough times to feel the slowness.</summary>
        public static void NotifyCursorMoved()
        {
            if (offeredThisSession.Contains("RWA_JumpModes"))
                return;
            if (++cursorMoveCount >= JumpModesMoveThreshold)
                Teach("RWA_JumpModes", OpportunityType.GoodToKnow);
        }

        /// <summary>
        /// Arms the "selecting colonists" lesson for once a colonist is actually spawned, rather than
        /// at game start while the starting pawns are in transit. Resolved by
        /// <see cref="PollDeferred"/>.
        /// </summary>
        public static void RequestSelectingColonistsWhenPresent()
        {
            selectingColonistsPending = true;
        }

        /// <summary>
        /// Arms the map-basics lessons (scanner, then forbid/allow) for once every starting colonist
        /// has spawned, i.e. all pods are down. Both fire in the same poll frame so they coalesce.
        /// </summary>
        public static void RequestMapBasicsWhenAllColonistsPresent()
        {
            scannerPending = true;
            forbiddingPending = true;
        }

        /// <summary>
        /// Per-frame check for deferred teaches waiting on world state; a bool test until something is
        /// pending. Called every OnGUI pass, so it runs while the game is paused (the starting pods
        /// land on the first unpaused ticks).
        /// </summary>
        public static void PollDeferred()
        {
            if (!selectingColonistsPending && !scannerPending && !forbiddingPending)
                return;

            Map map = Find.CurrentMap;
            if (map == null)
                return;

            MapPawns mp = map.mapPawns;
            int spawned = mp.FreeColonistsSpawnedCount;

            // Teach selecting as soon as there is a colonist on the map to select.
            if (selectingColonistsPending && spawned > 0)
            {
                selectingColonistsPending = false;
                Teach("RWA_SelectingColonists", OpportunityType.Critical);
            }

            // The map-basics batch waits until spawned has caught up to the total free-colonist count
            // (which includes pawns still in transit), so all pod cargo is on the map. Forbidding
            // stays gated on a meaningful number of forbidden items.
            if ((scannerPending || forbiddingPending) && spawned > 0 && spawned >= mp.FreeColonistsCount)
            {
                if (scannerPending)
                {
                    scannerPending = false;
                    Teach("RWA_Scanner");
                }
                if (forbiddingPending)
                {
                    forbiddingPending = false;
                    if (CountForbiddenItems(map) > 20)
                        Teach("Forbidding");
                }
            }
        }

        /// <summary>Counts forbidden items, stopping past 21: only the threshold matters, so there is no point scanning a whole late-game map.</summary>
        private static int CountForbiddenItems(Map map)
        {
            int count = 0;
            foreach (Thing thing in map.listerThings.AllThings)
            {
                CompForbiddable forbiddable = thing.TryGetComp<CompForbiddable>();
                if (forbiddable != null && forbiddable.Forbidden)
                {
                    count++;
                    if (count > 20)
                        break;
                }
            }
            return count;
        }

        /// <summary>Clears per-session teaching state, so each game re-offers contextual lessons the player has not learned.</summary>
        public static void ResetSession()
        {
            offeredThisSession.Clear();
            cursorMoveCount = 0;
            selectingColonistsPending = false;
            scannerPending = false;
            forbiddingPending = false;
        }
    }
}
