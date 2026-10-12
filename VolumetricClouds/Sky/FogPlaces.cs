using System;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Where fog gathers on its own (1.5.0): the hollows of the ground ("Fog fills valleys") and
    /// the water ("Fog over water"), worked out from the terrain map's heights. TerrainHeightMap puts
    /// both in its place texture, read by the fog march (CloudMedia FogAt).
    /// </summary>
    /// <remarks>
    /// Pure, no Unity types: tools/test-fogplaces.ps1 compiles this file on its own. The maps are
    /// square, n x n, row by row, in the terrain map's layout.
    /// </remarks>
    public static class FogPlaces
    {
        /// <summary>
        /// How far round a point the land is averaged to say how far the ground lies below it: about
        /// 1.5 km, a river valley's width. A box of this radius, twice: a soft-edged average.
        /// </summary>
        public const float HollowRadiusMetres = 1500f;

        /// <summary>How far the water's edge is softened, metres: the banks thin out over about this.</summary>
        public const float WaterBlurMetres = 340f;

        /// <summary>Water shallower than this counts as none (the terrain's own rounding).</summary>
        public const float WaterDepthStart = 0.2f;

        /// <summary>Water this much deeper than <see cref="WaterDepthStart"/> counts in full.</summary>
        public const float WaterDepthFull = 1f;

        /// <summary>The deepest hollow a map texel can hold, metres (one step a metre).</summary>
        public const float HollowMost = 255f;

        /// <summary>Texels for a distance in metres on a map of n texels over <paramref name="mapSize"/> metres; at least 1.</summary>
        public static int Texels(float metres, int n, float mapSize)
        {
            return Math.Max(1, (int)Math.Round(metres * n / mapSize));
        }

        /// <summary>
        /// How far the ground lies below the land round it, metres, never negative: the soft average
        /// of the heights within <paramref name="radius"/> texels, less the height here. A flat plain
        /// is 0, a valley floor is deep, a ridge or a hilltop is 0. Water counts as ground: a lake is
        /// its valley's floor.
        /// </summary>
        public static float[] Hollows(float[] heights, int n, int radius)
        {
            float[] blurred = SoftAverage(heights, n, radius);
            for (int i = 0; i < blurred.Length; i++)
                blurred[i] = Math.Max(0f, blurred[i] - heights[i]);

            return blurred;
        }

        /// <summary>
        /// How much of the ground round each point is water, 0..1: deeper than
        /// <see cref="WaterDepthStart"/> counts, fully past another <see cref="WaterDepthFull"/>; then
        /// softened over <paramref name="radius"/> texels, so the shore is about a half.
        /// </summary>
        public static float[] Water(float[] withWater, float[] ground, int n, int radius)
        {
            var water = new float[withWater.Length];
            for (int i = 0; i < water.Length; i++)
            {
                float depth = withWater[i] - ground[i] - WaterDepthStart;
                water[i] = depth <= 0f ? 0f : Math.Min(1f, depth / WaterDepthFull);
            }

            return SoftAverage(water, n, radius);
        }

        /// <summary>The largest value; 0 for an empty map.</summary>
        public static float Max(float[] values)
        {
            float most = 0f;
            for (int i = 0; i < values.Length; i++)
                most = Math.Max(most, values[i]);

            return most;
        }

        /// <summary>The mean value; 0 for an empty map.</summary>
        public static float Mean(float[] values)
        {
            if (values.Length == 0)
                return 0f;

            double sum = 0;
            for (int i = 0; i < values.Length; i++)
                sum += values[i];

            return (float)(sum / values.Length);
        }

        /// <summary>A texel's byte for the hollow: metres, rounded, 0..255.</summary>
        public static byte HollowByte(float metres)
        {
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(metres)));
        }

        /// <summary>A texel's byte for a 0..1 share.</summary>
        public static byte ShareByte(float share)
        {
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(share * 255f)));
        }

        /// <summary>
        /// A box average of <paramref name="radius"/> texels, run twice along each axis: a tent, without
        /// a box's hard edges. Off the map's edge the edge row repeats (the shader clamps there too).
        /// </summary>
        public static float[] SoftAverage(float[] values, int n, int radius)
        {
            var a = (float[])values.Clone();
            var b = new float[values.Length];

            for (int pass = 0; pass < 2; pass++)
            {
                BoxRows(a, b, n, radius);
                BoxColumns(b, a, n, radius);
            }

            return a;
        }

        private static void BoxRows(float[] from, float[] to, int n, int radius)
        {
            float width = 2 * radius + 1;
            for (int z = 0; z < n; z++)
            {
                int row = z * n;
                double sum = 0;
                for (int k = -radius; k <= radius; k++)
                    sum += from[row + Clamp(k, n)];

                for (int x = 0; x < n; x++)
                {
                    to[row + x] = (float)(sum / width);
                    sum += from[row + Clamp(x + radius + 1, n)] - from[row + Clamp(x - radius, n)];
                }
            }
        }

        private static void BoxColumns(float[] from, float[] to, int n, int radius)
        {
            float width = 2 * radius + 1;
            for (int x = 0; x < n; x++)
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++)
                    sum += from[Clamp(k, n) * n + x];

                for (int z = 0; z < n; z++)
                {
                    to[z * n + x] = (float)(sum / width);
                    sum += from[Clamp(z + radius + 1, n) * n + x] - from[Clamp(z - radius, n) * n + x];
                }
            }
        }

        private static int Clamp(int i, int n)
        {
            return i < 0 ? 0 : i >= n ? n - 1 : i;
        }
    }
}
