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
