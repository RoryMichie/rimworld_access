using RimWorldAccess;

namespace RimWorldAccess.Tests.Shell
{
    public class CellBoundsTests
    {
        private static (int MinX, int MinZ, int MaxX, int MaxZ, bool Any) Fold(params (int X, int Z)[] cells)
        {
            int minX = 0, minZ = 0, maxX = 0, maxZ = 0;
            bool any = false;
            foreach (var (x, z) in cells)
            {
                CellBounds.Accumulate(x, z, ref minX, ref minZ, ref maxX, ref maxZ, ref any);
            }
            return (minX, minZ, maxX, maxZ, any);
        }

        [Fact]
        public void SingleCellIsItsOwnBoundingBox()
        {
            var bounds = Fold((7, 12));

            Assert.True(bounds.Any);
            Assert.Equal(7, bounds.MinX);
            Assert.Equal(7, bounds.MaxX);
            Assert.Equal(12, bounds.MinZ);
            Assert.Equal(12, bounds.MaxZ);
        }

        [Fact]
        public void ScatteredCellsYieldTheBoundingBoxNotTheFirstOrLast()
        {
            var bounds = Fold((5, 9), (2, 30), (11, 4));

            Assert.Equal(2, bounds.MinX);
            Assert.Equal(11, bounds.MaxX);
            Assert.Equal(4, bounds.MinZ);
            Assert.Equal(30, bounds.MaxZ);
        }

        [Fact]
        public void NegativeCoordinatesFoldCorrectly()
        {
            var bounds = Fold((-3, -8), (4, -1), (-10, 6));

            Assert.Equal(-10, bounds.MinX);
            Assert.Equal(4, bounds.MaxX);
            Assert.Equal(-8, bounds.MinZ);
            Assert.Equal(6, bounds.MaxZ);
        }

        [Fact]
        public void ZeroCellsLeaveAnyFalseAndExtentsUntouched()
        {
            int minX = 42, minZ = 43, maxX = 44, maxZ = 45;
            bool any = false;

            foreach (var (x, z) in new (int X, int Z)[0])
            {
                CellBounds.Accumulate(x, z, ref minX, ref minZ, ref maxX, ref maxZ, ref any);
            }

            Assert.False(any);
            Assert.Equal(42, minX);
            Assert.Equal(43, minZ);
            Assert.Equal(44, maxX);
            Assert.Equal(45, maxZ);
        }
    }
}
