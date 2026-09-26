using System;
using System.Threading;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Raymarched volumetric clouds. Draws a box that follows the camera; the shader
    /// (compiled in Unity 5.6 and embedded as an AssetBundle) marches the cloud slab for
    /// every pixel. Also drives the <see cref="CloudShadowMap"/>, since both need the same
    /// noise volume. (Until 1.1.0 a switch could trade this for flat billboard clouds; the
    /// raymarched clouds are the mod, so that went.)
    /// </summary>
    public class CloudVolume : MonoBehaviour
    {
        /// <summary>Edge of the 3D noise volume. Part of what a pattern's seed reproduces: changing it changes every saved sky.</summary>
        public const int NoiseSize = 64;
        private const float MaxDistance = 30000f;

        /// <summary>True while the volumetric layer is actually drawing (the fog needs the pass).</summary>
        public static bool IsActive { get; private set; }

        private CloudDensityField _field;
        private CloudShadowMap _shadowMap;
        private Camera _camera;
        private GameObject _holder;
        private MeshRenderer _renderer;
        private Material _material;
        private Mesh _mesh;
        private Texture3D _noise;
        private Texture3D _detail;

        private Thread _noiseThread;
        private volatile byte[] _noiseBytes;

        // Cloud detail's texture (CloudDetail3D), made on the same worker thread BEFORE the noise
        // is handed over, so the clouds appear once, with it -- never soft first and sculpted a
        // moment later. Written before _noiseBytes (volatile), read after it.
        private volatile Color32[][] _detailLevels;
        private string _detailError;
        private int _detailMilliseconds;
        private bool _failed;
        private bool _loggedLighting;

        // The Cumulus style's noise (CloudStyle / CumulusNoise3D). Made on the load's worker thread
        // when the style is Cumulus then, else the first time it is chosen (a thread of its own;
        // the clouds stay Classic the few seconds that takes), and again for a new pattern.
        // _cumulusSeed is the noise seed the uploaded one was made from: when it is not the city's,
        // it is out of date. Written by the worker before _cumulusPending (volatile), read after.
        private Texture3D _cumulus;
        private int _cumulusSeed;
        private volatile Color32[][] _cumulusPending;
        private int _cumulusPendingSeed;
        private string _cumulusError;
        private int _cumulusMilliseconds;
        private volatile bool _cumulusWorking;

        private static readonly int IdSteps = Shader.PropertyToID("_Steps");
        private static readonly int IdMaxDistance = Shader.PropertyToID("_MaxDistance");
        private static readonly int IdUseDepth = Shader.PropertyToID("_UseDepth");
        private static readonly int IdSunDir = Shader.PropertyToID("_SunDir");
        private static readonly int IdSunColor = Shader.PropertyToID("_SunColor");
        private static readonly int IdAmbientColor = Shader.PropertyToID("_AmbientColor");
        private static readonly int IdRainShaftDensity = Shader.PropertyToID("_RainShaftDensity");
        private static readonly int IdRainSteps = Shader.PropertyToID("_RainSteps");
        private static readonly int IdRainMaxDistance = Shader.PropertyToID("_RainMaxDistance");
        private static readonly int IdRainFloor = Shader.PropertyToID("_RainFloor");
        private static readonly int IdRainFall = Shader.PropertyToID("_RainFall");
        private static readonly int IdRainCurtainScale = Shader.PropertyToID("_RainCurtainScale");
        private static readonly int IdRainAmbient = Shader.PropertyToID("_RainAmbient");
        private static readonly int IdRainSun = Shader.PropertyToID("_RainSun");
        private static readonly int IdNightOpacity = Shader.PropertyToID("_NightOpacity");
        private static readonly int IdCloudExtent = Shader.PropertyToID("_CloudExtent");
        private static readonly int IdNightGlow = Shader.PropertyToID("_NightGlow");
        private static readonly int IdFogAmount = Shader.PropertyToID("_FogAmount");
        private static readonly int IdFogDensity = Shader.PropertyToID("_FogDensity");
        private static readonly int IdFogThreshold = Shader.PropertyToID("_FogThreshold");
        private static readonly int IdFogTile = Shader.PropertyToID("_FogTile");
        private static readonly int IdFogPhase = Shader.PropertyToID("_FogPhase");
        private static readonly int IdFogBillowTile = Shader.PropertyToID("_FogBillowTile");
        private static readonly int IdFogBillowPhase = Shader.PropertyToID("_FogBillowPhase");
        private static readonly int IdFogLead = Shader.PropertyToID("_FogLead");
        private static readonly int IdFogSwirlTile = Shader.PropertyToID("_FogSwirlTile");
        private static readonly int IdFogSwirlPhase = Shader.PropertyToID("_FogSwirlPhase");
        private static readonly int IdFogWispTile = Shader.PropertyToID("_FogWispTile");
        private static readonly int IdFogWispPhase = Shader.PropertyToID("_FogWispPhase");
        private static readonly int IdFogBoil = Shader.PropertyToID("_FogBoil");
        private static readonly int IdFogPool = Shader.PropertyToID("_FogPool");
        private static readonly int IdFogFloor = Shader.PropertyToID("_FogFloor");
        private static readonly int IdFogBreakup = Shader.PropertyToID("_FogBreakup");
        private static readonly int IdTerrainTex = Shader.PropertyToID("_TerrainTex");
        private static readonly int IdTerrainMapSize = Shader.PropertyToID("_TerrainMapSize");
        private static readonly int IdFogHeight = Shader.PropertyToID("_FogHeight");
        private static readonly int IdFogBase = Shader.PropertyToID("_FogBase");
        private static readonly int IdFogFollowGround = Shader.PropertyToID("_FogFollowGround");
        private static readonly int IdFogLevel = Shader.PropertyToID("_FogLevel");
        private static readonly int IdFogSteps = Shader.PropertyToID("_FogSteps");
        private static readonly int IdFogMaxDistance = Shader.PropertyToID("_FogMaxDistance");
        private static readonly int IdFogAmbient = Shader.PropertyToID("_FogAmbient");
        private static readonly int IdFogSun = Shader.PropertyToID("_FogSun");
        private static readonly int IdCloudShadowTex = Shader.PropertyToID("_CloudShadowTex");
        private static readonly int IdShadowAvailable = Shader.PropertyToID("_ShadowAvailable");
        private static readonly int IdShadowOrigin = Shader.PropertyToID("_ShadowOrigin");
        private static readonly int IdShadowRight = Shader.PropertyToID("_ShadowRight");
        private static readonly int IdShadowUp = Shader.PropertyToID("_ShadowUp");
        private static readonly int IdShadowSize = Shader.PropertyToID("_ShadowSize");
        private static readonly int IdShadowDarkness = Shader.PropertyToID("_ShadowDarkness");
        private static readonly int IdDetailLight = Shader.PropertyToID("_DetailLight");
        private static readonly int IdDetailLight2 = Shader.PropertyToID("_DetailLight2");
        private static readonly int IdCumulusLight = Shader.PropertyToID("_CumulusLight");
        private static readonly int IdCumulusLobes = Shader.PropertyToID("_CumulusLobes");
        private static readonly int IdCumulusLobeG = Shader.PropertyToID("_CumulusLobeG");

        /// <summary>
        /// Extinction per metre inside full-strength rain, at 100% on the slider. Real
        /// downpours are several times denser, but a city builder has to stay readable from a
        /// kilometre up: at this value 1 km of heavy rain still passes about half the view.
        /// </summary>
        private const float RainExtinction = 0.0006f;
        private const float RainMaxDistance = 14000f;

        /// <summary>Radiance the night glow adds to the very base of a cloud, at 100%.</summary>
        private const float NightGlowStrength = 0.03f;

        /// <summary>
        /// The cloud brightness the fog was tuned under, kept as a constant now that the fog no
        /// longer borrows the clouds'.
        /// </summary>
        /// <remarks>
        /// Every fog value anyone has chosen was judged against a fog silently multiplied by
        /// the Cloud brightness slider -- see <see cref="ApplyLighting"/>. Taking that factor
        /// out without putting anything back would have made every saved fog value, and the
        /// shipping default, wrong by whatever that slider happened to be. Folding it in here
        /// instead means "Fog brightness 100%" keeps its meaning and finally does only its own
        /// job. 0.5 is the value the fog was built and tuned at.
        /// </remarks>
        public const float FogLightScale = 0.5f;

        /// <summary>Even a moonless night sky silhouettes cloud faintly; nothing goes fully black.</summary>
        private const float AmbientFloor = 0.012f;

        private const float DetailInterval = 1f;
        private float _nextDetailTime;
        private int _frameCount;
        private float _frameTime;
        private float _frameWorst;
        private bool _loggedDecoupling;

        public void Initialise(CloudDensityField field)
        {
            _field = field;
        }

        private void Start()
        {
            Shader shader = ShaderBundle.Get(ShaderBundle.Raymarch);
            if (shader == null)
            {
                _failed = true;
                enabled = false;
                return;
            }

            _material = new Material(shader) { name = "VolumetricCloudsRaymarch" };

            _holder = new GameObject("VolumetricCloudsVolume");
            _mesh = BuildBox();
            _holder.AddComponent<MeshFilter>().sharedMesh = _mesh;

            _renderer = _holder.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.enabled = false;

            _shadowMap = CloudShadowMap.TryCreate();

            // The noise volume is tens of millions of hash evaluations: generate it off
            // the main thread and upload when it lands. Clouds appear a moment after load.
            // The seed is this city's (SkyPattern): the save's, or a new one for a new city.
            int seed = SkyPattern.NoiseSeed;
            bool cumulus = CloudStyle.IsCumulus;
            CloudDetail.Failed = false;
            CloudStyle.Failed = false;
            if (cumulus)
                _cumulusWorking = true;
            _noiseThread = new Thread(() =>
            {
                byte[] noise;
                try
                {
                    noise = CloudNoise3D.Generate(NoiseSize, seed);
                }
                catch (Exception)
                {
                    noise = new byte[0];
                }

                string error;
                int milliseconds;
                _detailLevels = CloudDetail.BuildLevels(seed, out error, out milliseconds);
                _detailError = error;
                _detailMilliseconds = milliseconds;

                // The Cumulus noise too, when that is the style: the clouds then appear once, as
                // Cumulus, never Classic first.
                if (cumulus)
                    MakeCumulus(seed);

                // Last: the main thread takes them once it sees this.
                _noiseBytes = noise;
            })
            {
                IsBackground = true,
                Name = "VolumetricClouds noise",
            };
            _noiseThread.Start();

            Log.Msg("cloud volume created, generating " + NoiseSize + "^3 noise on a worker thread");
        }

        private void LateUpdate()
        {
            if (_failed || _field == null)
                return;

            if (_noise == null && !TryUploadNoise())
                return;

            UpdateCumulus();

            // Once: find out how shaders see the weather texture, so its CPU twin can match.
            _field.MeasureGpuDecoding();

            UpdateShadowMap();

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                    return;

                // The shader reads scene depth so buildings and hills occlude the clouds.
                _camera.depthTextureMode |= DepthTextureMode.Depth;
                Log.Msg("cloud volume camera='" + _camera.name + "' path=" + _camera.actualRenderingPath +
                        " far=" + _camera.farClipPlane + " hdr=" + _camera.allowHDR +
                        " colorSpace=" + QualitySettings.activeColorSpace);
            }

            bool wanted = Settings.CloudsVisible == null || Settings.CloudsVisible.value;

            _renderer.enabled = wanted;
            IsActive = wanted;
            if (!wanted)
                return;

            // Keep the box around the camera and inside the far plane, corners included.
            _holder.transform.position = _camera.transform.position;
            _holder.transform.localScale = Vector3.one * (_camera.farClipPlane * 0.45f);

            UpdateMaterial();
        }

        /// <summary>
        /// With "Show clouds" off there is nothing to cast a shadow, and the pass does not run.
        /// </summary>
        private void UpdateShadowMap()
        {
            if (_shadowMap == null)
                return;

            if (!Settings.ShadowsCast)
                return;

            DayNightProperties properties = DayNightProperties.instance;
            if (properties == null || properties.m_SunLight == null)
                return;

            _shadowMap.Render(properties.m_SunLight.GetComponent<Light>(), _field, _noise);
        }

        private bool TryUploadNoise()
        {
            byte[] bytes = _noiseBytes;
            if (bytes == null)
                return false;

            if (bytes.Length != NoiseSize * NoiseSize * NoiseSize * 4)
            {
                Log.Error("3D noise generation failed; volumetric clouds disabled.");
                _failed = true;
                return false;
            }

            _noise = new Texture3D(NoiseSize, NoiseSize, NoiseSize, TextureFormat.ARGB32, false)
            {
                name = "VolumetricCloudsNoise3D",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
            };
            _noise.SetPixels32(ToColors(bytes));
            _noise.Apply(false);
            _noiseBytes = null;

            UploadDetail(_detailLevels);
            _detailLevels = null;

            // Made with it when the style was Cumulus at load: in the same frame.
            UpdateCumulus();

            Log.Msg("3D noise uploaded; volumetric clouds live.");
            return true;
        }

        /// <summary>
        /// Worker thread: the Cumulus noise for <paramref name="seed"/>, handed to the main thread
        /// through <see cref="_cumulusPending"/>. A failure is kept for the log.
        /// </summary>
        private void MakeCumulus(int seed)
        {
            string error;
            int milliseconds;
            Color32[][] levels = CloudStyle.BuildLevels(seed, out error, out milliseconds);

            _cumulusError = error;
            _cumulusMilliseconds = milliseconds;
            _cumulusPendingSeed = seed;
            _cumulusPending = levels;
            _cumulusWorking = false;
        }

        /// <summary>
        /// Main thread, every frame once the noise is up: uploads a finished Cumulus noise, and
        /// starts one when the style wants it and the one there is missing or for another seed
        /// (a new pattern that came without it). A result for an old seed is dropped.
        /// </summary>
        private void UpdateCumulus()
        {
            // _cumulusWorking is read before the error: the worker clears it last.
            Color32[][] levels = _cumulusPending;
            if (levels != null || (!_cumulusWorking && _cumulusError != null))
            {
                _cumulusPending = null;
                int seed = _cumulusPendingSeed;
                string error = _cumulusError;
                _cumulusError = null;

                if (levels == null)
                {
                    CloudStyle.Failed = true;
                    Log.Warn("cloud style: the Cumulus noise could not be made (" + (error ?? "unknown") +
                             "); the clouds are drawn Classic");
                }
                else if (seed == SkyPattern.NoiseSeed)
                {
                    UploadCumulus(levels, seed, "made in " + _cumulusMilliseconds + " ms on worker threads");
                }
            }

            bool wanted = CloudStyle.IsCumulus || _cumulus != null;
            if (wanted && !_cumulusWorking && !CloudStyle.Failed && _cumulusSeed != SkyPattern.NoiseSeed)
            {
                int seed = SkyPattern.NoiseSeed;
                _cumulusWorking = true;
                Thread thread = new Thread(() => MakeCumulus(seed))
                {
                    IsBackground = true,
                    Name = "VolumetricClouds cumulus",
                };
                thread.Start();
                Log.Msg("cloud style: making the Cumulus noise (" + CumulusNoise3D.Size + "^3) for seed " + seed + " on a worker thread");
            }
        }

        /// <summary>
        /// The Cumulus noise, with the mip chain the worker built: ARGB32 read from ALPHA, like
        /// cloud detail's texture and for the same reason -- NEVER R8, which crashes Unity 5.6's
        /// Texture3D.SetPixels32 (see UploadDetail). Into the same texture when there is one.
        /// </summary>
        private void UploadCumulus(Color32[][] levels, int seed, string how)
        {
            int size = CumulusNoise3D.Size;
            try
            {
                if (_cumulus == null)
                {
                    _cumulus = new Texture3D(size, size, size, TextureFormat.ARGB32, true)
                    {
                        name = "VolumetricCloudsCumulus3D",
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = FilterMode.Trilinear,
                    };
                }

                for (int level = 0; level < levels.Length; level++)
                    _cumulus.SetPixels32(levels[level], level);
                _cumulus.Apply(false);
            }
            catch (Exception e)
            {
                if (_cumulus != null)
                    Destroy(_cumulus);
                _cumulus = null;
                CloudStyle.Texture = null;
                CloudStyle.Failed = true;
                Log.Warn("cloud style: the Cumulus noise could not be uploaded (" + e.GetType().Name + ": " + e.Message +
                         "); the clouds are drawn Classic");
                return;
            }

            _cumulusSeed = seed;
            CloudStyle.Texture = _cumulus;
            Log.Msg("cloud style: Cumulus noise " + size + "^3 ARGB32 (read from alpha), " + levels.Length + " mip levels, " +
                    how + " (generator " + CumulusNoise3D.Generator + ", seed " + seed + ")");
        }

        /// <summary>
        /// Cloud detail's texture, with the mip chain the worker built: ARGB32 -- the format the
        /// 64^3 noise has always used, on every platform -- read from ALPHA, which is never
        /// sRGB-decoded. NEVER R8: Unity 5.6's Texture3D.SetPixels32 on an R8 texture calls a null
        /// function pointer and the whole game crashes (2026-09-26, three times in the author's
        /// game; then reproduced in the 5.6 editor, where ARGB32, RGBA32 and Alpha8 all filled
        /// every mip level and read back exactly). No managed exception, so nothing can catch it:
        /// only formats probed there may ever be used here. Without the texture the clouds are
        /// drawn as before, and the log says why.
        /// </summary>
        private void UploadDetail(Color32[][] levels)
        {
            if (levels == null)
            {
                CloudDetail.Failed = true;
                Log.Warn("cloud detail: its texture could not be made (" + (_detailError ?? "unknown") +
                         "); the clouds are drawn without detail");
                return;
            }

            int size = CloudDetail3D.Size;
            try
            {
                _detail = new Texture3D(size, size, size, TextureFormat.ARGB32, true)
                {
                    name = "VolumetricCloudsDetail3D",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                };

                for (int level = 0; level < levels.Length; level++)
                    _detail.SetPixels32(levels[level], level);
                _detail.Apply(false);
            }
            catch (Exception e)
            {
                if (_detail != null)
                    Destroy(_detail);
                _detail = null;
                CloudDetail.Failed = true;
                Log.Warn("cloud detail: its texture could not be uploaded (" + e.GetType().Name + ": " + e.Message +
                         "); the clouds are drawn without detail");
                return;
            }

            CloudDetail.Texture = _detail;
            CloudDetail.TextureIsAlpha = true;
            Log.Msg("cloud detail: texture " + size + "^3 ARGB32 (read from alpha), " + levels.Length + " mip levels, made in " +
                    _detailMilliseconds + " ms on worker threads (generator " + CloudDetail3D.Generator + ")");
        }

        private static Color32[] ToColors(byte[] bytes)
        {
            Color32[] colors = new Color32[bytes.Length / 4];
            for (int i = 0; i < colors.Length; i++)
            {
                int o = i * 4;
                colors[i] = new Color32(bytes[o], bytes[o + 1], bytes[o + 2], bytes[o + 3]);
            }

            return colors;
        }

        /// <summary>
        /// False while the city's first noise is still being generated: a new pattern waits for
        /// it, or that late upload would put the old seed's noise back over the new one.
        /// </summary>
        public bool CanReplaceNoise
        {
            get { return _failed || !enabled || _noise != null; }
        }

        /// <summary>
        /// "Reset cloud pattern": the new seed's noise, detail and (when it was wanted) Cumulus
        /// noise, generated on a worker thread, into the SAME textures -- the shadow map and every
        /// material keep their reference. Main thread, after SkyPattern took the new seeds.
        /// </summary>
        public void ReplaceNoise(byte[] bytes, Color32[][] detail, Color32[][] cumulus)
        {
            if (_noise == null || bytes == null || bytes.Length != NoiseSize * NoiseSize * NoiseSize * 4)
                return;

            _noise.SetPixels32(ToColors(bytes));
            _noise.Apply(false);

            if (_detail != null && detail != null)
            {
                for (int level = 0; level < detail.Length; level++)
                    _detail.SetPixels32(detail[level], level);
                _detail.Apply(false);
            }

            // Without it (the style was Classic when the pattern was asked for), the one there is
            // now for the old seed, and UpdateCumulus makes a new one if it is wanted.
            if (cumulus != null && !CloudStyle.Failed)
                UploadCumulus(cumulus, SkyPattern.NoiseSeed, "with the new pattern");
        }

        private void UpdateMaterial()
        {
            float steps = Settings.CloudQuality != null ? Settings.CloudQuality.value : Settings.Defaults.Quality;
            bool useDepth = Settings.CloudDepthOcclusion == null || Settings.CloudDepthOcclusion.value;

            CloudShaderParams.Apply(_material, _field, _noise);
            _material.SetFloat(IdSteps, steps);
            _material.SetFloat(IdMaxDistance, MaxDistance);
            _material.SetFloat(IdUseDepth, useDepth ? 1f : 0f);

            ApplyLighting();
            ApplyDetailLight();
            CloudLightning.ApplyTo(_material);
        }

        /// <summary>
        /// The light of cloud detail or of the Cumulus style: the octaves (CloudDetail) or the four
        /// lobes (CloudStyle), how far the light steps reach, and the mip offset that makes a
        /// pixel's footprint pick the noise's mip level. Unused while neither is drawn (the
        /// shader's branches never read them).
        /// </summary>
        private void ApplyDetailLight()
        {
            bool cumulus = CloudStyle.Drawn;
            if (!cumulus && (!CloudDetail.On || CloudDetail.Texture == null))
                return;

            // The light steps reach about the tallest cloud: the layer's thickness, or under Cumulus
            // the whole span of its curve or the thickest the layer gets (as the renders it was
            // chosen on), whichever is more.
            float thickness = Settings.CloudThickness != null ? Settings.CloudThickness.value : Settings.Defaults.Thickness;
            float reach = cumulus ? Mathf.Max(CloudStyle.Span, thickness * CloudStyle.LayerThickest) : thickness;

            // After the three fine steps (6, 12, 24 m) four more grow by this much each, to about
            // 1.1 x that: 2x for the 350 m layer cloud detail's look was chosen on.
            float growth = Mathf.Max(1.25f, Mathf.Pow(Mathf.Max(1.1f * reach, 100f) / 24f, 0.25f));

            // A pixel covers t x angle metres at distance t; its mip level is log2 of that over a
            // texel, i.e. log2(t) + log2(angle / texel).
            float angle = 2f * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, _camera.pixelHeight);
            float texel = CloudShaderParams.NoiseTexel;
            float occlusion = cumulus ? CloudStyle.AmbientOcclusion : CloudDetail.AmbientOcclusion;

            // w: at a low amount the old light is handed over to the detail's (Classic; the Cumulus
            // light is its own at every amount).
            float share = cumulus ? 1f : CloudDetail.NewLightShare(CloudDetail.Amount);
            _material.SetVector(IdDetailLight2, new Vector4(occlusion, growth, Mathf.Log(angle / texel, 2f), share));

            if (cumulus)
            {
                _material.SetVector(IdCumulusLight, new Vector4(
                    CloudStyle.MultipleExtinction, CloudStyle.LightGain, CloudStyle.SingleCap, 1f / CloudStyle.Span));
                _material.SetVector(IdCumulusLobes, new Vector4(
                    CloudStyle.SingleW1, CloudStyle.SingleW2, CloudStyle.MultipleW1, CloudStyle.MultipleW2));
                _material.SetVector(IdCumulusLobeG, new Vector4(
                    CloudStyle.SingleG1, CloudStyle.SingleG2, CloudStyle.MultipleG1, CloudStyle.MultipleG2));
            }
            else
            {
                _material.SetVector(IdDetailLight, new Vector4(CloudDetail.OctaveA, CloudDetail.OctaveB, CloudDetail.OctaveC, CloudDetail.LightGain));
            }
        }

        /// <summary>
        /// Lights the clouds from whichever of the sun and moon is currently brighter, plus
        /// the scene's ambient. Scaled down because the game's sun runs at HDR intensities
        /// that would clip a near-white cloud straight to flat white.
        /// </summary>
        /// <remarks>
        /// The light is worked out ONCE, unscaled, and each consumer applies its own knob at
        /// the point of use. It used to bake the Cloud brightness slider into the two colours
        /// it then handed to the fog and the rain, so "Fog brightness" was never the fog's
        /// brightness at all -- it was a multiplier on the cloud's. With the brightness curve
        /// driving cloud brightness from the cover, that bug would have swung the fog four to
        /// one as the sky filled in.
        ///
        /// What stays shared is <c>overcast</c>: that is the light level everything in the
        /// world stands in, and the fog SHOULD be dimmer under heavy cloud. It is the artistic
        /// brightness knob that has no business leaving the cloud material.
        /// </remarks>
        private void ApplyLighting()
        {
            Light key = null;
            Light sun = null;
            DayNightProperties properties = DayNightProperties.instance;

            if (properties != null)
            {
                sun = properties.m_SunLight != null ? properties.m_SunLight.GetComponent<Light>() : null;
                Light moon = properties.m_MoonLight != null ? properties.m_MoonLight.GetComponent<Light>() : null;

                key = sun;
                if (moon != null && (sun == null || moon.intensity > sun.intensity))
                    key = moon;
            }

            float coverage = CloudShaderParams.Coverage;
            bool auto = Settings.BrightnessAuto;
            float brightness = Settings.EffectiveBrightness(coverage);
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

            Vector3 sunDir = Vector3.up;
            Color lightSun = Color.black;

            // Heavier cover reads greyer: the light everything stands in, before anybody's
            // brightness knob. Always computed, whatever the curve is doing -- only the CLOUD
            // skips it (the curve is then its single coverage-to-brightness relationship, and
            // two dimmers on one axis is how the old global-dim mess started).
            float illuminationFloor = Settings.MinIllumination != null
                ? Mathf.Clamp01(Settings.MinIllumination.value) : Settings.Defaults.MinIllumination;
            float overcast = Mathf.Lerp(1f, illuminationFloor, coverage);

            if (key != null)
            {
                sunDir = -key.transform.forward;
                Color c = linear ? key.color.linear : key.color;
                lightSun = c * (key.intensity * 0.25f);
            }

            Color ambient = RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat
                ? RenderSettings.ambientLight
                : RenderSettings.ambientSkyColor;
            if (linear)
                ambient = ambient.linear;

            Color lightAmbient = ambient * 0.9f;
            Color baseSun = lightSun * overcast;

            Color cloudSun = (auto ? lightSun : baseSun) * brightness;
            Color cloudAmbient = WithFloor(lightAmbient * brightness);

            // The player's grading, on the CLOUD's two lights only: the sunlit side and the
            // shaded side. A tint changes the hue, never the brightness, and white is exactly
            // (1, 1, 1) -- the shader gets bit for bit what it got before colours existed.
            Color sunlitTint = TintOf(Settings.CloudSunlitColor, Settings.Defaults.SunlitColor);
            Color shadeTint = TintOf(Settings.CloudShadeColor, Settings.Defaults.ShadeColor);

            _material.SetVector(IdSunDir, sunDir.normalized);
            _material.SetVector(IdSunColor, AsVector(cloudSun * sunlitTint));
            _material.SetVector(IdAmbientColor, AsVector(cloudAmbient * shadeTint));

            // Rain curtains hang from the clouds and are drawn in the same pass, so they keep
            // the CLOUD's two colours: one picture, one light. (A cloud override at 20% with
            // another mod's rain at full is the one corner where that reads bright.) The cloud's
            // light, NOT its grading: rain shafts under pink clouds would read as a bug.
            float curtains = Settings.RainCurtains != null ? Mathf.Max(0f, Settings.RainCurtains.value) : 1f;
            _material.SetFloat(IdRainShaftDensity, CloudRain.Active ? RainExtinction * curtains : 0f);
            _material.SetFloat(IdRainSteps, 20f);
            _material.SetFloat(IdRainMaxDistance, RainMaxDistance);
            _material.SetFloat(IdRainFloor, 0f);
            _material.SetFloat(IdRainFall, -CloudRain.FallOffset.y);
            _material.SetFloat(IdRainCurtainScale, CloudRain.CurtainScale);
            _material.SetVector(IdRainAmbient, AsVector(cloudAmbient) * 0.6f);
            _material.SetVector(IdRainSun, AsVector(cloudSun) * 0.12f);

            ApplyNight(sun, shadeTint);
            ApplyFog(sun, lightAmbient, baseSun);

            if (!_loggedLighting)
            {
                _loggedLighting = true;
                Log.Msg("cloud lighting key='" + (key == null ? "none" : key.name) + "' intensity=" +
                        (key == null ? 0f : key.intensity) + " sunColor=" + cloudSun +
                        " ambientMode=" + RenderSettings.ambientMode + " ambient=" + cloudAmbient);
            }

            if (!_loggedDecoupling && lightSun.maxColorComponent > 0.001f)
            {
                // Proves from the log alone that the fog no longer rides on the cloud's
                // brightness: baseSun and lightAmbient are the light, and each consumer's
                // colours are that light times ITS own knob.
                _loggedDecoupling = true;
                Log.Msg("light decoupling: baseSun=" + baseSun + " lightAmbient=" + lightAmbient +
                        " overcast=" + overcast.ToString("F2") +
                        " | cloud x" + brightness.ToString("F2") + (auto ? " (auto)" : " (fixed)") +
                        " sun=" + cloudSun + " ambient=" + cloudAmbient +
                        " | fog x" + FogLightScale.ToString("F2") + " x fogBrightness" +
                        " | rain = the cloud's");
            }

            // Every frame goes into the average, so the "frame:" line is the mean of the whole
            // second it covers and not whichever single frame the timer happened to land on.
            _frameCount++;
            _frameTime += Time.unscaledDeltaTime;
            _frameWorst = Mathf.Max(_frameWorst, Time.unscaledDeltaTime);

            if (Time.time >= _nextDetailTime)
            {
                _nextDetailTime = Time.time + DetailInterval;

                if (Log.Detailed)
                    LogDetail(coverage, auto, brightness, overcast);

                _frameCount = 0;
                _frameTime = 0f;
                _frameWorst = 0f;
            }
        }

        /// <summary>Never fully black: even a moonless night sky silhouettes cloud faintly.</summary>
        private static Color WithFloor(Color colour)
        {
            colour.r = Mathf.Max(colour.r, AmbientFloor);
            colour.g = Mathf.Max(colour.g, AmbientFloor);
            colour.b = Mathf.Max(colour.b, AmbientFloor * 1.3f);
            return colour;
        }

        private static Vector4 AsVector(Color colour)
        {
            return new Vector4(colour.r, colour.g, colour.b, 0f);
        }

        /// <summary>
        /// The once-a-second diagnostic pair: what the brightness curve is doing, and what the
        /// frame costs. The second line is what turns any user's report into data -- the mod
        /// has only ever been measured on one GPU.
        /// </summary>
        private void LogDetail(float coverage, bool auto, float brightness, float overcast)
        {
            Log.Detail("brightness: coverage=" + coverage.ToString("F2") +
                       " auto=" + auto +
                       " effective=" + brightness.ToString("F2") +
                       " overcastTerm=" + overcast.ToString("F2") +
                       (auto ? " (cloud skips the overcast term; the fog keeps it)" : ""));

            float mean = _frameCount > 0 ? _frameTime / _frameCount : 0f;

            Log.Detail("frame: " + (mean * 1000f).ToString("F1") + " ms mean, " +
                       (_frameWorst * 1000f).ToString("F1") + " ms worst, over " + _frameCount + " frames at " +
                       Screen.width + "x" + Screen.height +
                       " | steps=" + (Settings.CloudQuality != null ? Settings.CloudQuality.value : Settings.Defaults.Quality).ToString("F0") +
                       " style=" + (CloudStyle.Drawn ? "Cumulus" : "Classic") +
                       " detail=" + CloudDetail.Describe() +
                       " fragments=" + (CloudFragments.On ? (CloudFragments.Share * 100f).ToString("F0") + "%" : "off") +
                       " fog=" + (CloudFog.Active ? (CameraInFog() ? "INSIDE" : "on") : "off") +
                       " shadows=" + Settings.ShadowsCast +
                       " clouds=" + IsActive);
        }

        /// <summary>
        /// Whether the camera is down in the fog, which is where the fog march costs the most.
        /// An approximation: the height test, not the noise field.
        /// </summary>
        private bool CameraInFog()
        {
            if (!CloudFog.Active || _camera == null || !Singleton<TerrainManager>.exists)
                return false;

            float ground = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(_camera.transform.position);
            return CloudFog.InLayer(_camera.transform.position.y - ground, _camera.transform.position.y);
        }

        /// <summary>
        /// 0 by day, 1 once the sun is well down. From the sun's elevation rather than its
        /// intensity: the game keeps the sun bright until it is on the horizon, and the stars
        /// are out before it has faded.
        /// </summary>
        private static float NightFactor(Light sun)
        {
            if (sun == null)
                return 0f;

            float elevation = -Mathf.Asin(Mathf.Clamp(sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, -8f, elevation));
        }

        /// <summary>Clouds that hide the stars, and a faint pale glow on their undersides.</summary>
        private void ApplyNight(Light sun, Color shadeTint)
        {
            float night = NightFactor(sun);

            float opacity = Settings.CloudNightOpacity != null ? Mathf.Clamp01(Settings.CloudNightOpacity.value) : 1f;
            _material.SetFloat(IdNightOpacity, night * opacity);
            _material.SetFloat(IdCloudExtent, _holder.transform.localScale.x);

            // A small, even, pale luminance under the clouds. A night cloud is nearly black
            // (0.01-0.03 of radiance), so the strength is judged against THAT: at 100% the
            // underside is lifted by about as much again -- visible as form, not as a light.
            // Pale and slightly cool, not orange, and the same everywhere: the first version
            // projected a map of the city's buildings up in sodium orange, and an orange slab
            // shaped like the street plan looked "very weird" over a real city.
            // The shade colour tints it too: it lights the base, which is the shaded side, and
            // over a real city at night that glow is often orange -- the player's call.
            float glow = Settings.NightGlow != null ? Mathf.Max(0f, Settings.NightGlow.value) : 1f;
            Vector4 pale = new Vector4(0.8f * shadeTint.r, 0.86f * shadeTint.g, 0.96f * shadeTint.b, 0f);
            _material.SetVector(IdNightGlow, pale * (NightGlowStrength * glow * night));
        }

        /// <summary>A colour setting as the multiplier it is on the light (Sky.CloudTint).</summary>
        private static Color TintOf(ColorSetting setting, Color32 fallback)
        {
            return CloudTint.Of(setting != null ? setting.value : fallback);
        }

        /// <summary>
        /// The fog layer and the shadow map it is lit through. The sun's OWN transform is used
        /// for the lookup whatever is lighting the clouds: the shadow map is always rendered
        /// along the sun, and at night it is simply white.
        /// </summary>
        private void ApplyFog(Light sun, Color lightAmbient, Color baseSun)
        {
            // Off means nothing runs. The shader's guard is a uniform branch, so writing zero
            // here really is the whole cost of the feature when it is switched off -- no
            // keyword, no second variant, and TerrainHeightMap stands down with it.
            if (!CloudFog.Active)
            {
                _material.SetFloat(IdFogAmount, 0f);
                _material.SetFloat(IdFogDensity, 0f);
                _material.SetFloat(IdShadowAvailable, 0f);
                return;
            }

            float density = Settings.FogDensity != null ? Mathf.Max(0f, Settings.FogDensity.value) : Settings.Defaults.FogDensity;
            float height = CloudFog.Height;
            bool followsGround = CloudFog.FollowsGround;
            float breakup = Settings.FogBreakup != null ? Mathf.Clamp01(Settings.FogBreakup.value) : Settings.Defaults.FogBreakup;

            // The fog lies on the terrain map; until that exists there is nothing to lie on.
            // CloudFog.Active already says the map is ready, so this is belt and braces.
            Texture terrain = TerrainHeightMap.Texture;
            bool ready = terrain != null;

            _material.SetFloat(IdFogAmount, ready ? CloudFog.Amount : 0f);
            _material.SetFloat(IdFogDensity, CloudFog.BaseExtinction * density);

            // Where there is fog comes from the clouds' weather field, thresholded for the share
            // of the map it should cover. The threshold is solved against the field AS THE GPU
            // READS IT (gamma-decoded, measured), so "fog amount 30%" really is fog over about
            // a third of the map -- with the clouds' own table it covered a fraction of that,
            // which drove the slider to 100%, where fog is everywhere.
            _material.SetFloat(IdFogThreshold, _field.GetThresholdAsDrawn(CloudFog.Amount, CloudFog.CoverSoftness));
            _material.SetFloat(IdFogTile, CloudFog.Tile);
            _material.SetFloat(IdFogBoil, CloudFog.Boil);

            // The drift, as each lookup sees it (Drift.Phase): exact for the life of the city.
            _material.SetVector(IdFogPhase, CloudFog.Phase(1f, CloudFog.Tile));
            _material.SetVector(IdFogBillowTile, new Vector4(CloudFog.BillowTile, CloudFog.BillowTileVertical, CloudFog.BillowTile, 0f));
            _material.SetVector(IdFogBillowPhase, CloudFog.Phase(1f, CloudFog.BillowTile));
            _material.SetVector(IdFogLead, CloudFog.Lead);
            _material.SetFloat(IdFogSwirlTile, CloudFog.SwirlTile);
            _material.SetVector(IdFogSwirlPhase, CloudFog.Phase(CloudFog.SwirlDrift, CloudFog.SwirlTile));
            _material.SetFloat(IdFogWispTile, CloudFog.WispTile);
            _material.SetVector(IdFogWispPhase, CloudFog.Phase(CloudFog.WispDrift, CloudFog.WispTile));

            // Pooling is a ground-following fog's lean towards low ground. A level fog needs
            // none -- low ground is where it is deepest by construction -- and its reference is
            // one number, so a pooling level no ground can be under switches the term off
            // without the shader having to ask.
            _material.SetFloat(IdFogPool, followsGround ? TerrainHeightMap.PoolLevel : -100000f);
            _material.SetFloat(IdFogFloor, TerrainHeightMap.Lowest - 10f);
            _material.SetFloat(IdFogFollowGround, followsGround ? 1f : 0f);
            _material.SetFloat(IdFogLevel, TerrainHeightMap.SeaLevel);
            _material.SetFloat(IdFogBase, CloudFog.Base);
            _material.SetFloat(IdFogHeight, height);
            _material.SetFloat(IdFogBreakup, breakup * 0.8f);

            // Steps follow the Quality slider: the fog is the most expensive thing on screen
            // when the camera is inside it, and that slider is the lever for weaker GPUs.
            float quality = Settings.CloudQuality != null ? Settings.CloudQuality.value : Settings.Defaults.Quality;
            _material.SetFloat(IdFogSteps, Mathf.Clamp(quality * 0.3f, 8f, 32f));
            _material.SetFloat(IdFogMaxDistance, 9000f);

            if (terrain != null)
                _material.SetTexture(IdTerrainTex, terrain);
            _material.SetFloat(IdTerrainMapSize, TerrainHeightMap.MapSize);

            // Lit by the SAME light as the clouds, but through its own knob: the fog stands
            // under the same sky (so it keeps the overcast term, which is real) and is a
            // little brighter than a cloud base, because fog is lit from all round. What it no
            // longer takes is the clouds' brightness slider -- FogLightScale is the fixed
            // stand-in for the value everyone's fog was tuned under. Its own brightness and a
            // tint go on top: the light it is in still colours it (orange at sunset, blue at
            // night), and the tint leans that towards a cold blue-grey or a warm haze.
            float fogBrightness = Settings.FogBrightness != null ? Mathf.Max(0f, Settings.FogBrightness.value) : Settings.Defaults.FogBrightness;
            float tint = Settings.FogTint != null ? Mathf.Clamp(Settings.FogTint.value, -1f, 1f) : Settings.Defaults.FogTint;
            Color lean = tint < 0f
                ? Color.Lerp(Color.white, new Color(0.78f, 0.93f, 1.18f), -tint)
                : Color.Lerp(Color.white, new Color(1.18f, 1f, 0.76f), tint);
            lean *= fogBrightness;

            // The player's colour, as for the clouds (Sky.CloudTint): hue only, and white is
            // exactly (1, 1, 1), so a fog nobody coloured gets bit for bit the numbers it always
            // got. The lamps' glow inside the fog is added by the shader from its own map, in
            // the lamps' own colours, and is not touched.
            Color colour = CloudTint.Of(Settings.FogColor != null ? Settings.FogColor.value : Settings.Defaults.FogColor);
            lean = new Color(lean.r * colour.r, lean.g * colour.g, lean.b * colour.b, lean.a);

            Color fogAmbient = WithFloor(lightAmbient * FogLightScale);
            Color fogSun = baseSun * FogLightScale;

            _material.SetVector(IdFogAmbient, new Vector4(fogAmbient.r * lean.r, fogAmbient.g * lean.g, fogAmbient.b * lean.b, 0f) * 1.3f);
            _material.SetVector(IdFogSun, new Vector4(fogSun.r * lean.r, fogSun.g * lean.g, fogSun.b * lean.b, 0f) * 0.7f);

            CloudShadowMap map = CloudShadowMap.Current;
            bool shadows = Settings.ShadowsCast;
            bool available = shadows && sun != null && map != null && map.IsReady && map.Texture != null;

            _material.SetFloat(IdShadowAvailable, available ? 1f : 0f);
            if (available)
            {
                _material.SetTexture(IdCloudShadowTex, map.Texture);
                _material.SetVector(IdShadowOrigin, CloudShadowMap.Anchor);
                _material.SetVector(IdShadowRight, sun.transform.right);
                _material.SetVector(IdShadowUp, sun.transform.up);
                _material.SetFloat(IdShadowSize, CloudShadowMap.CookieSize);
                _material.SetFloat(IdShadowDarkness, CloudShadowMap.ShadowDepth(CloudShaderParams.Coverage));
            }
        }

        /// <summary>Unit cube; culling is off in the shader, so winding doesn't matter.</summary>
        private static Mesh BuildBox()
        {
            Mesh mesh = new Mesh { name = "VolumetricCloudsBox" };

            mesh.vertices = new[]
            {
                new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1),
                new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1),
            };

            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                3, 7, 6, 3, 6, 2,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5,
            };

            // Follows the camera, so it must never be frustum-culled.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);
            return mesh;
        }

        private void OnDestroy()
        {
            IsActive = false;

            if (_shadowMap != null)
            {
                _shadowMap.Dispose();
                _shadowMap = null;
            }

            if (_holder != null)
                Destroy(_holder);
            if (_mesh != null)
                Destroy(_mesh);
            if (_material != null)
                Destroy(_material);
            if (_noise != null)
                Destroy(_noise);

            if (_detail != null)
            {
                if (CloudDetail.Texture == _detail)
                    CloudDetail.Texture = null;
                Destroy(_detail);
            }

            if (_cumulus != null)
            {
                if (CloudStyle.Texture == _cumulus)
                    CloudStyle.Texture = null;
                Destroy(_cumulus);
            }

            // A worker still making one for this city: its result is for a city that is gone.
            _cumulusPending = null;
        }
    }
}
