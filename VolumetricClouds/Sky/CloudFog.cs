using System;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// How much fog there is and where it lies. Advanced from the one place that advances the
    /// wind, the cover and the rain (CloudLighting).
    /// </summary>
    /// <remarks>
    /// By default the amount follows the game's weather (WeatherManager.m_currentFog: 0, or
    /// 0.25..1 when the game rolls a foggy spell, which it only does when no rain is due).
    /// Play It cannot set fog, so the override is also the only way to have fog on demand.
    ///
    /// The fog is drawn in the cloud pass (CloudRaymarch: MarchFog), not as a screen effect:
    /// it is a layer in the WORLD, lying on a level and thinning with height, so valleys fill
    /// and hills stand out of it. That level cannot be a constant -- maps differ by hundreds
    /// of metres -- so it is measured once per city from the terrain itself.
    /// </remarks>
    public static class CloudFog
    {
        /// <summary>Real seconds for the fog to follow a change. Fog rolls in; it does not pop.</summary>
        private const float FollowSeconds = 5f;

        /// <summary>
        /// Extinction per metre at the fog's base when the amount is 1 and the thickness
        /// slider is at 100%: visibility of roughly 400 m, a proper fog without hiding the
        /// city from the player entirely.
        /// </summary>
        public const float BaseExtinction = 0.0075f;

        private static float _amount;
        private static bool _levelMeasured;

        /// <summary>True while our fog replaces the game's foggy-weather look.</summary>
        public static bool Active { get; private set; }

        /// <summary>0..1, smoothed.</summary>
        public static float Amount
        {
            get { return _amount; }
        }

        /// <summary>The game's own fog value, 0..1.</summary>
        public static float GameFog
        {
            get { return Singleton<WeatherManager>.exists ? Mathf.Clamp01(Singleton<WeatherManager>.instance.m_currentFog) : 0f; }
        }

        public static bool Overridden
        {
            get { return Settings.FogOverride != null && Settings.FogOverride.value; }
        }

        /// <summary>World height the fog lies on, measured from the terrain.</summary>
        public static float BaseLevel { get; private set; }

        /// <summary>
        /// Metres one tile of the weather field spans when it is read for fog. The clouds read
        /// it at 20 km; at 9 km the same pattern gives banks a few hundred metres to a couple
        /// of kilometres across.
        /// </summary>
        public const float Tile = 9000f;

        /// <summary>Metres per second the banks drift at 1x on the slider, before simulation speed.</summary>
        private const float DriftSpeed = 6f;

        /// <summary>How far the fog has drifted. Its own offset: fog moves at its own pace, not the clouds'.</summary>
        public static Vector3 Offset { get; private set; }

        /// <summary>Slow vertical slide of the billow noise, in noise tiles: the fog turning over.</summary>
        public static float Boil { get; private set; }

        /// <summary>
        /// The share of the low ground the banks cover, for the threshold table. The amount is
        /// how much fog the weather (or the player) asks for: a little is a few banks in the
        /// hollows, a lot is fog nearly everywhere with the odd clearing -- never a sealed lid,
        /// which is what a screen-space fog already does.
        /// </summary>
        public static float Coverage
        {
            get { return Mathf.Lerp(0.12f, 0.88f, _amount); }
        }

        public static void Advance(float deltaTime, bool shaderAvailable, Vector3 windDirection, float simulationRate)
        {
            // Frozen while paused, like the clouds; faster with the simulation, but capped --
            // fog racing across the map at 3x reads as smoke.
            float speed = Settings.FogSpeed != null ? Mathf.Max(0f, Settings.FogSpeed.value) : 1f;
            float moved = deltaTime * Mathf.Min(simulationRate, 2f);
            Offset += windDirection * (DriftSpeed * speed * moved);
            Boil = Mathf.Repeat(Boil + moved * speed * 0.004f, 1f);

            bool wanted = (Settings.FogEnabled == null || Settings.FogEnabled.value)
                       && (Settings.CloudsVisible == null || Settings.CloudsVisible.value);

            // The fog lives in the volumetric cloud pass; without it there is nothing to draw
            // with, and the game must keep its own.
            Active = wanted && shaderAvailable;

            float target = 0f;
            if (Active)
            {
                target = Overridden
                    ? (Settings.FogAmount != null ? Mathf.Clamp01(Settings.FogAmount.value) : 0f)
                    : GameFog;
            }

            _amount = Mathf.Lerp(_amount, target, 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / FollowSeconds));
            if (Mathf.Abs(target - _amount) < 0.0005f)
                _amount = target;

            if (Active && !_levelMeasured)
                MeasureLevel();
        }

        public static void Reset()
        {
            _amount = 0f;
            _levelMeasured = false;
            Active = false;
        }

        /// <summary>
        /// Fog pools in the low ground, so it lies on the level a third of the way up the
        /// distribution of heights across the part of the map people build on -- water
        /// surface included, or a coastal map's seabed would drag the fog under the sea.
        /// </summary>
        private static void MeasureLevel()
        {
            if (!Singleton<TerrainManager>.exists)
                return;

            _levelMeasured = true;

            const int grid = 24;
            const float extent = 4800f;

            try
            {
                TerrainManager terrain = Singleton<TerrainManager>.instance;
                float[] heights = new float[grid * grid];

                for (int z = 0; z < grid; z++)
                {
                    for (int x = 0; x < grid; x++)
                    {
                        Vector3 position = new Vector3(
                            Mathf.Lerp(-extent, extent, (x + 0.5f) / grid), 0f,
                            Mathf.Lerp(-extent, extent, (z + 0.5f) / grid));

                        heights[z * grid + x] = terrain.SampleRawHeightSmoothWithWater(position, false, 0f);
                    }
                }

                Array.Sort(heights);
                BaseLevel = heights[heights.Length / 3];

                Log.Msg("fog: terrain heights " + heights[0].ToString("F0") + ".." + heights[heights.Length - 1].ToString("F0") +
                        " m (median " + heights[heights.Length / 2].ToString("F0") + "); fog lies on " + BaseLevel.ToString("F0") + " m");
            }
            catch (Exception e)
            {
                Log.Error("Measuring the terrain for the fog level threw; using 0.", e);
                BaseLevel = 0f;
            }
        }
    }
}
