using System;
using System.Text;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// THE SKY LAYER's pictures (1.5.0, <see cref="SkyLayer"/>): which of them each kind shows at its
    /// amount. Pure: tools/test-skylayer.ps1 compiles this file.
    /// </summary>
    /// <remarks>
    /// The author picked several photographed skies per kind (2026-10-12: cirrus C6, C2, C3; sheep clouds
    /// S1, S7; far clouds F2, F6; one sky of several layers, M2). A kind's amount walks through them,
    /// thinnest first: with three pictures, 0-33% fades the first in, 33-67% turns it into the second,
    /// 67-100% the second into the third -- so 33%, 67% and 100% show each one alone, and more is always
    /// more cloud.
    /// </remarks>
    public static class SkyLayerMix
    {
        public const int Kinds = 4;
        public const int Cirrus = 0;
        public const int Sheep = 1;
        public const int Far = 2;
        public const int Layers = 3;

        /// <summary>
        /// The pictures, by their embedded names (Resources/SkyLayer/&lt;name&gt;.jpg, made by
        /// tools/make-skylayer.ps1), in the order they are packed: picture p goes into texture p / 4,
        /// channel p % 4 (r, g, b, a).
        /// </summary>
        public static readonly string[] Pictures =
        {
            "cirrus-1", "cirrus-2", "cirrus-3", "layers-1",
            "sheep-1", "sheep-2", "far-1", "far-2",
        };

        /// <summary>Each kind's pictures (indices into <see cref="Pictures"/>), thinnest first.</summary>
        private static readonly int[][] KindPictures =
        {
            new[] { 0, 1, 2 },
            new[] { 4, 5 },
            new[] { 6, 7 },
            new[] { 3 },
        };

        /// <summary>What the log calls each kind.</summary>
        public static readonly string[] KindNames = { "cirrus", "sheep clouds", "far clouds", "cloud layers" };

        /// <summary>
        /// How much of each picture (<paramref name="weights"/>, one per <see cref="Pictures"/> entry)
        /// <paramref name="kind"/> shows at <paramref name="amount"/> 0..1. Two pictures at most, their
        /// weights adding up to the share of the first that has faded in.
        /// </summary>
        public static void Weights(int kind, float amount, float[] weights)
        {
            Array.Clear(weights, 0, weights.Length);
            int[] pictures = KindPictures[kind];
            int n = pictures.Length;
            float s = Clamp01(amount) * n;
            if (s <= 1f)
            {
                weights[pictures[0]] = s;
                return;
            }

            float q = s - 1f;
            int a = (int)Math.Floor(q);
            if (a >= n - 1)
            {
                weights[pictures[n - 1]] = 1f;
                return;
            }

            float f = q - a;
            weights[pictures[a]] = 1f - f;
            weights[pictures[a + 1]] = f;
        }

        // "Dynamic distant clouds" (1.5.0): each kind's greatest amount, and how long (in lives of the
        // travelling copies, SkyLayer.Lifetime) its two waves take. The periods share no common
        // measure, so the mix never comes round again; each is several lives long, so the kinds
        // change more slowly than the pictures fade in and out.
        private static readonly float[] DynamicMost = { 0.75f, 0.55f, 0.55f, 0.45f };
        private static readonly double[] DynamicSlow = { 7.3, 9.1, 8.2, 11.7 };
        private static readonly double[] DynamicFast = { 2.9, 3.7, 3.3, 4.3 };

        /// <summary>A kind is gone while its waves are below this share (0..1): each is away some of the time.</summary>
        private const float DynamicAbsent = 0.4f;

        /// <summary>The most the four may add up to, so the mixed sky never turns into one grey sheet.</summary>
        private const float DynamicTotal = 1.3f;

        /// <summary>
        /// "Dynamic distant clouds" (1.5.0): the four kinds' amounts (<paramref name="amounts"/>, 0..1 each)
        /// at <paramref name="lives"/> -- how far the sky has travelled, in lives of its copies -- for a city
        /// of <paramref name="seed"/>. Each kind comes and goes on two slow waves of its own, the city's
        /// seed placing them; together they never add up to more than <see cref="DynamicTotal"/>.
        /// </summary>
        public static void Dynamic(double lives, int seed, float[] amounts)
        {
            float total = 0f;
            for (int k = 0; k < Kinds; k++)
            {
                uint hash = unchecked((uint)seed * 2654435761u + (uint)k * 40503u);
                hash ^= hash >> 15;
                hash = unchecked(hash * 2246822519u);
                hash ^= hash >> 13;
                double phaseSlow = (hash & 0xFFFF) / 65536.0;
                double phaseFast = (hash >> 16) / 65536.0;

                double wave = 0.65 * Math.Sin(2.0 * Math.PI * (lives / DynamicSlow[k] + phaseSlow))
                            + 0.35 * Math.Sin(2.0 * Math.PI * (lives / DynamicFast[k] + phaseFast));
                float share = (float)(0.5 + 0.5 * wave);
                float t = Clamp01((share - DynamicAbsent) / (1f - DynamicAbsent));
                amounts[k] = t * t * (3f - 2f * t) * DynamicMost[k];
                total += amounts[k];
            }

            if (total > DynamicTotal)
            {
                for (int k = 0; k < Kinds; k++)
                    amounts[k] *= DynamicTotal / total;
            }
        }

        /// <summary>For the log: "off", or the pictures shown, e.g. "cirrus-1 40% + cirrus-2 60%".</summary>
        public static string Describe(int kind, float amount)
        {
            var weights = new float[Pictures.Length];
            Weights(kind, amount, weights);
            var text = new StringBuilder();
            for (int p = 0; p < weights.Length; p++)
            {
                if (weights[p] <= 0f)
                    continue;
                if (text.Length > 0)
                    text.Append(" + ");
                text.Append(Pictures[p]).Append(' ').Append((weights[p] * 100f).ToString("F0")).Append('%');
            }
            return text.Length > 0 ? text.ToString() : "off";
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }
}
