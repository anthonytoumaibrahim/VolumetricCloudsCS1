using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// RAINBOWS (1.3.0; the author: "How easy for us is it to add Rainbows? AFAIK the vanilla game
    /// doesn't have rainbows"). Drawn by the rain march (CloudRaymarch.shader) wherever rain stands
    /// in direct sun, at the bow's angles round the point opposite the sun, in the colours of
    /// <see cref="RainbowTable"/>. This class says how strong the bow is this frame, and why not.
    /// </summary>
    /// <remarks>
    /// The game SIMULATES a rainbow and never draws one (IL, 2026-09-29): when rain starts on dry
    /// ground, WeatherManager rolls WeatherProperties.m_rainbowProbability for m_targetRainbow, and
    /// m_currentRainbow rises once the rain is under 25% with the ground still wet;
    /// GetRainbowVisibility() has no caller anywhere. Ours is physics instead (his call): wherever
    /// the sun reaches the rain in front of you, a bow -- as in nature, where every sunlit shower
    /// with the sun behind you has one. Chosen on the offline preview (docs/previews/
    /// render-rainbow.ps1, row B: "B is perfect!"): the curtains keep today's light, only the bow is new.
    ///
    /// Two settings (his: "give control for the user"): "Rainbows" (Weather tab) is the strength,
    /// 100% = nature; 0% switches it off and nothing runs. "Rainbow chance" (Now tab) is the share of
    /// rains that bring one at all: each rain gets its dice when it starts, and has rainbows while the
    /// dice are under the chance, so moving the slider acts at once. 100%, the default, is nature.
    ///
    /// It needs the cloud shadows: their map is what says where the sun reaches the rain (without
    /// it every drop under every cloud would be sunlit). None in snow (flakes make no bow), none with
    /// the sun under the horizon, and never lit by the moon. Faded in over the sun's first 3 degrees.
    /// A fog bank in front of the rain hides the bow (the shader dims it by the fog before it).
    ///
    /// ONE ARCH AT A TIME, FIXED IN THE WORLD (round 2, the same day; his: "one arch at a time, let's
    /// go"): the first build drew each camera's own bow, which followed the camera -- "the rainbow is
    /// happening on the camera lens itself". Now the bow of a SPOT near where the camera looks
    /// (CameraController.m_currentPosition, the point it orbits) is drawn as an arch standing in the
    /// shower in front of that spot (RainbowArch). Every <see cref="LookInterval"/> the arch is
    /// scored -- how much of it stands in sunlit rain, from the CPU twins of the rain mask and the
    /// cloud cover -- and it stays while you look around; it fades out when you have moved
    /// <see cref="MoveAway"/> from its spot or the shower has left it, and a new one forms where
    /// you look. The sun moving turns it slowly, as a real one turns.
    /// </remarks>
    public static class Rainbow
    {
        /// <summary>The sun climbs this many degrees over the horizon before the bow is at full strength.</summary>
        private const float SunFade = 3f;

        /// <summary>
        /// Seen from its spot the arch adds the light the camera's own bow added there (1.3.0's first
        /// build, which he called beautiful): 0.82 measured on the preview.
        /// </summary>
        public const float ArchGain = 0.8f;

        /// <summary>Seconds between looks at the arch (is it still in sunlit rain; a spot for a new one).</summary>
        private const float LookInterval = 2f;

        /// <summary>Seconds an arch takes to appear or to go.</summary>
        private const float FadeTime = 2.5f;

        /// <summary>The camera's look-at point this far (metres, level) from the arch's middle: it goes, and one forms where you look.</summary>
        private const float MoveAway = 3500f;

        /// <summary>The share of the arch in sunlit rain it takes to stand one; half of it to keep one.</summary>
        private const float MinScore = 0.25f;

        /// <summary>Candidate places for the arch's middle round the look-at point: a grid this far each way, at this spacing.</summary>
        private const float SearchReach = 2000f;
        private const float SearchSpacing = 500f;

        /// <summary>The arch's top at this share of the room under the cloud base (the author: "too low to the ground").</summary>
        private const float TopShare = 0.9f;

        /// <summary>Samples along the arch, foot to foot, when scoring it.</summary>
        private const int ArchSamples = 25;

        private static bool _hasArch;
        private static bool _leaving;
        private static float _envelope;
        private static float _nextLook;
        private static float _score;
        private static string _archState;

        /// <summary>Where the arch's spot is (the point it is seen from, in the air in front of it), when there is one.</summary>
        public static Vector3 ArchSpot { get; private set; }

        /// <summary>The middle between the arch's feet, on the ground.</summary>
        public static Vector3 ArchCentre { get; private set; }

        /// <summary>How far the arch stands from its spot, metres.</summary>
        public static float ArchRadius { get; private set; }

        /// <summary>How far in the arch is, 0..1: 0 = none (the shader draws nothing).</summary>
        public static float ArchShown
        {
            get { return _hasArch ? _envelope : 0f; }
        }

        private static bool _raining;

        /// <summary>This rain's dice, 0..1; -1 before the first rain of this city.</summary>
        private static float _dice = -1f;

        /// <summary>Why there is or is not a bow, as last logged: one line per change.</summary>
        private static string _state;

        /// <summary>This frame's strength, for the detail line.</summary>
        public static float Current { get; private set; }

        /// <summary>The "Rainbows" setting: 1 = nature.</summary>
        public static float Amount
        {
            get { return Settings.Rainbows != null ? Mathf.Max(0f, Settings.Rainbows.value) : Settings.Defaults.Rainbows; }
        }

        /// <summary>The "Rainbow chance" setting, 0..1.</summary>
        public static float Chance
        {
            get { return Settings.RainbowChance != null ? Mathf.Clamp01(Settings.RainbowChance.value) : Settings.Defaults.RainbowChance; }
        }

        /// <summary>Whether this rain's dice came under the chance (100% always does, 0% never).</summary>
        private static bool Granted
        {
            get
            {
                float chance = Chance;
                return chance >= 1f || (chance > 0f && _dice >= 0f && _dice < chance);
            }
        }

        /// <summary>Once per frame, from CloudLighting, after CloudRain.Advance: a new rain gets its dice.</summary>
        public static void Advance()
        {
            bool raining = CloudRain.Active && !CloudRain.IsSnow && CloudRain.Amount > 0f;
            if (raining && !_raining)
            {
                _dice = Random.value;
                Log.Msg("rainbow: a new rain -- its dice " + _dice.ToString("F2") + " against the chance of " +
                        Percent(Chance) + ": " + (Granted ? "rainbows wherever the sun shines on it" : "no rainbows with this one"));
            }

            _raining = raining;
        }

        /// <summary>
        /// The bow's strength this frame -- the "Rainbows" setting, faded in over the sun's first
        /// degrees -- or 0, with the reason logged whenever it changes. <paramref name="sunLights"/>:
        /// the sun, not the moon, is what lights the clouds. <paramref name="shadowMap"/>: the cloud
        /// shadow map is there to read. <paramref name="table"/>: the colour texture is there.
        /// </summary>
        public static float Strength(float sunElevation, bool sunLights, bool shadowMap, bool table)
        {
            string state, detail = "";
            float strength = 0f;

            if (Amount <= 0f)
                state = "off (Rainbows 0%)";
            else if (!table)
                state = "none: the colour table could not be made (see above)";
            else if (!CloudRain.Active)
                state = "none: our rain is off or the clouds are hidden";
            else if (CloudRain.IsSnow)
                state = "none: snow makes no rainbow";
            else if (CloudRain.Amount <= 0f)
                state = "none: no rain";
            else if (!Granted)
                state = "none with this rain (its dice against the chance)";
            else if (!Settings.ShadowsCast)
                state = "none: the cloud shadows are off -- they say where the sun reaches the rain";
            else if (!shadowMap)
                state = "waiting for the cloud shadow map";
            else if (!sunLights || sunElevation <= 0f)
                state = "none: the sun is down";
            else
            {
                strength = Amount * Mathf.Clamp01(sunElevation / SunFade);
                state = "possible: wherever the sun shines on the rain, opposite the sun";
                detail = " (sun " + sunElevation.ToString("F0") + " deg up, rain " + Percent(CloudRain.Amount) +
                         ", strength " + Percent(Amount) + ", chance " + Percent(Chance) + ")";
            }

            if (state != _state)
            {
                _state = state;
                Log.Msg("rainbow: " + state + detail);
            }

            Current = strength;
            return strength;
        }

        /// <summary>
        /// Once per frame, after <see cref="Strength"/>, from CloudVolume: keeps, fades, or forms the
        /// arch. <paramref name="focus"/> is where the camera looks, <paramref name="toSun"/> a unit
        /// vector towards the sun, <paramref name="strength"/> this frame's (0: no bow can show).
        /// </summary>
        public static void UpdateArch(CloudDensityField field, Vector3 focus, Vector3 toSun, float sunElevation, float strength, float deltaTime)
        {
            bool possible = strength > 0f && field != null;

            // No bow can show (the rain stopped, the sun set, switched off...): the arch goes, and
            // the next time one can show, it forms afresh where the camera looks.
            if (!possible)
            {
                _envelope = Mathf.MoveTowards(_envelope, 0f, deltaTime / FadeTime);
                if (_hasArch && _envelope <= 0f)
                    Drop("gone with the conditions");
                return;
            }

            _envelope = Mathf.MoveTowards(_envelope, _hasArch && !_leaving ? 1f : 0f, deltaTime / FadeTime);
            if (_hasArch && _leaving && _envelope <= 0f)
            {
                Drop(null);
                _nextLook = 0f;
            }

            if (Time.time < _nextLook)
                return;
            _nextLook = Time.time + LookInterval;

            if (_hasArch)
            {
                if (_leaving)
                    return;

                _score = Score(field, ArchSpot, ArchRadius, toSun, ArchCentre.y);
                float away = Level(focus - ArchCentre).magnitude;
                string why = away > MoveAway ? "the camera looks " + (away / 1000f).ToString("F1") + " km away"
                           : _score < MinScore * 0.5f ? "the sunlit rain has left it (" + Percent(_score) + " of it now)"
                           : null;
                if (why != null)
                {
                    _leaving = true;
                    Log.Msg("rainbow: the arch fades -- " + why);
                }

                return;
            }

            // A place for a new one: its middle (between its feet) round where the camera looks,
            // nearer and taller ones preferred. Tall: its top just under the cloud base.
            Vector3 bestSpot = Vector3.zero, bestCentre = Vector3.zero;
            float bestRadius = 0f, bestScore = 0f, bestRank = 0f;
            bool anyRoom = false;
            for (float dx = -SearchReach; dx <= SearchReach + 1f; dx += SearchSpacing)
            {
                for (float dz = -SearchReach; dz <= SearchReach + 1f; dz += SearchSpacing)
                {
                    Vector3 centre = new Vector3(focus.x + dx, 0f, focus.z + dz);
                    centre.y = Ground(centre);

                    float tallest = RainbowArch.Radius(TopShare * (CloudRain.CloudBottom - centre.y), sunElevation);
                    if (tallest < 500f)
                        continue;
                    anyRoom = true;

                    for (int size = 0; size < 2; size++)
                    {
                        float radius = tallest * (size == 0 ? 1f : 0.7f);
                        Vector3 spot = RainbowArch.Spot(centre, radius, toSun);
                        float score = Score(field, spot, radius, toSun, centre.y);
                        float rank = score * (size == 0 ? 1f : 0.85f) *
                                     (1f - 0.25f * Mathf.Sqrt(dx * dx + dz * dz) / (SearchReach * 1.4142f));
                        if (rank > bestRank)
                        {
                            bestRank = rank;
                            bestScore = score;
                            bestSpot = spot;
                            bestCentre = centre;
                            bestRadius = radius;
                        }
                    }
                }
            }

            if (bestScore >= MinScore)
            {
                _hasArch = true;
                _leaving = false;
                _envelope = 0f;
                _score = bestScore;
                ArchSpot = bestSpot;
                ArchCentre = bestCentre;
                ArchRadius = bestRadius;
                _archState = null;
                float circle = bestRadius * Mathf.Sin(RainbowArch.BowAngle * Mathf.Deg2Rad);
                Log.Msg("rainbow: an arch stands at (" + bestCentre.x.ToString("F0") + ", " + bestCentre.z.ToString("F0") + "), " +
                        (Level(bestCentre - focus).magnitude / 1000f).ToString("F1") + " km from where the camera looks: " +
                        (2f * circle / 1000f).ToString("F1") + " km wide, " +
                        (circle * Mathf.Cos(sunElevation * Mathf.Deg2Rad)).ToString("F0") + " m tall, leaning back " +
                        sunElevation.ToString("F0") + " deg; " + Percent(bestScore) + " of it in sunlit rain");
            }
            else
            {
                string state = !anyRoom
                    ? "no arch: the sun is too high (" + sunElevation.ToString("F0") + " deg, " +
                      RainbowArch.MaxSunElevation.ToString("F0") + " at most) or no room under the clouds"
                    : "no arch: no sunlit rain near where the camera looks";
                if (state != _archState)
                {
                    _archState = state;
                    Log.Msg("rainbow: " + state + " (best " + Percent(bestScore) + ", " + Percent(MinScore) + " needed)");
                }
            }
        }

        /// <summary>
        /// How much of the arch stands in sunlit rain, 0..1, from foot to foot: the CPU twins of the
        /// rain mask (CloudRain.OnScreen) and the cloud cover the sun comes through (the weather map
        /// at the middle of the layer, as lightning's CloudAt reads it) -- each point as the shader
        /// lights it, averaged along the spot's line of sight through it.
        /// </summary>
        private static float Score(CloudDensityField field, Vector3 spot, float radius, Vector3 toSun, float ground)
        {
            float bottom = CloudRain.CloudBottom;
            float thickness = Settings.CloudThickness != null ? Settings.CloudThickness.value : Settings.Defaults.Thickness;
            float middle = bottom + 0.35f * thickness;
            float tile = Mathf.Max(500f, Settings.WeatherTileSize != null ? Settings.WeatherTileSize.value : Settings.Defaults.WeatherTileSize);
            Vector2 phase = CloudWind.WeatherPhase(tile);
            float coverage = CloudWeather.Coverage;

            float sum = 0f;
            for (int k = 0; k < ArchSamples; k++)
            {
                float phi = Mathf.PI * ((float)k / (ArchSamples - 1) - 0.5f);
                Vector3 p = RainbowArch.Point(spot, radius, toSun, phi);
                Vector3 look = (p - spot) / radius;

                // Only the depths between the ground and the cloud base, where rain can be.
                float lit = 0f;
                int count = 0;
                for (int d = 0; d < RainbowArch.Depths.Length; d++)
                {
                    Vector3 q = spot + look * (radius * RainbowArch.Depths[d]);
                    if (q.y <= ground || q.y >= bottom)
                        continue;

                    count++;
                    float rain = CloudRain.OnScreen(q);
                    if (rain <= 0f)
                        continue;

                    // Where the sun's ray to this drop crosses the middle of the cloud layer.
                    Vector3 c = q + toSun * ((middle - q.y) / Mathf.Max(toSun.y, 0.05f));
                    lit += rain * (1f - field.SampleCloud(c.x / tile - phase.x, c.z / tile - phase.y, coverage));
                }

                if (count > 0)
                    sum += lit / count;
            }

            return sum / ArchSamples;
        }

        private static float Ground(Vector3 position)
        {
            return Singleton<TerrainManager>.exists ? Singleton<TerrainManager>.instance.SampleRawHeightSmooth(position) : 0f;
        }

        private static Vector3 Level(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }

        private static void Drop(string why)
        {
            _hasArch = false;
            _leaving = false;
            _envelope = 0f;
            if (why != null)
                Log.Msg("rainbow: the arch has " + why);
        }

        /// <summary>For the detail line: "arch 2.1 km, 64% in sunlit rain, 100% in", or "no arch".</summary>
        public static string DescribeArch()
        {
            return !_hasArch ? "no arch"
                : "arch " + (ArchRadius / 1000f).ToString("F1") + " km, " + Percent(_score) + " in sunlit rain, " +
                  Percent(_envelope) + (_leaving ? " going" : " in");
        }

        /// <summary>For the startup report: "100% (chance 100%)", or "off".</summary>
        public static string Describe()
        {
            return Amount <= 0f ? "off" : Percent(Amount) + " (chance " + Percent(Chance) + ")";
        }

        /// <summary>On unload: the next city's first rain rolls its own dice.</summary>
        public static void Clear()
        {
            _raining = false;
            _dice = -1f;
            _state = null;
            Current = 0f;
            _hasArch = false;
            _leaving = false;
            _envelope = 0f;
            _nextLook = 0f;
            _score = 0f;
            _archState = null;
        }

        private static string Percent(float value)
        {
            return (value * 100f).ToString("F0") + "%";
        }
    }
}
