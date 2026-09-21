using System;
using System.Threading;
using ICities;
using VolumetricClouds.Sky;

namespace VolumetricClouds
{
    /// <summary>
    /// The city's sky in its savegame: which pattern, and where its clouds and fog had drifted
    /// to. Found by the game like <see cref="Loader"/>.
    /// </summary>
    /// <remarks>
    /// Safe with the mod removed (read from IL, 2026-09-21): the save keeps mod data in
    /// SimulationManager.m_serializableDataStorage, a dictionary of name -> bytes that
    /// SimulationManager.Data reads back whole and writes out whole, with no idea whose each
    /// entry is. Without the mod our 61 bytes are simply carried along; nothing of the game's
    /// own data is ever touched.
    ///
    /// THREADS. OnSaveData runs on the SIMULATION thread (LoadingManager.SaveLevelCoroutine
    /// queues the serializer with SimulationManager.AddAction; autosaves too), so it never
    /// reads the live statics: it writes the copy <see cref="Publish"/> made on the main
    /// thread. OnLoadData is called from SimulationManager.LateUpdateData during loading,
    /// before OnLevelLoaded creates the controller; it only stores what it read. Both log
    /// their thread, which settles the load order from the log.
    ///
    /// The one way this could break a player's load is an exception escaping OnLoadData, so
    /// nothing does: an unreadable record is logged and the city gets a new pattern.
    /// </remarks>
    public class SkySaveData : SerializableDataExtensionBase
    {
        /// <summary>Our key in the save's dictionary of mod data. Never change it: saves hold it.</summary>
        public const string Key = "VolumetricCloudsSky";

        private static readonly object Sync = new object();
        private static SkyState _latest;
        private static bool _hasLatest;

        /// <summary>
        /// What the save being loaded held, or null. Set by every OnLoadData, null included,
        /// so one city never inherits another's sky.
        /// </summary>
        public static SkyState? Loaded { get; private set; }

        public override void OnCreated(ISerializableData serializableData)
        {
            base.OnCreated(serializableData);
            Loaded = null;
        }

        public override void OnLoadData()
        {
            SkyState? found = null;
            string what;

            try
            {
                byte[] data = serializableDataManager.LoadData(Key);
                SkyState state;
                string note;

                if (data == null)
                {
                    what = "no sky of ours in this save (a new city, or saved before this version)";
                }
                else if (SkyState.TryRead(data, out state, out note))
                {
                    found = state;
                    what = "a saved sky, " + data.Length + " bytes" + (note != null ? " (" + note + ")" : "");
                }
                else
                {
                    what = "a saved sky that could not be read (" + note + "); the city gets a new pattern, and the next save replaces it";
                }
            }
            catch (Exception e)
            {
                what = "reading the saved sky threw (" + e.GetType().Name + ": " + e.Message + "); the city gets a new pattern";
            }

            Loaded = found;

            try
            {
                Log.Msg("sky: savegame read -- " + what + " [" + ThreadName() + "]");
            }
            catch (Exception)
            {
                // Nothing may escape a load hook.
            }
        }

        public override void OnSaveData()
        {
            try
            {
                SkyState state;
                bool has;
                lock (Sync)
                {
                    state = _latest;
                    has = _hasLatest;
                }

                if (!has)
                {
                    Log.Msg("sky: nothing written into this save -- no sky of ours is running [" + ThreadName() + "]");
                    return;
                }

                byte[] bytes = state.ToBytes();
                serializableDataManager.SaveData(Key, bytes);
                Log.Msg("sky: written into the savegame (" + bytes.Length + " bytes) -- " + state.Describe() + " [" + ThreadName() + "]");
            }
            catch (Exception e)
            {
                Log.Error("Could not write the sky into the save. The save itself is unaffected; it keeps the sky it had.", e);
            }
        }

        /// <summary>
        /// Once per city, from ModController.Start, before anything is generated: the pattern's
        /// seeds come from the save, or are rolled for a city that has none.
        /// </summary>
        public static void BeginCity()
        {
            SkyState? loaded = Loaded;
            SkyPattern.Choose(loaded);

            if (loaded.HasValue)
                Log.Msg("sky: restored from the save -- " + loaded.Value.Describe() + " [" + ThreadName() + "]");
            else
                Log.Msg("sky: a new pattern for this city -- seeds " + SkyPattern.FieldSeed + "/" + SkyPattern.NoiseSeed + " [" + ThreadName() + "]");
        }

        /// <summary>
        /// From CloudLighting.Start, after it has reset the weather statics: the clouds' and
        /// the fog's drift where the save left them, or at 0 for a city without one.
        /// </summary>
        public static void RestoreDrifts()
        {
            SkyState? loaded = Loaded;
            if (loaded.HasValue)
            {
                SkyState s = loaded.Value;
                CloudWind.Restore(s.WindX, s.WindZ);
                CloudFog.Restore(s.FogX, s.FogZ, s.FogBoil, s.FogAmount);
                CloudRain.RestoreFall(new UnityEngine.Vector3(s.RainFallX, s.RainFallY, s.RainFallZ));
            }
            else
            {
                CloudWind.Reset();
                CloudFog.Restore(0.0, 0.0, 0f, 0f);
                CloudRain.RestoreFall(UnityEngine.Vector3.zero);
            }
        }

        /// <summary>
        /// Main thread, every frame, right after the one place the drifts advance
        /// (CloudLighting.LateUpdate): what a save made now would record.
        /// </summary>
        public static void Publish()
        {
            SkyState state = new SkyState
            {
                FieldSeed = SkyPattern.FieldSeed,
                NoiseSeed = SkyPattern.NoiseSeed,
                WindX = CloudWind.X,
                WindZ = CloudWind.Z,
                FogX = CloudFog.OffsetX,
                FogZ = CloudFog.OffsetZ,
                FogBoil = CloudFog.Boil,
                FogAmount = CloudFog.Amount,
                RainFallX = CloudRain.FallOffset.x,
                RainFallY = CloudRain.FallOffset.y,
                RainFallZ = CloudRain.FallOffset.z,
            };

            lock (Sync)
            {
                _latest = state;
                _hasLatest = true;
            }
        }

        /// <summary>The city is going: nothing of ours is left to save.</summary>
        public static void Forget()
        {
            lock (Sync)
            {
                _hasLatest = false;
            }
        }

        private static string ThreadName()
        {
            Thread thread = Thread.CurrentThread;
            return "thread " + thread.ManagedThreadId + (string.IsNullOrEmpty(thread.Name) ? "" : " '" + thread.Name + "'");
        }
    }
}
