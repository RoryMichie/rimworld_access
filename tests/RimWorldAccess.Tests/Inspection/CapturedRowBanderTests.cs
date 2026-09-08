using System.Collections.Generic;
using RimWorldAccess;
using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Inspection
{
    public class CapturedRowBanderTests
    {
        private static List<List<int>> Band(params (float x, float y)[] geoms)
        {
            var list = new List<BandGeom>();
            foreach ((float x, float y) g in geoms)
            {
                list.Add(new BandGeom(g.x, g.y));
            }
            return CapturedRowBander.Band(list);
        }

        /// <summary>One widget with real clip context: local left edge, screen top edge, and the clip it was drawn under.</summary>
        private static BandGeom Clipped(float x, float screenY, int clipDepth, float clipHeight = 200f)
        {
            return new BandGeom(x, screenY, new TipClip(new TipRect(0f, 0f, 500f, clipHeight), clipDepth), true);
        }

        [Fact]
        public void EmptyStream_YieldsNoBands()
        {
            Assert.Empty(CapturedRowBander.Band(new List<BandGeom>()));
            Assert.Empty(CapturedRowBander.Band(null));
        }

        [Fact]
        public void SingleWidget_YieldsOneBand()
        {
            List<List<int>> bands = Band((10f, 0f));
            Assert.Single(bands);
            Assert.Equal(new[] { 0 }, bands[0]);
        }

        [Fact]
        public void SameTopEdge_MergesAndSortsLeftToRight()
        {
            // The Gear tab draws each row's mass label (x=292) BEFORE its
            // name label (x=36); reading order must flip them.
            List<List<int>> bands = Band((292f, 100f), (36f, 100f));
            Assert.Single(bands);
            Assert.Equal(new[] { 1, 0 }, bands[0]);
        }

        [Fact]
        public void WithinTolerance_CountsAsSameRow()
        {
            List<List<int>> bands = Band((0f, 100f), (200f, 102f), (400f, 98f));
            Assert.Single(bands);
            Assert.Equal(new[] { 0, 1, 2 }, bands[0]);
        }

        [Fact]
        public void BeyondTolerance_StartsNewBand()
        {
            List<List<int>> bands = Band((0f, 100f), (0f, 128f));
            Assert.Equal(2, bands.Count);
        }

        [Fact]
        public void EqualYAcrossNonAdjacentWidgets_NeverMerges()
        {
            // Group-local rects: a widget in a later GUI group can repeat an
            // earlier Y. Only CONSECUTIVE records may share a band.
            List<List<int>> bands = Band((0f, 0f), (0f, 100f), (0f, 0f));
            Assert.Equal(3, bands.Count);
            Assert.Equal(new[] { 0 }, bands[0]);
            Assert.Equal(new[] { 1 }, bands[1]);
            Assert.Equal(new[] { 2 }, bands[2]);
        }

        [Fact]
        public void EqualX_KeepsDrawOrder()
        {
            List<List<int>> bands = Band((50f, 10f), (50f, 10f), (20f, 10f));
            Assert.Single(bands);
            Assert.Equal(new[] { 2, 0, 1 }, bands[0]);
        }

        [Fact]
        public void BandsKeepDrawOrderAcrossRows()
        {
            List<List<int>> bands = Band((0f, 0f), (0f, 30f), (100f, 30f), (0f, 60f));
            Assert.Equal(3, bands.Count);
            Assert.Equal(new[] { 0 }, bands[0]);
            Assert.Equal(new[] { 1, 2 }, bands[1]);
            Assert.Equal(new[] { 3 }, bands[2]);
        }

        [Fact]
        public void AdjacentWidgetsInDifferentClipContexts_NeverMerge()
        {
            // Page_ConfigureStartingPawns: the "Selected" section header draws in
            // the outer scroll view, each pawn's name label inside its own nested
            // per-row group, and both land on the same local Y — "Selected,
            // O'Brien" as one row.
            List<List<int>> bands = CapturedRowBander.Band(new List<BandGeom>
            {
                Clipped(0f, 300f, clipDepth: 1),
                Clipped(0f, 340f, clipDepth: 3),
            });
            Assert.Equal(2, bands.Count);
        }

        [Fact]
        public void SameClipButDifferentScreenRow_NeverMerges()
        {
            // Two per-row groups of the SAME SIZE produce identical clip keys
            // (GUIClip.visibleRect is group-local) and both restart their local
            // origin at 0, so only the screen edge tells their rows apart.
            List<List<int>> bands = CapturedRowBander.Band(new List<BandGeom>
            {
                Clipped(0f, 300f, clipDepth: 2),
                Clipped(120f, 330f, clipDepth: 2),
            });
            Assert.Equal(2, bands.Count);
        }

        [Fact]
        public void SameClipAndSameScreenRow_Merges()
        {
            // Local origins may disagree — two groups at different offsets can
            // reach the same absolute row — so only the screen edge is compared.
            List<List<int>> bands = CapturedRowBander.Band(new List<BandGeom>
            {
                Clipped(120f, 300f, clipDepth: 2),
                Clipped(20f, 302f, clipDepth: 2),
            });
            Assert.Single(bands);
            Assert.Equal(new[] { 1, 0 }, bands[0]);
        }

        [Fact]
        public void MissingClipContext_JudgedByScreenProximityAlone()
        {
            // GuiSpace's reflection bindings degraded: no widget carries context,
            // so screen proximity is the only usable test — and equal local Ys in
            // unrelated groups still separate, because their screen edges differ.
            var unclipped = new List<BandGeom>
            {
                new BandGeom(0f, 300f, default(TipClip), false),
                new BandGeom(0f, 340f, default(TipClip), false),
                new BandGeom(200f, 342f, default(TipClip), false),
            };
            List<List<int>> bands = CapturedRowBander.Band(unclipped);
            Assert.Equal(2, bands.Count);
            Assert.Equal(new[] { 0 }, bands[0]);
            Assert.Equal(new[] { 1, 2 }, bands[1]);
        }

        [Fact]
        public void ToleranceAppliesToTheScreenEdge()
        {
            Assert.Single(CapturedRowBander.Band(new List<BandGeom>
            {
                Clipped(0f, 300f, clipDepth: 1),
                Clipped(200f, 305f, clipDepth: 1),
            }));
            Assert.Equal(2, CapturedRowBander.Band(new List<BandGeom>
            {
                Clipped(0f, 300f, clipDepth: 1),
                Clipped(200f, 307f, clipDepth: 1),
            }).Count);
        }

        [Fact]
        public void ToleranceAnchorsOnBandFirstMember_NoDrift()
        {
            // A slow Y drift must not chain rows together: membership compares
            // against the band's FIRST member, not the previous record.
            List<List<int>> bands = Band((0f, 0f), (0f, 5f), (0f, 10f));
            Assert.Equal(2, bands.Count);
            Assert.Equal(new[] { 0, 1 }, bands[0]);
            Assert.Equal(new[] { 2 }, bands[1]);
        }
    }
}
