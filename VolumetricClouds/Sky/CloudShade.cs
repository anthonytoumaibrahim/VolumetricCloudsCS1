using System;
using System.Globalization;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// LIGHT IN THE SHADE: how dark the clouds' shaded sides and undersides are. Two parts, both on
    /// the "Light inside the clouds" slider (0% = the light from before, exactly): the sky light the
    /// base gets (as a share of what the top gets, and how much the cloud above takes away), and the
    /// sunlit ground's light on the undersides.
    /// </summary>
    /// <remarks>
    /// The game's sky light is about a tenth of its sun (in red, at noon; read off the log), so a
    /// cloud's shade was nearly all that tenth and came out heavy -- the author, with sunlight through
    /// the clouds on: "the shadows are too strong", the dark parts of the clouds. Both are documented
    /// cloud lighting: ambient as a gradient over the cloud's height (Horizon Zero Dawn; here its
    /// floor raised and the occlusion by the cloud above eased), and the ground's light on the
    /// undersides (Frostbite lights cloud bottoms with the ground's ambient): the sun x its height x
    /// what the ground gives back x the share of it in sun -- strongest at noon, under the bases,
    /// nothing at a low sun, so a backlit deck at sunset is left as dark as it was. Chosen on offline
    /// renders in the game's own light (2026-10-04, "A + both"). Only the visible clouds' light; the
    /// light of Classic at Cloud detail 0% (the soft look from before) is left alone.
    /// </remarks>
    public static class CloudShade
    {
        /// <summary>The sky light at the base, as a share of the top's: the shader's figure before this existed.</summary>
        public const float SkyFloorBefore = 0.45f;

        /// <summary>What 100% adds to it (75% at 100%; the whole of the top's from 183%).</summary>
        public const float SkyFloorPerSetting = 0.3f;

        /// <summary>What 100% takes off the occlusion by the cloud above (half; none left at 200%).</summary>
        public const float OcclusionEasePerSetting = 0.5f;

        /// <summary>The ground's albedo at 100%: a city and its fields give back about a quarter of the sun.</summary>
        public const float GroundAlbedo = 0.25f;

        /// <summary>How much of the ground a full sky leaves out of the sun (the share in sun is 1 - this x the cover).</summary>
        public const float GroundShadedAtFullCover = 0.6f;

        // Both parts follow the one "Light inside the clouds" slider (with LightThrough); kept apart
        // here so the maths and the log can still name each.
        public static float SkySetting
        {
            get { return LightThrough.Setting; }
        }

        public static float GroundSetting
        {
            get { return LightThrough.Setting; }
        }

        /// <summary>The sky light at the base, as a share of the top's (the shader's _ShadeLight.x).</summary>
        public static float SkyFloorFor(float setting)
        {
            return Math.Min(1f, SkyFloorBefore + SkyFloorPerSetting * Clamp(setting));
        }

        /// <summary>The factor on the occlusion by the cloud above (1 = as before).</summary>
        public static float OcclusionScaleFor(float setting)
        {
            return Math.Max(0f, 1f - OcclusionEasePerSetting * Clamp(setting));
        }

        /// <summary>
        /// The ground's light on the base, as a share of the clouds' sun (the shader's _ShadeLight.y):
        /// albedo x the setting x the share of the ground in sun x the sun's height (sin of its
        /// elevation; nothing once it is down).
        /// </summary>
        public static float GroundFor(float setting, float coverage, float sunUp)
        {
            float lit = 1f - GroundShadedAtFullCover * Math.Max(0f, Math.Min(1f, coverage));
            return GroundAlbedo * Clamp(setting) * lit * Math.Max(0f, sunUp);
        }

        public static string Describe(float sky, float ground)
        {
            return "sky light in the shade " + Percent(sky) +
                   (sky > 0f
                       ? " (the base " + Percent(SkyFloorFor(sky)) + " of the top's, occlusion x" +
                         OcclusionScaleFor(sky).ToString("F2", CultureInfo.InvariantCulture) + ")"
                       : " (as before)") +
                   ", light from the ground " + Percent(ground) +
                   (ground > 0f
                       ? " (albedo " + (GroundAlbedo * Clamp(ground)).ToString("F2", CultureInfo.InvariantCulture) + ")"
                       : " (none)");
        }

        private static float Clamp(float setting)
        {
            return Math.Max(0f, Math.Min(2f, setting));
        }

        private static string Percent(float value)
        {
            return (value * 100f).ToString("F0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
