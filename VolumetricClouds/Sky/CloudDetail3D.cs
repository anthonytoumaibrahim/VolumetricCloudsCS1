using System;
using System.Threading;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// CLOUD DETAIL's texture (1.2): a tiling 128^3 volume of "puffy" Worley noise, the fine
    /// billows <see cref="CloudDetail"/> erodes into the clouds' surfaces, with its whole mip chain.
    /// </summary>
    /// <remarks>
    /// Three octaves of Worley noise (periods 4, 8, 16; each half the weight of the one before),
    /// each cell given the round profile sqrt(1 - d^2) instead of the cone 1 - d the old G channel
    /// uses: EVE-Redux V5's "spherical" Worley, "billowy and puffy". Designed on offline renders of
    /// the shader's formula before it was built (the 500 m repeat, the three octaves, the round
    /// profile were all chosen from pictures).
    ///
    /// A SEPARATE texture on purpose: the 64^3 noise (<see cref="CloudNoise3D"/>) and the weather
    /// field -- what every saved city's big clouds are made of -- are untouched, so a save keeps
    /// its sky and detail only sculpts its surfaces. The volume comes from the city's noise seed,
    /// so it is the same every time the city is loaded. Like the other generators, these numbers
    /// are part of what a seed reproduces (invariant 14): change one and every city's billows
    /// change; that is what <see cref="Generator"/> is for.
    ///
    /// Pure System.Math on plain arrays -- no UnityEngine calls -- so it runs on worker threads
    /// and offline in tools/test-detail.ps1. The mips are built here too: Unity 5.6 would build a
    /// 3D texture's on the main thread, and a box filter of a noise keeps its mean, which is what
    /// the shader's distance fade relies on.
    /// </remarks>
    public static class CloudDetail3D
    {
        /// <summary>Edge of the volume, in texels.</summary>
        public const int Size = 128;

        /// <summary>What this generator is. Bump it with any change to what <see cref="Generate"/> makes.</summary>
        public const int Generator = 1;

        private const int Octaves = 3;
        private const int BasePeriod = 4;
        private const float Persistence = 0.5f;

        /// <summary>The volume's seed from the city's noise seed: its own, so it matches no other noise.</summary>
        public static int SeedFor(int noiseSeed)
        {
            return unchecked(noiseSeed * 31 + 7919);
        }

        /// <summary>
        /// The mip chain, level 0 first: <paramref name="size"/>^3 bytes, then each level half the
        /// edge, down to 1^3. Index x + y * n + z * n * n. <paramref name="size"/> must be a power
        /// of two. Split over <paramref name="threads"/> worker threads (0 = one per core, up to 8);
        /// the result does not depend on how many.
        /// </summary>
        public static byte[][] Generate(int size, int seed, int threads)
        {
            if (size < 1 || (size & (size - 1)) != 0)
                throw new ArgumentException("size must be a power of two", "size");

            // Every octave's feature points, one per cell, placed once: the volume then only
            // measures distances. A cell's point is in [cell, cell + 1) on each axis.
            float[][] points = new float[Octaves][];
            for (int o = 0; o < Octaves; o++)
            {
                int period = BasePeriod << o;
                float[] p = new float[period * period * period * 3];
                for (int z = 0; z < period; z++)
                    for (int y = 0; y < period; y++)
                        for (int x = 0; x < period; x++)
                        {
                            int h = HashInt(x, y, z, seed + 101 * o);
                            int i = (x + y * period + z * period * period) * 3;
                            p[i] = (h & 1023) / 1024f;
                            p[i + 1] = ((h >> 10) & 1023) / 1024f;
                            p[i + 2] = ((h >> 20) & 1023) / 1024f;
                        }
                points[o] = p;
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
                        Fill(values, size, points, first, last);
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }
                })
                {
                    IsBackground = true,
                    Name = "VolumetricClouds detail",
                };
                workers[w].Start();
            }

            for (int w = 0; w < threads; w++)
                workers[w].Join();

            if (error != null)
                throw error;

            return Levels(Normalise(values), size);
        }

        /// <summary>The z-slices [first, last) of the volume.</summary>
        private static void Fill(float[] values, int size, float[][] points, int first, int last)
        {
            float inv = 1f / size;

            for (int z = first; z < last; z++)
            {
                for (int y = 0; y < size; y++)
                {
                    int row = y * size + z * size * size;
                    for (int x = 0; x < size; x++)
                    {
                        float sum = 0f, amplitude = 1f, total = 0f;

                        for (int o = 0; o < Octaves; o++)
                        {
                            int period = BasePeriod << o;
                            float d = Distance(x * inv * period, y * inv * period, z * inv * period, period, points[o]);

                            // The round profile: a sphere's height over its disc, not a cone.
                            sum += (float)Math.Sqrt(Math.Max(0f, 1f - d * d)) * amplitude;
                            total += amplitude;
                            amplitude *= Persistence;
                        }

                        values[row + x] = sum / total;
                    }
                }
            }
        }

        /// <summary>Distance to the nearest feature point, in cells, capped at 1 (tiling).</summary>
        private static float Distance(float x, float y, float z, int period, float[] points)
        {
            int xi = (int)Math.Floor(x);
            int yi = (int)Math.Floor(y);
            int zi = (int)Math.Floor(z);
            float best = float.MaxValue;

            for (int dz = -1; dz <= 1; dz++)
            {
                int cz = Wrap(zi + dz, period);
                for (int dy = -1; dy <= 1; dy++)
                {
                    int cy = Wrap(yi + dy, period);
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int cx = Wrap(xi + dx, period);
                        int i = (cx + cy * period + cz * period * period) * 3;

                        float ox = xi + dx + points[i] - x;
                        float oy = yi + dy + points[i + 1] - y;
                        float oz = zi + dz + points[i + 2] - z;
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
