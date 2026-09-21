using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// WHERE it rains and how the rain falls. The rain curtains, the streaks near the camera,
    /// the rain sound and the wet roads all take it from here, so they agree.
    /// </summary>
    /// <remarks>
    /// The game's rain is one number for the whole map. Ours is that number times a mask cut
    /// from the same weather field as the clouds: it rains where the field clears the
    /// threshold for a SMALLER coverage than the clouds' -- the thickest part of the cover.
    /// The threshold table is solved ("40% means 40% of the sky"), so this is exact: rain
    /// falls under <see cref="RainCoverage"/> of the sky, always beneath cloud. SampleRain in
    /// CloudCommon.cginc and <see cref="LocalRain"/> here are the same function twice.
    ///
    /// Advanced from the one place that advances the wind and the cover (CloudLighting).
    /// <see cref="LocalRain"/> is called from the SIMULATION thread (road wetness) as well as
    /// the main one, so everything it needs is published as one immutable snapshot and
    /// swapped atomically; it never touches Unity objects or settings.
    /// </remarks>
    public static class CloudRain
    {
        /// <summary>Rain at or above this falls under ALL the cloud; below it, under the thickest part.</summary>
        private const float RainForFullSpread = 0.5f;

        /// <summary>Terminal velocity of a raindrop, m/s.</summary>
        private const float FallSpeed = 10f;

        /// <summary>
        /// Horizontal size of the rain curtains' noise pattern, and how much taller than wide
        /// it is stretched. CloudRaymarch slides that pattern down by the distance fallen.
        /// </summary>
        public const float CurtainScale = 420f;
        public const float CurtainStretch = 6f;

        /// <summary>
        /// The accumulated fall is wrapped to stay precise in a float. For the streaks a wrap
        /// is one reshuffle of random rain every several minutes: invisible. For the curtains
        /// it would be a visible jump of the pattern -- unless the wrap is a whole number of
        /// pattern periods, which is why it is derived from them.
        /// </summary>
        private const float WrapDistance = CurtainScale * CurtainStretch * 2f;

        private sealed class Snapshot
        {
            public CloudDensityField Field;
            public float Tile;
            public float PhaseX;   // CloudWind.WeatherPhase(Tile): the shaders' _WeatherPhase
            public float PhaseZ;
            public float SlantX;
            public float SlantZ;
            public float CloudBottom;
            public float RainCoverage;
        }

        private static volatile Snapshot _snapshot;
        private static readonly AmountReporter Reporter = new AmountReporter();

        /// <summary>True while our rain replaces the game's.</summary>
        public static bool Active { get; private set; }

        /// <summary>True on winter maps, where the game's rain is snow and ours stands down.</summary>
        public static bool IsSnow { get; private set; }

        /// <summary>The game's rain, 0..1, or 0 when our rain is off.</summary>
        public static float Amount { get; private set; }

        /// <summary>The fraction of the sky it is raining under.</summary>
        public static float RainCoverage { get; private set; }

        /// <summary>Horizontal metres a drop drifts downwind per metre it falls.</summary>
        public static Vector3 Slant { get; private set; }

        /// <summary>Unit vector the rain falls along.</summary>
        public static Vector3 FallDirection { get; private set; }

        /// <summary>How far the rain has fallen so far, wrapped. Frozen while the game is paused.</summary>
        public static Vector3 FallOffset { get; private set; }

        static CloudRain()
        {
            FallDirection = Vector3.down;
        }

        /// <summary>Once per frame, from CloudLighting. <paramref name="simulationRate"/> is 0 while paused.</summary>
        public static void Advance(CloudDensityField field, bool shadersAvailable, float deltaTime, float simulationRate)
        {
            bool wanted = Settings.RainEnabled == null || Settings.RainEnabled.value;
            bool cloudsShowing = Settings.CloudsVisible == null || Settings.CloudsVisible.value;

            // On winter maps the game's "rain" is snow. Fast grey streaks are not snowflakes,
            // so until there is a snow look of our own the game keeps its snow.
            IsSnow = Singleton<WeatherManager>.exists
                  && Singleton<WeatherManager>.instance.m_properties != null
                  && Singleton<WeatherManager>.instance.m_properties.m_rainIsSnow;

            Active = wanted && cloudsShowing && shadersAvailable && field != null && !IsSnow;
            Amount = Active ? CloudWeather.Rain : 0f;

            float spread = Mathf.Pow(Mathf.Clamp01(Amount / RainForFullSpread), 0.75f);
            RainCoverage = CloudWeather.Coverage * spread;

            if (Reporter.Moved(Amount))
                Report();

            // Rain leans with the wind, and leans harder in a downpour.
            Slant = CloudWeather.WindDirection * (0.2f + 0.25f * Amount);
            Vector3 velocity = new Vector3(Slant.x, -1f, Slant.z);
            FallDirection = velocity.normalized;

            // Faster with the simulation, like everything else, but 3x rain is just noise.
            Vector3 fallen = FallOffset + velocity * (FallSpeed * deltaTime * Mathf.Min(simulationRate, 2f));
            FallOffset = new Vector3(Mathf.Repeat(fallen.x, WrapDistance),
                                     -Mathf.Repeat(-fallen.y, WrapDistance),
                                     Mathf.Repeat(fallen.z, WrapDistance));

            bool localised = Active && Amount > 0f
                          && (Settings.RainLocalised == null || Settings.RainLocalised.value);

            float tile = Mathf.Max(500f, Value(Settings.WeatherTileSize, Settings.Defaults.WeatherTileSize));
            Vector2 phase = CloudWind.WeatherPhase(tile);

            _snapshot = !localised ? null : new Snapshot
            {
                Field = field,
                Tile = tile,
                PhaseX = phase.x,
                PhaseZ = phase.y,
                SlantX = Slant.x,
                SlantZ = Slant.z,
                CloudBottom = Value(Settings.CloudAltitude, Settings.Defaults.Altitude),
                RainCoverage = RainCoverage,
            };
        }

        /// <summary>
        /// The share of the game's rain that reaches <paramref name="position"/>: 0 in a
        /// clearing, 1 under a raining cloud, and always 1 while localisation is off. The C#
        /// twin of SampleRain in CloudCommon.cginc. Safe to call from any thread.
        /// </summary>
        public static float LocalRain(Vector3 position)
        {
            Snapshot s = _snapshot;
            if (s == null)
                return 1f;

            float below = Mathf.Max(0f, s.CloudBottom - position.y);
            float x = position.x - s.SlantX * below;
            float z = position.z - s.SlantZ * below;

            return s.Field.SampleCloud(x / s.Tile - s.PhaseX, z / s.Tile - s.PhaseZ, s.RainCoverage);
        }

        /// <summary>
        /// Always on, a line per quarter the rain travels. It names the curtains and the fog on
        /// purpose: heavy rain under a near-total cover is a grey medium that takes half the view
        /// per kilometre, which from the ground reads as fog, and it was once reported as one.
        /// </summary>
        private static void Report()
        {
            if (Amount <= 0f)
            {
                Log.Msg("rain: OUR rain has stopped");
                return;
            }

            float curtains = Mathf.Max(0f, Value(Settings.RainCurtains, Settings.Defaults.RainCurtains));

            Log.Msg("rain: OUR rain is on screen at " + (Amount * 100f).ToString("F0") + "% (the game's rain), under " +
                    (RainCoverage * 100f).ToString("F0") + "% of the sky on the slider scale; curtains at " +
                    (curtains * 100f).ToString("F0") + "% -- in heavy rain they haze the distance like a fog" +
                    " | our volumetric fog: " + (!CloudFog.Enabled ? "switched off" : (CloudFog.Amount * 100f).ToString("F0") + "% of the map") +
                    ", the game's fog value: " + (CloudFog.GameFog * 100f).ToString("F0") + "%");
        }

        /// <summary>How far the rain had fallen when the city was saved (SkySaveData), or 0.</summary>
        public static void RestoreFall(Vector3 fallen)
        {
            FallOffset = new Vector3(Mathf.Repeat(fallen.x, WrapDistance),
                                     -Mathf.Repeat(-fallen.y, WrapDistance),
                                     Mathf.Repeat(fallen.z, WrapDistance));
        }

        /// <summary>On unload: the game's rain is everywhere again.</summary>
        public static void Clear()
        {
            _snapshot = null;
            Active = false;
            Amount = 0f;
            Reporter.Reset();
        }

        private static float Value(FloatSetting setting, float fallback)
        {
            return setting != null ? setting.value : fallback;
        }
    }
}
