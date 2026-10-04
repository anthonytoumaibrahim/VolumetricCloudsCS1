using System;
using System.Globalization;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// SUNLIGHT THROUGH THE CLOUDS: thin and middling cloud glows with the sun behind it, the way
    /// real cloud does at sunset, and a thick deck's far side stays dark. 0% is the light from
    /// before, exactly (every use sits behind the shader's <c>_LightThrough.x &gt; 0</c> branch).
    /// </summary>
    /// <remarks>
    /// Cloud droplets scatter mostly forwards, so light that has been scattered keeps going: the
    /// two-stream (diffusion) result for a slab falls off like 1 / (1 + c (1 - g) tau), not
    /// exponentially (Bohren 1987). The clouds' light had only exponentials -- Cumulus' multiple
    /// scattering exp(-0.5 tau), cloud detail's octaves exp(-0.35 tau) and exp(-0.12 tau) -- so a
    /// heap seen against a low sun was lit by the sky alone: a dark lump. Here those terms fall off
    /// no faster than <see cref="Tail"/>, SQUARED: chosen on offline renders of the shader's own
    /// formula, where the plain tail lit a 100% deck towards the sun right up (the backlit deck is
    /// meant to stay dark) and the squared one kept it so while the heaps glowed. Never darker than
    /// before; at tau = 0 (a sunlit face) nothing changes, so the brightness settings keep their
    /// meaning. Only the visible clouds' light: the ground shadows are the shadow map's, untouched.
    /// </remarks>
    public static class LightThrough
    {
        /// <summary>The tail's k at 100%: the setting picked on the renders (2026-10-04).</summary>
        public const float K = 0.15f;

        /// <summary>
        /// The setting, 0..2 (the slider's 0%..200%); its default when there is none. "Light inside
        /// the clouds": one slider for this and both of CloudShade's parts.
        /// </summary>
        public static float Setting
        {
            get
            {
                return Settings.LightInsideClouds != null
                    ? Math.Max(0f, Math.Min(2f, Settings.LightInsideClouds.value))
                    : Settings.Defaults.LightInsideClouds;
            }
        }

        /// <summary>
        /// How much of the tail is used: the slider up to 100%, all of it above. 0 = off, the light
        /// from before.
        /// </summary>
        public static float AmountFor(float setting)
        {
            return Math.Max(0f, Math.Min(1f, setting));
        }

        /// <summary>The tail's k: <see cref="K"/> up to 100%, then K / setting -- 200% lets light twice as deep.</summary>
        public static float KFor(float setting)
        {
            return K / Math.Max(1f, setting);
        }

        /// <summary>The slowest the multiple-scattering light may fall off at optical depth tau.</summary>
        public static float Tail(float tau, float k)
        {
            float t = 1f / (1f + k * Math.Max(0f, tau));
            return t * t;
        }

        /// <summary>
        /// The shader's Soak: a term's transmittance <paramref name="transmittance"/> at optical depth
        /// <paramref name="tau"/>, raised towards the tail by <paramref name="amount"/>. Never lower.
        /// </summary>
        public static float Apply(float transmittance, float tau, float amount, float k)
        {
            if (amount <= 0f)
                return transmittance;

            return transmittance + amount * (Math.Max(transmittance, Tail(tau, k)) - transmittance);
        }

        public static string Describe(float setting)
        {
            if (AmountFor(setting) <= 0f)
                return "off (the light from before)";

            float k = KFor(setting);
            return (setting * 100f).ToString("F0", CultureInfo.InvariantCulture) + "% -- 10 optical depths in, " +
                   (Apply(0f, 10f, AmountFor(setting), k) * 100f).ToString("F0", CultureInfo.InvariantCulture) +
                   "% of the multiple-scattered sun gets through (before: Cumulus " +
                   (Math.Exp(-10f * CloudStyle.MultipleExtinction) * 100f).ToString("F1", CultureInfo.InvariantCulture) +
                   "%), 60 in " + (Tail(60f, k) * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%";
        }
    }
}
