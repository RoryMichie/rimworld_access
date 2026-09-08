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
    /// <summary>
    /// The windowless learning helper's data and mutations: the concept list in Active or All
    /// lessons mode, the help text, and the progressive-knowledge tracking that fills in a
    /// concept's completion as the player reads its help lines.
    ///
    /// A pure data/mutation backend behind <see cref="Shell.LearningHelperScope"/>, which owns
    /// every cursor and typeahead concern, reads this class through
    /// <see cref="ConceptAt"/>/<see cref="ConceptCount"/> and drives reading through
    /// <see cref="BeginReading"/>/<see cref="TrackLineVisit"/>/<see cref="CommitReading"/>. No
    /// selectedIndex survives here: "which concept is selected" is purely the scope's region
    /// cursor, and progressive reading therefore tracks its concept BY REFERENCE.
    ///
    /// Each newly visited content line fills a proportional share of the remaining knowledge gap.
    /// Knowledge commits when leaving the content region or via "Mark as Learned", never per line —
    /// a per-line commit would reintroduce the auto-removal-mid-read bug this design avoids.
    /// </summary>
    public static class LearningHelperState
    {
        private static bool isActive = false;
        private static bool showAllMode = false;
        private static List<ConceptDef> concepts = null;
        private static int openGeneration = 0;

        private static FieldInfo activeConceptsField = null;

        // Progressive reading state: tracked locally against the concept open in the content
        // region, committed to the game's database only on leaving it or via Mark as Learned.
        private static ConceptDef readingConcept = null;
        private static float pendingKnowledge = 0f;
        private static float detailStartKnowledge = 0f;
        private static readonly HashSet<int> visitedLines = new HashSet<int>();
        private static int totalContentLines = 0;

        public static bool IsActive => isActive;
        public static bool ShowAllMode => showAllMode;

        /// <summary>
        /// Bumped on every genuinely fresh <see cref="Open"/>, so
        /// <see cref="Shell.LearningHelperScope.OnPush"/> can tell a new open from a re-float and
        /// reset its one-shot opening-announcement guard.
        /// </summary>
        internal static int OpenGeneration => openGeneration;

        public static int ConceptCount => concepts?.Count ?? 0;

        /// <summary>The concept at a given index in the current mode's list, or null out of range.</summary>
        public static ConceptDef ConceptAt(int index)
        {
            if (concepts == null || index < 0 || index >= concepts.Count)
                return null;
            return concepts[index];
        }

        /// <summary>
        /// Opens the learning helper in active-lessons mode. Refuses with an announcement when
        /// adaptive training is off, since the scope is never pushed then. A zero-concept open
        /// still activates: the mode row is always present and navigable, and the scope's OnFocus
        /// speaks the distinct "no active lessons" notice instead of a per-row announcement.
        /// </summary>
        public static void Open()
        {
            if (!TutorSystem.AdaptiveTrainingEnabled || TutorSystem.TutorialMode)
            {
                TolkHelper.SpeakData("LearningHelper".Translate() + " " + "RimWorldAccess.Learning.DisabledSuffix".Translate());
                return;
            }

            showAllMode = false;
            concepts = CollectActiveConcepts();

            isActive = true;
            openGeneration++;
            ResetReadingProgress();
        }

        /// <summary>Closes the menu, committing any pending reading progress first.</summary>
        public static void Close()
        {
            CommitReading();
            isActive = false;
            concepts = null;
            showAllMode = false;
        }

        /// <summary>Closes the menu and announces it.</summary>
        public static void CloseMenu()
        {
            Close();
            TolkHelper.Speak("RimWorldAccess.Learning.Closed".Loc("LearningHelper".Translate()));
        }

        /// <summary>Flips between Active and All lessons, rebuilding the list. Touches no cursor state: the scope repositions and announces.</summary>
        public static void ToggleMode()
        {
            SetMode(!showAllMode);
        }

        /// <summary>Selects Active or All lessons outright and rebuilds the list, for the scope's mode-row ComboBox picker.</summary>
        public static void SetMode(bool showAll)
        {
            showAllMode = showAll;
            concepts = showAllMode ? CollectAllConcepts() : CollectActiveConcepts();
        }

        /// <summary>Re-derives the concept list for the current mode (a completed lesson may have dropped out of Active mode).</summary>
        public static void RefreshConcepts()
        {
            concepts = showAllMode ? CollectAllConcepts() : CollectActiveConcepts();
        }

        // Progressive reading knowledge.

        /// <summary>
        /// Begins or restarts progressive reading for a concept: snapshots its database knowledge
        /// as the baseline and clears the visited-lines set. Called whenever the scope's cursor
        /// enters the Lesson content region.
        /// </summary>
        public static void BeginReading(ConceptDef conc)
        {
            readingConcept = conc;
            if (conc == null)
            {
                ResetReadingProgress();
                return;
            }
            detailStartKnowledge = PlayerKnowledgeDatabase.GetKnowledge(conc);
            pendingKnowledge = detailStartKnowledge;
            visitedLines.Clear();
            totalContentLines = SplitHelpText(conc).Length;
        }

        /// <summary>
        /// Tracks a newly visited 0-based content line for the concept being read, spreading the
        /// remaining knowledge gap evenly across all its lines. Called after every cursor move
        /// inside the Lesson content region.
        /// </summary>
        public static void TrackLineVisit(int lineIndex)
        {
            if (readingConcept == null || totalContentLines <= 0)
                return;
            if (lineIndex < 0 || lineIndex >= totalContentLines)
                return;
            if (visitedLines.Add(lineIndex)) // Returns true only if newly added.
            {
                float knowledgePerLine = (1f - detailStartKnowledge) / totalContentLines;
                pendingKnowledge = Mathf.Clamp01(detailStartKnowledge + visitedLines.Count * knowledgePerLine);
            }
        }

        /// <summary>
        /// Commits pending reading progress to the game's database and clears reading state.
        /// Partial progress persists as-is: the game only auto-removes a concept from the Active
        /// list at >= 99.9%, so a partial commit never does. Called on leaving the content region
        /// and on close; Mark as Learned commits through <see cref="MarkLearned"/> instead.
        /// </summary>
        public static void CommitReading()
        {
            if (readingConcept != null && pendingKnowledge > PlayerKnowledgeDatabase.GetKnowledge(readingConcept))
            {
                PlayerKnowledgeDatabase.SetKnowledge(readingConcept, pendingKnowledge);
            }
            ResetReadingProgress();
        }

        /// <summary>Marks a concept fully learned through PlayerKnowledgeDatabase's own gated setter.</summary>
        public static void MarkLearned(ConceptDef conc)
        {
            if (conc == null)
                return;
            if (ReferenceEquals(conc, readingConcept))
            {
                pendingKnowledge = 1f;
            }
            PlayerKnowledgeDatabase.SetKnowledge(conc, 1f);
        }

        /// <summary>Whether a concept counts as complete — the committed database value, or the pending in-progress read if it has reached the completion threshold.</summary>
        public static bool IsComplete(ConceptDef conc)
        {
            if (conc == null)
                return false;
            if (ReferenceEquals(conc, readingConcept) && pendingKnowledge >= 0.999f)
                return true;
            return PlayerKnowledgeDatabase.IsComplete(conc);
        }

        /// <summary>The knowledge percentage text for a concept ("42%") — the live pending value while it is the one being read, else the committed database value.</summary>
        public static string KnowledgeText(ConceptDef conc)
        {
            if (conc == null)
                return "0%";
            float knowledge = ReferenceEquals(conc, readingConcept) ? pendingKnowledge : PlayerKnowledgeDatabase.GetKnowledge(conc);
            int percent = Mathf.Clamp(Mathf.RoundToInt(knowledge * 100f), 0, 100);
            return percent + "%";
        }

        private static void ResetReadingProgress()
        {
            readingConcept = null;
            pendingKnowledge = 0f;
            detailStartKnowledge = 0f;
            visitedLines.Clear();
            totalContentLines = 0;
        }

        // Content.

        /// <summary>The concept's help text, split into arrow-able lines (RimWorld Access's own corrected documentation takes priority over the game's own help text).</summary>
        public static string[] HelpLines(ConceptDef conc)
        {
            return SplitHelpText(conc);
        }

        private static string[] SplitHelpText(ConceptDef conc)
        {
            // The mod's corrected documentation is authoritative here, so the read-time override
            // wins and the game's own help text is the fallback.
            string text = ConceptHelpOverrides.GetHelpText(conc) ?? conc.HelpTextAdjusted;
            if (string.IsNullOrEmpty(text))
                return new string[] { "RimWorldAccess.Learning.NoHelpText".Translate().ToString() };

            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
                lines[i] = lines[i].Trim();

            lines = lines.Where(l => l.Length > 0).ToArray();

            return lines.Length > 0 ? lines : new string[] { text.Trim() };
        }

        /// <summary>The currently active, unlearned concepts from the game's LearningReadout.</summary>
        private static List<ConceptDef> CollectActiveConcepts()
        {
            try
            {
                if (Find.Tutor?.learningReadout == null)
                    return new List<ConceptDef>();

                if (activeConceptsField == null)
                {
                    activeConceptsField = AccessTools.Field(typeof(LearningReadout), "activeConcepts");
                }

                if (activeConceptsField == null)
                {
                    Log.Warning("[RimWorld Access] Could not find activeConcepts field on LearningReadout");
                    return new List<ConceptDef>();
                }

                var activeConcepts = activeConceptsField.GetValue(Find.Tutor.learningReadout) as List<ConceptDef>;
                if (activeConcepts == null)
                    return new List<ConceptDef>();

                // Vanilla shows Active Lessons in activation order and never re-sorts, so this
                // copy stays unsorted.
                return new List<ConceptDef>(activeConcepts);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Failed to collect active concepts: {ex.Message}");
                return new List<ConceptDef>();
            }
        }

        /// <summary>All non-triggered concepts, matching LearningReadout's showAllMode filter.</summary>
        private static List<ConceptDef> CollectAllConcepts()
        {
            try
            {
                // The sort key is deliberately ConceptHelpOverrides.DisplayLabel — what the player
                // actually hears, label corrections included — not vanilla's raw Def.label. The
                // comparer is culture-sensitive to match vanilla's own ordering
                // (LearningReadout.cs:217). No search re-sort: vanilla boosts matches to the top in
                // real time, while this mod's typeahead jumps the cursor within the fixed order, a
                // deliberate divergence the shared typeahead grammar preserves.
                return DefDatabase<ConceptDef>.AllDefsListForReading
                    .Where(c => !c.TriggeredDirect)
                    .OrderBy(c => ConceptHelpOverrides.DisplayLabel(c), StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Failed to collect all concepts: {ex.Message}");
                return new List<ConceptDef>();
            }
        }
    }
}
