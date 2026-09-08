using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Mutation/data logic for the base (issue-based) precept editor, opened from the
    /// Custom-creation hub, the in-game reform dialog, and the Archonexus reform screen
    /// (via <see cref="IdeoBuilderSectionActions.Activate"/>). Presentation, row building
    /// and keyboard routing moved to <see cref="RimWorldAccess.Shell.IdeoPreceptScreenScope"/>
    /// — this class now holds only what that scope cannot own itself:
    /// the liveness/target-Ideo the three hosts open and close, and the vanilla removal
    /// guard (Precept.DrawPreceptBox) mirror the old tree's Delete key rode.
    ///
    /// <see cref="IdeoPreceptSelectionHelper"/> (issue enumeration, value-picker options,
    /// SetPrecept/RemovePreceptsOfDef, section tokens, BuildTree) stays the presentation
    /// source — the scope reads it directly and hosts
    /// <see cref="IdeoPreceptSelectionHelper.BuildTree"/>'s section/issue/detail-line
    /// InspectionTreeItem shape as a live tree.
    /// </summary>
    public static class IdeoPreceptSelectionState
    {
        public static bool IsActive { get; private set; }

        /// <summary>The ideo currently being edited. Null while inactive.</summary>
        public static Ideo Ideo { get; private set; }

        public static void Open(Ideo targetIdeo)
        {
            if (targetIdeo == null) return;
            Ideo = targetIdeo;
            IsActive = true;
        }

        public static void Close()
        {
            IsActive = false;
            Ideo = null;
        }

        /// <summary>
        /// Housekeeping after a precept is set or removed for an issue: mirrors the old
        /// tree's OnPreceptChanged mutation half (row rebuild/cursor/announcement are now
        /// the scope's job).
        /// </summary>
        public static void OnPreceptChanged()
        {
            Ideo.RegenerateDescription();
        }

        /// <summary>
        /// Attempts to remove the issue's current precept(s), mirroring vanilla's removal
        /// guard (Precept.DrawPreceptBox): removable in the UI, the issue has no mandatory
        /// default precept, and no meme requires the precept. Speaks the "nothing to
        /// remove" / "blocked" outcomes directly (terminal, no further state to announce);
        /// a successful removal is silent here — the scope speaks the "issue: None,
        /// removed" confirmation once it has rebuilt its tree. Returns true only when a
        /// removal actually happened.
        /// </summary>
        public static bool TryRemovePrecept(IssueDef issue)
        {
            var current = IdeoPreceptSelectionHelper.CurrentPreceptsForIssue(Ideo, issue);
            if (current.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(IdeoPreceptSelectionHelper.BuildIssueLabel(issue, current), SpeechPriority.High);
                return false;
            }

            var removable = current.Where(CanRemovePrecept).ToList();
            if (removable.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(RemovalBlockedReason(current[0]), SpeechPriority.High);
                return false;
            }

            foreach (var precept in removable)
                Ideo.RemovePrecept(precept);
            Ideo.anyPreceptEdited = true;
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            return true;
        }

        /// <summary>Vanilla's removal guard (Precept.DrawPreceptBox): removable in the UI, the issue
        /// has no mandatory default precept, and no meme requires the precept.</summary>
        private static bool CanRemovePrecept(Precept precept)
        {
            return precept.def.canRemoveInUI
                && !precept.def.issue.HasDefaultPrecept
                && Ideo.GetMemeThatRequiresPrecept(precept.def) == null;
        }

        private static string RemovalBlockedReason(Precept precept)
        {
            var requiringMeme = Ideo.GetMemeThatRequiresPrecept(precept.def);
            if (requiringMeme != null)
                return "CannotRemove".Translate() + ": " + "RequiredByMeme".Translate(requiringMeme.label);
            return "CannotRemove".Translate() + ": " + IdeoBuilderHelper.PreceptLabel(precept);
        }
    }
}
