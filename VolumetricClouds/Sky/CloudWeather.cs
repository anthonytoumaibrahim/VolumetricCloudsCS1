using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// THE cloud cover. The visible clouds, the shadow map and the fallback cookie all read
    /// <see cref="Coverage"/>, and it is advanced in exactly one place (CloudLighting), like
    /// the wind.
    /// </summary>
    /// <remarks>
    /// By default the cover follows the game's weather, which is read and never written, so
    /// random weather, Play It's rain slider and the thunderstorm and tornado disasters all
    /// work: they just set the same two public fields.
    ///   WeatherManager.m_currentRain   0..1
    ///   WeatherManager.m_currentCloud  0, or 0.25..1 when the game rolls a cloudy spell;
    ///                                  it creeps towards its target by 0.0008 a sim step
    /// The game turns those into its own dome's coverage with max(rain * 4, fog * 0.5, cloud,
    /// wetness * 0.5). Fog and wetness are left out here on purpose: PersistentFogAdjuster
    /// parks fog at a constant, and wet ground long after a shower is not an overcast sky.
    ///
    /// The Intensity slider is the override, for what the game cannot express -- a cloudy day
    /// with no rain on demand. It is off by default.
    /// </remarks>
    public static class CloudWeather
    {
        /// <summary>
        /// Rain at or above this means a fully overcast sky -- the WHOLE of the game's range,
        /// because Play It's rain slider is a slider and a slider that does nothing over its
        /// top half is a bug. It used to be 0.3 (near the game's own dome, which saturates at
        /// rain * 4 = 0.25), and everything from 30% up was one flat sky: rain set to 50% and
        /// then to 100% gave no difference at all.
        /// The shape below is what keeps light rain honest at the same time.
        /// </summary>
        private const float RainForFullOvercast = 1f;

        /// <summary>
        /// Time constants, in real seconds, for the cover to reach a new target. Play It can
        /// move rain from 0 to 1 in one frame, and a sky that fills in instantly looks like a
        /// texture swap. The manual slider only needs the pop taken off.
        /// </summary>
        private const float FollowSeconds = 3f;
        private const float ManualSeconds = 0.25f;

        private static float _coverage;
        private static bool _started;

        /// <summary>The cover everything renders with, 0..1, smoothed.</summary>
        public static float Coverage
        {
            get { return _started ? _coverage : Target(); }
        }

        /// <summary>True when the Intensity slider is in charge rather than the weather.</summary>
        public static bool Manual
        {
            get { return Settings.CoverageOverride != null && Settings.CoverageOverride.value; }
        }

        public static float Rain
        {
            get { return Singleton<WeatherManager>.exists ? Mathf.Clamp01(Singleton<WeatherManager>.instance.m_currentRain) : 0f; }
        }

        public static float GameCloud
        {
            get { return Singleton<WeatherManager>.exists ? Mathf.Clamp01(Singleton<WeatherManager>.instance.m_currentCloud) : 0f; }
        }

        /// <summary>How overcast the weather says it is, 0 (clear) .. 1 (rain or full cloud).</summary>
        /// <remarks>
        /// Eased OUT, not SmoothStep: the first drops should already put a heavy sky up there
        /// (it does not drizzle out of a half-empty one), while the top of the range still has
        /// somewhere to go. A SmoothStep is slow at BOTH ends, which was the worst of both --
        /// light rain barely clouded over, and heavy rain was a plateau.
        /// The game's own storms reach full overcast through m_currentCloud regardless: this
        /// curve is what a rain slider moved on its own travels along.
        /// </remarks>
        public static float Overcast
        {
            get
            {
                float t = Mathf.Clamp01(Rain / RainForFullOvercast);
                float rain = 1f - (1f - t) * (1f - t);
                return Mathf.Max(rain, GameCloud);
            }
        }

        /// <summary>The cover the mapping gives a clear day, 0..1.</summary>
        public static float FairEnd
        {
            get { return Value(Settings.WeatherFairCoverage, Settings.Defaults.FairCoverage); }
        }

        /// <summary>The cover the mapping gives rain or a full cloudy spell, 0..1.</summary>
        public static float RainEnd
        {
            get { return Value(Settings.WeatherOvercastCoverage, Settings.Defaults.OvercastCoverage); }
        }

        /// <summary>
        /// The direction the weather travels, in the game's own convention: WeatherManager
        /// publishes _WindDirection as (sin a, 0, cos a) with a = m_windDirection in degrees,
        /// and the rain shaders slant along it -- so clouds drift the way the rain falls.
        /// </summary>
        public static Vector3 WindDirection
        {
            get
            {
                if (!Singleton<WeatherManager>.exists)
                    return CloudWind.DefaultDirection;

                float angle = Singleton<WeatherManager>.instance.m_windDirection * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            }
        }

        /// <summary>
        /// Rain brings wind. The rain half of the game's GetWindSpeedFactor (1 + rain / 2 -
        /// fog / 2); the fog half is dropped for the same reason as above.
        /// </summary>
        public static float WindSpeedFactor
        {
            get { return 1f + 0.5f * Rain; }
        }

        /// <summary>Call once per frame, from the one place that also advances the wind.</summary>
        public static void Advance(float deltaTime)
        {
            float target = Target();

            if (!_started)
            {
                _coverage = target;
                _started = true;
                return;
            }

            float seconds = Manual ? ManualSeconds : FollowSeconds;
            _coverage = Mathf.Lerp(_coverage, target, 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / seconds));

            // An exponential never arrives; the slider's extremes should be reachable.
            if (Mathf.Abs(target - _coverage) < 0.0005f)
                _coverage = target;
        }

        /// <summary>Forget the smoothed value, so a newly loaded city starts on its own weather.</summary>
        public static void Reset()
        {
            _started = false;
        }

        /// <summary>Where the cover is heading: the slider, or the weather's verdict.</summary>
        public static float Target()
        {
            if (Manual)
                return Value(Settings.Coverage, Settings.Defaults.Coverage);

            return Mathf.Lerp(FairEnd, RainEnd, Overcast);
        }

        /// <summary>
        /// One line for the panel and the log, and it says which of the three levels is in
        /// charge. An override is a saved setting, so it survives the session: someone who forgot
        /// it is on will report the mod as broken, and a loud status line is the cheap fix.
        /// While FOLLOWING it also prints the two ends of the mapping, because those live on
        /// the options page and nothing else on screen says what the weather is being mapped
        /// ONTO -- a rain end left at 0 makes the whole feature look dead, which is exactly
        /// how it was found.
        /// </summary>
        public static string Describe()
        {
            string weather = Localization.Get("Status.Weather.Game", Percent(Rain), Percent(GameCloud));
            string intensity = Percent(Coverage);

            return Manual
                ? Localization.Get("Status.Weather.Manual", intensity, weather)
                : Localization.Get("Status.Weather.Following", weather, intensity, Percent(FairEnd), Percent(RainEnd));
        }

        private static string Percent(float fraction)
        {
            return Localization.Get("Unit.Percent", (fraction * 100f).ToString("F0"));
        }

        private static float Value(FloatSetting setting, float fallback)
        {
            return setting != null ? Mathf.Clamp01(setting.value) : fallback;
        }
    }
}
