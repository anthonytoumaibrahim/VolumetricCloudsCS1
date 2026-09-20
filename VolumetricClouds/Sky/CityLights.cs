using System;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// A small map of where the city's lights are, for the glow a city throws onto the clouds,
    /// the rain and the fog above it at night.
    /// </summary>
    /// <remarks>
    /// Deliberately not a simulation of light. Every building adds its footprint, weighted up
    /// for height, to a coarse grid over the whole map; the grid is blurred to roughly the
    /// spread ground light has by the time it reaches a cloud base half a kilometre up, and
    /// handed to the cloud pass as a texture. Downtown glows, suburbs glow faintly, farmland
    /// and wilderness stay dark, and the glow follows the city as it grows.
    ///
    /// Buildings are read on the main thread without locking, as the game's own renderers do.
    /// A building caught half-created contributes one wrong cell for one refresh.
    /// </remarks>
    public class CityLights : MonoBehaviour
    {
        /// <summary>The game's map, 9 x 9 tiles of 1920 m.</summary>
        public const float MapSize = 17280f;

        private const int Resolution = 128;
        private const float RefreshInterval = 20f;

        /// <summary>
        /// The weighted footprint, in m^2, at which a cell counts as fully lit. A cell is
        /// 135 m square (18 225 m^2); a dense block of towers reaches this, a suburb a fifth.
        /// </summary>
        private const float FullyLit = 14000f;

        /// <summary>Blur passes of a 5-tap box: three of them are close to a gaussian ~400 m wide.</summary>
        private const int BlurPasses = 3;

        public static Texture2D Texture { get; private set; }

        private readonly float[] _light = new float[Resolution * Resolution];
        private readonly float[] _scratch = new float[Resolution * Resolution];
        private readonly Color32[] _pixels = new Color32[Resolution * Resolution];
        private float _nextRefresh;
        private int _loggedCount = -1;

        private void Start()
        {
            Texture = new Texture2D(Resolution, Resolution, TextureFormat.ARGB32, false)
            {
                name = "VolumetricCloudsCityLights",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };

            Upload();
            _nextRefresh = Time.time + 3f;
        }

        private void Update()
        {
            if (Time.time < _nextRefresh)
                return;

            _nextRefresh = Time.time + RefreshInterval;

            // Nothing reads the map while the glow is off.
            if (Settings.CityGlow != null && Settings.CityGlow.value <= 0f)
                return;

            try
            {
                Rebuild();
            }
            catch (Exception e)
            {
                Log.Error("Rebuilding the city light map threw.", e);
            }
        }

        private void Rebuild()
        {
            if (!Singleton<BuildingManager>.exists)
                return;

            Array.Clear(_light, 0, _light.Length);

            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            int counted = 0;

            const Building.Flags dark = Building.Flags.Deleted | Building.Flags.Abandoned | Building.Flags.BurnedDown
                                      | Building.Flags.Collapsed | Building.Flags.Hidden | Building.Flags.Demolishing;

            for (int i = 1; i < buildings.Length; i++)
            {
                Building.Flags flags = buildings[i].m_flags;
                if ((flags & Building.Flags.Created) == 0 || (flags & dark) != 0)
                    continue;

                BuildingInfo info = buildings[i].Info;
                if (info == null)
                    continue;

                Vector3 position = buildings[i].m_position;
                int x = (int)((position.x / MapSize + 0.5f) * Resolution);
                int z = (int)((position.z / MapSize + 0.5f) * Resolution);
                if (x < 0 || z < 0 || x >= Resolution || z >= Resolution)
                    continue;

                // Footprint in m^2 (a cell of the zoning grid is 8 m), and a tower is many
                // floors of lit windows where a house is one.
                float footprint = buildings[i].Width * buildings[i].Length * 64f;
                float floors = 1f + Mathf.Clamp(info.m_size.y, 0f, 300f) / 25f;

                _light[z * Resolution + x] += footprint * floors;
                counted++;
            }

            float brightest = 0f;
            for (int i = 0; i < _light.Length; i++)
            {
                _light[i] = Mathf.Clamp01(_light[i] / FullyLit);
                brightest = Mathf.Max(brightest, _light[i]);
            }

            for (int pass = 0; pass < BlurPasses; pass++)
            {
                Blur(_light, _scratch, 1, 0);
                Blur(_scratch, _light, 0, 1);
            }

            Upload();

            // Refreshed every 20 s; only worth a log line when the city has actually changed.
            if (Mathf.Abs(counted - _loggedCount) < 50 && _loggedCount >= 0)
                return;

            _loggedCount = counted;

            float lit = 0f;
            float peak = 0f;
            for (int i = 0; i < _light.Length; i++)
            {
                lit += _light[i];
                peak = Mathf.Max(peak, _light[i]);
            }

            Log.Msg("city lights: " + counted + " buildings; brightest cell " + brightest.ToString("F2") +
                    " before blur, " + peak.ToString("F2") + " after; map mean " + (lit / _light.Length).ToString("F3"));
        }

        /// <summary>5-tap box blur along one axis, clamped at the edges.</summary>
        private static void Blur(float[] source, float[] target, int dx, int dz)
        {
            for (int z = 0; z < Resolution; z++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    float sum = 0f;
                    for (int k = -2; k <= 2; k++)
                    {
                        int sx = Mathf.Clamp(x + k * dx, 0, Resolution - 1);
                        int sz = Mathf.Clamp(z + k * dz, 0, Resolution - 1);
                        sum += source[sz * Resolution + sx];
                    }

                    target[z * Resolution + x] = sum * 0.2f;
                }
            }
        }

        private void Upload()
        {
            for (int i = 0; i < _light.Length; i++)
            {
                byte v = (byte)(Mathf.Clamp01(_light[i]) * 255f);
                _pixels[i] = new Color32(v, v, v, 255);
            }

            Texture.SetPixels32(_pixels);
            Texture.Apply(false);
        }

        private void OnDestroy()
        {
            if (Texture != null)
            {
                Destroy(Texture);
                Texture = null;
            }
        }
    }
}
