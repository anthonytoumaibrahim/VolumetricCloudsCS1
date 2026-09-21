using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;
using UnityEngine.Rendering;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The city's lights as our fog sees them: a map, centred on the camera, of the light each
    /// lamp and vehicle sheds into the air round it. The fog march reads it (CloudRaymarch:
    /// LampLightAt) and the fog round a street light glows -- which is what the glow round a
    /// street light on a foggy night IS. The halos are not touched.
    /// </summary>
    /// <remarks>
    /// Where the lights come from (measured by a probe on 2026-09-21, see CLAUDE.md):
    ///
    /// Street and building lamps are baked by the game into the light layers of
    /// RenderManager.m_groups (45 x 45 cells of 384 m; MeshLayer.m_mesh is a chain of
    /// MeshWrapper), in world coordinates, one quad per lamp. The mega groups hold light
    /// layers too, but their layer bits are never set: the game draws lights from the fine
    /// cells only. Each frame this draws those meshes again, with our own material into our
    /// own texture -- the game's meshes and materials are only read.
    ///
    /// Vehicles (and lamps drawn without an instance id, such as IMT's) never reach those
    /// meshes; they are LightSystem.DrawLight calls, which the existing prefix hands to
    /// <see cref="Record"/>. About 45 a frame, all spot lights, so they become beams.
    ///
    /// Because the map is redrawn every frame from what the game itself draws, a moving car's
    /// light moves with it, and a moved lamp lights the fog wherever the game draws that lamp.
    /// </remarks>
    public class FogLampMap : MonoBehaviour
    {
        public const int Resolution = 1024;

        /// <summary>Metres the map spans: 2 m a texel, lamps (about 37 m apart) well separated.</summary>
        public const float Size = 2048f;

        /// <summary>The light at a lamp, per unit of its colour x brightness, at 100%.</summary>
        private const float LampScale = 1f;

        private const int MaxDynamic = 2048;
        private const float MapHalf = 8640f;
        private const float GroupCell = 384f;
        private const float LogInterval = 5f;
        private const int ProbeLamps = 16;

        private static readonly int IdLampMapRect = Shader.PropertyToID("_LampMapRect");
        private static readonly int IdLampFlip = Shader.PropertyToID("_LampFlip");
        private static readonly int IdLampRadius = Shader.PropertyToID("_LampRadius");
        private static readonly int IdProbeUV = Shader.PropertyToID("_ProbeUV");
        private static readonly int IdVCLampMap = Shader.PropertyToID("_VCLampMap");
        private static readonly int IdVCLampMapRect = Shader.PropertyToID("_VCLampMapRect");
        private static readonly int IdVCLampParams = Shader.PropertyToID("_VCLampParams");

        private struct DynamicLight
        {
            public Vector3 Position;
            public Vector4 Beam;
            public Color Color;
            public float Range;
            public float Intensity;
        }

        // Filled from the DrawLight prefix (main thread) while Collecting; drained each frame.
        public static bool Collecting;
        private static readonly List<DynamicLight> Pending = new List<DynamicLight>();
        private static Rect _collectArea;

        private enum State { Off, Idle, On }

        private State _state = State.Off;
        private bool _failed;
        private Camera _camera;
        private Material _material;
        private RenderTexture _map;
        private CommandBuffer _buffer;
        private Mesh _dynamicMesh;
        private float _flip = 1f;

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector4> _tangents = new List<Vector4>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();

        private int _meshesDrawn;
        private long _verticesDrawn;
        private int _dynamicDrawn;
        private float _nextLog;

        private bool _orientationKnown;
        private int _probeAttempts;
        private float _nextProbe;

        /// <summary>
        /// A dynamic light the game is drawing this frame. Called for every DrawLight, so it
        /// does as little as it can.
        /// </summary>
        public static void Record(LightType type, Vector3 pos, Vector3 dir, Color color, float intensity, float range, float spotAngle)
        {
            if (Pending.Count >= MaxDynamic || !_collectArea.Contains(new Vector2(pos.x, pos.z)))
                return;

            float cosHalf = type == LightType.Spot ? Mathf.Cos(spotAngle * 0.5f * Mathf.Deg2Rad) : -2f;
            Pending.Add(new DynamicLight
            {
                Position = pos,
                Beam = new Vector4(dir.x, dir.y, dir.z, cosHalf),
                Color = color,
                Range = range,
                Intensity = intensity,
            });
        }

        private static bool Enabled
        {
            get { return Settings.FogLampsEnabled == null || Settings.FogLampsEnabled.value; }
        }

        private static float Strength
        {
            get { return Settings.FogLampLight != null ? Mathf.Max(0f, Settings.FogLampLight.value) : Settings.Defaults.FogLampLight; }
        }

        private static float Radius
        {
            get { return Settings.FogLampRadius != null ? Mathf.Clamp(Settings.FogLampRadius.value, 2f, 40f) : Settings.Defaults.FogLampRadius; }
        }

        private void LateUpdate()
        {
            if (_failed)
                return;

            // Off: nothing exists. Idle (daytime): the map is kept but neither drawn nor read.
            string whyOff = !CloudFog.Active ? "our fog is off"
                          : !Enabled ? "'Fog lit by the city's lights' is switched off"
                          : Strength <= 0f ? "Lit fog brightness is at 0%"
                          : null;

            if (whyOff != null)
            {
                SetState(State.Off, whyOff);
                return;
            }

            if (_camera == null)
                _camera = Camera.main;

            if (_camera == null || !Singleton<RenderManager>.exists)
                return;

            // The lamps switch on at dusk by their own thresholds, which the shader applies; this
            // only saves the work while the sun is high enough that none can be on.
            if (SunElevation() > 20f)
            {
                SetState(State.Idle, "daytime: no lamp is on");
                return;
            }

            SetState(State.On, null);
            if (_state != State.On)
                return;

            Render();

            if (!_orientationKnown && Time.realtimeSinceStartup >= _nextProbe)
                CheckOrientation();

            if (Log.Detailed && Time.realtimeSinceStartup >= _nextLog)
            {
                _nextLog = Time.realtimeSinceStartup + LogInterval;
                Log.Detail("fog lamps: drew " + _meshesDrawn + " lamp meshes (~" + _verticesDrawn / 4 + " lamps) + " + _dynamicDrawn +
                           " dynamic lights | strength " + (Strength * 100f).ToString("F0") + "%, lit radius " + Radius.ToString("F0") +
                           " m | daylight " + Shader.GetGlobalFloat("_GiantMarshmallow").ToString("F2") +
                           " | orientation " + (_orientationKnown ? "verified" : "not yet verified") + " flip=" + _flip);
            }
        }

        private void SetState(State state, string why)
        {
            if (state == _state)
                return;

            if (state == State.On && !Create())
                return;

            if (state == State.Off)
                Release();

            if (state != State.On)
            {
                Collecting = false;
                Pending.Clear();
                Shader.SetGlobalVector(IdVCLampParams, Vector4.zero);
            }

            Log.Msg(state == State.On
                ? "fog lamps: ON -- the city's lights light our fog. Map " + Resolution + "^2 over " + Size.ToString("F0") + " m (" +
                  (Size / Resolution).ToString("F1") + " m a texel), strength " + (Strength * 100f).ToString("F0") +
                  "%, lit radius " + Radius.ToString("F0") + " m"
                : "fog lamps: " + (state == State.Idle ? "idle" : "off") + " (" + why + ")");

            _state = state;
        }

        private bool Create()
        {
            if (_material != null)
                return true;

            Shader shader = ShaderBundle.Get(ShaderBundle.FogLamps);
            if (shader == null || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                _failed = true;
                Log.Warn("fog lamps: unavailable (" + (shader == null ? "shader missing" : "no half-float render textures") +
                         "); the fog will not be lit by the city's lights.");
                return false;
            }

            _material = new Material(shader) { name = "VolumetricClouds FogLamps" };

            _map = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "VolumetricClouds FogLampMap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
            };
            _map.Create();

            _buffer = new CommandBuffer { name = "VolumetricClouds fog lamps" };
            _dynamicMesh = new Mesh { name = "VolumetricClouds dynamic lights" };
            _dynamicMesh.MarkDynamic();

            // -1 where render targets are stored top row first (D3D): exactly what Unity
            // itself applies when a camera renders into a texture. Verified against the map
            // once it has lamps in it (CheckOrientation).
            _flip = GL.GetGPUProjectionMatrix(Matrix4x4.identity, true).m11 < 0f ? -1f : 1f;
            _orientationKnown = false;
            _probeAttempts = 0;
            _nextProbe = Time.realtimeSinceStartup + 1f;
            return true;
        }

        private void Release()
        {
            if (_map != null)
            {
                _map.Release();
                Destroy(_map);
                _map = null;
            }

            if (_buffer != null)
            {
                _buffer.Release();
                _buffer = null;
            }

            if (_dynamicMesh != null)
            {
                Destroy(_dynamicMesh);
                _dynamicMesh = null;
            }

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        private void Render()
        {
            Vector3 eye = _camera.transform.position;

            // Snapped to whole texels, so the lamps do not shimmer as the camera moves.
            float texel = Size / Resolution;
            float minX = Mathf.Round(eye.x / texel) * texel - Size * 0.5f;
            float minZ = Mathf.Round(eye.z / texel) * texel - Size * 0.5f;
            var rect = new Vector4(minX, minZ, 1f / Size, 0f);
            float radius = Radius;

            _material.SetVector(IdLampMapRect, rect);
            _material.SetFloat(IdLampFlip, _flip);
            _material.SetFloat(IdLampRadius, radius);

            _buffer.Clear();
            _buffer.SetRenderTarget(_map);
            _buffer.ClearRenderTarget(false, true, Color.clear);

            // Lamps whose footprint can reach into the map.
            float margin = 2f * radius + 10f;
            _meshesDrawn = 0;
            _verticesDrawn = 0;

            RenderManager render = Singleton<RenderManager>.instance;
            LightSystem lights = render.lightSystem;
            RenderGroup[] groups = render.m_groups;

            for (int i = 0; lights != null && groups != null && i < groups.Length; i++)
            {
                RenderGroup group = groups[i];
                if (group == null)
                    continue;

                float cellX = group.m_x * GroupCell - MapHalf;
                float cellZ = group.m_z * GroupCell - MapHalf;
                if (cellX > minX + Size + margin || cellX + GroupCell < minX - margin ||
                    cellZ > minZ + Size + margin || cellZ + GroupCell < minZ - margin)
                    continue;

                for (RenderGroup.MeshLayer layer = group.m_layers; layer != null; layer = layer.m_nextLayer)
                {
                    if (layer.m_layer != lights.m_lightLayer && layer.m_layer != lights.m_lightLayerFloating)
                        continue;

                    for (RenderGroup.MeshWrapper wrapper = layer.m_mesh; wrapper != null; wrapper = wrapper.m_next)
                    {
                        Mesh mesh = wrapper.m_mesh;
                        if (mesh == null)
                            continue;

                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                            _buffer.DrawMesh(mesh, group.m_matrix, _material, sub, 0);

                        _meshesDrawn++;
                        _verticesDrawn += mesh.vertexCount;
                    }
                }
            }

            // Vehicles: what DrawLight was given since the last frame.
            _dynamicDrawn = BuildDynamicMesh();
            if (_dynamicDrawn > 0)
                _buffer.DrawMesh(_dynamicMesh, Matrix4x4.identity, _material, 0, 1);

            Graphics.ExecuteCommandBuffer(_buffer);

            // Published in the same frame as the map is drawn, so the fog can never read it
            // with last frame's position.
            Shader.SetGlobalTexture(IdVCLampMap, _map);
            Shader.SetGlobalVector(IdVCLampMapRect, rect);
            Shader.SetGlobalVector(IdVCLampParams, new Vector4(Strength * LampScale, 3f / (radius * radius), 1f, Size * 0.75f));

            _collectArea = new Rect(minX - 100f, minZ - 100f, Size + 200f, Size + 200f);
            Collecting = true;
        }

        private int BuildDynamicMesh()
        {
            _vertices.Clear();
            _normals.Clear();
            _tangents.Clear();
            _colors.Clear();
            _uvs.Clear();
            _triangles.Clear();

            foreach (DynamicLight light in Pending)
            {
                int first = _vertices.Count;
                AddCorner(-1f, -1f, light);
                AddCorner(1f, -1f, light);
                AddCorner(1f, 1f, light);
                AddCorner(-1f, 1f, light);

                _triangles.Add(first);
                _triangles.Add(first + 1);
                _triangles.Add(first + 2);
                _triangles.Add(first);
                _triangles.Add(first + 2);
                _triangles.Add(first + 3);
            }

            int count = Pending.Count;
            Pending.Clear();

            _dynamicMesh.Clear();
            if (count == 0)
                return 0;

            _dynamicMesh.SetVertices(_vertices);
            _dynamicMesh.SetNormals(_normals);
            _dynamicMesh.SetTangents(_tangents);
            _dynamicMesh.SetColors(_colors);
            _dynamicMesh.SetUVs(0, _uvs);
            _dynamicMesh.SetTriangles(_triangles, 0);
            _dynamicMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return count;
        }

        private void AddCorner(float x, float y, DynamicLight light)
        {
            _vertices.Add(new Vector3(x, y, 0f));
            _normals.Add(light.Position);
            _tangents.Add(light.Beam);
            _colors.Add(light.Color);
            _uvs.Add(new Vector2(light.Range, light.Intensity));
        }

        /// <summary>
        /// Once, when the map first has lamps in it: samples the map, the way the fog does, at
        /// real lamps and at the same points mirrored north-south about the map's centre. If the
        /// mirror images are the bright ones the map was drawn upside down, and the flip is
        /// corrected. Either way the answer goes in the log: a guessed orientation that was
        /// wrong would show as fog lit where there are no lamps.
        /// </summary>
        private void CheckOrientation()
        {
            _nextProbe = Time.realtimeSinceStartup + 2f;
            if (++_probeAttempts > 10)
            {
                _orientationKnown = true;
                Log.Warn("fog lamps: could not verify the map's orientation (no lit lamps found near the camera after 10 tries); " +
                         "keeping flip=" + _flip);
                return;
            }

            List<Vector3> lamps = LampsNearCentre();
            if (lamps.Count < 3)
                return;

            Vector4 rect = Shader.GetGlobalVector(IdVCLampMapRect);
            var uvs = new Vector4[ProbeLamps];
            for (int i = 0; i < ProbeLamps; i++)
            {
                Vector3 p = lamps[i % lamps.Count];
                float u = (p.x - rect.x) * rect.z;
                float v = (p.z - rect.y) * rect.z;
                uvs[i] = new Vector4(u, v, u, 1f - v);
            }

            var probe = RenderTexture.GetTemporary(ProbeLamps * 2, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(ProbeLamps * 2, 1, TextureFormat.RGBA32, false, true);
            RenderTexture previous = RenderTexture.active;
            try
            {
                _material.SetVectorArray(IdProbeUV, uvs);
                Graphics.Blit(_map, probe, _material, 2);
                RenderTexture.active = probe;
                pixels.ReadPixels(new Rect(0, 0, ProbeLamps * 2, 1), 0, 0, false);

                int right = 0, mirrored = 0;
                for (int i = 0; i < Mathf.Min(lamps.Count, ProbeLamps); i++)
                {
                    float atLamp = pixels.GetPixel(2 * i, 0).r;
                    float atMirror = pixels.GetPixel(2 * i + 1, 0).r;
                    if (atLamp > atMirror * 1.5f && atLamp > 0.02f)
                        right++;
                    else if (atMirror > atLamp * 1.5f && atMirror > 0.02f)
                        mirrored++;
                }

                if (right + mirrored < 3)
                    return;

                _orientationKnown = true;
                if (mirrored > right)
                {
                    _flip = -_flip;
                    Log.Msg("fog lamps: the map was drawn MIRRORED (" + mirrored + " lamps bright at their mirror image, " + right +
                            " where they are); corrected, flip=" + _flip);
                }
                else
                {
                    Log.Msg("fog lamps: map orientation verified (" + right + " lamps bright where they are, " + mirrored +
                            " at their mirror image), flip=" + _flip);
                }
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(probe);
                Destroy(pixels);
            }
        }

        /// <summary>Lamp positions from the light meshes nearest the camera (they are readable: probed).</summary>
        private List<Vector3> LampsNearCentre()
        {
            var result = new List<Vector3>();
            Vector3 eye = _camera.transform.position;
            RenderManager render = Singleton<RenderManager>.instance;
            LightSystem lights = render.lightSystem;
            RenderGroup[] groups = render.m_groups;

            for (int i = 0; lights != null && groups != null && i < groups.Length && result.Count < ProbeLamps; i++)
            {
                RenderGroup group = groups[i];
                if (group == null)
                    continue;

                float cx = (group.m_x + 0.5f) * GroupCell - MapHalf;
                float cz = (group.m_z + 0.5f) * GroupCell - MapHalf;
                if (Mathf.Abs(cx - eye.x) > Size * 0.3f || Mathf.Abs(cz - eye.z) > Size * 0.3f)
                    continue;

                for (RenderGroup.MeshLayer layer = group.m_layers; layer != null && result.Count < ProbeLamps; layer = layer.m_nextLayer)
                {
                    if (layer.m_layer != lights.m_lightLayer || layer.m_mesh == null)
                        continue;

                    Mesh mesh = layer.m_mesh.m_mesh;
                    if (mesh == null || !mesh.isReadable)
                        continue;

                    Vector3[] normals = mesh.normals;
                    for (int v = 0; v < normals.Length && result.Count < ProbeLamps; v += 24)
                    {
                        // Far enough from the centre line that the mirror image is somewhere else.
                        if (Mathf.Abs(normals[v].z - eye.z) > 60f)
                            result.Add(normals[v]);
                    }
                }
            }

            return result;
        }

        private static float SunElevation()
        {
            DayNightProperties properties = DayNightProperties.instance;
            if (properties == null || properties.m_SunLight == null)
                return -90f;

            return -Mathf.Asin(Mathf.Clamp(properties.m_SunLight.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private void OnDestroy()
        {
            Collecting = false;
            Pending.Clear();
            Shader.SetGlobalVector(IdVCLampParams, Vector4.zero);
            Release();
        }
    }
}
