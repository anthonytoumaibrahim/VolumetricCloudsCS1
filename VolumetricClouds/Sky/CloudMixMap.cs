using System;
using System.Threading;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The Mixed style's TYPE MAP on the GPU (<see cref="CloudMix.BakeType"/>, in its alpha): which way
    /// each stretch of sky leans, towards the layer or the puffs. Baked on a worker thread once per city's
    /// seed and weather map size (Larger weather pattern), uploaded on the main thread. Until it is up the
    /// style is drawn Classic (CloudStyle.MixedDrawn), as while its noise is being made.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-17 the map also held Mixed's separate clouds, baked again whenever the intensity
    /// moved by half a percent, with an empty-sky map of its own; the puffs that replaced them are cut in
    /// the shader. The cloud pass has no sampler to spare, so it is read through the score texture's
    /// (CloudCommon.cginc).
    /// </remarks>
    public static class CloudMixMap
    {
        public static Texture2D Map { get; private set; }

        /// <summary>The map for the current seed and size is on the GPU.</summary>
        public static bool Ready { get; private set; }

        /// <summary>A bake threw: the style is drawn Classic for the rest of the session, said once.</summary>
        public static bool Failed { get; private set; }

        private sealed class Job
        {
            public int Generation;
            public int Seed;
            public int Scale;        // the field's (Larger weather pattern): the map is MapSizeFor it
            public byte[] Type;
            public int Milliseconds;
            public string Error;
        }

        // One slot: a bake starts only after the previous one is uploaded.
        private static volatile Job _done;
        private static volatile bool _working;
        private static int _generation;
        private static int _frame = -1;

        // What is on the GPU, and what the last bake was asked for.
        private static Job _uploaded;
        private static Job _asked;

        /// <summary>
        /// Main thread, from the shader parameters, at most once a frame: uploads a finished bake and
        /// starts the next when the seed or the weather map's size has moved.
        /// </summary>
        public static void Update(CloudDensityField field, int seed)
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

            // A map baked for the other size (Larger weather pattern just switched) would tip the sky in
            // patches four times too big or too small: drawn Classic until the new one is up.
            if (_uploaded != null && (_uploaded.Scale != field.Scale || _uploaded.Seed != seed))
                Ready = false;

            if (_working || Failed)
                return;

            Job have = _asked ?? _uploaded;
            if (have != null && have.Seed == seed && have.Scale == field.Scale)
                return;

            Job want = new Job { Generation = _generation, Seed = seed, Scale = field.Scale };
            _asked = want;
            _working = true;

            Thread thread = new Thread(() => Bake(want))
            {
                IsBackground = true,
                Name = "VolumetricClouds mixed map",
            };
            thread.Start();
        }

        /// <summary>City unloaded: the texture goes, and a bake still running is dropped when it lands.</summary>
        public static void Release()
        {
            _generation++;
            if (Map != null)
                UnityEngine.Object.Destroy(Map);
            Map = null;
            Ready = false;
            Failed = false;
            _uploaded = null;
            _asked = null;
            _done = null;
            _frame = -1;
        }

        // Worker thread.
        private static void Bake(Job job)
        {
            DateTime start = DateTime.UtcNow;
            try
            {
                float seedX, seedZ;
                CloudMix.SeedOffsets(job.Seed, out seedX, out seedZ);
                job.Type = CloudMix.BakeType(CloudMix.MapSizeFor(job.Scale), seedX, seedZ, job.Scale);
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
                Log.Warn("mixed style: the type map could not be baked (" + job.Error + "); the clouds are drawn Classic");
                return;
            }

            int size = CloudMix.MapSizeFor(job.Scale);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Made again at another size (Larger weather pattern switched).
                if (Map != null && Map.width != size)
                {
                    UnityEngine.Object.Destroy(Map);
                    Map = null;
                }

                // LINEAR: the bytes are data (the type), never colours to decode.
                if (Map == null)
                {
                    Map = new Texture2D(size, size, TextureFormat.ARGB32, false, true)
                    {
                        name = "VolumetricCloudsMixMap",
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = FilterMode.Bilinear,
                        anisoLevel = 0,
                    };
                }

                var colours = new Color32[size * size];
                for (int i = 0; i < colours.Length; i++)
                    colours[i] = new Color32(0, 0, 0, job.Type[i]);
                Map.SetPixels32(colours);
                Map.Apply(false);
            }
            catch (Exception e)
            {
                Failed = true;
                Ready = false;
                Log.Warn("mixed style: the type map could not be uploaded (" + e.GetType().Name + ": " + e.Message +
                         "); the clouds are drawn Classic");
                return;
            }

            _uploaded = job;
            if (ReferenceEquals(_asked, job))
                _asked = null;
            Ready = true;

            Log.Msg("mixed style: type map baked for seed " + job.Seed + " in " + job.Milliseconds + " ms, uploaded in " +
                    (int)watch.Elapsed.TotalMilliseconds + " ms (" + size + "^2)");
        }
    }
}
