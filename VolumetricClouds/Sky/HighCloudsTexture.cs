using System;
using System.Threading;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// THE CIRRUS PATTERN (a later update's plan, pictures first; nothing in the mod uses it yet): the tiling 2D texture the cirrus layer is
    /// drawn from, the way Horizon Zero Dawn draws its "high altitude 2D alto and cirro class clouds
    /// above 4000 meters" as tiling, scrolling textures above its volumetric clouds (Schneider,
    /// SIGGRAPH 2015, slide 73 and notes 86-87). Theirs were photographs; this is made from the
    /// documented noises the volumetric clouds already use:
    ///   R  the fibres: stretched value noise, ridged (Perlin's turbulence, |2n - 1| turned into
    ///      crests) for thin parallel fibres, bent by domain warping -- the distortion HZD and EVE
    ///      use to make wisps -- and gathered into streak bundles.
    ///   G  where the cirrus is: a large, stretched patch field.
    /// Both histogram-equalised: a value is a rank, so a share of the sky is a share of values.
    /// Pure managed maths, no Unity: runs on a worker thread and in the offline preview. A seed
    /// gives the same bytes on any thread count.
    /// </summary>
    public static class HighCloudsTexture
    {
        public const int Size = 1024;

        /// <summary>Part of what a seed reproduces (invariant 14): bump with any change below.</summary>
        public const int Generator = 1;

        // Periods of the first octave along x (the streak) and across it.
        private const int FibreAlong = 3, FibreAcross = 48;
        private const int BundleAlong = 4, BundleAcross = 16;
        private const int PatchAlong = 2, PatchAcross = 3;
        private const float WarpAcross = 0.035f, WarpAlong = 0.02f;

        /// <summary>This texture's seed from the city's noise seed (not the noise's own: another texture).</summary>
        public static int SeedFor(int noiseSeed)
        {
            unchecked
            {
                return noiseSeed * 747796405 + 0x48494748;
            }
        }

        /// <summary>
        /// RGBA bytes, x + y * size, four a texel (R fibres, G patches, B and A zero), for a
        /// <paramref name="size"/> that is a multiple of 64.
        /// </summary>
        public static byte[] Generate(int size, int seed)
        {
            if (size < 64 || size % 64 != 0)
                throw new ArgumentException("size must be a positive multiple of 64", "size");

            int n = size * size;
            float[] fibres = new float[n], patches = new float[n];

            int threads = Math.Max(1, Math.Min(8, Environment.ProcessorCount));
            int next = -1;
            Thread[] workers = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                workers[t] = new Thread(() =>
                {
                    int y;
                    while ((y = Interlocked.Increment(ref next)) < size)
                        Row(y, size, seed, fibres, patches);
                });
                workers[t].Start();
            }

            foreach (Thread worker in workers)
                worker.Join();

            byte[] r = Equalise(fibres), g = Equalise(patches);
            byte[] rgba = new byte[n * 4];
            for (int i = 0; i < n; i++)
            {
                rgba[i * 4] = r[i];
                rgba[i * 4 + 1] = g[i];
            }

            return rgba;
        }

        private static void Row(int y, int size, int seed, float[] fibres, float[] patches)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                int i = x + y * size;

                // The warp bends the streaks mostly across themselves (curved fibres, hooks where it
                // is strong); periodic, so the texture still tiles.
                float wu = u + WarpAlong * (Fbm(u, v, 2, 2, 3, 0.5f, seed + 11) * 2f - 1f);
                float wv = v + WarpAcross * (Fbm(u, v, 3, 3, 3, 0.5f, seed + 17) * 2f - 1f);

                float fibre = 0f, weight = 1f, total = 0f;
                for (int o = 0; o < 3; o++)
                {
                    int along = FibreAlong << o, across = FibreAcross << o;
                    float crest = 1f - Math.Abs(Value(wu * along, wv * across, along, across, seed + 31 + o) * 2f - 1f);
                    fibre += crest * crest * weight;
                    total += weight;
                    weight *= 0.6f;
                }

                float bundle = Fbm(wu, wv, BundleAlong, BundleAcross, 3, 0.5f, seed + 41);
                fibres[i] = fibre / total * (0.3f + 0.7f * bundle);
                patches[i] = Fbm(u, v, PatchAlong, PatchAcross, 4, 0.5f, seed + 53);
            }
        }

        /// <summary>Tiling fractal value noise, periods per axis doubling each octave.</summary>
        private static float Fbm(float u, float v, int periodX, int periodY, int octaves, float persistence, int seed)
        {
            float sum = 0f, amp = 1f, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                int px = periodX << o, py = periodY << o;
                sum += Value(u * px, v * py, px, py, seed + o * 101) * amp;
                total += amp;
                amp *= persistence;
            }

            return sum / total;
        }

        /// <summary>Tiling value noise, quintic-smoothed, 0..1.</summary>
        private static float Value(float x, float y, int periodX, int periodY, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            fy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);

            int xa = Wrap(x0, periodX), xb = Wrap(x0 + 1, periodX);
            int ya = Wrap(y0, periodY), yb = Wrap(y0 + 1, periodY);
            float a = Unit(Hash(xa, ya, seed)), b = Unit(Hash(xb, ya, seed));
            float c = Unit(Hash(xa, yb, seed)), d = Unit(Hash(xb, yb, seed));

            float top = a + (b - a) * fx, bottom = c + (d - c) * fx;
            return top + (bottom - top) * fy;
        }

        /// <summary>Values to bytes by rank: a histogram-equalised channel, so a share of the sky is a share of values.</summary>
        private static byte[] Equalise(float[] values)
        {
            const int bins = 4096;
            float min = float.MaxValue, max = float.MinValue;
            foreach (float value in values)
            {
                if (value < min) min = value;
                if (value > max) max = value;
            }

            float scale = max > min ? (bins - 1) / (max - min) : 0f;
            int[] histogram = new int[bins];
            foreach (float value in values)
                histogram[(int)((value - min) * scale)]++;

            // Each bin's byte: the middle of its rank range, 0..255.
            byte[] rank = new byte[bins];
            long below = 0;
            for (int bin = 0; bin < bins; bin++)
            {
                double middle = (below + histogram[bin] * 0.5) / values.Length;
                rank[bin] = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(middle * 255.0)));
                below += histogram[bin];
            }

            byte[] result = new byte[values.Length];
            for (int i = 0; i < values.Length; i++)
                result[i] = rank[(int)((values[i] - min) * scale)];

            return result;
        }

        private static int Wrap(int value, int period)
        {
            int m = value % period;
            return m < 0 ? m + period : m;
        }

        private static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                h *= 2654435761u;
                return h ^ (h >> 15);
            }
        }

        private static float Unit(uint h)
        {
            return (h & 0xFFFFFF) / 16777215f;
        }
    }
}
