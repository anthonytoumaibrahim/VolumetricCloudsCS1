using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// How much of the game's sky in a direction is the sun's glow -- the C# twin of SkyGlowPass in
    /// CloudMedia.cginc, for the log and tools/test-placement.ps1. Pure.
    /// </summary>
    /// <remarks>
    /// The clouds dissolve into the distance by going see-through, and what a faded far cloud lets
    /// through towards the sun is the sun's disc and its glow, bright enough to burn the clouds
    /// white. 1.2.1 to 1.3.x kept the far clouds solid in a cone round the sun instead; that showed
    /// as a disc round it (the author, 2026-10-06: "the sun in-game has a huge ring around it").
    /// Since 1.4.0 the share that dissolves passes only the part of the sky that is NOT the sun's:
    /// the game's skybox maths (Hidden/DayNight/Skybox, from its inputs, which DayNightProperties
    /// sets globally) worked out as it is and without the glow -- the sun's disc term out, the Mie
    /// phase held at its value 90 degrees from the sun. Since 1.5.0 that share is painted instead
    /// of let through (<see cref="Paint"/>, SkyPaint): a cubemap no longer shows through the far
    /// clouds. Any change here goes into the shader too.
    /// </remarks>
    public sealed class SkyGlow
    {
        /// <summary>Towards the game's sun.</summary>
        public Vector3 SunDir = Vector3.up;

        /// <summary>The skybox's _SunSize (32 / DayNightProperties.m_SunSize).</summary>
        public float SunSize = 20f;

        public Vector3 BetaR, BetaM, MiePhaseG, MieConst, NightZenith, NightHorizon;
        public Vector4 SkyMultiplier;
        public Vector2 ColorCorrection = Vector2.one;

        /// <summary>
        /// The share of the sky along <paramref name="dir"/> (normalised) that is not the sun's
        /// disc or glow, 0..1: 1 away from the sun.
        /// </summary>
        public float Pass(Vector3 dir)
        {
            float lumFull, lumGlowless;
            Lumas(dir, out lumFull, out lumGlowless);
            return lumFull > 1e-6f ? Mathf.Clamp01(lumGlowless / lumFull) : 1f;
        }

        /// <summary>The sky's brightness along <paramref name="dir"/>, as it is and without the sun's disc and glow.</summary>
        private void Lumas(Vector3 dir, out float lumFull, out float lumGlowless)
        {
            Vector3 full, glowless;
            Sky(dir, out full, out glowless);
            lumFull = Luma(ToneMap(full));
            lumGlowless = Luma(ToneMap(glowless));
        }

        /// <summary>
        /// The painted sky (1.5.0, CloudMedia SkyPaint): the game's sky along <paramref name="dir"/>
        /// without the sun's disc and glow, graded as the skybox writes it -- <paramref name="compressed"/>:
        /// its variant that squeezes the light under white first.
        /// </summary>
        public Vector3 Paint(Vector3 dir, bool compressed)
        {
            Vector3 full, glowless;
            Sky(dir, out full, out glowless);
            if (compressed)
                for (int i = 0; i < 3; i++)
                    glowless[i] = 1f - Mathf.Exp(-glowless[i]);
            return ToneMap(glowless);
        }

        /// <summary>
        /// The painted sky's brightness <paramref name="degrees"/> round from the sun at
        /// <paramref name="elevation"/> degrees up -- for the log and tests.
        /// </summary>
        public float PaintAround(float degrees, float elevation, bool compressed)
        {
            Vector3 sun = SunDir.sqrMagnitude > 1e-8f ? SunDir.normalized : Vector3.up;
            Vector3 flat = new Vector3(sun.x, 0f, sun.z);
            flat = flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector3.forward;
            float a = degrees * Mathf.Deg2Rad;
            Vector3 way = flat * Mathf.Cos(a) + new Vector3(flat.z, 0f, -flat.x) * Mathf.Sin(a);
            float e = elevation * Mathf.Deg2Rad;
            return Luma(Paint((way * Mathf.Cos(e) + Vector3.up * Mathf.Sin(e)).normalized, compressed));
        }

        /// <summary>The skybox's maths along <paramref name="dir"/>, before its grading: as it is, and without the sun's disc and glow.</summary>
        private void Sky(Vector3 dir, out Vector3 full, out Vector3 glowless)
        {
            float c = Vector3.Dot(dir, SunDir);
            float h = Mathf.Max(dir.y + 0.06f, 0.06f);
            float hw = Mathf.Max(dir.y, 0f);
            float sR = 8f / h;
            float sM = 1.2f / h;

            float phase = MiePhaseG.x * Mathf.Pow(Mathf.Max(MiePhaseG.y - MiePhaseG.z * c, 1e-6f), -1.5f);
            float side = MiePhaseG.x * Mathf.Pow(Mathf.Max(MiePhaseG.y, 1e-6f), -1.5f);
            float ray = (1f + c * c) * SkyMultiplier.y;
            float disc = SunDir.y > -0.1f ? Mathf.Min(Mathf.Pow(Mathf.Max(SunSize * (1f - c), 1e-6f), -1.5f), 1000f) : 0f;
            bool dusk = SunDir.y < 0.25f;

            Vector3 q = Vector3.zero, tr = Vector3.zero, e = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                float x = sR * NightZenith[i];
                q[i] = x * (2f - x);
                tr[i] = Mathf.Exp(-(BetaR[i] * sR + BetaM[i] * sM));
                e[i] = q[i] * tr[i] + SkyMultiplier.x * ((1f - tr[i]) - tr[i] * q[i]);
            }

            float ex = Mathf.Abs(e.x) < 1e-6f ? 1e-6f : e.x;
            full = Vector3.zero;
            glowless = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                float mie = sM * e[i] / ex * MieConst[i];
                float night = dusk ? NightHorizon[i] * q[i] : 0f;
                full[i] = (0.75f * e[i] + phase * mie) * ray + night + Mathf.Min(mie, hw) * disc * tr[i];
                glowless[i] = (0.75f * e[i] + Mathf.Min(phase, side) * mie) * ray + night;
            }
        }

        /// <summary>
        /// The sky's grading, never its compression (its HDR_OFF variant's 1 - exp): on compressed
        /// values the share at the sun was 64% and the sun showed through the clouds (2026-10-06).
        /// Uncompressed it is exact for an HDR sky and holds back more round the sun, never less,
        /// for a compressed one.
        /// </summary>
        private Vector3 ToneMap(Vector3 v)
        {
            for (int i = 0; i < 3; i++)
                v[i] = Mathf.Pow(Mathf.Max(v[i] * ColorCorrection.x, 1e-10f), ColorCorrection.y);
            return v;
        }

        private static float Luma(Vector3 v)
        {
            return 0.2126f * v.x + 0.7152f * v.y + 0.0722f * v.z;
        }

        /// <summary>
        /// The share <paramref name="degrees"/> away from the sun, turning sideways from it (90 =
        /// level, square to the sun) -- for the log and tests.
        /// </summary>
        public float PassAround(float degrees)
        {
            return Pass(Around(degrees));
        }

        /// <summary>
        /// The brightness of what a fully faded far cloud lets through <paramref name="degrees"/>
        /// away from the sun: the share times the sky. For tests -- it must not jump anywhere.
        /// </summary>
        public float ThroughAround(float degrees)
        {
            float lumFull, lumGlowless;
            Lumas(Around(degrees), out lumFull, out lumGlowless);
            return lumFull > 1e-6f ? Mathf.Clamp01(lumGlowless / lumFull) * lumFull : lumFull;
        }

        private Vector3 Around(float degrees)
        {
            Vector3 sun = SunDir.sqrMagnitude > 1e-8f ? SunDir.normalized : Vector3.up;
            Vector3 side = new Vector3(sun.z, 0f, -sun.x);
            if (side.sqrMagnitude < 1e-8f)
                side = Vector3.right;
            side.Normalize();

            float a = degrees * Mathf.Deg2Rad;
            return (sun * Mathf.Cos(a) + side * Mathf.Sin(a)).normalized;
        }
    }
}
