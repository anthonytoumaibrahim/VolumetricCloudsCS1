using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Draws the visible clouds as camera-facing quads placed from the same density field
    /// that drives the shadow cookie.
    /// </summary>
    /// <remarks>
    /// A stopgap by design: billboards read acceptably from normal city-viewing angles and
    /// fall apart if you fly through them. The placement and wind live in the field and
    /// <see cref="CloudWind"/>, so swapping this for a raymarched shader later doesn't
    /// touch either.
    /// </remarks>
    public class CloudBillboards : MonoBehaviour
    {
        private const int MaxPuffs = 600;

        /// <summary>Half-width of the area clouds are scattered over, in metres.</summary>
        private const float FieldExtent = 12000f;

        /// <summary>How many field tiles span that area. Higher means smaller cloud clusters.</summary>
        private const float TileRepeat = 2f;

        private const float MinCloudToPlace = 0.2f;

        private struct Puff
        {
            public Vector3 Base;
            public float Size;
            public float Alpha;
        }

        private readonly Puff[] _puffs = new Puff[MaxPuffs];
        private int _puffCount;

        private CloudDensityField _field;
        private Camera _camera;
        private GameObject _holder;
        private Mesh _mesh;
        private Material _material;

        private Vector3[] _vertices;
        private Vector2[] _uv;
        private Color32[] _colors;
        private int[] _triangles;

        private float _builtCoverage = -1f;
        private int _builtCount = -1;
        private bool _failed;

        public void Initialise(CloudDensityField field)
        {
            _field = field;
        }

        private void Start()
        {
            _material = PuffMaterial.Create();
            if (_material == null)
            {
                _failed = true;
                enabled = false;
                return;
            }

            _holder = new GameObject("VolumetricCloudsLayer");
            _holder.transform.position = Vector3.zero;

            _mesh = new Mesh { name = "VolumetricCloudsMesh" };
            _mesh.MarkDynamic();

            MeshFilter filter = _holder.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            MeshRenderer renderer = _holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            AllocateBuffers();
            Log.Msg("cloud billboards created, max " + MaxPuffs + " puffs");
        }

        private void AllocateBuffers()
        {
            _vertices = new Vector3[MaxPuffs * 4];
            _uv = new Vector2[MaxPuffs * 4];
            _colors = new Color32[MaxPuffs * 4];
            _triangles = new int[MaxPuffs * 6];

            for (int i = 0; i < MaxPuffs; i++)
            {
                int v = i * 4;
                _uv[v + 0] = new Vector2(0f, 0f);
                _uv[v + 1] = new Vector2(1f, 0f);
                _uv[v + 2] = new Vector2(1f, 1f);
                _uv[v + 3] = new Vector2(0f, 1f);

                int t = i * 6;
                _triangles[t + 0] = v + 0;
                _triangles[t + 1] = v + 2;
                _triangles[t + 2] = v + 1;
                _triangles[t + 3] = v + 0;
                _triangles[t + 4] = v + 3;
                _triangles[t + 5] = v + 2;
            }
        }

        private void LateUpdate()
        {
            if (_failed || _field == null || _holder == null)
                return;

            // Stand down while the raymarched layer is drawing; this is only its fallback.
            bool visible = (Settings.CloudsVisible == null || Settings.CloudsVisible.value)
                        && !CloudVolume.IsActive;
            _holder.SetActive(visible);
            if (!visible)
                return;

            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return;

            float coverage = CloudShaderParams.CoverageStepped;
            int requested = Settings.CloudPuffCount != null
                ? Mathf.Clamp(Mathf.RoundToInt(Settings.CloudPuffCount.value), 0, MaxPuffs)
                : Mathf.Min((int)Settings.Defaults.PuffCount, MaxPuffs);

            if (!Mathf.Approximately(coverage, _builtCoverage) || requested != _builtCount)
            {
                Place(coverage, requested);
                _builtCoverage = coverage;
                _builtCount = requested;
            }

            BuildMesh();
        }

        /// <summary>
        /// Scatters puffs on a jittered grid and keeps the ones the density field says are
        /// cloudy, so the visible clouds and the shadows agree about where the weather is.
        /// </summary>
        private void Place(float coverage, int requested)
        {
            _puffCount = 0;
            if (requested <= 0)
                return;

            float altitude = Settings.CloudAltitude != null ? Settings.CloudAltitude.value : Settings.Defaults.Altitude;
            float baseSize = Settings.CloudPuffSize != null ? Settings.CloudPuffSize.value : Settings.Defaults.PuffSize;

            // Oversample the grid, since only cloudy cells produce a puff.
            int grid = Mathf.CeilToInt(Mathf.Sqrt(requested / Mathf.Max(0.05f, coverage)));
            grid = Mathf.Clamp(grid, 1, 256);

            for (int gz = 0; gz < grid && _puffCount < requested; gz++)
            {
                for (int gx = 0; gx < grid && _puffCount < requested; gx++)
                {
                    float u = (gx + Hash01(gx, gz, 11)) / grid;
                    float v = (gz + Hash01(gx, gz, 29)) / grid;

                    float cloud = _field.SampleCloud(u * TileRepeat, v * TileRepeat, coverage);
                    if (cloud < MinCloudToPlace)
                        continue;

                    float jitterY = (Hash01(gx, gz, 53) - 0.5f) * altitude * 0.15f;

                    _puffs[_puffCount++] = new Puff
                    {
                        Base = new Vector3((u - 0.5f) * 2f * FieldExtent,
                                           altitude + jitterY,
                                           (v - 0.5f) * 2f * FieldExtent),
                        Size = baseSize * (0.55f + cloud * 0.9f) * (0.7f + Hash01(gx, gz, 71) * 0.6f),
                        Alpha = Mathf.Clamp01(cloud * 1.1f),
                    };
                }
            }
        }

        private void BuildMesh()
        {
            Transform cam = _camera.transform;
            Vector3 right = cam.right;
            Vector3 up = cam.up;

            Vector3 wind = CloudWind.Offset;
            float span = FieldExtent * 2f;

            byte tint = 255;
            for (int i = 0; i < _puffCount; i++)
            {
                Puff puff = _puffs[i];

                // Drift with the wind, wrapping back around the field so the layer never
                // runs out. Wrapping pops, but at this distance it reads as cloud churn.
                Vector3 position = puff.Base + wind;
                position.x = Wrap(position.x + FieldExtent, span) - FieldExtent;
                position.z = Wrap(position.z + FieldExtent, span) - FieldExtent;

                Vector3 dx = right * puff.Size;
                Vector3 dy = up * puff.Size;

                int v = i * 4;
                _vertices[v + 0] = position - dx - dy;
                _vertices[v + 1] = position + dx - dy;
                _vertices[v + 2] = position + dx + dy;
                _vertices[v + 3] = position - dx + dy;

                byte a = (byte)(puff.Alpha * 255f);
                Color32 colour = new Color32(tint, tint, tint, a);
                _colors[v + 0] = colour;
                _colors[v + 1] = colour;
                _colors[v + 2] = colour;
                _colors[v + 3] = colour;
            }

            // Collapse unused quads instead of resizing the arrays every rebuild.
            for (int i = _puffCount; i < MaxPuffs; i++)
            {
                int v = i * 4;
                _vertices[v + 0] = Vector3.zero;
                _vertices[v + 1] = Vector3.zero;
                _vertices[v + 2] = Vector3.zero;
                _vertices[v + 3] = Vector3.zero;
            }

            _mesh.vertices = _vertices;
            _mesh.uv = _uv;
            _mesh.colors32 = _colors;
            _mesh.triangles = _triangles;

            // Set explicitly: the mesh moves every frame and recalculated bounds would
            // make the renderer cull the layer as the camera turns.
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(span * 2f, 20000f, span * 2f));
        }

        private static float Wrap(float value, float span)
        {
            float r = value % span;
            return r < 0f ? r + span : r;
        }

        private static float Hash01(int x, int y, int seed)
        {
            int n = x * 374761393 + y * 668265263 + seed * 1274126177;
            n = (n ^ (n >> 13)) * 1274126177;
            n = n ^ (n >> 16);
            return (n & 0x7fffffff) / (float)0x7fffffff;
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
