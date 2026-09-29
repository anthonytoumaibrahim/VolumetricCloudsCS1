using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Takes the game fog's HORIZON BAND off the sky while our clouds are drawn (1.3.0; the author:
    /// "the blue sky horizon is still there, it's visible especially at night, it hides far away
    /// clouds. Let's remove it as long as the user has our mod turned on"). Handed back when the
    /// clouds are hidden ("Volumetric Weather" unticked) and when the city closes.
    /// </summary>
    /// <remarks>
    /// Read from the disassembly of the game's fog (Hidden/DayNight/Fog, tools/shaderdump.ps1):
    /// on a pixel of SKY (depth at the far plane) it lays its fog colour over the sky by
    /// max(1 - y / m_HorizonHeight, its distance fog faded over m_FogHeight), y being the height of
    /// the far-plane point on the pixel's ray -- a band over the lowest few degrees. The effect is
    /// [ImageEffectOpaque], so it runs BEFORE our clouds: the band is painted on the empty sky and
    /// the clouds over it, and it shows wherever they are thin -- at the horizon, where they fade
    /// into the distance. FogProperties.Update hands m_HorizonHeight to the shader every frame
    /// (its inverse, _FogPollutionParams.z), and the shader uses it for sky pixels only: making it
    /// a millimetre takes the band off the sky above the horizon and leaves the ground's fog, the
    /// map edge and the pollution tint alone. Below the horizon it still fills, so the void past
    /// the map edge stays hidden. Nothing is patched.
    ///
    /// Render It! writes this field from its profile when one of its sliders moves: the latest
    /// value anyone else wrote is the one handed back.
    /// </remarks>
    public class GameHorizon : MonoBehaviour
    {
        private const float SearchInterval = 2f;

        /// <summary>The band's height while it is off: a millimetre (the shader divides by it, so never 0).</summary>
        private const float Flat = 0.001f;

        /// <summary>Another mod writing it back is said this many times, then no more.</summary>
        private const int RewritesLogged = 3;

        private FogProperties _fog;
        private float _original;
        private bool _flattened;
        private int _rewrites;
        private float _nextSearch;

        private void LateUpdate()
        {
            if (_fog == null && !TryFind())
                return;

            if (CloudVolume.IsActive)
                Flatten();
            else
                Restore("the clouds are hidden");
        }

        private bool TryFind()
        {
            if (Time.time < _nextSearch)
                return false;

            _nextSearch = Time.time + SearchInterval;
            _fog = FindObjectOfType<FogProperties>();
            if (_fog == null)
                return false;

            // Everything the band over the sky is made of, once, whatever the logging level.
            Log.Msg("game fog, sky: horizon band " + _fog.m_HorizonHeight.ToString("F0") + " m, fog height " +
                    _fog.m_FogHeight.ToString("F0") + " m, distance " + _fog.m_FogDistance.ToString("F0") +
                    ", start " + _fog.m_FogStart.ToString("F0") + ", density " + _fog.m_FogDensity.ToString("G3") +
                    ", noise " + _fog.m_NoiseContribution.ToString("F2") + ", edge fog " + _fog.m_edgeFog +
                    " at " + _fog.m_EdgeFogDistance.ToString("F0") + " m");
            return true;
        }

        private void Flatten()
        {
            // Already off: ours, or flattened by something else (a second copy of this mod) before
            // we ever saw it -- then there is nothing of ours to hand back, and never a 0 (the
            // shader divides by it).
            float current = _fog.m_HorizonHeight;
            if (Mathf.Approximately(current, Flat))
                return;

            // Ours to hand back: the value found, or the one another mod has written since.
            _original = current;

            if (!_flattened)
            {
                Log.Msg("game fog: its horizon band is off the sky while our clouds are drawn (it was " +
                        current.ToString("F0") + " m; the ground's fog and the map edge are untouched)");
            }
            else if (++_rewrites <= RewritesLogged)
            {
                Log.Msg("game fog: another mod set the horizon band to " + current.ToString("F0") +
                        " m; kept off while our clouds are drawn, and that value is the one handed back");
            }

            _flattened = true;
            _fog.m_HorizonHeight = Flat;
        }

        private void Restore(string why)
        {
            if (!_flattened)
                return;

            _flattened = false;
            if (_fog == null)
                return;

            // Only if it is still ours: a mod that wrote its own since has the last word.
            if (Mathf.Approximately(_fog.m_HorizonHeight, Flat))
                _fog.m_HorizonHeight = _original;

            Log.Msg("game fog: its horizon band is back at " + _fog.m_HorizonHeight.ToString("F0") + " m (" + why + ")");
        }

        private void OnDestroy()
        {
            Restore("the city is closing");
        }
    }
}
