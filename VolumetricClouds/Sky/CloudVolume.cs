using System;
using System.Text;
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

        /// <summary>
        /// From ABOVE the clouds the reach grows by this many metres per metre of height over their
        /// tops (the author, 2026-09-26: "From really high up in the sky, they disappear at a certain
        /// distance" -- from 4 km up every cloud is far along the ray, so the 30 km fade left only a
        /// near band). 4 km over 1.1 km tops: 103 km, a carpet to the horizon on the preview
        /// (render-detail.ps1 -Set reach). From below the tops it is MaxDistance exactly: the look
        /// from the city is untouched.
        /// </summary>
        private const float ReachPerMetreAbove = 25f;
        private const float MaxReach = 150000f;

        /// <summary>
        /// The cloud pass's own queue (the shader's Transparent+100): after everything see-through the
        /// game draws; the rain streaks (+110) come after it.
        /// </summary>
        public const int LateQueue = 3100;

        /// <summary>
        /// The clouds' queue while the camera is under their base (<see cref="UpdateParts"/>, 1.3.0):
        /// after the game's sky -- the aurora 2501, the painted clouds 2502, the stars 2503 (the
        /// shaders' Queue tags, read out of the game's asset files, and the log) -- and before
        /// everything see-through in the city: the halos 2990, decals and water 2999, the power lines
        /// 3000 ('Custom/Net/Electricity': alpha-blended, no depth), the particles 3000/3001 (which
        /// <see cref="GameParticles"/> moves after <see cref="AirQueue"/> since 1.4.0).
        /// </summary>
        public const int EarlyQueue = 2520;

        /// <summary>
        /// The air's own draw -- rain, fog, the rainbow's arch (1.4.0; until then <see cref="LateQueue"/>,
        /// and only under the base): after everything see-through the game draws, so power lines,
        /// halos and water are inside the fog, and a few queues before the clouds' late draw, so the
        /// game's particle effects fit in between (<see cref="GameParticles"/>: smoke over the fog,
        /// under the clouds above it). The lightning bolt goes before both (<see cref="CloudsQueue"/>).
        /// </summary>
        public const int AirQueue = LateQueue - 5;

        /// <summary>
        /// The clouds go early this far under their base and come back to the one draw within
        /// <see cref="SplitLeave"/> of it, so a camera hovering at the base does not flip them every
        /// frame. Anywhere under the base either way is right for the clouds and the air.
        /// </summary>
        private const float SplitEnter = 20f;
        private const float SplitLeave = 5f;

        /// <summary>True while the volumetric layer is actually drawing (the fog needs the pass).</summary>
        public static bool IsActive { get; private set; }

        /// <summary>
        /// The last frame's twilight: 0 by day, 1 once the sun is well down (NightFactor); 0
        /// without a city. What the Clouds tab's brightness line blends the night's in by.
        /// </summary>
        public static float Night { get; private set; }

        /// <summary>
        /// The queue the clouds are drawn at: <see cref="EarlyQueue"/> while the camera is under
        /// their base (<see cref="UpdateParts"/>), else <see cref="LateQueue"/>. The lightning bolt
        /// goes just before it (CloudLightning, as in 1.2: the clouds are laid over it), which is also
        /// before <see cref="AirQueue"/> from above the base.
        /// </summary>
        public static int CloudsQueue { get; private set; } = LateQueue;

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
        private bool _loggedSunCurve;

        // What the log last said about the light inside the clouds: which of its three rows were on
        // (bits; -1 = nothing said yet) and their values.
        private int _cloudLightOn = -1;
        private Vector3 _cloudLightLogged;

        // Sunset light (SunsetLight): the setting the log last named (-1 = none yet), and what it is
        // doing right now for the per-second sun line (null while the clouds have the game's sun).
        private float _sunsetLogged = -1f;
        private string _sunsetNote;

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

        // The raymarches' jitter (BlueNoise): made on the load's worker thread (once per session),
        // uploaded with the noise. Null: the shader keeps its white-noise hash. The error is
        // written by the worker before _noiseBytes (volatile), read after.
        private Texture2D _blueNoise;
        private string _blueNoiseError;

        // The rainbow's colours (RainbowTable): made on the load's worker thread (once per session),
        // uploaded with the noise. Null: no rainbows, and the log says why. The error is written by
        // the worker before _noiseBytes (volatile), read after.
        private Texture2D _rainbowTable;
        private string _rainbowError;

        // The game's camera, for where it looks (the rainbow's arch stands there).
        private CameraController _cameraController;
        private bool _lookedForController;

        // The air under the clouds (rain, fog, the rainbow's arch) as a draw of its own while the
        // camera is under the cloud base (UpdateParts): the same box and shader, _DrawPart 2, on a
        // material that gets the cloud material's values every frame it is drawn.
        private MeshRenderer _airRenderer;
        private Material _airMaterial;
        private bool _split;
        private int _partsLogged = -1;

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
        private static readonly int IdFogFadeStart = Shader.PropertyToID("_FogFadeStart");
        private static readonly int IdFogFadeLength = Shader.PropertyToID("_FogFadeLength");
        private static readonly int IdFogCeiling = Shader.PropertyToID("_FogCeiling");
        private static readonly int IdFogAmbient = Shader.PropertyToID("_FogAmbient");
        private static readonly int IdFogSun = Shader.PropertyToID("_FogSun");
        private static readonly int IdCloudShadowTex = Shader.PropertyToID("_CloudShadowTex");
        private static readonly int IdShadowAvailable = Shader.PropertyToID("_ShadowAvailable");
        private static readonly int IdShadowOrigin = Shader.PropertyToID("_ShadowOrigin");
        private static readonly int IdShadowRight = Shader.PropertyToID("_ShadowRight");
        private static readonly int IdShadowUp = Shader.PropertyToID("_ShadowUp");
        private static readonly int IdShadowSize = Shader.PropertyToID("_ShadowSize");
        private static readonly int IdShadowDarkness = Shader.PropertyToID("_ShadowDarkness");
        private static readonly int IdBlueNoiseTex = Shader.PropertyToID("_BlueNoiseTex");
        private static readonly int IdBlueNoiseScale = Shader.PropertyToID("_BlueNoiseScale");
        private static readonly int IdDetailLight = Shader.PropertyToID("_DetailLight");
        private static readonly int IdDetailLight2 = Shader.PropertyToID("_DetailLight2");
        private static readonly int IdCumulusLight = Shader.PropertyToID("_CumulusLight");
        private static readonly int IdLightThrough = Shader.PropertyToID("_LightThrough");
        private static readonly int IdShadeLight = Shader.PropertyToID("_ShadeLight");
        private static readonly int IdSunsetEdge = Shader.PropertyToID("_SunsetEdge");
        private static readonly int IdCumulusLobes = Shader.PropertyToID("_CumulusLobes");
        private static readonly int IdCumulusLobeG = Shader.PropertyToID("_CumulusLobeG");
        private static readonly int IdBowTex = Shader.PropertyToID("_BowTex");
        private static readonly int IdBowParams = Shader.PropertyToID("_BowParams");
        private static readonly int IdBowSun = Shader.PropertyToID("_BowSun");
        private static readonly int IdBowArch = Shader.PropertyToID("_BowArch");
        private static readonly int IdDrawPart = Shader.PropertyToID("_DrawPart");
        private static readonly int IdCloudBottom = Shader.PropertyToID("_CloudBottom");
        private static readonly int IdRainAmount = Shader.PropertyToID("_RainAmount");

        /// <summary>
        /// Extinction per metre inside full-strength rain, at 100% on the slider. Real
        /// downpours are several times denser, but a city builder has to stay readable from a
        /// kilometre up: at this value 1 km of heavy rain still passes about half the view.
        /// </summary>
        private const float RainExtinction = 0.0006f;
        private const float RainMaxDistance = 14000f;

        /// <summary>Snow's curtains are this much thicker than rain's at the same amount (winter maps, 1.3.0).</summary>
        private const float SnowExtinction = 1.5f;

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

        // Whether Real Light held the game's ambient apart from the city's when last looked at
        // (SceneAmbient): logged on every change, as it splits the clouds' light from the air's.
        private bool _ambientHeld;

        // Whether the clouds were brightened to stand in Real Light's sky when last looked at
        // (SceneAmbient.CloudLight): logged on every change.
        private bool _realLightSkyLogged;

        /// <summary>How far the night colours are in, 0..1 (0 whenever they are off); for the detail line.</summary>
        private float _nightColourShare;

        private static readonly int IdSkyGlow = Shader.PropertyToID("_SkyGlow");
        private static readonly int IdSkyGlowMode = Shader.PropertyToID("_SkyGlowMode");

        /// <summary>The game's sky as the clouds read it (ApplySkyGlow), for the log.</summary>
        private readonly SkyGlow _skyGlow = new SkyGlow();

        /// <summary>What ApplySkyGlow last logged: -1 never, 0 no game sky, else 1 + 1 per HDR_ON (global, material x2).</summary>
        private int _skyGlowLogged = -1;
        private float _nextSkyGlowDetail;

        /// <summary>The game's skybox shader (DayNightProperties.skyboxMaterial), whose maths SkyGlow copies.</summary>
        private const string GameSkyboxShader = "Hidden/DayNight/Skybox";

        /// <summary>Where the "sun glow" log line samples the sky, degrees from the sun along its height.</summary>
        private static readonly int[] SkyGlowLogAngles = { 0, 5, 10, 20, 30, 45, 60, 90 };

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

            _material.renderQueue = LateQueue;
            _renderer = _holder.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.enabled = false;

            // The air under the clouds, drawn apart whenever there is any (UpdateParts). A child,
            // so it follows the box.
            _airMaterial = new Material(shader) { name = "VolumetricCloudsAir" };
            _airMaterial.renderQueue = AirQueue;
            GameObject air = new GameObject("VolumetricCloudsAir");
            air.transform.SetParent(_holder.transform, false);
            air.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _airRenderer = air.AddComponent<MeshRenderer>();
            _airRenderer.sharedMaterial = _airMaterial;
            _airRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _airRenderer.receiveShadows = false;
            _airRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _airRenderer.enabled = false;

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

                // The raymarches' jitter tile: made the first time, kept for the session.
                try
                {
                    if (BlueNoise.Bytes == null)
                        _blueNoiseError = "no tile";
                }
                catch (Exception e)
                {
                    _blueNoiseError = e.GetType().Name + ": " + e.Message;
                }

                // The rainbow's colours: made the first time, kept for the session.
                try
                {
                    if (RainbowTable.Bytes == null)
                        _rainbowError = "no table";
                }
                catch (Exception e)
                {
                    _rainbowError = e.GetType().Name + ": " + e.Message;
                }

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
            {
                _airRenderer.enabled = false;
                return;
            }

            // Keep the box around the camera and inside the far plane, corners included.
            _holder.transform.position = _camera.transform.position;
            _holder.transform.localScale = Vector3.one * (_camera.farClipPlane * 0.45f);

            UpdateMaterial();
            UpdateParts();
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

            UploadBlueNoise();
            UploadRainbowTable();

            // Made with it when the style was Cumulus at load: in the same frame.
            UpdateCumulus();

            Log.Msg("3D noise uploaded; volumetric clouds live.");
            return true;
        }

        /// <summary>
        /// The raymarches' jitter tile (BlueNoise), made by the worker: ARGB32, LINEAR, the value in
        /// every channel (the shader reads alpha), point-sampled, repeating -- the score texture's
        /// format. Without it the shader keeps its white-noise hash, and the log says why.
        /// </summary>
        private void UploadBlueNoise()
        {
            if (_blueNoiseError != null)
            {
                Log.Warn("jitter: the blue-noise tile could not be made (" + _blueNoiseError +
                         "); the clouds, rain and fog keep the white-noise hash");
                return;
            }

            try
            {
                byte[] bytes = BlueNoise.Bytes;
                Color32[] pixels = new Color32[bytes.Length];
                for (int i = 0; i < bytes.Length; i++)
                {
                    byte v = bytes[i];
                    pixels[i] = new Color32(v, v, v, v);
                }

                _blueNoise = new Texture2D(BlueNoise.Size, BlueNoise.Size, TextureFormat.ARGB32, false, true)
                {
                    name = "VolumetricCloudsBlueNoise",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Point,
                    anisoLevel = 0,
                };
                _blueNoise.SetPixels32(pixels);
                _blueNoise.Apply(false);
            }
            catch (Exception e)
            {
                if (_blueNoise != null)
                    Destroy(_blueNoise);
                _blueNoise = null;
                Log.Warn("jitter: the blue-noise tile could not be uploaded (" + e.GetType().Name + ": " + e.Message +
                         "); the clouds, rain and fog keep the white-noise hash");
                return;
            }

            Log.Msg("jitter: blue noise " + BlueNoise.Size + "^2 for the clouds', rain's and fog's steps (made in " +
                    BlueNoise.Milliseconds + " ms, once per session)");
        }

        /// <summary>
        /// The rainbow's colours (RainbowTable), made by the worker: ARGB32, LINEAR, one row per
        /// colour with the value in every channel (the shader reads alpha), filtered along the angle,
        /// clamped at the ends -- the blue noise's format, so no engine call the mod has not made.
        /// Without it there are no rainbows, and the log says why.
        /// </summary>
        private void UploadRainbowTable()
        {
            if (_rainbowError != null)
            {
                Log.Warn("rainbow: its colour table could not be made (" + _rainbowError + "); no rainbows");
                return;
            }

            try
            {
                byte[] bytes = RainbowTable.Bytes;
                Color32[] pixels = new Color32[bytes.Length];
                for (int i = 0; i < bytes.Length; i++)
                {
                    byte v = bytes[i];
                    pixels[i] = new Color32(v, v, v, v);
                }

                _rainbowTable = new Texture2D(RainbowTable.Size, RainbowTable.Rows, TextureFormat.ARGB32, false, true)
                {
                    name = "VolumetricCloudsRainbow",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 0,
                };
                _rainbowTable.SetPixels32(pixels);
                _rainbowTable.Apply(false);
            }
            catch (Exception e)
            {
                if (_rainbowTable != null)
                    Destroy(_rainbowTable);
                _rainbowTable = null;
                Log.Warn("rainbow: its colour table could not be uploaded (" + e.GetType().Name + ": " + e.Message + "); no rainbows");
                return;
            }

            Log.Msg("rainbow: colour table " + RainbowTable.Size + "x" + RainbowTable.Rows + " over " + RainbowTable.MaxAngle +
                    " deg round the antisolar point, 255 = " + RainbowTable.Scale.ToString("F3") + " (made in " +
                    RainbowTable.Milliseconds + " ms, once per session)");
        }

        /// <summary>
        /// How far along a ray the clouds reach -- where the distance fade ends and the march stops:
        /// <see cref="MaxDistance"/> from below the tops, more from above them (<see cref="ReachPerMetreAbove"/>).
        /// </summary>
        private float Reach
        {
            get
            {
                if (_camera == null)
                    return MaxDistance;

                float bottom = Settings.CloudAltitude != null ? Settings.CloudAltitude.value : Settings.Defaults.Altitude;
                float above = _camera.transform.position.y - (bottom + CloudShaderParams.LayerHeight);
                return Mathf.Min(MaxReach, MaxDistance + ReachPerMetreAbove * Mathf.Max(0f, above));
            }
        }

        /// <summary>
        /// The raymarch's step count: the Quality setting, times <see cref="CloudStyle.StepFactor"/>
        /// while Cumulus is drawn (its slab is ~2.8x Classic's height).
        /// </summary>
        private static float MarchSteps
        {
            get
            {
                float steps = Settings.CloudQuality != null ? Settings.CloudQuality.value : Settings.Defaults.Quality;
                return CloudStyle.Drawn ? steps * CloudStyle.StepFactor : steps;
            }
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
            bool useDepth = Settings.CloudDepthOcclusion == null || Settings.CloudDepthOcclusion.value;

            CloudShaderParams.Apply(_material, _field, _noise);
            _material.SetFloat(IdSteps, MarchSteps);

            // The jitter tile, or 0: the shader's white-noise hash.
            _material.SetTexture(IdBlueNoiseTex, _blueNoise);
            _material.SetFloat(IdBlueNoiseScale, _blueNoise != null ? 1f / BlueNoise.Size : 0f);
            _material.SetFloat(IdMaxDistance, Reach);
            _material.SetFloat(IdUseDepth, useDepth ? 1f : 0f);

            ApplyLighting();
            ApplyDetailLight();
            CloudLightning.ApplyTo(_material);
        }

        /// <summary>
        /// Where the pass goes in the frame (1.3.0; the author: the clouds "blend" with the power
        /// lines). The game draws its power lines, halos, smoke and particles see-through, without
        /// depth, BEFORE the cloud pass, which stops only at depth: the whole cloud behind a wire was
        /// painted over it. From under the cloud base the city is under the clouds, so there the
        /// clouds are drawn on their own at <see cref="EarlyQueue"/>, before all of it, and the air
        /// under them -- rain, fog, the rainbow's arch, which those objects DO stand in -- is a second
        /// draw after them (<see cref="AirQueue"/>), only while there is any. The two blends make
        /// exactly the one pass's Over(lower, clouds). The price: anything see-through ABOVE the base
        /// -- a lamp or a wire on a hill that reaches into the clouds, a plane's lights over them --
        /// is then drawn over the cloud in front of it, as the wires were under the clouds behind
        /// them.
        /// The air is a draw of its own from ABOVE the base too since 1.4.0 (reported: the fog
        /// "is making smoke (from industrial factories) disappear"): at <see cref="AirQueue"/>, with
        /// the game's particles moved after it (<see cref="GameParticles"/>) and the clouds after
        /// those at <see cref="LateQueue"/> -- from above, the clouds are in front of all the air,
        /// so the two blends are exactly the one pass's Over(clouds, lower). In the few metres under
        /// the base where the clouds have not gone early yet (<see cref="SplitEnter"/>) this order
        /// is kept, so the air between the camera and the base is drawn under the clouds on rays
        /// that go up. Without rain, fog or an arch, the one draw as before.
        /// </summary>
        private void UpdateParts()
        {
            // What the shader compares the camera with: the value it was given.
            float bottom = _material.GetFloat(IdCloudBottom);
            float height = _camera.transform.position.y;
            _split = height < bottom - (_split ? SplitLeave : SplitEnter);

            // The shader's own tests for its rain, fog and arch, a hair lower, so a rounding can never
            // leave any of them undrawn.
            bool air = (_material.GetFloat(IdRainAmount) > 0.0009f && _material.GetFloat(IdRainShaftDensity) > 0f) ||
                       (_material.GetFloat(IdFogAmount) > 0.0009f && _material.GetFloat(IdFogDensity) > 0f) ||
                       _material.GetVector(IdBowParams).z > 0.4f;

            _material.renderQueue = _split ? EarlyQueue : LateQueue;
            _material.SetFloat(IdDrawPart, _split || air ? 1f : 0f);
            CloudsQueue = _material.renderQueue;

            if (air)
            {
                // Every value the cloud pass has, then the textures one by one (Unity 5.6 copies only
                // those in the shader's Properties block; see CopyValues) and the lightning's
                // arrays. CopyValues keeps this material's own queue, AirQueue: a plain copy takes
                // the clouds' queue with the values (1.3.0 did, and the power lines, the water
                // and the halos were drawn over the fog).
                CopyValues(_material, _airMaterial);
                CloudShaderParams.Apply(_airMaterial, _field, _noise);
                _airMaterial.SetTexture(IdBlueNoiseTex, _blueNoise);

                Texture terrain = TerrainHeightMap.Texture;
                if (terrain != null)
                    _airMaterial.SetTexture(IdTerrainTex, terrain);
                CloudShadowMap map = CloudShadowMap.Current;
                if (map != null && map.Texture != null)
                    _airMaterial.SetTexture(IdCloudShadowTex, map.Texture);
                if (_rainbowTable != null)
                    _airMaterial.SetTexture(IdBowTex, _rainbowTable);

                CloudLightning.ApplyTo(_airMaterial);
                _airMaterial.SetFloat(IdDrawPart, 2f);
            }

            _airRenderer.enabled = air;

            int state = (_split ? 1 : 0) + (air ? 2 : 0);
            if (state != _partsLogged)
            {
                _partsLogged = state;
                // The queues as the materials hold them, read back: the constants are only what was
                // asked for (in 1.3.0 this line said 3100 while the air was really at 2520).
                string where = " (camera " + height.ToString("F0") + " m, cloud base " + bottom.ToString("F0") + " m)";
                string airAt = "rain, fog and rainbow a draw of their own at " + _airMaterial.renderQueue +
                               ", after the power lines and halos, before the particles (smoke)";
                switch (state)
                {
                    case 0:
                        Log.Msg("cloud pass: one draw at queue " + _material.renderQueue + ", after the see-through objects; no rain, fog or rainbow" + where);
                        break;
                    case 1:
                        Log.Msg("cloud pass: clouds at queue " + _material.renderQueue + ", before the city's see-through objects (power lines, halos, smoke); no rain, fog or rainbow to draw after them" + where);
                        break;
                    case 2:
                        Log.Msg("cloud pass: " + airAt + "; the clouds over all of it at " + _material.renderQueue + where);
                        break;
                    default:
                        Log.Msg("cloud pass: clouds at queue " + _material.renderQueue + ", before the city's see-through objects; " + airAt + where);
                        break;
                }
            }
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
            // Eased by "Sky light in the shade" (CloudShade; 0%: as before).
            float occlusion = (cumulus ? CloudStyle.AmbientOcclusion : CloudDetail.AmbientOcclusion) *
                              CloudShade.OcclusionScaleFor(CloudShade.SkySetting);

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

            // Sunlight through the clouds (LightThrough), on both lights above; 0 = off, the shader's
            // branch never taken.
            float setting = LightThrough.Setting;
            _material.SetVector(IdLightThrough, new Vector4(LightThrough.AmountFor(setting), LightThrough.KFor(setting), 0f, 0f));

            // The light inside the clouds, its three parts (one slider since 1.4.0's merge): on the log
            // at the start and whenever it is switched on or off (0%), every other change on the
            // detailed log.
            float sky = CloudShade.SkySetting;
            float ground = CloudShade.GroundSetting;
            int on = (setting > 0f ? 1 : 0) | (sky > 0f ? 2 : 0) | (ground > 0f ? 4 : 0);
            if (on != _cloudLightOn || setting != _cloudLightLogged.x || sky != _cloudLightLogged.y || ground != _cloudLightLogged.z)
            {
                string text = "light inside the clouds: sunlight through " + LightThrough.Describe(setting) + "; " + CloudShade.Describe(sky, ground);
                if (on != _cloudLightOn)
                    Log.Msg(text);
                else if (Log.Detailed)
                    Log.Detail(text);

                _cloudLightOn = on;
                _cloudLightLogged = new Vector3(setting, sky, ground);
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

            // 0 by day, 1 at night: what brings in the night's brightness (with "Brightness differs
            // at night", 1.3.0) and its colours below, through one twilight.
            float night = NightFactor(sun);
            Night = night;
            float sunElevation = SunElevation(sun);

            float coverage = CloudShaderParams.Coverage;
            bool auto = Settings.BrightnessAuto;
            float brightness = Settings.EffectiveBrightness(coverage, night);
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

            Color ambient = SceneAmbient.Sky();
            if (linear)
                ambient = ambient.linear;

            Color lightAmbient = ambient * 0.9f;
            Color baseSun = lightSun * overcast;

            // The air under the clouds -- rain curtains and fog -- in the light the CITY is lit with
            // (SceneAmbient.Air): seen against the city, it has to stand in the same light, or it
            // vanishes into a brighter city. The same colour as the clouds' unless Real Light holds
            // the game's apart, so without it this is exactly lightAmbient.
            Color airAmbient = SceneAmbient.Air();
            if (linear)
                airAmbient = airAmbient.linear;

            Color airLightAmbient = airAmbient * 0.9f;

            bool held = SceneAmbient.IsHeld;
            if (held != _ambientHeld)
            {
                _ambientHeld = held;
                Log.Msg(held
                    ? "lighting: Real Light holds the game's ambient apart from the city's -- the clouds take the game's " + lightAmbient +
                      ", the rain curtains, rain streaks and fog the city's " + airLightAmbient
                    : "lighting: Real Light no longer holds the game's ambient apart -- clouds, rain and fog take the city's " + airLightAmbient);
            }

            // Sunset light (SunsetLight): the CLOUDS' sun only -- the fog is down in the dusk with the
            // city and keeps the game's (baseSun above), as do the ground and the shadows.
            Color cloudLightSun = SunsetSun(properties, sun, key, lightSun, sunElevation, linear);

            // Under Real Light's sky, as bright as that sky's sun (SceneAmbient.CloudLight; 1 without it).
            float underRealLight = SceneAmbient.CloudLight;
            bool brightened = underRealLight > 1.001f;
            if (brightened != _realLightSkyLogged)
            {
                _realLightSkyLogged = brightened;
                Log.Msg(brightened
                    ? "lighting: Real Light's sky is drawn -- the clouds stand in its sun, x" + underRealLight.ToString("F2") + " the light they take from the game's"
                    : "lighting: Real Light's sky is not drawn -- the clouds take the game's light as it is");
            }

            Color cloudSun = (auto ? cloudLightSun : cloudLightSun * overcast) * (brightness * underRealLight);
            Color cloudAmbient = WithFloor(lightAmbient * brightness) * underRealLight;

            // The player's grading, on the CLOUD's two lights only: the sunlit side and the
            // shaded side. A tint changes the hue, never the brightness, and white is exactly
            // (1, 1, 1) -- the shader gets bit for bit what it got before colours existed.
            // With "Different colours at night" (1.3.0) the night's two take over as the sun goes
            // down, through the same twilight as the night's opacity and glow; off, the day's
            // are used around the clock, exactly as before the switch.
            _nightColourShare = NightColours ? night : 0f;
            Color sunlitTint = CloudTint.Blend(ColourOf(Settings.CloudSunlitColor, Settings.Defaults.SunlitColor),
                ColourOf(Settings.CloudMoonlitColor, Settings.Defaults.MoonlitColor), _nightColourShare);
            Color shadeTint = CloudTint.Blend(ColourOf(Settings.CloudShadeColor, Settings.Defaults.ShadeColor),
                ColourOf(Settings.CloudNightShadeColor, Settings.Defaults.NightShadeColor), _nightColourShare);

            _material.SetVector(IdSunDir, sunDir.normalized);
            _material.SetVector(IdSunColor, AsVector(cloudSun * sunlitTint));
            _material.SetVector(IdAmbientColor, AsVector(cloudAmbient * shadeTint));

            // The light in the clouds' shade (CloudShade): the sky light at the base as a share of the
            // top's, and the sunlit ground's light on the undersides as a share of the sun above (the
            // key's: moonlit ground at night). Both at 0%: (0.45, 0), the light from before.
            _material.SetVector(IdShadeLight, new Vector4(
                CloudShade.SkyFloorFor(CloudShade.SkySetting),
                CloudShade.GroundFor(CloudShade.GroundSetting, coverage, sunDir.normalized.y), 0f, 0f));

            // Rain curtains hang from the clouds and are drawn in the same pass, so they keep
            // the CLOUD's two colours: one picture, one light. (A cloud override at 20% with
            // another mod's rain at full is the one corner where that reads bright.) The cloud's
            // light, NOT its grading: rain shafts under pink clouds would read as a bug. Its
            // ambient is the AIR's, though (the same as the cloud's unless Real Light holds the
            // game's apart): lit by the game's while the city behind is lit ~20x brighter, the
            // curtains disappeared (2026-10-09).
            // Snow (winter maps, 1.3.0) hangs whiter and a little thicker than rain: flakes scatter
            // far more of the light they are in, and a snowfall takes the view sooner than rain.
            bool snow = CloudRain.IsSnow;
            float curtains = Settings.RainCurtains != null ? Mathf.Max(0f, Settings.RainCurtains.value) : 1f;
            _material.SetFloat(IdRainShaftDensity, CloudRain.Active ? RainExtinction * curtains * (snow ? SnowExtinction : 1f) : 0f);
            _material.SetFloat(IdRainSteps, 20f);
            _material.SetFloat(IdRainMaxDistance, RainMaxDistance);
            _material.SetFloat(IdRainFloor, 0f);
            _material.SetFloat(IdRainFall, -CloudRain.FallOffset.y);
            _material.SetFloat(IdRainCurtainScale, CloudRain.CurtainScale);
            Color rainAmbient = WithFloor(airLightAmbient * brightness);
            _material.SetVector(IdRainAmbient, AsVector(rainAmbient) * (snow ? 1f : 0.6f));
            _material.SetVector(IdRainSun, AsVector(cloudSun) * (snow ? 0.3f : 0.12f));

            ApplyNight(night, shadeTint);
            ApplySkyGlow(properties);
            ApplyFog(airLightAmbient, baseSun);

            // The cloud shadow map, as the air under the clouds reads it: the fog's sun shafts and
            // the rainbow's "is this drop in the sun" (1.3.0; until then only the fog's, so it was
            // set only while the fog was on). Uploaded whenever either wants it -- and never at
            // "Shadow darkness" 0%, where the map is white and says nothing: the fog is then lit as
            // with the shadows off (until 1.3.0 it had no sun at all), and the rainbow says why
            // there is none.
            CloudShadowMap map = CloudShadowMap.Current;
            bool shadowMap = Settings.ShadowsCast && sun != null && map != null && map.IsReady && map.Texture != null;
            bool bow = ApplyRainbow(sun, key, cloudSun, shadowMap, sunElevation);
            ApplyShadowLookup(sun, map, shadowMap && CloudShadowMap.Readable(CloudShaderParams.Coverage) && (CloudFog.Active || bow));

            if (!_loggedLighting)
            {
                _loggedLighting = true;
                Log.Msg("cloud lighting key='" + (key == null ? "none" : key.name) + "' intensity=" +
                        (key == null ? 0f : key.intensity) + " sunColor=" + cloudSun +
                        " ambientMode=" + RenderSettings.ambientMode + " ambient=" + cloudAmbient +
                        (properties != null
                            ? " | sky: mie=" + properties.m_MieScattering + " sunGlow g=" + properties.m_SunAnisotropyFactor +
                              " sunSize=" + properties.m_SunSize
                            : ""));
            }

            if (!_loggedSunCurve && properties != null)
            {
                _loggedSunCurve = true;
                try
                {
                    Log.Msg(DescribeSunCurve(properties));
                }
                catch (Exception e)
                {
                    Log.Msg("sun curve: could not be read (" + e.GetType().Name + ": " + e.Message + ")");
                }
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
                        " | rain = the cloud's" +
                        (SceneAmbient.IsHeld
                            ? " | Real Light holds the game's ambient apart: the clouds take it, the rain and the fog the city's, airAmbient=" + airLightAmbient
                            : ""));
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
                {
                    LogDetail(coverage, auto, brightness, overcast);
                    LogSun(properties, sun, key, cloudSun, _sunsetNote);
                }

                _frameCount = 0;
                _frameTime = 0f;
                _frameWorst = 0f;
            }
        }

        /// <summary>
        /// The clouds' sun with "Sunset light" (SunsetLight): below the hold elevation, the game's own
        /// light at that elevation, redder and fading as the sun sinks, never below the game's light;
        /// and the Earth's shadow per height for the shader (_SunsetEdge). The game's sun unchanged
        /// above it, at 0%, and while the moon is the key light.
        /// </summary>
        private Color SunsetSun(DayNightProperties properties, Light sun, Light key, Color lightSun, float elevation, bool linear)
        {
            float setting = SunsetLight.Setting;
            float amount = SunsetLight.AmountFor(setting);
            if (amount <= 0f || properties == null || sun == null || key != sun || elevation >= SunsetLight.HoldElevation ||
                properties.m_LightColor == null)
            {
                _material.SetVector(IdSunsetEdge, Vector4.zero);
                _sunsetNote = null;
                LogSunset(setting);
                return lightSun;
            }

            // The hold light: the game's at the hold elevation (DayTime 1 there), its gradient read at
            // the time of day the sun stood there this evening (or morning).
            float holdTime = SunsetLight.TimeAt(SunsetLight.HoldElevation, properties.m_Latitude, properties.m_TimeOfDay > 12f);
            Color hold = properties.m_LightColor.Evaluate(holdTime / 24f);
            if (linear)
                hold = hold.linear;
            hold *= properties.m_Exposure * properties.m_SunIntensity * 0.25f;

            // The colour at the layer's middle; the Earth's shadow per height is the shader's.
            float bottom = Settings.CloudAltitude != null ? Settings.CloudAltitude.value : Settings.Defaults.Altitude;
            float thickness = Settings.CloudThickness != null ? Settings.CloudThickness.value : Settings.Defaults.Thickness;
            float r, g, b;
            SunsetLight.Share(elevation, bottom + 0.5f * thickness, setting, out r, out g, out b);

            Color held = new Color(Mathf.Max(lightSun.r, hold.r * r), Mathf.Max(lightSun.g, hold.g * g), Mathf.Max(lightSun.b, hold.b * b), lightSun.a);
            Color result = lightSun + (held - lightSun) * amount;

            // The edge over the height above y = 0 (sea level is a few tens of metres: a hundredth of
            // a degree). x + y sqrt(height) is the edge's 0..1, smoothed in the shader; w = how much of
            // it is used (the amount).
            float width = SunsetLight.EdgeFull - SunsetLight.EdgeGone;
            _material.SetVector(IdSunsetEdge, new Vector4(
                (elevation - SunsetLight.EdgeGone) / width,
                SunsetLight.DipPerRootMetre * SunsetLight.StretchFor(setting) / width,
                0f, amount));

            // For this second's sun line only (it is written further down this frame): no string a frame.
            _sunsetNote = Log.Detailed && Time.time >= _nextDetailTime
                ? "sunset light: hold " + Rgb(hold) + " at " + holdTime.ToString("F2") + "h, share (" +
                  r.ToString("F2") + "," + g.ToString("F2") + "," + b.ToString("F2") + "), clouds' sun " + Rgb(result) +
                  " (the game's " + Rgb(lightSun) + "), sun up for: base " +
                  (SunsetLight.Edge(elevation, bottom, setting) * 100f).ToString("F0") + "% top " +
                  (SunsetLight.Edge(elevation, bottom + thickness, setting) * 100f).ToString("F0") + "%"
                : null;
            LogSunset(setting);
            return result;
        }

        /// <summary>The "sunset light" line: on the log whenever it is switched on or off, other changes on the detailed log.</summary>
        private void LogSunset(float setting)
        {
            if (setting == _sunsetLogged)
                return;

            string text = "sunset light: " + SunsetLight.Describe(setting);
            if (_sunsetLogged < 0f || (setting > 0f) != (_sunsetLogged > 0f))
                Log.Msg(text);
            else if (Log.Detailed)
                Log.Detail(text);

            _sunsetLogged = setting;
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
                       (auto ? " (cloud skips the overcast term; the fog keeps it)" : "") +
                       (Settings.NightBrightnessOn ? " | night brightness " + (Night * 100f).ToString("F0") + "% in" : "") +
                       (NightColours ? " | night colours " + (_nightColourShare * 100f).ToString("F0") + "% in" : ""));

            float mean = _frameCount > 0 ? _frameTime / _frameCount : 0f;

            Log.Detail("frame: " + (mean * 1000f).ToString("F1") + " ms mean, " +
                       (_frameWorst * 1000f).ToString("F1") + " ms worst, over " + _frameCount + " frames at " +
                       Screen.width + "x" + Screen.height +
                       " | steps=" + MarchSteps.ToString("F0") +
                       " reach=" + (Reach / 1000f).ToString("F0") + "km" +
                       " style=" + (CloudStyle.Drawn ? "Cumulus(layer " + (CloudStyle.LayerAmount * 100f).ToString("F0") + "%)" : "Classic") +
                       " detail=" + CloudDetail.Describe() +
                       " fragments=" + (CloudFragments.On ? (CloudFragments.Share * 100f).ToString("F0") + "%" : "off") +
                       " fog=" + (CloudFog.Active ? (CameraInFog() ? "INSIDE" : "on") : "off") +
                       " rainbow=" + (Rainbow.Current > 0f ? (Rainbow.Current * 100f).ToString("F0") + "% " + Rainbow.DescribeArch() : "none") +
                       " shadows=" + Settings.ShadowsCast +
                       " clouds=" + IsActive);
        }

        /// <summary>
        /// The game's sun over the day, once per city: the colour gradient its light is coloured by
        /// (the moon's too, both times the game's day/night factor) and what its intensity is made
        /// of. With the latitude and longitude that place it, the clouds' light at every sun height
        /// can be worked out from this line alone.
        /// </summary>
        private static string DescribeSunCurve(DayNightProperties properties)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder("sun curve: light colour");
            Gradient gradient = properties.m_LightColor;
            if (gradient == null)
            {
                text.Append(" none");
            }
            else
            {
                GradientColorKey[] keys = gradient.colorKeys;
                for (int k = 0; k < keys.Length; k++)
                    text.Append(' ').Append((keys[k].time * 24f).ToString("F2")).Append("h=").Append(Rgb(keys[k].color));

                GradientAlphaKey[] alpha = gradient.alphaKeys;
                text.Append(" alpha");
                for (int k = 0; k < alpha.Length; k++)
                    text.Append(' ').Append((alpha[k].time * 24f).ToString("F2")).Append("h=").Append(alpha[k].alpha.ToString("F3"));
            }

            text.Append(" | sunIntensity=").Append(properties.m_SunIntensity.ToString("F3"))
                .Append(" moonIntensity=").Append(properties.m_MoonIntensity.ToString("F3"))
                .Append(" exposure=").Append(properties.m_Exposure.ToString("F3"))
                .Append(" rayleigh=").Append(properties.m_RayleighScattering.ToString("F3"))
                .Append(" latitude=").Append(properties.m_Latitude.ToString("F2"))
                .Append(" longitude=").Append(properties.m_Longitude.ToString("F2"));
            return text.ToString();
        }

        /// <summary>
        /// The once-a-second sun line: how high it is, the game's day/night factor, which light the
        /// clouds are lit by and what reaches them, and the sky's three ambient colours. What the
        /// clouds' light does through a sunset, read straight off the log.
        /// </summary>
        private static void LogSun(DayNightProperties properties, Light sun, Light key, Color cloudSun, string sunsetNote)
        {
            if (sun == null)
                return;

            Light moon = properties != null && properties.m_MoonLight != null ? properties.m_MoonLight.GetComponent<Light>() : null;
            Log.Detail("sun: elevation=" + SunElevation(sun).ToString("F1") + "deg" +
                       (properties != null
                           ? " time=" + properties.m_TimeOfDay.ToString("F2") + "h dayTime=" + properties.DayTime.ToString("F3")
                           : "") +
                       " sun=" + sun.intensity.ToString("F3") + "x" + Rgb(sun.color) +
                       " moon=" + (moon != null ? moon.intensity.ToString("F3") + "x" + Rgb(moon.color) : "none") +
                       " key=" + (key == null ? "none" : key == sun ? "sun" : "moon") +
                       " cloudSun=" + Rgb(cloudSun) +
                       " underRealLight=x" + SceneAmbient.CloudLight.ToString("F2") +
                       " | ambient " + RenderSettings.ambientMode +
                       " sky=" + Rgb(RenderSettings.ambientSkyColor) +
                       " equator=" + Rgb(RenderSettings.ambientEquatorColor) +
                       " ground=" + Rgb(RenderSettings.ambientGroundColor) +
                       (sunsetNote != null ? " | " + sunsetNote : ""));
        }

        private static string Rgb(Color colour)
        {
            return "(" + colour.r.ToString("F3") + "," + colour.g.ToString("F3") + "," + colour.b.ToString("F3") + ")";
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

            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, -8f, SunElevation(sun)));
        }

        /// <summary>The sun's height over the horizon, degrees; -90 without a sun.</summary>
        private static float SunElevation(Light sun)
        {
            return sun != null ? -Mathf.Asin(Mathf.Clamp(sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg : -90f;
        }

        /// <summary>Clouds that hide the stars, and a faint pale glow on their undersides.</summary>
        private void ApplyNight(float night, Color shadeTint)
        {
            float opacity = Settings.CloudNightOpacity != null ? Mathf.Clamp01(Settings.CloudNightOpacity.value) : 1f;
            _material.SetFloat(IdNightOpacity, night * opacity);

            // Where the night's opaque clouds end softly: where the march ends (1.3.0). It was the
            // edge of the box they are drawn on, a third of that (~10 km from the city), and past
            // it the lowest few degrees of sky showed through the clouds -- with the game fog's
            // horizon band on it: "it hides far away clouds" (GameHorizon takes the band off).
            _material.SetFloat(IdCloudExtent, Reach);

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

        /// <summary>
        /// The far clouds block the sun (CloudMedia.cginc SkyGlowPass, SkyGlow): what they let
        /// through as they fade into the distance is the game's sky without the sun's disc and glow.
        /// </summary>
        /// <remarks>
        /// The author: "like in real life: the clouds should block the sun". 1.2.1 to 1.3.x did it
        /// with a cone round the sun where the far clouds stayed solid; it showed as a disc, "a huge
        /// ring" round the sun (2026-10-06), worst at a low brightness. The shader reads the sky's
        /// own inputs, which DayNightProperties sets globally each frame (Render It! and Theme
        /// Mixer change them there, so they are followed), and only needs the sun and the sun's size
        /// (a skybox material value). With another
        /// skybox shader in place the maths would be someone else's: the clouds then fade into
        /// that sky as they did before 1.2.1.
        /// </remarks>
        private void ApplySkyGlow(DayNightProperties properties)
        {
            Material skybox = RenderSettings.skybox;
            bool gameSky = properties != null && properties.m_SunLight != null && skybox != null &&
                           skybox.shader != null && skybox.shader.name == GameSkyboxShader;
            if (!gameSky)
            {
                _material.SetVector(IdSkyGlowMode, Vector4.zero);
                if (_skyGlowLogged != 0)
                {
                    _skyGlowLogged = 0;
                    Log.Msg("sun glow: the sky is not the game's skybox ('" + (skybox != null && skybox.shader != null ? skybox.shader.name : "none") +
                            "') -- the far clouds fade into it unchanged, the sun's glow included");
                }
                return;
            }

            Vector3 sunDir = -properties.m_SunLight.forward;
            float sunSize = skybox.HasProperty("_SunSize") ? skybox.GetFloat("_SunSize") : 32f / Mathf.Max(properties.m_SunSize, 1e-3f);
            _material.SetVector(IdSkyGlow, new Vector4(sunDir.x, sunDir.y, sunDir.z, sunSize));
            _material.SetVector(IdSkyGlowMode, new Vector4(0f, 1f, 0f, 0f));

            // What the shader computes, worked out here with the same inputs: on the log when it
            // starts or the sky's variant switch changes, every 10 s on the detailed log. The switch
            // is only reported (SkyGlow uses the sky's light uncompressed either way): globally
            // and on the skybox material, since the variant drawn is the two together.
            bool globalHdr = Shader.IsKeywordEnabled("HDR_ON");
            bool materialHdr = skybox.IsKeywordEnabled("HDR_ON");
            int mode = (globalHdr ? 2 : 1) + (materialHdr ? 2 : 0);
            bool detail = Log.Detailed && Time.time >= _nextSkyGlowDetail;
            if (mode == _skyGlowLogged && !detail)
                return;
            _nextSkyGlowDetail = Time.time + 10f;

            _skyGlow.SunDir = sunDir;
            _skyGlow.SunSize = sunSize;
            _skyGlow.BetaR = Shader.GetGlobalVector("_BetaR");
            _skyGlow.BetaM = Shader.GetGlobalVector("_BetaM");
            _skyGlow.MiePhaseG = Shader.GetGlobalVector("_MiePhase_g");
            _skyGlow.MieConst = Shader.GetGlobalVector("_MieConst");
            _skyGlow.NightZenith = Shader.GetGlobalVector("_NightZenithColor");
            _skyGlow.NightHorizon = Shader.GetGlobalVector("_NightHorizonColor");
            _skyGlow.SkyMultiplier = Shader.GetGlobalVector("_SkyMultiplier");
            _skyGlow.ColorCorrection = Shader.GetGlobalVector("_ColorCorrection");

            var text = new StringBuilder("sun glow: the far clouds let the sky through but not the sun's glow (sun ");
            text.Append((Mathf.Asin(Mathf.Clamp(sunDir.y, -1f, 1f)) * Mathf.Rad2Deg).ToString("F1"))
                .Append(" deg up) -- sky let through, by degrees from the sun:");
            for (int i = 0; i < SkyGlowLogAngles.Length; i++)
                text.Append(' ').Append(SkyGlowLogAngles[i]).Append('=').Append((_skyGlow.PassAround(SkyGlowLogAngles[i]) * 100f).ToString("F0")).Append('%');

            if (mode != _skyGlowLogged)
            {
                text.Append(" | sky inputs: betaR=").Append(_skyGlow.BetaR.ToString("F5")).Append(" betaM=").Append(_skyGlow.BetaM.ToString("F5"))
                    .Append(" miePhase=").Append(_skyGlow.MiePhaseG.ToString("F4")).Append(" mieConst=").Append(_skyGlow.MieConst.ToString("F4"))
                    .Append(" skyMultiplier=").Append(_skyGlow.SkyMultiplier.ToString("F3")).Append(" colorCorrection=").Append(_skyGlow.ColorCorrection.ToString("F3"))
                    .Append(" sunSize=").Append(sunSize.ToString("F2"))
                    .Append(" nightZenith=").Append(_skyGlow.NightZenith.ToString("F5")).Append(" nightHorizon=").Append(_skyGlow.NightHorizon.ToString("F5"))
                    .Append(" | HDR_ON keyword: global=").Append(globalHdr).Append(" material=").Append(materialHdr)
                    .Append(" (material keywords: ").Append(skybox.shaderKeywords != null ? string.Join(" ", skybox.shaderKeywords) : "").Append(')');
                Log.Msg(text.ToString());
            }
            else
            {
                Log.Detail(text.ToString());
            }

            _skyGlowLogged = mode;
        }

        /// <summary>A colour setting's value, or its default before the settings exist.</summary>
        private static Color32 ColourOf(ColorSetting setting, Color32 fallback)
        {
            return setting != null ? setting.value : fallback;
        }

        /// <summary>"Different colours at night" is on.</summary>
        private static bool NightColours
        {
            get { return Settings.CloudNightColors != null && Settings.CloudNightColors.value; }
        }

        /// <summary>
        /// The fog layer. (The shadow map it is lit through: <see cref="ApplyShadowLookup"/>.)
        /// </summary>
        private void ApplyFog(Color lightAmbient, Color baseSun)
        {
            // Off means nothing runs. The shader's guard is a uniform branch, so writing zero
            // here really is the whole cost of the feature when it is switched off -- no
            // keyword, no second variant, and TerrainHeightMap stands down with it.
            if (!CloudFog.Active)
            {
                _material.SetFloat(IdFogAmount, 0f);
                _material.SetFloat(IdFogDensity, 0f);
                return;
            }

            // The layer AS DRAWN (1.5.0): the sliders are the most fog there can be, and the look
            // (CloudFog.Look: the time of day, the morning fog, the mist) takes it lower, thinner or
            // wispier inside them. With the three "Changing fog" rows at 0% every one of these is
            // its slider exactly. Uniforms only: the shader does not know, and costs no more.
            float density = (Settings.FogDensity != null ? Mathf.Max(0f, Settings.FogDensity.value) : Settings.Defaults.FogDensity) * CloudFog.Look.Density;
            float layerBase = CloudFog.LayerBase;
            float height = CloudFog.LayerHeight;
            bool followsGround = CloudFog.FollowsGround;
            float breakup = CloudFog.LayerBreakup(Settings.FogBreakup != null ? Mathf.Clamp01(Settings.FogBreakup.value) : Settings.Defaults.FogBreakup);

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
            _material.SetFloat(IdFogBase, layerBase);
            _material.SetFloat(IdFogHeight, height);
            _material.SetFloat(IdFogBreakup, breakup * 0.8f);

            // Steps follow the Quality slider: the fog is the most expensive thing on screen
            // when the camera is inside it, and that slider is the lever for weaker GPUs. 1.3.0
            // (the author: "The volumetric fog is still too grainy"): they grow with the SQUARE
            // of Quality, so the grain fix is paid for where there is headroom -- 58 at the High
            // preset (29 before), 26 at Medium (19), 8 at Low (10: Low is no dearer than it was,
            // and less grainy, the steps now crowding where the ray meets the fog). Low-end
            // hardware: docs/FOG-PERFORMANCE-PLAN.md.
            float quality = Settings.CloudQuality != null ? Settings.CloudQuality.value : Settings.Defaults.Quality;
            // Whole steps: the march's last one then ends exactly where the fog does, not a few
            // per cent past it.
            _material.SetFloat(IdFogSteps, Mathf.Clamp(Mathf.Round(quality * quality / 160f), 8f, 64f));

            // How far it is drawn: the "Fog distance" setting (1.3.1; 9 km before, as its default
            // is). It fades out over the last 9 km: at the default that is from the camera on, the
            // fade it always had; farther, the fog is whole up to there. A ray longer than 9 km gets
            // more steps in the shader, so its steps stay as long -- that is what a farther fog costs.
            float reach = CloudFog.Reach;
            float fadeLength = Mathf.Min(reach, CloudFog.FadeLength);
            _material.SetFloat(IdFogMaxDistance, reach);
            _material.SetFloat(IdFogFadeStart, reach - fadeLength);
            _material.SetFloat(IdFogFadeLength, fadeLength);

            // Nothing of a fog that follows the ground is above the highest ground plus its top: the
            // march looks no higher (and no longer stops as if the ground were level).
            _material.SetFloat(IdFogCeiling, TerrainHeightMap.Highest + layerBase + height);

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
        }

        /// <summary>
        /// The cloud shadow map for the fog's sun shafts and the rainbow, or none. The sun's OWN
        /// transform is used for the lookup whatever is lighting the clouds: the shadow map is
        /// always rendered along the sun, and at night it is simply white.
        /// </summary>
        private void ApplyShadowLookup(Light sun, CloudShadowMap map, bool wanted)
        {
            _material.SetFloat(IdShadowAvailable, wanted ? 1f : 0f);
            if (!wanted)
                return;

            _material.SetTexture(IdCloudShadowTex, map.Texture);
            _material.SetVector(IdShadowOrigin, CloudShadowMap.Anchor);
            _material.SetVector(IdShadowRight, sun.transform.right);
            _material.SetVector(IdShadowUp, sun.transform.up);
            _material.SetFloat(IdShadowSize, CloudShadowMap.CookieSize);
            _material.SetFloat(IdShadowDarkness, CloudShadowMap.ShadowDepth(CloudShaderParams.Coverage));
        }

        /// <summary>
        /// The rainbow (Sky.Rainbow): its colour table, how far round the antisolar point it reaches,
        /// and the light it is lit by -- the CLOUD's sun, untinted, like the rain it is part of (the
        /// rain is drawn in the cloud pass and reads as part of the cloud), times the Rainbows
        /// setting. A zero strength switches the shader's bow off (a uniform branch: nothing runs).
        /// True while a bow can show.
        /// </summary>
        private bool ApplyRainbow(Light sun, Light key, Color cloudSun, bool shadowMap, float elevation)
        {
            float strength = Rainbow.Strength(elevation, sun != null && key == sun, shadowMap, _rainbowTable != null);

            // One arch at a time, standing where the camera looks (Rainbow.UpdateArch).
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            Rainbow.UpdateArch(_field, LookAt(), toSun.normalized, elevation, strength, Time.deltaTime);
            float shown = strength * Rainbow.ArchShown;
            bool on = shown > 0f;

            _material.SetVector(IdBowParams, new Vector4(1f / (RainbowTable.MaxAngle * Mathf.Deg2Rad), RainbowTable.Scale, on ? 1f : 0f,
                                                         Rainbow.ArchCentre.y));
            _material.SetVector(IdBowSun, AsVector(cloudSun) * (shown * Rainbow.ArchGain));
            Vector3 spot = Rainbow.ArchSpot;
            _material.SetVector(IdBowArch, new Vector4(spot.x, spot.y, spot.z, Mathf.Max(Rainbow.ArchRadius, 1f)));
            if (on)
                _material.SetTexture(IdBowTex, _rainbowTable);

            return on;
        }

        /// <summary>
        /// Where the camera looks: the point the game's camera orbits (CameraController.m_currentPosition,
        /// IL: UpdateTransform places the camera round it), else 1.5 km ahead of the camera.
        /// </summary>
        private Vector3 LookAt()
        {
            if (_cameraController == null && !_lookedForController && _camera != null)
            {
                _lookedForController = true;
                _cameraController = _camera.GetComponent<CameraController>();
                if (_cameraController == null)
                    _cameraController = FindObjectOfType<CameraController>();
                Log.Msg("rainbow: arches stand where the camera looks -- " +
                        (_cameraController != null ? "the game camera's look-at point" : "no CameraController found, 1.5 km ahead of the camera"));
            }

            if (_cameraController != null)
                return _cameraController.m_currentPosition;

            return _camera != null ? _camera.transform.position + _camera.transform.forward * 1500f : Vector3.zero;
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

        /// <summary>
        /// Every float and vector of the cloud material onto another one, which KEEPS ITS OWN
        /// RENDER QUEUE. CopyPropertiesFromMaterial takes the source's queue along with its values
        /// (probed in the 5.6 editor, 2026-09-30: a source at 2520 put the destination at 2520,
        /// whatever it had): in 1.3.0 that put the air draw in the clouds' early slot, before the
        /// city's see-through objects, which were then drawn over the fog. The textures are not
        /// all copied -- Unity 5.6 copies only those a shader lists in its Properties block, and
        /// cannot even read the others back (probed 2026-09-28) -- so they go on after it. Every
        /// value the material had is replaced, so its own go on after it too.
        /// </summary>
        private static void CopyValues(Material from, Material to)
        {
            int queue = to.renderQueue;
            to.CopyPropertiesFromMaterial(from);
            to.renderQueue = queue;
        }

        private void OnDestroy()
        {
            IsActive = false;
            Night = 0f;
            CloudsQueue = LateQueue;

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
            if (_airMaterial != null)
                Destroy(_airMaterial);
            if (_noise != null)
                Destroy(_noise);
            if (_blueNoise != null)
                Destroy(_blueNoise);
            if (_rainbowTable != null)
                Destroy(_rainbowTable);

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
