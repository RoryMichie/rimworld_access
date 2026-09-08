#if DEBUG
namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Dev-bridge screen dump trailer for the tree family. No override of
    /// <see cref="ScreenScope.DebugDescribeSurface"/> itself is
    /// needed — the tree's single content region already rides <see cref="ContentItemCount"/>/
    /// <see cref="ScreenScope.DescribeContentItem"/> through <see cref="PrefixRowCount"/>'s offset
    /// exactly like every other content region, so the generic base walk renders prefix rows and
    /// tree nodes correctly with no bespoke code here. This adds only the one extra trailing line
    /// the base's opt-in hook asks for: how many rows the tree currently has flattened, and
    /// whether submenu navigation is on.
    /// </summary>
    public abstract partial class TreeRegionScope
    {
        protected override string DebugDescribeSurfaceExtra()
        {
            return "tree: " + Tree.Count + " visible rows, submenu=" + Tree.SubmenuMode;
        }
    }
}
#endif
