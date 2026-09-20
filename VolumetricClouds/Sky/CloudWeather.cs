using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// THE cloud cover. The visible clouds, the shadow map, the fallback cookie and the
    /// billboards all read <see cref="Coverage"/>, and it is advanced in exactly one place
    /// (CloudLighting), like the wind.
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
        /// Rain at or above this means a fully overcast sky. The game's dome uses 0.25 (its
        /// rain * 4); a little more travel makes Play It's slider useful at the low end, and
        /// the game's own random rain never starts below 0.25 anyway.
        /// </summary>
        private const float RainForFullOvercast = 0.3f;

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
        public static float Overcast
        {
            get
            {
                float rain = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Rain / RainForFullOvercast));
                return Mathf.Max(rain, GameCloud);
            }
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

            float fair = Value(Settings.WeatherFairCoverage, Settings.Defaults.FairCoverage);
            float overcast = Value(Settings.WeatherOvercastCoverage, Settings.Defaults.OvercastCoverage);
            return Mathf.Lerp(fair, overcast, Overcast);
        }

        /// <summary>
        /// One line for the panel and the log, and it says which of the three levels is in
        /// charge. An override is a SavedBool, so it survives the session: someone who forgot
        /// it is on will report the mod as broken, and a loud status line is the cheap fix.
        /// </summary>
        public static string Describe()
        {
            string weather = "rain " + (Rain * 100f).ToString("F0") + "%, clouds " +
                             (GameCloud * 100f).ToString("F0") + "%";
            string intensity = (Coverage * 100f).ToString("F0") + "%";

            return Manual
                ? "OVERRIDDEN: intensity " + intensity + "  (the game says " + weather + ")"
                : "Following the game: " + weather + "  ->  intensity " + intensity;
        }

        private static float Value(SavedFloat setting, float fallback)
        {
            return setting != null ? Mathf.Clamp01(setting.value) : fallback;
        }
    }
}
