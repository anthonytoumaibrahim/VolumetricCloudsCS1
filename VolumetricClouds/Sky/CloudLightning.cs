using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Lightning that belongs to the clouds: every strike lights the cloud layer and the rain
    /// from inside, and comes down as a bolt from OUR cloud base.
    /// </summary>
    /// <remarks>
    /// Where strikes come from (read from the IL; the scheduler in RainProperties is a red
    /// herring -- that component is disabled in a normal game):
    ///   WeatherManager.QueueLightningStrike  called by the weather's own simulation step in
    ///       heavy rain and by ThunderStormAI. Entries of {m_startFrame, m_position,
    ///       m_rotation} go into the private m_lightningQueue. StrikeNow plays the thunder
    ///       and sets fire to whatever was hit.
    ///   WeatherManager.EndRenderingImpl      draws each entry for 30 simulation frames: its
    ///       bolt MODEL, a small light at the base, and a 10 km-range light 500 m up.
    /// Those lights reach the city but not our clouds, which only know the sun, the moon and
    /// the ambient. So this reads the same queue and gives the cloud shader a point source
    /// inside the layer above each strike, flickering with the game's own formula
    /// (<see cref="LightningFlicker"/>) so cloud, ground and bolt pulse as one. The game's strikes are
    /// never created, moved or suppressed here: they start fires.
    ///
    /// On top of that comes purely visual lightning -- no fire, nothing the simulation can
    /// see -- whose rate follows the rain or the player's override. It only ever starts
    /// INSIDE cloud: candidates are tested against the same weather field the clouds are cut
    /// from, and a clear sky has none.
    ///
    /// The game's bolt model is hidden by swapping its public material for Invisible.shader;
    /// the mesh cannot simply be nulled, because the same block draws the ground lights.
    /// </remarks>
    public class CloudLightning : MonoBehaviour
    {
        public const int MaxFlashes = 4;

        private const uint StrikeFrames = LightningFlicker.StrikeFrames;
        private const float LogInterval = 5f;
        private const float SpeedOfSound = 343f;
        private const float MaxThunderDelay = 6f;

        private static readonly int IdFlashCount = Shader.PropertyToID("_FlashCount");
        private static readonly int IdFlashPos = Shader.PropertyToID("_FlashPos");
        private static readonly int IdFlashColor = Shader.PropertyToID("_FlashColor");
        private static readonly int IdBoltColor = Shader.PropertyToID("_BoltColor");
        private static readonly int IdBoltIntensity = Shader.PropertyToID("_BoltIntensity");
        private static readonly int IdPixelAngle = Shader.PropertyToID("_PixelAngle");

        // What the cloud pass reads. Always MaxFlashes long: Unity fixes an array's size on first use.
        private static readonly Vector4[] FlashPos = new Vector4[MaxFlashes];
        private static readonly Vector4[] FlashColor = new Vector4[MaxFlashes];
        private static int _flashCount;
        private static bool _testRequested;

        private sealed class Strike
        {
            public uint StartFrame;
            public Vector3 Ground;
            public Vector3 Source;
            public LightningFlicker.Profile Profile;
            public bool FromGame;
            public bool IsTest;
            public Mesh Bolt;
            public float ThunderIn = -1f;
            public float RollIn = -1f;      // a second, lower roll of thunder, for some strikes
            public float Intensity;
            public bool Seen;

            /// <summary>Sheet lightning is seen through more cloud than a stroke to the ground.</summary>
            public float Power
            {
                get { return Profile.Power * (Bolt != null || FromGame ? 1f : 0.8f); }
            }

            public bool Flashing(uint frame)
            {
                return frame >= StartFrame && frame < StartFrame + Profile.Frames;
            }
        }

        private readonly Dictionary<ulong, Strike> _gameStrikes = new Dictionary<ulong, Strike>();
        private readonly List<Strike> _ownStrikes = new List<Strike>();
        private readonly List<Strike> _active = new List<Strike>();
        private readonly List<ulong> _expired = new List<ulong>();

        private CloudDensityField _field;
        private Camera _camera;
        private FieldInfo _queueField;
        private bool _queueResolved;

        private Material _boltMaterial;
        private Material _invisible;
        private Material _gameBoltMaterial;
        private bool _gameBoltHidden;
        private MaterialPropertyBlock _block;

        private double _ownClock = 1000.0;
        private bool _testPlaying;
        private int _testCount;
        private System.Random _random = new System.Random();
        private Randomizer _soundRandomizer = new Randomizer(1u);
        private float _reduction;
        private float _lastTotal;
        private float _nextLogTime;
        private int _gameSeen;
        private int _ownSpawned;
        private int _ownSkippedNoCloud;

        public void Initialise(CloudDensityField field)
        {
            _field = field;
        }

        /// <summary>Sets the lightning uniforms of the cloud pass. Safe when nothing is running.</summary>
        public static void ApplyTo(Material material)
        {
            material.SetFloat(IdFlashCount, _flashCount);
            material.SetVectorArray(IdFlashPos, FlashPos);
            material.SetVectorArray(IdFlashColor, FlashColor);
        }

        /// <summary>A visual-only strike in front of the camera, for judging the look.</summary>
        public static void RequestTest()
        {
            _testRequested = true;
        }

        private void Start()
        {
            _block = new MaterialPropertyBlock();

            Shader bolt = ShaderBundle.Get(ShaderBundle.LightningBolt);
            if (bolt != null)
                _boltMaterial = new Material(bolt) { name = "VolumetricCloudsLightningBolt" };

            Shader invisible = ShaderBundle.Get(ShaderBundle.Invisible);
            if (invisible != null)
                _invisible = new Material(invisible) { name = "VolumetricCloudsInvisibleBolt" };

            Log.Msg("lightning: ready (bolt shader " + (_boltMaterial != null ? "ok" : "MISSING") + ")");
        }

        private void LateUpdate()
        {
            if (_field == null || !Singleton<WeatherManager>.exists || !Singleton<SimulationManager>.exists)
                return;

            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return;

            bool enabled = (Settings.LightningEnabled == null || Settings.LightningEnabled.value)
                        && (Settings.CloudsVisible == null || Settings.CloudsVisible.value);

            HideGameBolt(enabled && _boltMaterial != null && _invisible != null
                         && (Settings.LightningReplaceBolt == null || Settings.LightningReplaceBolt.value));

            if (!enabled)
            {
                Clear();
                return;
            }

            SimulationManager simulation = Singleton<SimulationManager>.instance;
            bool paused = simulation.SimulationPaused || simulation.ForcedSimulationPaused;
            uint frame = simulation.m_referenceFrameIndex;

            // Our own strikes run on their own 60 Hz clock. It stops with the game like
            // everything else -- except while a test strike is playing, so the Test button
            // shows a whole flash even when pressed with the game paused.
            if (!paused || _testPlaying)
                _ownClock += Time.deltaTime * 60.0;
            uint ownFrame = (uint)_ownClock;

            ReadGameStrikes(frame);
            SpawnOwnStrikes(ownFrame, paused);
            Evaluate(frame, ownFrame, paused);
            DrawBolts();

            if (Time.time >= _nextLogTime)
            {
                _nextLogTime = Time.time + LogInterval;
                Log.Msg("lightning: activity=" + Activity().ToString("F2") +
                        (Overridden ? " (override)" : " (from rain " + CloudWeather.Rain.ToString("F2") + ")") +
                        " | last 5s: game strikes=" + _gameSeen + " own=" + _ownSpawned +
                        " skippedNoCloud=" + _ownSkippedNoCloud +
                        " | active=" + _active.Count + " flashingReduction=" + _reduction.ToString("F2") +
                        " gameBolt=" + (_gameBoltHidden ? "hidden" : "shown"));
                _gameSeen = 0;
                _ownSpawned = 0;
                _ownSkippedNoCloud = 0;
            }
        }

        // ---- the game's strikes -------------------------------------------------------------

        private void ReadGameStrikes(uint frame)
        {
            foreach (Strike strike in _gameStrikes.Values)
                strike.Seen = false;

            FastList<WeatherManager.LightningStrike> queue = Queue();
            if (queue != null)
            {
                try
                {
                    // The simulation thread appends to this list. Take the buffer first and
                    // never index past it, exactly as careful as the game's own renderer.
                    WeatherManager.LightningStrike[] buffer = queue.m_buffer;
                    int count = buffer == null ? 0 : Mathf.Min(queue.m_size, buffer.Length);

                    for (int i = 0; i < count; i++)
                    {
                        uint start = buffer[i].m_startFrame;
                        if (frame < start || frame >= start + StrikeFrames)
                            continue;

                        Vector3 ground = buffer[i].m_position;
                        ulong key = ((ulong)start << 32) ^ (uint)ground.GetHashCode();

                        Strike strike;
                        if (!_gameStrikes.TryGetValue(key, out strike))
                        {
                            strike = NewStrike(start, ground, true, true);
                            _gameStrikes.Add(key, strike);
                            _gameSeen++;

                            Log.Msg("lightning: game strike at (" + ground.x.ToString("F0") + ", " + ground.z.ToString("F0") +
                                    "), " + Vector3.Distance(ground, _camera.transform.position).ToString("F0") +
                                    " m away, cloud above=" + CloudAt(ground).ToString("F2"));
                        }

                        strike.Seen = true;
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Reading the game's lightning queue threw.", e);
                }
            }

            _expired.Clear();
            foreach (KeyValuePair<ulong, Strike> pair in _gameStrikes)
            {
                if (!pair.Value.Seen)
                    _expired.Add(pair.Key);
            }

            foreach (ulong key in _expired)
            {
                Release(_gameStrikes[key]);
                _gameStrikes.Remove(key);
            }
        }

        private FastList<WeatherManager.LightningStrike> Queue()
        {
            if (!_queueResolved)
            {
                _queueResolved = true;
                _queueField = typeof(WeatherManager).GetField("m_lightningQueue",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (_queueField == null)
                    Log.Warn("lightning: WeatherManager.m_lightningQueue not found; only visual lightning will show.");
            }

            return _queueField == null
                ? null
                : _queueField.GetValue(Singleton<WeatherManager>.instance) as FastList<WeatherManager.LightningStrike>;
        }

        // ---- our own, visual-only lightning ----------------------------------------------------

        private static bool Overridden
        {
            get { return Settings.LightningOverride != null && Settings.LightningOverride.value; }
        }

        /// <summary>
        /// 0..1. Follows the rain -- nothing below a heavy shower, a proper storm in a
        /// downpour -- unless the player sets it, e.g. for a dry thunderstorm. Either way it
        /// needs cloud to happen in.
        /// </summary>
        private static float Activity()
        {
            if (CloudWeather.Coverage < 0.15f)
                return 0f;

            if (Overridden)
                return Settings.LightningActivity != null ? Mathf.Clamp01(Settings.LightningActivity.value) : 0f;

            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, CloudWeather.Rain));
        }

        private void SpawnOwnStrikes(uint frame, bool paused)
        {
            _testPlaying = false;

            for (int i = _ownStrikes.Count - 1; i >= 0; i--)
            {
                Strike strike = _ownStrikes[i];
                bool flashing = frame < strike.StartFrame + strike.Profile.Frames;
                bool thunderDue = strike.ThunderIn >= 0f || strike.RollIn >= 0f;

                if (!flashing && !thunderDue)
                {
                    Release(strike);
                    _ownStrikes.RemoveAt(i);
                }
                else if (strike.IsTest)
                {
                    _testPlaying = true;
                }
            }

            bool test = _testRequested;
            _testRequested = false;

            if (!test)
            {
                if (paused)
                    return;

                float activity = Activity();
                if (activity <= 0f)
                    return;

                // Real time, not simulation time: at 3x speed a storm should not strobe.
                float interval = Mathf.Lerp(30f, 3f, activity);
                if (_random.NextDouble() > Time.deltaTime / interval)
                    return;
            }

            Vector3 camera = _camera.transform.position;
            Vector3 spot = Vector3.zero;
            bool found = false;

            if (test)
            {
                Vector3 forward = _camera.transform.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;
                spot = camera + forward * 1200f;
                found = true;
            }
            else
            {
                // Only ever inside cloud.
                for (int attempt = 0; attempt < 10 && !found; attempt++)
                {
                    float angle = (float)_random.NextDouble() * Mathf.PI * 2f;
                    float distance = Mathf.Lerp(800f, 7000f, Mathf.Sqrt((float)_random.NextDouble()));
                    spot = camera + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                    found = CloudAt(spot) > 0.6f;
                }
            }

            if (!found)
            {
                _ownSkippedNoCloud++;
                return;
            }

            spot.y = Singleton<TerrainManager>.exists
                ? Singleton<TerrainManager>.instance.SampleRawHeightSmooth(spot)
                : 0f;

            // Most lightning stays in the cloud; some of it comes down. The test button
            // alternates, so both kinds can be judged.
            bool toGround = test ? (_testCount++ % 2 == 0) : _random.NextDouble() < 0.35;
            Strike own = NewStrike(frame, spot, false, toGround);

            float delay = Mathf.Min(Vector3.Distance(camera, own.Source) / SpeedOfSound, MaxThunderDelay);
            own.ThunderIn = delay;

            // A long strike, or now and then any strike, rolls on after the first clap.
            if (own.Profile.Frames > 45u || _random.NextDouble() < 0.3)
                own.RollIn = delay + Mathf.Lerp(0.5f, 1.6f, (float)_random.NextDouble());

            own.IsTest = test;
            _testPlaying |= test;

            LightningFlicker.Profile p = own.Profile;
            Log.Msg("lightning: own " + (toGround ? "bolt" : "sheet") + (test ? " (test)" : "") +
                    " lasts " + (p.Frames / 60f).ToString("F2") + "s, " + p.Strokes + " stroke(s), flicker every " +
                    p.Reseed + " frames, power " + p.Power.ToString("F2") + ", reach " + p.Reach.ToString("F0") + " m");

            _ownStrikes.Add(own);
            _ownSpawned++;
        }

        // ---- shared ---------------------------------------------------------------------------

        private Strike NewStrike(uint frame, Vector3 ground, bool fromGame, bool withBolt)
        {
            float bottom = Settings.CloudAltitude != null ? Settings.CloudAltitude.value : Settings.Defaults.Altitude;
            float thickness = Settings.CloudThickness != null ? Settings.CloudThickness.value : Settings.Defaults.Thickness;

            System.Random random = new System.Random(unchecked((int)(frame * 2654435761u) ^ ground.GetHashCode()));
            // Where in the cloud it starts varies too: a stroke to the ground leaves the lower
            // part of the layer, sheet lightning can be anywhere inside it.
            float depth = withBolt
                ? Mathf.Lerp(0.18f, 0.45f, (float)random.NextDouble())
                : Mathf.Lerp(0.3f, 0.75f, (float)random.NextDouble());

            Vector3 source = new Vector3(
                ground.x + ((float)random.NextDouble() - 0.5f) * 500f,
                bottom + Mathf.Max(50f, thickness) * depth,
                ground.z + ((float)random.NextDouble() - 0.5f) * 500f);

            // No two alike. The game's strikes keep the game's timing (its ground lights and
            // thunder run on it); ours vary in everything.
            Strike strike = new Strike
            {
                StartFrame = frame,
                Ground = ground,
                Source = source,
                Profile = fromGame
                    ? LightningFlicker.Profile.ForGameStrike(random)
                    : LightningFlicker.Profile.Random(random, withBolt),
                FromGame = fromGame,
            };

            if (withBolt && _boltMaterial != null && source.y > ground.y + 50f)
                strike.Bolt = LightningBoltMesh.Build(source, ground, random);

            return strike;
        }

        /// <summary>Cloud amount above a point, 0..1, from the same field and wind as the clouds.</summary>
        private float CloudAt(Vector3 position)
        {
            float tile = Mathf.Max(500f, Settings.WeatherTileSize != null ? Settings.WeatherTileSize.value : Settings.Defaults.WeatherTileSize);
            return _field.SampleCloud((position.x - CloudWind.Offset.x) / tile,
                                      (position.z - CloudWind.Offset.z) / tile,
                                      CloudWeather.Coverage);
        }

        private void Evaluate(uint gameFrame, uint ownFrame, bool paused)
        {
            // The game damps rapid flashing (m_flashingReduction) but only knows its own
            // strikes. Ours get the same treatment, from the same rule, over everything.
            float reduction = Mathf.Max(_reduction, Singleton<WeatherManager>.instance.m_flashingReduction);

            _active.Clear();
            float total = 0f;

            foreach (Strike strike in _gameStrikes.Values)
                total += Activate(strike, gameFrame, reduction);
            foreach (Strike strike in _ownStrikes)
                total += Activate(strike, ownFrame, reduction);

            if (!paused || _testPlaying)
            {
                _reduction = Mathf.Clamp01(_reduction + Mathf.Abs(total - _lastTotal) * 0.1f - Time.deltaTime * 0.3f);
                _lastTotal = total;
            }

            // The strongest few reach the shader.
            _active.Sort((a, b) => (b.Intensity * b.Power).CompareTo(a.Intensity * a.Power));

            float brightness = Settings.LightningBrightness != null ? Mathf.Max(0f, Settings.LightningBrightness.value) : 1f;
            _flashCount = Mathf.Min(_active.Count, MaxFlashes);

            for (int i = 0; i < MaxFlashes; i++)
            {
                if (i < _flashCount)
                {
                    Strike strike = _active[i];
                    Color colour = strike.Profile.Tint * (5f * brightness * strike.Power * strike.Intensity);
                    FlashPos[i] = new Vector4(strike.Source.x, strike.Source.y, strike.Source.z, strike.Profile.Reach);
                    FlashColor[i] = new Vector4(colour.r, colour.g, colour.b, 0f);
                }
                else
                {
                    FlashPos[i] = new Vector4(0f, 0f, 0f, 1f);
                    FlashColor[i] = Vector4.zero;
                }
            }

            PlayThunder(paused);
        }

        private float Activate(Strike strike, uint frame, float reduction)
        {
            strike.Intensity = 0f;
            if (!strike.Flashing(frame))
                return 0f;

            strike.Intensity = LightningFlicker.Intensity(frame, strike.StartFrame, reduction, strike.Profile);
            _active.Add(strike);
            return strike.Intensity;
        }

        /// <summary>
        /// The game thunders for its own strikes (StrikeNow). Ours borrow the same sound, the
        /// same way, after the time the sound would take to arrive.
        /// </summary>
        private void PlayThunder(bool paused)
        {
            if (paused && !_testPlaying)
                return;

            foreach (Strike strike in _ownStrikes)
            {
                // The clap: louder for a powerful strike and for one that reaches the ground.
                if (Due(ref strike.ThunderIn))
                {
                    float volume = Mathf.Clamp01((strike.Bolt != null ? 0.7f : 0.45f) * (0.6f + 0.5f * strike.Profile.Power));
                    Thunder(strike, volume, _soundRandomizer.Int32(620, 1150) * 0.001f);
                }

                // The roll that follows some of them: quieter, and pitched well down.
                if (Due(ref strike.RollIn))
                    Thunder(strike, 0.35f, _soundRandomizer.Int32(450, 700) * 0.001f);
            }
        }

        /// <summary>Counts a timer down; true on the frame it runs out. Negative means not pending.</summary>
        private static bool Due(ref float timer)
        {
            if (timer < 0f)
                return false;

            timer -= Time.deltaTime;
            if (timer > 0f)
                return false;

            timer = -1f;
            return true;
        }

        private void Thunder(Strike strike, float volume, float pitch)
        {
            WeatherProperties properties = Singleton<WeatherManager>.instance.m_properties;
            if (properties == null || properties.m_lightningSound == null || !Singleton<AudioManager>.exists)
                return;

            try
            {
                AudioManager audio = Singleton<AudioManager>.instance;
                audio.AddEvent(audio.EffectGroup, properties.m_lightningSound.GetVariation(ref _soundRandomizer),
                               strike.Bolt != null ? strike.Ground : strike.Source, Vector3.zero,
                               10000f, volume, pitch);
            }
            catch (Exception e)
            {
                Log.Error("Playing thunder threw.", e);
            }
        }

        private void DrawBolts()
        {
            if (_boltMaterial == null)
                return;

            float brightness = Settings.LightningBrightness != null ? Mathf.Max(0f, Settings.LightningBrightness.value) : 1f;
            _boltMaterial.SetFloat(IdPixelAngle,
                2f * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, _camera.pixelHeight));

            bool replaceGame = _gameBoltHidden;

            foreach (Strike strike in _active)
            {
                if (strike.Bolt == null || strike.Intensity <= 0.01f)
                    continue;

                // The game's strike keeps the game's bolt unless ours is replacing it.
                if (strike.FromGame && !replaceGame)
                    continue;

                // Per strike, so each bolt has the colour of its own flash in the cloud.
                Color colour = strike.Profile.Tint * (22f * brightness);

                _block.Clear();
                _block.SetVector(IdBoltColor, new Vector4(colour.r, colour.g, colour.b, 0f));
                _block.SetFloat(IdBoltIntensity, strike.Intensity * strike.Power);
                Graphics.DrawMesh(strike.Bolt, Matrix4x4.identity, _boltMaterial, 0, null, 0, _block);
            }
        }

        /// <summary>
        /// The game reads WeatherProperties.m_lightningMaterial at draw time, in the same block
        /// that draws the strike's ground lights -- so the material is swapped rather than the
        /// mesh nulled, which would take the lights with it.
        /// </summary>
        private void HideGameBolt(bool hide)
        {
            WeatherProperties properties = Singleton<WeatherManager>.instance.m_properties;
            if (properties == null)
                return;

            if (hide)
            {
                if (!_gameBoltHidden)
                {
                    _gameBoltMaterial = properties.m_lightningMaterial;
                    _gameBoltHidden = true;
                    Log.Msg("lightning: game bolt hidden (its material was " +
                            (_gameBoltMaterial == null ? "none" : "'" + _gameBoltMaterial.name + "'") +
                            "); its ground lights and thunder are untouched");
                }

                if (properties.m_lightningMaterial != _invisible)
                    properties.m_lightningMaterial = _invisible;
            }
            else if (_gameBoltHidden)
            {
                _gameBoltHidden = false;
                if (_gameBoltMaterial != null)
                    properties.m_lightningMaterial = _gameBoltMaterial;

                Log.Msg("lightning: game bolt restored");
            }
        }

        private void Clear()
        {
            foreach (Strike strike in _gameStrikes.Values)
                Release(strike);
            foreach (Strike strike in _ownStrikes)
                Release(strike);

            _gameStrikes.Clear();
            _ownStrikes.Clear();
            _active.Clear();
            _flashCount = 0;
        }

        private static void Release(Strike strike)
        {
            if (strike.Bolt != null)
            {
                Destroy(strike.Bolt);
                strike.Bolt = null;
            }
        }

        private void OnDestroy()
        {
            Clear();

            if (Singleton<WeatherManager>.exists)
                HideGameBolt(false);

            if (_boltMaterial != null)
                Destroy(_boltMaterial);
            if (_invisible != null)
                Destroy(_invisible);
        }
    }
}
