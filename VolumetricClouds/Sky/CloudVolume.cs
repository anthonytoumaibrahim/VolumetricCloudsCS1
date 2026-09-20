using System;
using System.Threading;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Raymarched volumetric clouds. Draws a box that follows the camera; the shader
    /// (compiled in Unity 5.6 and embedded as an AssetBundle) marches the cloud slab for
    /// every pixel. Also drives the <see cref="CloudShadowMap"/>, since both need the same
    /// noise volume. Falls back to the billboards when the bundle is missing or fails.
    /// </summary>
    public class CloudVolume : MonoBehaviour
    {
        private const int NoiseSize = 64;
        private const float MaxDistance = 30000f;

        /// <summary>True while the volumetric layer is actually drawing, so billboards stand down.</summary>
        public static bool IsActive { get; private set; }

        private CloudDensityField _field;
        private CloudShadowMap _shadowMap;
        private Camera _camera;
        private GameObject _holder;
        private MeshRenderer _renderer;
        private Material _material;
        private Mesh _mesh;
        private Texture3D _noise;

        private Thread _noiseThread;
        private volatile byte[] _noiseBytes;
        private bool _failed;
        private bool _loggedLighting;

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
        private static readonly int IdFogOffset = Shader.PropertyToID("_FogOffset");
        private static readonly int IdFogBoil = Shader.PropertyToID("_FogBoil");
        private static readonly int IdFogPool = Shader.PropertyToID("_FogPool");
        private static readonly int IdFogFloor = Shader.PropertyToID("_FogFloor");
        private static readonly int IdFogBreakup = Shader.PropertyToID("_FogBreakup");
        private static readonly int IdTerrainTex = Shader.PropertyToID("_TerrainTex");
        private static readonly int IdTerrainMapSize = Shader.PropertyToID("_TerrainMapSize");
        private static readonly int IdFogHeight = Shader.PropertyToID("_FogHeight");
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

        /// <summary>
        /// Extinction per metre inside full-strength rain, at 100% on the slider. Real
        /// downpours are several times denser, but a city builder has to stay readable from a
        /// kilometre up: at this value 1 km of heavy rain still passes about half the view.
        /// </summary>
        private const float RainExtinction = 0.0006f;
        private const float RainMaxDistance = 14000f;

        /// <summary>Radiance the night glow adds to the very base of a cloud, at 100%.</summary>
        private const float NightGlowStrength = 0.03f;

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
            int seed = UnityEngine.Random.Range(1, 100000);
            _noiseThread = new Thread(() =>
            {
                try
                {
                    _noiseBytes = CloudNoise3D.Generate(NoiseSize, seed);
                }
                catch (Exception)
                {
                    _noiseBytes = new byte[0];
                }
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

            bool wanted = (Settings.CloudsVisible == null || Settings.CloudsVisible.value)
                       && (Settings.UseVolumetric == null || Settings.UseVolumetric.value);

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
        /// Rendered whether or not the volumetric clouds are being drawn: the billboards
        /// are placed from the same weather field, so the shadows still belong to them.
        /// </summary>
        private void UpdateShadowMap()
        {
            if (_shadowMap == null)
                return;

            bool enabled = Settings.CloudShadows == null || Settings.CloudShadows.value;
            if (!enabled)
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

            Color32[] colors = new Color32[bytes.Length / 4];
            for (int i = 0; i < colors.Length; i++)
            {
                int o = i * 4;
                colors[i] = new Color32(bytes[o], bytes[o + 1], bytes[o + 2], bytes[o + 3]);
            }

            _noise = new Texture3D(NoiseSize, NoiseSize, NoiseSize, TextureFormat.ARGB32, false)
            {
                name = "VolumetricCloudsNoise3D",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
            };
            _noise.SetPixels32(colors);
            _noise.Apply(false);
            _noiseBytes = null;

            Log.Msg("3D noise uploaded; volumetric clouds live.");
            return true;
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
            CloudLightning.ApplyTo(_material);
        }

        /// <summary>
        /// Lights the clouds from whichever of the sun and moon is currently brighter, plus
        /// the scene's ambient. Scaled down because the game's sun runs at HDR intensities
        /// that would clip a near-white cloud straight to flat white.
        /// </summary>
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

            float brightness = Settings.CloudBrightness != null ? Settings.CloudBrightness.value : Settings.Defaults.Brightness;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

            Vector3 sunDir = Vector3.up;
            Color sunColor = Color.black;

            if (key != null)
            {
                sunDir = -key.transform.forward;
                Color c = linear ? key.color.linear : key.color;

                // Heavier cover reads greyer. The clouds used to pick this up for free from
                // the global sun dimming; that is gone now, so apply the same curve here to
                // keep the look unchanged.
                float illuminationFloor = Settings.MinIllumination != null
                    ? Mathf.Clamp01(Settings.MinIllumination.value) : Settings.Defaults.MinIllumination;
                float overcast = Mathf.Lerp(1f, illuminationFloor, CloudShaderParams.Coverage);

                sunColor = c * (key.intensity * 0.25f * brightness * overcast);
            }

            Color ambient = RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat
                ? RenderSettings.ambientLight
                : RenderSettings.ambientSkyColor;
            if (linear)
                ambient = ambient.linear;
            ambient *= 0.9f * brightness;

            // Never fully black: even a moonless night sky silhouettes cloud faintly.
            const float floor = 0.012f;
            ambient.r = Mathf.Max(ambient.r, floor);
            ambient.g = Mathf.Max(ambient.g, floor);
            ambient.b = Mathf.Max(ambient.b, floor * 1.3f);

            _material.SetVector(IdSunDir, sunDir.normalized);
            _material.SetVector(IdSunColor, new Vector4(sunColor.r, sunColor.g, sunColor.b, 0f));
            _material.SetVector(IdAmbientColor, new Vector4(ambient.r, ambient.g, ambient.b, 0f));

            // Rain curtains are lit like the underside of the clouds they hang from (which is
            // why they go through the same brightness and overcast terms), a little dimmer.
            float curtains = Settings.RainCurtains != null ? Mathf.Max(0f, Settings.RainCurtains.value) : 1f;
            _material.SetFloat(IdRainShaftDensity, CloudRain.Active ? RainExtinction * curtains : 0f);
            _material.SetFloat(IdRainSteps, 20f);
            _material.SetFloat(IdRainMaxDistance, RainMaxDistance);
            _material.SetFloat(IdRainFloor, 0f);
            _material.SetFloat(IdRainFall, -CloudRain.FallOffset.y);
            _material.SetFloat(IdRainCurtainScale, CloudRain.CurtainScale);
            _material.SetVector(IdRainAmbient, new Vector4(ambient.r, ambient.g, ambient.b, 0f) * 0.6f);
            _material.SetVector(IdRainSun, new Vector4(sunColor.r, sunColor.g, sunColor.b, 0f) * 0.12f);

            ApplyNight(sun);
            ApplyFog(sun, ambient, sunColor);

            if (!_loggedLighting)
            {
                _loggedLighting = true;
                Log.Msg("cloud lighting key='" + (key == null ? "none" : key.name) + "' intensity=" +
                        (key == null ? 0f : key.intensity) + " sunColor=" + sunColor +
                        " ambientMode=" + RenderSettings.ambientMode + " ambient=" + ambient);
            }
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
        private void ApplyNight(Light sun)
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
            float glow = Settings.NightGlow != null ? Mathf.Max(0f, Settings.NightGlow.value) : 1f;
            _material.SetVector(IdNightGlow, new Vector4(0.8f, 0.86f, 0.96f, 0f) * (NightGlowStrength * glow * night));
        }

        /// <summary>
        /// The fog layer and the shadow map it is lit through. The sun's OWN transform is used
        /// for the lookup whatever is lighting the clouds: the shadow map is always rendered
        /// along the sun, and at night it is simply white.
        /// </summary>
        private void ApplyFog(Light sun, Color ambient, Color sunColor)
        {
            float density = Settings.FogDensity != null ? Mathf.Max(0f, Settings.FogDensity.value) : 1f;
            float height = Settings.FogHeight != null ? Mathf.Max(10f, Settings.FogHeight.value) : 90f;
            float breakup = Settings.FogBreakup != null ? Mathf.Clamp01(Settings.FogBreakup.value) : 0.5f;

            // The fog lies on the terrain map; until that exists there is nothing to lie on.
            Texture terrain = TerrainHeightMap.Texture;
            bool ready = CloudFog.Active && terrain != null;

            _material.SetFloat(IdFogAmount, ready ? CloudFog.Amount : 0f);
            _material.SetFloat(IdFogDensity, CloudFog.BaseExtinction * density);

            // Where there is fog comes from the clouds' weather field, thresholded for the share
            // of the map it should cover. The threshold is solved against the field AS THE GPU
            // READS IT (gamma-decoded, measured), so "fog amount 30%" really is fog over about
            // a third of the map -- with the clouds' own table it covered a fraction of that,
            // which drove the slider to 100%, where fog is everywhere.
            _material.SetFloat(IdFogThreshold, _field.GetThresholdAsDrawn(CloudFog.Amount, CloudFog.CoverSoftness));
            _material.SetFloat(IdFogTile, CloudFog.Tile);
            _material.SetVector(IdFogOffset, CloudFog.Offset);
            _material.SetFloat(IdFogBoil, CloudFog.Boil);
            _material.SetFloat(IdFogPool, TerrainHeightMap.PoolLevel);
            _material.SetFloat(IdFogFloor, TerrainHeightMap.Lowest - 10f);
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

            // Lit like the clouds (same brightness and overcast terms, so it sits in the same
            // picture), a little brighter: fog is lit from all round, a cloud base from below.
            // On top of that its own brightness, as the clouds have theirs, and a tint: the
            // light it is in still colours it (orange at sunset, blue at night), the tint
            // leans that towards a cold blue-grey or a warm haze.
            float fogBrightness = Settings.FogBrightness != null ? Mathf.Max(0f, Settings.FogBrightness.value) : 1f;
            float tint = Settings.FogTint != null ? Mathf.Clamp(Settings.FogTint.value, -1f, 1f) : 0f;
            Color lean = tint < 0f
                ? Color.Lerp(Color.white, new Color(0.78f, 0.93f, 1.18f), -tint)
                : Color.Lerp(Color.white, new Color(1.18f, 1f, 0.76f), tint);
            lean *= fogBrightness;

            _material.SetVector(IdFogAmbient, new Vector4(ambient.r * lean.r, ambient.g * lean.g, ambient.b * lean.b, 0f) * 1.3f);
            _material.SetVector(IdFogSun, new Vector4(sunColor.r * lean.r, sunColor.g * lean.g, sunColor.b * lean.b, 0f) * 0.7f);

            CloudShadowMap map = CloudShadowMap.Current;
            bool shadows = Settings.CloudShadows == null || Settings.CloudShadows.value;
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
        }
    }
}
