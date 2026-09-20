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
    /// Deliberately not a simulation of light. Every lit building goes into
    /// <see cref="CityLightMap"/> (footprint, weighted up for height, on a coarse grid over
    /// the whole map, then blurred to the spread ground light has by the time it reaches a
    /// cloud base half a kilometre up) and the result is handed to the cloud pass as a
    /// texture. Downtown glows, suburbs glow less, farmland and wilderness stay dark, and the
    /// glow follows the city as it grows. The arithmetic lives in CityLightMap so it can be
    /// checked offline; this class only reads the game's buildings and owns the texture.
    ///
    /// Buildings are read on the main thread without locking, as the game's own renderers do.
    /// A building caught half-created contributes one wrong cell for one refresh.
    /// </remarks>
    public class CityLights : MonoBehaviour
    {
        public const float MapSize = CityLightMap.MapSize;

        private const float RefreshInterval = 20f;

        public static Texture2D Texture { get; private set; }

        private readonly CityLightMap _map = new CityLightMap();
        private readonly Color32[] _pixels = new Color32[CityLightMap.Resolution * CityLightMap.Resolution];
        private float _nextRefresh;
        private int _loggedCount = -1;

        private void Start()
        {
            Texture = new Texture2D(CityLightMap.Resolution, CityLightMap.Resolution, TextureFormat.ARGB32, false, true)
            {
                name = "VolumetricCloudsCityLights",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };

            _map.Clear();
            Upload(_map.Finish());
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

            _map.Clear();

            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

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

                // Footprint in m^2: a cell of the zoning grid is 8 m.
                Vector3 position = buildings[i].m_position;
                _map.Add(position.x, position.z, buildings[i].Width * buildings[i].Length * 64f, info.m_size.y);
            }

            float[] light = _map.Finish();
            Upload(light);

            // Refreshed every 20 s; only worth a log line when the city has actually changed.
            if (_loggedCount >= 0 && Mathf.Abs(_map.Count - _loggedCount) < 50)
                return;

            _loggedCount = _map.Count;

            float sum = 0f;
            float peak = 0f;
            int lit = 0;
            for (int i = 0; i < light.Length; i++)
            {
                sum += light[i];
                peak = Mathf.Max(peak, light[i]);
                if (light[i] > 0.05f)
                    lit++;
            }

            float cellKm2 = (MapSize / CityLightMap.Resolution) * (MapSize / CityLightMap.Resolution) / 1e6f;
            Log.Msg("city lights: " + _map.Count + " buildings; densest block " + _map.Brightest.ToString("F2") +
                    ", brightest glow " + peak.ToString("F2") + ", glowing area " + (lit * cellKm2).ToString("F1") +
                    " km2, map mean " + (sum / light.Length).ToString("F3"));
        }

        private void Upload(float[] light)
        {
            for (int i = 0; i < light.Length; i++)
            {
                byte v = (byte)(Mathf.Clamp01(light[i]) * 255f);
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
