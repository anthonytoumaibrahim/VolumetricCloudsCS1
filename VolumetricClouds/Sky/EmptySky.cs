using System;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// SKIP THE EMPTY SKY (1.5.0): per cell of the weather map, the most cloud it can hold, so the
    /// cloud march can jump over sky where no sample could find any. Same picture by construction:
    /// only samples that would have returned exactly 0 are skipped, and the ones taken stay where
    /// they were (whole steps).
    /// </summary>
    /// <remarks>
    /// Pure, no Unity types: tools/test-emptysky.ps1 compiles this file on its own.
    ///
    /// The map is n / <see cref="CellTexels"/> square (96 for the 384 weather map), one texel a FINE
    /// cell of 4 x 4 weather texels (140 m at the default 13.5 km tile), holding four bytes:
    ///   r = the most weather in the fine cell, as the GPU reads it, rounded UP, plus one step;
    ///   g = the same over its COARSE cell (<see cref="CoarseCells"/> x that: 560 m), repeated in each;
    ///   b, a = the same two for the Cumulus style's normal score (linear: exact).
    /// Each fine cell takes the texels round it too: bilinear filtering reads the neighbours, so a
    /// point anywhere in the cell reads nothing above its maximum. Both wrap like the weather map.
    /// Whether the GPU gamma-decodes the weather texture is measured (CloudDensityField); decoding is
    /// monotonic, so the most decoded value is the decoded most -- the cell maxima are of the STORED
    /// bytes, decoded once at the end.
    /// </remarks>
    public static class EmptySky
    {
        /// <summary>Weather texels along one side of a fine cell.</summary>
        public const int CellTexels = 4;

        /// <summary>Fine cells along one side of a coarse cell.</summary>
        public const int CoarseCells = 4;

        /// <summary>
        /// The most of <paramref name="texels"/> (n x n, row by row, wrapping) in each cell of
        /// <paramref name="cell"/> x <paramref name="cell"/> texels and one texel round it.
        /// </summary>
        public static byte[] CellMax(byte[] texels, int n, int cell)
        {
            int m = n / cell;
            var max = new byte[m * m];

            for (int cy = 0; cy < m; cy++)
            {
                for (int cx = 0; cx < m; cx++)
                {
                    byte best = 0;
                    for (int y = cy * cell - 1; y <= (cy + 1) * cell; y++)
                    {
                        int row = Wrap(y, n) * n;
                        for (int x = cx * cell - 1; x <= (cx + 1) * cell; x++)
                        {
                            byte v = texels[row + Wrap(x, n)];
                            if (v > best)
                                best = v;
                        }
                    }

                    max[cy * m + cx] = best;
                }
            }

            return max;
        }

        /// <summary>
        /// Each fine cell's coarse cell's most: the largest of the <paramref name="factor"/> x
        /// <paramref name="factor"/> fine cells it is one of (whose maxima already take the texels round
        /// them). Same layout as <paramref name="fine"/>, m x m.
        /// </summary>
        public static byte[] Coarse(byte[] fine, int m, int factor)
        {
            var coarse = new byte[fine.Length];

            for (int by = 0; by < m; by += factor)
            {
                for (int bx = 0; bx < m; bx += factor)
                {
                    byte best = 0;
                    for (int y = by; y < Math.Min(by + factor, m); y++)
                        for (int x = bx; x < Math.Min(bx + factor, m); x++)
                            best = Math.Max(best, fine[y * m + x]);

                    for (int y = by; y < Math.Min(by + factor, m); y++)
                        for (int x = bx; x < Math.Min(bx + factor, m); x++)
                            coarse[y * m + x] = best;
                }
            }

            return coarse;
        }

        /// <summary>
        /// A stored weather byte as the shader reads it, rounded UP to a byte and one step more: the
        /// GPU's own decoding may differ from this formula in the last bit, and a bound must never be
        /// below the value it bounds. 255 stays 255 (nothing is above it).
        /// </summary>
        public static byte AsDrawnUp(byte stored, bool srgb)
        {
            if (stored >= 255)
                return 255;

            double v = stored / 255.0;
            if (srgb)
                v = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

            int up = (int)Math.Ceiling(v * 255.0 - 1e-9) + 1;
            return (byte)Math.Min(255, up);
        }

        /// <summary>
        /// The map's texels, four bytes each (r, g, b, a as in the remarks), m x m, from the weather
        /// texture's stored bytes and the score texture's, both n x n.
        /// </summary>
        public static byte[] Build(byte[] weather, byte[] score, int n, bool srgb)
        {
            int m = n / CellTexels;
            byte[] weatherFine = CellMax(weather, n, CellTexels);
            byte[] scoreFine = CellMax(score, n, CellTexels);
            byte[] weatherCoarse = Coarse(weatherFine, m, CoarseCells);
            byte[] scoreCoarse = Coarse(scoreFine, m, CoarseCells);

            var rgba = new byte[m * m * 4];
            for (int i = 0; i < m * m; i++)
            {
                rgba[i * 4] = AsDrawnUp(weatherFine[i], srgb);
                rgba[i * 4 + 1] = AsDrawnUp(weatherCoarse[i], srgb);
                rgba[i * 4 + 2] = scoreFine[i];
                rgba[i * 4 + 3] = scoreCoarse[i];
            }

            return rgba;
        }

        /// <summary>
        /// The share of fine cells the Classic layer leaves empty at this threshold (the shader's test:
        /// most weather at or under it), 0..1. For the log and for whether the skip is worth asking.
        /// </summary>
        public static float EmptyShare(byte[] rgba, float threshold)
        {
            return EmptyShare(rgba, threshold, false, 0f, 0f);
        }

        /// <summary>
        /// The same with the Cumulus style's heaps too (<paramref name="cumulus"/>): a cell is empty
        /// only if their coverage line, <paramref name="lineA"/> + <paramref name="lineB"/> x score, is
        /// at or under 0 at its highest score -- the shader's test, CloudMedia CellEmpty.
        /// </summary>
        public static float EmptyShare(byte[] rgba, float threshold, bool cumulus, float lineA, float lineB)
        {
            int cells = rgba.Length / 4;
            if (cells == 0)
                return 0f;

            int empty = 0;
            for (int i = 0; i < cells; i++)
            {
                if (rgba[i * 4] / 255f > threshold)
                    continue;

                if (cumulus && !(lineB >= 0f && lineA + lineB * (rgba[i * 4 + 2] / 255f) <= 0f))
                    continue;

                empty++;
            }

            return empty / (float)cells;
        }

        /// <summary>
        /// The Mixed style (Sky.CloudMix): the map's bytes with the score channels -- which only the
        /// Cumulus heaps need -- replaced by where its separate clouds reach (CloudMix.Baker.Presence,
        /// 255 or 0 per fine cell, m x m): b = the fine cell's, a = its coarse cell's. r, g, the weather,
        /// as they were. A new array; <paramref name="rgba"/> is not changed.
        /// </summary>
        public static byte[] WithPresence(byte[] rgba, byte[] presence, int m)
        {
            byte[] coarse = Coarse(presence, m, CoarseCells);
            var result = (byte[])rgba.Clone();
            for (int i = 0; i < m * m; i++)
            {
                result[i * 4 + 2] = presence[i];
                result[i * 4 + 3] = coarse[i];
            }

            return result;
        }

        /// <summary>
        /// The share of fine cells empty under the Mixed style: the most weather at or under the layer's
        /// LOWEST threshold anywhere (its type varies it), and no separate cloud reaching the cell -- the
        /// shader's test, CloudMedia CellEmpty. <paramref name="rgba"/>: <see cref="WithPresence"/>'s.
        /// </summary>
        public static float EmptyShareMixed(byte[] rgba, float lowestThreshold)
        {
            int cells = rgba.Length / 4;
            if (cells == 0)
                return 0f;

            int empty = 0;
            for (int i = 0; i < cells; i++)
            {
                if (rgba[i * 4] / 255f <= lowestThreshold && rgba[i * 4 + 2] == 0)
                    empty++;
            }

            return empty / (float)cells;
        }

        private static int Wrap(int v, int n)
        {
            int r = v % n;
            return r < 0 ? r + n : r;
        }
    }
}
