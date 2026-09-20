using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// How much fog there is, how far it has drifted and how far it has turned over. Advanced
    /// from the one place that advances the wind, the cover and the rain (CloudLighting).
    /// </summary>
    /// <remarks>
    /// By default the amount follows the game's weather (WeatherManager.m_currentFog: 0, or
    /// 0.25..1 when the game rolls a foggy spell); the override is for fog on demand.
    ///
    /// The fog is drawn in the cloud pass (CloudRaymarch: FogAt / MarchFog) as a cloud layer
    /// lying on the ground, which it knows from <see cref="TerrainHeightMap"/>. "Amount" is
    /// the share of the map that has fog on it, through the same solved threshold table as
    /// the clouds' intensity: 30% is fog over about a third of the map, 100% is fog
    /// everywhere. How thick it is inside is a separate setting.
    ///
    /// History, because this took three attempts: a height-limited blanket, then soft
    /// kilometre-wide banks with an exponential falloff. Anthony called both "a coating the
    /// way RenderIt does". They were translucent, had no boundary and did not shade
    /// themselves -- the three things that make a cloud read as a volume.
    /// </remarks>
    public static class CloudFog
    {
        /// <summary>Real seconds for the fog to follow a change. Fog rolls in; it does not pop.</summary>
        private const float FollowSeconds = 5f;

        /// <summary>
        /// Extinction per metre inside the fog at 100% density: visibility of about 330 m, and
        /// from above a 90 m layer still passes around 60% of what is under it -- fog you see
        /// the city THROUGH, with body where it is thick. The third version shipped at 0.028
        /// (visibility 110 m, nearly opaque from above), which Anthony found "just looks like
        /// the clouds but at the terrain level"; that look is still there at ~300% on the
        /// slider. The first two were thinner again AND had no boundary or shading, which is
        /// what made them a coating: density was never the only thing wrong with them.
        /// </summary>
        public const float BaseExtinction = 0.009f;

        /// <summary>
        /// Width of the soft edge of a fog patch, in weather-field units. The shader has the
        /// same literal (FogAt: "/ 0.1"); the honest threshold below is solved for it.
        /// </summary>
        public const float CoverSoftness = 0.1f;

        /// <summary>
        /// Metres one tile of the weather field spans when it is read for fog. The clouds read
        /// it at 20 km; at 7 km the same pattern gives patches from a few hundred metres to a
        /// couple of kilometres across.
        /// </summary>
        public const float Tile = 7000f;

        /// <summary>Metres per second the fog drifts at 1x on the slider, before simulation speed.</summary>
        private const float DriftSpeed = 6f;

        /// <summary>Seconds for the swirl to turn over once, at 1x.</summary>
        private const float BoilPeriod = 240f;

        private static float _amount;

        /// <summary>True while our fog replaces the game's foggy-weather look.</summary>
        public static bool Active { get; private set; }

        /// <summary>0..1, smoothed: the share of the map with fog on it.</summary>
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

        /// <summary>How far the fog has drifted. Its own offset: fog moves at its own pace, not the clouds'.</summary>
        public static Vector3 Offset { get; private set; }

        /// <summary>Phase of the swirl, 0..1, wrapping. The shader scrolls its noise by whole multiples of it.</summary>
        public static float Boil { get; private set; }

        public static void Advance(float deltaTime, bool cloudPassRunning, Vector3 windDirection, float simulationRate)
        {
            // Frozen while paused, like the clouds; faster with the simulation, but capped --
            // fog racing across the map at 3x reads as smoke.
            float speed = Settings.FogSpeed != null ? Mathf.Max(0f, Settings.FogSpeed.value) : 1f;
            float moved = deltaTime * Mathf.Min(simulationRate, 2f) * speed;
            Offset += windDirection * (DriftSpeed * moved);
            Boil = Mathf.Repeat(Boil + moved / BoilPeriod, 1f);

            bool wanted = (Settings.FogEnabled == null || Settings.FogEnabled.value)
                       && (Settings.CloudsVisible == null || Settings.CloudsVisible.value);

            // The fog is drawn by the volumetric cloud pass and lies on the terrain map;
            // without either, the game keeps its own.
            Active = wanted && cloudPassRunning && TerrainHeightMap.Ready;

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
        }

        public static void Reset()
        {
            _amount = 0f;
            Active = false;
        }

        /// <summary>One line for the panel and the log.</summary>
        public static string Describe()
        {
            if (!Active)
                return TerrainHeightMap.Ready ? "Volumetric fog is off" : "Volumetric fog: measuring the terrain...";

            return (Overridden ? "Override on (game fog " : "Game fog ") + (GameFog * 100f).ToString("F0") + "%" +
                   (Overridden ? ")" : "") + "  ->  fog over " + (_amount * 100f).ToString("F0") + "% of the map";
        }
    }
}
