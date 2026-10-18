using System;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// One funnel as the cloud pass draws it (1.5.0): one of our waterspouts (Waterspouts). Everything is
    /// in world metres; the shader gets it through <see cref="Funnels"/>, packed as FunnelShape.cginc
    /// reads it. (Its collar, wedge shapes and bow were also a Natural Disasters tornado's, removed
    /// 2026-10-19 and kept in the plan.)
    /// </summary>
    public struct Funnel
    {
        /// <summary>Where the axis meets the ground or the water.</summary>
        public Vector3 Ground;

        /// <summary>Where the axis meets the cloud base (y = the base).</summary>
        public Vector3 Top;

        /// <summary>The funnel's radius at the ground, metres.</summary>
        public float GroundRadius;

        /// <summary>Its radius just under the collar, metres.</summary>
        public float TopRadius;

        /// <summary>How much wider the collar under the cloud base flares, metres.</summary>
        public float CollarRadius;

        /// <summary>How the radius grows with height: over 1 a rope (thin until high), under 1 a cone or wedge.</summary>
        public float Exponent;

        /// <summary>
        /// Where the visible funnel ends, as a share of the height (0 ground, 1 base): a pointed tip
        /// <see cref="FunnelShape.TipLength"/> long. At -TipLength or lower it reaches the ground at full width.
        /// </summary>
        public float Bottom;

        /// <summary>0..1: how see-through its middle is (a waterspout's hollow tube).</summary>
        public float Hollow;

        /// <summary>The axis's bow at mid-height, metres (x, z): rope-out.</summary>
        public Vector2 Bend;

        /// <summary>0..1, the whole funnel's fade in and out.</summary>
        public float Opacity;

        /// <summary>Extinction per metre at full density.</summary>
        public float Density;

        /// <summary>The spray ring or dust whirl at the bottom: radius and height, metres.</summary>
        public float SkirtRadius;
        public float SkirtHeight;

        /// <summary>0 = a full whirl (dust), 1 = a ring (spray).</summary>
        public float SkirtRing;

        /// <summary>0..1: how much of the skirt there is (0 = none).</summary>
        public float SkirtStrength;

        /// <summary>The skirt's colour (linear albedo) and extinction per metre at full.</summary>
        public Color SkirtColor;
        public float SkirtDensity;

        /// <summary>The funnel's colour (linear albedo), and how much of the skirt's it takes near the ground.</summary>
        public Color FunnelColor;
        public float Dirt;

        /// <summary>How far it has turned, radians (0..2 pi); how far its pattern has risen, noise tiles (0..1).</summary>
        public float Spin;
        public float Rise;

        /// <summary>0..1: which part of the noise it is cut from.</summary>
        public float Seed;

        /// <summary>Radians its pattern turns from the ground to the base (a spiral).</summary>
        public float Twist;

        /// <summary>+1 or -1: which way round it turns (its flecks go the same way).</summary>
        public float Turning;

        /// <summary>
        /// 0..1: a faint, thin, wavering thread from the funnel's tip down to the surface -- one that
        /// stops part way with only "a very light/small" connection to the sea (the author, 2026-10-18).
        /// </summary>
        public float Thread;

        /// <summary>Metres it reaches up into the cloud above the base, fading out (so it hangs from the cloud, not under it).</summary>
        public float Overlap;

        /// <summary>0..1: how much the noise's wisps eat it (0 = its billows only): the fog's look.</summary>
        public float Wisp;

        /// <summary>What is under it, for the skirt, the flecks and the water.</summary>
        public GroundKind Kind;

        /// <summary>Metres from the camera (level), for the order and the flecks.</summary>
        public float Distance;

        /// <summary>For the log: "waterspout #3".</summary>
        public int Id;

        /// <summary>The funnel's height, ground to base.</summary>
        public float Height
        {
            get { return Mathf.Max(Top.y - Ground.y, 1f); }
        }
    }

    /// <summary>
    /// The funnel's shape (1.5.0): pure maths the shader (FunnelShape.cginc) does the same way, and
    /// its C# twin for the tests and the offline picture. A funnel is a soft tube round a curved
    /// axis from the ground to the cloud base, narrow at the bottom and flaring into a collar under
    /// the base, cut by the 3D cloud noise in a turning, rising frame; and a skirt -- the spray ring
    /// on water, a dust whirl on land -- round its foot.
    /// </summary>
    public static class FunnelShape
    {
        /// <summary>How many the shader takes (the nearest).</summary>
        public const int Max = 4;

        /// <summary>The pointed tip of a funnel that does not reach the ground, as a share of the height.</summary>
        public const float TipLength = 0.15f;

        /// <summary>Where the collar under the cloud base starts, as a share of the height.</summary>
        public const float CollarStart = 0.7f;

        /// <summary>How the collar flares from there: a trumpet's curve (a smoothstep made a flat plate, "a T").</summary>
        public const float CollarCurve = 2.2f;

        /// <summary>Metres of height one repeat of the 3D noise spans along the funnel.</summary>
        public const float NoiseMetres = 200f;

        /// <summary>
        /// The funnel thins from this far in (share of its radius) to its edge: soft all the way, like
        /// the fog (0.55 until 2026-10-18: "too opaque ... I'd like it to look kind of like our volumetric fog").
        /// </summary>
        public const float WallStart = 0.2f;

        /// <summary>The thread's radius, a share of the funnel's at the ground, and how dense it is beside the funnel.</summary>
        public const float ThreadRadius = 0.35f;
        public const float ThreadDensity = 0.8f;

        /// <summary>
        /// The most it is thick across: extinction per metre x radius (a wider part is thinner per metre).
        /// A wide collar is as see-through as a thick fog, never a solid (2026-10-19, round 3: a white goblet).
        /// </summary>
        public const float MaxDepth = 2.5f;

        /// <summary>
        /// The shader's light: from this share of the height up it turns from the fog's light to the cloud
        /// base's own (all of it where it meets the base), so the two meet in the same light.
        /// </summary>
        public const float CloudLightFrom = 0.4f;

        /// <summary>The bow's shape along the height: 0 at both ends, 1 in the middle.</summary>
        public static float BendShare(float s)
        {
            return 4f * s * (1f - s);
        }

        /// <summary>The axis at height share s (0 ground .. 1 base), xz.</summary>
        public static Vector2 Axis(Funnel f, float s)
        {
            return new Vector2(Mathf.Lerp(f.Ground.x, f.Top.x, s), Mathf.Lerp(f.Ground.z, f.Top.z, s)) + f.Bend * BendShare(s);
        }

        /// <summary>The radius at height share s before the tip: ground to top by the exponent, and the collar.</summary>
        public static float BodyRadius(Funnel f, float s)
        {
            s = Mathf.Clamp01(s);
            float collar = Mathf.Pow(Mathf.Max(Mathf.Clamp01((s - CollarStart) / (1f - CollarStart)), 1e-6f), CollarCurve);
            return f.GroundRadius + (f.TopRadius - f.GroundRadius) * Mathf.Pow(Mathf.Max(s, 1e-6f), Mathf.Max(f.Exponent, 0.05f))
                 + f.CollarRadius * collar;
        }

        /// <summary>0 below the visible bottom, rising to 1 over the tip; 1 everywhere for one that touches down.</summary>
        public static float Tip(Funnel f, float s)
        {
            return Mathf.Sqrt(Mathf.Clamp01((s - f.Bottom) / TipLength));
        }

        /// <summary>The radius at height share s, the tip included.</summary>
        public static float Radius(Funnel f, float s)
        {
            return BodyRadius(f, s) * Tip(f, s);
        }

        /// <summary>
        /// The level circle every part of it stands in (centre x, z; radius), and the most its edge
        /// moves sideways per metre of height (for the march's steps). The skirt included.
        /// </summary>
        public static Vector4 Bound(Funnel f)
        {
            const int Samples = 32;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            Vector2[] axis = new Vector2[Samples + 1];
            float[] radius = new float[Samples + 1];

            for (int k = 0; k <= Samples; k++)
            {
                float s = (float)k / Samples;
                axis[k] = Axis(f, s);
                radius[k] = BodyRadius(f, s);
                minX = Mathf.Min(minX, axis[k].x);
                maxX = Mathf.Max(maxX, axis[k].x);
                minZ = Mathf.Min(minZ, axis[k].y);
                maxZ = Mathf.Max(maxZ, axis[k].y);
            }

            Vector2 centre = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
            float reach = 0f;
            float slope = 0f;
            float dy = f.Height / Samples;

            for (int k = 0; k <= Samples; k++)
            {
                reach = Mathf.Max(reach, (axis[k] - centre).magnitude + radius[k]);
                if (k > 0)
                    slope = Mathf.Max(slope, ((axis[k] - axis[k - 1]).magnitude + Mathf.Abs(radius[k] - radius[k - 1])) / dy);
            }

            if (f.SkirtStrength > 0f)
                reach = Mathf.Max(reach, (new Vector2(f.Ground.x, f.Ground.z) - centre).magnitude + f.SkirtRadius);

            return new Vector4(centre.x, centre.y, reach, slope);
        }

        // ---- the C# twin of the shader's density (tests, the offline picture) ----------------

        /// <summary>
        /// The funnel's density at p, 0..1, as FunnelShape.cginc's FunnelStem: the tube's soft wall
        /// (hollow inside for a waterspout) cut by the noise the fog's way -- billows inside the shape, a
        /// swirl that bends them, wisps eating its edges -- plus the thread under its tip, never thicker
        /// across than <see cref="MaxDepth"/>, and fading out up into the cloud over the overlap (from
        /// under the base the shader also dims what stands in the cloud by the cloud in front of it,
        /// which is not here). <paramref name="noise"/> reads the 3D cloud noise at a
        /// lookup in tiles, red and green, AS THE SHADER READS IT (gamma-decoded).
        /// <paramref name="footprint"/>: metres a pixel covers there (a thin funnel is drawn wider and fainter).
        /// </summary>
        public static float StemDensity(Funnel f, Vector3 p, Func<Vector3, Vector2> noise, float footprint)
        {
            float height = f.Height;
            float s = (p.y - f.Ground.y) / height;
            float above = f.Overlap / height;
            if (s < 0f || s > 1f + above)
                return 0f;

            float sc = Mathf.Min(s, 1f);
            float tip = Tip(f, sc);
            float body = BodyRadius(f, sc);
            Vector2 v = new Vector2(p.x, p.z) - Axis(f, sc);
            float r = body * tip;
            float scale = 1f;
            float vertical = height;

            // With a thread, ONE tube: the funnel narrows into it (2026-10-18: a point with a separate rope
            // beside it was "a bit awkward"), thinning as it goes, wavering more the further below the tip,
            // patchier (a finer noise along it), a little stronger again just over the surface.
            if (f.Thread > 0f)
            {
                r = Mathf.Lerp(ThreadRadius * f.GroundRadius, body, tip);
                v -= ThreadWave(f, sc) * (1f - tip);
                scale = Mathf.Lerp(ThreadAlong(f, sc), 1f, tip);
                vertical = height * Mathf.Lerp(3f, 1f, tip);
            }

            if (r <= 0f)
                return 0f;

            // Drawn wider than it is (a pixel's width at the least), it is thinner by as much: the
            // same amount across, and evenly so -- not a cut that leaves specks.
            float wide = Mathf.Max(r, 0.8f * footprint);
            float x = v.magnitude / wide;
            if (x >= 1f)
                return 0f;

            // Never thicker across than MaxDepth; fading out only in the upper part of its reach into the
            // cloud (which hides it from below).
            float depth = Mathf.Min(1f, MaxDepth / Mathf.Max(f.Density * r, 1e-4f));
            float density = Tube(f, v / wide, x, sc, vertical, noise) * (r / wide) * scale * depth;
            if (s > 1f)
                density *= 1f - SmoothStep(0.4f, 1f, (s - 1f) / above);
            return density;
        }

        /// <summary>The thread's density along it, at height share s: strongest under the tip, faint mid-way, a little more over the surface.</summary>
        public static float ThreadAlong(Funnel f, float s)
        {
            return ThreadDensity * f.Thread * (0.35f + 0.65f * SmoothStep(f.Bottom - 0.3f, f.Bottom, s))
                 + 0.3f * f.Thread * (1f - SmoothStep(0f, 0.08f, s));
        }

        /// <summary>The tube's density at x (its share of the radius), as FunnelShape.cginc's FunnelTube.</summary>
        private static float Tube(Funnel f, Vector2 unit, float x, float s, float height, Func<Vector3, Vector2> noise)
        {
            float wall = 1f - SmoothStep(WallStart, 1f, x);
            // Hollow down the middle, but full where it meets the cloud (a thin, hollow collar read as "an
            // empty space right under the cloud").
            float hollowness = f.Hollow * (1f - SmoothStep(0.6f, 0.95f, s));
            float hollow = 1f - hollowness * (1f - SmoothStep(0.1f, 0.6f, x));
            float shaped = wall * hollow;

            Vector2 w = Turn(unit, f.Spin + f.Twist * s);
            Vector3 q = new Vector3(w.x * 0.35f + f.Seed * 7.13f, s * (height / NoiseMetres) - f.Rise + f.Seed * 3.31f, w.y * 0.35f + f.Seed * 5.37f);

            // The swirl (the fog's): a slower look of the same noise bends the lookup.
            Vector2 swirl = noise(q * 0.5f + new Vector3(0.13f, 0.29f, 0.71f));
            float sx = Mathf.Sqrt(swirl.x) - 0.5f, sy = Mathf.Sqrt(swirl.y) - 0.5f;
            q += new Vector3(sx * 0.25f, sx * 0.3f, sy * 0.25f);

            Vector2 n = noise(q);
            float billow = Mathf.Sqrt(n.x), wisp = Mathf.Sqrt(n.y);
            float d = shaped * Mathf.Clamp01((billow - (1f - shaped)) / Mathf.Max(shaped, 1e-4f));
            return d * Mathf.Lerp(1f, wisp, f.Wisp * (0.4f + 0.6f * x));
        }

        /// <summary>
        /// Where the thread's axis is off the funnel's at height share s: a slow, turning wave. The turn
        /// only ever whole times (it wraps at 2 pi), or the wave would jump.
        /// </summary>
        public static Vector2 ThreadWave(Funnel f, float s)
        {
            return new Vector2(Mathf.Sin(s * 9f + f.Spin), Mathf.Cos(s * 7f + f.Spin + 1.3f)) * (0.5f * f.GroundRadius);
        }

        /// <summary>The skirt's density at p, 0..1, as FunnelShape.cginc's FunnelSkirt.</summary>
        public static float SkirtDensity(Funnel f, Vector3 p, Func<Vector3, Vector2> noise)
        {
            if (f.SkirtStrength <= 0f)
                return 0f;

            float z = (p.y - f.Ground.y) / Mathf.Max(f.SkirtHeight, 1f);
            if (z < -0.05f || z > 1f)
                return 0f;

            Vector2 v = new Vector2(p.x - f.Ground.x, p.z - f.Ground.z);
            float x = v.magnitude / Mathf.Max(f.SkirtRadius, 1f);
            if (x >= 1f)
                return 0f;

            float shaped = SkirtShape(f.SkirtRing, x, z);
            Vector2 w = Turn(v / Mathf.Max(f.SkirtRadius, 1f), 2f * f.Spin);
            Vector3 q = new Vector3(w.x * 0.6f + f.Seed * 2.91f, z * 0.5f - 2f * f.Rise + f.Seed, w.y * 0.6f + f.Seed * 1.73f);
            float n = Mathf.Sqrt(noise(q).y);

            return f.SkirtStrength * shaped * Mathf.Clamp01((n - (1f - shaped)) / Mathf.Max(shaped, 1e-4f));
        }

        /// <summary>
        /// The skirt's shape before the noise at x (share of its radius) and z (share of its height):
        /// a ring widening upwards (the spray's cascade) or a full whirl, thinning to its top.
        /// </summary>
        public static float SkirtShape(float ring, float x, float z)
        {
            float centre = Mathf.Lerp(0.25f, 0.75f, Mathf.Sqrt(Mathf.Clamp01(z)));
            float d = (x - centre) / 0.18f;
            float band = Mathf.Exp(-d * d);
            float full = 1f - SmoothStep(0.2f, 1f, x);
            float top = Mathf.Pow(Mathf.Max(1f - Mathf.Clamp01(z), 1e-6f), 1.5f);
            return Mathf.Lerp(full, band, ring) * top * SmoothStep(-0.05f, 0.05f, z);
        }

        /// <summary>v turned by a radians.</summary>
        public static Vector2 Turn(Vector2 v, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>HLSL's smoothstep.</summary>
        public static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
