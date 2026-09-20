using System;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Generates the tiling 3D noise volume the raymarch shader shapes clouds with.
    /// R = Perlin-Worley base shape, G = Worley detail used to erode the edges.
    /// </summary>
    /// <remarks>
    /// Pure System.Math on plain arrays -- no UnityEngine calls -- so it can run on a
    /// worker thread. A 64^3 volume is tens of millions of hash evaluations, which would
    /// be a multi-second stall on the main thread under the game's old Mono runtime.
    /// </remarks>
    public static class CloudNoise3D
    {
        private const int ValueOctaves = 4;
        private const int BaseWorleyOctaves = 3;
        private const int DetailWorleyOctaves = 2;
        private const int BasePeriod = 4;
        private const int DetailPeriod = 8;

        /// <summary>Returns size^3 RGBA bytes (4 per voxel), x-major then y then z.</summary>
        public static byte[] Generate(int size, int seed)
        {
            int count = size * size * size;
            float[] baseShape = new float[count];
            float[] detail = new float[count];

            float inv = 1f / size;
            int index = 0;

            for (int z = 0; z < size; z++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++, index++)
                    {
                        float fx = x * inv;
                        float fy = y * inv;
                        float fz = z * inv;

                        float value = ValueFbm(fx, fy, fz, seed);
                        float billow = WorleyFbm(fx, fy, fz, BasePeriod, BaseWorleyOctaves, seed + 101);

                        // Same bounded remap as the 2D field: divisor is 2 - billow, in [1,2].
                        baseShape[index] = (value - (billow - 1f)) / (2f - billow);
                        detail[index] = WorleyFbm(fx, fy, fz, DetailPeriod, DetailWorleyOctaves, seed + 313);
                    }
                }
            }

            Normalise(baseShape);
            Normalise(detail);

            byte[] bytes = new byte[count * 4];
            for (int i = 0; i < count; i++)
            {
                byte r = (byte)(baseShape[i] * 255f);
                byte g = (byte)(detail[i] * 255f);
                int o = i * 4;
                bytes[o + 0] = r;
                bytes[o + 1] = g;
                bytes[o + 2] = 0;
                bytes[o + 3] = 255;
            }

            return bytes;
        }

        private static float ValueFbm(float x, float y, float z, int seed)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;
            int period = BasePeriod;

            for (int octave = 0; octave < ValueOctaves; octave++)
            {
                sum += ValueNoise(x * period, y * period, z * period, period, seed + octave) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                period *= 2;
            }

            return sum / total;
        }

        /// <summary>Inverted Worley fBm: near a feature point means dense.</summary>
        private static float WorleyFbm(float x, float y, float z, int basePeriod, int octaves, int seed)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;
            int period = basePeriod;

            for (int octave = 0; octave < octaves; octave++)
            {
                sum += (1f - Worley(x * period, y * period, z * period, period, seed + octave)) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                period *= 2;
            }

            float result = sum / total;
            return result < 0f ? 0f : (result > 1f ? 1f : result);
        }

        private static float Worley(float x, float y, float z, int period, int seed)
        {
            int xi = (int)Math.Floor(x);
            int yi = (int)Math.Floor(y);
            int zi = (int)Math.Floor(z);
            float best = float.MaxValue;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        // One hash per cell, sliced into three 10-bit offsets: a third of
                        // the hashing of jittering each axis separately.
                        int h = HashInt(Wrap(xi + dx, period), Wrap(yi + dy, period), Wrap(zi + dz, period), seed);

                        float ox = xi + dx + (h & 1023) / 1023f - x;
                        float oy = yi + dy + ((h >> 10) & 1023) / 1023f - y;
                        float oz = zi + dz + ((h >> 20) & 1023) / 1023f - z;

                        float d = ox * ox + oy * oy + oz * oz;
                        if (d < best)
                            best = d;
                    }
                }
            }

            float distance = (float)Math.Sqrt(best);
            return distance > 1f ? 1f : distance;
        }

        private static float ValueNoise(float x, float y, float z, int period, int seed)
        {
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            int z0 = (int)Math.Floor(z);

            float fx = Smooth(x - x0);
            float fy = Smooth(y - y0);
            float fz = Smooth(z - z0);

            int x1 = Wrap(x0 + 1, period);
            int y1 = Wrap(y0 + 1, period);
            int z1 = Wrap(z0 + 1, period);
            x0 = Wrap(x0, period);
            y0 = Wrap(y0, period);
            z0 = Wrap(z0, period);

            float c00 = Lerp(Hash01(x0, y0, z0, seed), Hash01(x1, y0, z0, seed), fx);
            float c10 = Lerp(Hash01(x0, y1, z0, seed), Hash01(x1, y1, z0, seed), fx);
            float c01 = Lerp(Hash01(x0, y0, z1, seed), Hash01(x1, y0, z1, seed), fx);
            float c11 = Lerp(Hash01(x0, y1, z1, seed), Hash01(x1, y1, z1, seed), fx);

            return Lerp(Lerp(c00, c10, fy), Lerp(c01, c11, fy), fz);
        }

        private static void Normalise(float[] data)
        {
            float min = float.MaxValue;
            float max = float.MinValue;

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] < min)
                    min = data[i];
                if (data[i] > max)
                    max = data[i];
            }

            float span = max - min;
            if (span < 1e-5f)
                return;

            for (int i = 0; i < data.Length; i++)
                data[i] = (data[i] - min) / span;
        }

        private static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
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

        private static float Hash01(int x, int y, int z, int seed)
        {
            return HashInt(x, y, z, seed) / (float)0x7fffffff;
        }
    }
}
