using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// A tiling cloud density field, shared by the shadow cookie and the visible clouds so
    /// the two describe the same weather. Generated once; changing coverage only
    /// re-thresholds the cached field, which is cheap enough to do on a slider drag.
    /// </summary>
    public class CloudDensityField
    {
        public const int Resolution = 384;

        private const int ValueOctaves = 5;
        private const int WorleyOctaves = 3;
        private const int BasePeriod = 4;
        private const int HistogramBins = 256;
        private const int ThresholdSteps = 32;

        private readonly float[] _density = new float[Resolution * Resolution];
        private readonly Color32[] _pixels = new Color32[Resolution * Resolution];
        private readonly int[] _histogram = new int[HistogramBins];
        private readonly float[] _thresholdTable = new float[ThresholdSteps + 1];

        private Texture2D _texture;
        private Texture2D _densityTexture;

        public CloudDensityField(int seed)
        {
            Generate(seed);
        }

        /// <summary>The cookie texture. Alpha carries the value a directional light samples.</summary>
        public Texture2D Texture
        {
            get
            {
                if (_texture == null)
                {
                    _texture = new Texture2D(Resolution, Resolution, TextureFormat.ARGB32, false)
                    {
                        name = "VolumetricCloudsCookie",
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = FilterMode.Bilinear,
                        anisoLevel = 0,
                    };
                }

                return _texture;
            }
        }

        /// <summary>Width of the soft edge band, in density units. The shader needs the same value.</summary>
        public const float EdgeSoftness = Softness;

        /// <summary>
        /// The raw, unthresholded field as a texture. The raymarch shader thresholds it
        /// itself from <see cref="GetThreshold"/>, so a coverage change is one uniform
        /// rather than a texture rewrite -- and it is the same data the cookie is cut from,
        /// so the clouds and their shadows share one weather pattern.
        /// </summary>
        public Texture2D DensityTexture
        {
            get
            {
                if (_densityTexture == null)
                {
                    _densityTexture = new Texture2D(Resolution, Resolution, TextureFormat.ARGB32, false)
                    {
                        name = "VolumetricCloudsWeather",
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = FilterMode.Bilinear,
                        anisoLevel = 0,
                    };

                    Color32[] pixels = new Color32[_density.Length];
                    for (int i = 0; i < _density.Length; i++)
                    {
                        byte v = (byte)(Mathf.Clamp01(_density[i]) * 255f);
                        pixels[i] = new Color32(v, v, v, v);
                    }

                    _densityTexture.SetPixels32(pixels);
                    _densityTexture.Apply(false);
                }

                return _densityTexture;
            }
        }

        /// <summary>The density threshold that yields the requested sky coverage.</summary>
        public float GetThreshold(float coverage)
        {
            return Threshold(coverage);
        }

        /// <summary>Cloud amount at a point in field space, 0..1, for a given coverage.</summary>
        public float SampleCloud(float u, float v, float coverage)
        {
            int x = Wrap(Mathf.FloorToInt(u * Resolution), Resolution);
            int y = Wrap(Mathf.FloorToInt(v * Resolution), Resolution);
            return Mathf.Clamp01((_density[y * Resolution + x] - Threshold(coverage)) / Softness);
        }

        private const float Softness = 0.18f;

        /// <summary>
        /// Threshold for a given coverage, looked up from a table solved at generation so
        /// that "40%" really means 40% of the sky. A linear sweep leaves the bottom half of
        /// the slider doing nothing, and a plain percentile still undershoots because the
        /// soft edge band loses more than it gains on a skewed distribution.
        /// </summary>
        private float Threshold(float coverage)
        {
            coverage = Mathf.Clamp01(coverage);

            float t = coverage * ThresholdSteps;
            int index = Mathf.Clamp((int)t, 0, ThresholdSteps - 1);
            return Mathf.Lerp(_thresholdTable[index], _thresholdTable[index + 1], t - index);
        }

        /// <summary>
        /// Solves, for each coverage step, the threshold whose resulting mean cloud amount
        /// equals it. Evaluated against the histogram rather than the full field, which
        /// makes each probe 256 operations instead of 147k.
        /// </summary>
        private void BuildThresholdTable()
        {
            for (int i = 0; i < _histogram.Length; i++)
                _histogram[i] = 0;

            for (int i = 0; i < _density.Length; i++)
            {
                int bin = Mathf.Clamp((int)(_density[i] * HistogramBins), 0, HistogramBins - 1);
                _histogram[bin]++;
            }

            for (int step = 0; step <= ThresholdSteps; step++)
                _thresholdTable[step] = SolveThreshold(step / (float)ThresholdSteps);
        }

        private float SolveThreshold(float targetMean)
        {
            if (targetMean <= 0f)
                return 1f + Softness;
            if (targetMean >= 1f)
                return -Softness;

            // Mean falls as the threshold rises, so bracket and bisect.
            float low = -Softness;
            float high = 1f + Softness;

            for (int i = 0; i < 24; i++)
            {
                float mid = (low + high) * 0.5f;
                if (SoftMean(mid) > targetMean)
                    low = mid;
                else
                    high = mid;
            }

            return (low + high) * 0.5f;
        }

        private float SoftMean(float threshold)
        {
            float sum = 0f;

            for (int bin = 0; bin < HistogramBins; bin++)
            {
                if (_histogram[bin] == 0)
                    continue;

                float centre = (bin + 0.5f) / HistogramBins;
                sum += Mathf.Clamp01((centre - threshold) / Softness) * _histogram[bin];
            }

            return sum / _density.Length;
        }

        /// <summary>
        /// Rewrites the fallback cookie for a given coverage: full light in the gaps, and
        /// (1 - depth) under cloud. Same meaning as the marched shadow map, just flat and
        /// unaligned -- it is only used when the shader bundle is unavailable.
        /// </summary>
        public void Apply(float coverage, float depth)
        {
            float threshold = Threshold(coverage);
            depth = Mathf.Clamp01(depth);

            for (int i = 0; i < _density.Length; i++)
            {
                float cloud = Mathf.Clamp01((_density[i] - threshold) / Softness);
                byte v = (byte)((1f - cloud * depth) * 255f);
                _pixels[i] = new Color32(v, v, v, v);
            }

            Texture.SetPixels32(_pixels);
            Texture.Apply(false);
        }

        /// <summary>Writes a checkerboard instead of cloud noise, for verifying the cookie lands at all.</summary>
        public void ApplyChecker()
        {
            const int Cells = 4;
            int cellSize = Resolution / Cells;

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    bool dark = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                    byte v = dark ? (byte)40 : (byte)255;
                    _pixels[y * Resolution + x] = new Color32(v, v, v, v);
                }
            }

            Texture.SetPixels32(_pixels);
            Texture.Apply(false);
        }

        /// <summary>
        /// Perlin-Worley: inverted cellular noise gives the rounded, billowy lobes that read
        /// as cumulus, and value-noise fBm breaks up the edges so they aren't obviously
        /// cellular. Plain fBm alone produces soft grey clumps that don't read as cloud.
        /// </summary>
        private void Generate(int seed)
        {
            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    float value = ValueFbm(x, y, seed);
                    float billow = WorleyFbm(x, y, seed);

                    // Remap the value noise by the billow floor: where billow is high the
                    // cloud is solid, where it is low the value noise carves it away.
                    // The lower bound is billow-1 rather than 1-billow so the divisor is
                    // 2-billow, bounded to [1,2]. Dividing by billow itself lets the result
                    // explode wherever billow approaches zero, and those outliers squash
                    // the whole field once it is normalised.
                    _density[y * Resolution + x] = Remap(value, billow - 1f, 1f, 0f, 1f);
                }
            }

            Normalise();
            BuildThresholdTable();
        }

        private static float ValueFbm(int x, int y, int seed)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;
            int period = BasePeriod;

            for (int octave = 0; octave < ValueOctaves; octave++)
            {
                sum += ValueNoise(x * period / (float)Resolution, y * period / (float)Resolution,
                                  period, seed + octave) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                period *= 2;
            }

            return sum / total;
        }

        private static float WorleyFbm(int x, int y, int seed)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;
            int period = BasePeriod;

            for (int octave = 0; octave < WorleyOctaves; octave++)
            {
                // Inverted: near a feature point means dense, not sparse.
                float worley = 1f - Worley(x * period / (float)Resolution, y * period / (float)Resolution,
                                           period, seed + 101 + octave);
                sum += worley * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                period *= 2;
            }

            return Mathf.Clamp01(sum / total);
        }

        /// <summary>Distance to the nearest feature point, on a periodic cell grid so it tiles.</summary>
        private static float Worley(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float best = float.MaxValue;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = Wrap(xi + dx, period);
                    int cy = Wrap(yi + dy, period);

                    float px = xi + dx + Hash(cx, cy, seed);
                    float py = yi + dy + Hash(cx, cy, seed + 7919);

                    float ox = px - x;
                    float oy = py - y;
                    float d = ox * ox + oy * oy;

                    if (d < best)
                        best = d;
                }
            }

            return Mathf.Clamp01(Mathf.Sqrt(best));
        }

        private static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;

            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            int x1 = Wrap(x0 + 1, period);
            int y1 = Wrap(y0 + 1, period);
            x0 = Wrap(x0, period);
            y0 = Wrap(y0, period);

            float top = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx);
            float bottom = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx);
            return Mathf.Lerp(top, bottom, fy);
        }

        /// <summary>
        /// Stretches the field to the full 0..1 range. Without this the Perlin-Worley remap
        /// leaves it bunched in the middle and the coverage slider does nothing at the ends.
        /// </summary>
        private void Normalise()
        {
            float min = float.MaxValue;
            float max = float.MinValue;

            for (int i = 0; i < _density.Length; i++)
            {
                if (_density[i] < min)
                    min = _density[i];
                if (_density[i] > max)
                    max = _density[i];
            }

            float span = max - min;
            if (span < 1e-5f)
                return;

            for (int i = 0; i < _density.Length; i++)
                _density[i] = (_density[i] - min) / span;
        }

        private static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
        {
            float span = fromMax - fromMin;
            if (Mathf.Abs(span) < 1e-5f)
                return toMin;

            return toMin + (value - fromMin) / span * (toMax - toMin);
        }

        private static int Wrap(int v, int period)
        {
            int r = v % period;
            return r < 0 ? r + period : r;
        }

        private static float Hash(int x, int y, int seed)
        {
            int n = x * 374761393 + y * 668265263 + seed * 1274126177;
            n = (n ^ (n >> 13)) * 1274126177;
            n = n ^ (n >> 16);
            return (n & 0x7fffffff) / (float)0x7fffffff;
        }
    }
}
