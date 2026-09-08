using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keeps the real architect panel showing what the architect tree is doing: the
    /// focused row's category is the panel vanilla opens, and that category's button
    /// carries the shared focus ring.
    ///
    /// Opening the panel is not decoration. <c>ExtraOnGUI</c> draws the designator
    /// grid through <c>OpenTab()</c>, which is <c>selectedDesPanel</c> in the ordinary
    /// non-search case (decompiled RimWorld/MainTabWindow_Architect.cs:73-98), so
    /// without this write there is no grid for the designator ring in
    /// <see cref="GizmoNavigationPatch"/> to land on.
    ///
    /// The button rect is a local inside the private <c>DoCategoryButton</c>, so it is
    /// read in the two halves <c>ThingFilterTreeSync</c> established: the
    /// <c>DoCategoryButton</c> bracket knows WHICH category is being drawn, and the
    /// <c>Widgets.ButtonTextSubtle</c> call vanilla makes inside it (:118) hands over
    /// the rect it actually draws with. Identity is by <see cref="DesignationCategoryDef"/>
    /// reference; a category we cannot identify draws nothing.
    /// </summary>
    internal static class ArchitectCategoryRingPatch
    {
        private static readonly AccessTools.FieldRef<MainTabWindow_Architect, List<ArchitectCategoryTab>> desPanelsCached =
            AccessTools.FieldRefAccess<MainTabWindow_Architect, List<ArchitectCategoryTab>>("desPanelsCached");

        // Edge-triggered, the same discipline ThingFilterTreeSync uses for open bits: writing
        // only on change leaves vanilla's own selection standing between our moves, instead of
        // pinning the panel to our last cursor position every frame.
        private static DesignationCategoryDef lastDriven;

        private static ArchitectCategoryTab drawingPanel;
        private static bool haveRect;
        private static Rect buttonRect;

        /// <summary>
        /// <c>selectedDesPanel</c> is public view state (decompiled
        /// RimWorld/MainTabWindow_Architect.cs:14) and is exactly what a mouse click
        /// would set. <c>ClickedCategory</c> (:224-244) is deliberately not the vehicle:
        /// it toggles the panel closed when it is already the open one, sets
        /// <c>userForcedSelectionDuringSearch</c>, and plays a category-select sound.
        /// </summary>
        private static void DriveOpenCategory(MainTabWindow_Architect window)
        {
            if (!ArchitectTreeState.IsActive)
            {
                lastDriven = null;
                return;
            }
            DesignationCategoryDef focused = ArchitectTreeState.GetSelectedCategory();
            if (focused == null || focused == lastDriven)
            {
                return;
            }
            List<ArchitectCategoryTab> panels = desPanelsCached(window);
            if (panels == null)
            {
                return;
            }
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i].def == focused)
                {
                    window.selectedDesPanel = panels[i];
                    lastDriven = focused;
                    return;
                }
            }
        }

        private static bool Drawing
        {
            get { return Event.current != null && Event.current.type == EventType.Repaint; }
        }

        private static void BeginCategoryButton(ArchitectCategoryTab panel)
        {
            if (!Drawing || !ArchitectTreeState.IsActive)
            {
                return;
            }
            drawingPanel = panel;
            haveRect = false;
        }

        private static void NoteButtonRect(Rect rect)
        {
            if (drawingPanel == null)
            {
                return;
            }
            buttonRect = rect;
            haveRect = true;
        }

        private static void EndCategoryButton()
        {
            ArchitectCategoryTab panel = drawingPanel;
            drawingPanel = null;
            if (panel == null || !haveRect)
            {
                return;
            }
            haveRect = false;
            // Only while the cursor is ON a category row. With the cursor down among the
            // designators the grid's own ring is the focus, and a second ring up here would
            // compete with it rather than locate anything.
            if (ArchitectTreeState.GetSelectedDesignator() == null
                && panel.def == ArchitectTreeState.GetSelectedCategory())
            {
                FocusRing.Draw(buttonRect);
            }
        }

        /// <summary>
        /// The write rides <c>ExtraOnGUI</c> rather than <c>DoWindowContents</c> because
        /// the window stack runs every window's <c>ExtraOnGUI</c> first (decompiled
        /// Verse/WindowStack.cs:211 then :228), and that is the pass that draws the grid.
        /// </summary>
        [HarmonyPatch(typeof(MainTabWindow_Architect), "ExtraOnGUI")]
        internal static class OpenCategoryDriverPatch
        {
            [HarmonyPrefix]
            internal static void Prefix(MainTabWindow_Architect __instance)
            {
                try
                {
                    DriveOpenCategory(__instance);
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Architect category drive error", ex);
                }
            }
        }

        [HarmonyPatch(typeof(MainTabWindow_Architect), "DoCategoryButton")]
        internal static class CategoryButtonPatch
        {
            [HarmonyPrefix]
            internal static void Prefix(ArchitectCategoryTab __0)
            {
                try
                {
                    BeginCategoryButton(__0);
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Architect category ring draw error", ex);
                }
            }

            [HarmonyPostfix]
            internal static void Postfix()
            {
                try
                {
                    EndCategoryButton();
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Architect category ring draw error", ex);
                }
            }
        }

        /// <summary>
        /// Vanilla's own rect, tapped only while a category button's bracket is open;
        /// every other <c>ButtonTextSubtle</c> in the game leaves on the null check.
        /// </summary>
        [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextSubtle))]
        internal static class CategoryButtonRectPatch
        {
            [HarmonyPrefix]
            internal static void Prefix(Rect __0)
            {
                try
                {
                    NoteButtonRect(__0);
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Architect category ring draw error", ex);
                }
            }
        }
    }
}
