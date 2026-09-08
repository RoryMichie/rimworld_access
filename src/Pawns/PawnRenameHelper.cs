using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The one answer to "does vanilla offer this pawn a rename button?", shared by Alt+R on
    /// the map and the info card's Rename action row so the two can never disagree.
    ///
    /// Vanilla has no such method: it draws RenameUIUtility.DrawRenameButton(Rect, Pawn) from
    /// three surfaces, each with its own gate, and this mirrors all three.
    /// </summary>
    public static class PawnRenameHelper
    {
        public static bool CanRename(Pawn pawn)
        {
            if (pawn == null) return false;
            return IsRenameableColonist(pawn) || HasTrainingTab(pawn) || HasInspectPaneButton(pawn);
        }

        /// <summary>Decompiled RimWorld/CharacterCardUtility.cs:213.</summary>
        private static bool IsRenameableColonist(Pawn pawn) =>
            pawn.IsColonist || pawn.IsColonySubhuman;

        /// <summary>
        /// Decompiled RimWorld/ITab_Pawn_Training.cs:IsVisible. The training card draws the
        /// rename button unconditionally (TrainingCardUtility.DrawTrainingCard:29), so tab
        /// visibility is the whole gate. Mirrored rather than queried live: an ITab reads its
        /// pawn from Find.Selector, and a hotkey gate must not churn the selection.
        /// </summary>
        private static bool HasTrainingTab(Pawn pawn)
        {
            if (pawn.training == null || pawn.Faction != Faction.OfPlayer || pawn.RaceProps.hideTrainingTab)
                return false;
            return !pawn.IsMutant || pawn.mutant.Def.tameable;
        }

        /// <summary>
        /// Decompiled RimWorld/MainTabWindow_Inspect.cs:196 — the player animals the training
        /// tab skips, plus Biotech colony mechs.
        /// </summary>
        private static bool HasInspectPaneButton(Pawn pawn) =>
            (pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Animal && pawn.RaceProps.hideTrainingTab)
            || (ModsConfig.BiotechActive && pawn.IsColonyMech);
    }
}
