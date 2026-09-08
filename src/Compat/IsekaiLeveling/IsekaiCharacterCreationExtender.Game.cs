using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The mod's character-creation panel (Patch_CharacterCreation draws level, rank, the six
    /// stats and a Reroll button onto Page_ConfigureStartingPawns) as a category under each
    /// starting pawn in the accessible tree. Values are live so a reroll, or the page's own
    /// randomize, reads back at once; Reroll rides PawnStatGenerator.InitializePawnStats, the
    /// button's own call. The captured-extras diff then folds the drawn button away, since the
    /// row here carries the same label.
    /// </summary>
    internal static class IsekaiCharacterCreationExtender
    {
        private const string Key = "IsekaiStats";

        public static void Register()
        {
            StartingPawnHelper.RegisterCategoryExtender(Build);
        }

        private static void Build(Pawn pawn, int pawnIndex, InspectionTreeItem pawnNode)
        {
            object comp = IsekaiCompat.ComponentOf(pawn);
            if (comp == null)
                return;

            InspectionTreeItem category = StartingPawnHelper.AddExtensionCategory(
                pawnNode, Key, CompatText.ModText("Isekai_CharCreate_Title"), pawnIndex);

            StartingPawnHelper.AddExtensionLeaf(category, LevelLine(comp), pawnIndex, labelProvider: () => LevelLine(comp));
            object stats = IsekaiCompat.StatsOf(comp);
            foreach (int ordinal in IsekaiCompat.StatDisplayOrder)
            {
                int captured = ordinal;
                StartingPawnHelper.AddExtensionLeaf(category, StatLine(stats, captured), pawnIndex,
                    labelProvider: () => StatLine(stats, captured));
            }
            StartingPawnHelper.AddExtensionLeaf(category, CompatText.ModText("Isekai_CharCreate_Reroll"), pawnIndex,
                activate: delegate
                {
                    IsekaiCompat.RerollStartingStats(pawn, comp);
                    TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Isekai.Rerolled".Translate(LevelLine(comp)));
                });
        }

        private static string LevelLine(object comp)
        {
            return IsekaiCompat.LevelRankLine(IsekaiCompat.Level(comp));
        }

        private static string StatLine(object stats, int ordinal)
        {
            return IsekaiCompat.StatAbbreviation(ordinal) + " " + IsekaiCompat.StatValue(stats, ordinal);
        }
    }
}
