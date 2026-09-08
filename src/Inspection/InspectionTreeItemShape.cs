using System.Collections.Generic;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Maps <see cref="InspectionTreeItem"/>'s tree topology onto the shape
    /// <see cref="TreeModel{T}"/> needs. Consumed by <see cref="TreeNavigationHelper"/>,
    /// which drives all tree navigation through the shared model.
    /// </summary>
    internal sealed class InspectionTreeItemShape : ITreeShape<InspectionTreeItem>
    {
        public IReadOnlyList<InspectionTreeItem> ChildrenOf(InspectionTreeItem node)
        {
            return node.Children;
        }

        public InspectionTreeItem ParentOf(InspectionTreeItem node)
        {
            return node.Parent;
        }

        public bool IsExpandable(InspectionTreeItem node)
        {
            return node.IsExpandable;
        }

        public bool IsExpanded(InspectionTreeItem node)
        {
            return node.IsExpanded;
        }

        public void SetExpanded(InspectionTreeItem node, bool value)
        {
            node.IsExpanded = value;
        }

        public int IndentLevelOf(InspectionTreeItem node)
        {
            return node.IndentLevel;
        }
    }
}
