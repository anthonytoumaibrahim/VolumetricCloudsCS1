using System;
using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Our waterspouts (1.5.0, "Waterspout chance" on the Now tab): small funnels under showery
    /// clouds, mostly over the sea, mostly hanging from the base without reaching it -- for every
    /// player, DLC or not. VISUAL ONLY: no damage, no disaster, nothing the simulation can see, never
    /// saved. The rules are <see cref="WaterspoutRules"/>; this places them, moves them with their
    /// cloud and ages them, on simulation time (a paused city's stand still).
    /// </summary>
    public static class Waterspouts
    {
        /// <summary>Real seconds between looks at what is under each one.</summary>
        private const float GroundInterval = 1f;

        /// <summary>Candidates looked at for one waterspout.</summary>
        private const int Attempts = 12;

        /// <summary>Room it needs under the cloud base, metres.</summary>
        private const float MinRoom = 150f;

        /// <summary>No closer to the map's edge than this (metres from its middle, per axis).</summary>
        private const float MapReach = 8500f;

        private static readonly List<Spout> _spouts = new List<Spout>();
        private static readonly System.Random _random = new System.Random();
        private static int _nextId = 1;
        private static float _nextGround;
        private static float _nextDetail;
        private static int _formed;
        private static int _missed;

        /// <summary>"Waterspout chance": 1 = 100%.</summary>
        public static float Chance
        {
            get { return Settings.WaterspoutChance != null ? Mathf.Max(0f, Settings.WaterspoutChance.value) : Settings.Defaults.WaterspoutChance; }
        }

        /// <summary>Ours showing now.</summary>
        public static int Count
        {
            get { return _spouts.Count; }
        }

        /// <summary>
        /// Once a frame, from Funnels. <paramref name="draw"/>: the clouds are shown. Ages, moves and
        /// ends the ones there are; now and then, in showery weather, forms one.
        /// </summary>
        public static void Advance(CloudDensityField field, Camera camera, bool draw, float deltaTime, float simulationRate)
        {
            if (!draw)
            {
                Clear("the clouds are hidden");
                return;
            }

            // No camera or field yet (a moment of a load): nothing moves, nothing goes.
            if (field == null || camera == null)
                return;

            // At 0% the ones there are live out their lives; no new ones form.
            float chance = Chance;
            float dt = deltaTime * simulationRate;
            bool lookAtGround = Time.time >= _nextGround;
            if (lookAtGround)
                _nextGround = Time.time + GroundInterval;

            for (int i = _spouts.Count - 1; i >= 0; i--)
            {
                Spout s = _spouts[i];
                if (dt > 0f)
                {
                    s.Age += dt;
                    s.SpinPhase += s.SpinRate * dt;
                    s.RiseMetres += s.RiseRate * dt;
                }

                if (s.Age >= s.Life)
                {
                    Log.Msg("waterspout #" + s.Id + ": gone, after " + (s.Life / 60f).ToString("F1") + " min");
                    _spouts.RemoveAt(i);
                    continue;
                }

                // It rides with its cloud: the drift since it formed, in doubles.
                Vector3 at = new Vector3(s.Origin.x + (float)(DriftX - s.DriftX), 0f, s.Origin.z + (float)(DriftZ - s.DriftZ));
                at.y = GroundUnder.Surface(at);
                s.Ground = at;

                if (lookAtGround && s.Age >= 0f)
                {
                    s.Kind = GroundUnder.At(at);

                    // A waterspout does not live over land: it ropes out as it comes ashore.
                    if (s.OverWater && s.LandfallAt < 0f && s.Kind != GroundKind.Water)
                    {
                        s.LandfallAt = s.Age;
                        s.Life = Mathf.Min(s.Life, s.Age + WaterspoutRules.LandfallSeconds);
                        Log.Msg("waterspout #" + s.Id + ": comes ashore (" + GroundLook.Describe(s.Kind) + ") and dies");
                    }
                }
            }

            if (chance > 0f && dt > 0f && Active() < WaterspoutRules.MaxOwn)
            {
                float rain = CloudRain.Active ? CloudRain.Amount : CloudWeather.Rain;
                float rate = WaterspoutRules.EventsPerSecond(chance, CloudWeather.Coverage, rain);
                if (rate > 0f && _random.NextDouble() < rate * dt)
                    Form(field, camera);
            }

            if (Log.Detailed && Time.time >= _nextDetail)
            {
                _nextDetail = Time.time + 5f;
                Log.Detail("waterspouts: " + Describe() + " | chance " + (chance * 100f).ToString("F0") + "%, weather allows " +
                           (WaterspoutRules.Weather(CloudWeather.Coverage, CloudRain.Active ? CloudRain.Amount : CloudWeather.Rain) * 100f).ToString("F0") +
                           "% (cover " + (CloudWeather.Coverage * 100f).ToString("F0") + "%), formed " + _formed + ", no place found " + _missed);
            }
        }

        /// <summary>The funnels of the ones showing, for a cloud base and the wind.</summary>
        public static void Collect(List<Funnel> into, float cloudBase, Vector3 wind, Vector3 camera)
        {
            foreach (Spout s in _spouts)
            {
                if (s.Age < 0f)
                    continue;

                Funnel f;
                if (!WaterspoutRules.Shape(s, cloudBase, wind, out f))
                    continue;

                f.Distance = new Vector2(f.Ground.x - camera.x, f.Ground.z - camera.z).magnitude;
                into.Add(f);
            }
        }

        /// <summary>
        /// Tries to form one (and now and then its family): always under cloud, over water or by the
        /// shore.
        /// </summary>
        private static void Form(CloudDensityField field, Camera camera)
        {
            bool wantView = _random.NextDouble() < WaterspoutRules.InViewShare;
            bool allowLand = _random.NextDouble() < WaterspoutRules.LandShare;
            float cloudBase = CloudRain.CloudBottom;

            Vector3 spot;
            bool water, inView;
            float distance;
            if (!Place(field, camera, wantView, allowLand, cloudBase, out spot, out water, out inView, out distance))
            {
                _missed++;
                if (Log.Detailed)
                    Log.Detail("waterspout: one could have formed, but no water under cloud near the view");
                return;
            }

            Spout s = Add(spot, water, 0f);
            _formed++;

            float height = cloudBase - spot.y;
            string end = s.TouchesDown ? "will touch down"
                : s.Thread > 0f ? "stops " + (s.AloftBottom * 100f).ToString("F0") + "% of the way up, a faint thread down to its spray ring"
                : "stays aloft (its funnel ends " + (s.AloftBottom * 100f).ToString("F0") + "% of the way up)" +
                  (s.RingAloft > 0f ? ", a spray ring under it" : "");
            Log.Msg("waterspout #" + s.Id + ": forms " + (distance / 1000f).ToString("F1") + " km from the camera" +
                    (inView ? " (in view)" : "") + ", over " + GroundLook.Describe(s.Kind) + ", under cloud, " +
                    (2f * s.GroundRadius).ToString("F0") + " m across, " + height.ToString("F0") + " m to the cloud base, " +
                    end + ", for " + (s.Life / 60f).ToString("F1") + " min");

            // Now and then two or three under one line of cloud.
            if (_random.NextDouble() < WaterspoutRules.FamilyChance)
                Family(field, spot, water, cloudBase);
        }

        /// <summary>A place for one: in the band of distances, room under the base, under cloud, over water or by the shore.</summary>
        private static bool Place(CloudDensityField field, Camera camera, bool wantView, bool allowLand, float cloudBase,
                                  out Vector3 spot, out bool water, out bool inView, out float distance)
        {
            Vector3 cam = camera.transform.position;
            float camGround = GroundUnder.Surface(cam);
            spot = Vector3.zero;
            water = false;
            inView = false;
            distance = 0f;

            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                Vector3 direction = LightningPlacement.Spot(cam, camera.transform.rotation, camera.fieldOfView, camera.aspect, camGround,
                                                            wantView && attempt < Attempts / 2, (float)_random.NextDouble(),
                                                            (float)_random.NextDouble(), (float)_random.NextDouble(), out inView) - cam;
                direction.y = 0f;
                if (direction.sqrMagnitude < 1f)
                    continue;

                distance = Mathf.Lerp(WaterspoutRules.MinDistance, WaterspoutRules.MaxDistance, Mathf.Pow((float)_random.NextDouble(), 0.8f));
                spot = cam + direction.normalized * distance;
                if (Mathf.Abs(spot.x) > MapReach || Mathf.Abs(spot.z) > MapReach)
                    continue;

                spot.y = GroundUnder.Surface(spot);
                if (cloudBase - spot.y < MinRoom)
                    continue;

                // Always under a cloud: it is that cloud's.
                if (CloudAt(field, spot) < 0.5f)
                    continue;

                water = GroundUnder.WaterDepth(spot) >= 2f;
                if (!water && !(allowLand && GroundUnder.WaterNear(spot, WaterspoutRules.ShoreReach)))
                    continue;

                return true;
            }

            return false;
        }

        private static void Family(CloudDensityField field, Vector3 first, bool water, float cloudBase)
        {
            Vector3 wind = CloudWeather.WindDirection;
            Vector3 along = new Vector3(wind.x, 0f, wind.z).sqrMagnitude > 1e-6f ? new Vector3(wind.x, 0f, wind.z).normalized : Vector3.right;
            int more = _random.NextDouble() < 0.4 ? 2 : 1;

            for (int k = 0; k < more && Active() < WaterspoutRules.MaxOwn; k++)
            {
                float apart = (400f + 800f * (float)_random.NextDouble()) * (k == 0 ? 1f : -1f);
                Vector3 spot = first + along * apart;
                spot.y = GroundUnder.Surface(spot);
                if (cloudBase - spot.y < MinRoom || CloudAt(field, spot) < 0.4f)
                    continue;
                if (water && GroundUnder.WaterDepth(spot) < 2f)
                    continue;

                Spout s = Add(spot, water, -(5f + 35f * (float)_random.NextDouble()));
                Log.Msg("waterspout #" + s.Id + ": a second one under the same cloud, " + Mathf.Abs(apart).ToString("F0") +
                        " m away, in " + (-s.Age).ToString("F0") + " s");
            }
        }

        private static Spout Add(Vector3 spot, bool water, float age)
        {
            Spout s = WaterspoutRules.Make(_random, water);
            s.Id = _nextId++;
            s.Age = age;
            s.Origin = spot;
            s.Ground = spot;
            s.DriftX = DriftX;
            s.DriftZ = DriftZ;
            s.Kind = GroundUnder.At(spot);
            _spouts.Add(s);
            return s;
        }

        /// <summary>The ones that have begun, or are waiting to.</summary>
        private static int Active()
        {
            return _spouts.Count;
        }

        /// <summary>Cloud over a point, 0..1, as lightning's test reads it.</summary>
        private static float CloudAt(CloudDensityField field, Vector3 position)
        {
            float tile = field.Tile;
            Vector2 phase = CloudWind.WeatherPhase(tile);
            return field.SampleCloud(position.x / tile - phase.x, position.z / tile - phase.y,
                                     CloudStyle.CloudTestCoverage(CloudWeather.Coverage));
        }

        private static double DriftX
        {
            get { return CloudWind.X + CloudWind.ShiftX; }
        }

        private static double DriftZ
        {
            get { return CloudWind.Z + CloudWind.ShiftZ; }
        }

        /// <summary>For the logs: "none", or "2 (#3 aloft 40 s / 2.1 min, #4 down ...)".</summary>
        public static string Describe()
        {
            if (_spouts.Count == 0)
                return "none";

            string text = _spouts.Count.ToString();
            string list = "";
            foreach (Spout s in _spouts)
            {
                list += (list.Length > 0 ? ", " : "") + "#" + s.Id + (s.TouchesDown ? " touching down" : s.Thread > 0f ? " thread" : " aloft") + " " +
                        (s.Age < 0f ? "in " + (-s.Age).ToString("F0") + " s" : s.Age.ToString("F0") + " s of " + s.Life.ToString("F0") + " s") +
                        " over " + s.Kind;
            }

            return text + " (" + list + ")";
        }

        /// <summary>On unload or with the clouds hidden: none kept.</summary>
        public static void Clear(string why)
        {
            if (_spouts.Count > 0 && why != null)
                Log.Msg("waterspouts: " + _spouts.Count + " gone -- " + why);
            _spouts.Clear();
        }
    }
}
