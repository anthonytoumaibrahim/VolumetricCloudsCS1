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
                        "Volumetric Clouds log - session started " +
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
