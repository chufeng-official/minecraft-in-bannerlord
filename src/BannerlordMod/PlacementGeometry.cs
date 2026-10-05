using System;

namespace BannerlordBlocks
{
    internal static class PlacementGeometry
    {
        internal static float GroundCenter(float ground, float baseHeight)
        {
            if (float.IsNaN(ground) || float.IsInfinity(ground) ||
                float.IsNaN(baseHeight) || float.IsInfinity(baseHeight))
                throw new ArgumentOutOfRangeException(nameof(ground));
            // Snap upwards to avoid burying blocks. Tolerate sub-millimetre float error.
            return baseHeight + (float)Math.Ceiling(ground - baseHeight - 0.001f) + 0.5f;
        }

        internal static float CellCenter(float position)
        {
            if (float.IsNaN(position) || float.IsInfinity(position))
                throw new ArgumentOutOfRangeException(nameof(position));
            return (float)Math.Floor(position) + 0.5f;
        }

        // Axis-aligned cube: largest normalized displacement identifies the hit face.
        // Edge/corner ties choose X, then Y, then Z deterministically.
        internal static void FaceOffset(float x, float y, float z, out int axis, out int sign)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) ||
                float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z))
                throw new ArgumentOutOfRangeException("hit");
            axis = 0;
            float coordinate = x;
            if (Math.Abs(y) > Math.Abs(coordinate)) { axis = 1; coordinate = y; }
            if (Math.Abs(z) > Math.Abs(coordinate)) { axis = 2; coordinate = z; }
            sign = coordinate < 0 ? -1 : 1;
        }
    }
}
