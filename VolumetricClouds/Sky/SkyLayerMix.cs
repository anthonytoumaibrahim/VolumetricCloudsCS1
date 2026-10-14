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

        // "Dynamic distant clouds" (1.5.0). The author, 2026-10-14: tied to the weather, "but I still need
        // randomness ... for the most part it should be without distant clouds, so the chances for them
        // should be less than 50%". So two parts:
        //   a SPELL -- two slow waves of the city's own deciding whether there are distant clouds at all;
        //     they are there less than half the time (test-skylayer measures it), fading in and out;
        //   the KINDS -- the cloud cover picks them: cirrus and sheep clouds under a clear sky, cumulus on
        //     the horizon in a partly cloudy one, layered skies towards overcast; each kind's own slow wave
        //     makes it more or less of that, so two clear days don't look alike.
        // Periods in lives of the travelling copies (SkyLayer.Lifetime); none shares a measure with another,
        // so nothing comes round again, and all are several lives long: slower than the pictures' fades.
        private const double SpellSlow = 13.1;
        private const double SpellFast = 5.3;

        /// <summary>The spell's waves (0..1) must be above this for distant clouds to show at all.</summary>
        private const float SpellStart = 0.58f;

        /// <summary>... and are whole this far above it: between, they fade in (over a life or so).</summary>
        private const float SpellFade = 0.25f;

        private static readonly float[] DynamicMost = { 0.75f, 0.6f, 0.6f, 0.55f };
        private static readonly double[] DynamicSlow = { 7.3, 9.1, 8.2, 11.7 };

        /// <summary>The most the four may add up to, so the mixed sky never turns into one grey sheet.</summary>
        private const float DynamicTotal = 1.3f;

        /// <summary>
        /// "Dynamic distant clouds" (1.5.0): the four kinds' amounts (<paramref name="amounts"/>, 0..1 each)
        /// at <paramref name="lives"/> -- how far the sky has travelled, in lives of its copies -- under a
        /// cloud cover of <paramref name="cover"/> (0..1), for a city of <paramref name="seed"/>. All 0
        /// outside a spell (most of the time); never adding up to more than <see cref="DynamicTotal"/>.
        /// </summary>
        public static void Dynamic(double lives, int seed, float cover, float[] amounts)
        {
            double spellSlow, spellFast;
            Phases(seed, Kinds, out spellSlow, out spellFast);
            double spellWave = 0.6 * Math.Sin(2.0 * Math.PI * (lives / SpellSlow + spellSlow))
                             + 0.4 * Math.Sin(2.0 * Math.PI * (lives / SpellFast + spellFast));
            float spell = Smooth(((float)(0.5 + 0.5 * spellWave) - SpellStart) / SpellFade);

            cover = Clamp01(cover);
            float total = 0f;
            for (int k = 0; k < Kinds; k++)
            {
                double phase, unused;
                Phases(seed, k, out phase, out unused);
                float wave = (float)(0.5 + 0.5 * Math.Sin(2.0 * Math.PI * (lives / DynamicSlow[k] + phase)));
                amounts[k] = spell * Fit(k, cover) * (0.35f + 0.65f * wave) * DynamicMost[k];
                total += amounts[k];
            }

            if (total > DynamicTotal)
            {
                for (int k = 0; k < Kinds; k++)
                    amounts[k] *= DynamicTotal / total;
            }
        }

        /// <summary>How well a kind suits a cloud cover, 0..1.</summary>
        private static float Fit(int kind, float cover)
        {
            switch (kind)
            {
                case Cirrus: return 1f - Smooth((cover - 0.35f) / 0.35f);                        // clear, gone by 70%
                case Sheep: return Smooth(cover / 0.2f) * (1f - Smooth((cover - 0.45f) / 0.25f)); // fair weather
                case Far: return Smooth((cover - 0.15f) / 0.25f) * (1f - Smooth((cover - 0.75f) / 0.2f)); // partly cloudy
                default: return Smooth((cover - 0.45f) / 0.3f);                                   // towards overcast
            }
        }

        /// <summary>Two phases, 0..1, for a city and a slot (a kind, or <see cref="Kinds"/> for the spell).</summary>
        private static void Phases(int seed, int slot, out double a, out double b)
        {
            uint hash = unchecked((uint)seed * 2654435761u + (uint)slot * 40503u);
            hash ^= hash >> 15;
            hash = unchecked(hash * 2246822519u);
            hash ^= hash >> 13;
            a = (hash & 0xFFFF) / 65536.0;
            b = (hash >> 16) / 65536.0;
        }

        private static float Smooth(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
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
