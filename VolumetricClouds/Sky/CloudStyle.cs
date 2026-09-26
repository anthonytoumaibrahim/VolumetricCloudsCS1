using System;
using System.Globalization;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The two cloud styles (1.2). CLASSIC is the mod's cloud layer as it has always been: the
    /// weather map thresholded into clouds, one thickness for all of them. CUMULUS adds HEAPS to that
    /// layer: EVE-Redux V5's cloud model (blackrack's KSP clouds, which the author asked to match "as
    /// close as" possible), every heap carved out of one big noise (<see cref="CumulusNoise3D"/>),
    /// each with its own height, a flat base and a billowing top -- and the Classic layer among and
    /// under them, its thickness following the map (the author's own idea, after his first look).
    /// Where both are, the denser wins. Classic's settings all shape the layer part.
    /// </summary>
    /// <remarks>
    /// Chosen on offline renders of this very formula (docs, "The EVE look" and after it; sheets
    /// eve-1-kerbal-look, eve-4-combined): the heaps alone have Classic's amount of cloud at every
    /// slider position (<see cref="EdgeTable"/>), the layer comes on top. What the heaps take from
    /// EVE V5, with EVE's numbers (NoiseWrapper.cs defaults and an Earth-like V5 config,
    /// PromisedWorlds' Gurdamma):
    /// <list type="bullet">
    /// <item>the noise: 8 octaves of spherical Worley, persistence 0.57, repeating every 2300 m;</item>
    /// <item>the shape: cg = saturate(c + coverageCurve(h) - 1), their cumulus coverage curve over
    /// <see cref="Span"/>; density = saturate((cg - (1 - noise) x 0.65) / (1 - 0.97));</item>
    /// <item>the light: single scattering 0.1 HG(0.95) + 0.2 HG(0.8), multiple scattering
    /// 3.0 HG(0.2) + 0.3 HG(-0.4) through a reduced extinction.</item>
    /// </list>
    /// Where it differs, from the renders: EVE paints its coverage (in-between values over tens of
    /// km); here it is our weather map, gently -- <see cref="EdgeCoverage"/> at the map's median,
    /// <see cref="CoverageSlope"/> more at its top tenth, a straight line in the map's normal
    /// scores, capped at <see cref="CoverageMax"/> -- so the noise makes every cloud and the map
    /// says where they gather (the map as a 0..1 coverage made cones: its Worley peaks are conical). Density 0.03 /m, 2.5 x EVE's, for crisp
    /// edges at a city camera's distances; EVE's density curve (thin bases) washed everything out
    /// and is left out; the multiple-scattering extinction is not public (0.5, by eye).
    ///
    /// Pure: no engine calls, so tools/test-cumulus.ps1 checks it offline, and the offline preview
    /// uses these numbers. The shader gets them from CloudShaderParams.ApplyStyle (the shape) and
    /// CloudVolume.ApplyDetailLight (the light).
    /// </remarks>
    public static class CloudStyle
    {
        // ---- the styles, stored by VALUE in VolumetricClouds.xml (append only) ----------------

        public const int Classic = 0;
        public const int Cumulus = 1;

        /// <summary>The setting's value (the default without one).</summary>
        public static int Current
        {
            get { return Settings.CloudStyle != null ? Settings.CloudStyle.value : Settings.Defaults.CloudStyle; }
        }

        public static bool IsCumulus
        {
            get { return Current == Cumulus; }
        }

        /// <summary>
        /// The Cumulus noise, while a city is loaded (CloudVolume makes and uploads it the first
        /// time the style is wanted; null until then).
        /// </summary>
        public static UnityEngine.Texture3D Texture { get; set; }

        /// <summary>This city's Cumulus noise could not be made: the clouds are drawn Classic, and the log says why.</summary>
        public static bool Failed { get; set; }

        /// <summary>What is actually drawn: Cumulus when it is chosen AND its noise is there.</summary>
        public static bool Drawn
        {
            get { return IsCumulus && Texture != null; }
        }

        /// <summary>
        /// The noise's mip chain as Color32 (the value in every channel; the shader reads alpha),
        /// for the city's noise seed. Worker thread: Color32 is a plain struct. Null on failure,
        /// with the reason in <paramref name="error"/>.
        /// </summary>
        public static UnityEngine.Color32[][] BuildLevels(int noiseSeed, out string error, out int milliseconds)
        {
            error = null;
            milliseconds = 0;
            try
            {
                DateTime start = DateTime.UtcNow;
                byte[][] levels = CumulusNoise3D.Generate(CumulusNoise3D.Size, CumulusNoise3D.SeedFor(noiseSeed), 0);
                var colours = new UnityEngine.Color32[levels.Length][];
                for (int l = 0; l < levels.Length; l++)
                {
                    byte[] src = levels[l];
                    var dst = new UnityEngine.Color32[src.Length];
                    for (int i = 0; i < src.Length; i++)
                    {
                        byte v = src[i];
                        dst[i] = new UnityEngine.Color32(v, v, v, v);
                    }
                    colours[l] = dst;
                }

                milliseconds = (int)(DateTime.UtcNow - start).TotalMilliseconds;
                return colours;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        // ---- the shape ------------------------------------------------------------------------

        /// <summary>Metres per repeat of the noise: EVE's baseNoiseTiling in their Earth-like config.</summary>
        public const float NoiseTile = 2300f;

        /// <summary>How deep the noise carves into the cloud's body (EVE's erosionDepth).</summary>
        public const float ErosionDepth = 0.65f;

        /// <summary>EVE's noiseEdgeHardness: the density is full 3% of the way in.</summary>
        public const float Hardness = 0.97f;

        /// <summary>Metres over which the coverage curve runs, base to top.</summary>
        public const float Span = 2000f;

        /// <summary>
        /// Coverage gained from the map's median to its top tenth, measured on the map's NORMAL
        /// SCORES (CloudDensityField.ScoreTexture: each texel's rank as a standard normal z), so
        /// the same share of every city's map gets each coverage. Measured from the raw values
        /// (the first build), one city's map sat lower than another's and its sky was twice as
        /// cloudy at the same slider.
        /// </summary>
        public const float CoverageSlope = 0.155f;

        /// <summary>The normal score a tenth of a map lies above.</summary>
        public const float TopTenthScore = 1.2815516f;

        /// <summary>
        /// The most coverage anywhere: EVE's "in-between" regime, where the noise shapes the cloud.
        /// It also sets the tallest cloud (<see cref="Tallest"/>).
        /// </summary>
        public const float CoverageMax = 0.45f;

        /// <summary>Extinction per metre at full density, at the default Density.</summary>
        public const float BaseExtinction = 0.03f;

        /// <summary>
        /// The raymarch takes this many times the Quality setting's steps under Cumulus (the shader's
        /// loop allows 192 = 96 x 2). Its slab is ~2.8x Classic's height (the tallest heap, 1.1 km),
        /// so with the same count every step skipped over the heaps' hard edges and the jitter showed
        /// as grain (the author, 2026-09-26: "small dots everywhere"). On the offline preview twice
        /// the steps with blue-noise jitter (<see cref="BlueNoise"/>) came close to four times.
        /// </summary>
        public const float StepFactor = 2f;

        // EVE's cumulus coverage curve (Gurdamma, type Cumulus): Unity float-curve keys, time =
        // height over Span, value = coverage, in/out tangents per unit of time.
        public static readonly float[] CurveTime = { 0.01728953f, 0.08695792f, 0.9732781f };
        public static readonly float[] CurveValue = { -0.004032524f, 0.976166f, -0.006797761f };
        public static readonly float[] CurveIn = { 1.633945f, 0.06669102f, -0.7966601f };
        public static readonly float[] CurveOut = { 1.633945f, 0.04846065f, -0.5550035f };

        /// <summary>
        /// The curve at height h (0 = base, 1 = Span up), as Unity evaluates a float curve:
        /// Hermite between keys, the end values outside them.
        /// </summary>
        public static float Curve(float h)
        {
            int n = CurveTime.Length;
            if (h <= CurveTime[0])
                return CurveValue[0];
            if (h >= CurveTime[n - 1])
                return CurveValue[n - 1];

            int i = 1;
            while (i < n - 1 && h > CurveTime[i])
                i++;

            float[] c = SegmentCubic(i - 1);
            float s = (h - CurveTime[i - 1]) / (CurveTime[i] - CurveTime[i - 1]);
            return ((c[0] * s + c[1]) * s + c[2]) * s + c[3];
        }

        /// <summary>
        /// Segment k (key k to key k + 1) as a cubic in s = 0..1 across it: a s^3 + b s^2 + c s + d.
        /// What the shader evaluates, so the two cannot disagree.
        /// </summary>
        public static float[] SegmentCubic(int k)
        {
            float dt = CurveTime[k + 1] - CurveTime[k];
            float v0 = CurveValue[k], v1 = CurveValue[k + 1];
            float m0 = CurveOut[k] * dt, m1 = CurveIn[k + 1] * dt;
            return new[]
            {
                2f * v0 + m0 - 2f * v1 + m1,
                -3f * v0 - 2f * m0 + 3f * v1 - m1,
                m0,
                v0,
            };
        }

        /// <summary>
        /// Metres above the base where the tallest cloud ends: above it c + curve - 1 is below 0
        /// even at <see cref="CoverageMax"/>, so the march can stop there.
        /// </summary>
        public static readonly float Tallest = ComputeTallest();

        private static float ComputeTallest()
        {
            // The curve falls from its peak key to 0 at the last one; bisect where it crosses
            // 1 - CoverageMax on the way down.
            float target = 1f - CoverageMax;
            float lo = CurveTime[1], hi = CurveTime[CurveTime.Length - 1];
            for (int k = 0; k < 40; k++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Curve(mid) > target)
                    lo = mid;
                else
                    hi = mid;
            }

            return hi * Span;
        }

        // ---- how much cloud -------------------------------------------------------------------

        /// <summary>
        /// The heaps' coverage at the weather map's median, one per 5% of the Intensity slider
        /// (0%, 5% .. 100%): solved offline (docs/previews/render-detail.ps1 -Calibrate, several
        /// cities' seeds averaged) so that the heaps ALONE cover the share of the sky Classic does at
        /// Classic's default settings (the author's first pick, "so people don't get surprised").
        /// With the layer beside them the sky has somewhat more (his later pick: "maybe 5", the
        /// layer at the full slider): at 83%, 25% of the sky where Classic has 17%.
        /// </summary>
        public static readonly float[] EdgeTable =
        {
            // Solved 2026-09-26 on the normal scores, over 2 x 2 repeats of each map, for six
            // cities' seeds (field/noise 12345/67890, 23456/78901, 34567/89012, 45678/90123,
            // 56789/11223, 67891/22334) and averaged. Classic's own amount differs from city to
            // city (at 95%: 25% to 49% of the sky), so a single city's Cumulus sky is "about"
            // Classic's, not exactly.
            -0.3000f, -0.1274f, -0.0789f, -0.0532f, -0.0366f, -0.0244f, -0.0136f, -0.0030f, 0.0081f, 0.0191f,
            0.0301f, 0.0416f, 0.0542f, 0.0682f, 0.0820f, 0.0969f, 0.1139f, 0.1325f, 0.1555f, 0.1942f,
            0.3648f,
        };

        /// <summary>
        /// Above this Intensity the heaps stop multiplying and the Classic layer alone fills the sky
        /// in. At 100% the heaps alone covered the whole map, and their noise's 2.3 km repeat was
        /// all there was to see (the author: "the pattern starts to show"); a few heaps over the
        /// layer have no repeat to see.
        /// </summary>
        public const float HeapCap = 0.85f;

        /// <summary>The heaps' coverage at the map's median at this Intensity (capped at <see cref="HeapCap"/>).</summary>
        public static float HeapEdge(float coverage)
        {
            return EdgeCoverage(Math.Min(coverage, HeapCap));
        }

        /// <summary>
        /// THE LAYER under the heaps (the author: "Maybe we can combine the classic and cumulus
        /// clouds for a more natural result?"): Classic's own layer at the slider's Intensity, its
        /// thickness x (1 + this x the map's normal score), within <see cref="LayerThinnest"/> ..
        /// <see cref="LayerThickest"/>: thick where the map is high. A layer filled to a closed lid
        /// (tried: "the pattern repetition is super obvious") is one even carpet of the same puffs;
        /// its own gaps and this variety over kilometres are what keep 100% natural.
        /// </summary>
        public const float LayerThicknessVariety = 0.35f;
        public const float LayerThinnest = 0.25f, LayerThickest = 1.75f;

        /// <summary>
        /// The layer's thickness factor as a line in the score texture's value t, a + b x t (the
        /// shader then keeps it within LayerThinnest .. LayerThickest). Returns { a, b }.
        /// </summary>
        public static float[] LayerLine()
        {
            float range = CloudDensityField.ScoreRange;
            return new[] { 1f - LayerThicknessVariety * range, 2f * LayerThicknessVariety * range };
        }

        /// <summary>The layer's thickness factor at a score texel value (see <see cref="LayerLine"/>).</summary>
        public static float LayerFactor(float scoreTexel)
        {
            float[] line = LayerLine();
            return Math.Max(LayerThinnest, Math.Min(LayerThickest, line[0] + line[1] * scoreTexel));
        }

        /// <summary>The layer's thickness factor as a line in the score texture's value, cached: set every frame.</summary>
        public static readonly float[] LayerLineCached = LayerLine();

        /// <summary>The coverage at the map's median at this Intensity (0..1).</summary>
        public static float EdgeCoverage(float coverage)
        {
            float x = Math.Max(0f, Math.Min(1f, coverage)) * (EdgeTable.Length - 1);
            int i = Math.Min((int)x, EdgeTable.Length - 2);
            float f = x - i;
            return EdgeTable[i] + (EdgeTable[i + 1] - EdgeTable[i]) * f;
        }

        /// <summary>
        /// The shader's coverage as a straight line in the score texture's value t (0..1, the
        /// normal score z = (2t - 1) x ScoreRange): <paramref name="edge"/> at the map's median
        /// (z = 0), <see cref="CoverageSlope"/> more at its top tenth (z = 1.28), within
        /// 0..<see cref="CoverageMax"/> in the shader. Returns { a, b } for a + b x t.
        /// </summary>
        public static float[] Line(float edge)
        {
            float a, b;
            Line(edge, out a, out b);
            return new[] { a, b };
        }

        /// <summary>The same, without an allocation: the shader parameters ask for it every frame.</summary>
        public static void Line(float edge, out float a, out float b)
        {
            float range = CloudDensityField.ScoreRange;
            float perScore = CoverageSlope / TopTenthScore;
            a = edge - perScore * range;
            b = perScore * 2f * range;
        }

        /// <summary>Both segments' cubics, worked out once (the shader parameters are set every frame).</summary>
        public static readonly float[][] Cubics = { SegmentCubic(0), SegmentCubic(1) };

        /// <summary>The shader's coverage at a score texel value (see <see cref="Line"/>).</summary>
        public static float CoverageAt(float scoreTexel, float edge)
        {
            float[] line = Line(edge);
            return Math.Max(0f, Math.Min(CoverageMax, line[0] + line[1] * scoreTexel));
        }

        /// <summary>
        /// The density at one point, 0..1, from the coverage, the height over <see cref="Span"/> and
        /// the noise there: the shader's formula, for tests and the preview.
        /// </summary>
        public static float Density(float coverage, float h, float noise)
        {
            float cg = Math.Max(0f, Math.Min(1f, coverage + Curve(h) - 1f));
            if (cg <= 0f)
                return 0f;

            float d = (cg - (1f - noise) * ErosionDepth) / (1f - Hardness);
            return Math.Max(0f, Math.Min(1f, d));
        }

        // ---- the light ------------------------------------------------------------------------

        /// <summary>
        /// EVE's four phase lobes: eccentricity and strength. Two for single scattering (the
        /// silver lining), two for multiple scattering (the soft glow deep inside).
        /// </summary>
        public const float SingleG1 = 0.95f, SingleW1 = 0.1f, SingleG2 = 0.8f, SingleW2 = 0.2f;
        public const float MultipleG1 = 0.2f, MultipleW1 = 3.0f, MultipleG2 = -0.4f, MultipleW2 = 0.3f;

        /// <summary>The multiple-scattering light falls through this share of the extinction.</summary>
        public const float MultipleExtinction = 0.5f;

        /// <summary>The single-scattering lobes' cap: straight into the sun the rim cannot blow out.</summary>
        public const float SingleCap = 5f;

        /// <summary>How strongly the cloud above takes the sky light away (as cloud detail's).</summary>
        public const float AmbientOcclusion = 0.15f;

        public static float Single(float cosTheta)
        {
            return Math.Min(SingleCap, SingleW1 * HenyeyGreenstein(cosTheta, SingleG1) + SingleW2 * HenyeyGreenstein(cosTheta, SingleG2));
        }

        public static float Multiple(float cosTheta)
        {
            return MultipleW1 * HenyeyGreenstein(cosTheta, MultipleG1) + MultipleW2 * HenyeyGreenstein(cosTheta, MultipleG2);
        }

        /// <summary>
        /// The lobes' gain, so that a sunlit face seen side-on is as bright as Classic's old light
        /// makes it: the brightness settings keep their meaning in both styles.
        /// </summary>
        public static readonly float LightGain = ComputeLightGain();

        private static float ComputeLightGain()
        {
            float old = 0.65f + 0.35f * Math.Min(HenyeyGreenstein(0f, 0.55f), 5f);
            return old / (Single(0f) + Multiple(0f));
        }

        /// <summary>
        /// The sun's light at a point, as the shader works it out, from the optical depth towards
        /// the sun: both kinds of scattering, through the gain.
        /// </summary>
        public static float SunLight(float cosTheta, float tau)
        {
            return (Single(cosTheta) * (float)Math.Exp(-tau) + Multiple(cosTheta) * (float)Math.Exp(-tau * MultipleExtinction)) * LightGain;
        }

        public static float HenyeyGreenstein(float cosTheta, float g)
        {
            float g2 = g * g;
            return (1f - g2) / (float)Math.Pow(1f + g2 - 2f * g * cosTheta, 1.5);
        }

        // ---- for the log ----------------------------------------------------------------------

        public static string Name(int style)
        {
            return style == Cumulus ? "Cumulus" : style == Classic ? "Classic" : "unknown (" + style + ")";
        }

        public static string Describe()
        {
            return Name(Current);
        }

        /// <summary>The Cumulus style's shape, once, for the log.</summary>
        public static string DescribeShape()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return "heaps carved from one " + CumulusNoise3D.Size + "^3 noise repeating every " + NoiseTile.ToString("F0", c) +
                   " m, up to " + Tallest.ToString("F0", c) + " m tall, edges " + (Hardness * 100f).ToString("F0", c) +
                   "% hard, erosion " + ErosionDepth.ToString("F2", c) + ", no more of them above " +
                   (HeapCap * 100f).ToString("F0", c) + "%; with the Classic layer, its thickness x " +
                   LayerThinnest.ToString("F2", c) + ".." + LayerThickest.ToString("F2", c) + " following the map";
        }
    }
}
