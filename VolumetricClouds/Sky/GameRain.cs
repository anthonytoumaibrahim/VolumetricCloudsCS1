using System;
using System.Reflection;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Silences the game's own rain drawing while ours replaces it, and puts it back after. On a
    /// winter map the game's SNOWFLAKES stay, shown only where it snows (1.3.0).
    /// </summary>
    /// <remarks>
    /// The game has two rain renderers, both attached to the camera (read from the IL):
    ///
    ///   RainParticleProperties  a grid of particle quads in a box just in front of the camera
    ///                           (8 m + the near plane ahead, on a 2 m grid), drawn from
    ///                           LateUpdate and doing nothing else. On a rain map disabling the
    ///                           component is an exact off-switch.
    ///
    ///   RainProperties          a "spindle" shell around the camera with a scrolling rain
    ///                           texture. In a normal game this component is DISABLED (the
    ///                           log says so: the particle renderer is the live one). Where
    ///                           it is enabled, its Update also runs a legacy lightning and
    ///                           thunder scheduler and draws the shell unconditionally at the
    ///                           end of the same method -- so its enabled state is never
    ///                           touched here. Its material is a public field read at draw
    ///                           time: swapping in one that draws nothing stops the shell and
    ///                           leaves whatever else that Update does alone.
    ///
    /// SNOW. On a winter map the particle renderer draws snow (its SNOWPARTICLE keyword, set in
    /// its OnEnable from the map's environment), and the author liked it better than the flakes
    /// the first 1.3.0 build drew in its place ("I like the vanilla game's snow better"). So it
    /// stays on, and follows the clouds: every frame its LateUpdate sets `_RainIntensity` =
    /// Lerp(0, m_MaxRainIntensity, m_RainControl) on its three material copies and submits them
    /// with Graphics.DrawMesh, which reads a material's values when the camera renders -- so
    /// just before each camera culls (Camera.onPreCull) the same value is set again, times how
    /// much snow falls where the camera is (<see cref="CloudRain.OnScreen"/>, and none above the
    /// cloud base). Its particles are all within a few metres of the camera, so that one number
    /// is exact for everything it draws. onPreCull, not a Harmony patch on its LateUpdate:
    /// FPS Booster runs the game's LateUpdates through copies of its own, which a patch on the
    /// original would miss. The copies are private fields, read by reflection every time (the
    /// component makes new ones whenever it is enabled again).
    ///
    /// Nothing of the game's is modified or destroyed; restoring is putting two things back.
    /// </remarks>
    public class GameRain : MonoBehaviour
    {
        private const float SearchInterval = 2f;
        private const float LogInterval = 5f;

        /// <summary>Seconds the game's snow takes to come or go as the camera moves under, or out from under, a snowfall.</summary>
        private const float SnowFade = 1f;

        /// <summary>Metres above the cloud base over which the snow near the camera fades out: none is made up there.</summary>
        private const float AboveBase = 200f;

        private static readonly int IdRainIntensity = Shader.PropertyToID("_RainIntensity");

        private static readonly string[] CopyNames = { "m_RainMaterialCopyX", "m_RainMaterialCopyY", "m_RainMaterialCopyZ" };
        private static FieldInfo[] _copies;
        private static bool _copiesLooked;

        private RainProperties[] _shells;
        private Material[] _shellMaterials;
        private RainParticleProperties[] _particles;
        private bool[] _particlesEnabled;

        private Material _invisible;
        private bool _silenced;
        private bool _searched;
        private float _nextSearch;

        private Camera _camera;
        private bool _scalingSnow;
        private float _snowShown = 1f;
        private float _snowUnder;
        private float _snowBelowBase;
        private bool _loggedSnow;
        private float _nextLogTime;
        private bool _hooked;

        private void Start()
        {
            Camera.onPreCull += ScaleSnow;
            _hooked = true;
        }

        private void LateUpdate()
        {
            if (!_searched && !TryFind())
                return;

            if (CloudRain.Active)
                Silence();
            else
                Restore();

            UpdateSnow();
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
                Log.Msg(CloudRain.IsSnow
                    ? "game snow: its snowflakes kept, shown only where it snows; its rain shell silenced (" + _shells.Length + ")"
                    : "game rain: silenced (" + _shells.Length + " shell, " + _particles.Length +
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
                RainParticleProperties particles = _particles[i];
                if (particles == null)
                    continue;

                // Snow: its own flakes stay (scaled in ScaleSnow); one switched off here while it
                // rained comes back. Rain: switched off.
                if (CloudRain.IsSnow)
                {
                    if (_particlesEnabled[i] && !particles.enabled)
                        particles.enabled = true;
                }
                else if (particles.enabled)
                {
                    particles.enabled = false;
                }
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

        /// <summary>How much of the game's snow shows: how much falls where the camera is, eased.</summary>
        private void UpdateSnow()
        {
            bool wasScaling = _scalingSnow;
            bool wanted = CloudRain.Active && CloudRain.IsSnow && _particles != null && _particles.Length > 0 && Copies() != null;

            if (wanted && _camera == null)
                _camera = Camera.main;

            _scalingSnow = wanted && _camera != null;
            if (!_scalingSnow)
                return;

            // Snow at the camera: under a snowing cloud (the curtains' own mask, slanted by the
            // wind), and below the base the clouds make it at.
            Vector3 position = _camera.transform.position;
            _snowUnder = CloudRain.OnScreen(position);
            _snowBelowBase = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CloudRain.CloudBottom, CloudRain.CloudBottom + AboveBase, position.y));

            // Eased as the camera moves, in real time (it moves while the game is paused too);
            // set at once when it starts, or it would fade out of a clearing it was never in.
            float target = _snowUnder * _snowBelowBase;
            _snowShown = wasScaling ? Mathf.MoveTowards(_snowShown, target, Time.unscaledDeltaTime / SnowFade) : target;

            if (!_loggedSnow)
            {
                _loggedSnow = true;
                Log.Msg("game snow: shown where it snows -- " + Share(_snowUnder) + " under snow at the camera now, " +
                        Share(_snowBelowBase) + " below the cloud base (" + CloudRain.CloudBottom.ToString("F0") + " m; the camera is at " +
                        position.y.ToString("F0") + " m)");
            }

            if (Log.Detailed && Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + LogInterval;
                Log.Detail("game snow: shown " + Share(_snowShown) + " (under snow " + Share(_snowUnder) + ", below the base " +
                           Share(_snowBelowBase) + ") x the game's " + GameIntensity() + "; snow amount " + Share(CloudRain.Amount));
            }
        }

        /// <summary>
        /// Camera.onPreCull, for every camera: the game's snow intensity, times how much of it shows.
        /// Set again for each camera, since one rendered from inside a LateUpdate would come before
        /// the game's own value is written.
        /// </summary>
        private void ScaleSnow(Camera camera)
        {
            if (!_scalingSnow)
                return;

            try
            {
                FieldInfo[] copies = Copies();
                for (int i = 0; i < _particles.Length; i++)
                {
                    RainParticleProperties particles = _particles[i];
                    if (particles == null || !particles.enabled)
                        continue;

                    float intensity = Mathf.Lerp(0f, particles.m_MaxRainIntensity, particles.m_RainControl) * _snowShown;
                    foreach (FieldInfo copy in copies)
                    {
                        Material material = copy.GetValue(particles) as Material;
                        if (material != null)
                            material.SetFloat(IdRainIntensity, intensity);
                    }
                }
            }
            catch (Exception e)
            {
                // Once, and the game's snow is left as the game draws it.
                _scalingSnow = false;
                _copies = new FieldInfo[0];
                Log.Error("game snow: could not scale the game's snowflakes; they fall everywhere, as without the mod", e);
            }
        }

        /// <summary>The particle renderer's three material copies, or null (logged once) if the game has no such fields.</summary>
        private static FieldInfo[] Copies()
        {
            if (!_copiesLooked)
            {
                _copiesLooked = true;
                var found = new FieldInfo[CopyNames.Length];
                for (int i = 0; i < CopyNames.Length; i++)
                {
                    found[i] = typeof(RainParticleProperties).GetField(CopyNames[i], BindingFlags.NonPublic | BindingFlags.Instance);
                    if (found[i] == null || found[i].FieldType != typeof(Material))
                    {
                        Log.Warn("game snow: RainParticleProperties has no Material field '" + CopyNames[i] +
                                 "', so its snowflakes cannot follow the clouds: they fall everywhere, as without the mod");
                        return _copies = null;
                    }
                }

                _copies = found;
            }

            return _copies != null && _copies.Length > 0 ? _copies : null;
        }

        private string GameIntensity()
        {
            foreach (RainParticleProperties particles in _particles)
            {
                if (particles != null)
                    return "intensity " + Mathf.Lerp(0f, particles.m_MaxRainIntensity, particles.m_RainControl).ToString("F2") +
                           " (max " + particles.m_MaxRainIntensity.ToString("F2") + ")";
            }

            return "intensity (none)";
        }

        private static string Share(float value)
        {
            return (value * 100f).ToString("F0") + "%";
        }

        private static string Describe(Material material)
        {
            if (material == null)
                return "none";

            return "'" + material.name + "' shader '" + (material.shader == null ? "null" : material.shader.name) + "'";
        }

        private void OnDestroy()
        {
            if (_hooked)
                Camera.onPreCull -= ScaleSnow;

            _scalingSnow = false;
            Restore();

            if (_invisible != null)
                Destroy(_invisible);
        }
    }
}
