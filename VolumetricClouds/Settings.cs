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

        /// <summary>How much direct sunlight thick cloud removes from the ground below it, 0..1.</summary>
        public static SavedFloat CloudShadowDarkness { get; private set; }

        /// <summary>
        /// Multiplier on cloud optical depth for shadows only. Above 1, thin cloud and cloud
        /// edges cast a fuller shadow, so more of the ground reads as shaded.
        /// </summary>
        public static SavedFloat CloudShadowFullness { get; private set; }

        /// <summary>Raymarched clouds (needs the embedded shader bundle); off means billboards.</summary>
        public static SavedBool UseVolumetric { get; private set; }

        /// <summary>Vertical extent of the cloud layer, in metres.</summary>
        public static SavedFloat CloudThickness { get; private set; }

        /// <summary>
        /// How hard fine noise erodes the clouds: 0 leaves smooth solid masses, the 0.3
        /// default gives soft cumulus, higher tears them into ragged tufts and opens gaps.
        /// </summary>
        public static SavedFloat CloudBreakup { get; private set; }

        /// <summary>Frequency of that erosion noise: low tears off big chunks, high makes fine wisps.</summary>
        public static SavedFloat CloudBreakupScale { get; private set; }

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

        /// <summary>Give the light halos their own fog instead of the world's.</summary>
        public static SavedBool HaloFogEnabled { get; private set; }

        /// <summary>
        /// The fog amount the halos see, on the same scale as the game's fog value (what
        /// PersistentFogAdjuster edits): 0 is clear, 1 is foggy, negative extrapolates.
        /// </summary>
        public static SavedFloat HaloFogValue { get; private set; }

        /// <summary>
        /// The fog amount for lights close to the camera. Must stay in the range the game's
        /// shader handles (about -0.49 and up): below it, nearby lights render as boxes.
        /// <see cref="HaloFogValue"/> is what distant lights blend towards.
        /// </summary>
        public static SavedFloat HaloFogNear { get; private set; }

        /// <summary>Distance at which halos start moving from the near value to the far one, in metres.</summary>
        public static SavedFloat HaloFogStart { get; private set; }

        /// <summary>Distance at which halos have fully reached the far value, in metres.</summary>
        public static SavedFloat HaloFogEnd { get; private set; }

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

        /// <summary>
        /// Default cloud settings, tuned by Anthony in his own city on 2026-09-20. The single
        /// source of truth: both the saved-setting constructors and the fallbacks scattered
        /// through the renderers read these, so the two cannot drift apart.
        /// </summary>
        public static class Defaults
        {
            public const float Coverage = 0.98f;
            public const float WindSpeed = 0.3f;
            public const float Altitude = 550f;
            public const float Thickness = 650f;
            public const float WeatherTileSize = 20000f;
            public const float Breakup = 0.55f;
            public const float BreakupScale = 2.75f;
            public const float Density = 2.6f;
            public const float Brightness = 0.2f;
            public const float MinIllumination = 0.73f;
            public const float ShadowDarkness = 0.7f;
            public const float ShadowFullness = 7.75f;

            /// <summary>
            /// Raymarch steps. 96 is the slider's maximum and was chosen on an RTX 5070 Ti;
            /// cost scales linearly, so this is the first thing to lower on a weaker GPU.
            /// </summary>
            public const float Quality = 96f;

            public const float PuffCount = 600f;
            public const float PuffSize = 900f;
        }

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

                Coverage = new SavedFloat("Coverage", FileName, Defaults.Coverage, true);
                WindSpeed = new SavedFloat("WindSpeed", FileName, Defaults.WindSpeed, true);
                // New key rather than reusing CookieTileSize: the old 2000 m default was saved
                // into existing settings files and is far too small for cloud-sized features.
                WeatherTileSize = new SavedFloat("WeatherTileSize", FileName, Defaults.WeatherTileSize, true);
                CloudShadows = new SavedBool("CloudShadows", FileName, true, true);
                // New keys rather than reusing CloudShadowStrength: that slider had almost no
                // effect at high coverage, so saved values of it are not meaningful.
                CloudShadowDarkness = new SavedFloat("CloudShadowDarkness", FileName, Defaults.ShadowDarkness, true);
                CloudShadowFullness = new SavedFloat("CloudShadowFullness", FileName, Defaults.ShadowFullness, true);
                UseVolumetric = new SavedBool("UseVolumetric", FileName, true, true);
                CloudThickness = new SavedFloat("CloudThickness", FileName, Defaults.Thickness, true);
                CloudDensity = new SavedFloat("CloudDensity", FileName, Defaults.Density, true);
                CloudBreakup = new SavedFloat("CloudBreakup", FileName, Defaults.Breakup, true);
                CloudBreakupScale = new SavedFloat("CloudBreakupScale", FileName, Defaults.BreakupScale, true);
                CloudBrightness = new SavedFloat("CloudBrightness", FileName, Defaults.Brightness, true);
                CloudQuality = new SavedFloat("CloudQuality", FileName, Defaults.Quality, true);
                CloudDepthOcclusion = new SavedBool("CloudDepthOcclusion", FileName, true, true);
                MinIllumination = new SavedFloat("MinIllumination", FileName, Defaults.MinIllumination, true);

                CloudsVisible = new SavedBool("CloudsVisible", FileName, true, true);
                CloudAltitude = new SavedFloat("CloudAltitude", FileName, Defaults.Altitude, true);
                CloudPuffCount = new SavedFloat("CloudPuffCount", FileName, Defaults.PuffCount, true);
                CloudPuffSize = new SavedFloat("CloudPuffSize", FileName, Defaults.PuffSize, true);

                HaloFogEnabled = new SavedBool("HaloFogEnabled", FileName, false, true);
                HaloFogValue = new SavedFloat("HaloFogValue", FileName, 0f, true);
                HaloFogNear = new SavedFloat("HaloFogNear", FileName, -0.45f, true);
                HaloFogStart = new SavedFloat("HaloFogStart", FileName, 500f, true);
                HaloFogEnd = new SavedFloat("HaloFogEnd", FileName, 2500f, true);

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
