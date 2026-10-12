using System;
using System.Threading;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The Mixed style's maps on the GPU (<see cref="CloudMix.Baker"/>): the cloud map, and its own
    /// copy of the empty-sky map that knows where the separate clouds reach. Baked on a worker thread
    /// whenever the intensity moves by <see cref="CloudMix.BakeStep"/> (or the pattern, the weather
    /// repeat or the seed changes), one bake at a time, and uploaded on the main thread. Until the
    /// first one is up the style is drawn Classic (CloudStyle.MixedDrawn), as while its noise is being
    /// made.
    /// </summary>
    /// <remarks>
    /// The first build (2026-10-05) worked the layout out per pixel and step -- four cells, their
    /// random numbers, two weather-map reads each -- and cost the author's 4K frame 20-85 ms. The
    /// layout only changes with the weather, so it is worked out here, once per step of the intensity.
    /// 1.5.0: one texture (the type in its alpha; no id map) -- the cloud pass has no sampler to spare,
    /// so it is read through the score texture's (CloudCommon.cginc).
    /// </remarks>
    public static class CloudMixMap
    {
        public static Texture2D Map { get; private set; }

        /// <summary>The empty-sky map for this style (EmptySky.WithPresence); null until the first bake.</summary>
        public static Texture2D EmptySkyTexture { get; private set; }

        /// <summary>Maps for the current pattern are on the GPU.</summary>
        public static bool Ready { get; private set; }

        /// <summary>A bake threw: the style is drawn Classic for the rest of the session, said once.</summary>
        public static bool Failed { get; private set; }

        /// <summary>The weather repeat the map on the GPU was baked for: the shader's scales must be its.</summary>
        public static float BakedTile
        {
            get { return _uploaded != null ? _uploaded.Tile : 0f; }
        }

        /// <summary>The player's shape the map on the GPU was baked with: its bands are stored over it.</summary>
        public static CloudMix.Shape BakedShape
        {
            get { return _uploaded != null ? _uploaded.Shape : CloudMix.Shape.Plain; }
        }

        /// <summary>
        /// Metres above the base of the tallest cloud in the map on the GPU (CloudMix.Baker.TallestTop;
        /// 0 with none): its tops are stored over it, and the march's slab ends there.
        /// </summary>
        public static float BakedTallest
        {
            get { return _uploaded != null ? _uploaded.Tallest : 0f; }
        }

        /// <summary>
        /// The empty-sky bytes on the GPU (<see cref="EmptySkyTexture"/>'s), or null. The skip may only use them
        /// while they were made from the field's current bytes (<see cref="EmptySkyFor"/>).
        /// </summary>
        public static byte[] EmptySkyBytes { get; private set; }

        /// <summary>The map on the GPU was baked from these empty-sky bytes of the field (CloudDensityField.EmptySkyBytes).</summary>
        public static bool EmptySkyFor(byte[] fieldBytes)
        {
            return _uploaded != null && ReferenceEquals(_uploaded.EmptyBase, fieldBytes);
        }

        private sealed class Job
        {
            public int Generation;
            public int Step;
            public float Tile;
            public int Seed;
            public CloudMix.Shape Shape;
            public float Tallest;
            public int FieldVersion;
            public CloudDensityField Field;
            public byte[] EmptyBase;
            public byte[] EmptyBytes;
            public int Milliseconds;
            public string Error;
        }

        // One slot: a bake starts only after the previous one is uploaded, so the worker never writes
        // what the main thread is reading.
        private static CloudMix.Baker _baker;
        private static Color32[] _mapColours, _emptyColours;

        private static volatile Job _done;
        private static volatile bool _working;
        private static int _generation;
        private static int _frame = -1;

        // What is on the GPU, and what the last bake was asked for.
        private static Job _uploaded;
        private static Job _asked;

        // Read by the bakes (built on the worker): the weather map's scores per field and version, and
        // the type pattern per seed.
        private static CloudDensityField _scoresField;
        private static int _scoresVersion = -1;
        private static byte[] _scores;
        private static int _typeSeed;
        private static byte[] _type;

        private static bool _loggedFirst;

        /// <summary>
        /// Main thread, from the shader parameters, at most once a frame: uploads a finished bake and
        /// starts the next when what is wanted has moved (the player's shape included).
        /// </summary>
        public static void Update(CloudDensityField field, float intensity, float weatherTile, int seed, CloudMix.Shape shape)
        {
            if (Failed || field == null || _frame == Time.frameCount)
                return;
            _frame = Time.frameCount;

            Job done = _done;
            if (done != null)
            {
                _done = null;
                Finish(done);
            }

            if (_working || Failed)
                return;

            int step = Mathf.RoundToInt(Mathf.Clamp01(intensity) / CloudMix.BakeStep);
            Job want = new Job
            {
                Generation = _generation,
                Step = step,
                Tile = weatherTile,
                Seed = seed,
                Shape = shape ?? CloudMix.Shape.Plain,
                FieldVersion = field.Version,
                Field = field,
                EmptyBase = field.EmptySkyBytes,
            };

            Job have = _asked ?? _uploaded;
            if (have != null && have.Step == want.Step && Mathf.Approximately(have.Tile, want.Tile) && have.Seed == want.Seed &&
                have.Shape.SameAs(want.Shape) &&
                have.FieldVersion == want.FieldVersion && ReferenceEquals(have.Field, want.Field) &&
                ReferenceEquals(have.EmptyBase, want.EmptyBase))
                return;

            _asked = want;
            _working = true;

            Thread thread = new Thread(() => Bake(want))
            {
                IsBackground = true,
                Name = "VolumetricClouds mixed map",
            };
            thread.Start();
        }

        /// <summary>City unloaded: the textures go, and a bake still running is dropped when it lands.</summary>
        public static void Release()
        {
            _generation++;
            DestroyTexture(Map);
            DestroyTexture(EmptySkyTexture);
            Map = null;
            EmptySkyTexture = null;
            EmptySkyBytes = null;
            Ready = false;
            Failed = false;
            _uploaded = null;
            _asked = null;
            _done = null;
            _scoresField = null;
            _scores = null;
            _scoresVersion = -1;
            _type = null;
            _frame = -1;
            _loggedFirst = false;
        }

        // Worker thread.
        private static void Bake(Job job)
        {
            DateTime start = DateTime.UtcNow;
            try
            {
                if (!ReferenceEquals(_scoresField, job.Field) || _scoresVersion != job.FieldVersion || _scores == null)
                {
                    _scores = job.Field.ScoreBytes();
                    _scoresField = job.Field;
                    _scoresVersion = job.FieldVersion;
                }

                float seedX, seedZ;
                CloudMix.SeedOffsets(job.Seed, out seedX, out seedZ);
                if (_type == null || _typeSeed != job.Seed)
                {
                    _type = CloudMix.BakeType(CloudMix.MapSize, seedX, seedZ);
                    _typeSeed = job.Seed;
                }

                int cells = CloudDensityField.Resolution / EmptySky.CellTexels;
                if (_baker == null)
                {
                    _baker = new CloudMix.Baker(CloudMix.MapSize, cells);
                    _mapColours = new Color32[CloudMix.MapSize * CloudMix.MapSize];
                    _emptyColours = new Color32[cells * cells];
                }

                _baker.Bake(_scores, CloudDensityField.Resolution, _type, job.Step * CloudMix.BakeStep, job.Tile, seedX, seedZ, job.Shape);
                job.Tallest = _baker.TallestTop;
                ToColours(_baker.Map, _mapColours);

                job.EmptyBytes = EmptySky.WithPresence(job.EmptyBase, _baker.Presence, cells);
                ToColours(job.EmptyBytes, _emptyColours);
            }
            catch (Exception e)
            {
                job.Error = e.GetType().Name + ": " + e.Message;
            }

            job.Milliseconds = (int)(DateTime.UtcNow - start).TotalMilliseconds;
            _done = job;
            _working = false;
        }

        // Main thread.
        private static void Finish(Job job)
        {
            if (job.Generation != _generation)
                return;

            if (job.Error != null)
            {
                Failed = true;
                Ready = false;
                Log.Warn("mixed style: the cloud map could not be baked (" + job.Error + "); the clouds are drawn Classic");
                return;
            }

            try
            {
                int cells = CloudDensityField.Resolution / EmptySky.CellTexels;
                if (Map == null)
                    Map = NewTexture("VolumetricCloudsMixMap", CloudMix.MapSize, FilterMode.Bilinear);
                if (EmptySkyTexture == null)
                    EmptySkyTexture = NewTexture("VolumetricCloudsMixEmptySky", cells, FilterMode.Point);

                Map.SetPixels32(_mapColours);
                Map.Apply(false);
                EmptySkyTexture.SetPixels32(_emptyColours);
                EmptySkyTexture.Apply(false);
            }
            catch (Exception e)
            {
                Failed = true;
                Ready = false;
                Log.Warn("mixed style: the cloud map could not be uploaded (" + e.GetType().Name + ": " + e.Message +
                         "); the clouds are drawn Classic");
                return;
            }

            _uploaded = job;
            EmptySkyBytes = job.EmptyBytes;
            if (ReferenceEquals(_asked, job))
                _asked = null;
            Ready = true;

            int cellCount = (CloudDensityField.Resolution / EmptySky.CellTexels) * (CloudDensityField.Resolution / EmptySky.CellTexels);
            string text = "mixed style: cloud map baked for " + (job.Step * CloudMix.BakeStep * 100f).ToString("F1") + "% in " +
                          job.Milliseconds + " ms -- " + _baker.Clouds + " clouds over " +
                          (100f * _baker.Covered / (CloudMix.MapSize * CloudMix.MapSize)).ToString("F1") + "% of the map, the tallest " +
                          job.Tallest.ToString("F0") + " m, reaching " +
                          (100f * _baker.CellsReached / cellCount).ToString("F0") + "% of the empty-sky cells";
            if (!_loggedFirst)
            {
                _loggedFirst = true;
                Log.Msg(text);
            }
            else if (Log.Detailed)
            {
                Log.Detail(text);
            }
        }

        private static Texture2D NewTexture(string name, int size, FilterMode filter)
        {
            // LINEAR: the bytes are data (a profile, a height, a type), never colours to decode.
            return new Texture2D(size, size, TextureFormat.ARGB32, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = filter,
                anisoLevel = 0,
            };
        }

        private static void ToColours(byte[] rgba, Color32[] colours)
        {
            for (int i = 0; i < colours.Length; i++)
            {
                int o = i * 4;
                colours[i] = new Color32(rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
            }
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        }
    }
}
