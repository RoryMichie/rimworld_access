using System.Collections.Generic;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The inspect tree's "Gizmos" category: the same commands the G menu shows
    /// for this object, as child rows of the tree. Nothing is re-derived — the
    /// rows come from <see cref="GizmoNavigationState.CollectGizmosFor"/> (the
    /// menu's own collection pipeline, including the visibility filter, reverse
    /// designators, vanilla's Order sort and GroupsWith/MergeWith grouping) and
    /// each row's spoken form from <see cref="GizmoNavigationState.DescribeGizmoRow"/>,
    /// so a command reads identically in both places.
    ///
    /// Rows describe LIVE (<see cref="InspectionTreeItem.DescribeElement"/>):
    /// gizmo state moves on its own — cooldowns tick, fuel drains, another pawn
    /// forbids the door — so the row reports what the game says at the moment
    /// the user arrives on it, not what it said when the node was first built.
    ///
    /// Interaction mirrors the menu: Enter runs the gizmo through the shared
    /// execution path, and Left/Right adjust a gizmo that carries a slider
    /// (falling through to the tree's expand/collapse when it does not). Every
    /// gizmo announces its own outcome — a toggle its new state, a targeter its
    /// prompt, a float menu itself — so the rows are marked
    /// <see cref="InspectionTreeItem.OpensOverlayMenu"/> and the tree does not
    /// speak over them.
    /// </summary>
    internal sealed class GizmosAdapter : InspectNodeAdapter
    {
        /// <summary>Stable dispatch key; the display name comes from InspectionCategoryLocalizer.</summary>
        internal const string Key = "Gizmos";

        public override string CategoryKey => Key;

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        /// <summary>
        /// Always expandable for a selectable. The alternative — collecting the
        /// gizmos here to decide — would run the whole collection (including a
        /// CreateReverseDesignationGizmo sweep) eagerly for every object in the
        /// tree just to answer a yes/no; an object that genuinely has no
        /// commands says so on expansion instead.
        /// </summary>
        public override bool CanExpand(object obj)
        {
            return obj is ISelectable;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            BuildGizmoRows(categoryItem, obj, mode);
        }

        private static void BuildGizmoRows(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(obj is ISelectable selectable))
            {
                return;
            }

            var owners = new Dictionary<Gizmo, ISelectable>();
            var groups = new Dictionary<Gizmo, List<Gizmo>>();
            List<Gizmo> gizmos = GizmoNavigationState.CollectGizmosFor(selectable, owners, groups);

            if (gizmos.Count == 0)
            {
                InspectNodeFactory.DetailLine(categoryItem,
                    "RimWorldAccess.Inspection.Tree.NoCommands".Translate());
                return;
            }

            for (int i = 0; i < gizmos.Count; i++)
            {
                Gizmo gizmo = gizmos[i];
                owners.TryGetValue(gizmo, out ISelectable owner);
                groups.TryGetValue(gizmo, out List<Gizmo> group);
                AddGizmoRow(categoryItem, gizmo, owner ?? selectable, group, obj, mode);
            }
        }

        private static void AddGizmoRow(InspectionTreeItem categoryItem, Gizmo gizmo, ISelectable owner,
            List<Gizmo> group, object inspected, InspectionMode mode)
        {
            ElementDescription initial = GizmoNavigationState.DescribeGizmoRow(gizmo, owner);

            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                // The label is the typeahead search text and the fallback; what the
                // row actually speaks comes from DescribeElement below.
                Label = initial.Label ?? "",
                Data = gizmo,
                IndentLevel = categoryItem.IndentLevel + 1,
                IsExpandable = false,
                DescribeElement = () => GizmoNavigationState.DescribeGizmoRow(gizmo, owner),
            };

            // Read-only inspection presents the commands but never fires them.
            if (mode == InspectionMode.Full)
            {
                item.OnActivate = () =>
                {
                    Designator designatorBefore = Find.DesignatorManager?.SelectedDesignator;
                    bool targetingBefore = Find.Targeter != null && Find.Targeter.IsTargeting;

                    GizmoNavigationState.ActivateGizmoFromTree(gizmo, owner, group);

                    if (HandedOffToMap(designatorBefore, targetingBefore))
                    {
                        // The command put the player into map targeting or blueprint
                        // placement, and those flows need the map keyboard. The tree
                        // has to get out of the way for them: TargetingScopeMirror and
                        // PlacementScopeMirror reconcile BEFORE InspectionScopeMirror,
                        // so leaving the tree open would re-float it on top every pass
                        // and swallow the very keys the targeter is waiting for. This
                        // is the same yield the G menu makes by closing on execute.
                        // Closing restores the previous selection with
                        // forceDesignatorDeselect false, so the armed designator and
                        // the in-flight targeter both survive it.
                        WindowlessInspectionState.Close();
                        return;
                    }

                    RebuildAfterGizmoAction(categoryItem, inspected, mode);
                };
                // The gizmo's own handler owns the announcement (state word,
                // targeting prompt, opened menu) — the tree must not speak on top.
                item.OpensOverlayMenu = true;

                item.OnAdjust = direction =>
                    GizmoNavigationState.AdjustGizmoSliderFromTree(gizmo, owner, direction, bigStep: false);
            }

            InspectNodeFactory.Attach(categoryItem, item);
        }

        /// <summary>
        /// Whether the command just handed control to a map-level flow — an armed
        /// designator (blueprint placement, Cancel, Deconstruct) or an open
        /// targeter (Attack, an ability, a Command_Target). Compared against the
        /// state BEFORE activation so a designator the player already had armed
        /// does not read as a hand-off.
        ///
        /// Windowless float menus (a command's right-bracket options, material
        /// pickers) and real dialogs are deliberately NOT hand-offs: the
        /// dispatcher's blanket overlay stand-down covers the former and
        /// InspectionScopeMirror's own foreign-window term covers the latter, so
        /// the tree is still there when the user comes back.
        /// </summary>
        private static bool HandedOffToMap(Designator designatorBefore, bool targetingBefore)
        {
            Designator designatorNow = Find.DesignatorManager?.SelectedDesignator;
            if (designatorNow != null && designatorNow != designatorBefore)
            {
                return true;
            }
            return Find.Targeter != null && Find.Targeter.IsTargeting && !targetingBefore;
        }

        /// <summary>
        /// A command can change which commands exist (drafting swaps a pawn's
        /// whole set, cancelling removes its own row), so the category's
        /// lazily-cached children are rebuilt after an activation. Silent: the
        /// gizmo already spoke, and one announcement per action is the rule.
        /// </summary>
        private static void RebuildAfterGizmoAction(InspectionTreeItem categoryItem, object inspected, InspectionMode mode)
        {
            if (!WindowlessInspectionState.IsActive)
            {
                return;
            }
            categoryItem.Children.Clear();
            BuildGizmoRows(categoryItem, inspected, mode);
            WindowlessInspectionState.RefreshVisibleList();
        }
    }
}
