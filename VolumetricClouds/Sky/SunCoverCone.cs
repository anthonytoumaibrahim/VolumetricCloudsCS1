using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// How far round the sun the clouds keep their full cover instead of fading into the distance
    /// (CloudMedia.cginc SunCover, set by CloudVolume.ApplyNight). Pure: tools/test-placement.ps1.
    /// </summary>
    /// <remarks>
    /// The clouds dissolve into the distance by going see-through, which by day shows sky colour
    /// behind them -- the look that is liked. Towards the sun it shows the SUN: its disc and the
    /// sky's glow round it are so bright that a thick cloud 4 km away, still 25% see-through,
    /// burns white. The author, 2026-09-29: "At 100% intensity I can't even see the clouds anymore,
    /// the sun is too strong ... like in real life: the clouds should block the sun." The first
    /// fixes kept the cover within 10 degrees of the sun, widened to 45 at sunset: a hard dark disc
    /// with the glare all round it (preview: docs/previews/render-detail.ps1 -Set sun, row B). Now
    /// the cover follows the sky's OWN glow -- the game's Mie phase, g = DayNightProperties.
    /// m_SunAnisotropyFactor, set by the map's theme: full where the glow is at least
    /// <see cref="FullShare"/> of its peak, none below <see cref="NoneShare"/> (row C, a backlit
    /// overcast with the sun hidden). With g 0.651 (his map) that is 28 and 49 degrees round the sun,
    /// with the default 0.76, 18 and 31: a wide glow gets a wide cover.
    /// </remarks>
    public static class SunCoverCone
    {
        /// <summary>The glow's share of its peak where the cover is full.</summary>
        public const float FullShare = 0.3f;

        /// <summary>The glow's share of its peak below which there is no cover.</summary>
        public const float NoneShare = 0.1f;

        /// <summary>The game's own default g (DayNightProperties' constructor).</summary>
        public const float DefaultG = 0.76f;

        /// <summary>
        /// The angle from the sun (degrees) at which a Henyey-Greenstein glow with eccentricity
        /// <paramref name="g"/> has fallen to <paramref name="share"/> of its peak.
        /// </summary>
        public static float AngleAt(float g, float share)
        {
            g = Mathf.Clamp(g, 0.05f, 0.98f);
            share = Mathf.Clamp(share, 1e-4f, 1f);

            // HG(c) / HG(1) = ((1 - g)^2 / (1 + g^2 - 2 g c))^1.5 = share, solved for c.
            float c = (1f + g * g - (1f - g) * (1f - g) / Mathf.Pow(share, 2f / 3f)) / (2f * g);
            return Mathf.Acos(Mathf.Clamp(c, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The cone for the sky's glow: full cover within <paramref name="innerDeg"/> of the sun,
        /// none beyond <paramref name="outerDeg"/> -- 3 to 70 degrees, at least 5 apart.
        /// </summary>
        public static void Angles(float g, out float innerDeg, out float outerDeg)
        {
            innerDeg = Mathf.Clamp(AngleAt(g, FullShare), 3f, 60f);
            outerDeg = Mathf.Clamp(AngleAt(g, NoneShare), innerDeg + 5f, 70f);
        }
    }
}
