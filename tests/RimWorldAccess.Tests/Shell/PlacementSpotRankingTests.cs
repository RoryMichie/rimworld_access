using System.Collections.Generic;
using RimWorldAccess;

namespace RimWorldAccess.Tests.Shell
{
    public class PlacementSpotRankingTests
    {
        // Rot4.AsInt values, the form the pure layer carries facings in.
        private const int North = 0;
        private const int East = 1;
        private const int South = 2;
        private const int West = 3;

        private static PlacementSpot Spot(float distance, int? requiredFacing = null)
        {
            return new PlacementSpot(0, (int)distance, distance, requiredFacing);
        }

        [Fact]
        public void SelectCapsTheListAtMaxSpots()
        {
            var candidates = new List<PlacementSpot>();
            for (int i = 0; i < PlacementSpotRanking.MaxSpots * 3; i++)
                candidates.Add(Spot(i));

            var selected = PlacementSpotRanking.Select(candidates);

            Assert.Equal(PlacementSpotRanking.MaxSpots, selected.Count);
        }

        [Fact]
        public void SelectKeepsTheNearestCandidatesWhenTheCapBites()
        {
            var candidates = new List<PlacementSpot>();
            for (int i = PlacementSpotRanking.MaxSpots * 2; i >= 0; i--)
                candidates.Add(Spot(i));

            var selected = PlacementSpotRanking.Select(candidates);

            Assert.Equal(0f, selected[0].Distance);
            Assert.Equal(PlacementSpotRanking.MaxSpots - 1, selected[selected.Count - 1].Distance);
        }

        [Fact]
        public void SelectOrdersNearestFirst()
        {
            var selected = PlacementSpotRanking.Select(new List<PlacementSpot>
            {
                Spot(9f), Spot(1f), Spot(4.5f), Spot(2f),
            });

            Assert.Equal(new[] { 1f, 2f, 4.5f, 9f }, selected.ConvertAll(s => s.Distance).ToArray());
        }

        [Fact]
        public void SelectKeepsTheSweepOrderForEqualDistances()
        {
            var selected = PlacementSpotRanking.Select(new List<PlacementSpot>
            {
                new PlacementSpot(5, 0, 3f, null),
                new PlacementSpot(-5, 0, 3f, null),
                new PlacementSpot(0, 3, 3f, null),
            });

            Assert.Equal(new[] { 5, -5, 0 }, selected.ConvertAll(s => s.X).ToArray());
        }

        [Fact]
        public void SelectToleratesNoCandidates()
        {
            Assert.Empty(PlacementSpotRanking.Select(null));
            Assert.Empty(PlacementSpotRanking.Select(new List<PlacementSpot>()));
        }

        [Fact]
        public void ACellThatFitsAtTheCurrentRotationNeedsNoFacing()
        {
            bool isSpot = PlacementSpotRanking.TryChooseFacing(East, new[] { East }, out int? required);

            Assert.True(isSpot);
            Assert.Null(required);
        }

        [Fact]
        public void TheCurrentRotationWinsEvenWhenOthersAlsoFit()
        {
            bool isSpot = PlacementSpotRanking.TryChooseFacing(South, new[] { North, South, West }, out int? required);

            Assert.True(isSpot);
            Assert.Null(required);
        }

        [Fact]
        public void ACellThatFitsOnlySidewaysReportsThatFacing()
        {
            bool isSpot = PlacementSpotRanking.TryChooseFacing(North, new[] { West, East }, out int? required);

            Assert.True(isSpot);
            Assert.Equal(West, required);
        }

        [Fact]
        public void ACellNothingFitsInIsNotASpot()
        {
            Assert.False(PlacementSpotRanking.TryChooseFacing(North, new int[0], out int? required));
            Assert.Null(required);

            Assert.False(PlacementSpotRanking.TryChooseFacing(North, null, out required));
            Assert.Null(required);
        }
    }
}
