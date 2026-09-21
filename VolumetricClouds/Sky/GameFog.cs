using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Stops the game's fog from reacting to foggy WEATHER while ours is drawing it, and
    /// leaves everything else the game's fog does alone.
    /// </summary>
    /// <remarks>
    /// The game's fog is a screen effect (DayNightFogEffect) fed by FogProperties.Update. It
    /// does jobs worth keeping -- distance haze, hiding the map edge, the pollution tint --
    /// and one that ours replaces: when the weather turns foggy it lerps three values towards
    /// their "foggy" twins by clamp01(WeatherManager.m_currentFog), greying the whole screen
    /// evenly:
    ///     m_FogDensity         -> m_FoggyFogDensity
    ///     m_FogStart           -> m_FoggyFogStart
    ///     m_NoiseContribution  -> m_FoggyNoiseContribution
    /// All six are public fields read every frame, so making each foggy twin equal its clear
    /// value switches that reaction off exactly, and writing the originals back restores it.
    /// Nothing is patched and the weather itself is untouched.
    /// </remarks>
    public class GameFog : MonoBehaviour
    {
        private const float SearchInterval = 2f;

        private FogProperties _fog;
        private float _foggyDensity;
        private float _foggyStart;
        private float _foggyNoise;
        private bool _neutralised;
        private bool _alreadyFlat;
        private float _nextSearch;

        private void LateUpdate()
        {
            if (_fog == null && !TryFind())
                return;

            if (CloudFog.Active)
                Neutralise();
            else
                Restore();
        }

        private bool TryFind()
        {
            if (Time.time < _nextSearch)
                return false;

            _nextSearch = Time.time + SearchInterval;
            _fog = FindObjectOfType<FogProperties>();
            if (_fog == null)
                return false;

            Log.Msg("game fog: on '" + _fog.gameObject.name + "' clear/foggy density=" +
                    _fog.m_FogDensity.ToString("G3") + "/" + _fog.m_FoggyFogDensity.ToString("G3") +
                    " start=" + _fog.m_FogStart.ToString("F0") + "/" + _fog.m_FoggyFogStart.ToString("F0") +
                    " noise=" + _fog.m_NoiseContribution.ToString("F2") + "/" + _fog.m_FoggyNoiseContribution.ToString("F2"));
            return true;
        }

        private void Neutralise()
        {
            if (!_neutralised)
            {
                _foggyDensity = _fog.m_FoggyFogDensity;
                _foggyStart = _fog.m_FoggyFogStart;
                _foggyNoise = _fog.m_FoggyNoiseContribution;

                // Render It! writes ONE profile value into each clear/foggy pair (read from its
                // IL: ModManager.UpdateEnvironment), and only when its profile changes. Under
                // it the twins are already flat, and what has to come back on restore is
                // "flat", not the numbers of whichever profile was live when we looked.
                _alreadyFlat = Mathf.Approximately(_foggyDensity, _fog.m_FogDensity)
                            && Mathf.Approximately(_foggyStart, _fog.m_FogStart)
                            && Mathf.Approximately(_foggyNoise, _fog.m_NoiseContribution);

                _neutralised = true;
                Log.Msg("game fog: its foggy-weather reaction is off; distance haze, edge fog and pollution tint untouched" +
                        (_alreadyFlat ? " (another mod, such as Render It!, had already flattened it; it will be handed back flat)" : ""));
            }

            // Every frame: the clear values can be changed by theme and look mods at any time.
            _fog.m_FoggyFogDensity = _fog.m_FogDensity;
            _fog.m_FoggyFogStart = _fog.m_FogStart;
            _fog.m_FoggyNoiseContribution = _fog.m_NoiseContribution;
        }

        private void Restore()
        {
            if (!_neutralised)
                return;

            _neutralised = false;
            if (_fog == null)
                return;

            // Flat when we found it: leave the twins on the CURRENT clear values, which is what
            // that mod wants whatever its sliders have been moved to since.
            _fog.m_FoggyFogDensity = _alreadyFlat ? _fog.m_FogDensity : _foggyDensity;
            _fog.m_FoggyFogStart = _alreadyFlat ? _fog.m_FogStart : _foggyStart;
            _fog.m_FoggyNoiseContribution = _alreadyFlat ? _fog.m_NoiseContribution : _foggyNoise;
            Log.Msg("game fog: foggy-weather reaction restored" + (_alreadyFlat ? " (flat, as it was found)" : ""));
        }

        private void OnDestroy()
        {
            Restore();
        }
    }
}
