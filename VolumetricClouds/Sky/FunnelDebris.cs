using System.Collections.Generic;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// What a funnel throws up near the camera, and what it does to the water (1.5.0; the author:
    /// "the tornado/sprout should 'interact' with the ground around it, like by moving leaves around,
    /// sand, dust, etc. I know we'd have to fake this ... Maybe over water we can make the water
    /// splash"). Two draws per funnel, both FunnelDebris.shader:
    ///   FLECKS   one mesh of seeds the vertex shader flies on a spiral -- in along the ground, up
    ///            the funnel, flung out and falling -- as spray, sand, leaves, grit or snow by the
    ///            ground under it (GroundLook). Within <see cref="FleckReach"/> only, where they can be
    ///            seen; the skirt in the cloud pass carries it further out. Drawn AFTER the air (fog,
    ///            rain, the funnels), so they are not fogged as if they stood on the ground behind
    ///            them: each fades by the fog in front of it instead (FogInFront).
    ///   WATER    one quad on the water under it: a darker patch, turning foam streaks, rings spreading
    ///            out. Drawn just BEFORE the air, like the water it lies on: the fog and the funnel's
    ///            spray are laid over it.
    /// All visual: nothing here touches the game's water or anything else.
    /// </summary>
    public class FunnelDebris : MonoBehaviour
    {
        /// <summary>Four vertices a fleck.</summary>
        private const int FleckCount = 768;

        /// <summary>Metres from the camera the flecks are drawn within (faded out over the last third).</summary>
        public const float FleckReach = 1500f;

        /// <summary>Metres from the camera the water's patch is drawn within.</summary>
        public const float WaterReach = 9000f;

        private static readonly int IdPart = Shader.PropertyToID("_Part");
        private static readonly int IdGround = Shader.PropertyToID("_DebrisGround");
        private static readonly int IdSize = Shader.PropertyToID("_DebrisSize");
        private static readonly int IdColor = Shader.PropertyToID("_DebrisColor");
        private static readonly int IdLook = Shader.PropertyToID("_FleckLook");
        private static readonly int IdTime = Shader.PropertyToID("_DebrisTime");
        private static readonly int IdAmbient = Shader.PropertyToID("_DebrisAmbient");
        private static readonly int IdSun = Shader.PropertyToID("_DebrisSun");
        private static readonly int IdStem = Shader.PropertyToID("_DebrisStem");
        private static readonly int IdPixelAngle = Shader.PropertyToID("_PixelAngle");
        private static readonly int IdReach = Shader.PropertyToID("_DebrisReach");

        private CloudDensityField _field;
        private Material _flecks;
        private Material _water;
        private Mesh _fleckMesh;
        private Mesh _waterMesh;
        private MaterialPropertyBlock _block;
        private Camera _camera;
        private bool _loggedDraw;

        public void Initialise(CloudDensityField field)
        {
            _field = field;
        }

        private void Start()
        {
            Shader shader = ShaderBundle.Get(ShaderBundle.FunnelDebris);
            if (shader == null)
            {
                Log.Warn("waterspouts: the debris shader is missing from the bundle; they are drawn without flecks or water");
                enabled = false;
                return;
            }

            // The flecks just after the air, the water just before it (CloudVolume.AirQueue).
            _flecks = new Material(shader) { name = "VolumetricCloudsFunnelFlecks", renderQueue = CloudVolume.AirQueue + 1 };
            _flecks.SetFloat(IdPart, 0f);
            _water = new Material(shader) { name = "VolumetricCloudsFunnelWater", renderQueue = CloudVolume.AirQueue - 1 };
            _water.SetFloat(IdPart, 1f);

            _fleckMesh = BuildFlecks();
            _waterMesh = BuildQuad();
            _block = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (_flecks == null || Funnels.Count == 0)
                return;

            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return;

            Vector3 eye = _camera.transform.position;
            float pixelAngle = 2f * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, _camera.pixelHeight);
            float time = (float)(Funnels.SimTime % 1000.0);
            int flecks = 0, waters = 0;

            for (int i = 0; i < Funnels.Count; i++)
            {
                Funnel f = Funnels.Get(i);
                if (f.SkirtStrength <= 0.02f)
                    continue;

                float distance = (f.Ground - eye).magnitude;
                bool nearEnough = distance < FleckReach + f.SkirtRadius;
                bool water = f.Kind == GroundKind.Water && distance < WaterReach;
                if (!nearEnough && !water)
                    continue;

                // The cloud right above it shades it: lit by the sky, a little by the sun.
                float sunShare = 1f - 0.8f * CloudAbove(f.Ground);
                Color fleck = GroundLook.FleckColor(f.Kind);
                float fog = FogInFront(distance);

                _block.Clear();
                _block.SetVector(IdGround, new Vector4(f.Ground.x, f.Ground.y, f.Ground.z, f.SkirtRadius));
                _block.SetVector(IdSize, new Vector4(f.SkirtHeight, Mathf.Max(f.GroundRadius, 2f), f.SkirtStrength, f.Turning));
                _block.SetVector(IdColor, new Vector4(fleck.r, fleck.g, fleck.b, fog * GroundLook.FleckOpacity(f.Kind)));
                _block.SetVector(IdLook, GroundLook.FleckLook(f.Kind));
                _block.SetVector(IdTime, new Vector4(time, f.Spin, f.Seed, f.Opacity));
                _block.SetVector(IdAmbient, AsVector(Funnels.AirAmbient));
                _block.SetVector(IdSun, AsVector(Funnels.Sun) * sunShare);
                _block.SetVector(IdStem, new Vector4(f.Ground.x, f.Ground.z, f.GroundRadius, f.Top.y));
                _block.SetFloat(IdPixelAngle, pixelAngle);
                _block.SetVector(IdReach, new Vector4(FleckReach * 0.66f, FleckReach, 0f, 0f));

                if (nearEnough)
                {
                    Graphics.DrawMesh(_fleckMesh, Matrix4x4.identity, _flecks, 0, null, 0, _block);
                    flecks++;
                }

                if (water)
                {
                    Graphics.DrawMesh(_waterMesh, Matrix4x4.identity, _water, 0, null, 0, _block);
                    waters++;
                }
            }

            if (!_loggedDraw && (flecks > 0 || waters > 0))
            {
                _loggedDraw = true;
                Log.Msg("waterspouts: flecks and water drawn (first time: " + flecks + " fleck draw(s) at queue " + _flecks.renderQueue +
                        ", " + waters + " on the water at queue " + _water.renderQueue + ")");
            }
        }

        /// <summary>Cloud above a point, 0..1, as lightning's test reads it.</summary>
        private float CloudAbove(Vector3 position)
        {
            if (_field == null)
                return 1f;

            float tile = _field.Tile;
            Vector2 phase = CloudWind.WeatherPhase(tile);
            return _field.SampleCloud(position.x / tile - phase.x, position.z / tile - phase.y,
                                      CloudStyle.CloudTestCoverage(CloudWeather.Coverage));
        }

        /// <summary>
        /// How much of a fleck the fog between it and the camera leaves, roughly: the fog's extinction
        /// over half the way (the camera is mostly above the layer). 1 without fog.
        /// </summary>
        private static float FogInFront(float distance)
        {
            if (!CloudFog.Active)
                return 1f;

            float density = Mathf.Max(0f, Settings.Now(Settings.FogDensity, Settings.FogDensitySunset, Settings.FogDensityNight,
                Settings.Defaults.FogDensity)) * CloudFog.Look.Density;
            return Mathf.Exp(-CloudFog.BaseExtinction * density * CloudFog.Amount * distance * 0.5f);
        }

        private static Vector4 AsVector(Color c)
        {
            return new Vector4(c.r, c.g, c.b, 0f);
        }

        /// <summary>
        /// Seeds, not positions: POSITION and TEXCOORD1 random 0..1, TEXCOORD0 the corner. The shader
        /// makes every fleck's flight from them.
        /// </summary>
        private static Mesh BuildFlecks()
        {
            System.Random random = new System.Random(20261018);
            Vector3[] vertices = new Vector3[FleckCount * 4];
            Vector2[] corners = new Vector2[FleckCount * 4];
            List<Vector3> more = new List<Vector3>(FleckCount * 4);
            int[] triangles = new int[FleckCount * 6];

            for (int i = 0; i < FleckCount; i++)
            {
                Vector3 seed = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                Vector3 extra = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                int v = i * 4;
                for (int c = 0; c < 4; c++)
                {
                    vertices[v + c] = seed;
                    more.Add(extra);
                }

                corners[v + 0] = new Vector2(-1f, -1f);
                corners[v + 1] = new Vector2(1f, -1f);
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

            Mesh mesh = new Mesh { name = "VolumetricCloudsFunnelFlecks" };
            mesh.vertices = vertices;
            mesh.uv = corners;
            mesh.SetUVs(1, more);
            mesh.triangles = triangles;
            // The real positions only exist in the shader.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return mesh;
        }

        private static Mesh BuildQuad()
        {
            Mesh mesh = new Mesh { name = "VolumetricCloudsFunnelWater" };
            mesh.vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
            mesh.uv = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            mesh.SetUVs(1, new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero });
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return mesh;
        }

        private void OnDestroy()
        {
            if (_flecks != null)
                Destroy(_flecks);
            if (_water != null)
                Destroy(_water);
            if (_fleckMesh != null)
                Destroy(_fleckMesh);
            if (_waterMesh != null)
                Destroy(_waterMesh);
        }
    }
}
