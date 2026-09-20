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
            float steps = Settings.CloudQuality != null ? Settings.CloudQuality.value : 48f;
            bool useDepth = Settings.CloudDepthOcclusion == null || Settings.CloudDepthOcclusion.value;

            CloudShaderParams.Apply(_material, _field, _noise);
            _material.SetFloat(IdSteps, steps);
            _material.SetFloat(IdMaxDistance, MaxDistance);
            _material.SetFloat(IdUseDepth, useDepth ? 1f : 0f);

            ApplyLighting();
        }

        /// <summary>
        /// Lights the clouds from whichever of the sun and moon is currently brighter, plus
        /// the scene's ambient. Scaled down because the game's sun runs at HDR intensities
        /// that would clip a near-white cloud straight to flat white.
        /// </summary>
        private void ApplyLighting()
        {
            Light key = null;
            DayNightProperties properties = DayNightProperties.instance;

            if (properties != null)
            {
                Light sun = properties.m_SunLight != null ? properties.m_SunLight.GetComponent<Light>() : null;
                Light moon = properties.m_MoonLight != null ? properties.m_MoonLight.GetComponent<Light>() : null;

                key = sun;
                if (moon != null && (sun == null || moon.intensity > sun.intensity))
                    key = moon;
            }

            float brightness = Settings.CloudBrightness != null ? Settings.CloudBrightness.value : 1f;
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
                    ? Mathf.Clamp01(Settings.MinIllumination.value) : 0.65f;
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

            if (!_loggedLighting)
            {
                _loggedLighting = true;
                Log.Msg("cloud lighting key='" + (key == null ? "none" : key.name) + "' intensity=" +
                        (key == null ? 0f : key.intensity) + " sunColor=" + sunColor +
                        " ambientMode=" + RenderSettings.ambientMode + " ambient=" + ambient);
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
