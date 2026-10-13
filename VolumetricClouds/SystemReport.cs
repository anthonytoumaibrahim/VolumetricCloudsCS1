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
                        " shadows=" + On(Settings.CloudShadows, true) +
                        " rain=" + On(Settings.RainEnabled, true) +
                        " lightning=" + On(Settings.LightningEnabled, true) +
                        " rainbows=" + Sky.Rainbow.Describe() +
                        " style=" + Sky.CloudStyle.Describe() +
                        " detail=" + Sky.CloudDetail.Describe() +
                        " lightInside=" + (Sky.LightThrough.Setting * 100f).ToString("F0") + "%" +
                        " sunsetLight=" + (Sky.SunsetLight.Setting * 100f).ToString("F0") + "%" +
                        // 1.5.0: changes what shows through the far clouds (by day; at sunset and night
                        // in the time-of-day part below when they differ).
                        " hideSky=" + Percent(Settings.CloudDayOpacity, Settings.Defaults.DayOpacity) +
                        // 1.5.0: the far deck under a full overcast.
                        " cloudsToHorizon=" + On(Settings.CloudsToHorizon, Settings.Defaults.CloudsToHorizon) +
                        // 1.5.0: Smoother clouds.
                        " smootherClouds=" + On(Settings.CloudTemporal, Settings.Defaults.Temporal) +
                        " fragments=" + Sky.CloudFragments.Describe() +
                        // 1.5.0: distant clouds (cirrus/sheep/far/layers).
                        " skyLayer=" + Percent(Settings.SkyCirrus, 0f) + "/" + Percent(Settings.SkySheepClouds, 0f) + "/" +
                        Percent(Settings.SkyFarClouds, 0f) + "/" + Percent(Settings.SkyCloudLayers, 0f) +
                        " volumetricFog=" + On(Settings.FogEnabled, false) +
                        " fogDistance=" + (Sky.CloudFog.Reach / 1000f).ToString("F1") + "km" +
                        " fogLitByLights=" + On(Settings.FogLampsEnabled, true) +
                        // 1.5.0: change the fog's look and bring fog of their own (the three read 0
                        // with dynamic fog off).
                        " dynamicFog=" + On(Settings.DynamicFog, Settings.Defaults.DynamicFog) +
                        " fogChangesWithWeather=" + (Sky.CloudFog.ChangeWithWeather * 100f).ToString("F0") + "%" +
                        " morningFog=" + (Sky.CloudFog.MorningSetting * 100f).ToString("F0") + "%" +
                        " mistAfterRain=" + (Sky.CloudFog.MistSetting * 100f).ToString("F0") + "%" +
                        " fogInValleys=" + Percent(Settings.FogInValleys, Settings.Defaults.FogInValleys) +
                        (Sky.CloudFog.FollowsGround ? "" : " (level fog: none)") +
                        " fogOverWater=" + (Sky.CloudFog.WaterSetting * 100f).ToString("F0") + "%" +
                        " lightHalos=" + On(Settings.HaloEnabled, false) +
                        " | overrides: weather=" + On(Settings.CoverageOverride, false) +
                        " fog=" + On(Settings.FogOverride, false) +
                        " lightning=" + On(Settings.LightningOverride, false) +
                        " wind=" + Sky.CloudWeather.DescribeWind() +
                        " | advancedPanel=" + On(Settings.ShowAdvancedInPanel, false) +
                        " panelLook=" + UI.PanelLook.Skin +
                        // The grading changes the picture, so it is in the quiet log too.
                        " | colours: sunlit=" + Hex(Settings.CloudSunlitColor, Settings.Defaults.SunlitColor) +
                        " shade=" + Hex(Settings.CloudShadeColor, Settings.Defaults.ShadeColor) +
                        " fog=" + Hex(Settings.FogColor, Settings.Defaults.FogColor) +
                        // Like the grading: it changes the picture.
                        " | brightness: " + Brightness() +
                        // 1.5.0: every row with a clock whose Day, Sunset and Night differ (the values
                        // above are the Day's), as the settings file writes them.
                        " | time of day: " + TimeRows() +
                        // Which saved sky is being edited, if any: a slider moved goes into it.
                        " | profile=" + Profiles.Describe());

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
        /// The cloud brightness as set by day: "auto 90%..40%", or "fixed 25%" with automatic
        /// brightness off. Sunset's and night's, where they differ, are in the time-of-day part.
        /// </summary>
        private static string Brightness()
        {
            bool auto = Settings.BrightnessAuto;
            return auto
                ? "auto " + Percent(Settings.CloudBrightnessClear, Settings.Defaults.BrightnessClear) + ".." +
                  Percent(Settings.CloudBrightnessOvercast, Settings.Defaults.BrightnessOvercast)
                : "fixed " + Percent(Settings.CloudBrightness, Settings.Defaults.Brightness);
        }

        /// <summary>
        /// The rows with a clock (1.5.0) set apart by time: "CloudDensity 100/25/100, ..." (day /
        /// sunset / night, as the settings file writes them), or "the same all day".
        /// </summary>
        private static string TimeRows()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (UI.Row row in UI.SettingsCatalog.ClockRows())
            {
                string day = SettingsXml.ValueOf(row.Times[0]);
                string sunset = SettingsXml.ValueOf(row.Times[1]);
                string night = SettingsXml.ValueOf(row.Times[2]);
                if (sunset != day || night != day)
                    parts.Add(row.Name + " " + day + "/" + sunset + "/" + night);
            }

            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "every row the same all day";
        }

        private static string Percent(FloatSetting setting, float fallback)
        {
            return (Value(setting, fallback) * 100f).ToString("F0") + "%";
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
