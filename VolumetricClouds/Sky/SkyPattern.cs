using System;
using System.Threading;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Which pattern of clouds and fog this city has: the two seeds everything is generated
    /// from, and "Reset cloud pattern", which rolls new ones.
    /// </summary>
    /// <remarks>
    /// Before a city kept its sky in its save, every load rolled new seeds. Now a city keeps
    /// its pattern for good, so the player needs a way to ask for another -- with the wind at
    /// 0 a clearing over the one district you look at stays there for the life of the save.
    /// The weather field also places the fog, so a new pattern moves the fog too.
    ///
    /// A new pattern is generated on a worker thread (the weather field is 384^2 of noise, 1536^2
    /// with Larger weather pattern, and
    /// the 3D volume tens of millions of hash evaluations: a visible hitch on the main
    /// thread) and handed over in ONE frame by ModController, so the clouds, their shadows,
    /// the rain and the fog change together.
    /// </remarks>
    public static class SkyPattern
    {
        private sealed class Replacement
        {
            public int Request;
            public bool FieldOnly;   // Larger weather pattern switched: the same seeds, the map at its new size
            public int Scale;
            public int FieldSeed;
            public int NoiseSeed;
            public CloudDensityField Field;
            public byte[] Noise;
            public UnityEngine.Color32[][] Detail;   // CloudDetail's texture; null if it failed (said once)
            public UnityEngine.Color32[][] Cumulus;  // the Cumulus noise, when it was wanted; null otherwise
            public Exception Error;
        }

        private static volatile Replacement _ready;
        private static volatile bool _working;
        private static volatile int _request;

        public static int FieldSeed { get; private set; }
        public static int NoiseSeed { get; private set; }

        /// <summary>
        /// The weather map's size the settings ask for: <see cref="CloudDensityField.LargerScale"/>
        /// with Larger weather pattern on, else 1. ModController brings the city's map to it.
        /// </summary>
        public static int WantedScale
        {
            get { return Settings.LargerWeatherPattern != null && Settings.LargerWeatherPattern.value ? CloudDensityField.LargerScale : 1; }
        }

        /// <summary>True while a new pattern is being generated.</summary>
        public static bool Working
        {
            get { return _working; }
        }

        /// <summary>
        /// Whether the "Reset cloud pattern" button does anything now: only in a loaded city
        /// (the options page is also open at the main menu), and not while a pattern is on its way.
        /// </summary>
        public static bool CanReset
        {
            get { return ModController.Instance != null && !_working; }
        }

        /// <summary>
        /// The seeds for the city being loaded: its save's, or new ones. Once per city, before
        /// anything is generated. Also drops a new pattern the previous city was still waiting for.
        /// </summary>
        public static void Choose(SkyState? saved)
        {
            _request++;
            _ready = null;
            _working = false;
            _failedScale = 0;

            if (saved.HasValue)
            {
                FieldSeed = saved.Value.FieldSeed;
                NoiseSeed = saved.Value.NoiseSeed;
            }
            else
            {
                FieldSeed = NewSeed(0);
                NoiseSeed = NewSeed(0);
            }
        }

        /// <summary>Main thread (Unity's Random), from the button, after the player said yes.</summary>
        public static void RequestReset()
        {
            if (!CanReset)
                return;

            int request = ++_request;
            int fieldSeed = NewSeed(FieldSeed);
            int noiseSeed = NewSeed(NoiseSeed);
            int scale = WantedScale;
            _working = true;

            // The Cumulus noise too while that style is chosen or its noise exists, so the new
            // pattern lands in one frame; otherwise it is made later, if it is ever wanted.
            bool withCumulus = (CloudStyle.UsesNoise || CloudStyle.Texture != null) && !CloudStyle.Failed;

            Log.Msg("sky: a new cloud pattern was asked for -- generating seeds " + fieldSeed + "/" + noiseSeed + " on a worker thread" +
                    (withCumulus ? " (with the Cumulus noise)" : ""));

            Thread thread = new Thread(() =>
            {
                Replacement result = new Replacement { Request = request, FieldSeed = fieldSeed, NoiseSeed = noiseSeed };
                try
                {
                    result.Field = new CloudDensityField(fieldSeed, scale);
                    result.Field.ScoreBytes();   // worked out here rather than on the main thread when it lands
                    result.Noise = CloudNoise3D.Generate(CloudVolume.NoiseSize, noiseSeed);

                    string detailError;
                    int milliseconds;
                    result.Detail = CloudDetail.BuildLevels(noiseSeed, out detailError, out milliseconds);
                    if (detailError != null)
                        Log.Warn("sky: the new pattern's detail texture failed (" + detailError + "); the old one's billows stay");

                    if (withCumulus)
                    {
                        string cumulusError;
                        result.Cumulus = CloudStyle.BuildLevels(noiseSeed, out cumulusError, out milliseconds);
                        if (cumulusError != null)
                            Log.Warn("sky: the new pattern's Cumulus noise failed (" + cumulusError + "); it is tried again after the pattern lands");
                    }
                }
                catch (Exception e)
                {
                    result.Error = e;
                }

                // A result for a request that has been superseded (a city unloaded under it)
                // is dropped here rather than risk it landing over the current one.
                if (request == _request)
                    _ready = result;
            })
            {
                IsBackground = true,
                Name = "VolumetricClouds new pattern",
            };
            thread.Start();
        }

        private static int _failedScale;

        /// <summary>
        /// The city's map (<paramref name="currentScale"/>) is to be built again at
        /// <see cref="WantedScale"/>: another size is asked for, nothing is being made, and the last
        /// try at that size did not fail.
        /// </summary>
        public static bool WantsRescale(int currentScale)
        {
            int wanted = WantedScale;
            if (wanted == currentScale)
            {
                _failedScale = 0;
                return false;
            }

            return !_working && wanted != _failedScale;
        }

        /// <summary>
        /// Larger weather pattern switched (or a profile or the settings file switched it): the
        /// city's map built again at <paramref name="scale"/> from the SAME seed, on a worker thread,
        /// and handed over like a new pattern (the 3D noise is left alone). Main thread, from
        /// ModController, only while nothing else is being made.
        /// </summary>
        public static void RequestScale(int scale)
        {
            if (ModController.Instance == null || _working)
                return;

            int request = ++_request;
            int fieldSeed = FieldSeed;
            _working = true;

            Log.Msg("sky: Larger weather pattern " + (scale > 1 ? "on" : "off") + " -- building the weather map again at " +
                    CloudDensityField.BaseResolution * scale + "^2 from the same seed " + fieldSeed + " on a worker thread");

            Thread thread = new Thread(() =>
            {
                Replacement result = new Replacement { Request = request, FieldOnly = true, Scale = scale, FieldSeed = fieldSeed, NoiseSeed = NoiseSeed };
                try
                {
                    result.Field = new CloudDensityField(fieldSeed, scale);
                    result.Field.ScoreBytes();
                }
                catch (Exception e)
                {
                    result.Error = e;
                }

                if (request == _request)
                    _ready = result;
            })
            {
                IsBackground = true,
                Name = "VolumetricClouds weather map size",
            };
            thread.Start();
        }

        /// <summary>
        /// Main thread, every frame: the finished pattern, once. It waits while
        /// <paramref name="noiseCanBeReplaced"/> is false -- the city's first noise still on its
        /// way would otherwise land over the new one.
        /// </summary>
        public static bool TryTake(bool noiseCanBeReplaced, out CloudDensityField field, out byte[] noise,
                                   out UnityEngine.Color32[][] detail, out UnityEngine.Color32[][] cumulus)
        {
            field = null;
            noise = null;
            detail = null;
            cumulus = null;

            Replacement result = _ready;
            if (result == null)
                return false;

            if (result.Request != _request)
            {
                _ready = null;
                return false;
            }

            // (A map alone never waits: the noise is not replaced.)
            if (result.Error == null && !result.FieldOnly && !noiseCanBeReplaced)
                return false;

            _ready = null;
            _working = false;

            if (result.Error != null)
            {
                if (result.FieldOnly)
                {
                    // Not tried again by itself (ModController would ask every frame): switching
                    // the setting again, or the next city, tries again.
                    _failedScale = result.Scale;
                    Log.Error("Building the weather map at its new size failed; the sky is unchanged.", result.Error);
                }
                else
                {
                    Log.Error("Generating a new cloud pattern failed; the sky is unchanged.", result.Error);
                }

                return false;
            }

            if (result.FieldOnly)
            {
                Log.Msg("sky: weather map " + result.Field.Describe());
                field = result.Field;
                return true;
            }

            Log.Msg("sky: pattern reset by the player -- seeds " + FieldSeed + "/" + NoiseSeed + " -> " +
                    result.FieldSeed + "/" + result.NoiseSeed + "; the city's next save keeps it; weather map " + result.Field.Describe());

            FieldSeed = result.FieldSeed;
            NoiseSeed = result.NoiseSeed;
            field = result.Field;
            noise = result.Noise;
            detail = result.Detail;
            cumulus = result.Cumulus;
            return true;
        }

        /// <summary>A seed in the range the mod has always used, and not <paramref name="not"/>.</summary>
        private static int NewSeed(int not)
        {
            int seed;
            do
            {
                seed = UnityEngine.Random.Range(1, 100000);
            }
            while (seed == not);

            return seed;
        }
    }
}
