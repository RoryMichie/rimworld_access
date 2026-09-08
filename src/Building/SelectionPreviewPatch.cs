using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Renders a visual preview of keyboard-based selection with RimWorld's own highlighting
    /// materials, so sighted observers can see what the keyboard is selecting.
    /// </summary>
    [HarmonyPatch(typeof(MapInterface))]
    [HarmonyPatch("MapInterfaceUpdate")]
    public static class SelectionPreviewPatch
    {
        // The same pooled bracket material vanilla's GenUI.RenderMouseoverBracket draws under
        // the mouse cell, so the keyboard cursor reads identically to a mouse hover.
        private static readonly Material KeyboardCursorMaterial =
            MaterialPool.MatFrom("UI/Overlays/MouseoverBracketTex", ShaderDatabase.MetaOverlay);

        [HarmonyPostfix]
        public static void Postfix()
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return;

            // Cursor visibility parity for sighted viewers; placement modes draw their own.
            if (WorldRendererUtility.DrawingMap && !ArchitectState.IsInPlacementMode && !ShapePlacementState.IsActive
                && MapNavigationState.CurrentCursorPosition.InBounds(map))
            {
                Vector3 cursorPos = MapNavigationState.CurrentCursorPosition
                    .ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
                Graphics.DrawMesh(MeshPool.plane10, cursorPos, Quaternion.identity, KeyboardCursorMaterial, 0);
            }

            // Scanner emphasis via vanilla's quest/tutor "look here" pointer. Highlight queues
            // into static lists that TargetHighlighterUpdate drains later in the same frame
            // (decompiled Verse/Root.cs:138), so the queue never goes stale. ViewingModeState
            // owns the emphasis while it borrows the scanner's temporary category, keeping one
            // indicator per thing.
            if (WorldRendererUtility.DrawingMap && ScannerState.NavigationSessionActive && !ViewingModeState.IsActive)
            {
                ScannerItem item = ScannerState.CurrentFocusedItem;
                if (item != null)
                {
                    Thing thing = item.Thing;
                    if (thing != null && thing.Spawned && thing.Map == map && !HiddenPawns.IsHidden(thing))
                    {
                        TargetHighlighter.Highlight(new GlobalTargetInfo(thing), arrow: true, colonistBar: false, circleOverlay: true);
                    }
                    else if (item.Position.IsValid && item.Position.InBounds(map))
                    {
                        TargetHighlighter.Highlight(new GlobalTargetInfo(item.Position, map), arrow: true, colonistBar: false, circleOverlay: false);
                    }
                }
            }

            // Range ring, target highlight and AOE field at the keyboard cursor while aiming.
            // MapInterfaceUpdate opens with targeter.TargeterUpdate() (decompiled
            // RimWorld/MapInterface.cs:118), so this lands in the same Update phase as vanilla's.
            KeyboardTargetingHighlight.Draw(map);

            // MarkForDraw is good for one frame only and the other callers sit in per-keypress
            // handlers, so without this per-frame request the browsed area flashes once and
            // vanishes. MapInterfaceUpdate runs before AreaManagerUpdate consumes the flag.
            Area browsed = BrowsedAreaForDisplay();
            if (browsed != null && browsed.Map == map) browsed.MarkForDraw();

            if (ArchitectState.IsInPlacementMode)
            {
                if (ArchitectState.SelectedCells.Count > 0)
                {
                    RenderCells(ArchitectState.SelectedCells, DesignatorUtility.DragHighlightCellMat);
                }
            }

            // The building itself under the keyboard cursor, for the sighted viewer.
            DrawKeyboardPlacementGhost(map);

            if (ShapePlacementState.IsActive && ShapePlacementState.PreviewCells != null && ShapePlacementState.PreviewCells.Count > 0)
            {
                RenderCells(ShapePlacementState.PreviewCells, DesignatorUtility.DragHighlightCellMat);

                if (ShapePlacementState.FirstPoint.HasValue)
                {
                    IntVec3 firstPoint = ShapePlacementState.FirstPoint.Value;
                    IntVec3 secondPoint = ShapePlacementState.SecondPoint ?? MapNavigationState.CurrentCursorPosition;
                    RenderRectangleOutline(firstPoint, secondPoint);
                }
            }

            if (ViewingModeState.IsActive)
            {
                // Captured once: the list can change mid-render.
                var obstacleCells = ViewingModeState.ObstacleCells;

                if (obstacleCells != null && obstacleCells.Count > 0)
                {
                    GenDraw.DrawFieldEdges(obstacleCells, Color.red);

                    // The current obstacle is tracked by ScannerState's temporary category.
                    if (ScannerState.IsInTemporaryCategory())
                    {
                        IntVec3 currentObstacle = ScannerState.GetCurrentItemPosition();
                        if (currentObstacle.IsValid && obstacleCells.Contains(currentObstacle))
                        {
                            GenDraw.DrawFieldEdges(new List<IntVec3> { currentObstacle }, Color.yellow);
                        }
                    }
                }

                if (ViewingModeState.PlacedBlueprints != null && ViewingModeState.PlacedBlueprints.Count > 0)
                {
                    List<IntVec3> blueprintCells = new List<IntVec3>();
                    foreach (Thing blueprint in ViewingModeState.PlacedBlueprints)
                    {
                        if (blueprint != null && !blueprint.Destroyed)
                        {
                            blueprintCells.Add(blueprint.Position);
                        }
                    }
                    if (blueprintCells.Count > 0)
                    {
                        GenDraw.DrawFieldEdges(blueprintCells, Color.green);
                    }
                }
            }
        }

        /// <summary>
        /// Draws the ghost of the building being placed at the keyboard cursor, using vanilla's own
        /// ghost drawer and accept/reject colours. Vanilla anchors its placement preview to the real
        /// pointer (decompiled RimWorld/Designator_Place.cs:140-158), so without this a sighted
        /// viewer sees the highlighted cells grow with no building in them.
        /// </summary>
        private static void DrawKeyboardPlacementGhost(Map map)
        {
            // Only a session the keyboard opened: a mouse-selected designator's ghost already
            // follows the pointer, and a live drag is the pointer's gesture.
            if (!ShapePlacementState.IsActive && !ArchitectState.IsInPlacementMode)
                return;
            if (Find.DesignatorManager == null || Find.DesignatorManager.Dragger.Dragging)
                return;
            if (!(ArchitectPlacementInputPatch.GetActiveDesignator() is Designator_Place place))
                return;

            IntVec3 cell = MapNavigationState.CurrentCursorPosition;
            if (!cell.InBounds(map))
                return;

            // Vanilla's pre-ghost overlays (disturbed meditation foci, Gauranlen connections,
            // psychic ritual spots), redrawn at the keyboard cursor: vanilla's SelectedUpdate
            // returns before them whenever the pointer is off-map or resting in the architect info
            // rect, so a parked pointer would hide them entirely.
            InvokeAtCursor(place, cell, DrawBeforeGhostMethod);

            // Terrain takes vanilla's early branch, where the place workers are drawn on their own
            // rather than through the ghost's drawPlaceWorkers flag below.
            if (!(place.PlacingDef is ThingDef thingDef))
            {
                InvokeAtCursor(place, cell, DrawPlaceWorkersMethod);
                return;
            }

            Color ghostCol = place.CanDesignateCell(cell).Accepted
                ? Designator_Place.CanPlaceColor
                : Designator_Place.CannotPlaceColor;
            Rot4 rot = BuildingReflection.GetPlacingRot(place);
            GhostDrawer.DrawGhostThing(cell, rot, thingDef,
                place.ThingStyleDefForPreview?.Graphic, ghostCol, AltitudeLayer.Blueprint,
                null, drawPlaceWorkers: true, place.StuffDef);
            if (ghostCol == Designator_Place.CanPlaceColor && thingDef.specialDisplayRadius > 0.01f)
            {
                GenDraw.DrawRadiusRing(cell, thingDef.specialDisplayRadius);
            }
            GenDraw.DrawInteractionCells(thingDef, cell, rot);
        }

        // Both are protected virtual on Designator_Place, so they are invoked on the instance and a
        // subclass's own override answers.
        private static readonly System.Reflection.MethodInfo DrawBeforeGhostMethod =
            AccessTools.Method(typeof(Designator_Place), "DrawBeforeGhost");
        private static readonly System.Reflection.MethodInfo DrawPlaceWorkersMethod =
            AccessTools.Method(typeof(Designator_Place), "DrawPlaceWorkers");

        /// <summary>
        /// Runs one of vanilla's draw helpers with the cursor override in place, so every
        /// <c>UI.MouseCell</c> inside it answers with the keyboard cursor. Draw-only, so a modded
        /// place worker that throws costs its own overlay and nothing else.
        /// </summary>
        private static void InvokeAtCursor(Designator_Place place, IntVec3 cell, System.Reflection.MethodInfo method)
        {
            if (method == null)
                return;
            try
            {
                DevToolTargeting.WithCursorOverride(cell, () =>
                {
                    method.Invoke(place, null);
                    return true;
                });
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Keyboard placement overlay error", ex);
            }
        }

        /// <summary>
        /// The area currently being browsed by keyboard, whichever of the three area-editing
        /// surfaces is live.
        /// </summary>
        private static Area BrowsedAreaForDisplay()
        {
            Area area = AreaSelectionMenuState.CurrentAreaForDisplay;
            if (area != null)
                return area;

            ManageAreasScope manageScope = FocusStackLookup.TopmostOfType<ManageAreasScope>();
            if (manageScope != null)
            {
                area = manageScope.FocusedAreaForDisplay();
                if (area != null)
                    return area;
            }

            PawnAreaMenuScope pawnAreaScope = FocusStackLookup.TopmostOfType<PawnAreaMenuScope>();
            if (pawnAreaScope != null)
            {
                area = pawnAreaScope.FocusedAreaForDisplay();
                if (area != null)
                    return area;
            }

            return null;
        }

        private static void RenderCells(IEnumerable<IntVec3> cells, Material material)
        {
            foreach (var cell in cells)
            {
                Vector3 pos = cell.ToVector3Shifted();
                pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                Graphics.DrawMesh(MeshPool.plane10, pos, Quaternion.identity, material, 0);
            }
        }

        private static void RenderRectangleOutline(IntVec3 start, IntVec3 end)
        {
            int minX = Mathf.Min(start.x, end.x);
            int maxX = Mathf.Max(start.x, end.x);
            int minZ = Mathf.Min(start.z, end.z);
            int maxZ = Mathf.Max(start.z, end.z);

            CellRect rect = CellRect.FromLimits(minX, minZ, maxX, maxZ);
            GenDraw.DrawFieldEdges(rect.Cells.ToList());
        }
    }

    /// <summary>
    /// Paints a keyboard-driven shape's dimensions and cell count over the map the way vanilla's
    /// dragger paints a mouse drag's (decompiled Verse/DesignationDragger.cs:162-181): same widget,
    /// same thresholds, and the designator's DragDrawMeasurements plus the shape's DrawStyleDef
    /// flags decide whether each number is drawn. Measurements belong in the GUI pass, not the
    /// Update-phase preview above.
    /// </summary>
    [HarmonyPatch(typeof(DesignatorManager), nameof(DesignatorManager.DesignationManagerOnGUI))]
    internal static class ShapeMeasurementOverlayPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!ShapePlacementState.IsActive || !ShapePlacementState.FirstPoint.HasValue)
                return;
            if (Find.DesignatorManager == null || Find.DesignatorManager.Dragger.Dragging)
                return;

            Designator designator = ArchitectPlacementInputPatch.GetActiveDesignator();
            if (designator == null)
                return;

            DrawStyleDef style = ShapeHelper.GetDrawStyleDef(designator, ShapePlacementState.CurrentShape);
            IntVec3 start = ShapePlacementState.FirstPoint.Value;
            IntVec3 end = ShapePlacementState.SecondPoint ?? MapNavigationState.CurrentCursorPosition;
            int width = Mathf.Abs(start.x - end.x) + 1;
            int height = Mathf.Abs(start.z - end.z) + 1;
            Vector2 middle = (start.ToUIPosition() + end.ToUIPosition()) / 2f;

            if (designator.DragDrawMeasurements)
            {
                bool longSideIsX = width >= height;
                bool drawShortSide = style != null && style.drawShortSideMeasurement;
                if (width >= 5 && (drawShortSide || longSideIsX))
                {
                    Vector2 pos = new Vector2(middle.x, start.ToUIPosition().y);
                    Widgets.DrawNumberOnMap(pos, width, Color.white);
                }
                if (height >= 5 && (drawShortSide || !longSideIsX))
                {
                    Vector2 pos = new Vector2(start.ToUIPosition().x, middle.y);
                    Widgets.DrawNumberOnMap(pos, height, Color.white);
                }
            }

            int cells = ShapePlacementState.PreviewCells?.Count ?? 0;
            if (cells > 0 && width >= 3 && height >= 3 && (style == null || style.drawArea))
            {
                Widgets.DrawNumberOnMap(middle, cells, Color.white);
            }
        }
    }
}
