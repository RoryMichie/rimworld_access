using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Cell-specific context for buildings, blueprints and frames: which part of a multi-cell
    /// building the cursor is on (bed head vs foot, fuel port direction, interaction spot).
    /// </summary>
    public static class BuildingCellHelper
    {
        /// <summary>
        /// Prefix to prepend to a building label describing the cursor's cell, or null when the
        /// cell carries no special meaning.
        /// </summary>
        public static string GetCellPrefix(Thing thing, IntVec3 cursorPosition)
        {
            if (thing == null)
                return null;

            ThingDef thingDef = GetActualThingDef(thing);
            if (thingDef == null)
                return null;

            IntVec3 position = thing.Position;
            Rot4 rotation = thing.Rotation;

            if (thingDef.IsBed)
            {
                return GetBedCellPrefix(thingDef, position, rotation, cursorPosition);
            }

            if (thingDef.building?.hasFuelingPort == true)
            {
                return GetFuelPortInfo(position, rotation, cursorPosition);
            }

            if (IsCooler(thingDef))
            {
                return GetCoolerDirectionInfo(rotation);
            }

            if (IsVent(thingDef))
            {
                return GetVentDirectionInfo(rotation);
            }

            if (IsThrone(thingDef))
            {
                return GetThroneDirectionInfo(rotation);
            }

            if (IsNutrientPasteDispenser(thingDef))
            {
                return GetDispenserInfo(thingDef, rotation);
            }

            if (IsTurret(thingDef))
            {
                return GetTurretDirectionInfo(rotation);
            }

            if (IsMechCharger(thingDef))
            {
                return GetMechChargerInfo(thingDef, rotation);
            }

            if (IsBookcase(thingDef))
            {
                return GetBookcaseInfo(rotation);
            }

            if (IsWatchableBuilding(thingDef))
            {
                return GetWatchableInfo(thingDef, rotation);
            }

            if (!thingDef.multipleInteractionCellOffsets.NullOrEmpty())
            {
                return GetMultiInteractionCellInfo(thingDef, position, rotation, cursorPosition);
            }

            if (thingDef.hasInteractionCell)
            {
                return GetInteractionCellInfo(thingDef, position, rotation, cursorPosition);
            }

            return null;
        }

        /// <summary>
        /// The def a thing represents: for blueprints and frames, the def they will become.
        /// </summary>
        private static ThingDef GetActualThingDef(Thing thing)
        {
            if (thing == null)
                return null;

            if (thing is Blueprint blueprint)
            {
                return blueprint.def.entityDefToBuild as ThingDef;
            }

            if (thing is Frame frame)
            {
                return frame.def.entityDefToBuild as ThingDef;
            }

            return thing.def as ThingDef;
        }

        /// <summary>
        /// Head/foot prefix for a bed cell, or null when the cursor is on neither.
        /// </summary>
        private static string GetBedCellPrefix(ThingDef bedDef, IntVec3 bedPosition, Rot4 bedRotation, IntVec3 cursorPosition)
        {
            int slots = BedUtility.GetSleepingSlotsCount(bedDef.size);

            for (int i = 0; i < slots; i++)
            {
                IntVec3 slotHead = BedUtility.GetSleepingSlotPos(i, bedPosition, bedRotation, bedDef.size);
                IntVec3 slotFoot = BedUtility.GetFeetSlotPos(i, bedPosition, bedRotation, bedDef.size);

                if (cursorPosition == slotHead)
                    return "RimWorldAccess.Map.BuildingCell.Head".Translate();
                if (cursorPosition == slotFoot)
                    return "RimWorldAccess.Map.BuildingCell.Foot".Translate();
            }

            return null;
        }

        /// <summary>
        /// Fuel port direction, spoken only when the cursor sits directly adjacent to the port cell.
        /// </summary>
        private static string GetFuelPortInfo(IntVec3 position, Rot4 rotation, IntVec3 cursorPosition)
        {
            IntVec3 fuelPortCell = FuelingPortUtility.GetFuelingPortCell(position, rotation);

            IntVec3 offsetFromCursor = fuelPortCell - cursorPosition;

            bool isAdjacent = (System.Math.Abs(offsetFromCursor.x) + System.Math.Abs(offsetFromCursor.z)) == 1;

            if (isAdjacent)
            {
                string direction = GetCardinalDirection(offsetFromCursor);
                if (!string.IsNullOrEmpty(direction))
                    return "RimWorldAccess.Map.BuildingCell.FuelPort".Translate(direction);
            }

            return null;
        }

        /// <summary>Whether a def is a cooler or a subclass of one.</summary>
        private static bool IsCooler(ThingDef def)
        {
            if (def?.thingClass == null)
                return false;

            return typeof(Building_Cooler).IsAssignableFrom(def.thingClass);
        }

        /// <summary>
        /// Cooling/heating sides for a cooler. Per <c>Building_Cooler.TickRare</c>, south relative
        /// to rotation cools and north exhausts heat.
        /// </summary>
        private static string GetCoolerDirectionInfo(Rot4 rotation)
        {
            IntVec3 coolingSide = IntVec3.South.RotatedBy(rotation);
            string coolingDir = GetCardinalDirection(coolingSide);

            IntVec3 heatingSide = IntVec3.North.RotatedBy(rotation);
            string heatingDir = GetCardinalDirection(heatingSide);

            return "RimWorldAccess.Map.BuildingCell.Cooler".Translate(coolingDir, heatingDir);
        }

        /// <summary>Whether a def is a vent or a subclass of one (e.g. Building_AncientVent).</summary>
        private static bool IsVent(ThingDef def)
        {
            if (def?.thingClass == null)
                return false;

            return typeof(Building_Vent).IsAssignableFrom(def.thingClass);
        }

        /// <summary>
        /// The two rooms a vent connects. Per <c>GenTemperature.EqualizeTemperaturesThroughBuilding</c>,
        /// that is the facing cell and its opposite, i.e. north and south relative to rotation.
        /// </summary>
        private static string GetVentDirectionInfo(Rot4 rotation)
        {
            IntVec3 northSide = IntVec3.North.RotatedBy(rotation);
            IntVec3 southSide = IntVec3.South.RotatedBy(rotation);

            string northDir = GetCardinalDirection(northSide);
            string southDir = GetCardinalDirection(southSide);

            return "RimWorldAccess.Map.BuildingCell.Vent".Translate(northDir, southDir);
        }

        /// <summary>Whether a def is a throne or a subclass of one.</summary>
        private static bool IsThrone(ThingDef def)
        {
            if (def?.thingClass == null)
                return false;

            return typeof(Building_Throne).IsAssignableFrom(def.thingClass);
        }

        /// <summary>
        /// Always null: thrones are 1x1 and the main announcement already carries "Facing X".
        /// </summary>
        private static string GetThroneDirectionInfo(Rot4 rotation)
        {
            return null;
        }

        /// <summary>Whether a def wants an adjacent hopper (nutrient paste dispensers and kin).</summary>
        private static bool IsNutrientPasteDispenser(ThingDef def)
        {
            if (def == null)
                return false;

            return def.building?.wantsHopperAdjacent == true;
        }

        /// <summary>
        /// Where hoppers may go. Per <c>Building_NutrientPasteDispenser.AdjCellsCardinalInBounds</c>,
        /// any of the four cardinal neighbours works, so no direction is named.
        /// </summary>
        private static string GetDispenserInfo(ThingDef def, Rot4 rotation)
        {
            return "RimWorldAccess.Map.BuildingCell.HopperAdjacent".Translate();
        }

        /// <summary>Whether a def is a turret (the game's own turretGunDef check).</summary>
        private static bool IsTurret(ThingDef def)
        {
            if (def == null)
                return false;

            return def.building?.IsTurret == true;
        }

        /// <summary>
        /// Always null: turrets rotate to fire in any direction, so "Facing X" already says enough.
        /// </summary>
        private static string GetTurretDirectionInfo(Rot4 rotation)
        {
            return null;
        }

        /// <summary>Whether a def is a mech charger or a subclass of one.</summary>
        private static bool IsMechCharger(ThingDef def)
        {
            if (def?.thingClass == null)
                return false;

            return typeof(Building_MechCharger).IsAssignableFrom(def.thingClass);
        }

        /// <summary>Where the mech stands to charge: the def's interaction cell.</summary>
        private static string GetMechChargerInfo(ThingDef def, Rot4 rotation)
        {
            if (def.hasInteractionCell)
            {
                IntVec3 offset = def.interactionCellOffset.RotatedBy(rotation);
                string standDir = GetCardinalDirection(offset);
                if (!string.IsNullOrEmpty(standDir))
                    return "RimWorldAccess.Map.BuildingCell.MechCharger".Translate(standDir);
            }
            return null;
        }

        /// <summary>Whether a def is a bookcase or a subclass of one.</summary>
        private static bool IsBookcase(ThingDef def)
        {
            if (def?.thingClass == null)
                return false;

            return typeof(Building_Bookcase).IsAssignableFrom(def.thingClass);
        }

        /// <summary>
        /// Where pawns reach books. Bookcases have no interaction cell; access is from the facing side.
        /// </summary>
        private static string GetBookcaseInfo(Rot4 rotation)
        {
            string accessDir = GetRotationName(rotation);
            return "RimWorldAccess.Map.BuildingCell.Bookcase".Translate(accessDir);
        }

        /// <summary>
        /// Whether pawns can watch a building for joy. <c>PlaceWorker_WatchArea</c> is the only
        /// reliable marker.
        /// </summary>
        private static bool IsWatchableBuilding(ThingDef def)
        {
            if (def == null)
                return false;

            var placeWorkers = def.placeWorkers;
            if (placeWorkers != null)
            {
                foreach (var workerType in placeWorkers)
                {
                    if (workerType == typeof(PlaceWorker_WatchArea))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Which side pawns watch from, or null for non-rotatable buildings watchable from anywhere.
        /// </summary>
        private static string GetWatchableInfo(ThingDef def, Rot4 rotation)
        {
            if (def.rotatable)
            {
                string direction = GetCardinalDirection(rotation.FacingCell);
                if (!string.IsNullOrEmpty(direction))
                    return "RimWorldAccess.Map.BuildingCell.Watchable".Translate(direction);
            }
            return null;
        }

        /// <summary>
        /// Interaction cell info, spoken only when the cursor is on that cell or directly adjacent.
        /// </summary>
        private static string GetInteractionCellInfo(ThingDef thingDef, IntVec3 position, Rot4 rotation, IntVec3 cursorPosition)
        {
            IntVec3 interactionCell = ThingUtility.InteractionCellWhenAt(thingDef, position, rotation, null);

            if (cursorPosition == interactionCell)
            {
                return "RimWorldAccess.Map.BuildingCell.InteractionSpot".Translate();
            }

            IntVec3 offset = interactionCell - cursorPosition;
            bool isAdjacent = (System.Math.Abs(offset.x) + System.Math.Abs(offset.z)) == 1;

            if (isAdjacent)
            {
                string direction = GetCardinalDirection(offset);
                if (!string.IsNullOrEmpty(direction))
                    return "RimWorldAccess.Map.BuildingCell.UseFrom".Translate(direction);
            }

            return null;
        }

        /// <summary>
        /// Localized name of the direction an offset points, cardinals and diagonals; null for zero.
        /// </summary>
        internal static string GetCardinalDirection(IntVec3 offset)
        {
            if (offset.z > 0 && offset.x == 0) return "RimWorldAccess.Map.Direction.Lower.North".Translate();
            if (offset.z < 0 && offset.x == 0) return "RimWorldAccess.Map.Direction.Lower.South".Translate();
            if (offset.x > 0 && offset.z == 0) return "RimWorldAccess.Map.Direction.Lower.East".Translate();
            if (offset.x < 0 && offset.z == 0) return "RimWorldAccess.Map.Direction.Lower.West".Translate();

            if (offset.z > 0 && offset.x > 0) return "RimWorldAccess.Map.Direction.Lower.Northeast".Translate();
            if (offset.z > 0 && offset.x < 0) return "RimWorldAccess.Map.Direction.Lower.Northwest".Translate();
            if (offset.z < 0 && offset.x > 0) return "RimWorldAccess.Map.Direction.Lower.Southeast".Translate();
            if (offset.z < 0 && offset.x < 0) return "RimWorldAccess.Map.Direction.Lower.Southwest".Translate();

            return null;
        }

        /// <summary>
        /// Special position info to announce while placing a def, or null if it has none.
        /// </summary>
        public static string GetPlacementPositionInfo(ThingDef def, Rot4 rotation)
        {
            if (def == null)
                return null;

            if (def.IsBed)
            {
                return GetBedHeadInfo(def, rotation);
            }

            if (def.building?.hasFuelingPort == true)
            {
                return GetFuelPortDirectionInfo(rotation);
            }

            if (IsCooler(def))
            {
                return GetCoolerDirectionInfo(rotation);
            }

            if (IsVent(def))
            {
                return GetVentDirectionInfo(rotation);
            }

            if (IsThrone(def))
            {
                return GetThroneDirectionInfo(rotation);
            }

            if (IsNutrientPasteDispenser(def))
            {
                return GetDispenserInfo(def, rotation);
            }

            if (IsTurret(def))
            {
                return GetTurretDirectionInfo(rotation);
            }

            if (IsMechCharger(def))
            {
                return GetMechChargerInfo(def, rotation);
            }

            if (IsBookcase(def))
            {
                return GetBookcaseInfo(rotation);
            }

            if (IsWatchableBuilding(def))
            {
                if (def.rotatable)
                {
                    string direction = GetRotationName(rotation);
                    return "RimWorldAccess.Map.Placement.Watchable".Translate(direction);
                }
            }

            if (!def.multipleInteractionCellOffsets.NullOrEmpty())
            {
                return GetMultiInteractionPlacementInfo(def, rotation);
            }

            if (def.hasInteractionCell)
            {
                IntVec3 offset = def.interactionCellOffset.RotatedBy(rotation);
                int distance = System.Math.Abs(offset.x) + System.Math.Abs(offset.z);
                string direction = GetCardinalDirection(offset);

                if (!string.IsNullOrEmpty(direction))
                {
                    // Be specific about distance if more than 1 tile away
                    if (distance == 1)
                        return "RimWorldAccess.Map.Placement.InteractFromAdjacent".Translate(direction);
                    else
                        return "RimWorldAccess.Map.Placement.InteractFromDistant".Translate(distance, direction);
                }
            }

            // Facility linking info (for buildings that provide or receive facility bonuses)
            string facilityInfo = FacilityLinkHelper.GetPlacementInfo(def);
            if (!string.IsNullOrEmpty(facilityInfo))
                return facilityInfo;

            return null;
        }

        /// <summary>
        /// Gets bed head position info for placement.
        /// RimWorld places beds with the head (pillow end) opposite to the facing direction.
        /// For double beds (2x2), there are two head positions side by side along one edge.
        /// Animal sleeping spots don't have meaningful head/foot distinction.
        /// </summary>
        private static string GetBedHeadInfo(ThingDef def, Rot4 rotation)
        {
            // Skip head info for animal beds - they don't have head/foot distinction
            if (def.building?.bed_humanlike == false)
            {
                return null;
            }

            // Head is opposite to facing direction
            // North-facing bed: head on south edge
            // East-facing bed: head on west edge
            // etc.
            Rot4 headDirection = rotation.Opposite;
            string headEdge = GetRotationName(headDirection);

            // Get bed size to determine number of sleeping slots
            IntVec2 size = def.Size;
            int slots = BedUtility.GetSleepingSlotsCount(size);

            if (slots >= 2)
            {
                // Double bed - head is an edge, not a single tile
                return "RimWorldAccess.Map.Placement.BedHeadEdge".Translate(headEdge);
            }

            // Single bed - head is on one side
            return "RimWorldAccess.Map.Placement.BedHeadSide".Translate(headEdge);
        }

        /// <summary>
        /// Gets fuel port direction info for placement.
        /// Calculates the actual fuel port cell position relative to cursor.
        /// </summary>
        private static string GetFuelPortDirectionInfo(Rot4 rotation)
        {
            // Get cursor position and calculate actual fuel port location
            IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
            IntVec3 fuelPortCell = FuelingPortUtility.GetFuelingPortCell(cursorPosition, rotation);

            // Calculate offset from cursor to fuel port
            IntVec3 offset = fuelPortCell - cursorPosition;
            int distance = System.Math.Abs(offset.x) + System.Math.Abs(offset.z);

            string direction = GetCardinalDirection(offset);
            if (string.IsNullOrEmpty(direction))
                return null;

            // Be specific about distance if more than 1 tile away
            if (distance == 1)
            {
                return "RimWorldAccess.Map.Placement.FuelPortAdjacent".Translate(direction);
            }
            else
            {
                return "RimWorldAccess.Map.Placement.FuelPortDistant".Translate(distance, direction);
            }
        }

        /// <summary>
        /// Gets interaction cell info for buildings with multiple interaction cells.
        /// Handles school desks (student/teacher spots) and other multi-cell buildings generically.
        /// </summary>
        private static string GetMultiInteractionCellInfo(ThingDef thingDef, IntVec3 position, Rot4 rotation, IntVec3 cursorPosition)
        {
            var cells = ThingUtility.InteractionCellsWhenAt(thingDef, position, rotation, null);

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                string spotLabel = GetInteractionSpotLabel(thingDef, i);

                if (cursorPosition == cell)
                {
                    return spotLabel;
                }

                IntVec3 offset = cell - cursorPosition;
                bool isAdjacent = (System.Math.Abs(offset.x) + System.Math.Abs(offset.z)) == 1;
                if (isAdjacent)
                {
                    string direction = GetCardinalDirection(offset);
                    if (!string.IsNullOrEmpty(direction))
                        return "RimWorldAccess.Map.BuildingCell.SpotWithDirection".Translate(spotLabel, direction);
                }
            }

            return null;
        }

        /// <summary>
        /// Gets placement info for buildings with multiple interaction cells.
        /// Announces the direction of each interaction spot relative to the building.
        /// </summary>
        private static string GetMultiInteractionPlacementInfo(ThingDef def, Rot4 rotation)
        {
            var parts = new List<string>();

            for (int i = 0; i < def.multipleInteractionCellOffsets.Count; i++)
            {
                IntVec3 offset = def.multipleInteractionCellOffsets[i].RotatedBy(rotation);
                string direction = GetCardinalDirection(offset);
                string label = GetInteractionSpotLabel(def, i);

                if (!string.IsNullOrEmpty(direction))
                {
                    parts.Add("RimWorldAccess.Map.BuildingCell.SpotWithDirection".Translate(label, direction));
                }
            }

            if (parts.Count > 0)
                return string.Join(", ", parts);

            return null;
        }

        /// <summary>
        /// Gets a human-readable label for a specific interaction cell index.
        /// School desks have known spots: index 0 = student, index 1 = teacher.
        /// Other buildings get generic numbered labels.
        /// </summary>
        private static string GetInteractionSpotLabel(ThingDef thingDef, int index)
        {
            if (thingDef == ThingDefOf.SchoolDesk)
            {
                if (index == 0) return "RimWorldAccess.Map.BuildingCell.StudentSpot".Translate();
                if (index == 1) return "RimWorldAccess.Map.BuildingCell.TeacherSpot".Translate();
            }

            if (thingDef.multipleInteractionCellOffsets != null && thingDef.multipleInteractionCellOffsets.Count == 1)
                return "RimWorldAccess.Map.BuildingCell.InteractionSpot".Translate();

            return "RimWorldAccess.Map.BuildingCell.NumberedSpot".Translate(index + 1);
        }

        /// <summary>
        /// Gets a human-readable name for a rotation (lowercase).
        /// Delegates to ArchitectState for shared implementation.
        /// </summary>
        private static string GetRotationName(Rot4 rotation)
        {
            return ArchitectState.GetRotationName(rotation).ToLower();
        }
    }
}
