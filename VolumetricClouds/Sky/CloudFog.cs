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
    /// kilometre-wide banks with an exponential falloff. Both were called "a coating the
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
        /// (visibility 110 m, nearly opaque from above), which was judged to "just look like
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
        private static readonly AmountReporter Reporter = new AmountReporter();

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

        /// <summary>
        /// True: heights are measured from the ground under the fog, so it drapes over hills.
        /// False (the default): from the map's sea level, so it is level, like the game's.
        /// </summary>
        public static bool FollowsGround
        {
            get { return Settings.FogFollowsGround != null ? Settings.FogFollowsGround.value : Settings.Defaults.FogFollowsGround; }
        }

        /// <summary>Metres from the reference to the underside of the layer. 0 = it starts at the reference.</summary>
        public static float Base
        {
            get { return Settings.FogBase != null ? Mathf.Max(0f, Settings.FogBase.value) : Settings.Defaults.FogBase; }
        }

        /// <summary>Metres from the underside of the layer to its top.</summary>
        public static float Height
        {
            get { return Settings.FogHeight != null ? Mathf.Max(10f, Settings.FogHeight.value) : Settings.Defaults.FogHeight; }
        }

        /// <summary>
        /// Whether a point this far above the ground, at this altitude, is between the layer's
        /// underside and its top. The height test only, not the noise field.
        /// </summary>
        public static bool InLayer(float aboveGround, float altitude)
        {
            float above = FollowsGround ? aboveGround : altitude - TerrainHeightMap.SeaLevel;
            return above >= Base && above < Base + Height;
        }

        /// <summary>
        /// Where the layer is, for the log. A level fog whose range is under the city is the
        /// one way "the fog is on and I see none" can be nobody's bug, so it is spelled out.
        /// </summary>
        public static string DescribeLayer()
        {
            if (FollowsGround)
                return "follows the ground, " + Base.ToString("F0") + ".." + (Base + Height).ToString("F0") + " m above it";

            float sea = TerrainHeightMap.SeaLevel;
            return "level, " + Base.ToString("F0") + ".." + (Base + Height).ToString("F0") + " m above sea level (altitude " +
                   (sea + Base).ToString("F0") + ".." + (sea + Base + Height).ToString("F0") + " m)";
        }

        /// <summary>How far the fog has drifted. Its own offset: fog moves at its own pace, not the clouds'.</summary>
        public static Vector3 Offset { get; private set; }

        /// <summary>Phase of the swirl, 0..1, wrapping. The shader scrolls its noise by whole multiples of it.</summary>
        public static float Boil { get; private set; }

        /// <summary>
        /// The player has asked for volumetric fog. Off by default -- it is the heaviest thing
        /// the mod draws -- and when it is off nothing here runs at all, including the terrain
        /// height map that exists only to carry it.
        /// </summary>
        public static bool Enabled
        {
            get
            {
                return (Settings.FogEnabled != null && Settings.FogEnabled.value)
                    && (Settings.CloudsVisible == null || Settings.CloudsVisible.value);
            }
        }

        public static void Advance(float deltaTime, bool cloudPassRunning, Vector3 windDirection, float simulationRate)
        {
            bool wanted = Enabled;

            // The fog is drawn by the volumetric cloud pass and lies on the terrain map;
            // without either, the game keeps its own.
            Active = wanted && cloudPassRunning && TerrainHeightMap.Ready;

            if (!wanted)
            {
                // Everything below this line is work the fog does not need done while it is
                // switched off -- but Active and the amount are set FIRST and always: GameFog
                // keys on Active, so returning above it would leave the game's own
                // foggy-weather reaction switched off for ever with nothing of ours in its place.
                _amount = 0f;
                Report();
                return;
            }

            // Frozen while paused, like the clouds; faster with the simulation, but capped --
            // fog racing across the map at 3x reads as smoke.
            float speed = Settings.FogSpeed != null ? Mathf.Max(0f, Settings.FogSpeed.value) : Settings.Defaults.FogSpeed;
            float moved = deltaTime * Mathf.Min(simulationRate, 2f) * speed;
            Offset += windDirection * (DriftSpeed * moved);
            Boil = Mathf.Repeat(Boil + moved / BoilPeriod, 1f);

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

            Report();
        }

        /// <summary>
        /// Always on, a line per quarter the fog travels: whether OUR fog is on screen has to be
        /// in a quiet log. The game's fog never reacts to rain and ours follows only the game's
        /// FOG value, so "is that fog ours?" is answered by whether these lines are there.
        /// </summary>
        private static void Report()
        {
            if (!Reporter.Moved(_amount))
                return;

            if (_amount <= 0f)
            {
                Log.Msg("fog: OUR volumetric fog has gone" + (Enabled ? "" : " (it is switched off)"));
                return;
            }

            Log.Msg("fog: OUR volumetric fog is on screen, over " + (_amount * 100f).ToString("F0") + "% of the map -- " +
                    (Overridden
                        ? "the fog override asks for " + ((Settings.FogAmount != null ? Mathf.Clamp01(Settings.FogAmount.value) : 0f) * 100f).ToString("F0") + "%"
                        : "following the game's fog, which is at " + (GameFog * 100f).ToString("F0") + "%") +
                    " | " + DescribeLayer() +
                    " | the game's rain is at " + (CloudWeather.Rain * 100f).ToString("F0") + "%");
        }

        public static void Reset()
        {
            _amount = 0f;
            Active = false;
            Reporter.Reset();
        }

        /// <summary>One line for the panel and the log.</summary>
        public static string Describe()
        {
            // The setting first: with the fog switched off the terrain map is never built, so
            // asking the map first would tell every fog-off player it was "measuring the
            // terrain" for ever.
            if (!Enabled)
                return Localization.Get("Status.Fog.Off");

            if (!Active)
                return Localization.Get(TerrainHeightMap.Ready ? "Status.Fog.Waiting" : "Status.Fog.Measuring");

            string share = Localization.Get("Status.Fog.Share", Percent(_amount));

            return Overridden
                ? Localization.Get("Status.Fog.Manual", share, Percent(GameFog))
                : Localization.Get("Status.Fog.Following", Percent(GameFog), share);
        }

        private static string Percent(float fraction)
        {
            return Localization.Get("Unit.Percent", (fraction * 100f).ToString("F0"));
        }
    }
}
