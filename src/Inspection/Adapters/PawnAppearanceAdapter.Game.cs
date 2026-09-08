using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the synthetic "Appearance" inspection category: hair
    /// (with color), beard, tattoos, and favorite color — visual-only in
    /// vanilla, so this is the only place a screen reader user can review
    /// what a pawn looks like.
    /// </summary>
    internal sealed class PawnAppearanceAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Appearance";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;
            BuildAppearanceChildren(categoryItem, pawn);
        }

        /// <summary>
        /// Builds the synthetic Appearance category: hair (with color), beard, tattoos, and favorite
        /// color. These are visual-only in vanilla (the styling station and portrait), so this is the
        /// only place a screen reader user can review what a pawn looks like. All labels come from the
        /// game's own translated data; color names from ColorNameHelper.
        /// </summary>
        private static void BuildAppearanceChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (pawn?.story == null)
                return;

            int indent = parentItem.IndentLevel + 1;

            void AddRow(string text)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = text,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }

            // Appends the style's visual description (when one exists) so the player can picture it.
            void AddStyleRow(string text, StyleItemDef styleDef)
            {
                string description = StyleDescriptionHelper.Describe(styleDef);
                AddRow(string.IsNullOrEmpty(description) ? text : text + ". " + description);
            }

            // Hair: style plus the hair color (Bald still has a color).
            if (pawn.story.hairDef != null)
            {
                string hairColor = ColorNameHelper.NameForColor(pawn.story.HairColor);
                AddStyleRow((string)"RimWorldAccess.Inspection.Appearance.HairRow".Translate(
                    "Hair".Translate(), pawn.story.hairDef.LabelCap, hairColor), pawn.story.hairDef);
            }

            // Beard: only when the pawn actually has one (skip the clean-shaven default).
            if (pawn.style?.beardDef != null && pawn.style.beardDef != BeardDefOf.NoBeard)
                AddStyleRow((string)"RimWorldAccess.Inspection.Appearance.Row".Translate(
                    "Beard".Translate(), pawn.style.beardDef.LabelCap), pawn.style.beardDef);

            // Tattoos and favorite color exist only with Ideology. Tattoos are shown even when
            // "None" so the user can confirm there is none.
            if (ModsConfig.IdeologyActive)
            {
                if (pawn.style?.FaceTattoo != null)
                    AddStyleRow((string)"RimWorldAccess.Inspection.Appearance.Row".Translate(
                        "TattooFace".Translate(), pawn.style.FaceTattoo.LabelCap), pawn.style.FaceTattoo);
                if (pawn.style?.BodyTattoo != null)
                    AddStyleRow((string)"RimWorldAccess.Inspection.Appearance.Row".Translate(
                        "TattooBody".Translate(), pawn.style.BodyTattoo.LabelCap), pawn.style.BodyTattoo);

                if (pawn.story.favoriteColor != null)
                    AddRow((string)"RimWorldAccess.Inspection.Appearance.Row".Translate(
                        "RimWorldAccess.Inspection.Appearance.FavoriteColorLabel".Translate(),
                        pawn.story.favoriteColor.LabelCap));
            }
        }
    }
}
