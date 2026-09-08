using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Base class for inspection tree adapters. An adapter is registered against the game tab
    /// <see cref="System.Type"/> it consumes and owns how that tab appears in the inspection tree:
    /// dispatch identity, handler behavior, display name, children and activation.
    /// </summary>
    public abstract class InspectNodeAdapter
    {
        /// <summary>
        /// Stable English identifier for dispatch and de-duplication. Never displayed raw;
        /// rendering goes through <see cref="InspectionCategoryLocalizer"/>. (l10n-exempt:
        /// dispatch token, localized for display via InspectionCategoryLocalizer.)
        /// </summary>
        public abstract string CategoryKey { get; }

        /// <summary>How the category behaves in the tree (rich/action/basic).</summary>
        public abstract TabHandlerType Handler { get; }

        /// <summary>Whether the adapter resolved everything it reflects on. Unready adapters must not be registered.</summary>
        public virtual bool Ready
        {
            get { return true; }
        }

        /// <summary>Translated display name for the category node.</summary>
        public virtual string DisplayName(InspectTabBase tab)
        {
            return InspectionCategoryLocalizer.Localize(CategoryKey);
        }

        /// <summary>
        /// Short display name resolved from the live object. Adapters whose name is dynamic game
        /// data override this, so <see cref="CategoryKey"/> stays stable and language-independent.
        /// </summary>
        public virtual string CategoryDisplayName(object obj)
        {
            return InspectionCategoryLocalizer.Localize(CategoryKey);
        }

        /// <summary>Full navigation label for the collapsed category row; adapters decorating it with live values override.</summary>
        public virtual string CategoryLabel(object obj, string displayName)
        {
            return displayName;
        }

        /// <summary>True when the category renders as one inline "Name: content" line instead of an expandable node.</summary>
        public virtual bool IsInline => false;

        /// <summary>Inline content: the category's info text flattened to one line, with the redundant leading object name stripped.</summary>
        public virtual string InlineContent(object obj)
        {
            string content = InspectionInfoHelper.GetCategoryInfo(obj, CategoryKey);

            if (string.IsNullOrEmpty(content))
                return null;

            content = content.StripTags();
            content = content.Replace("\n", " ").Replace("\r", "").Trim();

            // The object label already carries the name.
            if (obj is Pawn pawn)
            {
                string pawnName = pawn.LabelCap.StripTags();
                if (content.StartsWith(pawnName))
                {
                    content = content.Substring(pawnName.Length).Trim();
                }

                // "age 33 (63)" -> "age 33, chronological age: 63"
                var agePattern = new System.Text.RegularExpressions.Regex(@"age (\d+) \((\d+)\)");
                content = agePattern.Replace(content, "age $1, chronological age: $2");
            }
            else if (obj is Building building)
            {
                string buildingName = building.LabelCap.StripTags();
                if (content.StartsWith(buildingName))
                {
                    content = content.Substring(buildingName.Length).Trim();
                }
            }

            return content;
        }

        /// <summary>Whether a RichNavigation category can expand into children for this object; adapters depending on components or DLC state override.</summary>
        public virtual bool CanExpand(object obj)
        {
            return true;
        }

        /// <summary>
        /// Replacement label when a RichNavigation category cannot expand. Null routes the node to
        /// the detailed-info fallback; non-null renders a non-expandable row with this label.
        /// </summary>
        public virtual string NoExpandLabel(object obj)
        {
            return null;
        }

        /// <summary>Executes an Action-handler category's activation, opening the overlay state or dialog that owns the interaction.</summary>
        public virtual void ExecuteAction(object obj)
        {
        }

        /// <summary>
        /// Populates an expanded category node's children. The already-built guard lives at the
        /// call site. Adapters marking only identity and behavior add nothing, leaving the node to
        /// the orchestrator's fallback presentation.
        /// </summary>
        public virtual void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
        }
    }

    /// <summary>
    /// Adapter for a tab whose dispatch key and handler behavior are fixed per registered type.
    /// Content still builds through InspectionTreeBuilder's category-key dispatch, so this adapter
    /// owns identity and behavior only.
    /// </summary>
    public sealed class StaticTabAdapter : InspectNodeAdapter
    {
        private readonly string categoryKey;
        private readonly TabHandlerType handler;

        public StaticTabAdapter(string categoryKey, TabHandlerType handler)
        {
            this.categoryKey = categoryKey;
            this.handler = handler;
        }

        public override string CategoryKey => categoryKey;

        public override TabHandlerType Handler => handler;
    }
}
