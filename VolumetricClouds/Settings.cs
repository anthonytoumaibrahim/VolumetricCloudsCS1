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

        /// <summary>
        /// The cloud cover the OVERRIDE asks for, 0..1. Only in effect while
        /// <see cref="CoverageOverride"/> is on; rendering reads CloudWeather.Coverage, never this.
        /// </summary>
        public static SavedFloat Coverage { get; private set; }

        /// <summary>
        /// Off (the default): the cover follows the game's weather. On: <see cref="Coverage"/>
        /// fixes it, for skies the game cannot express -- cloudy with no rain, on demand.
        /// </summary>
        public static SavedBool CoverageOverride { get; private set; }

        /// <summary>Following the weather: the cover on a clear day, 0..1.</summary>
        public static SavedFloat WeatherFairCoverage { get; private set; }

        /// <summary>Following the weather: the cover in rain or a full cloudy spell, 0..1.</summary>
        public static SavedFloat WeatherOvercastCoverage { get; private set; }

        /// <summary>
        /// Replace the game's camera-locked rain with ours: curtains under the clouds and
        /// world-anchored streaks near the camera, both only where it is raining.
        /// </summary>
        public static SavedBool RainEnabled { get; private set; }

        /// <summary>Multiplier on how dense the rain curtains under the clouds are. 0 = none.</summary>
        public static SavedFloat RainCurtains { get; private set; }

        /// <summary>Multiplier on how many streaks show near the camera. 0 = none.</summary>
        public static SavedFloat RainStreaks { get; private set; }

        /// <summary>Camera height above the ground, in metres, at which the streaks are gone.</summary>
        public static SavedFloat RainStreakHeight { get; private set; }

        /// <summary>
        /// The rain sound and wet roads follow the clouds too. The one setting that changes
        /// what the simulation sees (see SampleRainIntensityPatch).
        /// </summary>
        public static SavedBool RainLocalised { get; private set; }

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

        /// <summary>Take over the glow pass of batched (distant) lights. Off = untouched game.</summary>
        public static SavedBool HaloEnabled { get; private set; }

        /// <summary>
        /// Use the ported replacement shader, which adds brightness/tightness/size and cannot
        /// produce the NaN "box". Off = a copy of the game's shader with only fog overridden.
        /// </summary>
        public static SavedBool HaloReplaceShader { get; private set; }

        /// <summary>
        /// The fog amount the halos see, on the game's own scale (what PersistentFogAdjuster
        /// edits): 0 is a clear night, 1 is foggy, -0.5 is no glow at all. Independent of the
        /// world's fog.
        /// </summary>
        public static SavedFloat HaloFogAmount { get; private set; }

        /// <summary>Multiplier on glow amplitude. Replacement shader only.</summary>
        public static SavedFloat HaloBrightness { get; private set; }

        /// <summary>
        /// Multiplier on the falloff exponent: above 1 keeps the bright core and sheds the wide
        /// haze, which is what makes a halo read as small. Replacement shader only.
        /// </summary>
        public static SavedFloat HaloTightness { get; private set; }

        /// <summary>Multiplier on the glow's world radius. Replacement shader only.</summary>
        public static SavedFloat HaloRadius { get; private set; }

        /// <summary>
        /// Lamps closer to the camera than this (metres) get the three "near" multipliers below
        /// in full, fading out to none at twice the distance. A halo has a fixed world size, so
        /// what makes a distant lamp a crisp dot makes one 100 m away a blob. 0 = off.
        /// Replacement shader only.
        /// </summary>
        public static SavedFloat HaloNearLightDistance { get; private set; }

        /// <summary>Near lamps: multiplier on top of <see cref="HaloBrightness"/>.</summary>
        public static SavedFloat HaloNearLightBrightness { get; private set; }

        /// <summary>Near lamps: multiplier on top of <see cref="HaloTightness"/>.</summary>
        public static SavedFloat HaloNearLightTightness { get; private set; }

        /// <summary>Near lamps: multiplier on top of <see cref="HaloRadius"/>.</summary>
        public static SavedFloat HaloNearLightRadius { get; private set; }

        /// <summary>Master switch for the dynamic-light (vehicle) adjustments.</summary>
        public static SavedBool HaloAdjustEnabled { get; private set; }

        /// <summary>Multiplier on a dynamic light's range. 1 leaves the game untouched.</summary>
        public static SavedFloat HaloRangeScale { get; private set; }

        /// <summary>Multiplier on a dynamic light's brightness. 1 leaves the game untouched.</summary>
        public static SavedFloat HaloIntensityScale { get; private set; }

        /// <summary>
        /// Dynamic lights closer than this skip the volume pass. 0 disables the cutoff. Nothing
        /// to do with <see cref="HaloNearLightDistance"/>: street lamps are never dynamic.
        /// </summary>
        public static SavedFloat DynamicHaloCutoff { get; private set; }

        /// <summary>Spike aid: projects a checkerboard instead of clouds.</summary>
        public static SavedBool DebugChecker { get; private set; }

        /// <summary>
        /// Default cloud settings, tuned by Anthony in his own city on 2026-09-20. The single
        /// source of truth: both the saved-setting constructors and the fallbacks scattered
        /// through the renderers read these, so the two cannot drift apart.
        /// </summary>
        public static class Defaults
        {
            /// <summary>Anthony's overcast. Also the default cover in rain when following the weather.</summary>
            public const float Coverage = 0.98f;

            /// <summary>A clear day: scattered cloud. A guess until he tunes it -- 98% was "just for demo".</summary>
            public const float FairCoverage = 0.35f;

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
                CoverageOverride = new SavedBool("CoverageOverride", FileName, false, true);
                WeatherFairCoverage = new SavedFloat("WeatherFairCoverage", FileName, Defaults.FairCoverage, true);
                WeatherOvercastCoverage = new SavedFloat("WeatherOvercastCoverage", FileName, Defaults.Coverage, true);

                RainEnabled = new SavedBool("RainEnabled", FileName, true, true);
                RainCurtains = new SavedFloat("RainCurtains", FileName, 1f, true);
                RainStreaks = new SavedFloat("RainStreaks", FileName, 1f, true);
                RainStreakHeight = new SavedFloat("RainStreakHeight", FileName, 450f, true);
                RainLocalised = new SavedBool("RainLocalised", FileName, true, true);
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

                // All new keys. The old HaloFogEnabled/HaloFogValue belonged to an experiment where
                // values like -5 were normal; here -5 would simply mean "no glow", and an old
                // "enabled" must not switch on a replacement shader nobody has opted into.
                // Defaults are neutral: enabling the feature reproduces a clear vanilla night.
                HaloEnabled = new SavedBool("HaloEnabled", FileName, false, true);
                HaloReplaceShader = new SavedBool("HaloReplaceShader", FileName, true, true);
                HaloFogAmount = new SavedFloat("HaloFogAmount", FileName, 0f, true);
                HaloBrightness = new SavedFloat("HaloBrightness", FileName, 1f, true);
                HaloTightness = new SavedFloat("HaloTightness", FileName, 1f, true);
                HaloRadius = new SavedFloat("HaloRadius", FileName, 1f, true);

                // 300 m is the distance Anthony named; the multipliers start neutral, so the
                // distance alone changes nothing.
                HaloNearLightDistance = new SavedFloat("HaloNearLightDistance", FileName, 300f, true);
                HaloNearLightBrightness = new SavedFloat("HaloNearLightBrightness", FileName, 1f, true);
                HaloNearLightTightness = new SavedFloat("HaloNearLightTightness", FileName, 1f, true);
                HaloNearLightRadius = new SavedFloat("HaloNearLightRadius", FileName, 1f, true);

                HaloAdjustEnabled = new SavedBool("HaloAdjustEnabled", FileName, false, true);
                HaloRangeScale = new SavedFloat("HaloRangeScale", FileName, 1f, true);
                HaloIntensityScale = new SavedFloat("HaloIntensityScale", FileName, 1f, true);
                DynamicHaloCutoff = new SavedFloat("HaloNearDistance", FileName, 0f, true);

                // There is deliberately no DebugSkipDrawLight any more. It suppressed every
                // dynamic light, was saved as true during one experiment, and then lost its
                // checkbox when the settings moved into the panel -- so it stayed on, unseen,
                // hiding vehicle lights for every session after. A debug switch that can
                // change the picture must never outlive its UI.
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
