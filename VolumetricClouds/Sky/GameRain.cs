using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Silences the game's own rain drawing while ours replaces it, and puts it back after.
    /// </summary>
    /// <remarks>
    /// The game has two rain renderers, both attached to the camera (read from the IL):
    ///
    ///   RainParticleProperties  a grid of particle quads around the camera, drawn from
    ///                           LateUpdate and doing nothing else. Disabling the component
    ///                           is an exact off-switch.
    ///
    ///   RainProperties          a "spindle" shell around the camera with a scrolling rain
    ///                           texture -- the filter on the lens. Its Update ALSO schedules
    ///                           the lightning flashes and the thunder sound, and draws the
    ///                           shell unconditionally at the end of the same method, so the
    ///                           component must stay enabled. Its material is a public field
    ///                           read at draw time, though: swapping in a material that draws
    ///                           nothing stops the shell and leaves the thunder alone.
    ///
    /// Nothing of the game's is modified or destroyed; restoring is putting two things back.
    /// </remarks>
    public class GameRain : MonoBehaviour
    {
        private const float SearchInterval = 2f;

        private RainProperties[] _shells;
        private Material[] _shellMaterials;
        private RainParticleProperties[] _particles;
        private bool[] _particlesEnabled;

        private Material _invisible;
        private bool _silenced;
        private bool _searched;
        private float _nextSearch;

        private void LateUpdate()
        {
            if (!_searched && !TryFind())
                return;

            if (CloudRain.Active)
                Silence();
            else
                Restore();
        }

        private bool TryFind()
        {
            if (Time.time < _nextSearch)
                return false;

            _nextSearch = Time.time + SearchInterval;

            _shells = FindObjectsOfType<RainProperties>();
            _particles = FindObjectsOfType<RainParticleProperties>();
            if (_shells.Length == 0 && _particles.Length == 0)
                return false;

            _searched = true;
            _shellMaterials = new Material[_shells.Length];
            _particlesEnabled = new bool[_particles.Length];

            foreach (RainProperties shell in _shells)
            {
                Material material = shell.m_RainMaterial;
                Log.Msg("game rain shell: on '" + shell.gameObject.name + "' enabled=" + shell.enabled +
                        " material=" + Describe(material) +
                        " lightningAboveRain=" + shell.m_LightningTreshold.ToString("F2") +
                        " strikeEvery=" + shell.m_LightningMinMaxIntervals.x.ToString("F0") + ".." +
                        shell.m_LightningMinMaxIntervals.y.ToString("F0") + "s");
            }

            foreach (RainParticleProperties particles in _particles)
            {
                Log.Msg("game rain particles: on '" + particles.gameObject.name + "' enabled=" + particles.enabled +
                        " material=" + Describe(particles.m_RainMaterialX));
            }

            return true;
        }

        private void Silence()
        {
            if (_invisible == null)
            {
                Shader shader = ShaderBundle.Get(ShaderBundle.Invisible);
                if (shader == null)
                    return;

                _invisible = new Material(shader) { name = "VolumetricCloudsInvisible" };
            }

            if (!_silenced)
            {
                for (int i = 0; i < _shells.Length; i++)
                    _shellMaterials[i] = _shells[i] != null ? _shells[i].m_RainMaterial : null;
                for (int i = 0; i < _particles.Length; i++)
                    _particlesEnabled[i] = _particles[i] != null && _particles[i].enabled;

                _silenced = true;
                Log.Msg("game rain: silenced (" + _shells.Length + " shell, " + _particles.Length +
                        " particle renderer); lightning and thunder left running");
            }

            // Every frame: cheap, and it holds if anything puts the originals back.
            for (int i = 0; i < _shells.Length; i++)
            {
                if (_shells[i] != null && _shells[i].m_RainMaterial != _invisible)
                    _shells[i].m_RainMaterial = _invisible;
            }

            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i] != null && _particles[i].enabled)
                    _particles[i].enabled = false;
            }
        }

        private void Restore()
        {
            if (!_silenced)
                return;

            _silenced = false;

            for (int i = 0; i < _shells.Length; i++)
            {
                if (_shells[i] != null && _shellMaterials[i] != null)
                    _shells[i].m_RainMaterial = _shellMaterials[i];
            }

            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i] != null)
                    _particles[i].enabled = _particlesEnabled[i];
            }

            Log.Msg("game rain: restored");
        }

        private static string Describe(Material material)
        {
            if (material == null)
                return "none";

            return "'" + material.name + "' shader '" + (material.shader == null ? "null" : material.shader.name) + "'";
        }

        private void OnDestroy()
        {
            Restore();

            if (_invisible != null)
                Destroy(_invisible);
        }
    }
}
