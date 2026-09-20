using System;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds
{
    /// <summary>
    /// Persisted options. Backed by the game's own settings file so values survive
    /// restarts and show up alongside vanilla settings.
    /// </summary>
    public static class Settings
    {
        public const string FileName = "VolumetricClouds";

        public static SavedBool ShowInUnifiedUI { get; private set; }
        public static SavedInputKey ToggleKey { get; private set; }

        /// <summary>Cloud coverage, 0..1. Drives both shadow density and world dimming.</summary>
        public static SavedFloat Coverage { get; private set; }

        /// <summary>Multiplier on how fast cloud shadows drift.</summary>
        public static SavedFloat WindSpeed { get; private set; }

        /// <summary>
        /// World-space size of one weather tile, in metres. Shared by the shadow cookie and
        /// the raymarched clouds so both repeat at the same scale.
        /// </summary>
        public static SavedFloat WeatherTileSize { get; private set; }

        /// <summary>Clouds cast shadows on the ground.</summary>
        public static SavedBool CloudShadows { get; private set; }

        /// <summary>
        /// How much direct sunlight a thick cloud removes under scattered cover, 0..1. Eases
        /// off towards overcast so full cover still lands on <see cref="MinIllumination"/>.
        /// </summary>
        public static SavedFloat CloudShadowStrength { get; private set; }

        /// <summary>Raymarched clouds (needs the embedded shader bundle); off means billboards.</summary>
        public static SavedBool UseVolumetric { get; private set; }

        /// <summary>Vertical extent of the cloud layer, in metres.</summary>
        public static SavedFloat CloudThickness { get; private set; }

        /// <summary>Multiplier on cloud optical density.</summary>
        public static SavedFloat CloudDensity { get; private set; }

        /// <summary>Multiplier on how brightly clouds are lit.</summary>
        public static SavedFloat CloudBrightness { get; private set; }

        /// <summary>Raymarch steps per pixel. Cost scales linearly.</summary>
        public static SavedFloat CloudQuality { get; private set; }

        /// <summary>Let scene geometry hide clouds behind it. Diagnostic escape hatch.</summary>
        public static SavedBool CloudDepthOcclusion { get; private set; }

        /// <summary>World illumination at 100% coverage, 0..1. Overcast, not night.</summary>
        public static SavedFloat MinIllumination { get; private set; }

        /// <summary>Draw the visible cloud billboards.</summary>
        public static SavedBool CloudsVisible { get; private set; }

        /// <summary>Cloud layer height above sea level, in metres.</summary>
        public static SavedFloat CloudAltitude { get; private set; }

        /// <summary>How many billboard puffs to place at full coverage.</summary>
        public static SavedFloat CloudPuffCount { get; private set; }

        /// <summary>Base width of a single puff, in metres.</summary>
        public static SavedFloat CloudPuffSize { get; private set; }

        /// <summary>Master switch for the light-halo adjustments.</summary>
        public static SavedBool HaloAdjustEnabled { get; private set; }

        /// <summary>Multiplier on halo radius. 1 leaves the game untouched.</summary>
        public static SavedFloat HaloRangeScale { get; private set; }

        /// <summary>Multiplier on halo brightness. 1 leaves the game untouched.</summary>
        public static SavedFloat HaloIntensityScale { get; private set; }

        /// <summary>Lights closer than this skip the volume pass. 0 disables the cutoff.</summary>
        public static SavedFloat HaloNearDistance { get; private set; }

        /// <summary>Diagnostic: suppress LightSystem.DrawLight entirely.</summary>
        public static SavedBool DebugSkipDrawLight { get; private set; }

        /// <summary>Spike aid: projects a checkerboard instead of clouds.</summary>
        public static SavedBool DebugChecker { get; private set; }

        private static bool _initialised;

        public static void Init()
        {
            if (_initialised)
                return;

            try
            {
                if (GameSettings.FindSettingsFileByName(FileName) == null)
                    GameSettings.AddSettingsFile(new SettingsFile { fileName = FileName });

                ShowInUnifiedUI = new SavedBool("ShowInUnifiedUI", FileName, true, true);
                ToggleKey = new SavedInputKey("ToggleKey", FileName,
                    SavedInputKey.Encode(KeyCode.F4, false, false, false), true);

                Coverage = new SavedFloat("Coverage", FileName, 0.5f, true);
                WindSpeed = new SavedFloat("WindSpeed", FileName, 1f, true);
                // New key rather than reusing CookieTileSize: the old 2000 m default was saved
                // into existing settings files and is far too small for cloud-sized features.
                WeatherTileSize = new SavedFloat("WeatherTileSize", FileName, 10000f, true);
                CloudShadows = new SavedBool("CloudShadows", FileName, true, true);
                CloudShadowStrength = new SavedFloat("CloudShadowStrength", FileName, 0.65f, true);
                UseVolumetric = new SavedBool("UseVolumetric", FileName, true, true);
                CloudThickness = new SavedFloat("CloudThickness", FileName, 600f, true);
                CloudDensity = new SavedFloat("CloudDensity", FileName, 1f, true);
                CloudBrightness = new SavedFloat("CloudBrightness", FileName, 1f, true);
                CloudQuality = new SavedFloat("CloudQuality", FileName, 48f, true);
                CloudDepthOcclusion = new SavedBool("CloudDepthOcclusion", FileName, true, true);
                MinIllumination = new SavedFloat("MinIllumination", FileName, 0.65f, true);

                CloudsVisible = new SavedBool("CloudsVisible", FileName, true, true);
                CloudAltitude = new SavedFloat("CloudAltitude", FileName, 900f, true);
                CloudPuffCount = new SavedFloat("CloudPuffCount", FileName, 300f, true);
                CloudPuffSize = new SavedFloat("CloudPuffSize", FileName, 700f, true);

                HaloAdjustEnabled = new SavedBool("HaloAdjustEnabled", FileName, false, true);
                HaloRangeScale = new SavedFloat("HaloRangeScale", FileName, 1f, true);
                HaloIntensityScale = new SavedFloat("HaloIntensityScale", FileName, 1f, true);
                HaloNearDistance = new SavedFloat("HaloNearDistance", FileName, 0f, true);

                DebugSkipDrawLight = new SavedBool("DebugSkipDrawLight", FileName, false, true);
                DebugChecker = new SavedBool("DebugChecker", FileName, false, true);

                _initialised = true;
            }
            catch (Exception e)
            {
                Debug.LogError("[VolumetricClouds] Could not initialise settings.");
                Debug.LogException(e);
            }
        }
    }
}
