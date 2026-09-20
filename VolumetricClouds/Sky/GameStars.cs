using System.Reflection;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Makes sure the game's stars are drawn BEFORE our clouds, so the clouds can cover them.
    /// </summary>
    /// <remarks>
    /// The stars are not part of the skybox: DayNightProperties.LateUpdate draws a separate
    /// mesh with the shader 'Hidden/DayNight/Stars'. Which render queue that shader sits in is
    /// asset data, not code, so it is read at runtime and logged rather than assumed. If it is
    /// at or after the cloud pass (Transparent+100 = 3100) the stars are painted on top of the
    /// clouds whatever the clouds' opacity, and the material's queue is pulled in front of
    /// them; the original value is put back on unload. If it is already earlier, stars showing
    /// through were purely the clouds' own transparency, which CloudRaymarch now closes at
    /// night (_NightOpacity), and nothing is changed here.
    /// </remarks>
    public class GameStars : MonoBehaviour
    {
        /// <summary>The cloud pass is Transparent+100; the lightning bolt sits at +90.</summary>
        private const int BehindClouds = 3080;
        private const int CloudQueue = 3100;
        private const float SearchInterval = 2f;

        private Material _stars;
        private int _originalQueue;
        private bool _moved;
        private bool _done;
        private float _nextSearch;

        private void LateUpdate()
        {
            if (_done || Time.time < _nextSearch)
                return;

            _nextSearch = Time.time + SearchInterval;

            DayNightProperties properties = DayNightProperties.instance;
            if (properties == null)
                return;

            // The property is not public. It creates the material on first use, exactly as the
            // game's own LateUpdate does, so reading it early is harmless.
            PropertyInfo property = typeof(DayNightProperties).GetProperty("starMaterial",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                _done = true;
                Log.Warn("game stars: DayNightProperties.starMaterial not found; cannot check the draw order.");
                return;
            }

            _stars = property.GetValue(properties, null) as Material;
            if (_stars == null)
                return;

            _done = true;
            _originalQueue = _stars.renderQueue;

            string shader = _stars.shader == null ? "null" : _stars.shader.name;
            if (_originalQueue >= CloudQueue)
            {
                _stars.renderQueue = BehindClouds;
                _moved = true;
                Log.Msg("game stars: shader '" + shader + "' was in queue " + _originalQueue +
                        ", AFTER the clouds (" + CloudQueue + "); moved to " + BehindClouds + " so clouds cover them");
            }
            else
            {
                Log.Msg("game stars: shader '" + shader + "' is in queue " + _originalQueue +
                        ", already before the clouds (" + CloudQueue + "); left alone");
            }
        }

        private void OnDestroy()
        {
            if (_moved && _stars != null)
                _stars.renderQueue = _originalQueue;
        }
    }
}
