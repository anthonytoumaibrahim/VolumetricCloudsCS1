using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Owns the sun's light cookie, which is how clouds darken the world. Everything it
    /// touches on the sun is captured on start and restored on destroy.
    /// </summary>
    /// <remarks>
    /// There is deliberately no global sun dimming here any more. Scaling
    /// DayNightProperties.m_SunIntensity worked, but it dims sunlit gaps as much as shaded
    /// ground, fights RenderIt over the same field, and had to be balanced against the
    /// cookie to stop the two multiplying. The cookie alone gives full sun in the gaps and
    /// the configured floor under cloud, and the world still darkens with coverage because
    /// more of it is in shade.
    /// </remarks>
    public class CloudLighting : MonoBehaviour
    {
        private const float LogInterval = 5f;

        private Light _sun;
        private CloudDensityField _field;

        private Texture _originalCookie;
        private float _originalCookieSize;
        private Vector3 _originalSunPosition;
        private bool _captured;

        private float _appliedCoverage = -1f;
        private float _appliedDepth = -1f;
        private bool _appliedChecker;
        private float _nextLogTime;
        private string _mode = "none";
        private string _loggedMode = "none";

        /// <summary>Shares the density field so shadows and visible clouds agree.</summary>
        public void Initialise(CloudDensityField field)
        {
            _field = field;
        }

        private void Start()
        {
            _sun = FindSun();
            if (_sun == null)
            {
                Log.Error("Could not find the sun light; cloud lighting disabled.");
                enabled = false;
                return;
            }

            if (_field == null)
                _field = new CloudDensityField(Random.Range(1, 100000));

            _originalCookie = _sun.cookie;
            _originalCookieSize = _sun.cookieSize;
            _originalSunPosition = _sun.transform.position;
            _captured = true;

            // Statics outlive a city; start this one on its own weather, not the last one's.
            CloudWeather.Reset();
            CloudFog.Reset();

            Log.Msg("Sun '" + _sun.name + "' type=" + _sun.type +
                    " intensity=" + _sun.intensity +
                    " existingCookie=" + (_originalCookie == null ? "none" : _originalCookie.name) +
                    " cookieSize=" + _originalCookieSize);

            Refresh();
        }

        /// <summary>Re-applies the cookie after a settings change.</summary>
        public void Refresh()
        {
            if (_sun == null || _field == null)
                return;

            ApplyCookie();
        }

        /// <summary>
        /// Picks what the sun projects. The marched shadow map wins whenever it exists: each
        /// cloud then casts its own shadow in the right place. The flat cookie is the
        /// fallback for builds without the shader bundle, and the checkerboard is a debug aid.
        /// </summary>
        private void ApplyCookie()
        {
            // Hidden clouds cast nothing: the sun gets its own cookie back until they return.
            bool shadows = Settings.ShadowsCast;
            bool checker = Settings.DebugChecker != null && Settings.DebugChecker.value;
            CloudShadowMap map = CloudShadowMap.Current;

            Transform t = _sun.transform;

            if (!shadows && !checker)
            {
                _mode = "off";
                _sun.cookie = _originalCookie;
                _sun.cookieSize = _originalCookieSize;
                t.position = _originalSunPosition;
            }
            else if (map != null && map.IsReady && !checker)
            {
                // The cookie is centred on the light, so pin it to the point the shadow map
                // was rendered around. Wind is already baked into the map itself.
                _mode = "shadowmap";
                _sun.cookie = map.Texture;
                _sun.cookieSize = CloudShadowMap.CookieSize;
                t.position = CloudShadowMap.Anchor;
            }
            else
            {
                // A directional light ignores its position for lighting, but the cookie
                // projection does not -- so sliding the transform scrolls the pattern.
                _mode = checker ? "checker" : "flat-cookie";
                RebuildFlatCookie(checker);
                _sun.cookie = _field.Texture;
                _sun.cookieSize = Settings.WeatherTileSize != null ? Settings.WeatherTileSize.value : Settings.Defaults.WeatherTileSize;
                t.position = _originalSunPosition + CloudWind.Offset;
            }

            // One line per change, always: the cookie is the only thing that darkens the world,
            // so the log has to say when it went and why.
            if (_mode != _loggedMode)
            {
                _loggedMode = _mode;
                bool hidden = Settings.CloudsVisible != null && !Settings.CloudsVisible.value;
                Log.Msg("shadows: sun cookie = " + _mode +
                        (_mode != "off" ? "" : hidden ? " (the clouds are hidden)" : " (cloud shadows are switched off)"));
            }
        }

        /// <summary>Rewrites the CPU-side cookie, but only when something it depends on moved.</summary>
        private void RebuildFlatCookie(bool checker)
        {
            float coverage = CloudShaderParams.CoverageStepped;
            float depth = CloudShadowMap.ShadowDepth(coverage);

            if (checker == _appliedChecker
                && Mathf.Approximately(coverage, _appliedCoverage)
                && Mathf.Approximately(depth, _appliedDepth))
            {
                return;
            }

            if (checker)
                _field.ApplyChecker();
            else
                _field.Apply(coverage, depth);

            _appliedChecker = checker;
            _appliedCoverage = coverage;
            _appliedDepth = depth;
        }

        private void LateUpdate()
        {
            if (_sun == null)
                return;

            // The one place the cover and the wind advance; the visible clouds, the shadow map
            // and the fallback cookie all read the same two values. The cover runs on real
            // time, so Play It's rain slider shows its effect while the game is paused; the
            // wind runs on simulation time, so a paused city has still clouds.
            CloudWeather.Advance(Time.deltaTime);

            float simulationRate = SimulationRate();
            float speed = Settings.WindSpeed != null ? Settings.WindSpeed.value : Settings.Defaults.WindSpeed;
            CloudWind.Advance(CloudWeather.WindDirection,
                              Time.deltaTime * speed * 10f * CloudWeather.WindSpeedFactor * simulationRate);

            // After the cover and the wind: where it rains is derived from both.
            CloudRain.Advance(_field, RainDrops.ShadersAvailable, Time.deltaTime, simulationRate);

            // The fog is drawn by the volumetric cloud pass, so it needs that pass running.
            CloudFog.Advance(Time.deltaTime, CloudVolume.IsActive, CloudWeather.WindDirection, simulationRate);

            // Every frame, not just on a settings change: the shadow map becomes ready a
            // moment after load, and the game is free to move the sun's transform.
            ApplyCookie();

            if (Log.Detailed && Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + LogInterval;

                float coverage = CloudShaderParams.Coverage;
                Vector3 wind = CloudWeather.WindDirection;
                Log.Detail("weather: " + (CloudWeather.Manual ? "OVERRIDDEN" : "followed") +
                        " rain=" + CloudWeather.Rain.ToString("F2") +
                        " gameCloud=" + CloudWeather.GameCloud.ToString("F2") +
                        " overcast=" + CloudWeather.Overcast.ToString("F2") +
                        " map=" + CloudWeather.FairEnd.ToString("F2") + ".." + CloudWeather.RainEnd.ToString("F2") +
                        " target=" + CloudWeather.Target().ToString("F2") +
                        " | wind=(" + wind.x.ToString("F2") + "," + wind.z.ToString("F2") + ")" +
                        " x" + CloudWeather.WindSpeedFactor.ToString("F2"));
                Log.Detail("fog: " + (!CloudFog.Active ? "off (the game's)" : CloudFog.Overridden ? "OVERRIDDEN" : "followed") +
                        " gameFog=" + CloudFog.GameFog.ToString("F2") +
                        " amount=" + CloudFog.Amount.ToString("F2") +
                        " drift=(" + CloudFog.Offset.x.ToString("F0") + "," + CloudFog.Offset.z.ToString("F0") + ")m" +
                        " swirl=" + CloudFog.Boil.ToString("F2") +
                        " terrainMap=" + (TerrainHeightMap.Ready ? "ready" : CloudFog.Enabled ? "pending" : "not built (fog is off)"));
                Log.Detail("coverage=" + coverage.ToString("F2") +
                        " shadows=" + _mode +
                        " shadowDepth=" + CloudShadowMap.ShadowDepth(coverage).ToString("F2") +
                        " sunIntensity=" + _sun.intensity.ToString("F2") +
                        " cookie=" + (_sun.cookie == null ? "none" : _sun.cookie.name) +
                        " cookieSize=" + _sun.cookieSize.ToString("F0") +
                        " sunElevation=" + (-Mathf.Asin(Mathf.Clamp(_sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg).ToString("F0") + "deg");
            }
        }

        /// <summary>
        /// 0 while the game is paused, otherwise the simulation speed. Unity's clock keeps
        /// running through a pause, so without this the clouds would keep drifting over a
        /// frozen city; scaling by speed keeps them in step with the day/night cycle.
        /// </summary>
        private static float SimulationRate()
        {
            SimulationManager simulation = Singleton<SimulationManager>.instance;
            if (simulation == null)
                return 1f;

            if (simulation.SimulationPaused || simulation.ForcedSimulationPaused)
                return 0f;

            return Mathf.Max(1, simulation.FinalSimulationSpeed);
        }

        private static Light FindSun()
        {
            DayNightProperties properties = DayNightProperties.instance;
            if (properties == null || properties.m_SunLight == null)
                return null;

            Light light = properties.m_SunLight.GetComponent<Light>();
            return light != null ? light : properties.m_SunLight.GetComponentInChildren<Light>();
        }

        private void OnDestroy()
        {
            // The sound-and-wet-roads patch outlives the city; hand the game its rain back.
            CloudRain.Clear();

            if (!_captured || _sun == null)
                return;

            _sun.cookie = _originalCookie;
            _sun.cookieSize = _originalCookieSize;
            _sun.transform.position = _originalSunPosition;
        }
    }
}
