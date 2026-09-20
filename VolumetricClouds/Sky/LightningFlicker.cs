using ColossalFramework.Math;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// How bright a lightning strike is at each moment of its life.
    /// </summary>
    /// <remarks>
    /// Kept apart from CloudLightning, and free of anything that needs a running Unity, so it
    /// can be checked offline (tools/test-lightning.ps1 loads the mod DLL and calls it).
    ///
    /// Two kinds of strike use it:
    ///
    /// THE GAME'S strikes must pulse exactly as the game draws them, because its ground lights
    /// and thunder run on its own clock: <see cref="Intensity"/> is a port of the formula in
    /// WeatherManager.EndRenderingImpl. The IL:
    ///   r = new Randomizer(start ^ (frame / 3)); level = r.Int32(0, 100) * 0.01
    ///   if (2 &lt;= age &lt;= 4) level = max(0.75, level)
    ///   t = clamp01(age / 20); envelope = (6.75 * t * (t * (t - 2) + 1))^2
    ///   intensity = lerp(level, envelope, m_flashingReduction)
    /// A random level reseeded every three frames and held high at the start, blended towards
    /// a smooth envelope -- [6.75 t (1 - t)^2]^2, peaking at 1 a third of the way in and gone
    /// by frame 20 of 30 -- as the flashing reduction rises. For these only what the game
    /// does not show may vary: power, reach, tint, the shape of the bolt.
    ///
    /// OUR OWN, visual-only strikes have no such partner, and identical strikes are what
    /// makes a storm look canned, so everything about them is drawn from a
    /// <see cref="Profile"/>: how long they last, how nervously they flicker, how many return
    /// strokes they have, how bright and far-reaching they are, what colour. The flashing
    /// reduction means the same thing in both: it irons the random flicker out.
    /// </remarks>
    public static class LightningFlicker
    {
        /// <summary>The game draws a strike for this many simulation frames.</summary>
        public const uint StrikeFrames = 30;

        /// <summary>One strike's character. All of it comes from the strike's own seed.</summary>
        public struct Profile
        {
            /// <summary>True for the game's strikes: timing is the game's, to the frame.</summary>
            public bool Exact;

            /// <summary>How long the strike lasts, in 60 Hz frames.</summary>
            public uint Frames;

            /// <summary>Frames each random level is held: 2 is a nervous flutter, 6 slow pulses.</summary>
            public uint Reseed;

            /// <summary>Return strokes: separate peaks inside the one strike.</summary>
            public int Strokes;

            /// <summary>Peak brightness, relative to a standard strike.</summary>
            public float Power;

            /// <summary>Metres its light carries through the cloud.</summary>
            public float Reach;

            public Color Tint;

            /// <summary>The game's strike: its timing, our choice of everything it does not show.</summary>
            public static Profile ForGameStrike(System.Random random)
            {
                return new Profile
                {
                    Exact = true,
                    Frames = StrikeFrames,
                    Reseed = 3,
                    Strokes = 1,
                    Power = Mathf.Lerp(0.8f, 1.35f, (float)random.NextDouble()),
                    Reach = Mathf.Lerp(1800f, 3000f, (float)random.NextDouble()),
                    Tint = RandomTint(random),
                };
            }

            /// <summary>One of ours: nothing about it is fixed.</summary>
            public static Profile Random(System.Random random, bool toGround)
            {
                // Mostly short, now and then a long rolling one: a quarter of a second to 1.6 s.
                float length = (float)random.NextDouble();
                uint frames = 14u + (uint)(82f * length * length);

                // One to four return strokes; a long strike always has more than one.
                double roll = random.NextDouble();
                int strokes = roll < 0.45 ? 1 : roll < 0.75 ? 2 : roll < 0.92 ? 3 : 4;
                if (frames > 55u && strokes < 2)
                    strokes = 2;

                float power = (float)random.NextDouble();

                return new Profile
                {
                    Exact = false,
                    Frames = frames,
                    Reseed = 2u + (uint)random.Next(0, 5),
                    Strokes = strokes,
                    Power = 0.55f + 0.95f * power * Mathf.Sqrt(power),
                    Reach = toGround
                        ? Mathf.Lerp(1600f, 3200f, (float)random.NextDouble())
                        : Mathf.Lerp(2200f, 4500f, (float)random.NextDouble()),
                    Tint = RandomTint(random),
                };
            }

            /// <summary>Mostly blue-white, sometimes violet, now and then a warm white.</summary>
            private static Color RandomTint(System.Random random)
            {
                double roll = random.NextDouble();
                float v = (float)random.NextDouble();

                if (roll < 0.7)
                    return new Color(Mathf.Lerp(0.72f, 0.84f, v), Mathf.Lerp(0.82f, 0.9f, v), 1f);
                if (roll < 0.9)
                    return new Color(Mathf.Lerp(0.8f, 0.9f, v), Mathf.Lerp(0.72f, 0.8f, v), 1f);
                return new Color(1f, Mathf.Lerp(0.9f, 0.96f, v), Mathf.Lerp(0.78f, 0.88f, v));
            }
        }

        /// <summary>0..1 before <see cref="Profile.Power"/>, for a strike of any profile.</summary>
        public static float Intensity(uint frame, uint start, float reduction, Profile profile)
        {
            if (profile.Exact)
                return Intensity(frame, start, reduction);

            uint age = frame - start;
            if (age >= profile.Frames)
                return 0f;

            float u = age / (float)profile.Frames;

            // Each return stroke is the game's own pulse shape, squeezed into its share of the
            // strike. They overlap, start at slightly irregular moments, and weaken in turn.
            // begin + width stays below 1 for every stroke, so each one finishes inside the
            // strike: the last one fades out instead of being chopped off.
            float envelope = 0f;
            float width = profile.Strokes == 1 ? 1f : 1.15f / profile.Strokes;

            for (int k = 0; k < profile.Strokes; k++)
            {
                float jitter = new Randomizer(start * 31u + (uint)k).Int32(0, 100) * 0.01f;
                float begin = (k + jitter * 0.45f) / profile.Strokes * (1f - width);

                float t = (u - begin) / width;
                if (t <= 0f || t >= 1f)
                    continue;

                float pulse = 6.75f * t * (t * (t - 2f) + 1f);
                envelope = Mathf.Max(envelope, pulse * pulse * (1f - 0.2f * k));
            }

            // The flicker rides on the envelope instead of replacing it, so a strike dies away
            // rather than being cut off at full flutter as the game's is.
            uint reseed = profile.Reseed < 1u ? 1u : profile.Reseed;
            float level = new Randomizer(start ^ (frame / reseed)).Int32(0, 100) * 0.01f;
            float flickering = envelope * (0.45f + 0.55f * level);

            return Mathf.Clamp01(Mathf.Lerp(flickering, envelope, reduction));
        }

        /// <summary>The game's formula, to the frame.</summary>
        public static float Intensity(uint frame, uint start, float reduction)
        {
            uint age = frame - start;

            Randomizer randomizer = new Randomizer(start ^ (frame / 3u));
            float level = randomizer.Int32(0, 100) * 0.01f;
            if (age >= 2u && age <= 4u)
                level = Mathf.Max(0.75f, level);

            float t = Mathf.Clamp01(age / 20f);
            float envelope = 6.75f * t * (t * (t - 2f) + 1f);
            envelope *= envelope;

            return Mathf.Lerp(level, envelope, reduction);
        }
    }
}
