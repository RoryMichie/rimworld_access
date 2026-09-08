using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for GizmoGridDrawer to add visual highlighting for selected gizmos
    /// during keyboard navigation. The highlight rect is never computed here — it comes
    /// from <see cref="GizmoRectRegistry"/>, which <see cref="GizmoRectCaptureFullPatch"/>
    /// and <see cref="GizmoRectCaptureShrunkPatch"/> fill from vanilla's own per-gizmo
    /// draw calls while this class's prefix/postfix bracket is open.
    /// </summary>
    [HarmonyPatch(typeof(GizmoGridDrawer))]
    [HarmonyPatch("DrawGizmoGrid")]
    public static class GizmoNavigationPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            try
            {
                GizmoRectRegistry.BeginFrame();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Gizmo focus ring draw error", ex);
            }
        }

        /// <summary>
        /// Postfix patch that draws a highlight box around the currently selected gizmo
        /// when gizmo navigation is active.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        public static void Postfix()
        {
            try
            {
                // The bar's only hover hit-test site: the registry is filled and
                // this bracket is still open, and it runs whether or not the
                // keyboard has a gizmo focused.
                HoverSpeech.EvaluateGizmoBar();

                Gizmo gizmo = ResolveFocusedGizmo();
                if (gizmo == null)
                {
                    return;
                }

                // A miss means vanilla drew a different group representative than the one
                // this menu chose (GizmoNavigationState's own GroupIntoRepresentatives versus
                // GizmoGridDrawer's re-pick at draw time). No rect means no ring: a wrong ring
                // is worse than none, so there is deliberately no fallback estimate here.
                if (GizmoRectRegistry.TryGet(gizmo, out Rect rect))
                {
                    FocusRing.Draw(GizmoRingRequest.RefineElement(rect).ExpandedBy(2f));
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Gizmo focus ring draw error", ex);
            }
            finally
            {
                GizmoRectRegistry.EndFrame();
            }
        }

        /// <summary>
        /// Which gizmo to ring, in priority order: the G-menu's own navigation state first; otherwise,
        /// while the windowless inspection tree drives, its focused row when that row is a
        /// Gizmos-category row — GizmosAdapter stores the live Gizmo in the row's Data
        /// (src/Inspection/Adapters/GizmosAdapter.Game.cs:93), so the lookup is exact; otherwise the
        /// architect tree's focused designator, which vanilla draws through this same grid (decompiled
        /// RimWorld/ArchitectCategoryTab.cs:49). Nothing driving leaves nothing to ring.
        ///
        /// The three branches are mutually exclusive in practice: each of these screens is a
        /// windowless surface with its own live scope, and every MapScope opener that could
        /// start a second one either carries a <c>!ShellGuards.MenuOwnsInput()</c> term or sits
        /// below the driving scope in the dispatcher, where an unclaimed key is swallowed while
        /// that predicate holds. The order is nonetheless the ladder: first live driver wins.
        /// </summary>
        private static Gizmo ResolveFocusedGizmo()
        {
            if (GizmoNavigationState.IsActive &&
                GizmoNavigationState.AvailableGizmos.Count > 0 &&
                GizmoNavigationState.SelectedGizmoIndex >= 0 &&
                GizmoNavigationState.SelectedGizmoIndex < GizmoNavigationState.AvailableGizmos.Count)
            {
                return GizmoNavigationState.AvailableGizmos[GizmoNavigationState.SelectedGizmoIndex];
            }

            if (WindowlessInspectionState.IsActive)
            {
                InspectionScope scope = InspectionScope.Live;
                InspectionTreeItem row = scope != null ? scope.FocusedRow() : null;
                return row != null ? row.Data as Gizmo : null;
            }

            if (ArchitectTreeState.IsActive)
            {
                return ResolveArchitectGizmo(ArchitectTreeState.GetSelectedDesignator());
            }

            // The map-controls drill-in scopes (door/forbid/temp/ refuelable/plant-selection) have
            // their own live ScreenScope but none of the three states above -- their registered
            // provider supplies the gizmo backing their focused row, or null when that row has no
            // backing gizmo.
            if (GizmoRingRequest.Provider != null)
            {
                return GizmoRingRequest.Provider();
            }

            return null;
        }

        /// <summary>
        /// Caches the walk-up below per focused designator rather than per frame:
        /// <c>ResolvedAllowedDesignators</c> is a yield iterator that asks
        /// <c>Current.Game.Rules</c> about every designator it passes (decompiled
        /// Verse/DesignationCategoryDef.cs:57-75).
        /// </summary>
        private static Designator cachedFocusedDesignator;
        private static Gizmo cachedArchitectGizmo;

        private static Gizmo ResolveArchitectGizmo(Designator focused)
        {
            if (focused == null)
            {
                cachedFocusedDesignator = null;
                cachedArchitectGizmo = null;
                return null;
            }
            if (!ReferenceEquals(focused, cachedFocusedDesignator))
            {
                cachedFocusedDesignator = focused;
                cachedArchitectGizmo = ResolveDrawnGizmo(focused);
            }
            return cachedArchitectGizmo;
        }

        /// <summary>
        /// The tree flattens each <see cref="Designator_Dropdown"/> into its elements
        /// (src/Building/ArchitectHelper.cs:55-78), but the grid draws the DROPDOWN, so a
        /// focused wall, floor or piece of furniture matches no drawn gizmo on its own. Ring
        /// the dropdown that owns it instead; a designator found in neither place rings
        /// nothing.
        /// </summary>
        private static Gizmo ResolveDrawnGizmo(Designator focused)
        {
            DesignationCategoryDef category = ArchitectTreeState.GetSelectedCategory();
            if (category == null)
            {
                return null;
            }
            Designator_Dropdown owner = null;
            foreach (Designator drawn in category.ResolvedAllowedDesignators)
            {
                if (ReferenceEquals(drawn, focused))
                {
                    return drawn;
                }
                if (owner == null && drawn is Designator_Dropdown dropdown && dropdown.Elements != null)
                {
                    for (int i = 0; i < dropdown.Elements.Count; i++)
                    {
                        if (ReferenceEquals(dropdown.Elements[i], focused))
                        {
                            owner = dropdown;
                            break;
                        }
                    }
                }
            }
            return owner;
        }
    }

    /// <summary>
    /// The five map-controls drill-in scopes each register a provider in OnPush (returning the gizmo
    /// backing its focused row, or null) and clears it in OnPop. Only one of the mutually-exclusive
    /// drill-ins is ever live at a time (see InspectComponentScopeMirror's own proof), so a single
    /// static slot is enough.
    /// </summary>
    internal static class GizmoRingRequest
    {
        internal static Func<Gizmo> Provider;

        /// <summary>
        /// Narrows the ring onto one element inside the provided gizmo, for scopes whose
        /// cursor moves within a single gizmo rather than along the bar (the mech
        /// control-group drill-in). Returning nothing keeps the whole-gizmo ring, so a
        /// frame vanilla did not draw the element on is never a dead ring.
        /// </summary>
        internal static Func<Rect?> ElementRectProvider;

        internal static Rect RefineElement(Rect gizmoRect)
        {
            Rect? element = ElementRectProvider != null ? ElementRectProvider() : null;
            return element ?? gizmoRect;
        }
    }
}
