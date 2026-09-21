using System;
using System.IO;
using UnityEngine;

namespace VolumetricClouds
{
    /// <summary>
    /// Writes to a dedicated file next to the game's user data, so diagnostics can be
    /// read without digging through a multi-megabyte output_log. Everything also goes to
    /// the Unity log so it still shows up in ModTools.
    /// </summary>
    public static class Log
    {
        private const string FileName = "VolumetricClouds.log";

        private static readonly object Sync = new object();

        private static string _path;
        private static bool _disabled;

        public static string Path
        {
            get
            {
                if (_path == null)
                {
                    string dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        System.IO.Path.Combine("Colossal Order", "Cities_Skylines"));
                    _path = System.IO.Path.Combine(dir, FileName);
                }

                return _path;
            }
        }

        /// <summary>Starts a fresh file for this session.</summary>
        public static void Start()
        {
            lock (Sync)
            {
                _disabled = false;

                try
                {
                    File.WriteAllText(Path,
                        Mod.DisplayName + " log - session started " +
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
                }
                catch (Exception e)
                {
                    _disabled = true;
                    Debug.LogWarning("[VolumetricClouds] File logging unavailable: " + e.Message);
                }
            }
        }

        public static void Msg(string message)
        {
            Debug.Log("[VolumetricClouds] " + message);
            Write("INFO ", message);
        }

        /// <summary>
        /// True while the player has asked for the repeating diagnostic lines. Test it before
        /// BUILDING a detail string, or the concatenation is paid for whatever the switch says.
        /// </summary>
        public static bool Detailed
        {
            get { return Settings.DetailedLogging != null && Settings.DetailedLogging.value; }
        }

        /// <summary>
        /// A line that repeats: the per-second and per-five-second diagnostics this mod is
        /// developed against. Silent unless "Detailed logging" is on, and never goes to the
        /// Unity log, which follows every entry with a stack trace.
        /// </summary>
        /// <remarks>
        /// What is NOT allowed in here: anything that only happens once, any warning or error,
        /// and the state of anything that can suppress or replace rendering. A quiet log must
        /// still explain a black screen: a debug switch once left saved as on silently removed
        /// every vehicle light, session after session, until a log line gave it away.
        /// </remarks>
        public static void Detail(string message)
        {
            if (!Detailed)
                return;

            Write("DEBUG", message);
        }

        /// <summary>
        /// Says whether the repeating lines are on, so a pasted log explains its own silence.
        /// Called once the settings exist, which is after <see cref="Start"/>.
        /// </summary>
        public static void ReportLevel()
        {
            Msg("detailed logging is " + (Detailed ? "ON" : "off") +
                (Detailed ? "" : " -- tick it in Options -> General before reporting a problem"));
        }

        public static void Warn(string message)
        {
            Debug.LogWarning("[VolumetricClouds] " + message);
            Write("WARN ", message);
        }

        public static void Error(string message, Exception e = null)
        {
            Debug.LogError("[VolumetricClouds] " + message);
            if (e != null)
                Debug.LogException(e);

            Write("ERROR", message + (e == null ? string.Empty : Environment.NewLine + e));
        }

        private static void Write(string level, string message)
        {
            if (_disabled)
                return;

            lock (Sync)
            {
                try
                {
                    File.AppendAllText(Path,
                        DateTime.Now.ToString("HH:mm:ss") + " " + level + " " + message + Environment.NewLine);
                }
                catch (Exception)
                {
                    // A locked or unwritable file shouldn't take the mod down with it.
                    _disabled = true;
                }
            }
        }
    }
}
