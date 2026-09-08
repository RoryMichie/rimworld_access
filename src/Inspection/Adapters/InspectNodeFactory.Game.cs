using System;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared constructors for inspection tree nodes (rework §B.3 item 3).
    /// Every node an adapter emits goes through one of these, replacing the
    /// per-site <c>new InspectionTreeItem { ... }</c> boilerplate. All methods
    /// parent-link the new node, set its indent to parent + 1, and return it.
    /// </summary>
    internal static class InspectNodeFactory
    {
        /// <summary>A read-only detail text line.</summary>
        public static InspectionTreeItem DetailLine(InspectionTreeItem parent, string label)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = label,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = false,
            };
            Attach(parent, item);
            return item;
        }

        /// <summary>
        /// A read-only detail line whose text is re-read on every announcement
        /// (<see cref="InspectionTreeItem.LabelProvider"/>) — for a line summarizing state a
        /// child action or an external dialog mutates in place without any category rebuild.
        /// </summary>
        public static InspectionTreeItem LiveDetailLine(InspectionTreeItem parent, Func<string> labelProvider)
        {
            InspectionTreeItem item = DetailLine(parent, labelProvider());
            item.LabelProvider = labelProvider;
            return item;
        }

        /// <summary>
        /// Strips rich-text tags from a multi-line blob and adds each non-empty
        /// line as a detail line. Lines that merely repeat
        /// <paramref name="redundantWithLabel"/> are dropped.
        /// </summary>
        public static void DetailLines(InspectionTreeItem parent, string blob, string redundantWithLabel = null)
        {
            if (string.IsNullOrWhiteSpace(blob))
                return;
            foreach (string line in InspectTextUtility.SplitLines(blob.StripTags(), redundantWithLabel))
            {
                DetailLine(parent, line);
            }
        }

        /// <summary>
        /// An expandable section/subcategory whose children build lazily on
        /// first expansion. The already-built guard lives here, not in the
        /// builder callback, and so does the try/catch around the build itself:
        /// a failure is contained and logged rather than propagating out of the
        /// OnActivate handler.
        /// </summary>
        public static InspectionTreeItem Section(InspectionTreeItem parent, string label, object data,
            Action<InspectionTreeItem> buildChildren, string expandedLabel = null)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = label,
                ExpandedLabel = expandedLabel,
                Data = data,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
            };
            item.OnActivate = () =>
            {
                if (item.Children.Count > 0)
                    return;
                try
                {
                    buildChildren(item);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"Inspection section '{label}' build failed: {ex.Message}");
                }
            };
            Attach(parent, item);
            return item;
        }

        /// <summary>
        /// A lazily-built section whose COLLAPSED summary label is re-read on every
        /// announcement (<see cref="InspectionTreeItem.LabelProvider"/>) — for a header that
        /// summarizes mutable child state (a shift's staff and hours) which child rows edit
        /// in place without rebuilding the section.
        /// </summary>
        public static InspectionTreeItem LiveSection(InspectionTreeItem parent, Func<string> labelProvider,
            object data, Action<InspectionTreeItem> buildChildren, string expandedLabel = null)
        {
            InspectionTreeItem item = Section(parent, labelProvider(), data, buildChildren, expandedLabel);
            item.LabelProvider = labelProvider;
            return item;
        }

        /// <summary>
        /// Clears and rebuilds one section's children in place after an adapter
        /// action mutates state, guarded like every other factory build. For
        /// SECTION nodes only — category-root rebuilds must ride
        /// <see cref="InspectionTreeBuilder.RebuildAdapterCategory"/>, which also
        /// re-runs the extender and parity passes.
        /// </summary>
        public static void RebuildChildren(InspectionTreeItem item, Action<InspectionTreeItem> buildChildren)
        {
            item.Children.Clear();
            try
            {
                buildChildren(item);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Inspection section '{item.Label}' rebuild failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs one eager build step, containing a failure to that step so sibling
        /// steps still build (log, never throw). Lazy Section builds are guarded by
        /// the factory itself; this is for adapters that build several independent
        /// row groups eagerly.
        /// </summary>
        public static void GuardedBuild(string context, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{context} build failed: {ex.Message}");
            }
        }

        /// <summary>An activatable action row (Enter runs <paramref name="onActivate"/>).</summary>
        public static InspectionTreeItem ActionRow(InspectionTreeItem parent, string label, object data,
            Action onActivate, bool opensOverlayMenu = false)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = label,
                Data = data,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = false,
                OnActivate = onActivate,
                OpensOverlayMenu = opensOverlayMenu,
            };
            Attach(parent, item);
            return item;
        }

        /// <summary>A leaf item row carrying a datum (gear item, skill, need, ...).</summary>
        public static InspectionTreeItem ItemRow(InspectionTreeItem parent, string label, object data)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = label,
                Data = data,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = false,
            };
            Attach(parent, item);
            return item;
        }

        /// <summary>Parent-links and appends an already-constructed node.</summary>
        public static void Attach(InspectionTreeItem parent, InspectionTreeItem child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }

        /// <summary>Parent-links and inserts a node at the FRONT of the parent's
        /// children, so it leads the list (used for lead action rows added after a
        /// category has built its own content).</summary>
        public static void AttachFirst(InspectionTreeItem parent, InspectionTreeItem child)
        {
            child.Parent = parent;
            parent.Children.Insert(0, child);
        }
    }
}
