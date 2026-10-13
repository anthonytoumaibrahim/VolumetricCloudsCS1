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
        /// <remarks>
        /// 1.5.0, "Wind direction" (Now tab): the game's wind (the default, exactly as before), the
        /// game's wind turning slowly, or a fixed direction. The game's wind never settles: once it
        /// is within 0.2 deg of its target it picks a new one anywhere on the compass and eases into
        /// it (WeatherManager.SimulationStepImpl, IL), so the whole sky swings round every few minutes
        /// -- the author: "it does not look good the way the whole clouds just shift direction
        /// suddenly". The clouds, the fog and the rain all read this one wind. The game's own
        /// snowflakes (kept on winter maps) lean with the game's wind, which is never written.
        /// </remarks>
        public static Vector3 WindDirection
        {
            get
            {
                if (_windStarted && WindMode != WindFollow)
                    return FromAngle(_windAngle);

                if (WindMode == WindFixed)
                    return FromAngle(FixedAngle);

                if (!Singleton<WeatherManager>.exists)
                    return CloudWind.DefaultDirection;

                float angle = Singleton<WeatherManager>.instance.m_windDirection * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            }
        }

        public const int WindFollow = 0;
        public const int WindSlowly = 1;
        public const int WindFixed = 2;

        /// <summary>
        /// "Follow the game slowly": how long the wind takes to make most of a turn the game makes,
        /// in seconds of simulation at 1x (paused: it holds). The game's own turn takes about 1000
        /// simulation steps; this is many times slower, and never faster than <see cref="SlowTurnMost"/>.
        /// Starting points, tuned in-game.
        /// </summary>
        private const float SlowTurnSeconds = 300f;

        /// <summary>The fastest the slow wind turns, degrees per second of simulation at 1x.</summary>
        private const float SlowTurnMost = 0.25f;

        private static float _windAngle;
        private static bool _windStarted;
        private static int _loggedWindMode = -1;

        /// <summary>The "Wind direction" row's pick; an unknown value (typed by hand) follows the game.</summary>
        public static int WindMode
        {
            get
            {
                int mode = Settings.WindDirection != null ? Settings.WindDirection.value : Settings.Defaults.WindDirection;
                return mode == WindSlowly || mode == WindFixed ? mode : WindFollow;
            }
        }

        /// <summary>The game's wind, degrees; the default direction's without a WeatherManager.</summary>
        public static float GameWindAngle
        {
            get
            {
                if (Singleton<WeatherManager>.exists)
                    return Singleton<WeatherManager>.instance.m_windDirection;

                Vector3 d = CloudWind.DefaultDirection;
                return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
        }

        private static float FixedAngle
        {
            get { return Settings.WindAngle != null ? Settings.WindAngle.value : Settings.Defaults.WindAngle; }
        }

        private static Vector3 FromAngle(float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        }

        /// <summary>
        /// Once per frame, before anything moves along the wind (CloudLighting, the one place).
        /// <paramref name="simulationSeconds"/>: real seconds x the simulation speed, 0 while paused.
        /// A city starts on the game's wind (or the fixed one); a change of pick carries on from the
        /// direction there was, so "slowly" turns from a fixed wind towards the game's.
        /// </summary>
        public static void AdvanceWind(float simulationSeconds)
        {
            int mode = WindMode;
            float game = GameWindAngle;

            if (mode == WindFollow)
            {
                _windAngle = game;
            }
            else if (mode == WindFixed)
            {
                _windAngle = FixedAngle;
            }
            else if (!_windStarted)
            {
                _windAngle = game;
            }
            else
            {
                float seconds = Mathf.Max(0f, simulationSeconds);
                float turn = Mathf.DeltaAngle(_windAngle, game) * (1f - Mathf.Exp(-seconds / SlowTurnSeconds));
                float most = SlowTurnMost * seconds;
                _windAngle = Mathf.Repeat(_windAngle + Mathf.Clamp(turn, -most, most), 360f);
            }

            _windStarted = true;

            if (mode != _loggedWindMode)
            {
                _loggedWindMode = mode;
                Log.Msg("wind: " + DescribeWind() + " (the game's wind is " + game.ToString("F0") + " deg)");
            }
        }

        /// <summary>For the logs: "follows the game", "follows the game slowly, now 132 deg", "fixed at 90 deg".</summary>
        public static string DescribeWind()
        {
            switch (WindMode)
            {
                case WindSlowly:
                    return "follows the game slowly, now " + _windAngle.ToString("F0") + " deg";
                case WindFixed:
                    return "fixed at " + FixedAngle.ToString("F0") + " deg";
                default:
                    return "follows the game";
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
            _windStarted = false;
            _loggedWindMode = -1;
        }

        /// <summary>Where the cover is heading: the slider, or the weather's verdict.</summary>
        public static float Target()
        {
            if (Manual)
                return Value(Settings.Coverage, Settings.Defaults.Coverage);

            return Mathf.Lerp(FairEnd, RainEnd, Overcast);
        }

        private static float Value(FloatSetting setting, float fallback)
        {
            return setting != null ? Mathf.Clamp01(setting.value) : fallback;
        }
    }
}
