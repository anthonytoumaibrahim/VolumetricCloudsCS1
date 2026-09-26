using System;
using System.Threading;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// A tiling cube of "spherical" Worley noise with its whole mip chain: the generator behind
    /// cloud detail's billows (<see cref="CloudDetail3D"/>) and the Cumulus style's clouds
    /// (<see cref="CumulusNoise3D"/>), which differ only in their octaves.
    /// </summary>
    /// <remarks>
    /// Each octave is Worley noise with the round profile sqrt(1 - d^2) instead of the cone 1 - d:
    /// EVE-Redux V5's "spherical" Worley, "billowy and puffy". Octave o has period
    /// basePeriod x 2^o cells across the cube and weight persistence^o. The sum is stretched to
    /// 0..255 over the cube's own range and box-filtered down to 1^3.
    ///
    /// Pure System.Math on plain arrays -- no UnityEngine calls -- so it runs on worker threads and
    /// offline in the tools' tests. Its output is part of what a city's seed reproduces
    /// (invariant 14): the callers carry a Generator number for that.
    /// </remarks>
    public static class WorleyVolume
    {
        /// <summary>
        /// Octaves up to this period keep their feature points in a table (at most 32^3 of them);
        /// finer ones hash each cell as it is visited, the same numbers without the memory.
        /// </summary>
        private const int TabledPeriod = 32;

        /// <summary>
        /// The mip chain, level 0 first: <paramref name="size"/>^3 bytes, then each level half the
        /// edge, down to 1^3. Index x + y * n + z * n * n. <paramref name="size"/> must be a power
        /// of two. Split over <paramref name="threads"/> worker threads (0 = one per core, up to 8);
        /// the result does not depend on how many.
        /// </summary>
        public static byte[][] Generate(int size, int seed, int threads, int octaves, int basePeriod, float persistence, string threadName)
        {
            if (size < 1 || (size & (size - 1)) != 0)
                throw new ArgumentException("size must be a power of two", "size");
            if (octaves < 1 || basePeriod < 1)
                throw new ArgumentException("at least one octave, of at least one cell");

            var layers = new Octave[octaves];
            float amplitude = 1f;
            for (int o = 0; o < octaves; o++)
            {
                int period = basePeriod << o;
                layers[o] = new Octave
                {
                    Period = period,
                    Seed = seed + 101 * o,
                    Weight = amplitude,
                    Points = period <= TabledPeriod ? PlacePoints(period, seed + 101 * o) : null,
                };
                amplitude *= persistence;
            }

            float[] values = new float[size * size * size];

            if (threads <= 0)
                threads = Math.Min(8, Math.Max(1, Environment.ProcessorCount));
            threads = Math.Min(threads, size);

            Exception error = null;
            Thread[] workers = new Thread[threads];
            for (int w = 0; w < threads; w++)
            {
                int first = size * w / threads;
                int last = size * (w + 1) / threads;
                workers[w] = new Thread(() =>
                {
                    try
                    {
                        Fill(values, size, layers, first, last);
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }
                })
                {
                    IsBackground = true,
                    Name = threadName,
                };
                workers[w].Start();
            }

            for (int w = 0; w < threads; w++)
                workers[w].Join();

            if (error != null)
                throw error;

            return Levels(Normalise(values), size);
        }

        private sealed class Octave
        {
            public int Period;
            public int Seed;
            public float Weight;

            /// <summary>One feature point per cell, xyz in [0, 1) of the cell; null = hashed on the fly.</summary>
            public float[] Points;
        }

        /// <summary>Every cell's feature point, placed once: the volume then only measures distances.</summary>
        private static float[] PlacePoints(int period, int seed)
        {
            float[] p = new float[period * period * period * 3];
            for (int z = 0; z < period; z++)
                for (int y = 0; y < period; y++)
                    for (int x = 0; x < period; x++)
                    {
                        int h = HashInt(x, y, z, seed);
                        int i = (x + y * period + z * period * period) * 3;
                        p[i] = (h & 1023) / 1024f;
                        p[i + 1] = ((h >> 10) & 1023) / 1024f;
                        p[i + 2] = ((h >> 20) & 1023) / 1024f;
                    }
            return p;
        }

        /// <summary>The z-slices [first, last) of the volume.</summary>
        private static void Fill(float[] values, int size, Octave[] layers, int first, int last)
        {
            float inv = 1f / size;

            for (int z = first; z < last; z++)
            {
                for (int y = 0; y < size; y++)
                {
                    int row = y * size + z * size * size;
                    for (int x = 0; x < size; x++)
                    {
                        float sum = 0f, total = 0f;

                        for (int o = 0; o < layers.Length; o++)
                        {
                            Octave layer = layers[o];
                            int period = layer.Period;
                            float d = Distance(x * inv * period, y * inv * period, z * inv * period, period, layer);

                            // The round profile: a sphere's height over its disc, not a cone.
                            sum += (float)Math.Sqrt(Math.Max(0f, 1f - d * d)) * layer.Weight;
                            total += layer.Weight;
                        }

                        values[row + x] = sum / total;
                    }
                }
            }
        }

        /// <summary>Distance to the nearest feature point, in cells, capped at 1 (tiling).</summary>
        private static float Distance(float x, float y, float z, int period, Octave layer)
        {
            int xi = (int)Math.Floor(x);
            int yi = (int)Math.Floor(y);
            int zi = (int)Math.Floor(z);
            float best = float.MaxValue;
            float[] points = layer.Points;

            for (int dz = -1; dz <= 1; dz++)
            {
                int cz = Wrap(zi + dz, period);
                for (int dy = -1; dy <= 1; dy++)
                {
                    int cy = Wrap(yi + dy, period);
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int cx = Wrap(xi + dx, period);

                        float px, py, pz;
                        if (points != null)
                        {
                            int i = (cx + cy * period + cz * period * period) * 3;
                            px = points[i];
                            py = points[i + 1];
                            pz = points[i + 2];
                        }
                        else
                        {
                            int h = HashInt(cx, cy, cz, layer.Seed);
                            px = (h & 1023) / 1024f;
                            py = ((h >> 10) & 1023) / 1024f;
                            pz = ((h >> 20) & 1023) / 1024f;
                        }

                        float ox = xi + dx + px - x;
                        float oy = yi + dy + py - y;
                        float oz = zi + dz + pz - z;
                        float d = ox * ox + oy * oy + oz * oz;
                        if (d < best)
                            best = d;
                    }
                }
            }

            float distance = (float)Math.Sqrt(best);
            return distance > 1f ? 1f : distance;
        }

        /// <summary>0..1 over the volume's own range, as bytes.</summary>
        private static byte[] Normalise(float[] values)
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < min)
                    min = values[i];
                if (values[i] > max)
                    max = values[i];
            }

            float span = max - min;
            float scale = span > 1e-6f ? 255f / span : 0f;

            byte[] bytes = new byte[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                int v = (int)((values[i] - min) * scale + 0.5f);
                bytes[i] = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
            }

            return bytes;
        }

        /// <summary>Level 0 and every box-filtered level under it, down to 1^3.</summary>
        private static byte[][] Levels(byte[] top, int size)
        {
            int count = 1;
            for (int s = size; s > 1; s >>= 1)
                count++;

            byte[][] levels = new byte[count][];
            levels[0] = top;

            int n = size;
            for (int l = 1; l < count; l++)
            {
                int m = n >> 1;
                byte[] src = levels[l - 1];
                byte[] dst = new byte[m * m * m];

                for (int z = 0; z < m; z++)
                    for (int y = 0; y < m; y++)
                        for (int x = 0; x < m; x++)
                        {
                            int sx = x * 2, sy = y * 2, sz = z * 2;
                            int sum = 0;
                            for (int k = 0; k < 8; k++)
                                sum += src[(sx + (k & 1)) + (sy + ((k >> 1) & 1)) * n + (sz + (k >> 2)) * n * n];
                            dst[x + y * m + z * m * m] = (byte)((sum + 4) / 8);
                        }

                levels[l] = dst;
                n = m;
            }

            return levels;
        }

        private static int Wrap(int v, int period)
        {
            int r = v % period;
            return r < 0 ? r + period : r;
        }

        private static int HashInt(int x, int y, int z, int seed)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + z * 1440662683 + seed * 1274126177;
                n = (n ^ (n >> 13)) * 1274126177;
                return (n ^ (n >> 16)) & 0x7fffffff;
            }
        }
    }
}
