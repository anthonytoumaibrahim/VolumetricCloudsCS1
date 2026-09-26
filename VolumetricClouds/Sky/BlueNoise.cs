using System;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// THE RAYMARCHES' JITTER (1.2): a tiling tile of BLUE noise, read by CloudRaymarch.shader for
    /// every pixel's first step (clouds, rain and fog). Until 1.2 it was a white-noise hash, and
    /// the author saw its result on the Cumulus heaps: "a bit of artifacting, similar to the
    /// volumetric fog ... small dots everywhere".
    /// </summary>
    /// <remarks>
    /// The jitter trades banding for noise: each pixel starts its steps at its own offset, so an
    /// edge the steps skip over lands differently in every pixel. White noise clumps: neighbours
    /// often get similar offsets, and the error gathers into dots. Blue noise spreads every value
    /// evenly over any few neighbours, so the same error reads as a fine, even grain (rendered on
    /// the offline preview first, with the Cumulus march doubled: CloudStyle.StepFactor).
    ///
    /// Made by void-and-cluster (Ulichney 1993): a Gaussian energy (sigma 1.5) on the torus, the
    /// ones ranked by removing the tightest cluster, the rest by filling the largest void. With
    /// the self term in the kernel a texel's energy from the ones is the whole kernel minus its
    /// energy from the zeros, so "the largest void" also serves the second half. Pure managed
    /// maths (worker threads, tools/test-bluenoise.ps1), deterministic, the same for every city:
    /// made once per game session. Not part of what a seed reproduces.
    /// </remarks>
    public static class BlueNoise
    {
        /// <summary>Edge of the tile, in pixels.</summary>
        public const int Size = 64;

        private static readonly object Gate = new object();
        private static byte[] _bytes;
        private static int _milliseconds;

        /// <summary>
        /// The tile, row by row, one byte a pixel, every value 0..255 equally often: made the first
        /// time it is asked for (a city load's worker thread), then kept. Throws if it cannot be made.
        /// </summary>
        public static byte[] Bytes
        {
            get
            {
                lock (Gate)
                {
                    if (_bytes == null)
                    {
                        DateTime start = DateTime.UtcNow;
                        _bytes = Generate(Size, 20260926);
                        _milliseconds = (int)(DateTime.UtcNow - start).TotalMilliseconds;
                    }

                    return _bytes;
                }
            }
        }

        /// <summary>How long the tile took to make, for the log.</summary>
        public static int Milliseconds
        {
            get { lock (Gate) return _milliseconds; }
        }

        /// <summary>A blue-noise tile <paramref name="n"/> x <paramref name="n"/>, as bytes (rank x 256 / n^2).</summary>
        public static byte[] Generate(int n, int seed)
        {
            if (n < 8 || n > 256)
                throw new ArgumentException("the tile is 8 to 256 pixels across", "n");

            int count = n * n;
            const int radius = 6;
            const int width = 2 * radius + 1;
            double[] kernel = new double[width * width];
            for (int ky = -radius; ky <= radius; ky++)
                for (int kx = -radius; kx <= radius; kx++)
                    kernel[(ky + radius) * width + kx + radius] = Math.Exp(-(kx * kx + ky * ky) / (2.0 * 1.5 * 1.5));

            // A tenth of the pixels on, at random (a hash, not System.Random: the same on every runtime).
            bool[] on = new bool[count];
            double[] energy = new double[count];
            int ones = count / 10;
            uint state = unchecked((uint)seed * 2654435761u + 1u);
            for (int placed = 0; placed < ones;)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                int k = (int)(state % (uint)count);
                if (on[k])
                    continue;

                on[k] = true;
                Splat(energy, kernel, n, radius, k, 1);
                placed++;
            }

            // Relax it: move the tightest cluster's pixel to the largest void until that changes nothing.
            for (int guard = 0; guard < count; guard++)
            {
                int cluster = Extreme(on, energy, true);
                on[cluster] = false;
                Splat(energy, kernel, n, radius, cluster, -1);

                int hole = Extreme(on, energy, false);
                on[hole] = true;
                Splat(energy, kernel, n, radius, hole, 1);

                if (hole == cluster)
                    break;
            }

            int[] rank = new int[count];
            bool[] startOn = (bool[])on.Clone();
            double[] startEnergy = (double[])energy.Clone();

            // The ones, from the last down: take away the tightest cluster each time.
            for (int left = ones; left > 0; left--)
            {
                int cluster = Extreme(on, energy, true);
                on[cluster] = false;
                Splat(energy, kernel, n, radius, cluster, -1);
                rank[cluster] = left - 1;
            }

            // The rest, from the relaxed start up: fill the largest void each time.
            on = startOn;
            energy = startEnergy;
            for (int filled = ones; filled < count; filled++)
            {
                int hole = Extreme(on, energy, false);
                on[hole] = true;
                Splat(energy, kernel, n, radius, hole, 1);
                rank[hole] = filled;
            }

            byte[] bytes = new byte[count];
            for (int i = 0; i < count; i++)
                bytes[i] = (byte)(rank[i] * 256 / count);
            return bytes;
        }

        /// <summary>Adds (sign 1) or takes away (-1) one pixel's energy, wrapping round the tile.</summary>
        private static void Splat(double[] energy, double[] kernel, int n, int radius, int index, int sign)
        {
            int x0 = index % n, y0 = index / n, width = 2 * radius + 1;
            for (int ky = -radius; ky <= radius; ky++)
            {
                int y = ((y0 + ky) % n + n) % n;
                int row = y * n;
                int krow = (ky + radius) * width + radius;
                for (int kx = -radius; kx <= radius; kx++)
                {
                    int x = ((x0 + kx) % n + n) % n;
                    energy[row + x] += sign * kernel[krow + kx];
                }
            }
        }

        /// <summary>The tightest cluster (the on pixel with the most energy) or the largest void (the off one with the least).</summary>
        private static int Extreme(bool[] on, double[] energy, bool cluster)
        {
            int best = -1;
            for (int i = 0; i < on.Length; i++)
            {
                if (on[i] != cluster)
                    continue;

                if (best < 0 || (cluster ? energy[i] > energy[best] : energy[i] < energy[best]))
                    best = i;
            }

            return best;
        }
    }
}
