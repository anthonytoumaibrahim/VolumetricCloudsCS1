using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Switches off the game's own weather clouds while ours are showing.
    /// </summary>
    /// <remarks>
    /// What the game draws when it rains is DayNightDynamicCloudsProperties: a sky-dome mesh
    /// whose material gets _Coverage from WeatherManager.SampleCloudCoverage, drawn with
    /// Graphics.DrawMesh from the component's own Update. It lives on the same GameObject as
    /// DayNightProperties (its OnEnable does GetComponent for it). Being a plain behaviour,
    /// disabling it is an exact off-switch and enabling it again is an exact restore; nothing
    /// is destroyed and the weather simulation itself is never touched.
    ///
    /// The other dome, DayNightCloudsProperties, is the always-on painted sky layer (the one
    /// cloud-replacer mods retexture). It is only reported, not touched.
    ///
    /// NEVER FOUGHT OVER (invariant 6, 1.3.0): the game only ever switches it off itself (its
    /// OnEnable and InitSkydomeMesh, IL), so a dome switched back on while we hold it is another
    /// mod's wish. It is left on for the city and said once. Switching it off again every frame
    /// would be a tug-of-war that rebuilds its mesh every frame (OnEnable builds it, OnDisable
    /// destroys it).
    /// </remarks>
    public class GameCloudDome : MonoBehaviour
    {
        private const float SearchInterval = 2f;

        private DayNightDynamicCloudsProperties _dome;
        private bool _originalEnabled;
        private bool _hidden;
        private bool _reportedMissing;
        private float _nextSearch;

        /// <summary>Something else switched the dome back on: it is theirs for the rest of the city.</summary>
        private bool _leftToOthers;

        private static bool OurCloudsShowing
        {
            get { return Settings.CloudsVisible == null || Settings.CloudsVisible.value; }
        }

        private void LateUpdate()
        {
            if (_leftToOthers || (_dome == null && !TryFind()))
                return;

            if (OurCloudsShowing)
            {
                if (!_hidden)
                {
                    _originalEnabled = _dome.enabled;
                    _hidden = true;
                    _dome.enabled = false;
                    Log.Msg("game cloud dome: hidden (it was " + (_originalEnabled ? "enabled" : "already disabled") + ")");
                }
                else if (_dome.enabled)
                {
                    _hidden = false;
                    _leftToOthers = true;
                    Log.Msg("game cloud dome: switched back on by something else; it is left as it is for this city " +
                            "(the game's rain clouds may show with ours)");
                }
            }
            else
            {
                Restore();
            }
        }

        private bool TryFind()
        {
            if (Time.time < _nextSearch)
                return false;

            _nextSearch = Time.time + SearchInterval;

            DayNightProperties properties = DayNightProperties.instance;
            if (properties != null)
                _dome = properties.GetComponent<DayNightDynamicCloudsProperties>();
            if (_dome == null)
                _dome = FindObjectOfType<DayNightDynamicCloudsProperties>();

            if (_dome == null)
            {
                if (!_reportedMissing)
                {
                    _reportedMissing = true;
                    Log.Warn("game cloud dome: DayNightDynamicCloudsProperties not found (yet); will keep looking");
                }

                return false;
            }

            Material material = _dome.m_CloudMaterial;
            Log.Msg("game cloud dome: found on '" + _dome.gameObject.name + "' enabled=" + _dome.enabled +
                    " maxCoverage=" + _dome.m_MaxCoverage.ToString("F2") +
                    " material=" + (material == null ? "none" : "'" + material.name + "' shader '" +
                                    (material.shader == null ? "null" : material.shader.name) + "'"));

            DayNightCloudsProperties painted = properties != null
                ? properties.GetComponent<DayNightCloudsProperties>()
                : FindObjectOfType<DayNightCloudsProperties>();
            Log.Msg("game painted sky clouds (left alone): " +
                    (painted == null ? "not present" : "enabled=" + painted.enabled + " material=" +
                        (painted.m_CloudMaterial == null ? "none" : "'" + painted.m_CloudMaterial.name + "'")));

            return true;
        }

        private void Restore()
        {
            if (!_hidden)
                return;

            _hidden = false;

            // Only if it is still as we left it: switched on since, it is someone else's.
            if (_dome != null && !_dome.enabled)
            {
                _dome.enabled = _originalEnabled;
                Log.Msg("game cloud dome: restored (enabled=" + _originalEnabled + ")");
            }
        }

        private void OnDestroy()
        {
            Restore();
        }
    }
}
