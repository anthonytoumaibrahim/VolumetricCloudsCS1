using System;
using System.Globalization;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// THE SOFT STYLE (1.5.0, <see cref="CloudStyle.Soft"/>): fair-weather clouds made of puffs, soft at
    /// the edges. Asked for after the author's own UE5 game, whose stock clouds (Unreal's
    /// m_SimpleVolumetricCloud) "look so soft, unlike ours which look like hard blocks", most of all at
    /// low cover; round 3 (2026-10-17) after a photograph of real ones: "they're just too lumpy ... in
    /// real life, clouds are wispy like cotton candy, they don't just all come in in one big lump/blob
    /// ... different random shapes and sizes, and parts of them are floating away".
    /// <list type="bullet">
    /// <item>The weather map only says where clouds gather and how big they may grow (c, 0 at the cut,
    /// 1 <see cref="Look.Span"/> in). The SHAPE is the Cumulus style's noise (<see cref="CumulusNoise3D"/>,
    /// 8 octaves of puffs on puffs), cutting deep (<see cref="Look.Erosion"/>): a cloud is a bunch of
    /// clumps with sky between them, and where the map is low only the noise's biggest bumps stay --
    /// small lone puffs round and between the big clouds, many more small ones than big.</item>
    /// <item>A cloud's height follows c (EVE's cg = c + curve - 1): a small one is a low puff a little
    /// over the base, a big one reaches the tallest. Its widest level is near the bottom
    /// (<see cref="Look.Middle"/>), the underside rounding up below it.</item>
    /// <item>No surface: what the noise leaves thickens over <see cref="Look.Softness"/> (Cumulus's heaps
    /// over 0.03), so every clump is dense in its middle and see-through at its rim, and the outer
    /// clouds fainter again (<see cref="Look.EdgeFade"/>). Deep inside the biggest clouds the carving
    /// eases off (<see cref="Look.Fill"/>): their middles are solid, as real ones are.</item>
    /// <item>Pieces floating away: a finer read of the same noise (<see cref="Look.WispMultiple"/>) eats
    /// the thin parts -- strands near the bottom (it carves the puffs' middles, leaving their borders),
    /// round billows higher up (it carves between them), Horizon Zero Dawn's rule -- and leaves a faint
    /// fringe of strands just outside every puff (<see cref="Look.Fringe"/>: the cotton-candy edge).
    /// Every lookup is pushed about by the 64^3 noise (<see cref="Look.Warp"/>) so the shreds twist
    /// instead of lining up on the noise's own cells.</item>
    /// <item>THE CLASSIC LAYER from <see cref="Look.LayerFrom"/> up (<see cref="LayerCover"/>, the
    /// author's call 2026-10-17: "the classic layer fill after a threshold so the pattern doesn't appear,
    /// basically like we did with Cumulus"): the noise alone repeats every 2.3 km, and once the map is
    /// over the cut nearly everywhere that repeat was all there was to see. The puffs stop spreading at
    /// <see cref="Look.PuffCap"/> (<see cref="PuffCoverage"/>, Cumulus's HeapCap) and the layer, which
    /// follows the 13.5 km map, carries the high end; where both are, the denser wins.</item>
    /// </list>
    /// Classic's map and threshold table, the puffs cut at a higher cover (<see cref="EdgeTable"/>) up to
    /// their cap. The rain and lightning's cloud test are cut at the larger of the puffs' and the layer's
    /// cover, never more than the intensity (<see cref="DrawnCoverage"/>: the intensity itself while the
    /// Classic layer row is at 100%), so they always fall from drawn cloud (invariant 2). Nothing of this
    /// is in a save. Its light is the Cumulus style's (EVE's lobes), the puffs at their own level
    /// (<see cref="LightScaleAt"/>).
    ///
    /// The MIXED style draws the same puffs, cut as at a share of its intensity (CloudMix.PuffIntensity),
    /// beside its own layer, their cut tipped by its type pattern.
    ///
    /// Pure, and in C# 5: the offline preview (docs/previews) and tools/test-soft.ps1 compile this very
    /// file. The shader's SoftDensity (CloudCommon.cginc) is its twin: change one, change the other.
    /// CloudShaderParams.ApplySoft hands it the numbers.
    /// </summary>
    public static class CloudSoft
    {
        /// <summary>The numbers of the look, together, so the preview can try others without a build.</summary>
        public struct Look
        {
            /// <summary>Weather (as the GPU reads the map) over the cut where c reaches 1: the biggest, tallest clouds.</summary>
            public float Span;

            /// <summary>A cloud's widest level, as a share of the tallest above the base: under it the underside rounds up.</summary>
            public float Middle;

            /// <summary>The Cumulus noise's repeat, metres.</summary>
            public float NoiseTile;

            /// <summary>The Cumulus noise as read (alpha, linear) stretched round its middle: (n - middle) x contrast + 0.5.</summary>
            public float NoiseMiddle, NoiseContrast;

            /// <summary>How deep the noise carves, in units of c (EVE's erosion depth; Cumulus's heaps 0.65).</summary>
            public float Erosion;

            /// <summary>What the carving leaves, in units of c, that is full density (1 - EVE's edge hardness; Cumulus 0.03).</summary>
            public float Softness;

            /// <summary>
            /// How much of the carving is gone deep inside the biggest clouds (0..1): reached
            /// <see cref="FillSpan"/> (in units of c) past c = 1, where the map is far over the cut.
            /// </summary>
            public float Fill, FillSpan;

            /// <summary>
            /// The intensity the puffs' cut stops rising at (<see cref="PuffCoverage"/>): above it the Classic
            /// layer fills the sky in, and the noise's repeat no longer spreads over the whole map.
            /// </summary>
            public float PuffCap;

            /// <summary>
            /// Soft's Classic layer comes in between these intensities (smoothstep; <see cref="LayerCover"/>):
            /// none below the first, Classic's own cover from the second. (Mixed's layer has its own curves, CloudMix.)
            /// </summary>
            public float LayerFrom, LayerTo;

            /// <summary>How deep the fine read carves, in units of c.</summary>
            public float Wisp;

            /// <summary>The fine read's frequency, times the noise's: a whole number, so it shares its phase.</summary>
            public float WispMultiple;

            /// <summary>The share of the tallest over which the fine read turns from strands (at the base) to billows.</summary>
            public float WispHeight;

            /// <summary>The fringe's density at most (0 = none): strands of the fine read just outside every puff.</summary>
            public float Fringe;

            /// <summary>How far out the fringe reaches, in units of c past the puff's own edge.</summary>
            public float FringeReach;

            /// <summary>
            /// Metres every noise lookup is pushed by the 64^3 noise (R and G, centred on their middles
            /// <see cref="WarpMiddleR"/>, <see cref="WarpMiddleG"/>): along x and z, half as much up.
            /// </summary>
            public float Warp;

            /// <summary>The 64^3 noise's R and G as the GPU reads them (sRGB-decoded), their middles.</summary>
            public float WarpMiddleR, WarpMiddleG;

            /// <summary>
            /// How much the density is also scaled by c, 0..1: 0 = the carving alone decides; 1 = the
            /// puffs out where the map is low are fainter (shreds), the middles of big clouds full.
            /// </summary>
            public float EdgeFade;

            /// <summary>Extinction per metre at full density, at the default Density slider.</summary>
            public float Extinction;

            /// <summary>
            /// The clouds' sun and sky light, times this where the puffs carry the sky (the brightness
            /// settings' meaning kept): light reaches all through a thin cloud, so at the same Cloud
            /// brightness its sky was much brighter than Cumulus's, whose heaps shade themselves -- at the
            /// author's 185% a flat, clipped white. See <see cref="LightScaleAt"/>.
            /// </summary>
            public float LightScale;

            /// <summary>
            /// The same where the Classic layer carries the sky, and the intensity Soft's light starts to
            /// rise to it at (reaching it at 100%): <see cref="LightShare"/>.
            /// </summary>
            public float LayerLight, LightFrom;

            /// <summary>The tallest cloud, times "Layer thickness".</summary>
            public float TallestPerThickness;

            /// <summary>
            /// The cover the map is cut at, one per 5% of the intensity (0%, 5% .. 100%), linear between
            /// (null: the intensity itself, Classic's own). See <see cref="EdgeTable"/>.
            /// </summary>
            public float[] AmountTable;
        }

        // Round 3 (2026-10-17), chosen on offline renders (docs/previews, render-detail.ps1 -Set puff*) at
        // the author's settings. Rounds 1-2 (2026-10-16): a lens round the widest level carved 0.15 deep
        // by the 64^3 noise -- the weather map's own smooth outline, one blob per bump of it ("too
        // lumpy"). Erosion 0.6 at softness 0.35 first: the carving never reached past the body (blobs
        // again); 1.5 at 0.2 splits them. The light level was measured again (-Set soft-measure-puff):
        // 0.5 still gives Cumulus's brightness.
        public const float Span = 0.35f;
        public const float Middle = 0.2f;
        public const float NoiseTile = 2300f;
        public const float NoiseMiddle = 0.7f, NoiseContrast = 2f;
        public const float Erosion = 1.5f;
        public const float Softness = 0.2f;
        public const float Fill = 0.8f, FillSpan = 1.5f;
        public const float Wisp = 0.25f;
        public const float WispMultiple = 4f;
        public const float WispHeight = 0.3f;
        public const float Fringe = 0.2f, FringeReach = 0.4f;
        public const float Warp = 250f;
        public const float WarpMiddleR = 0.28f, WarpMiddleG = 0.19f;
        public const float EdgeFade = 0.5f;
        public const float Extinction = 0.008f;
        public const float LightScale = 0.5f;
        public const float TallestPerThickness = 2.0f;

        // The layer and the cap (2026-10-17 evening, the author: Soft "in high intensity the pattern is too
        // obvious"), chosen on offline renders (render-detail.ps1 -Set soft-layer*). Round 3's overcast floor
        // (every stretch inside the cut at least a big cloud from 90%, 1.4 at 100%) went with it: it is what
        // made a full sky all noise.
        public const float PuffCap = 0.8f;
        public const float LayerFrom = 0.6f, LayerTo = 0.95f;

        // The light with the layer (render-detail.ps1 -Set soft-measure-layer, -layer2: the clouds' mean
        // brightness against Cumulus's at the same Cloud brightness): x0.5 still matches up to 85% -- the
        // layer there mostly fills holes inside the puffs' outline -- x0.6 at 90%, x0.67 at 95%, x0.8 at
        // 100%. Lit at the layer's share instead (x0.9 at 85%) the sky came out 10% brighter than Cumulus's.
        public const float LayerLight = 0.8f, LightFrom = 0.85f;

        /// <summary>
        /// The cover Soft's map is cut at per 5% of the intensity, solved offline (render-detail.ps1
        /// -Set soft-calib, bisected and averaged over three cities' seeds: 12345/67890, 23456/78901,
        /// 34567/89012) so the share of the sky that is cloud from below (optical depth over 0.5) is the
        /// Cumulus style's at its default height ("about as much as Cumulus"). The noise carves most of
        /// the map's outer reaches away, so Soft needs far more of it for that: at 47% it is cut where
        /// Classic is at ~87%. Never under the intensity (EdgeCoverage). Past <see cref="PuffCap"/> the
        /// cut stays (<see cref="PuffCoverage"/>): the tail repeats the cap's value. With the Classic layer
        /// (2026-10-17 evening) the entries up to the cap are round 3's: below the cap the layer only fills
        /// holes inside the puffs' outline (+0.1-0.4% of the sky at 75-80% on the test maps). Above it the
        /// layer brings the amount to Cumulus's (95%: 44% of the sky on the first map, Cumulus 46%; 100%:
        /// 90%, Cumulus 90%).
        /// </summary>
        public static readonly float[] EdgeTable =
        {
            0.0000f, 0.3738f, 0.5668f, 0.6611f, 0.7155f, 0.7563f, 0.7891f, 0.8187f, 0.8432f, 0.8663f,
            0.8855f, 0.9027f, 0.9186f, 0.9323f, 0.9432f, 0.9533f, 0.9634f, 0.9634f, 0.9634f, 0.9634f,
            0.9634f,
        };

        /// <summary>The look as built.</summary>
        public static Look Default
        {
            get
            {
                Look look;
                look.Span = Span;
                look.Middle = Middle;
                look.NoiseTile = NoiseTile;
                look.NoiseMiddle = NoiseMiddle;
                look.NoiseContrast = NoiseContrast;
                look.Erosion = Erosion;
                look.Softness = Softness;
                look.Fill = Fill;
                look.FillSpan = FillSpan;
                look.PuffCap = PuffCap;
                look.LayerFrom = LayerFrom;
                look.LayerTo = LayerTo;
                look.Wisp = Wisp;
                look.WispMultiple = WispMultiple;
                look.WispHeight = WispHeight;
                look.Fringe = Fringe;
                look.FringeReach = FringeReach;
                look.Warp = Warp;
                look.WarpMiddleR = WarpMiddleR;
                look.WarpMiddleG = WarpMiddleG;
                look.EdgeFade = EdgeFade;
                look.Extinction = Extinction;
                look.LightScale = LightScale;
                look.LayerLight = LayerLight;
                look.LightFrom = LightFrom;
                look.TallestPerThickness = TallestPerThickness;
                look.AmountTable = EdgeTable;
                return look;
            }
        }

        /// <summary>
        /// The amount table at an intensity, never less than the intensity (null table: the intensity
        /// itself, Classic's own). <see cref="PuffCoverage"/> reads it up to the cap.
        /// </summary>
        public static float EdgeCoverage(Look look, float coverage)
        {
            float c = Clamp01(coverage);
            float[] table = look.AmountTable;
            if (table == null || table.Length < 2)
                return c;

            float x = c * (table.Length - 1);
            int i = Math.Min((int)x, table.Length - 2);
            float f = x - i;
            return Math.Max(c, Clamp01(table[i] + (table[i + 1] - table[i]) * f));
        }

        /// <summary>
        /// The cover the weather map is cut at for the puffs (CloudDensityField.GetThreshold, Classic's
        /// table) at an intensity: the amount table, rising to <see cref="Look.PuffCap"/> and staying there
        /// above it. Never less than the intensity up to the cap, so there Classic's clouds lie inside the
        /// puffs' envelope. 0 at 0.
        /// </summary>
        public static float PuffCoverage(Look look, float coverage)
        {
            return EdgeCoverage(look, Math.Min(Clamp01(coverage), look.PuffCap));
        }

        /// <summary>
        /// How much of Classic's own cover the Classic layer is cut at under Soft, 0..1 at an intensity,
        /// before the "Classic layer" row: 0 up to <see cref="Look.LayerFrom"/>, 1 from <see cref="Look.LayerTo"/>.
        /// </summary>
        public static float LayerShare(Look look, float coverage)
        {
            float t = Clamp01((coverage - look.LayerFrom) / Math.Max(look.LayerTo - look.LayerFrom, 1e-3f));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// The cover Soft's Classic layer is cut at: the intensity times <see cref="LayerShare"/> times the
        /// "Classic layer" row (<paramref name="amount"/>, 0..1; 100% = Classic's own cover from LayerTo up,
        /// its gaps kept, as under Cumulus). Never more than the intensity.
        /// </summary>
        public static float LayerCover(Look look, float coverage, float amount)
        {
            float c = Clamp01(coverage);
            return c * LayerShare(look, c) * Clamp01(amount);
        }

        /// <summary>
        /// The cover the rain, lightning's cloud test and the rainbow's are cut at under Soft: the larger of
        /// the puffs' and the layer's (where the map passes either, cloud is drawn), never more than the
        /// intensity. The intensity itself while the "Classic layer" row is at 100%: up to the cap's table
        /// value the puffs' cover is past it, and from LayerTo up the layer's is the intensity.
        /// </summary>
        public static float DrawnCoverage(Look look, float coverage, float amount)
        {
            float c = Clamp01(coverage);
            return Math.Min(c, Math.Max(PuffCoverage(look, c), LayerCover(look, c, amount)));
        }

        /// <summary>
        /// How much the layer carries Soft's sky for its light, 0..1: 0 up to <see cref="Look.LightFrom"/>,
        /// 1 at 100% (smoothstep), times the "Classic layer" row (<paramref name="amount"/>).
        /// </summary>
        public static float LightShare(Look look, float coverage, float amount)
        {
            float t = Clamp01((coverage - look.LightFrom) / Math.Max(1f - look.LightFrom, 1e-3f));
            return t * t * (3f - 2f * t) * Clamp01(amount);
        }

        /// <summary>
        /// The clouds' light level for a share of the sky the layer carries (0..1, <see cref="LightShare"/>):
        /// the puffs' <see cref="Look.LightScale"/> where they carry it, <see cref="Look.LayerLight"/> where
        /// the layer does -- lit at the puffs' level, the layer was darker than under Cumulus.
        /// </summary>
        public static float LightScaleAt(Look look, float layerShare)
        {
            return look.LightScale + (look.LayerLight - look.LightScale) * Clamp01(layerShare);
        }

        /// <summary>The tallest cloud, metres above the base, for a "Layer thickness" in metres.</summary>
        public static float Tallest(Look look, float thickness)
        {
            return Math.Max(50f, thickness) * look.TallestPerThickness;
        }

        /// <summary>
        /// How big the cloud is here, in units of c (0 at the cut, 1 <see cref="Look.Span"/> in, more
        /// deep inside the biggest): the weather over the cut. 0 wherever the map is at or under the cut
        /// (the empty-sky skip's promise).
        /// </summary>
        public static float Size(Look look, float excess)
        {
            return excess <= 0f ? 0f : excess / look.Span;
        }

        /// <summary>
        /// How much of a cloud there is at one height, before the noise: c (<see cref="Size"/>, at most 1)
        /// less how far the height is from the widest level (0 at the base and at the tallest, EVE's
        /// coverage curve), so a cloud only reaches as high and as low as its c lets it. 0..1; 0 outside
        /// the slab.
        /// </summary>
        public static float Body(Look look, float excess, float y, float tallest)
        {
            float c = Clamp01(Size(look, excess));
            if (c <= 0f || y <= 0f || y >= tallest)
                return 0f;

            float h = y / tallest;
            float mid = look.Middle;
            float off = h < mid ? (mid - h) / mid : (h - mid) / (1f - mid);
            // The underside rounds up quickly (squared), the top falls off straight: a flatter base.
            return Clamp01(c - (h < mid ? off * off : off));
        }

        /// <summary>
        /// The density at one point, 0..1 (times the Density slider and <see cref="Look.Extinction"/> for
        /// the march): <paramref name="excess"/> the weather there minus the puffs' threshold,
        /// <paramref name="y"/> metres above the base, <paramref name="tallest"/> as <see cref="Tallest"/>,
        /// <paramref name="noise"/> the Cumulus noise as read (at the warped point), <paramref name="fine"/>
        /// its fine read, <paramref name="wisps"/> how much of that (0 for the light's looks and far away,
        /// where it would only shimmer).
        /// </summary>
        public static float Density(Look look, float excess, float y, float tallest, float noise, float fine, float wisps)
        {
            float body = Body(look, excess, y, tallest);
            if (body <= 0f)
                return 0f;

            // Deep inside the biggest clouds (c past 1) the carving eases off: their middles solid.
            float size = Size(look, excess);
            float fill = look.Fill * Clamp01((size - 1f) / look.FillSpan);
            float n = Clamp01((noise - look.NoiseMiddle) * look.NoiseContrast + 0.5f);
            float v = body - look.Erosion * (1f - fill) * (1f - n);

            if (wisps > 0f)
            {
                // Strands at the base (the fine puffs' middles carved, their borders left), billows higher.
                float f = Clamp01((fine - look.NoiseMiddle) * look.NoiseContrast + 0.5f);
                float billow = Clamp01(y / (tallest * Math.Max(look.WispHeight, 1e-3f)));
                v -= look.Wisp * wisps * (f + (1f - 2f * f) * billow);
            }

            float d = Clamp01(v / look.Softness);
            if (wisps > 0f && look.Fringe > 0f)
            {
                // The fringe: a faint, see-through edge of strands round every puff, out to FringeReach.
                float f = Clamp01((fine - look.NoiseMiddle) * look.NoiseContrast + 0.5f);
                d = Math.Max(d, look.Fringe * wisps * f * f * Clamp01(1f + v / look.FringeReach));
            }

            return d * (1f - look.EdgeFade * (1f - Clamp01(size)));
        }

        /// <summary>The look for the log.</summary>
        public static string Describe(Look look, float thickness)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return "puffs carved from the Cumulus noise (repeating every " + look.NoiseTile.ToString("F0", c) + " m, " +
                   look.Erosion.ToString("F2", c) + " deep, soft over " + look.Softness.ToString("F2", c) + "), shreds from its x" +
                   look.WispMultiple.ToString("F0", c) + " read " + look.Wisp.ToString("F2", c) + " deep, pushed about " +
                   look.Warp.ToString("F0", c) + " m; clouds as tall as the weather is high, up to " + Tallest(look, thickness).ToString("F0", c) +
                   " m, widest " + (look.Middle * 100f).ToString("F0", c) + "% up, " + (look.Extinction * 1000f).ToString("F1", c) +
                   "/km at full (at the default density), light x" + look.LightScale.ToString("F2", c) +
                   "; cut at Classic's map at the cover Cumulus's sky needs (47% -> " + (EdgeCoverage(look, 0.47f) * 100f).ToString("F0", c) +
                   "%), no further than at " + (look.PuffCap * 100f).ToString("F0", c) + "% (" + (PuffCoverage(look, 1f) * 100f).ToString("F0", c) +
                   "%); the Classic layer from " + (look.LayerFrom * 100f).ToString("F0", c) + "%, Classic's own cover from " +
                   (look.LayerTo * 100f).ToString("F0", c) + "% (times the Classic layer row); light rising to x" +
                   look.LayerLight.ToString("F2", c) + " from " + (look.LightFrom * 100f).ToString("F0", c) + "% as the layer carries the sky";
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
