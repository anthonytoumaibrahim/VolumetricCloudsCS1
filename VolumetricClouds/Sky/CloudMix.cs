using System;
using System.Globalization;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// THE MIXED STYLE (1.5.0; the author, 2026-10-05: "mixing the cloud style together, so at low
    /// overcast a mix of cumulus and classic clouds, and at higher intensity focus on classic layer
    /// ... the proportions can be randomized, but ... at full overcast the entire sky is covered").
    /// SEPARATE CLOUDS, each its own size and height, with the CLASSIC LAYER coming in as the
    /// intensity rises and taking over: at 100% the layer is Classic's own 100%.
    /// </summary>
    /// <remarks>
    /// Why: at low intensity both other styles draw a few big clumps of one height -- Classic stands
    /// the weather map's ~3 km blobs up as pancakes, Cumulus keeps only the peaks of a noise that
    /// repeats every 2.3 km. The amount of cloud was never the problem (at 47%: 2-5% of the sky in
    /// all three styles); how it is split up was. Chosen on offline renders (the author picked "C"):
    /// <list type="bullet">
    /// <item>THE SEPARATE CLOUDS: a grid of cells over the weather map (a whole number per repeat, so
    /// they drift and tile with it), at most one cloud in a cell. The intensity says how many cells
    /// hold one (<see cref="FieldShare"/>), more where the map's normal score is high
    /// (<see cref="Gather"/>: EVE's "the map says where they gather"); a new cloud forms out of the
    /// noise and grows (<see cref="FadeIn"/>, <see cref="Grow"/>). More small clouds than big ones;
    /// height follows width. Its body is three overlapping bubbles, but only as its ENVELOPE: the
    /// profile inside them reaches <see cref="CoreCover"/> at most, so the Cumulus noise's own surface
    /// is the cloud (EVE's "in-between coverage") -- cauliflower, never a smooth dome (solid domes
    /// were rejected in-game as "jellyfish caps").</item>
    /// <item>THE CLOUD MAP: where the clouds stand does not change from pixel to pixel, so it is not
    /// worked out per pixel. <see cref="Baker"/> rasterises every cloud's envelope into one texture
    /// over one weather repeat -- Horizon's Nubis does the same with its cloud maps -- on a worker
    /// thread, again when the intensity moves (CloudMixMap). The shader reads it once per step; empty
    /// sky stops there, and the empty-sky skip jumps the cells no cloud reaches
    /// (<see cref="Baker.Presence"/>).</item>
    /// <item>ONE NOISE FOR ALL OF THEM (1.5.0; the first builds, 2026-10-05, read it per cloud, at
    /// the cloud's own scale and place): read where the cloud is, at one repeat for the sky
    /// (<see cref="NoiseTile"/>), moving with the clouds. No id map, no per-cloud numbers in the
    /// shader, and no crease where two clouds meet -- a per-cloud noise switched there.</item>
    /// <item>THE LAYER: Classic's, its thickness following the map as under Cumulus, cut at
    /// <see cref="LayerCover"/>: none at low intensity, Classic's own cover from ~85% up.</item>
    /// <item>THE TYPE (random proportions; HZD's cloud-type channel): a slow pattern from the city's
    /// seed (<see cref="TypeAt"/>, periodic with the weather map, in the cloud map's alpha) tips each
    /// stretch of sky towards separate clouds or towards layer; it drifts with the clouds and fades
    /// out by 100%.</item>
    /// </list>
    /// Rain, lightning's cloud test and the rainbow's use the SMALLEST layer cover
    /// (<see cref="LayerCoverInCumulusArea"/>), so rain always falls from drawn cloud (invariant 2).
    ///
    /// Pure: no engine calls (tools/test-mix.ps1, the offline preview, which renders from these very
    /// maps). Every number and <see cref="Hash"/> is part of what a city's seed reproduces
    /// (invariant 14): <see cref="Generator"/>.
    /// </remarks>
    public static class CloudMix
    {
        /// <summary>What these numbers, the hash and the bake are. Bump it with any change to what a seed draws.</summary>
        public const int Generator = 1;

        // ---- the separate clouds -------------------------------------------------------------

        /// <summary>Metres per cell aimed at; the real cell is the weather repeat over <see cref="Cells"/>.</summary>
        public const float CellMetres = 1000f;

        /// <summary>The main bubble's radius, in cells: the smallest and the largest cloud.</summary>
        public const float RadiusMin = 0.16f, RadiusMax = 0.42f;

        /// <summary>Above 1: more small clouds than big ones.</summary>
        public const float SizePower = 1.6f;

        /// <summary>A cloud's height over its main bubble's radius, and how much that varies (+- half).</summary>
        public const float Aspect = 1.1f, AspectVariety = 0.5f;

        /// <summary>The two other bubbles: how far out (share of the radius), how big, how tall.</summary>
        public const float SatelliteDistance = 0.45f, SatelliteDistanceSpan = 0.4f;
        public const float SatelliteRadius = 0.4f, SatelliteRadiusSpan = 0.35f;
        public const float SatelliteHeight = 0.45f, SatelliteHeightSpan = 0.75f;

        /// <summary>How far a cloud's bubbles reach from its point, over its main radius.</summary>
        public const float Extent = SatelliteDistance + SatelliteDistanceSpan + SatelliteRadius + SatelliteRadiusSpan;

        /// <summary>The profile rises to full over this share of a bubble's radius, in from its surface.</summary>
        public const float Band = 0.6f;

        /// <summary>EVE's in-between coverage: the most the profile reaches, how deep the noise carves, the edge hardness.</summary>
        public const float CoreCover = 0.5f, ErosionDepth = 0.8f, Hardness = 0.9f;

        /// <summary>
        /// The Cumulus noise's repeat over the sky's mean cloud radius (<see cref="NoiseTile"/>): its
        /// lobes at about the clouds' own scale.
        /// </summary>
        public const float NoiseOverRadius = 2f;

        /// <summary>
        /// The Cumulus noise's middle (measured on its level 0: p10 0.50, p50 0.70, p90 0.84) and how
        /// much its spread is stretched round it.
        /// </summary>
        public const float NoiseMiddle = 0.7f, NoiseContrast = 2f;

        /// <summary>Metres over which a cloud's flat base comes in.</summary>
        public const float BaseSoftness = 30f;

        /// <summary>Share of cells added per unit of the map's normal score at the cell; a new cloud grows to its full size over <see cref="Grow"/>.</summary>
        public const float Gather = 0.1f, Grow = 0.12f;

        /// <summary>
        /// The share of <see cref="Grow"/> over which a new cloud forms: its profile rises from 0, so it
        /// comes out of the noise's highest lobes instead of appearing whole (it starts at 45% of its
        /// size). A cloud the intensity takes away goes the same way.
        /// </summary>
        public const float FadeIn = 0.3f;

        /// <summary>Extinction per metre at full density, at the default Density (the Cumulus style's).</summary>
        public const float Extinction = 0.03f;

        /// <summary>The intensity (0..1) -> share of cells holding a cloud, before the gathering. Linear between the keys.</summary>
        public static readonly float[] ShareKeys = { 0f, 0.15f, 0.30f, 0.47f, 0.65f, 0.80f, 0.90f, 1f };
        public static readonly float[] ShareValues = { 0f, 0.06f, 0.15f, 0.30f, 0.42f, 0.50f, 0.55f, 0.55f };

        public static float FieldShare(float intensity)
        {
            float x = Clamp01(intensity);
            for (int i = 1; i < ShareKeys.Length; i++)
            {
                if (x <= ShareKeys[i])
                {
                    float f = (x - ShareKeys[i - 1]) / (ShareKeys[i] - ShareKeys[i - 1]);
                    return ShareValues[i - 1] + (ShareValues[i] - ShareValues[i - 1]) * f;
                }
            }

            return ShareValues[ShareValues.Length - 1];
        }

        /// <summary>Cells per weather repeat: a whole number, at least one, at most 255.</summary>
        public static int Cells(float weatherTile)
        {
            return Math.Max(1, Math.Min(255, (int)Math.Round(weatherTile / CellMetres)));
        }

        /// <summary>Metres per cell for this weather repeat.</summary>
        public static float CellSize(float weatherTile)
        {
            return weatherTile / Cells(weatherTile);
        }

        /// <summary>Metres above the base the tallest cloud can reach: the march's slab must hold it.</summary>
        public static float Tallest(float weatherTile)
        {
            return Tallest(weatherTile, Shape.Plain);
        }

        /// <summary>The same for the player's shape: its size and height, and a big cloud's.</summary>
        public static float Tallest(float weatherTile, Shape shape)
        {
            float tallestMain = RadiusMax * CellSize(weatherTile) * (Aspect + AspectVariety * 0.5f);
            float big = shape.Big > 0f ? BigMost * BigTaller : 1f;
            return tallestMain * Math.Max(1f, SatelliteHeight + SatelliteHeightSpan) * shape.Size * shape.Height * big;
        }

        /// <summary>Metres per unit of the map's band channel: the widest band any bubble of this shape can have.</summary>
        public static float BandScale(float weatherTile, Shape shape)
        {
            float big = shape.Big > 0f ? BigMost : 1f;
            return Band * RadiusMax * CellSize(weatherTile) * shape.Size * big;
        }

        /// <summary>A full-grown cloud's mean main radius in metres (the size's power: the mean of u^k is 1 / (k + 1)).</summary>
        public static float MeanRadius(float weatherTile)
        {
            return CellSize(weatherTile) * (RadiusMin + (RadiusMax - RadiusMin) / (1f + SizePower));
        }

        /// <summary>Metres per repeat of the noise the separate clouds are carved from (~500 m at 13.5 km).</summary>
        public static float NoiseTile(float weatherTile)
        {
            return NoiseTile(weatherTile, Shape.Plain);
        }

        /// <summary>The same for the player's shape: the lobes grow with the clouds.</summary>
        public static float NoiseTile(float weatherTile, Shape shape)
        {
            return NoiseOverRadius * MeanRadius(weatherTile) * shape.Size;
        }

        /// <summary>Metres over which the sky light's height is measured, for the player's shape.</summary>
        public static float LightSpanFor(Shape shape)
        {
            return LightSpan * shape.Size * shape.Height;
        }

        // ---- the player's shape (1.5.0) ---------------------------------------------------------

        /// <summary>
        /// The player's shaping of the separate clouds, as one bake reads it: Cumulus size, height
        /// and amount (rows shared with the Cumulus style's heaps), and Mixed's Big clouds and Cloud
        /// shapes. <see cref="Plain"/> -- 1, 1, 1, 0, 0 -- is the clouds as first built, exactly.
        /// (Texture variety is the shader's alone: no bake.)
        /// </summary>
        public sealed class Shape
        {
            /// <summary>Times the clouds' width and height (0.5..2).</summary>
            public readonly float Size;

            /// <summary>Times their height alone (0.5..2).</summary>
            public readonly float Height;

            /// <summary>Times the share of cells holding a cloud (0..2).</summary>
            public readonly float Amount;

            /// <summary>Big clouds, 0..1: the share of clouds that grow big, over <see cref="BigShareMost"/>.</summary>
            public readonly float Big;

            /// <summary>Cloud shapes, 0..1: round (three bubbles) to irregular (more, spread, drawn out).</summary>
            public readonly float Outline;

            public Shape(float size, float height, float amount, float big, float outline)
            {
                Size = Math.Max(0.5f, Math.Min(2f, size));
                Height = Math.Max(0.5f, Math.Min(2f, height));
                Amount = Math.Max(0f, Math.Min(2f, amount));
                Big = Clamp01(big);
                Outline = Clamp01(outline);
            }

            public static readonly Shape Plain = new Shape(1f, 1f, 1f, 0f, 0f);

            public bool SameAs(Shape other)
            {
                return other != null && Size == other.Size && Height == other.Height && Amount == other.Amount &&
                       Big == other.Big && Outline == other.Outline;
            }

            public override string ToString()
            {
                CultureInfo c = CultureInfo.InvariantCulture;
                return "size " + (Size * 100f).ToString("F0", c) + "%, height " + (Height * 100f).ToString("F0", c) + "%, amount " +
                       (Amount * 100f).ToString("F0", c) + "%, big clouds " + (Big * 100f).ToString("F0", c) + "%, shapes " +
                       (Outline * 100f).ToString("F0", c) + "%";
            }
        }

        /// <summary>
        /// BIG CLOUDS: at 100% one cloud in five grows big (one in ten at 50%) -- the occasional
        /// towering cumulus among the small ones. The same clouds at any setting (a cloud's own random
        /// number decides), so raising it only adds big ones.
        /// </summary>
        public const float BigShareMost = 0.2f;

        /// <summary>
        /// A big cloud's width over its own (its random number picks within), and how much taller still
        /// it gets. The tallest stays near the Cumulus heaps' (~1.2 km at the default sizes): the slab
        /// the march spreads its steps over ends at the tallest cloud, and at 2.1 x 1.15 (1.6 km) the
        /// preview's far clouds went grainy wherever the camera sat inside it.
        /// </summary>
        public const float BigLeast = 1.4f, BigMost = 1.7f, BigTaller = 1.1f;

        /// <summary>
        /// CLOUD SHAPES at 100%: a fourth bubble on every cloud and a fifth on 60% of them, the side
        /// bubbles spread this much further out, and the cloud drawn out up to this much longer one way
        /// (each its own way: the game's wind swings every few minutes, and clouds lined up with it
        /// would have to turn in place).
        /// </summary>
        public const float OutlineFifth = 0.6f, OutlineSpread = 0.6f, OutlineStretch = 0.7f;

        /// <summary>
        /// TEXTURE VARIETY at 100% (the shader's: CloudShaderParams.ApplyMix): where the type leans to
        /// separate clouds their noise repeats this much wider (chunkier lobes), where it leans to
        /// layer this much finer; blended between.
        /// </summary>
        public const float TextureChunky = 0.6f, TextureFine = 0.35f;

        // ---- the light's cheap looks ----------------------------------------------------------

        /// <summary>
        /// The far light samples (24 m and more towards the sun) and the looks up for the sky light
        /// read the clouds WITHOUT the noise: the carved density's mean at that profile, which is what
        /// a long stretch of cloud averages to. Measured on the Cumulus noise (every mip level within
        /// 0.01) for <see cref="CoreCover"/>, <see cref="ErosionDepth"/>, <see cref="Hardness"/>,
        /// <see cref="NoiseMiddle"/> and <see cref="NoiseContrast"/> -- tools/test-mix.ps1 checks the
        /// fit against them: mean = gain x (profile - start)^1.5, within 0.011 everywhere (the best
        /// power, start and gain on a grid, 2026-10-12).
        /// </summary>
        public const float EnvelopeStart = 0.19f, EnvelopeGain = 0.82f;

        /// <summary>The C# twin of the shader's cheap look (density 0..1 before the base and Density).</summary>
        public static float EnvelopeDensity(float profile)
        {
            float t = Math.Max(profile - EnvelopeStart, 0f);
            return EnvelopeGain * t * (float)Math.Sqrt(t);
        }

        /// <summary>The C# twin of the shader's carving at one noise value (0..1 as read), before the base and Density.</summary>
        public static float CarvedDensity(float profile, float noise)
        {
            float n = Clamp01((noise - NoiseMiddle) * NoiseContrast + 0.5f);
            return Clamp01((profile * CoreCover - ErosionDepth * (1f - n)) / (1f - Hardness));
        }

        // ---- the layer and the type ------------------------------------------------------------

        /// <summary>The layer's cover at this intensity: none at low intensity, Classic's own from ~85% up.</summary>
        public static float LayerCover(float intensity)
        {
            return intensity * SmoothStep(0.25f, 0.85f, intensity);
        }

        /// <summary>The same in a stretch of sky the type tips towards layer: it comes in earlier. Never less than the other two.</summary>
        public static float LayerCoverInLayerArea(float intensity)
        {
            return intensity * SmoothStep(0.05f, 0.7f, intensity);
        }

        /// <summary>The same where the type tips towards separate clouds: later. Never more than the other two.</summary>
        public static float LayerCoverInCumulusArea(float intensity)
        {
            return intensity * SmoothStep(0.45f, 0.95f, intensity);
        }

        /// <summary>How strongly the type tips the balance: fully up to 90%, not at all at 100%.</summary>
        public static float TypeStrength(float intensity)
        {
            return 1f - SmoothStep(0.9f, 1f, intensity);
        }

        /// <summary>The separate clouds' share multiplied by this in a layer area, and in a separate-cloud area.</summary>
        public const float ShareInLayerArea = 0.4f, ShareInCumulusArea = 1.6f;

        /// <summary>The type pattern: value noise at these periods per weather repeat, with these weights.</summary>
        public const int TypePeriodA = 2, TypePeriodB = 3;
        public const float TypeWeightA = 0.65f, TypeWeightB = 0.35f;

        /// <summary>How hard the pattern is pushed towards its two ends (0 layer, 1 separate clouds).</summary>
        public const float TypeContrast = 2.5f;

        /// <summary>
        /// The type at (u, v) of the weather map's repeat (0..1, periodic), before the intensity's
        /// <see cref="TypeStrength"/>: 0 tips towards layer, 1 towards separate clouds.
        /// </summary>
        public static float TypeAt(float u, float v, float seedX, float seedZ)
        {
            float a = ValueNoise(u * TypePeriodA, v * TypePeriodA, TypePeriodA, 40f, seedX, seedZ);
            float b = ValueNoise(u * TypePeriodB, v * TypePeriodB, TypePeriodB, 41f, seedX, seedZ);
            float t = Clamp01(0.5f + (a * TypeWeightA + b * TypeWeightB - 0.5f) * TypeContrast);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Metres over which the sky light's height is measured (the previews' ambient).</summary>
        public const float LightSpan = 1250f;

        // ---- the map ---------------------------------------------------------------------------

        /// <summary>Texels per side of the cloud map, over one weather repeat (26 m at 13.5 km).</summary>
        public const int MapSize = 512;

        /// <summary>The bake is redone when the intensity moves by this much (a cloud's radius then moves by metres).</summary>
        public const float BakeStep = 0.005f;

        /// <summary>The tallest cloud a bake reports is rounded up to this many metres (Baker.TallestTop): the slab moves in steps.</summary>
        public const float TopStep = 50f;

        /// <summary>
        /// The type pattern as the cloud map's alpha holds it: <paramref name="size"/>^2 bytes, texel
        /// centres at (i + 0.5) / size like the GPU's. It only changes with the seed.
        /// </summary>
        public static byte[] BakeType(int size, float seedX, float seedZ)
        {
            byte[] bytes = new byte[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                    bytes[j * size + i] = ToByte(TypeAt((i + 0.5f) / size, (j + 0.5f) / size, seedX, seedZ));

            return bytes;
        }

        /// <summary>
        /// Rasterises the clouds for one intensity into the cloud map, and marks the empty-sky map's
        /// cells they reach. Reuse one instance: it keeps its buffers (the bake is redone as the weather
        /// changes; no garbage for the game's collector). Not thread-safe: one bake at a time per instance.
        /// </summary>
        public sealed class Baker
        {
            /// <summary>
            /// The CLOUD MAP, read bilinear, RGBA per texel: r = the horizontal profile (0 at a cloud's
            /// edge, 1 a band in; times a forming cloud's fade), g = the top over the base /
            /// <see cref="TallestTop"/>, b = the band of the bubble that set the top / BandScale, a = the type.
            /// </summary>
            public readonly byte[] Map;

            /// <summary>
            /// Metres above the base of the tallest cloud the last bake drew, rounded up to
            /// <see cref="TopStep"/> (0 with none): the map's tops are stored over it, and the march's
            /// slab is cut there.
            /// </summary>
            public float TallestTop { get; private set; }

            /// <summary>
            /// Per cell of the empty-sky map (<see cref="PresenceSize"/>^2, its layout: Sky.EmptySky),
            /// 255 where a point could read any separate cloud from the map, else 0. The map is read
            /// bilinear, so each cloud reaches one and a half texels further than its bubbles.
            /// </summary>
            public readonly byte[] Presence;

            public readonly int Size;
            public readonly int PresenceSize;

            /// <summary>Clouds the last bake drew, the texels they cover, and the empty-sky cells they reach (for the log).</summary>
            public int Clouds { get; private set; }
            public int Covered { get; private set; }
            public int CellsReached { get; private set; }

            private readonly float[] _bestProfile;
            private readonly float[] _bestTop;

            public Baker(int size, int presenceSize)
            {
                Size = size;
                PresenceSize = presenceSize;
                Map = new byte[size * size * 4];
                Presence = new byte[presenceSize * presenceSize];
                _bestProfile = new float[size * size];
                _bestTop = new float[size * size];
            }

            /// <summary>The clouds as first built (<see cref="Shape.Plain"/>).</summary>
            public void Bake(byte[] scores, int scoreSize, byte[] type, float intensity, float weatherTile, float seedX, float seedZ)
            {
                Bake(scores, scoreSize, type, intensity, weatherTile, seedX, seedZ, Shape.Plain);
            }

            /// <summary>
            /// The clouds at <paramref name="intensity"/>. <paramref name="scores"/>: the weather map's
            /// normal scores as CloudDensityField.ScoreBytes stores them (<paramref name="scoreSize"/>^2,
            /// read bilinear like the GPU). <paramref name="type"/>: <see cref="BakeType"/> at this size.
            /// <paramref name="shape"/>: the player's; the map's tops and bands are stored over its
            /// <see cref="Tallest(float, Shape)"/> and <see cref="BandScale"/>.
            /// </summary>
            public void Bake(byte[] scores, int scoreSize, byte[] type, float intensity, float weatherTile, float seedX, float seedZ, Shape shape)
            {
                Array.Clear(Map, 0, Map.Length);
                Array.Clear(Presence, 0, Presence.Length);
                Array.Clear(_bestProfile, 0, _bestProfile.Length);
                Array.Clear(_bestTop, 0, _bestTop.Length);
                Clouds = 0;
                Covered = 0;
                CellsReached = 0;
                TallestTop = 0f;

                for (int i = 0; i < type.Length; i++)
                    Map[i * 4 + 3] = type[i];

                int n = Cells(weatherTile);
                float cell = weatherTile / n;
                float texel = weatherTile / Size;
                float tallest = Tallest(weatherTile, shape);
                float bandScale = BandScale(weatherTile, shape);
                float baseShare = FieldShare(intensity) * shape.Amount;
                float strength = TypeStrength(intensity);
                if (baseShare <= 0f)
                    return;

                float[] bx = new float[5], bz = new float[5], br = new float[5], bh = new float[5];

                for (int cz = 0; cz < n; cz++)
                {
                    for (int cx = 0; cx < n; cx++)
                    {
                        float rank = Hash(cx, cz, 1f, seedX, seedZ);
                        float jx = 0.2f + 0.6f * Hash(cx, cz, 2f, seedX, seedZ);
                        float jz = 0.2f + 0.6f * Hash(cx, cz, 3f, seedX, seedZ);
                        float u = (cx + jx) / n, v = (cz + jz) / n;

                        // Cumulus amount scales the share, the map's gathering included (0%: none at all).
                        float z = (Bilinear(scores, scoreSize, u, v) * 2f - 1f) * 3f;
                        float t = 0.5f + (TypeAt(u, v, seedX, seedZ) - 0.5f) * strength;
                        float share = (baseShare + Gather * z * shape.Amount) * (ShareInLayerArea + (ShareInCumulusArea - ShareInLayerArea) * t);
                        if (rank >= share)
                            continue;

                        float grow = Clamp01((share - rank) / Grow);
                        float fade = SmoothStep(0f, FadeIn, grow);
                        float size = (float)Math.Pow(Hash(cx, cz, 4f, seedX, seedZ), SizePower);
                        float r0 = cell * (RadiusMin + (RadiusMax - RadiusMin) * size) * (0.45f + 0.55f * grow) * shape.Size;
                        float h0 = r0 * (Aspect + AspectVariety * (Hash(cx, cz, 5f, seedX, seedZ) - 0.5f)) * shape.Height;

                        // Big clouds (keys 40, 41): this cloud's own number decides, so a higher setting
                        // only adds big ones.
                        if (shape.Big > 0f && Hash(cx, cz, 40f, seedX, seedZ) < BigShareMost * shape.Big)
                        {
                            float grown = BigLeast + (BigMost - BigLeast) * Hash(cx, cz, 41f, seedX, seedZ);
                            r0 *= grown;
                            h0 *= grown * BigTaller;
                        }

                        // Cloud shapes (keys 42..45): more bubbles, spread further, the cloud drawn out
                        // one way. At 0 none of it: three bubbles as first built.
                        int bubbles = 3;
                        float spread = 1f, stretch = 1f, along = 0f;
                        if (shape.Outline > 0f)
                        {
                            if (Hash(cx, cz, 42f, seedX, seedZ) < shape.Outline)
                                bubbles++;
                            if (Hash(cx, cz, 43f, seedX, seedZ) < shape.Outline * OutlineFifth)
                                bubbles++;
                            spread = 1f + OutlineSpread * shape.Outline;
                            stretch = 1f + OutlineStretch * shape.Outline * Hash(cx, cz, 44f, seedX, seedZ);
                            along = 6.2831853f * Hash(cx, cz, 45f, seedX, seedZ);
                        }

                        float ca = (float)Math.Cos(along), sa = (float)Math.Sin(along);

                        // The bubbles, relative to the cloud's point, in its own (unstretched) frame
                        // (keys 10..25: four per side bubble).
                        bx[0] = 0f; bz[0] = 0f; br[0] = r0; bh[0] = h0;
                        float reach = r0;
                        for (int b = 1; b < bubbles; b++)
                        {
                            float k = 6f + 4f * b;
                            float angle = 6.2831853f * Hash(cx, cz, k, seedX, seedZ);
                            float dist = r0 * (SatelliteDistance + SatelliteDistanceSpan * spread * Hash(cx, cz, k + 1f, seedX, seedZ));
                            bx[b] = (float)Math.Cos(angle) * dist;
                            bz[b] = (float)Math.Sin(angle) * dist;
                            br[b] = r0 * (SatelliteRadius + SatelliteRadiusSpan * Hash(cx, cz, k + 2f, seedX, seedZ));
                            bh[b] = h0 * (SatelliteHeight + SatelliteHeightSpan * Hash(cx, cz, k + 3f, seedX, seedZ));
                            reach = Math.Max(reach, dist + br[b]);
                        }

                        // Drawn out, it reaches that much further (along its own way).
                        reach *= stretch;

                        Clouds++;

                        // The texels the cloud can touch; their centres are at (i + 0.5) x texel.
                        float centreX = (cx + jx) * cell, centreZ = (cz + jz) * cell;
                        int i0 = (int)Math.Floor((centreX - reach) / texel - 0.5f), i1 = (int)Math.Ceiling((centreX + reach) / texel - 0.5f);
                        int j0 = (int)Math.Floor((centreZ - reach) / texel - 0.5f), j1 = (int)Math.Ceiling((centreZ + reach) / texel - 0.5f);

                        for (int j = j0; j <= j1; j++)
                        {
                            float wz = (j + 0.5f) * texel - centreZ;
                            int row = Wrap(j, Size) * Size;
                            for (int i = i0; i <= i1; i++)
                            {
                                // Into the cloud's own frame: turned to its way, the stretch taken out.
                                float wx = (i + 0.5f) * texel - centreX;
                                float dx = (wx * ca + wz * sa) / stretch;
                                float dz = -wx * sa + wz * ca;
                                float profile = 0f, top = 0f, band = 0f;
                                for (int b = 0; b < bubbles; b++)
                                {
                                    float ex = dx - bx[b], ez = dz - bz[b];
                                    float q = (float)Math.Sqrt(ex * ex + ez * ez) / br[b];
                                    if (q >= 1f)
                                        continue;
                                    profile = Math.Max(profile, Clamp01((1f - q) / Band));
                                    float h = bh[b] * (float)Math.Sqrt(1f - q * q);
                                    if (h > top)
                                    {
                                        top = h;
                                        band = Band * br[b];
                                    }
                                }

                                // A forming cloud's profile rises with its fade. (A profile a byte cannot
                                // hold is none: the shader reads it as empty sky.)
                                profile *= fade;
                                if (profile < 0.5f / 255f)
                                    continue;

                                int index = row + Wrap(i, Size);
                                int o = index * 4;
                                if (_bestProfile[index] <= 0f)
                                    Covered++;

                                // The deepest profile and the highest top here (with its band).
                                if (profile > _bestProfile[index])
                                {
                                    _bestProfile[index] = profile;
                                    Map[o] = ToByte(profile);
                                }

                                if (top > _bestTop[index])
                                {
                                    _bestTop[index] = top;
                                    Map[o + 2] = ToByte(band / bandScale);
                                    TallestTop = Math.Max(TallestTop, top);
                                }
                            }
                        }

                        // The empty-sky cells the cloud reaches: a point reads the map bilinear, so up to a
                        // texel and a half (diagonally) past the last texel inside a bubble.
                        Mark(centreX, centreZ, reach + 1.5f * texel, weatherTile);
                    }
                }

                // The tops over the tallest cloud actually here (rounded up to TopStep), not over the
                // tallest the shape allows: the march's slab ends there. With big clouds or a raised
                // height the worst case is several times the usual, and a slab that tall spreads the
                // march's steps thin (grain), or holds the camera.
                if (TallestTop <= 0f)
                    return;
                TallestTop = Math.Min((float)Math.Ceiling(TallestTop / TopStep) * TopStep, Math.Max(tallest, TopStep));
                for (int k = 0; k < _bestTop.Length; k++)
                {
                    if (_bestTop[k] > 0f)
                        Map[k * 4 + 1] = ToByteUp(_bestTop[k] / TallestTop);
                }
            }

            // Every empty-sky cell (wrapping) a disc of `radius` metres round (x, z) touches.
            private void Mark(float x, float z, float radius, float weatherTile)
            {
                int m = PresenceSize;
                float cell = weatherTile / m;
                int i0 = (int)Math.Floor((x - radius) / cell), i1 = (int)Math.Floor((x + radius) / cell);
                int j0 = (int)Math.Floor((z - radius) / cell), j1 = (int)Math.Floor((z + radius) / cell);
                for (int j = j0; j <= j1; j++)
                {
                    float nearZ = Math.Max(j * cell, Math.Min(z, (j + 1) * cell)) - z;
                    for (int i = i0; i <= i1; i++)
                    {
                        float nearX = Math.Max(i * cell, Math.Min(x, (i + 1) * cell)) - x;
                        if (nearX * nearX + nearZ * nearZ > radius * radius)
                            continue;

                        int index = Wrap(j, m) * m + Wrap(i, m);
                        if (Presence[index] == 0)
                        {
                            Presence[index] = 255;
                            CellsReached++;
                        }
                    }
                }
            }
        }

        // ---- the hash ---------------------------------------------------------------------------

        /// <summary>
        /// The cell's random number <paramref name="k"/> (1, 2, ...), 0..1: Hoskins' "hash without
        /// sine" on the cell's whole-number coordinates plus the seed's offsets.
        /// </summary>
        public static float Hash(float cellX, float cellZ, float k, float seedX, float seedZ)
        {
            float px = Frac((cellX + seedX) * 0.1031f);
            float py = Frac((cellZ + seedZ) * 0.1031f);
            float pz = Frac(k * 0.1031f);
            float dot = px * (pz + 31.32f) + py * (py + 31.32f) + pz * (px + 31.32f);
            px += dot;
            py += dot;
            pz += dot;
            return Frac((px + py) * pz);
        }

        /// <summary>The seed's offsets into the hash (whole numbers, 17..1040 and 53..1076).</summary>
        public static void SeedOffsets(int seed, out float x, out float z)
        {
            unchecked
            {
                uint s = (uint)seed;
                x = (s & 1023u) + 17f;
                z = ((s >> 10) & 1023u) + 53f;
            }
        }

        // ---- for the log ----------------------------------------------------------------------

        /// <summary>The style's shape at this weather repeat, once, for the log.</summary>
        public static string DescribeShape(float weatherTile)
        {
            return DescribeShape(weatherTile, Shape.Plain);
        }

        /// <summary>The same for the player's shape.</summary>
        public static string DescribeShape(float weatherTile, Shape shape)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            float cell = CellSize(weatherTile);
            return "separate clouds on " + Cells(weatherTile) + " x " + Cells(weatherTile) + " cells of " + cell.ToString("F0", c) +
                   " m per weather repeat, " + (2f * RadiusMin * cell * 0.45f * shape.Size).ToString("F0", c) + "-" +
                   (2f * RadiusMax * cell * shape.Size).ToString("F0", c) + " m across, up to " + Tallest(weatherTile, shape).ToString("F0", c) +
                   " m tall, carved from the Cumulus noise repeating every " + NoiseTile(weatherTile, shape).ToString("F0", c) +
                   " m, baked into a " + MapSize + "^2 cloud map (" + (weatherTile / MapSize).ToString("F0", c) +
                   " m a texel); the Classic layer from ~25% up, Classic's own cover from 85%; " + shape + " (generator " + Generator + ")";
        }

        /// <summary>The mix at this intensity, for the detailed log.</summary>
        public static string DescribeMix(float intensity)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return "clouds in " + (FieldShare(intensity) * 100f).ToString("F0", c) + "% of cells, layer cover " +
                   (LayerCoverInCumulusArea(intensity) * 100f).ToString("F0", c) + "/" + (LayerCover(intensity) * 100f).ToString("F0", c) + "/" +
                   (LayerCoverInLayerArea(intensity) * 100f).ToString("F0", c) + "%, type " + (TypeStrength(intensity) * 100f).ToString("F0", c) + "%";
        }

        // ---- helpers ----------------------------------------------------------------------------

        /// <summary>One byte per texel (CloudDensityField.ScoreBytes) read bilinear at (u, v), wrapping, as the GPU reads it.</summary>
        public static float Bilinear(byte[] values, int size, float u, float v)
        {
            float x = u * size - 0.5f, y = v * size - 0.5f;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            int xa = Wrap(x0, size), xb = Wrap(x0 + 1, size), ya = Wrap(y0, size), yb = Wrap(y0 + 1, size);
            float a = values[ya * size + xa], b = values[ya * size + xb];
            float c = values[yb * size + xa], d = values[yb * size + xb];
            float top = a + (b - a) * fx, bottom = c + (d - c) * fx;
            return (top + (bottom - top) * fy) / 255f;
        }

        /// <summary>Smooth value noise over a lattice of <paramref name="period"/> cells, periodic, from the hash.</summary>
        private static float ValueNoise(float x, float y, int period, float k, float seedX, float seedZ)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int xa = Wrap(x0, period), xb = Wrap(x0 + 1, period), ya = Wrap(y0, period), yb = Wrap(y0 + 1, period);
            float a = Hash(xa, ya, k, seedX, seedZ), b = Hash(xb, ya, k, seedX, seedZ);
            float c = Hash(xa, yb, k, seedX, seedZ), d = Hash(xb, yb, k, seedX, seedZ);
            float top = a + (b - a) * fx, bottom = c + (d - c) * fx;
            return top + (bottom - top) * fy;
        }

        private static float SmoothStep(float a, float b, float x)
        {
            float t = Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static float Clamp01(float v)
        {
            return Math.Max(0f, Math.Min(1f, v));
        }

        private static float Frac(float v)
        {
            return v - (float)Math.Floor(v);
        }

        private static int Wrap(int i, int n)
        {
            int r = i % n;
            return r < 0 ? r + n : r;
        }

        private static byte ToByte(float v)
        {
            return (byte)Math.Max(0, Math.Min(255, (int)(v * 255f + 0.5f)));
        }

        /// <summary>Rounded up: a top stored a little high never cuts a cloud's crown off.</summary>
        private static byte ToByteUp(float v)
        {
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Ceiling(v * 255f)));
        }
    }
}
