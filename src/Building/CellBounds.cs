namespace RimWorldAccess
{
    /// <summary>
    /// The min/max extents of a cell run. Split out from the rect-designation path so the
    /// arithmetic is covered by tests without the game assemblies.
    /// </summary>
    public static class CellBounds
    {
        /// <summary>Folds one more cell's x/z into the running extents.</summary>
        public static void Accumulate(int x, int z, ref int minX, ref int minZ, ref int maxX, ref int maxZ, ref bool any)
        {
            if (!any)
            {
                any = true;
                minX = maxX = x;
                minZ = maxZ = z;
                return;
            }

            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (z < minZ) minZ = z;
            if (z > maxZ) maxZ = z;
        }
    }
}
