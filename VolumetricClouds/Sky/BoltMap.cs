using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Where the lightning bolts are on screen, for the cloud pass (1.5.0).
    /// </summary>
    /// <remarks>
    /// The bolt is drawn BEFORE the clouds, as in 1.2 (the author's choice, 2026-09-30), and the
    /// cloud pass lays every cloud on the line of sight over it. That is what fades its top into the
    /// cloud it comes out of -- and what cut it to pieces wherever a cloud far behind it showed
    /// through (2026-10-12: "the clouds behind are rendering on TOP of the lightning bolt"). 1.3.0
    /// drew the bolt after the clouds, dimmed by what is in front of it, and that looked "in front"
    /// of the cloud. So the bolt stays as it is, and each frame one shows, its core is drawn again
    /// into a screen-sized map of its distance from the camera (LightningBoltMap.shader). On those
    /// pixels the clouds, rain and fog stop <see cref="BehindShare"/> of the bolt's distance behind
    /// it (LightningBoltMap.cginc BoltStop). No bolt on screen: nothing is drawn and the cloud pass
    /// reads nothing.
    ///
    /// Drawn the way FogLampMap draws its map: a CommandBuffer run at once, with this frame's camera
    /// matrix. The cloud pass looks a ray up through that same matrix, at a point as far out as the
    /// nearest bolt, so a camera that moves after this (its LateUpdate may come later) still finds it.
    /// </remarks>
    public sealed class BoltMap
    {
        /// <summary>
        /// How far behind a bolt the clouds still count on its pixels, as a share of its distance. The
        /// cloud it comes out of keeps covering its top, as before; clouds farther back no longer cut
        /// it. 0 would be 1.3.0's look, which read as "in front of the cloud"; the old look is infinity.
        /// </summary>
        public const float BehindShare = 0.25f;

        /// <summary>Where no bolt is: above the shader's BOLT_MAP_NONE (60000), and a half float holds it.</summary>
        private const float Cleared = 65000f;

        /// <summary>Seconds without a bolt before the map is let go (16 MB at 4K).</summary>
        private const float IdleRelease = 30f;

        private const int ProbePoints = 8;
        private const int ProbeTries = 10;

        private static readonly int IdBoltMapGPUVP = Shader.PropertyToID("_BoltMapGPUVP");
        private static readonly int IdPixelAngle = Shader.PropertyToID("_PixelAngle");
        private static readonly int IdProbePoints = Shader.PropertyToID("_ProbePoints");
        private static readonly int IdVCBoltMap = Shader.PropertyToID("_VCBoltMap");
        private static readonly int IdVCBoltMapVP = Shader.PropertyToID("_VCBoltMapVP");
        private static readonly int IdVCBoltMapEye = Shader.PropertyToID("_VCBoltMapEye");
        private static readonly int IdVCBoltMapParams = Shader.PropertyToID("_VCBoltMapParams");

        private bool _failed;
        private Material _material;
        private CommandBuffer _buffer;
        private RenderTexture _map;
        private bool _on;
        private bool _createdOnce;
        private float _lastDrawn;
        private int _boltsDrawn;

        // The map's up, as the cloud pass reads it: +1 per Unity's convention for render textures,
        // turned over if the self-check finds the bolts at their mirror image.
        private float _up = 1f;
        private bool _orientationKnown;
        private int _probeTries;
        private float _nextProbe;

        public BoltMap()
        {
            // A global outlives the city: a map left on by the last one must not be read.
            Shader.SetGlobalVector(IdVCBoltMapParams, Vector4.zero);
        }

        /// <summary>For the lightning's detail line.</summary>
        public string Describe()
        {
            if (_failed)
                return "unavailable";
            if (_map == null)
                return "none";
            return (_on ? "on, " + _boltsDrawn + " bolt(s)" : "idle") + ", " + _map.width + "x" + _map.height +
                   ", orientation " + (_orientationKnown ? "verified" : "not yet verified") + " up=" + _up;
        }

        /// <summary>
        /// Draws this frame's bolts (the meshes LightningBolt.shader draws, in world space) into the map
        /// and hands it to the cloud pass; with none, the cloud pass stops reading it.
        /// </summary>
        public void Draw(Camera camera, List<Mesh> bolts, float pixelAngle)
        {
            if (bolts.Count == 0 || camera == null || !Create())
            {
                Off();
                return;
            }

            int width = Mathf.Max(1, camera.pixelWidth);
            int height = Mathf.Max(1, camera.pixelHeight);
            if (_map == null || _map.width != width || _map.height != height)
                CreateMap(width, height);

            Vector3 eye = camera.transform.position;
            Matrix4x4 view = camera.worldToCameraMatrix;
            Matrix4x4 projection = camera.projectionMatrix;

            // Looked up as far out as the nearest bolt (see the remarks).
            float nearest = float.MaxValue;
            foreach (Mesh bolt in bolts)
                nearest = Mathf.Min(nearest, Vector3.Distance(eye, bolt.bounds.center));

            // Read by the map's own shader too, so set before it is drawn.
            Shader.SetGlobalVector(IdVCBoltMapEye, new Vector4(eye.x, eye.y, eye.z, nearest));
            _material.SetMatrix(IdBoltMapGPUVP, GL.GetGPUProjectionMatrix(projection, true) * view);
            _material.SetFloat(IdPixelAngle, pixelAngle);

            _buffer.Clear();
            _buffer.SetRenderTarget(_map);
            _buffer.ClearRenderTarget(false, true, new Color(Cleared, 0f, 0f, 0f));
            foreach (Mesh bolt in bolts)
                _buffer.DrawMesh(bolt, Matrix4x4.identity, _material, 0, 0);
            Graphics.ExecuteCommandBuffer(_buffer);

            // Published in the same frame as the map is drawn: the cloud pass can never read it with
            // last frame's matrix.
            Shader.SetGlobalTexture(IdVCBoltMap, _map);
            Shader.SetGlobalMatrix(IdVCBoltMapVP, projection * view);
            Shader.SetGlobalVector(IdVCBoltMapParams, new Vector4(1f, 1f + BehindShare, _up, 0f));

            _on = true;
            _boltsDrawn = bolts.Count;
            _lastDrawn = Time.realtimeSinceStartup;

            if (!_orientationKnown && Time.realtimeSinceStartup >= _nextProbe)
                Check(camera, bolts[0]);
        }

        /// <summary>No bolt showing: the cloud pass reads nothing, and a map unused for a while goes.</summary>
        public void Off()
        {
            if (_on)
            {
                _on = false;
                Shader.SetGlobalVector(IdVCBoltMapParams, Vector4.zero);
            }

            if (_map != null && Time.realtimeSinceStartup - _lastDrawn > IdleRelease)
            {
                ReleaseMap();
                if (Log.Detailed)
                    Log.Detail("lightning: bolt map released (no bolt for " + IdleRelease.ToString("F0") + " s)");
            }
        }

        public void Dispose()
        {
            _on = false;
            Shader.SetGlobalVector(IdVCBoltMapParams, Vector4.zero);
            ReleaseMap();

            if (_buffer != null)
            {
                _buffer.Release();
                _buffer = null;
            }

            if (_material != null)
            {
                Object.Destroy(_material);
                _material = null;
            }
        }

        private bool Create()
        {
            if (_material != null)
                return true;
            if (_failed)
                return false;

            Shader shader = ShaderBundle.Get(ShaderBundle.LightningBoltMap);
            if (shader == null || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf))
            {
                _failed = true;
                Log.Warn("lightning: bolt map unavailable (" + (shader == null ? "shader missing" : "no half-float render textures") +
                         "); clouds behind a bolt cover it, as before 1.5.0.");
                return false;
            }

            _material = new Material(shader) { name = "VolumetricClouds LightningBoltMap" };
            _buffer = new CommandBuffer { name = "VolumetricClouds bolt map" };
            return true;
        }

        private void CreateMap(int width, int height)
        {
            ReleaseMap();

            _map = new RenderTexture(width, height, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
            {
                name = "VolumetricClouds BoltMap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                useMipMap = false,
            };
            _map.Create();

            string line = "lightning: bolt map " + width + "x" + height + " -- on a bolt's own pixels the clouds, rain and fog stop " +
                          (BehindShare * 100f).ToString("F0") + "% of its distance behind it, so a cloud farther back no longer cuts it";
            if (_createdOnce)
            {
                if (Log.Detailed)
                    Log.Detail(line);
            }
            else
            {
                _createdOnce = true;
                Log.Msg(line);
            }
        }

        private void ReleaseMap()
        {
            if (_map == null)
                return;

            _map.Release();
            Object.Destroy(_map);
            _map = null;
        }

        /// <summary>
        /// Once, at the first bolts: reads the map, the way the cloud pass does, at points on a bolt
        /// and at the same points mirrored top to bottom. If the mirror images are where the bolt is,
        /// the map is read upside down, and the reading is turned over. Either way the answer goes in
        /// the log: a wrong guess would show as a bolt still cut, and clouds missing beside it.
        /// </summary>
        private void Check(Camera camera, Mesh bolt)
        {
            _nextProbe = Time.realtimeSinceStartup + 1f;
            if (++_probeTries > ProbeTries)
            {
                _orientationKnown = true;
                Log.Warn("lightning: could not verify the bolt map's orientation (no bolt well up or down the screen in " +
                         ProbeTries + " tries); keeping up=" + _up);
                return;
            }

            // Middles of the bolt's segments (four vertices each: the start at 4k, the end at 4k + 2),
            // on screen and far enough from its middle row that the mirror image is somewhere else.
            Vector3[] vertices = bolt.vertices;
            int segments = vertices.Length / 4;
            var points = new Vector4[ProbePoints];
            int found = 0;

            for (int k = 0; k < segments && found < ProbePoints; k += Mathf.Max(1, segments / (ProbePoints * 4)))
            {
                Vector3 middle = (vertices[4 * k] + vertices[4 * k + 2]) * 0.5f;
                Vector3 seen = camera.WorldToViewportPoint(middle);
                if (seen.z <= 0f || seen.x < 0.02f || seen.x > 0.98f || seen.y < 0.02f || seen.y > 0.98f ||
                    Mathf.Abs(seen.y - 0.5f) < 0.1f)
                    continue;

                points[found++] = new Vector4(middle.x, middle.y, middle.z, 0f);
            }

            if (found < 3)
                return;

            var probe = RenderTexture.GetTemporary(ProbePoints * 2, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(ProbePoints * 2, 1, TextureFormat.RGBA32, false, true);
            RenderTexture previous = RenderTexture.active;
            try
            {
                _material.SetVectorArray(IdProbePoints, points);
                Graphics.Blit(_map, probe, _material, 1);
                RenderTexture.active = probe;
                pixels.ReadPixels(new Rect(0, 0, ProbePoints * 2, 1), 0, 0, false);

                int right = 0, mirrored = 0;
                for (int i = 0; i < found; i++)
                {
                    bool atPoint = pixels.GetPixel(2 * i, 0).r > 0.5f;
                    bool atMirror = pixels.GetPixel(2 * i + 1, 0).r > 0.5f;
                    if (atPoint && !atMirror)
                        right++;
                    else if (atMirror && !atPoint)
                        mirrored++;
                }

                if (right + mirrored < 3)
                {
                    if (Log.Detailed)
                        Log.Detail("lightning: bolt map check inconclusive (" + found + " points: " + right + " found where they are, " +
                               mirrored + " at their mirror image); trying again with the next bolt");
                    return;
                }

                _orientationKnown = true;
                if (mirrored > right)
                {
                    _up = -_up;
                    Shader.SetGlobalVector(IdVCBoltMapParams, new Vector4(1f, 1f + BehindShare, _up, 0f));
                    Log.Msg("lightning: the bolt map was read UPSIDE DOWN (" + mirrored + " points on a bolt found at their mirror image, " +
                            right + " where they are); corrected, up=" + _up);
                }
                else
                {
                    Log.Msg("lightning: bolt map lines up with the screen (" + right + " points on a bolt found where they are, " +
                            mirrored + " at their mirror image), up=" + _up);
                }
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(probe);
                Object.Destroy(pixels);
            }
        }
    }
}
