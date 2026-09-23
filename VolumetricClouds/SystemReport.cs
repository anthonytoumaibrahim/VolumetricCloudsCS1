using System;
using ColossalFramework;
using UnityEngine;

namespace VolumetricClouds
{
    /// <summary>
    /// The one always-on line about the machine this is running on, written when a city loads.
    /// </summary>
    /// <remarks>
    /// This mod's cost is proportional to PIXELS and to the step count, and it has only ever
    /// been measured on one GPU (an RTX 5070 Ti, where the game is simulation-bound and the
    /// whole thing looks free). Until a proper sweep exists, every log a subscriber sends
    /// should at least carry the facts a first-run quality choice would need: the resolution,
    /// the card, and what they are already running the game at.
    ///
    /// The game's own graphics levels are read but deliberately NOT used to choose a preset.
    /// Its defaults are the top of the range, so a player who never opened the graphics options
    /// reads as "high-end" whatever is in the machine; and when someone HAS lowered them it is
    /// usually to shed draw calls in a big city, which is exactly the city where our cost hides
    /// for free. The signal would mostly be wrong in the expensive direction.
    /// </remarks>
    public static class SystemReport
    {
        private static bool _written;

        public static void Write()
        {
            if (_written)
                return;

            _written = true;

            try
            {
                Log.Msg("system: " + Screen.width + "x" + Screen.height +
                        (Screen.fullScreen ? " fullscreen" : " windowed") +
                        // A crash on load in a big modded game is most often memory, and ours
                        // can be the allocation that tips it over: the RAM settles that first.
                        " | RAM " + SystemInfo.systemMemorySize + " MB" +
                        " | GPU '" + SystemInfo.graphicsDeviceName + "' " + SystemInfo.graphicsMemorySize + " MB, " +
                        SystemInfo.graphicsDeviceVersion +
                        " | Unity " + Application.unityVersion +
                        ", colour space " + QualitySettings.activeColorSpace);

                Log.Msg("game graphics: shadows=" + GameSetting(global::Settings.shadowsQuality) +
                        " shadowDistance=" + GameSetting(global::Settings.shadowsDistance) +
                        " textures=" + GameSetting(global::Settings.texturesQuality) +
                        " levelOfDetail=" + GameSetting(global::Settings.levelOfDetail) +
                        " antialiasing=" + GameSetting(global::Settings.antialiasing) +
                        " anisotropic=" + GameSetting(global::Settings.anisotropicFiltering) +
                        " dof=" + GameSetting(global::Settings.dofMode) +
                        "  (read for the record; the preset below is not deduced from them)");

                int preset = Settings.QualityPreset != null ? Settings.QualityPreset.value : Settings.Defaults.Preset;
                Log.Msg("mod quality: preset=" + UI.SettingsCatalog.PresetName(preset) +
                        " steps=" + Value(Settings.CloudQuality, Settings.Defaults.Quality).ToString("F0") +
                        " shadowMap=" + Int(Settings.ShadowMapResolution, Settings.Defaults.ShadowMapResolution) + "^2 @ " +
                        Int(Settings.ShadowMapRate, Settings.Defaults.ShadowMapRate) + " Hz");

                // The feature switches, always, whatever the logging level: anything that can
                // suppress or replace what is drawn has to be visible in the log. This is the
                // DebugSkipDrawLight lesson, which cost a whole session of missing vehicle
                // lights because a switch outlived its checkbox.
                Log.Msg("features: clouds=" + On(Settings.CloudsVisible, true) +
                        " raymarched=" + On(Settings.UseVolumetric, true) +
                        " shadows=" + On(Settings.CloudShadows, true) +
                        " rain=" + On(Settings.RainEnabled, true) +
                        " lightning=" + On(Settings.LightningEnabled, true) +
                        " volumetricFog=" + On(Settings.FogEnabled, false) +
                        " fogLitByLights=" + On(Settings.FogLampsEnabled, true) +
                        " lightHalos=" + On(Settings.HaloEnabled, false) +
                        // This pair can hide vehicle halos outright, so it is named here too.
                        " dynamicLightAdjust=" + On(Settings.HaloAdjustEnabled, false) +
                        " (cutoff " + Value(Settings.DynamicHaloCutoff, Settings.Defaults.DynamicHaloCutoff).ToString("F0") + " m)" +
                        " | overrides: weather=" + On(Settings.CoverageOverride, false) +
                        " fog=" + On(Settings.FogOverride, false) +
                        " lightning=" + On(Settings.LightningOverride, false) +
                        " | debugChecker=" + On(Settings.DebugChecker, false) +
                        " advancedPanel=" + On(Settings.ShowAdvancedInPanel, false) +
                        // The grading changes the picture, so it is in the quiet log too.
                        " | colours: sunlit=" + Hex(Settings.CloudSunlitColor, Settings.Defaults.SunlitColor) +
                        " shade=" + Hex(Settings.CloudShadeColor, Settings.Defaults.ShadeColor));

                // Asked here, not inside: the method names Harmony's types, and without the
                // Harmony mod merely compiling it would fail (IsPatched names none).
                if (Patcher.IsPatched)
                    Patcher.LogSharedMethods();
            }
            catch (Exception e)
            {
                Log.Warn("Could not write the system report: " + e.Message);
            }
        }

        /// <summary>
        /// One of the game's own graphics levels. They live as SavedInts in the gameSettings
        /// file under key names the game publishes as public statics, so nothing is guessed.
        /// </summary>
        private static string GameSetting(string key)
        {
            try
            {
                if (GameSettings.FindSettingsFileByName(global::Settings.gameSettingsFile) == null)
                    return "?";

                return new SavedInt(key, global::Settings.gameSettingsFile, -1, false).value.ToString();
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static string On(BoolSetting setting, bool fallback)
        {
            return (setting != null ? setting.value : fallback) ? "on" : "off";
        }

        private static float Value(FloatSetting setting, float fallback)
        {
            return setting != null ? setting.value : fallback;
        }

        private static int Int(IntSetting setting, int fallback)
        {
            return setting != null ? setting.value : fallback;
        }

        private static string Hex(ColorSetting setting, Color32 fallback)
        {
            return ColorText.Format(setting != null ? setting.value : fallback);
        }
    }
}
