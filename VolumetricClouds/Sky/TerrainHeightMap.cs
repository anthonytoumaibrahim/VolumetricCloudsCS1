using System;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The height of the ground (or the water on it) over the whole map, as a texture the fog
    /// shader can read.
    /// </summary>
    /// <remarks>
    /// The game publishes no terrain heightmap to shaders (checked: no SetGlobalTexture of
    /// one anywhere in Assembly-CSharp), and without the ground's height a shader can only
    /// put fog at one altitude -- a flat haze that fills the lowest valley and misses a city
    /// twenty metres higher. With it, fog is measured from the ground up everywhere: it lies
    /// on the terrain, flows over hills, sits on the water, and buildings stand out of it.
    ///
    /// 256 x 256 over 17.28 km is one sample per 67 m, bilinearly filtered: fog does not need
    /// more, and at 16 rows a frame the 65k terrain samples never cost a visible hitch. It is
    /// re-sampled every few minutes for terraforming and changing water levels.
    /// </remarks>
    public class TerrainHeightMap : MonoBehaviour
    {
        /// <summary>The game's map, 9 x 9 tiles of 1920 m.</summary>
        public const float MapSize = 17280f;

        private const int Resolution = 256;
        private const int RowsPerFrame = 16;
        private const float RefreshInterval = 180f;

        public static Texture2D Texture { get; private set; }

        /// <summary>True once the first full pass has been uploaded.</summary>
        public static bool Ready { get; private set; }

        public static float Lowest { get; private set; }
        public static float Highest { get; private set; }

        /// <summary>The level a third of the way up the distribution of heights: where fog pools.</summary>
        public static float PoolLevel { get; private set; }

        /// <summary>
        /// What a LEVEL fog is measured from: the height 2% of the way up the distribution.
        /// Water is in the map, so wherever there is a sea this IS sea level (any sea is far
        /// more than 2% of 17 km square); on a landlocked map it is the valley floor. Not world
        /// y = 0 -- that is under the seabed (the sea is usually at 40 m and the land starts
        /// above it), so the first stretch of every fog slider would do nothing -- and not
        /// <see cref="Lowest"/>, which one quarry pit would move.
        /// </summary>
        public static float SeaLevel { get; private set; }

        private readonly float[] _heights = new float[Resolution * Resolution];
        private readonly byte[] _raw = new byte[Resolution * Resolution * 4];
        private float[] _sorted;
        private int _row;
        private float _nextPass;
        private bool _failed;

        private void Start()
        {
            Ready = false;

            if (!SystemInfo.SupportsTextureFormat(TextureFormat.RFloat))
            {
                _failed = true;
                Log.Warn("terrain map: this GPU has no single-channel float textures; volumetric fog is unavailable.");
            }

            // The texture is NOT created here. This map exists only to carry the fog, and the
            // fog is off by default: "off" has to mean nothing runs, not "runs into a texture
            // nobody samples".
        }

        private void Update()
        {
            if (_failed || !Singleton<TerrainManager>.exists)
                return;

            if (!CloudFog.Enabled)
            {
                Release();
                return;
            }

            if (Texture == null)
                Create();

            if (Texture == null)
                return;

            if (_row >= Resolution)
            {
                if (Time.time < _nextPass)
                    return;

                _row = 0;
            }

            try
            {
                SampleRows();
            }
            catch (Exception e)
            {
                _failed = true;
                Log.Error("Sampling the terrain for the fog threw; volumetric fog is unavailable.", e);
            }
        }

        /// <summary>
        /// Builds the map from cold. Called the first frame the fog is wanted, which may be an
        /// hour into a session: the first full pass then takes 16 frames.
        /// </summary>
        private void Create()
        {
            Texture = new Texture2D(Resolution, Resolution, TextureFormat.RFloat, false, true)
            {
                name = "VolumetricCloudsTerrainHeight",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };

            _row = 0;
            _nextPass = 0f;
            Ready = false;
            Log.Msg("terrain map: building (volumetric fog is on)");
        }

        /// <summary>Drops the map when the fog is switched off. Costing nothing is the point of it.</summary>
        private void Release()
        {
            if (Texture == null)
                return;

            Destroy(Texture);
            Texture = null;
            Ready = false;
            _row = 0;
            Log.Msg("terrain map: released (volumetric fog is off)");
        }

        private void SampleRows()
        {
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            int last = Mathf.Min(_row + RowsPerFrame, Resolution);

            for (int z = _row; z < last; z++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    // Texel centres, matching uv = xz / MapSize + 0.5 in the shader.
                    Vector3 position = new Vector3(
                        ((x + 0.5f) / Resolution - 0.5f) * MapSize, 0f,
                        ((z + 0.5f) / Resolution - 0.5f) * MapSize);

                    // With water: fog sits ON a lake, not on its bed.
                    _heights[z * Resolution + x] = terrain.SampleRawHeightSmoothWithWater(position, false, 0f);
                }
            }

            _row = last;
            if (_row < Resolution)
                return;

            Buffer.BlockCopy(_heights, 0, _raw, 0, _raw.Length);
            Texture.LoadRawTextureData(_raw);
            Texture.Apply(false);

            if (_sorted == null)
                _sorted = new float[_heights.Length];
            Array.Copy(_heights, _sorted, _heights.Length);
            Array.Sort(_sorted);

            Lowest = _sorted[0];
            Highest = _sorted[_sorted.Length - 1];
            PoolLevel = _sorted[_sorted.Length / 3];
            SeaLevel = _sorted[_sorted.Length / 50];

            if (!Ready)
            {
                // Always logged, not a detail line: this is the frame the fog becomes possible,
                // and after a mid-game switch-on it is the answer to "why is there no fog yet".
                Log.Msg("terrain map: ready. " + Resolution + "x" + Resolution + " heights " + Lowest.ToString("F0") + ".." +
                        Highest.ToString("F0") + " m, median " + _sorted[_sorted.Length / 2].ToString("F0") +
                        " m; a level fog is measured from " + SeaLevel.ToString("F0") +
                        " m; a ground-following one pools below " + PoolLevel.ToString("F0") + " m");
            }

            Ready = true;
            _nextPass = Time.time + RefreshInterval;
        }

        private void OnDestroy()
        {
            Ready = false;

            if (Texture != null)
            {
                Destroy(Texture);
                Texture = null;
            }
        }
    }
}
