using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// One legal position for the building being placed: its map coordinates, its distance from
    /// the cursor at sweep time, and the facing the building must be turned to for it to fit
    /// there (null when the rotation the player is already holding fits).
    /// Facings are <c>Rot4.AsInt</c> values so this carrier stays free of game types.
    /// </summary>
    public readonly struct PlacementSpot
    {
        public readonly int X;
        public readonly int Z;
        public readonly float Distance;
        public readonly int? RequiredFacing;

        public PlacementSpot(int x, int z, float distance, int? requiredFacing)
        {
            X = x;
            Z = z;
            Distance = distance;
            RequiredFacing = requiredFacing;
        }
    }

    /// <summary>
    /// The cap, the ordering and the facing choice behind the placement-spot scanner category.
    /// Free of game types so the rules are unit-tested; the map sweep that feeds it lives in
    /// PlacementSpotScanner.Game.cs.
    /// </summary>
    public static class PlacementSpotRanking
    {
        /// <summary>How many positions the category lists. The one place to revisit the cap.</summary>
        public const int MaxSpots = 25;

        /// <summary>
        /// How far from the cursor the sweep looks before giving up. Radial cells arrive
        /// nearest-first, so the sweep normally stops at <see cref="MaxSpots"/> long before this;
        /// the radius only bounds the hopeless case where almost nothing fits. Must stay under
        /// GenRadial.MaxRadialPatternRadius (~79 with the 20,000-cell pattern) or the sweep
        /// silently truncates.
        /// </summary>
        public const float SearchRadius = 60f;

        /// <summary>
        /// How far the cursor may drift from the last sweep's origin before the category is
        /// re-swept from the new position. Half the radius keeps the listed spots relevant
        /// wherever the player wanders without paying for a sweep on every step.
        /// </summary>
        public const float ResweepDistance = SearchRadius / 2f;

        /// <summary>
        /// Orders candidates nearest-first and cuts the list to <see cref="MaxSpots"/>.
        /// The sort is stable, so candidates at equal distance keep the sweep's own radial order.
        /// </summary>
        public static List<PlacementSpot> Select(IEnumerable<PlacementSpot> candidates)
        {
            var ordered = new List<PlacementSpot>();
            if (candidates == null)
                return ordered;

            ordered.AddRange(candidates);
            StableSortByDistance(ordered);

            if (ordered.Count > MaxSpots)
                ordered.RemoveRange(MaxSpots, ordered.Count - MaxSpots);

            return ordered;
        }

        /// <summary>
        /// Decides what facing a cell needs, given every facing the building fits at there.
        /// Returns false when nothing fits — the cell is not a placement spot at all.
        /// <paramref name="requiredFacing"/> stays null when the current facing is among the
        /// fitting ones, so a spot that already works is announced without a facing fragment.
        /// </summary>
        public static bool TryChooseFacing(int currentFacing, IReadOnlyList<int> fittingFacings, out int? requiredFacing)
        {
            requiredFacing = null;
            if (fittingFacings == null || fittingFacings.Count == 0)
                return false;

            for (int i = 0; i < fittingFacings.Count; i++)
            {
                if (fittingFacings[i] == currentFacing)
                    return true;
            }

            requiredFacing = fittingFacings[0];
            return true;
        }

        // List<T>.Sort is unstable, and equal-distance ties must keep the radial order the sweep
        // produced (which is already nearest-first and deterministic).
        private static void StableSortByDistance(List<PlacementSpot> spots)
        {
            for (int i = 1; i < spots.Count; i++)
            {
                PlacementSpot spot = spots[i];
                int j = i - 1;
                while (j >= 0 && spots[j].Distance > spot.Distance)
                {
                    spots[j + 1] = spots[j];
                    j--;
                }
                spots[j + 1] = spot;
            }
        }
    }
}
