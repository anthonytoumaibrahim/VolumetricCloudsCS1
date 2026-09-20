using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Renders real cloud shadows: a texture where each texel is one sun ray marched through
    /// the cloud volume, assigned as the sun's light cookie.
    /// </summary>
    /// <remarks>
    /// Unity projects a directional cookie in the light's local XY plane, centred on the
    /// light's position and repeating every cookieSize metres. So the light is pinned to
    /// <see cref="Anchor"/> and the cookie made large enough to cover the whole map in a
    /// single tile -- the pattern isn't periodic in light space, so a visible repeat would
    /// be a seam.
    /// </remarks>
    public class CloudShadowMap
    {
        /// <summary>Where the sun's transform is pinned while the shadow map is in use (map centre).</summary>
        public static readonly Vector3 Anchor = Vector3.zero;

        /// <summary>
        /// The map is 17.28 km square, so its corners are 12.2 km from the centre; one tile
        /// has to reach them at any sun azimuth.
        /// </summary>
        public const float CookieSize = 26000f;

        private const int Resolution = 2048;
        private const float UpdateInterval = 1f / 20f;
        private const float Steps = 20f;

        private static readonly int IdLightPos = Shader.PropertyToID("_LightPos");
        private static readonly int IdLightRight = Shader.PropertyToID("_LightRight");
        private static readonly int IdLightUp = Shader.PropertyToID("_LightUp");
        private static readonly int IdLightForward = Shader.PropertyToID("_LightForward");
        private static readonly int IdCookieSize = Shader.PropertyToID("_CookieSize");
        private static readonly int IdShadowDepth = Shader.PropertyToID("_ShadowDepth");
        private static readonly int IdShadowFullness = Shader.PropertyToID("_ShadowFullness");
        private static readonly int IdShadowSteps = Shader.PropertyToID("_ShadowSteps");

        /// <summary>The live shadow map, if the shader was available. Read by CloudLighting.</summary>
        public static CloudShadowMap Current { get; private set; }

        private const float ProbeInterval = 20f;

        private readonly Material _material;
        private RenderTexture _texture;
        private Texture2D _probeTexture;
        private float _nextUpdate;
        private float _nextProbe;

        public RenderTexture Texture
        {
            get { return _texture; }
        }

        /// <summary>True once there is real content to project.</summary>
        public bool IsReady { get; private set; }

        private CloudShadowMap(Shader shader)
        {
            _material = new Material(shader) { name = "VolumetricCloudsShadowMap" };
        }

        public static CloudShadowMap TryCreate()
        {
            Shader shader = ShaderBundle.Get(ShaderBundle.ShadowMap);
            if (shader == null)
                return null;

            CloudShadowMap map = new CloudShadowMap(shader);
            Current = map;
            Log.Msg("cloud shadow map created: " + Resolution + "x" + Resolution + " over " +
                    CookieSize + " m (" + (CookieSize / Resolution).ToString("F1") + " m/texel)");
            return map;
        }

        /// <summary>
        /// How much of the direct light a thick cloud removes, for a given coverage.
        /// </summary>
        /// <remarks>
        /// The shadow map is the *only* thing that darkens the world; there is no separate
        /// global sun dimming. Gaps between clouds get full sun, ground under thick cloud
        /// gets (1 - darkness), and the world darkens with coverage simply because more of
        /// it is in shade.
        ///
        /// Darkness is deliberately the same at every coverage. Earlier versions blended it
        /// towards an "overcast floor" as coverage rose; at the ~97% Anthony actually plays
        /// at, that made the strength slider 3% of the result and it felt disconnected.
        /// Before that, a global dim was balanced against the cookie and erased the shadows
        /// at high coverage altogether.
        /// </remarks>
        public static float ShadowDepth(float coverage)
        {
            return Settings.CloudShadowDarkness != null
                ? Mathf.Clamp01(Settings.CloudShadowDarkness.value)
                : 0.55f;
        }

        /// <summary>Multiplier on optical depth for shadows only; see CloudShadowMap.shader.</summary>
        public static float ShadowFullness
        {
            get
            {
                return Settings.CloudShadowFullness != null
                    ? Mathf.Max(0.01f, Settings.CloudShadowFullness.value)
                    : 2.5f;
            }
        }

        public void Render(Light sun, CloudDensityField field, Texture3D noise)
        {
            if (sun == null || field == null || noise == null)
                return;

            if (Time.time < _nextUpdate && IsReady)
                return;

            _nextUpdate = Time.time + UpdateInterval;

            if (!EnsureTexture())
                return;

            Transform t = sun.transform;

            CloudShaderParams.Apply(_material, field, noise);
            _material.SetVector(IdLightPos, Anchor);
            _material.SetVector(IdLightRight, t.right);
            _material.SetVector(IdLightUp, t.up);
            _material.SetVector(IdLightForward, t.forward);
            _material.SetFloat(IdCookieSize, CookieSize);
            _material.SetFloat(IdShadowDepth, ShadowDepth(CloudShaderParams.Coverage));
            _material.SetFloat(IdShadowFullness, ShadowFullness);
            _material.SetFloat(IdShadowSteps, Steps);

            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(null, _texture, _material);
            RenderTexture.active = previous;

            if (!IsReady)
                _nextProbe = Time.time + 3f;

            IsReady = true;

            if (Time.time >= _nextProbe)
            {
                _nextProbe = Time.time + ProbeInterval;
                Probe();
            }
        }

        /// <summary>
        /// Reads a downsampled copy of the map back and logs what is in it. "Shadows are
        /// faint" and "the pass is rendering plain white" look identical on screen, and this
        /// tells them apart: the mean should track 1 - coverage * depth.
        /// </summary>
        private void Probe()
        {
            const int size = 128;

            RenderTexture small = RenderTexture.GetTemporary(size, size, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;

            try
            {
                Graphics.Blit(_texture, small);

                if (_probeTexture == null)
                    _probeTexture = new Texture2D(size, size, TextureFormat.ARGB32, false);

                RenderTexture.active = small;
                _probeTexture.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);

                Color32[] pixels = _probeTexture.GetPixels32();
                int min = 255;
                int max = 0;
                long sum = 0;
                int shadowed = 0;

                for (int i = 0; i < pixels.Length; i++)
                {
                    int a = pixels[i].a;
                    if (a < min)
                        min = a;
                    if (a > max)
                        max = a;
                    if (a < 250)
                        shadowed++;
                    sum += a;
                }

                float coverage = CloudShaderParams.Coverage;
                float expected = 1f - coverage * ShadowDepth(coverage);

                Log.Msg("shadow map readback: light min=" + (min / 255f).ToString("F2") +
                        " max=" + (max / 255f).ToString("F2") +
                        " mean=" + (sum / 255f / pixels.Length).ToString("F2") +
                        " (expected mean ~" + expected.ToString("F2") + ")" +
                        " shadowedTexels=" + (100f * shadowed / pixels.Length).ToString("F0") + "%");
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(small);
            }
        }

        /// <summary>Render textures are lost on a device reset (alt-tab, resolution change).</summary>
        private bool EnsureTexture()
        {
            if (_texture != null && _texture.IsCreated())
                return true;

            if (_texture == null)
            {
                _texture = new RenderTexture(Resolution, Resolution, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "VolumetricCloudsShadowMap",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    useMipMap = false,
                    anisoLevel = 0,
                };
            }

            IsReady = false;
            return _texture.Create();
        }

        public void Dispose()
        {
            if (Current == this)
                Current = null;

            IsReady = false;

            if (_texture != null)
            {
                _texture.Release();
                Object.Destroy(_texture);
                _texture = null;
            }

            if (_material != null)
                Object.Destroy(_material);

            if (_probeTexture != null)
                Object.Destroy(_probeTexture);
        }
    }
}
