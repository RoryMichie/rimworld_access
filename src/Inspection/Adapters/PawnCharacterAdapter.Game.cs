using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for a pawn's "Character" inspection category: the shared
    /// detailed-info dump, the incapable-of work tags the character card
    /// shows, plus a favorite-color
    /// line (visual-only in vanilla, added here for accessibility).
    /// </summary>
    internal sealed class PawnCharacterAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Character";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;

            InspectionTreeBuilder.BuildDetailedInfoChildren(categoryItem, obj, "Character");

            // Incapable-of work tags — the character card's own list, wording
            // and order replicated from CharacterCardUtility.DoLeftSection
            // (header, tag labels, "None", royal-title-caused tags sorted
            // last).
            InspectNodeFactory.DetailLine(categoryItem,
                $"{"IncapableOf".Translate(pawn)}: {GetIncapableOfList(pawn)}");

            // Favorite color — visual-only in vanilla, add as text for accessibility
            if (pawn != null && ModsConfig.IdeologyActive
                && !pawn.DevelopmentalStage.Baby()
                && pawn.story?.favoriteColor != null)
            {
                string orIdeoColor = string.Empty;
                if (pawn.Ideo != null && !pawn.Ideo.classicMode)
                {
                    orIdeoColor = "OrIdeoColor".Translate(pawn.Named("PAWN"));
                }
                string colorLabel = "FavoriteColorTooltip".Translate(
                    pawn.Named("PAWN"),
                    pawn.story.favoriteColor.label.Named("COLOR"),
                    0.6f.ToStringPercent().Named("PERCENTAGE"),
                    orIdeoColor.Named("ORIDEO")
                ).Resolve();
                InspectNodeFactory.Attach(categoryItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = colorLabel,
                    IndentLevel = categoryItem.IndentLevel + 1,
                    IsExpandable = false
                });
            }
        }

        /// <summary>
        /// The character card's disabled work tag list as one comma-joined
        /// string: WorkTagsFrom(CombinedDisabledWorkTags) with vanilla's sort
        /// (royal-title-caused tags last) and per-tag wording
        /// (LabelTranslated().CapitalizeFirst()), or "None".
        /// </summary>
        private static string GetIncapableOfList(Pawn pawn)
        {
            WorkTags disabledTags = pawn.CombinedDisabledWorkTags;
            if (disabledTags == WorkTags.None)
                return "None".Translate();

            List<WorkTags> tags = disabledTags.GetAllSelectedItems<WorkTags>()
                .Where(t => t != WorkTags.None)
                .ToList();
            tags.Sort((a, b) => CausedByRoyalTitle(pawn, a).CompareTo(CausedByRoyalTitle(pawn, b)));
            return string.Join(", ", tags.Select(t => t.LabelTranslated().CapitalizeFirst()));
        }

        private static int CausedByRoyalTitle(Pawn pawn, WorkTags tag)
        {
            // Reflection lookup shared with InfoCardDataExtractor.GetCauseString via
            // VanillaAccess's cache, instead of each site resolving its own MethodInfo.
            var method = VanillaAccess.GetMethod(typeof(CharacterCardUtility), "GetWorkTypeDisableCauses");
            var causes = (IEnumerable<object>)method.Invoke(null, new object[] { pawn, tag });
            return causes.Any(c => c is RoyalTitleDef) ? 1 : -1;
        }
    }
}
