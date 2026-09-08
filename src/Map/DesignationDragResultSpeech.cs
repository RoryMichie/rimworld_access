using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speaks the result of a placement the mouse made — what was placed and at what cost, what
    /// blocked it, the enclosure it formed — in the words the keyboard flow uses when it confirms a
    /// shape, minus viewing mode's keyboard hints, since the gesture is over and vanilla owns what
    /// follows. The extent while the drag grows is <see cref="DesignationDragSpeech"/>.
    ///
    /// Announce-only. Bracketing vanilla's own input handler is what makes origination structural:
    /// the constructibles appearing inside the bracket are the ones this gesture created. Nothing
    /// here designates, deselects or touches the event. A gesture that placed nothing stays silent,
    /// since <c>Designator.FinalizeDesignationFailed</c> speaks its own rejection and the Messages
    /// patch reads that out.
    /// </summary>
    [HarmonyPatch(typeof(DesignatorManager), nameof(DesignatorManager.ProcessInputEvents))]
    internal static class DesignationDragResultSpeech
    {
        /// <summary>What the designator was about to act on, captured before it acted.</summary>
        internal sealed class Gesture
        {
            internal Designator Designator;
            internal Map Map;
            internal DesignatorClassification Classification;
            internal ThingDef PlacingDef;
            /// <summary>A styleless click on one cell rather than a drag over a shape.</summary>
            internal bool IsClick;
            internal IntVec3 Cell;
            internal List<IntVec3> Candidates;
            internal HashSet<IntVec3> Accepted;
            /// <summary>Constructibles already standing on the accepted cells.</summary>
            internal HashSet<Thing> Standing;
            /// <summary>Every zone on the map before the gesture, so a zone it lands in is either one of these (expansion) or brand new.</summary>
            internal HashSet<Zone> ZonesBefore;
            /// <summary>The accepted cells that already belonged to a zone.</summary>
            internal HashSet<IntVec3> ZonedBefore;
            /// <summary>What was selected before the gesture, so a gesture that only changed the selection can say so.</summary>
            internal List<object> SelectionBefore;
        }

        /// <summary>
        /// Whether vanilla is about to designate through the pointer this pass, whatever the tool.
        /// Separate from <see cref="Gesture"/> because the keyboard shape session must reset for
        /// EVERY pointer designation while only some have a result worth describing: the styleless
        /// branch designates a cell for any designator (Verse/DesignatorManager.cs:93-107), but
        /// <see cref="ArmClick"/> declines everything that is not a place designator.
        /// </summary>
        private static bool pointerDesignated;

        [HarmonyPrefix]
        public static void Prefix(DesignatorManager __instance, out Gesture __state)
        {
            __state = null;
            pointerDesignated = false;
            try
            {
                Event ev = Event.current;
                if (__instance == null || ev == null)
                    return;

                Designator designator = __instance.SelectedDesignator;
                Map map = designator?.Map;
                // CanRemainSelected is vanilla's first gate; a tool it drops designates nothing.
                if (designator == null || map == null || !designator.CanRemainSelected())
                    return;

                // Vanilla's two designating branches (DesignatorManager.cs:93-129): a styleless
                // press designates the cell under the pointer, a release ends a drag.
                if (ev.type == EventType.MouseDown && ev.button == 0 && __instance.SelectedStyle == null)
                {
                    IntVec3 clicked = UI.MouseCell();
                    pointerDesignated = clicked.InBounds(map) && designator.CanDesignateCell(clicked).Accepted;
                    __state = ArmClick(designator, map);
                    return;
                }
                bool singleCellStyle = __instance.SelectedStyle?.DrawStyleWorker?.SingleCell ?? false;
                if (ev.type == EventType.MouseUp && ev.button == 0 && (__instance.Dragger.Dragging || singleCellStyle))
                {
                    pointerDesignated = true;
                    __state = ArmDrag(designator, map, __instance.Dragger);
                }
            }
            catch (Exception ex)
            {
                __state = null;
                ModLogger.LimitedError("Designation result speech arm error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Gesture __state)
        {
            bool designated = pointerDesignated;
            pointerDesignated = false;
            if (designated)
            {
                ClearKeyboardShapeAfterPointerGesture();
            }
            if (__state == null)
                return;
            try
            {
                string text = __state.IsClick ? DescribeClick(__state) : DescribeDrag(__state);
                // A zone gesture that designated nothing may have changed the selection instead:
                // dropping a zone tool on an existing zone of its own kind selects it and returns
                // (Designator_ZoneAdd.cs:107-117). That happens inside the bracketed body, earlier
                // in the frame than Selector.HandleMapClicks, so MapClickAnnouncementPatch finds
                // the selection already changed and stays silent.
                if (string.IsNullOrEmpty(text))
                {
                    text = DescribeSelectionChange(__state);
                }
                if (!string.IsNullOrEmpty(text))
                {
                    // High, like the drag extents this supersedes: the last extent is usually
                    // still being read when the button comes up.
                    TolkHelper.SpeakData(text, SpeechPriority.High);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Designation result speech error", ex);
            }
        }

        /// <summary>
        /// Puts the keyboard shape session back where the keyboard's own confirm leaves it, once
        /// the pointer has finished a designation with the same designator. Otherwise half-set
        /// corners survive the gesture and Space stretches a shape from a cell the pointer already
        /// built over. Silent: the gesture's result has just been spoken.
        /// </summary>
        private static void ClearKeyboardShapeAfterPointerGesture()
        {
            if (ShapePlacementState.CurrentPhase != PlacementPhase.Inactive)
            {
                ShapePlacementState.ClearSelectionAndStay(silent: true);
            }
        }

        /// <summary>
        /// A styleless press places one building under the pointer. Armed only for place
        /// designators, whose single-cell result the keyboard also speaks as "placed at"; other
        /// styleless tools have no placement to report.
        /// </summary>
        private static Gesture ArmClick(Designator designator, Map map)
        {
            if (!ShapeHelper.IsPlaceDesignator(designator))
                return null;
            IntVec3 cell = UI.MouseCell();
            // Vanilla's gate on this branch, same cell, same frame.
            if (!cell.InBounds(map) || !designator.CanDesignateCell(cell).Accepted)
                return null;
            return new Gesture { Designator = designator, Map = map, IsClick = true, Cell = cell };
        }

        private static Gesture ArmDrag(Designator designator, Map map, DesignationDragger dragger)
        {
            // DragCells is the accepted subset vanilla is about to designate, and reading it first
            // refreshes this frame's cell buffer, which holds the whole shape including rejections.
            var accepted = new HashSet<IntVec3>(dragger.DragCells);
            if (accepted.Count == 0)
                return null;

            DesignatorClassification classification = ShapeHelper.ClassifyDesignator(designator);
            ThingDef placingDef = (designator as Designator_Place)?.PlacingDef as ThingDef;
            var standing = new HashSet<Thing>();
            HashSet<Zone> zonesBefore = null;
            HashSet<IntVec3> zonedBefore = null;

            if (classification.IsZone)
            {
                zonesBefore = new HashSet<Zone>(map.zoneManager.AllZones);
                zonedBefore = new HashSet<IntVec3>();
                foreach (IntVec3 cell in accepted)
                {
                    if (map.zoneManager.ZoneAt(cell) != null)
                        zonedBefore.Add(cell);
                }
            }
            else
            {
                foreach (IntVec3 cell in accepted)
                {
                    CollectConstructibles(cell, map, placingDef, standing);
                }
            }

            return new Gesture
            {
                Designator = designator,
                Map = map,
                Classification = classification,
                PlacingDef = placingDef,
                Candidates = new List<IntVec3>(dragger.CellBuffer),
                Accepted = accepted,
                Standing = standing,
                ZonesBefore = zonesBefore,
                ZonedBefore = zonedBefore,
                SelectionBefore = SelectionSnapshot(),
            };
        }

        /// <summary>Whatever the selector holds right now, or null when there is no selector.</summary>
        private static List<object> SelectionSnapshot()
        {
            List<object> selected = Find.Selector != null ? Find.Selector.SelectedObjects : null;
            return selected != null ? new List<object>(selected) : null;
        }

        /// <summary>
        /// The selection the gesture left behind, when it changed it, described with the announcer
        /// a plain map click uses so both read alike.
        /// </summary>
        private static string DescribeSelectionChange(Gesture gesture)
        {
            List<object> before = gesture.SelectionBefore;
            if (before == null)
                return null;
            List<object> now = Find.Selector != null ? Find.Selector.SelectedObjects : null;
            if (now == null || SameSelection(before, now))
                return null;
            return MapSelectionAnnouncer.DescribeSelection(now);
        }

        private static bool SameSelection(List<object> before, List<object> now)
        {
            if (before.Count != now.Count)
                return false;
            for (int i = 0; i < now.Count; i++)
            {
                if (!ReferenceEquals(before[i], now[i]))
                    return false;
            }
            return true;
        }

        private static string DescribeClick(Gesture gesture)
        {
            return "RimWorldAccess.Building.View.PlacedAt"
                .Loc(gesture.Designator.Label, gesture.Cell.x, gesture.Cell.z).ToString();
        }

        private static string DescribeDrag(Gesture gesture)
        {
            Designator designator = gesture.Designator;
            Map map = gesture.Map;
            DesignatorClassification classification = gesture.Classification;
            var designatedCells = new List<IntVec3>();
            var placedThings = new List<Thing>();
            var obstacleCells = new List<IntVec3>();

            // A building's footprint can swallow a later cell of the same drag, so a cell counts
            // as placed only once something stands on it that was not there before. Terrain and
            // designations leave no thing behind and cannot conflict, so for those the accepted
            // cells are the designated cells.
            bool countThings = gesture.PlacingDef != null;
            foreach (IntVec3 cell in gesture.Candidates)
            {
                if (!cell.InBounds(map))
                    continue;
                if (!gesture.Accepted.Contains(cell))
                {
                    obstacleCells.Add(cell);
                    continue;
                }
                if (classification.IsZone)
                {
                    if (ZoneMembershipChanged(cell, gesture))
                        designatedCells.Add(cell);
                    continue;
                }
                Thing placed = NewConstructibleAt(cell, map, gesture.PlacingDef, gesture.Standing);
                if (placed != null)
                    placedThings.Add(placed);
                if (placed != null || !countThings)
                    designatedCells.Add(cell);
            }

            if (designatedCells.Count == 0)
                return null;

            if (ShapeHelper.IsPlaceDesignator(designator))
            {
                List<Enclosure> enclosures = EnclosureDetector.DetectEnclosures(placedThings, map, obstacleCells);
                return Join(ViewingModeAnnouncer.BuildPlacementFactParts(
                    designator, designatedCells.Count, string.Empty, classification.IsBuild,
                    obstacleCells, enclosures, placedThings, designatedCells));
            }

            if (classification.IsZone)
            {
                // A shrink reports no obstacles, matching the cells viewing mode collects for it.
                List<IntVec3> zoneObstacles = classification.IsDelete ? new List<IntVec3>() : obstacleCells;
                HashSet<Zone> zones = ZonesOf(designatedCells, gesture, out bool wasExpansion);
                return Join(ViewingModeAnnouncer.BuildZoneFactParts(
                    designator, designatedCells.Count, string.Empty, classification.IsDelete,
                    zoneObstacles, designatedCells, designatedCells, wasExpansion, null, zones));
            }

            if (classification.IsArea || classification.IsBuiltInArea)
            {
                // The same area objects the designator writes to: vanilla's static selection for
                // allowed areas, the map's fixed area for the built-in ones.
                Area area = classification.IsBuiltInArea
                    ? ShapeHelper.GetBuiltInAreaForDesignator(designator, map)
                    : Designator_AreaAllowed.SelectedArea;
                return Join(ViewingModeAnnouncer.BuildAreaFactParts(
                    designator, designatedCells.Count, string.Empty, designatedCells, designatedCells,
                    area, classification.IsBuiltInArea));
            }

            // The same "Designated N for mining" the keyboard speaks without viewing mode.
            if (classification.IsOrder || ShapeHelper.IsCellsDesignator(designator))
            {
                string action = PlacementDescriber.DescribeAction(designator, ArchitectHelper.GetSanitizedLabel(designator));
                return "RimWorldAccess.Building.Place.DesignatedFor".Translate(designatedCells.Count, action);
            }

            return null;
        }

        /// <summary>
        /// Whether the gesture actually changed this cell's zone membership. Vanilla drops cells
        /// already in a zone when adding (Designator_ZoneAdd.cs:140) even though CanDesignateCell
        /// accepts them, and a shrink has done something only once the cell has no zone left.
        /// </summary>
        private static bool ZoneMembershipChanged(IntVec3 cell, Gesture gesture)
        {
            bool zonedNow = gesture.Map.zoneManager.ZoneAt(cell) != null;
            return zonedNow != gesture.ZonedBefore.Contains(cell);
        }

        /// <summary>
        /// The zones the gesture's cells now belong to, read off the live ZoneManager, and whether
        /// any predates the gesture — which makes it an expansion rather than a creation. A shrink
        /// leaves its cells zoneless.
        /// </summary>
        private static HashSet<Zone> ZonesOf(List<IntVec3> cells, Gesture gesture, out bool wasExpansion)
        {
            var zones = new HashSet<Zone>();
            wasExpansion = false;
            if (gesture.Classification.IsDelete)
                return zones;

            foreach (IntVec3 cell in cells)
            {
                Zone zone = gesture.Map.zoneManager.ZoneAt(cell);
                if (zone != null && zones.Add(zone) && gesture.ZonesBefore.Contains(zone))
                    wasExpansion = true;
            }
            return zones;
        }

        private static string Join(List<string> parts)
        {
            var builder = new AnnouncementBuilder();
            foreach (string part in parts)
            {
                builder.Add(part);
            }
            return builder.Build();
        }

        private static void CollectConstructibles(IntVec3 cell, Map map, ThingDef placingDef, HashSet<Thing> into)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (IsPlacementProduct(thing, placingDef))
                    into.Add(thing);
            }
        }

        private static Thing NewConstructibleAt(IntVec3 cell, Map map, ThingDef placingDef, HashSet<Thing> standing)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (IsPlacementProduct(thing, placingDef) && !standing.Contains(thing))
                    return thing;
            }
            return null;
        }

        /// <summary>
        /// What a place designator leaves in a cell: a blueprint, or the frame or finished
        /// building when the def needs no work or god mode is on.
        /// </summary>
        private static bool IsPlacementProduct(Thing thing, ThingDef placingDef)
        {
            return thing.def.IsBlueprint || thing.def.IsFrame || (placingDef != null && thing.def == placingDef);
        }
    }
}
