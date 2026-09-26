using System;
using System.Globalization;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// CLOUD DETAIL (1.2): crisp edges, puffy billows and the light and shade that show them --
    /// what players asked for as "much more detail". 0 is off: the soft look from before, exactly
    /// (every use sits behind the shader's <c>_DetailAmount &gt; 0</c> branch).
    /// </summary>
    /// <remarks>
    /// Documented techniques only, each chosen from offline renders of the shader's own formula
    /// (the CPU twin, comparison sheets) before it was built:
    /// <list type="bullet">
    /// <item>Horizon Zero Dawn's erosion rule (Schneider 2015, GPU Pro 7): the fine noise carves
    /// the cells' CENTRES in the bottom tenth of the layer (wisps) and BETWEEN them above
    /// (billows). The old G-channel erosion only ever did the first, everywhere: foam.</item>
    /// <item>EVE-Redux V5 (blackrack's KSP clouds): "spherical" Worley (<see cref="CloudDetail3D"/>),
    /// "edge hardness" (the density reaches full within a thin shell, so clouds get surfaces
    /// instead of a noise-valued inside) and a density curve (denser tops, softer bases).</item>
    /// <item>Lighting: three octaves of multiple scattering over a dual-lobe phase (Wrenninge
    /// 2013, as in Hillaire 2016), the first three light steps reading the detail so billows
    /// shade each other, and sky light blocked by the cloud above.</item>
    /// </list>
    /// Two fixes came from the author's first look at the pictures ("from the top a bit weird",
    /// "cluttered"): the detail lookup is shifted by the base noise and the weather value already
    /// read (the 500 m repeat made a grid in the distance), and the tops are eroded at
    /// <see cref="TopErosion"/> (from above you see tops; from below, the bases and sides, which keep
    /// the full detail).
    /// </remarks>
    public static class CloudDetail
    {
        /// <summary>Erosion per unit of Break-up: the default 50% gives 0.8, the look chosen.</summary>
        public const float ErosionPerBreakup = 1.6f;

        /// <summary>EVE's "edge hardness" at 100%: the density is full 30% of the way in.</summary>
        public const float Hardness = 0.7f;

        /// <summary>Density lost at the very base at 100% (EVE's density curve): the base is at 40%.</summary>
        public const float DensityGradient = 0.6f;

        /// <summary>The erosion's multiplier on the upper part of the layer.</summary>
        public const float TopErosion = 0.45f;

        /// <summary>How far, in repeats, the base noise and the weather shift the detail lookup.</summary>
        public const float Warp = 0.5f;

        /// <summary>Metres per repeat of the detail at Break-up detail 1x: 500 m at the default 4x.</summary>
        public const float TileAtScale1 = 2000f;

        /// <summary>Octaves of multiple scattering: attenuation, extinction, eccentricity (Wrenninge's a, b, c).</summary>
        public const float OctaveA = 0.5f, OctaveB = 0.35f, OctaveC = 0.5f;

        /// <summary>How strongly the cloud above takes the sky light away.</summary>
        public const float AmbientOcclusion = 0.15f;

        /// <summary>The live texture, while a city is loaded (CloudVolume uploads it; null = none yet).</summary>
        public static Texture3D Texture { get; set; }

        /// <summary>
        /// The value is in the texture's alpha (ARGB32: alpha is never sRGB-decoded). Always true
        /// today: R8, the one-byte format, crashes Unity 5.6's Texture3D.SetPixels32 (CloudVolume).
        /// </summary>
        public static bool TextureIsAlpha { get; set; }

        /// <summary>This city's texture could not be made: the clouds are drawn without detail.</summary>
        public static bool Failed { get; set; }

        /// <summary>The setting, 0..1.</summary>
        public static float Amount
        {
            get
            {
                return Settings.CloudDetail != null
                    ? Mathf.Clamp01(Settings.CloudDetail.value)
                    : Settings.Defaults.Detail;
            }
        }

        public static bool On
        {
            get { return Amount > 0f; }
        }

        /// <summary>Metres per repeat of the detail, from Break-up detail (the old erosion's frequency).</summary>
        public static float Tile(float breakupScale)
        {
            return TileAtScale1 / Mathf.Max(0.5f, breakupScale);
        }

        /// <summary>
        /// The octave sum's gain, so that a sunlit face seen side-on is as bright as the old
        /// lighting made it: the brightness settings keep their meaning, and what changes is shape
        /// and shade. Both phases exactly as CloudRaymarch.shader computes them, at cos = 0.
        /// </summary>
        public static readonly float LightGain = ComputeLightGain();

        private static float ComputeLightGain()
        {
            float sum = 0f, a = 1f, c = 1f;
            for (int o = 0; o < 3; o++)
            {
                sum += a * DualLobe(0f, c);
                a *= OctaveA;
                c *= OctaveC;
            }

            float old = 0.65f + 0.35f * Mathf.Min(HenyeyGreenstein(0f, 0.55f), 5f);
            return old / sum;
        }

        /// <summary>CloudRaymarch.shader's DetailPhase: a forward peak (capped) over a weak back lobe.</summary>
        public static float DualLobe(float cosTheta, float c)
        {
            return 0.75f * Mathf.Min(HenyeyGreenstein(cosTheta, 0.8f * c), 10f) + 0.25f * HenyeyGreenstein(cosTheta, -0.3f * c);
        }

        private static float HenyeyGreenstein(float cosTheta, float g)
        {
            float g2 = g * g;
            return (1f - g2) / Mathf.Pow(1f + g2 - 2f * g * cosTheta, 1.5f);
        }

        /// <summary>
        /// The texture's mip chain as Color32 (the value in every channel; the shader reads alpha),
        /// for the city's noise seed. Worker thread: Color32 is a plain struct. Returns null on
        /// failure, with the reason in <paramref name="error"/>.
        /// </summary>
        public static Color32[][] BuildLevels(int noiseSeed, out string error, out int milliseconds)
        {
            error = null;
            milliseconds = 0;
            try
            {
                DateTime start = DateTime.UtcNow;
                byte[][] levels = CloudDetail3D.Generate(CloudDetail3D.Size, CloudDetail3D.SeedFor(noiseSeed), 0);
                Color32[][] colors = new Color32[levels.Length][];
                for (int l = 0; l < levels.Length; l++)
                {
                    byte[] src = levels[l];
                    Color32[] dst = new Color32[src.Length];
                    for (int i = 0; i < src.Length; i++)
                    {
                        byte v = src[i];
                        dst[i] = new Color32(v, v, v, v);
                    }
                    colors[l] = dst;
                }

                milliseconds = (int)(DateTime.UtcNow - start).TotalMilliseconds;
                return colors;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        public static string Describe()
        {
            return On ? (Amount * 100f).ToString("F0", CultureInfo.InvariantCulture) + "%" : "off";
        }
    }
}
