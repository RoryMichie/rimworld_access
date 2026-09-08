using System;
using System.Collections.Generic;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Where a captured tree row came from: the inspected object, its tab, and the row's band
    /// ordinal within that capture. The three together re-find the row's rect in a live geometry pass.
    /// </summary>
    public sealed class CapturedRowRef
    {
        public object Target;
        public InspectTabBase Tab;
        public int BandIndex;
    }

    /// <summary>One expandable node of the inspection tree.</summary>
    public class InspectionTreeItem
    {
        public enum ItemType
        {
            Object,           // A thing/pawn/building at the cursor
            Category,         // A category like "Health", "Gear", "Overview"
            SubCategory,      // A sub-category like "Equipment", "Apparel"
            Item,             // An item in a list (gear item, skill)
            Action,           // An actionable item (Drop, Consume, etc.)
            DetailText        // Read-only detail text
        }

        public ItemType Type { get; set; }

        private string label;

        /// <summary>
        /// Optional live source for <see cref="Label"/>, re-evaluated on EVERY read, so a summary
        /// composed from mutable state speaks what the game says now rather than at build time. The
        /// stored label remains the fallback when this is null, returns null, or throws.
        /// </summary>
        public Func<string> LabelProvider { get; set; }

        public string Label
        {
            get
            {
                if (LabelProvider != null)
                {
                    try
                    {
                        return LabelProvider() ?? label;
                    }
                    catch (Exception e)
                    {
                        Log.ErrorOnce("RimWorldAccess: tree label provider failed: " + e.Message,
                            GetHashCode() ^ 0x4C61626C);
                    }
                }
                return label;
            }
            set { label = value; }
        }
        /// <summary>
        /// The short form spoken while the node is expanded, and by every expand/collapse
        /// announcement, leaving Label as the full collapsed summary. Null uses Label throughout.
        /// </summary>
        public string ExpandedLabel { get; set; }
        public string Description { get; set; }
        /// <summary>Extras-channel text, spoken only while collapsed: expanded, the children carry it.</summary>
        public string Detail { get; set; }
        /// <summary>Label plus <see cref="Detail"/>, so typeahead matches everything the row speaks.</summary>
        public string SearchText
        {
            get
            {
                string text = Label ?? "";
                return string.IsNullOrEmpty(Detail) ? text : text + ". " + Detail;
            }
        }
        public string Tooltip { get; set; }
        public int IndentLevel { get; set; }
        public bool IsExpandable { get; set; }
        public bool IsExpanded { get; set; }
        /// <summary>Selection state, spoken in the state channel. Null where the row has none: false says "not selected".</summary>
        public bool? Selected { get; set; }
        /// <summary>Marks a node typeahead auto-expands, so its lazily-built children become matchable without drilling in.</summary>
        public bool AutoExpandForSearch { get; set; }
        /// <summary>Marks a node as a section heading within a parent's detail lines, for the Page Up/Down jump.</summary>
        public bool IsSectionHeader { get; set; }
        /// <summary>
        /// The owning header's cleaned text, null outside every section. Consumers that emit no
        /// header rows announce a section on crossing into it and aim Page Up/Down at its first row.
        /// </summary>
        public string SectionTitle { get; set; }
        public List<InspectionTreeItem> Children { get; set; }
        public InspectionTreeItem Parent { get; set; }
        public object Data { get; set; }

        /// <summary>
        /// The vanilla object whose on-screen box this row belongs to, for the focus ring. Separate
        /// from <see cref="Data"/>, which already carries the row's info-card subject and is often a
        /// different object. Null on every row with no box.
        /// </summary>
        public object RingTarget { get; set; }
        /// <summary>
        /// The vanilla inspect tab this node's category was built from, null for a synthetic
        /// category. Carried so the visual driver can open the REAL tab while the cursor is anywhere
        /// inside this category; nothing reads it for speech.
        /// </summary>
        public InspectTabBase SourceTab { get; set; }

        /// <summary>
        /// The captured visual row this node was built from, so the focus ring can find its real
        /// rect. Descendants leave it null and inherit it by walking up.
        /// </summary>
        public CapturedRowRef CapturedRow { get; set; }
        /// <summary>The Def Alt+I opens an info card for.</summary>
        public Def LinkedDef { get; set; }
        public Action OnActivate { get; set; }
        /// <summary>True when OnActivate hands off to an overlay menu, which owns the announcement, so the tree must not re-announce its row.</summary>
        public bool OpensOverlayMenu { get; set; }
        public Action OnDelete { get; set; }
        public Action OnInfo { get; set; }

        /// <summary>
        /// Optional live datum for a row that is a real CONTROL rather than a line of text. The tree
        /// calls it on every announcement instead of composing from <see cref="Label"/>, so the row
        /// speaks the standard role and state words and reports what the game says NOW.
        /// <see cref="Label"/> stays set on these rows as the typeahead search text and the fallback.
        /// </summary>
        public Func<ElementDescription> DescribeElement { get; set; }

        /// <summary>
        /// Optional Left/Right handler for a row whose value adjusts in place. True means the row
        /// consumed the chord and owns the spoken result; false falls through to expand/collapse.
        /// </summary>
        public Func<int, bool> OnAdjust { get; set; }

        public InspectionTreeItem()
        {
            Children = new List<InspectionTreeItem>();
            IsExpandable = false;
            IsExpanded = false;
            IndentLevel = 0;
        }

        /// <summary>This node and every descendant its expansion state leaves visible.</summary>
        public List<InspectionTreeItem> GetVisibleItems()
        {
            var result = new List<InspectionTreeItem>();
            result.Add(this);

            if (IsExpanded && Children.Count > 0)
            {
                foreach (var child in Children)
                {
                    result.AddRange(child.GetVisibleItems());
                }
            }

            return result;
        }
    }
}
