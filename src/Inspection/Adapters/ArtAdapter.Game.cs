using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the Art category. Read-only presentation of a piece of
    /// art's generated title and image description.
    /// </summary>
    internal sealed class ArtAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Art";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(obj is Thing artThing))
                return;
            BuildArtChildren(categoryItem, artThing);
        }

        /// <summary>
        /// Builds children for the Art tab. Read-only: title and description.
        ///
        /// Stable-read rule: see
        /// <c>BookAdapter.BuildBookChildren</c>'s remarks -- same shape, same fix. Literature
        /// rewrites <c>artComp.Title</c>/<c>GenerateImageDescription()</c> asynchronously in
        /// place (the exact properties read below), so checking
        /// <see cref="RimTalkLiteratureCompat.IsArtGenerating"/> before the cache guard, keyed on
        /// the unwrapped <paramref name="thing"/> (the same inner Thing Literature's own CompArt
        /// scan keys against), is the whole fix.
        /// </summary>
        private static void BuildArtChildren(InspectionTreeItem parentItem, Thing thing)
        {
            // Handle minified (uninstalled) things
            Thing inner = thing is MinifiedThing mini ? mini.InnerThing : thing;

            bool generating = RimTalkLiteratureCompat.IsArtGenerating(inner);
            if (parentItem.Children.Count > 0)
            {
                if (!generating)
                    return; // already built with final content -- nothing left to do

                // Was showing the "still being written" marker; re-check every visit until settled.
                parentItem.Children.Clear();
            }

            int indent = parentItem.IndentLevel + 1;

            if (generating)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = AsyncTextStability.StillBeingWrittenMarker(),
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            var artComp = inner?.TryGetComp<CompArt>();

            if (artComp == null || !artComp.Active)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "(" + "NoneLower".Translate() + ")",
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            // Title
            string title = artComp.Title;
            if (!title.NullOrEmpty())
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = title,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }

            // Description
            string desc = artComp.GenerateImageDescription();
            if (!desc.NullOrEmpty())
            {
                desc = desc.StripTags().Trim();
                desc = System.Text.RegularExpressions.Regex.Replace(desc, @"\s+", " ");
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = desc,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }
        }
    }
}
