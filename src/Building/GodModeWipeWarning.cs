using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Audible warning for instant (god-mode / zero-work) build placements, which spawn the
    /// finished thing immediately and destroy whatever GenSpawn.SpawningWipes says they wipe.
    /// Shape flows fold the warning into the second-point announcement; instant single-cell
    /// placements arm a same-cell confirm so the first Space warns and the second places.
    /// </summary>
    public static class GodModeWipeWarning
    {
        private static IntVec3? armedCell;
        private static Designator armedDesignator;

        /// <summary>
        /// Mirrors Designator_Build.DesignateSingleCell's instant-path condition: god mode or a
        /// zero-work def skips the blueprint and spawns the finished structure.
        /// </summary>
        public static bool IsInstantBuild(Designator designator)
        {
            if (!(designator is Designator_Build build) || build.PlacingDef == null)
                return false;

            return DebugSettings.godMode ||
                build.PlacingDef.GetStatValueAbstract(StatDefOf.WorkToBuild, build.StuffDef) == 0f;
        }

        /// <summary>
        /// Things an instant placement over <paramref name="cells"/> would destroy. Mirrors the
        /// wipe GenSpawn.Spawn performs, filtered to buildings and items — the collisions vanilla
        /// itself treats as meaningful (SpawnBuildingAsPossible). Terrain placements wipe nothing.
        /// Only cells the designator accepts are counted, since only those get designated.
        /// </summary>
        public static List<Thing> ThingsWipedBy(Designator designator, IEnumerable<IntVec3> cells, Map map)
        {
            var wiped = new List<Thing>();
            if (map == null || !(designator is Designator_Build build) ||
                !(build.PlacingDef is ThingDef entDef))
                return wiped;

            Rot4 rot = BuildingReflection.GetPlacingRot(build);
            var seen = new HashSet<Thing>();
            foreach (IntVec3 cell in cells)
            {
                if (!designator.CanDesignateCell(cell).Accepted)
                    continue;

                foreach (IntVec3 occupied in GenAdj.CellsOccupiedBy(cell, rot, entDef.Size))
                {
                    if (!occupied.InBounds(map))
                        continue;

                    foreach (Thing thing in occupied.GetThingList(map))
                    {
                        if ((thing.def.category == ThingCategory.Building ||
                                thing.def.category == ThingCategory.Item) &&
                            GenSpawn.SpawningWipes(entDef, thing.def) &&
                            seen.Add(thing))
                        {
                            wiped.Add(thing);
                        }
                    }
                }
            }
            return wiped;
        }

        /// <summary>
        /// Groups identical labels with a count, preserving first-seen order,
        /// e.g. "table x2, steel x 75".
        /// </summary>
        public static string Summarize(List<Thing> wiped)
        {
            var order = new List<string>();
            var counts = new Dictionary<string, int>();
            foreach (Thing thing in wiped)
            {
                string label = thing.Label;
                if (counts.ContainsKey(label))
                {
                    counts[label]++;
                }
                else
                {
                    counts[label] = 1;
                    order.Add(label);
                }
            }

            var parts = new List<string>();
            foreach (string label in order)
            {
                parts.Add(counts[label] == 1
                    ? label
                    : (string)"RimWorldAccess.Building.Place.WillReplaceItem".Translate(label, counts[label]));
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Second-point announcement suffix for shape placements, or null when nothing is wiped
        /// or the placement is not instant.
        /// </summary>
        public static string ShapeSuffix(Designator designator, IEnumerable<IntVec3> cells, Map map)
        {
            if (!IsInstantBuild(designator))
                return null;

            List<Thing> wiped = ThingsWipedBy(designator, cells, map);
            if (wiped.Count == 0)
                return null;

            return "RimWorldAccess.Building.Place.WillReplaceSuffix".Translate(Summarize(wiped));
        }

        /// <summary>
        /// Gate for instant single-cell placement. Returns true when placement should proceed.
        /// The first press on a cell that would destroy something announces the loss and arms a
        /// confirm; a second press on the same cell with the same designator proceeds. Any other
        /// cell or designator re-warns.
        /// </summary>
        public static bool WarnBeforeInstantPlace(Designator designator, IntVec3 cell, Map map)
        {
            if (!IsInstantBuild(designator))
            {
                Disarm();
                return true;
            }

            List<Thing> wiped = ThingsWipedBy(designator, new List<IntVec3> { cell }, map);
            if (wiped.Count == 0)
            {
                Disarm();
                return true;
            }

            if (armedCell == cell && armedDesignator == designator)
            {
                Disarm();
                return true;
            }

            armedCell = cell;
            armedDesignator = designator;
            TolkHelper.SpeakData(
                "RimWorldAccess.Building.Place.WillReplaceConfirm".Translate(Summarize(wiped)));
            return false;
        }

        private static void Disarm()
        {
            armedCell = null;
            armedDesignator = null;
        }
    }
}
