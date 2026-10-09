using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// How much fog there is, how far it has drifted and how far it has turned over. Advanced
    /// from the one place that advances the wind, the cover and the rain (CloudLighting).
    /// </summary>
    /// <remarks>
    /// By default the amount follows the game's weather (WeatherManager.m_currentFog: 0, or
    /// 0.25..1 when the game rolls a foggy spell); the override is for fog on demand.
    ///
    /// The fog is drawn in the cloud pass (CloudRaymarch: FogAt / MarchFog) as a cloud layer
    /// lying on the ground, which it knows from <see cref="TerrainHeightMap"/>. "Amount" is
    /// the share of the map that has fog on it, through the same solved threshold table as
    /// the clouds' intensity: 30% is fog over about a third of the map, 100% is fog
    /// everywhere. How thick it is inside is a separate setting.
    ///
    /// 1.5.0: the weather's fog is one of three sources, with the morning fog and the mist after
    /// rain; Amount is the largest. Each gives the fog a LOOK (Sky.FogSources) -- how low, thin and
    /// wispy it is inside the room the fog sliders give it, which they never exceed. With the three
    /// "Changing fog" rows at 0% the look is neutral and everything here is as it was.
    ///
    /// History, because this took three attempts: a height-limited blanket, then soft
    /// kilometre-wide banks with an exponential falloff. Both were called "a coating the
    /// way RenderIt does". They were translucent, had no boundary and did not shade
    /// themselves -- the three things that make a cloud read as a volume.
    /// </remarks>
    public static class CloudFog
    {
        /// <summary>Real seconds for the fog to follow a change. Fog rolls in; it does not pop.</summary>
        private const float FollowSeconds = 5f;

        /// <summary>
        /// Extinction per metre inside the fog at 100% density: visibility of about 330 m, and
        /// from above a 90 m layer still passes around 60% of what is under it -- fog you see
        /// the city THROUGH, with body where it is thick. The third version shipped at 0.028
        /// (visibility 110 m, nearly opaque from above), which was judged to "just look like
        /// the clouds but at the terrain level"; that look is still there at ~300% on the
        /// slider. The first two were thinner again AND had no boundary or shading, which is
        /// what made them a coating: density was never the only thing wrong with them.
        /// </summary>
        public const float BaseExtinction = 0.009f;

        /// <summary>
        /// Width of the soft edge of a fog patch, in weather-field units. The shader has the
        /// same literal (FogAt: "/ 0.1"); the honest threshold below is solved for it.
        /// </summary>
        public const float CoverSoftness = 0.1f;

        /// <summary>
        /// Metres one tile of the weather field spans when it is read for fog. The clouds read
        /// it at 20 km; at 7 km the same pattern gives patches from a few hundred metres to a
        /// couple of kilometres across.
        /// </summary>
        public const float Tile = 7000f;

        /// <summary>Metres per second the fog drifts at 1x on the slider, before simulation speed.</summary>
        private const float DriftSpeed = 6f;

        /// <summary>Seconds for the swirl to turn over once, at 1x.</summary>
        private const float BoilPeriod = 240f;

        // The fog's noise lookups: metres per repeat, and how fast each drifts against the fog
        // itself. The shader reads the tiles from here (uniforms), so the phases worked out
        // against them can never disagree with the divisors it uses -- a mismatch would be a
        // jump every repeat, easy to miss in a short test.
        public const float BillowTile = 360f;
        public const float BillowTileVertical = 210f;
        public const float SwirlTile = 1300f;
        public const float SwirlDrift = 0.55f;
        public const float WispTile = 85f;
        public const float WispDrift = 1.8f;

        private static float _amount;
        private static bool _holdAmount;
        private static double _offsetX;
        private static double _offsetZ;
        private static readonly AmountReporter Reporter = new AmountReporter();

        // 1.5.0: the three sources of fog (Sky.FogSources) and the look they give it. The weather's
        // amount is what _amount was before; _amount is now the largest of the three.
        private static float _weatherAmount;
        private static float _morningAmount;
        private static float _mistAmount;
        private static FogLook _look = FogLook.Neutral;

        // What the one-shot lines last said, so each is said once per change.
        private static int _morningPhase;
        private static float _morningPeak;
        private static bool _misty;
        private static bool _toldNoDayNight;
        private static bool _toldSnow;

        // Two things worked out from the game's code and never measured, measured once a session:
        // a day/night cycle in real time, and the game hours the ground takes to dry.
        private static uint _lastCycle = uint.MaxValue;
        private static float _cycleStartedAt = -1f;
        private static bool _toldCycle;
        private static float _lastWetness = -1f;
        private static uint _dryingFrom;
        private static bool _drying;

        /// <summary>True while our fog replaces the game's foggy-weather look.</summary>
        public static bool Active { get; private set; }

        /// <summary>
        /// 0..1, smoothed: the share of the map with fog on it -- the largest of the weather's fog,
        /// the morning fog and the mist (1.5.0; with those two at 0%, the weather's fog as before).
        /// </summary>
        public static float Amount
        {
            get { return _amount; }
        }

        /// <summary>
        /// How the fog looks now, as shares of what its sliders allow (1.5.0, Sky.FogSources).
        /// Neutral -- the sliders exactly -- unless one of the three "Changing fog" rows is above 0%.
        /// </summary>
        public static FogLook Look
        {
            get { return _look; }
        }

        /// <summary>The game's own fog value, 0..1.</summary>
        public static float GameFog
        {
            get { return Singleton<WeatherManager>.exists ? Mathf.Clamp01(Singleton<WeatherManager>.instance.m_currentFog) : 0f; }
        }

        /// <summary>
        /// The game's ground wetness, 0..1: up while it rains, then drying on its own over some game
        /// hours (WeatherManager.SimulationStepImpl). Saved in the city by the game.
        /// </summary>
        public static float GroundWetness
        {
            get { return Singleton<WeatherManager>.exists ? Mathf.Clamp01(Singleton<WeatherManager>.instance.m_groundWetness) : 0f; }
        }

        /// <summary>"Fog changes with the weather", 0..1.</summary>
        public static float ChangeWithWeather
        {
            get { return Settings.FogChangesWithWeather != null ? Mathf.Clamp01(Settings.FogChangesWithWeather.value) : Settings.Defaults.FogChangesWithWeather; }
        }

        /// <summary>"Morning fog", 0..1.</summary>
        public static float MorningSetting
        {
            get { return Settings.MorningFog != null ? Mathf.Clamp01(Settings.MorningFog.value) : Settings.Defaults.MorningFog; }
        }

        /// <summary>"Mist after rain", 0..1.</summary>
        public static float MistSetting
        {
            get { return Settings.MistAfterRain != null ? Mathf.Clamp01(Settings.MistAfterRain.value) : Settings.Defaults.MistAfterRain; }
        }

        public static bool Overridden
        {
            get { return Settings.FogOverride != null && Settings.FogOverride.value; }
        }

        /// <summary>
        /// True: heights are measured from the ground under the fog, so it drapes over hills.
        /// False (the default): from the map's sea level, so it is level, like the game's.
        /// </summary>
        public static bool FollowsGround
        {
            get { return Settings.FogFollowsGround != null ? Settings.FogFollowsGround.value : Settings.Defaults.FogFollowsGround; }
        }

        /// <summary>Metres from the reference to the underside of the layer. 0 = it starts at the reference.</summary>
        public static float Base
        {
            get { return Settings.FogBase != null ? Mathf.Max(0f, Settings.FogBase.value) : Settings.Defaults.FogBase; }
        }

        /// <summary>
        /// Metres from the underside of the layer to its top. Floored at 1 m, not 0: the shaders
        /// divide by it (0 on the slider is then a layer too thin to see).
        /// </summary>
        public static float Height
        {
            get { return Settings.FogHeight != null ? Mathf.Max(1f, Settings.FogHeight.value) : Settings.Defaults.FogHeight; }
        }

        /// <summary>
        /// Metres from the reference to the underside of the fog as drawn: <see cref="Base"/> raised by
        /// the look (morning fog lifting). Never below Base. With a neutral look, Base exactly.
        /// </summary>
        public static float LayerBase
        {
            get { return Base + _look.Bottom * Height; }
        }

        /// <summary>
        /// Metres from that underside to the top of the fog as drawn: never past the top of
        /// <see cref="Base"/> + <see cref="Height"/>. With a neutral look, Height exactly.
        /// </summary>
        public static float LayerHeight
        {
            get { return Mathf.Max(1f, (_look.Top - _look.Bottom) * Height); }
        }

        /// <summary>The break-up as drawn, 0..1: the slider's, or wispier (never more solid).</summary>
        public static float LayerBreakup(float slider)
        {
            return slider + (1f - slider) * _look.Wisp;
        }

        /// <summary>
        /// Metres the fog is drawn out to: the "Fog distance" setting (1.3.1). Over the last
        /// <see cref="FadeLength"/> of it the fog thins to nothing -- at the default 9 km that is from
        /// the camera on, exactly the fade it always had; farther, it is whole up to there.
        /// </summary>
        public static float Reach
        {
            get { return Settings.FogDistance != null ? Mathf.Max(1000f, Settings.FogDistance.value) : Settings.Defaults.FogDistance; }
        }

        /// <summary>The stretch the fog fades out over, ending at <see cref="Reach"/> (all of a shorter one).</summary>
        public const float FadeLength = 9000f;

        /// <summary>
        /// Whether a point this far above the ground, at this altitude, is between the layer's
        /// underside and its top. The height test only, not the noise field.
        /// </summary>
        public static bool InLayer(float aboveGround, float altitude)
        {
            float above = FollowsGround ? aboveGround : altitude - TerrainHeightMap.SeaLevel;
            float bottom = LayerBase;
            return above >= bottom && above < bottom + LayerHeight;
        }

        /// <summary>
        /// Where the layer is, for the log. A level fog whose range is under the city is the
        /// one way "the fog is on and I see none" can be nobody's bug, so it is spelled out.
        /// </summary>
        public static string DescribeLayer()
        {
            float bottom = LayerBase;
            float top = bottom + LayerHeight;
            string reach = ", drawn to " + (Reach / 1000f).ToString("F1") + " km";

            // While the look moves it (1.5.0), the room the sliders give it too: the layer as drawn
            // is somewhere inside that.
            string room = _look.IsNeutral ? "" : " (the sliders allow " + Base.ToString("F0") + ".." + (Base + Height).ToString("F0") + " m)";

            if (FollowsGround)
                return "follows the ground, " + bottom.ToString("F0") + ".." + top.ToString("F0") + " m above it" + room + reach;

            float sea = TerrainHeightMap.SeaLevel;
            return "level, " + bottom.ToString("F0") + ".." + top.ToString("F0") + " m above sea level (altitude " +
                   (sea + bottom).ToString("F0") + ".." + (sea + top).ToString("F0") + " m)" + room + reach;
        }

        /// <summary>
        /// How far the fog has drifted along x, in metres. Its own offset: fog moves at its own
        /// pace, not the clouds'. Exact, saved with the city, and can be very large.
        /// </summary>
        public static double OffsetX
        {
            get { return _offsetX; }
        }

        /// <summary>How far the fog has drifted along z, in metres.</summary>
        public static double OffsetZ
        {
            get { return _offsetZ; }
        }

        /// <summary>The same as a vector, for the log only: it loses precision far out.</summary>
        public static Vector3 Offset
        {
            get { return new Vector3((float)_offsetX, 0f, (float)_offsetZ); }
        }

        /// <summary>
        /// A lookup's share of the drift (see <see cref="Drift.Phase"/>): x and z of how far
        /// through one repeat of <paramref name="tile"/> a lookup drifting at
        /// <paramref name="rate"/> times the fog has gone.
        /// </summary>
        public static Vector4 Phase(float rate, float tile)
        {
            return new Vector4(Drift.Phase(_offsetX * rate, tile), Drift.Phase(_offsetZ * rate, tile), 0f, 0f);
        }

        /// <summary>Metres the top of the layer is carried beyond its base (x, z): <see cref="Drift.FogLead"/>.</summary>
        public static Vector4 Lead
        {
            get
            {
                float x, z;
                Drift.FogLead(_offsetX, _offsetZ, out x, out z);
                return new Vector4(x, z, 0f, 0f);
            }
        }

        /// <summary>Phase of the swirl, 0..1, wrapping. The shader scrolls its noise by whole multiples of it.</summary>
        public static float Boil { get; private set; }

        /// <summary>
        /// The player has asked for volumetric fog. Off by default -- it is the heaviest thing
        /// the mod draws -- and when it is off nothing here runs at all, including the terrain
        /// height map that exists only to carry it.
        /// </summary>
        public static bool Enabled
        {
            get
            {
                return (Settings.FogEnabled != null && Settings.FogEnabled.value)
                    && (Settings.CloudsVisible == null || Settings.CloudsVisible.value);
            }
        }

        public static void Advance(float deltaTime, bool cloudPassRunning, Vector3 windDirection, float simulationRate)
        {
            bool wanted = Enabled;
            bool wasActive = Active;

            // The fog is drawn by the volumetric cloud pass and lies on the terrain map;
            // without either, the game keeps its own.
            Active = wanted && cloudPassRunning && TerrainHeightMap.Ready;

            if (!wanted)
            {
                // Everything below this line is work the fog does not need done while it is
                // switched off -- but Active and the amount are set FIRST and always: GameFog
                // keys on Active, so returning above it would leave the game's own
                // foggy-weather reaction switched off for ever with nothing of ours in its place.
                _amount = 0f;
                _weatherAmount = 0f;
                _morningAmount = 0f;
                _mistAmount = 0f;
                _look = FogLook.Neutral;
                _holdAmount = false;

                // Switched off, there is no morning fog or mist to have "gone": said again from
                // scratch when it is switched back on.
                _morningPhase = 0;
                _misty = false;
                Report();
                return;
            }

            // Frozen while paused, like the clouds; faster with the simulation, but capped --
            // fog racing across the map at 3x reads as smoke.
            float speed = Settings.FogSpeed != null ? Mathf.Max(0f, Settings.FogSpeed.value) : Settings.Defaults.FogSpeed;
            float moved = deltaTime * Mathf.Min(simulationRate, 2f) * speed;
            _offsetX += (double)windDirection.x * (DriftSpeed * moved);
            _offsetZ += (double)windDirection.z * (DriftSpeed * moved);
            Boil = Mathf.Repeat(Boil + moved / BoilPeriod, 1f);

            // An amount put back from a save waits for the fog to be drawn: until the terrain
            // map is measured and the cloud pass runs there is no fog on screen, and easing
            // towards nothing in the meantime would undo what the save restored -- a foggy city
            // would open by growing its fog in again.
            if (_holdAmount)
            {
                if (!Active)
                    return;

                _holdAmount = false;
            }

            // The clock and the ground, for the morning fog and the mist (1.5.0). The visual clock,
            // as SunsetLight's: sunrise 6h, sunset 18h on every map. With the day/night cycle off
            // the game parks it at about 11:08, so no morning ever comes.
            DayNightProperties sky = DayNightProperties.instance;
            float hour = sky != null ? sky.m_TimeOfDay : 12f;
            bool dayNight = false;
            uint cycle = 0u;
            uint frame = 0u;
            if (Singleton<SimulationManager>.exists)
            {
                SimulationManager simulation = Singleton<SimulationManager>.instance;
                dayNight = simulation.m_enableDayNight;
                frame = simulation.m_currentFrameIndex;
                cycle = (frame + simulation.m_dayTimeOffsetFrames) >> 16;
            }

            float wetness = GroundWetness;

            float target = 0f;
            float morningTarget = 0f;
            float mistTarget = 0f;
            if (Active)
            {
                target = Overridden
                    ? (Settings.FogAmount != null ? Mathf.Clamp01(Settings.FogAmount.value) : 0f)
                    : GameFog;

                float morning = MorningSetting;
                if (morning > 0f && dayNight)
                {
                    _morningPeak = morning * FogSources.MorningPeak(FogSources.MorningKey(cycle, hour));
                    morningTarget = _morningPeak * FogSources.MorningShare(hour);
                }

                float mist = MistSetting;
                if (mist > 0f)
                    mistTarget = mist * FogSources.MistShare(wetness, CloudWeather.Rain, CloudRain.IsSnow);
            }

            // Each source follows its target over the same few seconds, so none of them pops; the
            // morning fog and the mist start where they are when the fog first shows (a city opens
            // with its morning fog already lying there), as the look does below.
            float k = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / FollowSeconds);
            bool appearing = Active && !wasActive;

            _weatherAmount = Follow(_weatherAmount, target, k);
            _morningAmount = appearing ? morningTarget : Follow(_morningAmount, morningTarget, k);
            _mistAmount = appearing ? mistTarget : Follow(_mistAmount, mistTarget, k);
            _amount = Mathf.Max(_weatherAmount, Mathf.Max(_morningAmount, _mistAmount));

            // The look: the weather's fog's, moved by "Fog changes with the weather", blended with
            // the morning fog's and the mist's by how much of each there is. The SLIDERS are applied
            // where it is used (LayerBase...), so dragging one is as immediate as ever.
            FogLook look = FogSources.Blend(
                _weatherAmount, FogSources.WeatherLook(ChangeWithWeather, _weatherAmount, hour, dayNight),
                _morningAmount, FogSources.MorningLook(hour),
                _mistAmount, FogSources.MistLook);
            _look = appearing ? look : _look.Towards(look, k);

            Report();
            ReportSources(hour, dayNight, cycle, frame, wetness, morningTarget, mistTarget);
        }

        private static float Follow(float value, float target, float k)
        {
            value = Mathf.Lerp(value, target, k);
            return Mathf.Abs(target - value) < 0.0005f ? target : value;
        }

        /// <summary>
        /// The one-shot lines of the morning fog and the mist, and the two measurements. Always on
        /// (Log.Msg): each says something changed, once, and only while its row is above 0%.
        /// </summary>
        private static void ReportSources(float hour, bool dayNight, uint cycle, uint frame, float wetness, float morningTarget, float mistTarget)
        {
            bool morningOn = MorningSetting > 0f;
            bool mistOn = MistSetting > 0f;

            if (morningOn && !dayNight && !_toldNoDayNight)
            {
                _toldNoDayNight = true;
                Log.Msg("fog: morning fog is on, but the day/night cycle is off (the clock is held at about 11:00): none forms");
            }

            if (mistOn && CloudRain.IsSnow && !_toldSnow)
            {
                _toldSnow = true;
                Log.Msg("fog: mist after rain is on, but this map snows: no mist after snow");
            }

            int phase = morningTarget <= 0f ? 0 : (FogSources.MorningLift(hour) > 0f ? 2 : 1);
            if (phase != _morningPhase)
            {
                if (phase == 1)
                    Log.Msg("fog: morning fog forming at " + hour.ToString("F1") + "h (up to " + (_morningPeak * 100f).ToString("F0") +
                            "% of the map this morning, " + (FogSources.MorningTop * 100f).ToString("F0") + "% of the fog height, full density)");
                else if (phase == 2)
                    Log.Msg("fog: morning fog lifting at " + hour.ToString("F1") + "h");
                else
                    Log.Msg("fog: morning fog gone at " + hour.ToString("F1") + "h");

                _morningPhase = phase;
            }

            bool misty = mistTarget > 0f;
            if (misty != _misty)
            {
                _misty = misty;
                Log.Msg(misty
                    ? "fog: mist after rain rising (the ground is " + (wetness * 100f).ToString("F0") + "% wet, rain " + (CloudWeather.Rain * 100f).ToString("F0") + "%)"
                    : "fog: mist after rain gone (the ground is " + (wetness * 100f).ToString("F0") + "% wet, rain " + (CloudWeather.Rain * 100f).ToString("F0") + "%)");
            }

            // How long a day/night cycle lasts in real time (pauses and speed-ups included): the
            // first whole one of the session, while the morning fog is on.
            if (morningOn && dayNight && !_toldCycle && cycle != _lastCycle)
            {
                if (_lastCycle != uint.MaxValue)
                {
                    if (_cycleStartedAt >= 0f)
                    {
                        _toldCycle = true;
                        Log.Msg("fog: midnight; the last day/night cycle took " + ((Time.realtimeSinceStartup - _cycleStartedAt) / 60f).ToString("F1") +
                                " min of real time (pauses and speed changes included)");
                    }

                    _cycleStartedAt = Time.realtimeSinceStartup;
                }

                _lastCycle = cycle;
            }

            // How long the ground takes to dry from 60% to 5% wet, in game hours (worked out from
            // the game's code as about 4.7): the whole stretch of the mist's fade, after each rain.
            if (mistOn && _lastWetness >= 0f)
            {
                if (_lastWetness >= FogSources.MistWetFull && wetness < FogSources.MistWetFull)
                {
                    _drying = true;
                    _dryingFrom = frame;
                }
                else if (_drying && CloudWeather.Rain > 0.01f)
                {
                    // Rain again before it dried: not a drying time.
                    _drying = false;
                }
                else if (_drying && _lastWetness >= FogSources.MistWetStart && wetness < FogSources.MistWetStart)
                {
                    _drying = false;
                    Log.Msg("fog: the ground dried from " + (FogSources.MistWetFull * 100f).ToString("F0") + "% to " + (FogSources.MistWetStart * 100f).ToString("F0") +
                            "% wet in " + ((frame - _dryingFrom) * 24f / 65536f).ToString("F1") + " game hours");
                }
            }

            _lastWetness = wetness;
        }

        /// <summary>
        /// Always on, a line per quarter the fog travels: whether OUR fog is on screen has to be
        /// in a quiet log. The game's fog never reacts to rain and ours follows only the game's
        /// FOG value, so "is that fog ours?" is answered by whether these lines are there.
        /// </summary>
        private static void Report()
        {
            if (!Reporter.Moved(_amount))
                return;

            if (_amount <= 0f)
            {
                Log.Msg("fog: OUR volumetric fog has gone" + (Enabled ? "" : " (it is switched off)"));
                return;
            }

            Log.Msg("fog: OUR volumetric fog is on screen, over " + (_amount * 100f).ToString("F0") + "% of the map -- " +
                    (Overridden
                        ? "the fog override asks for " + ((Settings.FogAmount != null ? Mathf.Clamp01(Settings.FogAmount.value) : 0f) * 100f).ToString("F0") + "%"
                        : "following the game's fog, which is at " + (GameFog * 100f).ToString("F0") + "%") +
                    DescribeSources() +
                    " | " + DescribeLayer() +
                    " | the game's rain is at " + (CloudWeather.Rain * 100f).ToString("F0") + "%");
        }

        /// <summary>
        /// The three sources and the look (1.5.0), for the logs: empty while none of the three
        /// "Changing fog" rows is in use, so the line is what it always was.
        /// </summary>
        public static string DescribeSources()
        {
            if (ChangeWithWeather <= 0f && MorningSetting <= 0f && MistSetting <= 0f && _look.IsNeutral)
                return "";

            float breakup = Settings.FogBreakup != null ? Mathf.Clamp01(Settings.FogBreakup.value) : Settings.Defaults.FogBreakup;
            return " | sources: weather " + Percent100(_weatherAmount) + ", morning fog " + Percent100(_morningAmount) +
                   ", mist " + Percent100(_mistAmount) + " (ground " + Percent100(GroundWetness) + " wet)" +
                   " | look: density " + Percent100(_look.Density) + " of the slider, break-up " + Percent100(breakup) +
                   " -> " + Percent100(LayerBreakup(breakup)) +
                   " (changes with the weather " + Percent100(ChangeWithWeather) + ", morning fog " + Percent100(MorningSetting) +
                   ", mist after rain " + Percent100(MistSetting) + ")";
        }

        private static string Percent100(float fraction)
        {
            return (fraction * 100f).ToString("F0") + "%";
        }

        public static void Reset()
        {
            _amount = 0f;
            _weatherAmount = 0f;
            _morningAmount = 0f;
            _mistAmount = 0f;
            _look = FogLook.Neutral;
            _holdAmount = false;
            Active = false;
            Reporter.Reset();

            _morningPhase = 0;
            _misty = false;
            _toldNoDayNight = false;
            _toldSnow = false;
            _lastCycle = uint.MaxValue;
            _cycleStartedAt = -1f;
            _toldCycle = false;
            _lastWetness = -1f;
            _drying = false;
        }

        /// <summary>
        /// Puts the fog where a save left it: its drift, its swirl, and how much of it there
        /// was (held until the fog is drawn again, see <see cref="Advance"/>). A city without a
        /// saved sky gets zeros: a fresh drift, and the fog growing in as it always has.
        /// </summary>
        public static void Restore(double offsetX, double offsetZ, float boil, float amount)
        {
            _offsetX = offsetX;
            _offsetZ = offsetZ;
            Boil = float.IsNaN(boil) ? 0f : Mathf.Repeat(boil, 1f);
            _amount = float.IsNaN(amount) ? 0f : Mathf.Clamp01(amount);
            _holdAmount = _amount > 0f;

            // The save holds one amount, the largest of the three (1.5.0). It goes to the weather's
            // fog, which eases from there as it always did; the morning fog and the mist are worked
            // out afresh from the clock and the ground when the fog shows, so the city opens with
            // the larger of the two and nothing pops.
            _weatherAmount = _amount;
            _morningAmount = 0f;
            _mistAmount = 0f;
        }

        /// <summary>One line for the panel and the log.</summary>
        public static string Describe()
        {
            // The setting first: with the fog switched off the terrain map is never built, so
            // asking the map first would tell every fog-off player it was "measuring the
            // terrain" for ever.
            if (!Enabled)
                return Localization.Get("Status.Fog.Off");

            if (!Active)
                return Localization.Get(TerrainHeightMap.Ready ? "Status.Fog.Waiting" : "Status.Fog.Measuring");

            // Named after the morning fog or the mist while one of them is what covers the most
            // (1.5.0): "fog 0% -> morning fog over 40% of the map" explains itself.
            string shareKey = "Status.Fog.Share";
            if (_morningAmount > _weatherAmount && _morningAmount >= _mistAmount && _morningAmount > 0.005f)
                shareKey = "Status.Fog.ShareMorning";
            else if (_mistAmount > _weatherAmount && _mistAmount > _morningAmount && _mistAmount > 0.005f)
                shareKey = "Status.Fog.ShareMist";

            string share = Localization.Get(shareKey, Percent(_amount));

            return Overridden
                ? Localization.Get("Status.Fog.Manual", share, Percent(GameFog))
                : Localization.Get("Status.Fog.Following", Percent(GameFog), share);
        }

        private static string Percent(float fraction)
        {
            return Localization.Get("Unit.Percent", (fraction * 100f).ToString("F0"));
        }
    }
}
