using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Rain streaks near the camera: one mesh of random seeds that RainDrops.shader places in
    /// world space, wraps around the camera and masks with <see cref="CloudRain"/>. See the
    /// shader for why this does not read as a filter on the lens the way the game's does.
    /// </summary>
    public class RainDrops : MonoBehaviour
    {
        /// <summary>Four vertices a drop, and Unity 5.6 meshes stop at 65k vertices.</summary>
        private const int DropCount = 14000;
        private const float FarShare = 0.4f;
        private const float LogInterval = 5f;

        private static readonly Vector3 BoxNear = new Vector3(40f, 30f, 40f);
        private static readonly Vector3 BoxFar = new Vector3(170f, 110f, 170f);

        private static readonly int IdBoxNear = Shader.PropertyToID("_BoxNear");
        private static readonly int IdBoxFar = Shader.PropertyToID("_BoxFar");
        private static readonly int IdFallOffset = Shader.PropertyToID("_FallOffset");
        private static readonly int IdFallDirection = Shader.PropertyToID("_FallDirection");
        private static readonly int IdDropColor = Shader.PropertyToID("_DropColor");
        private static readonly int IdDropFade = Shader.PropertyToID("_DropFade");
        private static readonly int IdDropDensity = Shader.PropertyToID("_DropDensity");
        private static readonly int IdPixelAngle = Shader.PropertyToID("_PixelAngle");
        private static readonly int IdStreakNear = Shader.PropertyToID("_StreakNear");
        private static readonly int IdStreakFar = Shader.PropertyToID("_StreakFar");

        private static bool _shadersChecked;
        private static bool _shadersAvailable;

        /// <summary>
        /// Our rain only replaces the game's when BOTH shaders it needs are in the bundle: the
        /// streaks, and the invisible material that silences the game's rain shell.
        /// </summary>
        public static bool ShadersAvailable
        {
            get
            {
                if (!_shadersChecked)
                {
                    _shadersChecked = true;
                    _shadersAvailable = ShaderBundle.Get(ShaderBundle.RainDrops) != null
                                     && ShaderBundle.Get(ShaderBundle.Invisible) != null;
                }

                return _shadersAvailable;
            }
        }

        private CloudDensityField _field;
        private Camera _camera;
        private GameObject _holder;
        private MeshRenderer _renderer;
        private Material _material;
        private Mesh _mesh;
        private float _nextLogTime;
        private float _fade;
        private float _heightAboveGround;

        public void Initialise(CloudDensityField field)
        {
            _field = field;
        }

        private void Start()
        {
            if (!ShadersAvailable)
            {
                Log.Warn("rain: shaders missing from the bundle; the game's own rain stays.");
                enabled = false;
                return;
            }

            _material = new Material(ShaderBundle.Get(ShaderBundle.RainDrops)) { name = "VolumetricCloudsRain" };
            _mesh = BuildMesh();

            _holder = new GameObject("VolumetricCloudsRain");
            _holder.AddComponent<MeshFilter>().sharedMesh = _mesh;

            _renderer = _holder.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.enabled = false;

            Log.Msg("rain: " + DropCount + " streaks ready (" + _mesh.vertexCount + " vertices)");
        }

        private void LateUpdate()
        {
            if (_material == null || _field == null)
                return;

            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return;

            float streaks = Settings.RainStreaks != null ? Mathf.Max(0f, Settings.RainStreaks.value) : 1f;
            _fade = HeightFade();

            // Nothing to draw: no rain, switched off, or the camera is too high for streaks.
            bool draw = CloudRain.Active && CloudRain.Amount > 0.001f && streaks > 0f && _fade > 0.001f;
            _renderer.enabled = draw;

            if (draw)
            {
                // The shader places every drop itself; the object only has to stay in view.
                _holder.transform.position = _camera.transform.position;
                UpdateMaterial(streaks);
            }

            if (Log.Detailed && Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + LogInterval;
                Log.Detail("rain: active=" + CloudRain.Active + (CloudRain.IsSnow ? " (winter map: the game keeps its snow)" : "") +
                        " amount=" + CloudRain.Amount.ToString("F2") +
                        " rainsUnder=" + (CloudRain.RainCoverage * 100f).ToString("F0") + "% of sky" +
                        " atCamera=" + CloudRain.LocalRain(_camera.transform.position).ToString("F2") +
                        " | streaks " + (draw ? "drawn" : "hidden") +
                        " cameraHeight=" + _heightAboveGround.ToString("F0") + "m fade=" + _fade.ToString("F2") +
                        " | slant=(" + CloudRain.Slant.x.ToString("F2") + "," + CloudRain.Slant.z.ToString("F2") + ")");
            }
        }

        /// <summary>
        /// Streaks belong to street level. From a city-wide view they are what made the game's
        /// rain a filter on the lens, so they fade out with height and the rain curtains in
        /// the cloud pass take over.
        /// </summary>
        private float HeightFade()
        {
            Vector3 position = _camera.transform.position;
            float ground = Singleton<TerrainManager>.exists
                ? Singleton<TerrainManager>.instance.SampleRawHeightSmooth(position)
                : 0f;

            _heightAboveGround = position.y - ground;

            float limit = Settings.RainStreakHeight != null ? Mathf.Max(50f, Settings.RainStreakHeight.value) : 450f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(limit * 0.4f, limit, _heightAboveGround));
        }

        private void UpdateMaterial(float streaks)
        {
            // Where it rains, the wind, the cloud base: everything SampleRain needs.
            CloudShaderParams.Apply(_material, _field, null);

            _material.SetVector(IdBoxNear, BoxNear);
            _material.SetVector(IdBoxFar, BoxFar);
            _material.SetVector(IdFallOffset, CloudRain.FallOffset);
            _material.SetVector(IdFallDirection, CloudRain.FallDirection);
            _material.SetVector(IdStreakNear, new Vector4(1.1f, 0.012f, 0f, 0f));
            _material.SetVector(IdStreakFar, new Vector4(3.2f, 0.035f, 0f, 0f));
            _material.SetFloat(IdDropFade, _fade);
            _material.SetFloat(IdDropDensity, streaks);

            // One pixel's angle, so the shader can keep a streak from going sub-pixel.
            float pixelAngle = 2f * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, _camera.pixelHeight);
            _material.SetFloat(IdPixelAngle, pixelAngle);

            _material.SetVector(IdDropColor, DropColor());
        }

        /// <summary>Streaks catch the sky: the scene's ambient, lifted, with a little of the key light.</summary>
        private static Vector4 DropColor()
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

            Color ambient = RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat
                ? RenderSettings.ambientLight
                : RenderSettings.ambientSkyColor;
            if (linear)
                ambient = ambient.linear;

            Color color = ambient * 1.4f;

            DayNightProperties properties = DayNightProperties.instance;
            Light sun = properties != null && properties.m_SunLight != null
                ? properties.m_SunLight.GetComponent<Light>() : null;
            if (sun != null)
                color += (linear ? sun.color.linear : sun.color) * (sun.intensity * 0.04f);

            // Visible against a night sky, never glowing against a day one.
            const float floor = 0.05f;
            return new Vector4(Mathf.Clamp(color.r, floor, 1.2f),
                               Mathf.Clamp(color.g, floor, 1.2f),
                               Mathf.Clamp(color.b, floor * 1.2f, 1.2f),
                               0.5f);
        }

        /// <summary>
        /// A cloud of seeds, not of positions: POSITION is random in 0..1 and the shader turns
        /// it into a place in the world. See RainDrops.shader for the layout.
        /// </summary>
        private static Mesh BuildMesh()
        {
            System.Random random = new System.Random(20260920);

            Vector3[] vertices = new Vector3[DropCount * 4];
            Vector2[] corners = new Vector2[DropCount * 4];
            List<Vector3> randoms = new List<Vector3>(DropCount * 4);
            int[] triangles = new int[DropCount * 6];

            for (int i = 0; i < DropCount; i++)
            {
                Vector3 seed = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                Vector3 perDrop = new Vector3(
                    (float)random.NextDouble(),                          // rank: shown once the rain is this strong
                    random.NextDouble() < FarShare ? 1f : 0f,            // layer
                    (float)random.NextDouble());                         // speed variation

                int v = i * 4;
                for (int c = 0; c < 4; c++)
                {
                    vertices[v + c] = seed;
                    randoms.Add(perDrop);
                }

                corners[v + 0] = new Vector2(-1f, 0f);
                corners[v + 1] = new Vector2(1f, 0f);
                corners[v + 2] = new Vector2(1f, 1f);
                corners[v + 3] = new Vector2(-1f, 1f);

                int t = i * 6;
                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 0;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            Mesh mesh = new Mesh { name = "VolumetricCloudsRainDrops" };
            mesh.vertices = vertices;
            mesh.uv = corners;
            mesh.SetUVs(1, randoms);
            mesh.triangles = triangles;

            // The real positions only exist in the shader, so the bounds say "always visible".
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return mesh;
        }

        private void OnDestroy()
        {
            if (_holder != null)
                Destroy(_holder);
            if (_mesh != null)
                Destroy(_mesh);
            if (_material != null)
                Destroy(_material);
        }
    }
}
