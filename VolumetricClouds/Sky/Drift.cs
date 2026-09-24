using System;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The maths that keeps every drift exact for the life of a city. Pure: no engine calls,
    /// so tools/test-skystate.ps1 runs it against the built DLL.
    /// </summary>
    /// <remarks>
    /// A city keeps its sky in its save, so the drifts grow for as long as it is played. A
    /// float cannot carry that: at 2^23 m its step is a whole metre, and the clouds move about
    /// half a metre a frame. So the drifts are doubles, and no shader ever sees one. Each
    /// texture lookup gets its own <see cref="Phase"/> instead -- how far through one repeat
    /// of that lookup the drift has gone -- which is exact, not an approximation: a lookup a
    /// whole repeat further along reads the same texel, because the textures wrap.
    /// </remarks>
    public static class Drift
    {
        /// <summary>
        /// How fast the top of the fog layer used to pull ahead of its base, per metre the fog
        /// drifts: the old shader's <c>F * (1 + 0.35 h)</c>. Still the rate it starts at.
        /// </summary>
        public const double FogLeadRate = 0.35;

        /// <summary>The furthest the top of the fog gets ahead of (or behind) its base, in metres.</summary>
        public const double FogLeadReach = 400.0;

        /// <summary>
        /// Metres of drift for the top to swing ahead, back, behind and back again: chosen so
        /// that it starts at <see cref="FogLeadRate"/>. About 7.2 km, five and a half minutes
        /// at the default fog speed and 1x.
        /// </summary>
        public const double FogLeadCycle = 2.0 * Math.PI * FogLeadReach / FogLeadRate;

        /// <summary>
        /// frac(metres / period): the share of one repeat a lookup is shifted by, 0..1. A shader
        /// subtracts it from <c>p / period</c> where it used to subtract <c>offset / period</c>;
        /// the two differ by a whole number of repeats, which reads the same texel.
        /// </summary>
        public static float Phase(double metres, double period)
        {
            if (!(period > 0.0))
                return 0f;

            double t = metres / period;
            double phase = t - Math.Floor(t);
            if (double.IsNaN(phase) || double.IsInfinity(phase))
                return 0f;

            // A double just under 1 can round to exactly 1f, which is the same place as 0.
            float result = (float)phase;
            return result >= 1f ? 0f : result;
        }

        /// <summary>
        /// How far the top of the fog layer is carried beyond its base, in metres (x and z),
        /// for a fog that has drifted (<paramref name="driftX"/>, <paramref name="driftZ"/>).
        /// </summary>
        /// <remarks>
        /// The top of the layer is carried further than the base, which drags on the ground,
        /// so billows lean and shear instead of sliding past as one rigid pattern. It used to
        /// be <c>0.35 x the whole drift</c>, which never stopped growing: ten minutes into a
        /// session the top was 4.5 km ahead of a base a few hundred metres below it, and the
        /// billows had been sheared into thin sheets, then stripes. Rendered offline
        /// (2026-09-21) before this was built. Now the lead swings between
        /// <see cref="FogLeadReach"/> ahead and as far behind, like gusts over a calmer
        /// surface layer: the top's speed wanders between 0.65x and 1.35x the base's, it starts
        /// exactly as the old one did (0.35 per metre), and it is a function of the drift alone,
        /// so a save puts it back exactly and it stays small forever.
        /// </remarks>
        public static void FogLead(double driftX, double driftZ, out float leadX, out float leadZ)
        {
            double distance = Math.Sqrt(driftX * driftX + driftZ * driftZ);

            // Reach * sin(2 pi d / cycle) / d, which tends to the old rate as d -> 0.
            double scale = distance < 1e-6
                ? FogLeadRate
                : FogLeadReach * Math.Sin(2.0 * Math.PI * distance / FogLeadCycle) / distance;

            if (double.IsNaN(scale) || double.IsInfinity(scale))
                scale = 0.0;

            leadX = (float)(driftX * scale);
            leadZ = (float)(driftZ * scale);
        }

        /// <summary>
        /// (x, z) turned by <paramref name="degrees"/>: (x cos - z sin, x sin + z cos), the turn
        /// CloudCommon.cginc applies to the fragment lookup. A turned drift is still a function of
        /// the saved drift alone, so a lookup read through it restores exactly.
        /// </summary>
        public static void Turn(double x, double z, double degrees, out double tx, out double tz)
        {
            double a = degrees * Math.PI / 180.0;
            double c = Math.Cos(a);
            double s = Math.Sin(a);

            tx = x * c - z * s;
            tz = x * s + z * c;
        }
    }
}
