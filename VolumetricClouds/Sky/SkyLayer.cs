using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// THE SKY LAYER (1.5.0): photographed skies behind the clouds -- cirrus, sheep clouds, far cumulus,
    /// a sky of several layers -- as Cities: Skylines II draws its distant clouds (Unity HDRP's Cloud
    /// Layer): a picture of the whole sky per kind, a slider each, and they add up. The pictures are cut
    /// out of CC0 photographs (Poly Haven) by tools/make-skylayer.ps1 and embedded as grey JPEGs
    /// (Resources/SkyLayer, ~1.7 MB for all eight: the author's cap is the whole mod around 5 MB).
    /// </summary>
    /// <remarks>
    /// A child of the cloud box with its own shader (SkyLayer.shader) and queue: after the game's stars,
    /// before the lightning bolt and the clouds, so everything else in the sky lays itself over it. Lit
    /// with the clouds' own light, copied from their material each frame (their brightness and tints
    /// included, invariant 8). Nothing of it exists until a row is above 0: then the eight pictures are
    /// decoded once (two RGBA textures, ~11 MB of graphics memory with mips) and kept for the session.
    /// The pictures are turned per city (its seed), and flow a little way along the wind and back
    /// (HDRP's distortion): clouds that move, a sky that never drifts away. Not touched: rain,
    /// lightning, the rainbow, the shadows.
    /// </remarks>
    public sealed class SkyLayer
    {
        /// <summary>After the game's stars (2503), before the bolt (the clouds' queue - 10) and the clouds (2520 or 3100).</summary>
        public const int Queue = 2507;

        /// <summary>Metres the wind carries the clouds for one cycle of the flow.</summary>
        private const double FlowPeriod = 20000.0;

        /// <summary>How far the flow carries the pictures, radians (about 2.3 degrees).</summary>
        private const float FlowReach = 0.04f;

        /// <summary>A drift step longer than this in one frame is a jump (a save read, a reset), not wind.</summary>
        private const double FlowJump = 2000.0;

        // Each kind: how high it stands (m), how much its own cloud shades it (the shader's depth towards the
        // sun is an average, 0..1), its forward scattering, its greatest opacity. 2026-10-13, the author: at
        // 100% "super dense and really dark" -- the shade was a sum of four looks (up to 12 of depth: 2% of
        // the sun) and the thickest pictures (far-2, sheep-2: half the sky, 28% of it opaque) hid it all.
        private static readonly Vector4[] KindLight =
        {
            new Vector4(9000f, 0.6f, 0.7f, 0.9f),   // cirrus: thin ice, bright round the sun
            new Vector4(4500f, 1.5f, 0.55f, 0.75f), // sheep clouds
            new Vector4(1500f, 2f, 0.5f, 0.75f),    // far clouds: cumulus with grey bellies
            new Vector4(3000f, 1.5f, 0.55f, 0.8f),  // cloud layers
        };

        private static readonly int IdPicturesA = Shader.PropertyToID("_SkyPicturesA");
        private static readonly int IdPicturesB = Shader.PropertyToID("_SkyPicturesB");
        private static readonly int IdWeights = Shader.PropertyToID("_SkyWeights");
        private static readonly int IdKinds = Shader.PropertyToID("_SkyKinds");
        private static readonly int IdTurn = Shader.PropertyToID("_SkyTurn");
        private static readonly int IdFlow = Shader.PropertyToID("_SkyFlow");
        private static readonly int IdSunDir = Shader.PropertyToID("_SunDir");
        private static readonly int IdSunColor = Shader.PropertyToID("_SunColor");
        private static readonly int IdAmbientColor = Shader.PropertyToID("_AmbientColor");
        private static readonly int IdSunsetEdge = Shader.PropertyToID("_SunsetEdge");

        private readonly Material _material;
        private readonly GameObject _holder;
        private readonly MeshRenderer _renderer;
        private Texture2D _picturesA;
        private Texture2D _picturesB;
        private bool _loadFailed;

        private readonly float[] _amounts = new float[SkyLayerMix.Kinds];
        private readonly float[] _scratch = new float[SkyLayerMix.Pictures.Length];
        private readonly Vector4[] _weights = new Vector4[2 * SkyLayerMix.Kinds];

        private double _flowMetres;
        private double _lastX;
        private double _lastZ;
        private bool _haveLast;

        private string _logged;
        private bool _loggedOn;

        public SkyLayer(Shader shader, Transform parent, Mesh box)
        {
            _material = new Material(shader) { name = "VolumetricCloudsSkyLayer", renderQueue = Queue };
            _material.SetVectorArray(IdKinds, KindLight);
            _material.SetVectorArray(IdWeights, _weights);

            _holder = new GameObject("VolumetricCloudsSkyLayer");
            _holder.transform.SetParent(parent, false);
            _holder.AddComponent<MeshFilter>().sharedMesh = box;
            _renderer = _holder.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.enabled = false;
        }

        /// <summary>The rows' amounts, 0..1 (cirrus, sheep clouds, far clouds, cloud layers).</summary>
        private void ReadAmounts()
        {
            _amounts[SkyLayerMix.Cirrus] = Amount(Settings.SkyCirrus);
            _amounts[SkyLayerMix.Sheep] = Amount(Settings.SkySheepClouds);
            _amounts[SkyLayerMix.Far] = Amount(Settings.SkyFarClouds);
            _amounts[SkyLayerMix.Layers] = Amount(Settings.SkyCloudLayers);
        }

        private static float Amount(FloatSetting setting)
        {
            return setting != null ? Mathf.Clamp01(setting.value) : 0f;
        }

        /// <summary>
        /// Once a frame, after the cloud material has this frame's light (CloudVolume.UpdateMaterial).
        /// <paramref name="clouds"/> null, or every row at 0: nothing is drawn.
        /// </summary>
        public void Update(Material clouds)
        {
            ReadAmounts();
            bool any = false;
            for (int k = 0; k < SkyLayerMix.Kinds; k++)
                any |= _amounts[k] > 0f;

            AdvanceFlow();
            LogState(clouds != null && any);

            if (clouds == null || !any)
            {
                _renderer.enabled = false;
                return;
            }

            if (_picturesA == null && !_loadFailed)
                Load();
            if (_loadFailed)
            {
                _renderer.enabled = false;
                return;
            }

            for (int k = 0; k < SkyLayerMix.Kinds; k++)
            {
                SkyLayerMix.Weights(k, _amounts[k], _scratch);
                _weights[2 * k] = new Vector4(_scratch[0], _scratch[1], _scratch[2], _scratch[3]);
                _weights[2 * k + 1] = new Vector4(_scratch[4], _scratch[5], _scratch[6], _scratch[7]);
            }
            _material.SetVectorArray(IdWeights, _weights);
            _material.SetTexture(IdPicturesA, _picturesA);
            _material.SetTexture(IdPicturesB, _picturesB);

            // This city's turn of the pictures, from its pattern seed: cities don't share one sky, and
            // "Reset cloud pattern" turns it too.
            uint hash = unchecked((uint)SkyPattern.FieldSeed * 2654435761u);
            float turn = hash / 4294967296f * 2f * Mathf.PI;
            _material.SetVector(IdTurn, new Vector4(Mathf.Cos(turn), Mathf.Sin(turn), 0f, 0f));

            Vector3 wind = CloudWeather.WindDirection;
            Vector2 flat = new Vector2(wind.x, wind.z);
            if (flat.sqrMagnitude > 1e-6f)
                flat.Normalize();
            _material.SetVector(IdFlow, new Vector4(Drift.Phase(_flowMetres, FlowPeriod), flat.x * FlowReach, flat.y * FlowReach, 0f));

            // The clouds' own light (CloudVolume.ApplyLighting set it this frame).
            _material.SetVector(IdSunDir, clouds.GetVector(IdSunDir));
            _material.SetVector(IdSunColor, clouds.GetVector(IdSunColor));
            _material.SetVector(IdAmbientColor, clouds.GetVector(IdAmbientColor));
            _material.SetVector(IdSunsetEdge, clouds.GetVector(IdSunsetEdge));

            _renderer.enabled = true;
        }

        /// <summary>
        /// The flow's own clock: the metres the wind has carried the clouds this session, from the wind's
        /// drift (sim time: it stops with the game). A jump of the drift (a save read) is not wind.
        /// </summary>
        private void AdvanceFlow()
        {
            double x = CloudWind.X, z = CloudWind.Z;
            if (_haveLast)
            {
                double dx = x - _lastX, dz = z - _lastZ;
                double step = Math.Sqrt(dx * dx + dz * dz);
                if (step < FlowJump)
                    _flowMetres += step;
            }
            _lastX = x;
            _lastZ = z;
            _haveLast = true;
        }

        /// <summary>The rows' state: on the log when the layer comes on or goes off, other changes on the detailed log.</summary>
        private void LogState(bool on)
        {
            string text;
            if (!on)
            {
                text = "off";
            }
            else
            {
                var line = new System.Text.StringBuilder();
                for (int k = 0; k < SkyLayerMix.Kinds; k++)
                {
                    if (k > 0)
                        line.Append("; ");
                    line.Append(SkyLayerMix.KindNames[k]).Append(' ').Append((_amounts[k] * 100f).ToString("F0")).Append("% (")
                        .Append(SkyLayerMix.Describe(k, _amounts[k])).Append(')');
                }
                text = line.ToString();
            }

            if (text == _logged)
                return;

            if (_logged == null || on != _loggedOn)
                Log.Msg("sky layer: " + text);
            else if (Log.Detailed)
                Log.Detail("sky layer: " + text);

            _logged = text;
            _loggedOn = on;
        }

        /// <summary>
        /// The eight pictures, decoded (LoadImage, probed in the 5.6 editor 2026-10-12: a JPEG comes out
        /// RGB24, its rows bottom up -- row 0 the horizon) and packed four to a linear, mipmapped RGBA
        /// texture. Once per session; a failure leaves the layer off and says why.
        /// </summary>
        private void Load()
        {
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                Color32[] a = null, b = null;
                int width = 0, height = 0;
                for (int p = 0; p < SkyLayerMix.Pictures.Length; p++)
                {
                    string name = SkyLayerMix.Pictures[p];
                    Color32[] pixels;
                    int w, h;
                    string error = Decode(name, out pixels, out w, out h);
                    if (error != null)
                    {
                        Fail("picture '" + name + "' " + error);
                        return;
                    }

                    if (a == null)
                    {
                        width = w;
                        height = h;
                        a = new Color32[w * h];
                        b = new Color32[w * h];
                    }
                    else if (w != width || h != height)
                    {
                        Fail("picture '" + name + "' is " + w + "x" + h + ", the others " + width + "x" + height);
                        return;
                    }

                    Color32[] target = p < 4 ? a : b;
                    int channel = p % 4;
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        byte v = pixels[i].r;
                        switch (channel)
                        {
                            case 0: target[i].r = v; break;
                            case 1: target[i].g = v; break;
                            case 2: target[i].b = v; break;
                            default: target[i].a = v; break;
                        }
                    }
                }

                _picturesA = Make(a, width, height, "VolumetricCloudsSkyPicturesA");
                _picturesB = Make(b, width, height, "VolumetricCloudsSkyPicturesB");
                float megabytes = 2f * width * height * 4f * (4f / 3f) / (1024f * 1024f);
                Log.Msg("sky layer: " + SkyLayerMix.Pictures.Length + " pictures of " + width + "x" + height +
                        " decoded in " + watch.ElapsedMilliseconds + " ms, two textures, " + megabytes.ToString("F1") +
                        " MB of graphics memory with mips; kept for the session");
            }
            catch (Exception e)
            {
                Fail(e.GetType().Name + ": " + e.Message);
            }
        }

        private void Fail(string why)
        {
            _loadFailed = true;
            Log.Warn("sky layer: cannot load its pictures (" + why + "); it stays off this session.");
        }

        private static string Decode(string name, out Color32[] pixels, out int width, out int height)
        {
            pixels = null;
            width = height = 0;
            string resource = "SkyLayer." + name + ".jpg";
            byte[] bytes;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null)
                    return "is not embedded (" + resource + ")";
                bytes = new byte[stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0)
                        break;
                    offset += read;
                }
            }

            Texture2D decoded = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                if (!decoded.LoadImage(bytes))
                    return "could not be decoded";
                width = decoded.width;
                height = decoded.height;
                pixels = decoded.GetPixels32();
                return null;
            }
            finally
            {
                UnityEngine.Object.Destroy(decoded);
            }
        }

        private static Texture2D Make(Color32[] pixels, int width, int height, string name)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>For the frame log line: "off" or the four amounts.</summary>
        public string Describe()
        {
            if (!_renderer.enabled)
                return "off";
            return (_amounts[0] * 100f).ToString("F0") + "/" + (_amounts[1] * 100f).ToString("F0") + "/" +
                   (_amounts[2] * 100f).ToString("F0") + "/" + (_amounts[3] * 100f).ToString("F0") + "%";
        }

        public void Dispose()
        {
            if (_holder != null)
                UnityEngine.Object.Destroy(_holder);
            if (_material != null)
                UnityEngine.Object.Destroy(_material);
            if (_picturesA != null)
                UnityEngine.Object.Destroy(_picturesA);
            if (_picturesB != null)
                UnityEngine.Object.Destroy(_picturesB);
        }
    }
}
