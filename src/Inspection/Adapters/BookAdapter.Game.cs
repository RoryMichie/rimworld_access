using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the Book category. Read-only presentation of a book's
    /// per-doer benefits, mental-break danger, and flavor description.
    /// </summary>
    internal sealed class BookAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Book";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(obj is Book book))
                return;
            BuildBookChildren(categoryItem, book);
        }

        /// <summary>
        /// Builds children for the Book tab. Read-only: title, benefits, dangers, description.
        ///
        /// Stable-read rule: the Literature extension
        /// (cj.rimtalk.literature) rewrites <c>book.FlavorUI</c> asynchronously after an LLM
        /// round-trip, in place, on the SAME vanilla property this method already reads below --
        /// no separate wiring needed for the real text once it lands. The only fix required is
        /// NOT permanently caching a child built while Literature's own PendingBookQueue still
        /// holds this book: the ordinary `Children.Count > 0` early-return below would otherwise
        /// freeze whatever text existed at first expansion (a placeholder, or the marker text)
        /// forever, since this method never runs again for that node. Checking
        /// <see cref="RimTalkLiteratureCompat.IsBookGenerating"/> BEFORE that guard, and clearing
        /// any prior marker-only children while still generating, makes every revisit re-check for
        /// free until it settles -- no <see cref="AsyncTextStability"/> pending-interest/fire-once
        /// needed for this lazily-rebuilt-per-visit shape (see RimTalkLiteratureCompat's own
        /// remarks for why that's the right call here vs. S16 Quests).
        /// </summary>
        private static void BuildBookChildren(InspectionTreeItem parentItem, Book book)
        {
            bool generating = RimTalkLiteratureCompat.IsBookGenerating(book);
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

            // Benefits
            if (book.BookComp?.Doers != null)
            {
                foreach (var doer in book.BookComp.Doers)
                {
                    string benefits = doer.GetBenefitsString();
                    if (!benefits.NullOrEmpty())
                    {
                        string cleaned = benefits.StripTags().Trim();
                        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");
                        if (!cleaned.NullOrEmpty())
                        {
                            InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.DetailText,
                                Label = cleaned,
                                IndentLevel = indent,
                                IsExpandable = false
                            });
                        }
                    }
                }
            }

            // Dangers
            if (book.MentalBreakChancePerHour > 0f)
            {
                string dangerLabel = $"{"Dangers".Translate()}: {"BookMentalBreak".Translate()}, {book.MentalBreakChancePerHour.ToStringPercent("0.0")} {"PerHour".Translate()}";
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = dangerLabel,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }

            // Description / flavor text
            string flavor = book.FlavorUI;
            if (!flavor.NullOrEmpty())
            {
                flavor = flavor.StripTags().Trim();
                flavor = System.Text.RegularExpressions.Regex.Replace(flavor, @"\s+", " ");
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = flavor,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }
        }
    }
}
