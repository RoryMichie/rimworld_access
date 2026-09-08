using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using DeityType = RimWorld.IdeoFoundation_Deity.Deity;

namespace RimWorldAccess
{
    /// <summary>
    /// Data/mutation layer for a deity-foundation ideoligion's deities. Trimmed down from
    /// its own TreeNavigationHelper-driven overlay (kept every vanilla mutation
    /// vehicle: the reflection-invoked <c>GenerateNewDeity</c>/<c>FillDeity</c>, the
    /// <c>DeityCountRange</c> gates, <c>RandomizeAll</c>) to a pure
    /// data/mutation surface consumed by <see cref="RimWorldAccess.Shell.IdeoDeityScreenScope"/>,
    /// which now owns rows, announcements, and input (see that class's remarks for the ScreenScope
    /// shape). <see cref="IsActive"/>/<see cref="Open"/>/<see cref="Close"/> keep their exact
    /// external contract — <c>IdeoBuilderOverlays.CloseAllOverlayEditors</c> and
    /// <c>StateResetRegistry</c> call these unchanged.
    /// </summary>
    public static class IdeoDeityListState
    {
        public static bool IsActive { get; private set; }

        private static Ideo ideo;
        private static IdeoFoundation_Deity foundation;

        private static readonly System.Reflection.MethodInfo GenerateNewDeityMethod =
            AccessTools.Method(typeof(IdeoFoundation_Deity), "GenerateNewDeity");
        private static readonly System.Reflection.MethodInfo FillDeityMethod =
            AccessTools.Method(typeof(IdeoFoundation_Deity), "FillDeity");

        public static Ideo Ideo
        {
            get { return ideo; }
        }

        /// <summary>The live deity list, or null while inactive. Same list reference vanilla's own
        /// dialog mutates — callers must not cache it across a rebuild.</summary>
        public static IReadOnlyList<DeityType> Deities
        {
            get { return foundation != null ? foundation.DeitiesListForReading : null; }
        }

        public static void Open(Ideo targetIdeo)
        {
            if (targetIdeo?.foundation is IdeoFoundation_Deity f)
            {
                ideo = targetIdeo;
                foundation = f;
                IsActive = true;
            }
        }

        public static void Close()
        {
            IsActive = false;
            ideo = null;
            foundation = null;
        }

        /// <summary>"{name}, {type}, {gender}" — the label every deity row (and the per-deity float
        /// menu's title) uses.</summary>
        public static string BuildDeityLabel(DeityType deity)
        {
            var sb = new StringBuilder();
            sb.Append(deity.name);
            if (!string.IsNullOrEmpty(deity.type))
                sb.Append(", ").Append(deity.type);
            sb.Append(", ").Append(deity.gender.GetLabel().CapitalizeFirst());
            return sb.ToString();
        }

        #region Operations

        public static bool CanAddDeity()
        {
            return foundation != null && foundation.DeitiesListForReading.Count < ideo.DeityCountRange.max;
        }

        public static void AddDeity()
        {
            if (!CanAddDeity())
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            // MUTATION-C: mirrors IdeoFoundation_Deity.DoInfo's own "AddDeity" button body;
            // GenerateNewDeity is private, no public vehicle.
            var newDeity = (DeityType)GenerateNewDeityMethod.Invoke(foundation, null);
            foundation.DeitiesListForReading.Add(newDeity);
            ideo.RegenerateAllPreceptNames();
            ideo.RegenerateDescription();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Removes a deity, honoring the <see cref="Ideo.DeityCountRange"/> minimum. Speaks its
        /// own confirmation on success (or rejection reason at the floor) so the caller never
        /// double-announces — mirrors the old tree's single shared <c>RemoveDeity</c> body, used by
        /// both the Delete hotkey and the float menu's "Remove" option. Arms
        /// <see cref="SuppressNextReturnReannounce"/> on success since the float-menu path's return
        /// focus would otherwise re-announce on top of this method's own spoken line.
        /// </summary>
        public static bool RemoveDeity(DeityType deity)
        {
            int min = ideo.DeityCountRange.min;
            if (foundation.DeitiesListForReading.Count <= min)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                string noun = (min <= 1) ? "Deity".Translate().ToString() : Find.ActiveLanguageWorker.Pluralize("Deity".Translate(), min);
                TolkHelper.Speak("DeitiesRequired".Loc(min, noun.Named("DEITYNOUN")), SpeechPriority.High);
                return false;
            }
            foundation.DeitiesListForReading.Remove(deity);
            ideo.RegenerateDescription();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            TolkHelper.SpeakData($"{deity.name}, {(string)"RimWorldAccess.Ideology.Builder.Status.Removed".Translate()}");
            SuppressNextReturnReannounce();
            return true;
        }

        public static void RandomizeAll()
        {
            foundation.GenerateDeities();
            ideo.RegenerateAllPreceptNames();
            ideo.RegenerateDescription();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        /// <summary>Regenerates one deity's name/title/gender/related meme from scratch.</summary>
        public static void RegenerateDeity(DeityType deity)
        {
            // MUTATION-C: mirrors IdeoFoundation_Deity.DoInfo's own "Regenerate" float-menu body;
            // FillDeity is private, no public vehicle.
            FillDeityMethod.Invoke(foundation, new object[] { deity });
            ideo.RegenerateDescription();
        }

        #endregion

        #region Return-from-picker suppression

        // Frame on which an edit last spoke its own concise confirmation (RemoveDeity) — the same
        // shape as IdeoTypedPreceptState's identical
        // mechanism (see that class for the full rationale). When the sub-picker float menu closes
        // and the scope regains focus, it consults ShouldReannounceOnReturn so it doesn't
        // re-announce on top of whatever already spoke this frame.
        private static int suppressReturnReannounceFrame = -1;

        public static void SuppressNextReturnReannounce()
        {
            suppressReturnReannounceFrame = Time.frameCount;
        }

        public static bool ShouldReannounceOnReturn()
        {
            bool suppress = suppressReturnReannounceFrame >= 0
                && Time.frameCount - suppressReturnReannounceFrame <= 1;
            suppressReturnReannounceFrame = -1;
            return !suppress;
        }

        #endregion
    }
}
