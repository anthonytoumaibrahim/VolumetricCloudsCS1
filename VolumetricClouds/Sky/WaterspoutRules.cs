using System;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>One of our waterspouts (1.5.0, Sky.Waterspouts): what it was given when it formed, and where it is now.</summary>
    public sealed class Spout
    {
        public int Id;

        /// <summary>Seconds of simulation it lasts, and has lasted (below 0: a family member still to come).</summary>
        public float Life;
        public float Age;

        /// <summary>One in five reaches the surface (<see cref="WaterspoutRules.TouchDownShare"/>); the rest stop part way.</summary>
        public bool TouchesDown;

        /// <summary>Where one that stops part way ends, a share of the height.</summary>
        public float AloftBottom;

        /// <summary>
        /// 0..1: a faint thread from its tip down to the surface -- one that stops around the middle with
        /// only "a very light/small" connection to the sea (the author, 2026-10-18). 0 = none.
        /// </summary>
        public float Thread;

        public float GroundRadius;
        public float TopRadius;
        public float Collar;
        public float Hollow;
        public float SkirtRadius;
        public float SkirtHeight;

        /// <summary>An aloft one's spray ring: its circulation reaches the water, its funnel does not (0 = none).</summary>
        public float RingAloft;

        /// <summary>The top trails this share of the height downwind; the axis bows this share sideways (sign: which way).</summary>
        public float Lean;
        public float BendShare;
        public float BendSign;

        public float Strength;
        public float Seed;
        public float Twist;

        /// <summary>Radians a second it turns; metres a second its pattern rises.</summary>
        public float SpinRate;
        public float RiseRate;
        public double SpinPhase;
        public double RiseMetres;

        /// <summary>Where it formed (y = the surface), and the clouds' drift then: it rides with its cloud.</summary>
        public Vector3 Origin;
        public double DriftX;
        public double DriftZ;

        /// <summary>Where it is now (y = the surface), and what it is over.</summary>
        public Vector3 Ground;
        public GroundKind Kind;

        /// <summary>It formed over water (a landspout otherwise).</summary>
        public bool OverWater;

        /// <summary>Its age when it came ashore, after which it dies (a waterspout does not live over land); -1: it has not.</summary>
        public float LandfallAt = -1f;
    }

    /// <summary>
    /// Our waterspouts' rules (1.5.0), pure: how often, how big, how they live. The author
    /// (2026-10-18): "In real life, in climates such as the one in Lebanon, very small tornadoes form
    /// over the sea or close to the sky, and rarely they touch the ground. But they are very very small
    /// in size", "they don't go over a certain intensity, or turn to a tornado, they are just sprouts".
    /// Visual only.
    /// </summary>
    public static class WaterspoutRules
    {
        /// <summary>At once, at most (the cloud pass takes four funnels).</summary>
        public const int MaxOwn = 3;

        /// <summary>
        /// Events a second of simulation in showery weather at 100%: one an hour. Some attempts
        /// find no water under cloud near the view, so on a coastal map that is about one sighting
        /// every two hours of showery play (the plan's "most like real life"). Inland, none.
        /// </summary>
        public const float BaseRate = 1f / 3600f;

        /// <summary>How far from the camera they form, metres (level).</summary>
        public const float MinDistance = 1500f;
        public const float MaxDistance = 12000f;

        /// <summary>The share aimed into the view (as lightning's), the rest all round.</summary>
        public const float InViewShare = 0.7f;

        /// <summary>The share of attempts that may form over land, within <see cref="ShoreReach"/> of water.</summary>
        public const float LandShare = 0.1f;
        public const float ShoreReach = 1000f;

        /// <summary>A waterspout that comes ashore dies within this many seconds.</summary>
        public const float LandfallSeconds = 20f;

        /// <summary>Its life: forming (the funnel comes down), then decaying (rope-out), as shares of it.</summary>
        public const float FormShare = 0.3f;
        public const float DecayShare = 0.25f;

        /// <summary>A family: now and then two or three under one line of cloud.</summary>
        public const float FamilyChance = 0.2f;

        /// <summary>
        /// How they end (the author, 2026-10-18: "not all watersprouts should actually reach the water
        /// fully. In real life some sprouts stop around the middle and the connection with the sea is a
        /// very light/small"): this share reaches the surface, this share stops part way with a faint
        /// thread down to a spray ring, and the rest stay up there.
        /// </summary>
        public const float TouchDownShare = 0.2f;
        public const float ThreadShare = 0.3f;

        /// <summary>
        /// Extinction per metre at full (0.12 until 2026-10-18: "too opaque"; with the fog's soft wall and
        /// hollow core this is about half as dense, and see-through at its edges).
        /// </summary>
        public const float Density = 0.08f;

        /// <summary>How much the weather allows, 0..1: a showery sky (cover over ~half, rain about).</summary>
        public static float Weather(float coverage, float rain)
        {
            return FunnelShape.SmoothStep(0.45f, 0.75f, coverage) * (rain > 0.02f ? 1f : 0.35f);
        }

        /// <summary>Events a second of simulation: the chance (1 = 100%) times the base rate times the weather.</summary>
        public static float EventsPerSecond(float chance, float coverage, float rain)
        {
            return Mathf.Max(0f, chance) * BaseRate * Weather(coverage, rain);
        }

        /// <summary>A new waterspout's character, before it has a place: how it ends is the dice's too.</summary>
        public static Spout Make(System.Random random, bool overWater)
        {
            Spout s = new Spout();
            float u = (float)random.NextDouble();

            // Small, most of them very small: 5 to 30 m across at the bottom.
            s.GroundRadius = 2.5f + 12.5f * u * u;
            s.TopRadius = s.GroundRadius * Range(random, 1.8f, 3f) + 6f;
            s.Collar = Range(random, 40f, 110f) + 2f * s.GroundRadius;
            s.Hollow = overWater ? Range(random, 0.45f, 0.7f) : 0.2f;

            s.OverWater = overWater;
            double end = random.NextDouble();
            s.TouchesDown = end < TouchDownShare;
            if (s.TouchesDown)
            {
                s.AloftBottom = 0f;
            }
            else if (end < TouchDownShare + ThreadShare)
            {
                // Around the middle, with a faint thread down to its ring.
                s.AloftBottom = Range(random, 0.35f, 0.6f);
                s.Thread = Range(random, 0.5f, 1f);
                s.RingAloft = overWater ? Range(random, 0.4f, 0.8f) : Range(random, 0.3f, 0.5f);
            }
            else
            {
                // Up there; now and then its circulation still stirs the water under it.
                s.AloftBottom = Range(random, 0.3f, 0.75f);
                s.RingAloft = overWater && random.NextDouble() < 0.3 ? Range(random, 0.2f, 0.4f) : 0f;
            }

            // The spray ring: up to ~135 m across round the biggest.
            s.SkirtRadius = overWater ? s.GroundRadius * Range(random, 2.5f, 4f) + 8f : s.GroundRadius * Range(random, 2.5f, 3.5f) + 6f;
            s.SkirtHeight = overWater ? Range(random, 10f, 35f) : Range(random, 8f, 25f);

            s.Lean = Range(random, 0.06f, 0.22f);
            s.BendShare = Range(random, 0.03f, 0.08f);
            s.BendSign = random.NextDouble() < 0.5 ? -1f : 1f;
            s.Strength = Range(random, 0.7f, 1f);
            s.Seed = (float)random.NextDouble();
            s.Twist = Range(random, -2.5f, 2.5f);

            // Its wind at its middle, ~20 m/s, either way round; its pattern rising 6-12 m/s.
            float middle = 0.5f * (s.GroundRadius + s.TopRadius);
            s.SpinRate = Mathf.Clamp(20f / middle, 0.4f, 2f) * (random.NextDouble() < 0.5 ? -1f : 1f);
            s.RiseRate = Range(random, 6f, 12f);
            s.SpinPhase = random.NextDouble() * Math.PI * 2.0;

            // One to five minutes, the short ones likelier.
            s.Life = 60f + 240f * Mathf.Pow((float)random.NextDouble(), 1.5f);
            return s;
        }

        /// <summary>
        /// Where it is in its life: its opacity, where its funnel ends (share of the height), how much
        /// it has thinned (rope-out: 1 = not), how much its bow has grown, its skirt's strength and its
        /// thread's.
        /// </summary>
        public static void Stage(Spout s, out float opacity, out float bottom, out float thin, out float bow, out float skirt, out float thread)
        {
            float a = Mathf.Clamp01(s.Age / Mathf.Max(s.Life, 1f));
            float form = Smooth01(a / FormShare);
            float decay = Smooth01((a - (1f - DecayShare)) / DecayShare);
            float envelope = Smooth01(a / 0.06f) * (1f - Smooth01((a - 0.9f) / 0.1f));

            opacity = s.Strength * envelope;

            float target = s.TouchesDown ? -FunnelShape.TipLength : s.AloftBottom;
            float down = 1f - (1f - form) * (1f - form);
            bottom = Mathf.Lerp(Mathf.Lerp(0.92f, target, down), 0.92f, Mathf.Pow(decay, 1.3f));

            thin = 1f - 0.55f * decay;
            bow = 1f + 3f * decay;

            // The spray ring shows before the funnel reaches the water, and goes as it lifts. One that
            // stops part way has its ring once it is well down, and its thread with it.
            skirt = s.TouchesDown
                ? FunnelShape.SmoothStep(0.6f, 0.05f, bottom) * envelope
                : s.RingAloft * FunnelShape.SmoothStep(s.AloftBottom + 0.3f, s.AloftBottom + 0.05f, bottom) * envelope;
            thread = s.Thread * FunnelShape.SmoothStep(s.AloftBottom + 0.3f, s.AloftBottom + 0.05f, bottom) * envelope;

            // Come ashore: it ropes out, lifts and fades over its last seconds.
            if (s.LandfallAt >= 0f)
            {
                float land = Mathf.Clamp01((s.Age - s.LandfallAt) / LandfallSeconds);
                opacity *= 1f - land;
                skirt *= 1f - land;
                thread *= 1f - land;
                thin *= 1f - 0.5f * land;
                bottom = Mathf.Lerp(bottom, 0.92f, land);
            }
        }

        /// <summary>
        /// Its funnel now, for a cloud base at <paramref name="cloudBase"/> and the wind blowing towards
        /// <paramref name="wind"/> (level, unit). False when there is no room under the base.
        /// </summary>
        public static bool Shape(Spout s, float cloudBase, Vector3 wind, out Funnel f)
        {
            f = new Funnel();
            float height = cloudBase - s.Ground.y;
            if (height < 60f)
                return false;

            float opacity, bottom, thin, bow, skirt, thread;
            Stage(s, out opacity, out bottom, out thin, out bow, out skirt, out thread);
            if (opacity <= 0.001f && skirt <= 0.001f)
                return false;

            Vector3 level = new Vector3(wind.x, 0f, wind.z);
            Vector3 along = level.sqrMagnitude > 1e-6f ? level.normalized : Vector3.right;
            Vector3 side = new Vector3(along.z, 0f, -along.x) * s.BendSign;

            f.Ground = s.Ground;
            f.Top = new Vector3(s.Ground.x, cloudBase, s.Ground.z) + along * (s.Lean * height * (2f - thin));
            f.Bend = new Vector2(side.x, side.z) * (s.BendShare * height * bow);
            f.GroundRadius = s.GroundRadius * thin;
            f.TopRadius = s.TopRadius * thin;
            f.CollarRadius = s.Collar;
            f.Exponent = 2.4f;
            f.Bottom = bottom;
            f.Hollow = s.Hollow;
            f.Opacity = opacity;
            f.Density = Density;
            f.Thread = thread;
            f.Wisp = 0.6f;

            f.Kind = s.Kind;
            f.SkirtRadius = s.SkirtRadius;
            f.SkirtHeight = s.SkirtHeight;
            f.SkirtRing = GroundLook.SkirtRing(s.Kind);
            f.SkirtStrength = skirt;
            f.SkirtColor = GroundLook.SkirtColor(s.Kind);
            f.SkirtDensity = s.Kind == GroundKind.Water ? 0.03f : 0.025f;

            f.FunnelColor = s.OverWater ? new Color(0.78f, 0.79f, 0.82f) : new Color(0.62f, 0.60f, 0.58f);
            f.Dirt = s.OverWater ? 0f : 0.35f;
            f.Spin = Drift.Phase(s.SpinPhase, Math.PI * 2.0) * (float)(Math.PI * 2.0);
            f.Rise = Drift.Phase(s.RiseMetres, FunnelShape.NoiseMetres);
            f.Seed = s.Seed;
            f.Twist = s.Twist;
            f.Turning = s.SpinRate < 0f ? -1f : 1f;
            f.Id = s.Id;
            return true;
        }

        private static float Range(System.Random random, float a, float b)
        {
            return a + (b - a) * (float)random.NextDouble();
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
