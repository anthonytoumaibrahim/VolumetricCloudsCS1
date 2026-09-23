using System;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// What a picked cloud colour does to the light: a multiplier that changes the HUE of the
    /// clouds and never their brightness (the author's choice: "brightness should be
    /// independent from color"). Brightness stays the brightness settings' job alone.
    /// </summary>
    /// <remarks>
    /// Pure, so tools/test-color.ps1 checks it on the built DLL. The steps:
    ///   1. Any grey -- white, black and everything between -- has no hue: exactly (1, 1, 1),
    ///      short-circuited. White is the default and must change NOTHING, bit for bit, and
    ///      neither an sRGB decode nor a luminance sum is guaranteed to give 1.0f exactly.
    ///      x * 1f == x, so the shader then gets the very numbers it got before colours existed.
    ///   2. sRGB -> linear: the game renders in linear space, where #808080 is 0.216.
    ///   3. The brightest channel is scaled to 1. How light or dark the pick is does not count,
    ///      only its hue and saturation: otherwise #400000 would make near-black clouds that no
    ///      gain could lift.
    ///   4. The result is scaled so its luminance is 1, like white's. Capped at MaxGain: pure
    ///      red (luminance 0.21) would need 4.7x and pure blue (0.07) 14x, which blows out to
    ///      neon; with the cap those two still come out darker (64% and 22%). Pastels and
    ///      ordinary tints are exact.
    /// </remarks>
    public static class CloudTint
    {
        public const float MaxGain = 3f;

        public static Color Of(Color32 srgb)
        {
            if (srgb.r == srgb.g && srgb.g == srgb.b)
                return new Color(1f, 1f, 1f, 1f);

            float r = ToLinear(srgb.r);
            float g = ToLinear(srgb.g);
            float b = ToLinear(srgb.b);

            // Not a grey, so at least one channel is above zero.
            float brightest = Math.Max(r, Math.Max(g, b));
            r /= brightest;
            g /= brightest;
            b /= brightest;

            float luminance = Luminance(r, g, b);
            float gain = Math.Min(1f / luminance, MaxGain);
            return new Color(r * gain, g * gain, b * gain, 1f);
        }

        /// <summary>Rec. 709 luminance of a linear colour.</summary>
        public static float Luminance(float r, float g, float b)
        {
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        /// <summary>The sRGB transfer function, decoded: 0..255 to linear 0..1.</summary>
        public static float ToLinear(byte channel)
        {
            double c = channel / 255.0;
            return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }

        /// <summary>
        /// The tint for something that cannot go above 1 (a vertex or material colour on the
        /// billboard fallback): scaled so its brightest channel is 1. White stays exactly white.
        /// </summary>
        public static Color Displayable(Color tint)
        {
            float brightest = Math.Max(tint.r, Math.Max(tint.g, tint.b));
            if (brightest <= 1f)
                return new Color(tint.r, tint.g, tint.b, 1f);

            return new Color(tint.r / brightest, tint.g / brightest, tint.b / brightest, 1f);
        }
    }
}
