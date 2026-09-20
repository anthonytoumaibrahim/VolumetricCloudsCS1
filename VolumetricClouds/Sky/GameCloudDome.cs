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
    /// </remarks>
    public class GameCloudDome : MonoBehaviour
    {
        private const float SearchInterval = 2f;

        private DayNightDynamicCloudsProperties _dome;
        private bool _originalEnabled;
        private bool _hidden;
        private bool _reportedMissing;
        private float _nextSearch;
        private int _reDisabled;

        private static bool OurCloudsShowing
        {
            get { return Settings.CloudsVisible == null || Settings.CloudsVisible.value; }
        }

        private void LateUpdate()
        {
            if (_dome == null && !TryFind())
                return;

            if (OurCloudsShowing)
            {
                if (!_hidden)
                {
                    _originalEnabled = _dome.enabled;
                    _hidden = true;
                    _reDisabled = 0;
                    Log.Msg("game cloud dome: hidden (it was " + (_originalEnabled ? "enabled" : "already disabled") + ")");
                }

                // Checked every frame in case anything switches it back on.
                if (_dome.enabled)
                {
                    _dome.enabled = false;
                    if (++_reDisabled == 2)
                        Log.Warn("game cloud dome: something re-enabled it; keeping it off");
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
            if (_dome != null)
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
