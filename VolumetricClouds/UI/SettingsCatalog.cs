using System;
using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;
using VolumetricClouds.Lighting;
using VolumetricClouds.Sky;

namespace VolumetricClouds.UI
{
    public enum RowKind
    {
        /// <summary>A 0..1 (or multiplier) setting shown as a percentage.</summary>
        Percent,

        /// <summary>A slider in the setting's own units; <see cref="Row.Format"/> writes the readout.</summary>
        Value,

        Toggle,
        Key,
        Button,

        /// <summary>A line of text the UI keeps up to date. The F4 panel only.</summary>
        Status,

        /// <summary>A dropdown over <see cref="Row.Choices"/> / <see cref="Row.ChoiceValues"/>.</summary>
        Choice,
    }

    /// <summary>Which tab of the in-game (F4) panel a row appears on. None = not in it.</summary>
    public enum PanelPage { None, Now, Clouds, Fog }

    /// <summary>Which tab of the game's mod options page a row appears on. None = not in it.</summary>
    public enum OptionsPage { None, Weather, Light, Rendering, Halos, General }

    /// <summary>
    /// One setting, everything either UI needs to draw it, and its default.
    /// </summary>
    /// <remarks>
    /// A class rather than a struct: net35, and they are built once and then referenced.
    /// </remarks>
    public class Row
    {
        public RowKind Kind;
        public PanelPage Panel;
        public OptionsPage Options;

        public string Label;
        public string Tooltip;

        /// <summary>Non-null starts a new group here, with this as its title.</summary>
        public string Group;

        /// <summary>A line of text under the row. Used for the warnings a tooltip would hide.</summary>
        public string Note;

        /// <summary>Toggles: ask this question before switching ON. Never asked when switching off.</summary>
        public string ConfirmOn;

        public SavedFloat Float;
        public SavedBool Bool;
        public SavedInt Int;
        public SavedInputKey Key;

        // The SAME constants Settings.Init() constructs from. A Saved* does not remember its
        // own default, so this is the only thing that can put one back.
        public float DefaultFloat;
        public bool DefaultBool;
        public int DefaultInt;
        public int DefaultKey;

        public float Min, Max, Step;

        /// <summary>Formats the slider readout. Null means "N%".</summary>
        public Func<float, string> Format;

        public string[] Choices;
        public int[] ChoiceValues;

        /// <summary>Live-apply hook. Must be safe with no city loaded: the options page is.</summary>
        public Action AfterChange;

        public Func<string> StatusText;
        public Action OnClick;

        /// <summary>Greys the row out, e.g. the fog rows while fog is off.</summary>
        public Func<bool> Enabled;

        /// <summary>
        /// False only for the machine (rendering quality) and housekeeping. Nothing reads it
        /// yet -- profiles are version 1.1 -- but the answer belongs next to the row that
        /// knows it, not in a list written a year later.
        /// </summary>
        public bool Profiled = true;

        /// <summary>
        /// A deliberate, per-machine opt-in: once the player has made it, NOTHING but the
        /// player's own click on this row ever unmakes it. "Reset all settings to defaults"
        /// leaves it alone, and so must anything added later (a profile may ask to turn one ON,
        /// never off). Volumetric fog and the advanced panel are the two: Anthony, after
        /// finding his fog off at the start of a session -- "when enabled, SHOULD ALWAYS STAY
        /// ON. Never turn it off again."
        /// </summary>
        public bool Sticky;

        /// <summary>
        /// True while this row's confirmation dialog is open. The stored value is still the old
        /// one at that point, so a refresh would untick the box the player just ticked.
        /// </summary>
        public bool Pending;

        /// <summary>The key this row is stored under, for the log and (later) profiles.</summary>
        public string Name
        {
            get
            {
                if (Float != null) return Float.name;
                if (Bool != null) return Bool.name;
                if (Int != null) return Int.name;
                if (Key != null) return Key.name;
                return Label;
            }
        }

        public bool IsEnabled
        {
            get { return Enabled == null || Enabled(); }
        }

        /// <summary>The slider position: percent rows are stored as a fraction and shown x100.</summary>
        public float Display
        {
            get
            {
                if (Float == null)
                    return 0f;

                return Kind == RowKind.Percent ? Float.value * 100f : Float.value;
            }
        }

        public void Store(float display)
        {
            if (Float == null)
                return;

            Float.value = Kind == RowKind.Percent ? display / 100f : display;
        }

        public string FormatDisplay(float display)
        {
            if (Format != null)
                return Format(display);

            return display.ToString("F0") + "%";
        }

        /// <summary>The index into <see cref="Choices"/> that the stored value means.</summary>
        public int ChoiceIndex
        {
            get
            {
                if (Int == null || ChoiceValues == null)
                    return 0;

                for (int i = 0; i < ChoiceValues.Length; i++)
                {
                    if (ChoiceValues[i] == Int.value)
                        return i;
                }

                // An unknown value (a hand-edited file, or a preset that has been retired)
                // reads as the last entry, which is "Custom" wherever there is one.
                return ChoiceValues.Length - 1;
            }
        }

        /// <summary>Writes the shipping default back. Does not fire <see cref="AfterChange"/>.</summary>
        public void ResetToDefault()
        {
            if (Float != null) Float.value = DefaultFloat;
            if (Bool != null) Bool.value = DefaultBool;
            if (Int != null) Int.value = DefaultInt;
            if (Key != null) Key.value = DefaultKey;
        }
    }

    /// <summary>
    /// Every setting in the mod, once, with where it is shown and what its default is.
    /// </summary>
    /// <remarks>
    /// Built because the same ~60 rows are needed by two different UIs with two different
    /// widget vocabularies (the F4 panel's hand-built controls and the options page's
    /// UIHelper factories), and keeping two imperative lists in step is how they drift.
    /// Reset-to-defaults walks this list, and so will profiles in 1.1.
    ///
    /// The split between the two UIs is by KIND, not by tab: overrides and the things you
    /// judge against the sky are in the panel; configuration and one-time set-up are in the
    /// options page. A row can be in both (the fog switch is), which is why there are two
    /// page fields rather than one page plus a "where".
    /// </remarks>
    public static class SettingsCatalog
    {
        private static List<Row> _rows;

        /// <summary>True while a preset is writing the rendering rows, so they don't flip it to Custom.</summary>
        private static bool _applyingPreset;

        private static bool _loggedLayout;

        public static List<Row> Rows
        {
            get
            {
                if (_rows == null)
                    Build();

                return _rows;
            }
        }

        public static int CountForPanel(PanelPage page)
        {
            int n = 0;
            foreach (Row row in Rows)
            {
                if (row.Panel == page)
                    n++;
            }

            return n;
        }

        public static int CountForOptions(OptionsPage page)
        {
            int n = 0;
            foreach (Row row in Rows)
            {
                if (row.Options == page)
                    n++;
            }

            return n;
        }

        /// <summary>One line per page, at build time: the cheapest proof the split is what was meant.</summary>
        public static void LogLayout()
        {
            // Once per process: the catalog is static, and both the options page (at the main
            // menu) and the controller (in a city) ask for this line.
            if (_loggedLayout)
                return;

            _loggedLayout = true;

            int nowhere = 0;
            foreach (Row row in Rows)
            {
                if (row.Panel == PanelPage.None && row.Options == OptionsPage.None)
                    nowhere++;
            }

            Log.Msg("settings catalog: " + Rows.Count + " rows | panel Now=" + CountForPanel(PanelPage.Now) +
                    " Clouds=" + CountForPanel(PanelPage.Clouds) + " Fog=" + CountForPanel(PanelPage.Fog) +
                    " | options Weather=" + CountForOptions(OptionsPage.Weather) +
                    " Light=" + CountForOptions(OptionsPage.Light) +
                    " Rendering=" + CountForOptions(OptionsPage.Rendering) +
                    " Halos=" + CountForOptions(OptionsPage.Halos) +
                    " General=" + CountForOptions(OptionsPage.General) +
                    " | rows with no UI=" + nowhere);
        }

        /// <summary>
        /// Puts every setting back to its shipping default -- including the rows with no UI at
        /// all, which is the other reason they are in this list. Always logged: a settings file
        /// that changed under the player is the first thing to suspect when a log looks wrong.
        /// </summary>
        public static void ResetAll()
        {
            int kept = 0;

            foreach (Row row in Rows)
            {
                // The per-machine opt-ins are not part of "the settings" a reset is about:
                // they say what this player agreed to run, not how the sky looks.
                if (row.Sticky)
                {
                    kept++;
                    continue;
                }

                row.ResetToDefault();
            }

            foreach (Row row in Rows)
            {
                if (!row.Sticky && row.AfterChange != null)
                    row.AfterChange();
            }

            GameSettings.SaveAll();
            Log.Msg("settings: RESET to defaults (" + (Rows.Count - kept) + " rows; " + kept +
                    " per-machine opt-ins left as they were)");
        }

        /// <summary>
        /// Stores a checkbox, asking first where the row says to ask. The question is only ever
        /// put when switching ON -- nobody has to justify turning something off -- and the
        /// answer lands back in both UIs, which is what puts a refused checkbox back.
        /// </summary>
        public static void ApplyToggle(Row row, bool value)
        {
            if (row.Bool == null)
                return;

            if (!value || row.ConfirmOn == null)
            {
                Settle(row, value);
                return;
            }

            try
            {
                // Until the answer comes the stored value is still "off", and the panel
                // re-reads its controls four times a second: without this flag the box the
                // player just ticked unticks itself behind the dialog.
                row.Pending = true;

                ConfirmPanel.ShowModal("Volumetric Clouds", row.ConfirmOn,
                    (component, result) =>
                    {
                        row.Pending = false;
                        Settle(row, result == 1);
                    });
            }
            catch (Exception e)
            {
                // If the dialog cannot be shown, do what the player asked and say so. A
                // checkbox that silently refuses to tick reads as a broken mod.
                row.Pending = false;
                Log.Warn("Could not show the confirmation for '" + row.Label + "': " + e.Message);
                Settle(row, value);
            }
        }

        private static void Settle(Row row, bool value)
        {
            row.Bool.value = value;

            // Written to disk NOW rather than whenever the framework's background saver next
            // comes round. A switch is a decision, and "I ticked it and it was off again next
            // time" is the one report a settings page must never earn -- whatever happens to
            // the game in the next second.
            GameSettings.SaveAll();

            if (row.AfterChange != null)
                row.AfterChange();

            RefreshAllUIs();
        }

        /// <summary>
        /// Re-reads every control in both UIs. A row can be in the panel AND in the options
        /// page (the fog switch is), and either can be open while the other changes.
        /// </summary>
        public static void RefreshAllUIs()
        {
            if (OptionsUI.Instance != null)
                OptionsUI.Instance.RefreshValues();

            CloudsPanel.RefreshOpenPanel();
        }

        /// <summary>
        /// The quality preset writes the three rows that cost frames and nothing else. It
        /// describes the machine, so it is never carried by a profile and never touches the
        /// fog, which is its own opt-in.
        /// </summary>
        public static void ApplyPreset(int preset)
        {
            if (preset < 0 || preset > 2 || Settings.CloudQuality == null)
                return;

            float steps = preset == 0 ? 32f : preset == 1 ? 64f : Settings.Defaults.Quality;
            int resolution = preset == 0 ? 512 : preset == 1 ? 1024 : Settings.Defaults.ShadowMapResolution;
            int rate = preset == 0 ? 10 : Settings.Defaults.ShadowMapRate;

            _applyingPreset = true;
            try
            {
                Settings.CloudQuality.value = steps;
                if (Settings.ShadowMapResolution != null)
                    Settings.ShadowMapResolution.value = resolution;
                if (Settings.ShadowMapRate != null)
                    Settings.ShadowMapRate.value = rate;
            }
            finally
            {
                _applyingPreset = false;
            }

            Log.Msg("quality preset: " + PresetName(preset) + " -> steps=" + steps.ToString("F0") +
                    " shadowMap=" + resolution + "^2 @ " + rate + " Hz");
        }

        public static string PresetName(int preset)
        {
            switch (preset)
            {
                case 0: return "Low";
                case 1: return "Medium";
                case 2: return "High";
                default: return "Custom";
            }
        }

        /// <summary>A rendering row was dragged by hand, so the preset no longer describes it.</summary>
        private static void MarkCustomPreset()
        {
            if (_applyingPreset || Settings.QualityPreset == null || Settings.QualityPreset.value == 3)
                return;

            Settings.QualityPreset.value = 3;
            Log.Msg("quality preset: Custom (a rendering row was changed by hand)");
        }

        /// <summary>
        /// Every switch that can suppress or replace something on screen says so in the log,
        /// whatever the logging level. This is the DebugSkipDrawLight lesson as a function.
        /// </summary>
        private static Action State(string what, Func<string> value)
        {
            return () => Log.Msg("setting: " + what + " = " + value());
        }

        private static Func<string> OnOff(SavedBool setting)
        {
            return () => setting != null && setting.value ? "ON" : "off";
        }

        // ---- formatters ----------------------------------------------------------------------

        private static string Metres(float value)
        {
            return value.ToString("F0") + " m";
        }

        private static string Kilometres(float value)
        {
            return (value / 1000f).ToString("F1") + " km";
        }

        private static string Times(float value)
        {
            return value.ToString("F1") + "x";
        }

        private static string Times2(float value)
        {
            return value.ToString("F2") + "x";
        }

        private static string Steps(float value)
        {
            return value.ToString("F0");
        }

        private static string FogTintName(float value)
        {
            if (Mathf.Abs(value) < 0.025f)
                return "neutral";

            return (value < 0f ? "cool " : "warm ") + Mathf.RoundToInt(Mathf.Abs(value) * 100f) + "%";
        }

        /// <summary>Shows what the number MEANS: "400%" says nothing, "every 0.8 s" does.</summary>
        private static string ActivityRate(float percent)
        {
            float rate = LightningPlacement.StrikesPerSecond(percent / 100f);
            if (rate <= 0f)
                return "0% (none)";

            return percent.ToString("F0") + "% (every " + (1f / rate).ToString(rate >= 1f ? "F1" : "F0") + " s)";
        }

        private static string NearDistance(float value)
        {
            return value <= 0f ? "off" : Metres(value);
        }

        // ---- the switches other rows are greyed out by ----------------------------------------

        private static bool FogOn()
        {
            return Settings.FogEnabled != null && Settings.FogEnabled.value;
        }

        private static bool FogOverridden()
        {
            return FogOn() && Settings.FogOverride != null && Settings.FogOverride.value;
        }

        private static bool CoverageOverridden()
        {
            return Settings.CoverageOverride != null && Settings.CoverageOverride.value;
        }

        private static bool LightningOverridden()
        {
            return Settings.LightningOverride != null && Settings.LightningOverride.value;
        }

        private static bool LightningOn()
        {
            return Settings.LightningEnabled == null || Settings.LightningEnabled.value;
        }

        private static bool RainOn()
        {
            return Settings.RainEnabled == null || Settings.RainEnabled.value;
        }

        private static bool HalosOn()
        {
            return Settings.HaloEnabled != null && Settings.HaloEnabled.value;
        }

        private static bool PortedHalos()
        {
            return HalosOn() && (Settings.HaloReplaceShader == null || Settings.HaloReplaceShader.value);
        }

        private static bool ManualBrightness()
        {
            return !Settings.BrightnessAuto;
        }

        private static bool AutoBrightness()
        {
            return Settings.BrightnessAuto;
        }

        private static bool ShadowsOn()
        {
            return Settings.CloudShadows == null || Settings.CloudShadows.value;
        }

        private static bool Billboards()
        {
            return Settings.UseVolumetric != null && !Settings.UseVolumetric.value;
        }

        /// <summary>Clears all three overrides at once: one button for "just follow the game".</summary>
        public static void FollowTheGame()
        {
            if (Settings.CoverageOverride != null) Settings.CoverageOverride.value = false;
            if (Settings.FogOverride != null) Settings.FogOverride.value = false;
            if (Settings.LightningOverride != null) Settings.LightningOverride.value = false;

            GameSettings.SaveAll();
            Log.Msg("setting: every override cleared; the weather, the fog and the lightning all follow the game again");
        }

        /// <summary>The line the Clouds tab shows instead of putting the brightness sliders in the panel.</summary>
        public static string DescribeBrightness()
        {
            float coverage = CloudShaderParams.Coverage;
            float brightness = Settings.EffectiveBrightness(coverage);

            return "Cloud brightness: " + (brightness * 100f).ToString("F0") + "% " +
                   (Settings.BrightnessAuto
                       ? "(automatic, at intensity " + (coverage * 100f).ToString("F0") + "%)"
                       : "(set by hand)");
        }

        // ---- the catalog ----------------------------------------------------------------------

        private static void Build()
        {
            Settings.Init();
            _rows = new List<Row>();

            BuildNow();
            BuildClouds();
            BuildFog();
            BuildWeatherOptions();
            BuildLightOptions();
            BuildRenderingOptions();
            BuildHaloOptions();
            BuildGeneralOptions();
            BuildHidden();
        }

        private static void Add(Row row)
        {
            _rows.Add(row);
        }

        /// <summary>
        /// F4 -> Now. The director's tab: both status lines and every override in the mod,
        /// because those are the things you set while looking at the sky and unset an hour
        /// later. Nothing here is a look.
        /// </summary>
        private static void BuildNow()
        {
            Add(new Row
            {
                Kind = RowKind.Button,
                Panel = PanelPage.Now,
                Label = "Follow the game's weather (clear every override)",
                Tooltip = "Switches off the three overrides below in one go, so the clouds, the fog " +
                          "and the lightning all go back to following the game.",
                OnClick = FollowTheGame,
                Profiled = false,
            });

            Add(new Row
            {
                Kind = RowKind.Status,
                Panel = PanelPage.Now,
                StatusText = CloudWeather.Describe,
                Profiled = false,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Now,
                Label = "Ignore the game's weather",
                Tooltip = "Fixes the cloud intensity at the slider below, whatever the weather is doing. " +
                          "Only the clouds: rain, fog and lightning keep their own sources, and rain still " +
                          "falls only under cloud.",
                Bool = Settings.CoverageOverride,
                DefaultBool = Settings.Defaults.CoverageOverride,
                AfterChange = State("ignore the game's weather", OnOff(Settings.CoverageOverride)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Now,
                Label = "Fixed intensity",
                Tooltip = "The cloud cover to hold. 100% is not a sealed overcast: the 3D noise erodes it, " +
                          "which is what leaves sun patches at the top of the slider.",
                Float = Settings.Coverage,
                DefaultFloat = Settings.Defaults.Coverage,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = CoverageOverridden,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Now,
                Label = "Movement speed",
                Tooltip = "How fast the sky travels. Frozen while the game is paused and scaled by the " +
                          "simulation speed, so the clouds keep step with the day.",
                Float = Settings.WindSpeed,
                DefaultFloat = Settings.Defaults.WindSpeed,
                Min = 0f, Max = 5f, Step = 0.1f,
                Format = Times,
            });

            Add(new Row
            {
                Kind = RowKind.Status,
                Panel = PanelPage.Now,
                StatusText = CloudFog.Describe,
                Profiled = false,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Now,
                Label = "Override fog",
                Tooltip = "Fog on demand, for our fog only. With this off the fog follows the game's own " +
                          "fog value -- its random foggy spells, or another mod's fog slider.",
                Bool = Settings.FogOverride,
                DefaultBool = Settings.Defaults.FogOverride,
                Enabled = FogOn,
                AfterChange = State("override fog", OnOff(Settings.FogOverride)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Now,
                Label = "Fog amount",
                Tooltip = "The share of the MAP that has fog on it, exactly: 30% is fog over about a third " +
                          "of it, 100% is fog everywhere. It pools on low ground first.",
                Float = Settings.FogAmount,
                DefaultFloat = Settings.Defaults.FogAmount,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = FogOverridden,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Now,
                Label = "Set lightning activity myself",
                Tooltip = "Off, our extra lightning follows the rain. On, the slider sets it -- a dry " +
                          "thunderstorm, say. Either way it only ever happens inside cloud and never starts a fire.",
                Bool = Settings.LightningOverride,
                DefaultBool = Settings.Defaults.LightningOverride,
                Enabled = LightningOn,
                AfterChange = State("lightning activity override", OnOff(Settings.LightningOverride)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Now,
                Label = "Lightning activity",
                Tooltip = "How often a flash happens. Above 100% the storm keeps its anti-strobe damping, " +
                          "which is a photosensitivity safeguard rather than a look -- it is not tuned away " +
                          "at the top of the slider.",
                Float = Settings.LightningActivity,
                DefaultFloat = Settings.Defaults.LightningActivity,
                Min = 0f, Max = 400f, Step = 5f,
                Format = ActivityRate,
                Enabled = () => LightningOn() && LightningOverridden(),
            });

            Add(new Row
            {
                Kind = RowKind.Button,
                Panel = PanelPage.Now,
                Label = "Test lightning (visual only, in front of the camera)",
                Tooltip = "One strike ahead of the camera, alternating between a bolt to the ground and a " +
                          "sheet inside the cloud. Works while the game is paused.",
                OnClick = CloudLightning.RequestTest,
                Enabled = LightningOn,
                Profiled = false,
            });
        }

        /// <summary>F4 -> Clouds. The shape of the sky, which is judged against the sky.</summary>
        private static void BuildClouds()
        {
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Clouds,
                Label = "Show clouds",
                Tooltip = "Draws the cloud layer. With it off the game keeps its own painted sky, and our " +
                          "rain, fog and lightning stand down with it.",
                Bool = Settings.CloudsVisible,
                DefaultBool = Settings.Defaults.CloudsVisible,
                AfterChange = State("show clouds", OnOff(Settings.CloudsVisible)),
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Clouds,
                Label = "Cloud altitude",
                Tooltip = "Height of the cloud base above sea level.",
                Float = Settings.CloudAltitude,
                DefaultFloat = Settings.Defaults.Altitude,
                Min = 200f, Max = 3000f, Step = 50f,
                Format = Metres,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Clouds,
                Label = "Layer thickness",
                Tooltip = "How deep the layer is. Thicker clouds are darker underneath and cost a little more " +
                          "to march.",
                Float = Settings.CloudThickness,
                DefaultFloat = Settings.Defaults.Thickness,
                Min = 150f, Max = 2000f, Step = 50f,
                Format = Metres,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Clouds,
                Label = "Break-up (solid to ragged)",
                Tooltip = "How hard fine noise erodes the clouds: 0 leaves smooth masses, high tears them " +
                          "into tufts and opens gaps.",
                Float = Settings.CloudBreakup,
                DefaultFloat = Settings.Defaults.Breakup,
                Min = 0f, Max = 100f, Step = 5f,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Clouds,
                Label = "Break-up detail (big to fine)",
                Tooltip = "The size of that erosion: low tears off big chunks, high makes fine wisps.",
                Float = Settings.CloudBreakupScale,
                DefaultFloat = Settings.Defaults.BreakupScale,
                Min = 1.5f, Max = 10f, Step = 0.25f,
                Format = Times2,
            });

            Add(new Row
            {
                Kind = RowKind.Status,
                Panel = PanelPage.Clouds,
                StatusText = DescribeBrightness,
                Profiled = false,
            });
        }

        /// <summary>
        /// F4 -> Fog. Laid out like the Clouds tab on purpose: the fog IS a cloud layer lying
        /// on the ground, and every row is the fog's version of one over there.
        /// </summary>
        private static void BuildFog()
        {
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Fog,
                Options = OptionsPage.Rendering,
                Label = "Volumetric fog (VERY heavy on performance)",
                Note = "WARNING: This setting will significantly impact performance." +
                       "Only recommended for users with powerful hardware.",
                Tooltip = "Real fog lying on the terrain, marched in the cloud pass: it has a top, it shades " +
                          "itself and the sun breaks through it. It is also by far the heaviest thing this " +
                          "mod draws. Off, the game's own fog carries on as normal.",
                ConfirmOn = "Volumetric fog is by far the heaviest thing this mod draws and can cost a large " +
                            "part of your frame rate, most of all with the camera down in it.\n\n" +
                            "The game's own fog keeps working if you leave this off.\n\nTurn it on?",
                Bool = Settings.FogEnabled,
                DefaultBool = Settings.Defaults.FogEnabled,
                Sticky = true,
                AfterChange = State("volumetric fog", OnOff(Settings.FogEnabled)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Label = "Fog density",
                Tooltip = "How thick the fog is inside -- how far you can see in it. Costs frames: a denser " +
                          "fog is marched further.",
                Float = Settings.FogDensity,
                DefaultFloat = Settings.Defaults.FogDensity,
                Min = 10f, Max = 500f, Step = 5f,
                Enabled = FogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Fog,
                Label = "Fog height",
                Tooltip = "Metres from the ground to the top of the fog. It is measured from the terrain, so " +
                          "the layer follows hills instead of flooding the valleys.",
                Float = Settings.FogHeight,
                DefaultFloat = Settings.Defaults.FogHeight,
                Min = 20f, Max = 1000f, Step = 10f,
                Format = Metres,
                Enabled = FogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Label = "Fog break-up (solid to wispy)",
                Tooltip = "How hard fine noise eats into the fog. The clouds' Break-up, for the ground layer.",
                Float = Settings.FogBreakup,
                DefaultFloat = Settings.Defaults.FogBreakup,
                Min = 0f, Max = 100f, Step = 5f,
                Enabled = FogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Fog,
                Label = "Fog speed",
                Tooltip = "How fast it drifts and turns over. 0 is still air.",
                Float = Settings.FogSpeed,
                DefaultFloat = Settings.Defaults.FogSpeed,
                Min = 0f, Max = 5f, Step = 0.1f,
                Format = Times,
                Enabled = FogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Label = "Fog brightness",
                Tooltip = "The fog's own light. Independent of the clouds' brightness -- it used to be " +
                          "multiplied by it, which is why a value from an older version may read bright.",
                Float = Settings.FogBrightness,
                DefaultFloat = Settings.Defaults.FogBrightness,
                Min = 20f, Max = 300f, Step = 5f,
                Enabled = FogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Fog,
                Label = "Fog colour (cool to warm)",
                Tooltip = "Leans the fog towards a cold blue-grey or a warm haze. The light it stands in " +
                          "still colours it -- orange at sunset, blue at night.",
                Float = Settings.FogTint,
                DefaultFloat = Settings.Defaults.FogTint,
                Min = -1f, Max = 1f, Step = 0.05f,
                Format = FogTintName,
                Enabled = FogOn,
            });
        }

        /// <summary>
        /// Options -> Weather. How the game's weather MAPS to ours, plus the rain and the
        /// lightning: set once to describe the world, not dragged while watching the sky.
        /// </summary>
        private static void BuildWeatherOptions()
        {
            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Group = "How the game's weather maps to cloud cover",
                Label = "Intensity: clear weather",
                Tooltip = "The cover on a clear day. Raise it for more cloud when nothing is happening. " +
                          "This is not an override: the game still decides where between the two ends you are.",
                Float = Settings.WeatherFairCoverage,
                DefaultFloat = Settings.Defaults.FairCoverage,
                Min = 0f, Max = 100f, Step = 1f,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Label = "Intensity: rain",
                Tooltip = "The cover in rain, or in one of the game's cloudy spells. Raise it for more " +
                          "dramatic storms.",
                Float = Settings.WeatherOvercastCoverage,
                DefaultFloat = Settings.Defaults.OvercastCoverage,
                Min = 0f, Max = 100f, Step = 1f,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Weather,
                Label = "Weather pattern size",
                Tooltip = "How big one repeat of the weather pattern is. Smaller means more, smaller cloud " +
                          "systems crossing the map.",
                Float = Settings.WeatherTileSize,
                DefaultFloat = Settings.Defaults.WeatherTileSize,
                Min = 3000f, Max = 20000f, Step = 500f,
                Format = Kilometres,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Weather,
                Group = "Rain",
                Label = "Replace the game's rain",
                Tooltip = "Our rain falls only under the thickest cloud, in curtains you can see from a " +
                          "distance, instead of the game's even camera-locked sheet. How MUCH it rains is " +
                          "still the game's business.",
                Bool = Settings.RainEnabled,
                DefaultBool = Settings.Defaults.RainEnabled,
                AfterChange = State("replace the game's rain", OnOff(Settings.RainEnabled)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Label = "Rain curtains under clouds",
                Tooltip = "How dense the falling curtains are. 0 leaves the streaks near the camera only.",
                Float = Settings.RainCurtains,
                DefaultFloat = Settings.Defaults.RainCurtains,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = RainOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Label = "Rain streaks near camera",
                Tooltip = "How many world-anchored streaks fall past the camera. 0 leaves the curtains only.",
                Float = Settings.RainStreaks,
                DefaultFloat = Settings.Defaults.RainStreaks,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = RainOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Weather,
                Label = "Streaks fade out above",
                Tooltip = "The camera height at which the near streaks are gone. From high up you see the " +
                          "curtains instead.",
                Float = Settings.RainStreakHeight,
                DefaultFloat = Settings.Defaults.RainStreakHeight,
                Min = 100f, Max = 1500f, Step = 50f,
                Format = Metres,
                Enabled = RainOn,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Weather,
                Label = "Rain sound and wet roads follow the clouds",
                Tooltip = "The only setting that changes what the simulation sees: the rain you hear and the " +
                          "wet roads follow OUR rain, so it is dry where the sky is clear.",
                Bool = Settings.RainLocalised,
                DefaultBool = Settings.Defaults.RainLocalised,
                AfterChange = State("localised rain sound and wet roads", OnOff(Settings.RainLocalised)),
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Weather,
                Group = "Lightning",
                Label = "Lightning lights the clouds and rain",
                Tooltip = "Every strike -- the game's and ours -- lights the cloud layer and the rain from " +
                          "inside. Ours are visual only: they start no fires and the simulation cannot see them.",
                Bool = Settings.LightningEnabled,
                DefaultBool = Settings.Defaults.LightningEnabled,
                AfterChange = State("lightning", OnOff(Settings.LightningEnabled)),
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Weather,
                Label = "Replace the game's lightning bolt",
                Tooltip = "Draws our own bolt, down from our cloud base, instead of the game's fixed model. " +
                          "The game's ground lights, thunder and fires are untouched either way.",
                Bool = Settings.LightningReplaceBolt,
                DefaultBool = Settings.Defaults.LightningReplaceBolt,
                Enabled = LightningOn,
                AfterChange = State("replace the game's bolt", OnOff(Settings.LightningReplaceBolt)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Label = "Lightning brightness",
                Tooltip = "How bright the flash in the cloud and the bolt itself are.",
                Float = Settings.LightningBrightness,
                DefaultFloat = Settings.Defaults.LightningBrightness,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = LightningOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Label = "Lightning in storms",
                Tooltip = "Multiplies the activity a natural downpour produces, so a real storm can be as " +
                          "busy as you like without leaving the override on. 100% is one strike every three " +
                          "seconds at the height of a downpour.",
                Float = Settings.LightningStormScale,
                DefaultFloat = Settings.Defaults.LightningStormScale,
                Min = 0f, Max = 400f, Step = 5f,
                Enabled = LightningOn,
            });
        }

        /// <summary>Options -> Light. Shadows, the brightness curve, and the two night rows.</summary>
        private static void BuildLightOptions()
        {
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Light,
                Group = "Shadows on the ground",
                Label = "Clouds cast shadows on the ground",
                Tooltip = "Marches one sun ray per texel of a shadow map and hands it to the sun as its light " +
                          "cookie. It is the only thing in this mod that darkens the world.",
                Bool = Settings.CloudShadows,
                DefaultBool = Settings.Defaults.CloudShadows,
                AfterChange = State("cloud shadows", OnOff(Settings.CloudShadows)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Label = "Shadow darkness",
                Tooltip = "How much sunlight thick cloud takes off the ground under it. Gaps always get full " +
                          "sun, at every intensity.",
                Float = Settings.CloudShadowDarkness,
                DefaultFloat = Settings.Defaults.ShadowDarkness,
                Min = 0f, Max = 95f, Step = 5f,
                Enabled = ShadowsOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Light,
                Label = "Shadow fullness",
                Tooltip = "Above 1x, thin cloud and cloud edges cast a fuller shadow, so more of the ground " +
                          "reads as shaded. Soft-looking clouds otherwise cast surprisingly thin shadows. " +
                          "Costs a little in the shadow pass.",
                Float = Settings.CloudShadowFullness,
                DefaultFloat = Settings.Defaults.ShadowFullness,
                Min = 0.5f, Max = 8f, Step = 0.25f,
                Format = Times2,
                Enabled = ShadowsOn,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Light,
                Group = "How brightly the clouds are lit",
                Label = "Brightness follows the cloud intensity",
                Tooltip = "One number cannot suit both skies: what looks right on a full overcast is grey " +
                          "sludge on scattered fair-weather cloud. On, the brightness slides between the two " +
                          "ends below as the sky fills in, and 'Clouds dim at full overcast to' is bypassed.",
                Bool = Settings.CloudBrightnessAuto,
                DefaultBool = Settings.Defaults.BrightnessAuto,
                AfterChange = State("automatic cloud brightness", OnOff(Settings.CloudBrightnessAuto)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Label = "Brightness: clear sky",
                Tooltip = "The brightness at 0% intensity -- a few bright cumulus in the sun.",
                Float = Settings.CloudBrightnessClear,
                DefaultFloat = Settings.Defaults.BrightnessClear,
                Min = 5f, Max = 300f, Step = 5f,
                Enabled = AutoBrightness,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Label = "Brightness: full overcast",
                Tooltip = "The brightness at 100% intensity -- a sky that is one grey lid.",
                Float = Settings.CloudBrightnessOvercast,
                DefaultFloat = Settings.Defaults.BrightnessOvercast,
                Min = 5f, Max = 300f, Step = 5f,
                Enabled = AutoBrightness,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Label = "Cloud brightness (fixed)",
                Tooltip = "The one brightness used when the curve above is off.",
                Float = Settings.CloudBrightness,
                DefaultFloat = Settings.Defaults.Brightness,
                Min = 5f, Max = 300f, Step = 5f,
                Enabled = ManualBrightness,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Label = "Clouds dim at full overcast to",
                Tooltip = "Greys the clouds themselves as the sky fills in. Bypassed while the brightness " +
                          "curve is on, which does the same job in one place.",
                Float = Settings.MinIllumination,
                DefaultFloat = Settings.Defaults.MinIllumination,
                Min = 30f, Max = 100f, Step = 1f,
                Enabled = ManualBrightness,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Label = "Cloud density",
                Tooltip = "How optically dense the cloud is: higher is whiter on top and darker underneath.",
                Float = Settings.CloudDensity,
                DefaultFloat = Settings.Defaults.Density,
                Min = 20f, Max = 300f, Step = 5f,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Group = "At night",
                Label = "Clouds hide the stars at night",
                Tooltip = "Takes away the clouds' distance transparency after dark, so stars do not show " +
                          "through an overcast. By day this does nothing at all.",
                Float = Settings.CloudNightOpacity,
                DefaultFloat = Settings.Defaults.NightOpacity,
                Min = 0f, Max = 100f, Step = 5f,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Label = "Glow under the clouds at night",
                Tooltip = "A small, even, pale luminance on the cloud undersides, the way a city lights a " +
                          "night sky. Not an orange reflection of the streets -- that was tried and looked wrong.",
                Float = Settings.NightGlow,
                DefaultFloat = Settings.Defaults.NightGlow,
                Min = 0f, Max = 300f, Step = 5f,
            });
        }

        /// <summary>Options -> Rendering. What this costs, and the two things that cost the most.</summary>
        private static void BuildRenderingOptions()
        {
            Add(new Row
            {
                Kind = RowKind.Choice,
                Options = OptionsPage.Rendering,
                Group = "Performance",
                Label = "Quality preset",
                Tooltip = "Sets the raymarch steps and the shadow map together. It describes your machine, " +
                          "so it is never part of a shared preset. Moving any of the three rows below by hand " +
                          "puts this on Custom.",
                Int = Settings.QualityPreset,
                DefaultInt = Settings.Defaults.Preset,
                Choices = new[] { "Low", "Medium", "High", "Custom" },
                ChoiceValues = new[] { 0, 1, 2, 3 },
                Profiled = false,
                AfterChange = () =>
                {
                    if (Settings.QualityPreset != null)
                        ApplyPreset(Settings.QualityPreset.value);
                },
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Rendering,
                Label = "Quality (raymarch steps)",
                Tooltip = "Steps per pixel through the cloud layer, and the fog's step count with it. The " +
                          "first thing to lower on a weaker card: the cost is very nearly linear in this.",
                Float = Settings.CloudQuality,
                DefaultFloat = Settings.Defaults.Quality,
                Min = 16f, Max = 96f, Step = 8f,
                Format = Steps,
                Profiled = false,
                AfterChange = MarkCustomPreset,
            });

            Add(new Row
            {
                Kind = RowKind.Choice,
                Options = OptionsPage.Rendering,
                Label = "Cloud shadow resolution",
                Tooltip = "The shadow map's size. A fixed cost that does not depend on your screen, and the " +
                          "first thing to hand someone whose frame rate dropped.",
                Int = Settings.ShadowMapResolution,
                DefaultInt = Settings.Defaults.ShadowMapResolution,
                Choices = new[] { "512", "1024", "2048" },
                ChoiceValues = new[] { 512, 1024, 2048 },
                Profiled = false,
                Enabled = ShadowsOn,
                AfterChange = MarkCustomPreset,
            });

            Add(new Row
            {
                Kind = RowKind.Choice,
                Options = OptionsPage.Rendering,
                Label = "Cloud shadow updates",
                Tooltip = "How often the shadow map is re-marched. Lower is cheaper; the shadows then slide " +
                          "in slightly coarser steps.",
                Int = Settings.ShadowMapRate,
                DefaultInt = Settings.Defaults.ShadowMapRate,
                Choices = new[] { "10 Hz", "20 Hz", "30 Hz" },
                ChoiceValues = new[] { 10, 20, 30 },
                Profiled = false,
                Enabled = ShadowsOn,
                AfterChange = MarkCustomPreset,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Rendering,
                Group = "How the clouds are drawn",
                Label = "Raymarched volumetric clouds (off = billboards)",
                Tooltip = "Off falls back to sprite puffs, which is what a machine without the shader bundle " +
                          "gets. Much cheaper, and it looks it.",
                Bool = Settings.UseVolumetric,
                DefaultBool = Settings.Defaults.UseVolumetric,
                Profiled = false,
                AfterChange = State("raymarched clouds", OnOff(Settings.UseVolumetric)),
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Rendering,
                Label = "Buildings and terrain hide clouds behind them",
                Tooltip = "Reads the depth buffer so a hill in front of a cloud covers it. An escape hatch: " +
                          "switch it off if something renders through the sky.",
                Bool = Settings.CloudDepthOcclusion,
                DefaultBool = Settings.Defaults.DepthOcclusion,
                Profiled = false,
                AfterChange = State("depth occlusion", OnOff(Settings.CloudDepthOcclusion)),
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Rendering,
                Label = "Billboards: puff count",
                Tooltip = "How many sprite puffs the fallback places at full cover.",
                Float = Settings.CloudPuffCount,
                DefaultFloat = Settings.Defaults.PuffCount,
                Min = 0f, Max = 600f, Step = 25f,
                Format = Steps,
                Profiled = false,
                Enabled = Billboards,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Rendering,
                Label = "Billboards: puff size",
                Tooltip = "How wide one of those puffs is.",
                Float = Settings.CloudPuffSize,
                DefaultFloat = Settings.Defaults.PuffSize,
                Min = 200f, Max = 2500f, Step = 50f,
                Format = Metres,
                Profiled = false,
                Enabled = Billboards,
            });
        }

        /// <summary>
        /// Options -> Halos. Street and building lamps are batched at EVERY distance, so this
        /// is one replacement shader doing the whole night; the near rows are a per-light
        /// distance blend inside it.
        /// </summary>
        private static void BuildHaloOptions()
        {
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Halos,
                Group = "Street and building light halos",
                Label = "Customise street and building light halos",
                Tooltip = "Replaces the glow pass of the game's lamps with a port of its own shader that has " +
                          "brightness, tightness and size controls -- and cannot produce the white box the " +
                          "original does at low fog. Off leaves the game's night exactly as it is.",
                Bool = Settings.HaloEnabled,
                DefaultBool = Settings.Defaults.HaloEnabled,
                AfterChange = State("light halos", OnOff(Settings.HaloEnabled)),
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Halos,
                Label = "Use the replacement halo shader",
                Tooltip = "Off falls back to the game's own shader with only its fog value overridden, which " +
                          "is dimmer but has none of the controls below. Mainly here for comparing the two.",
                Bool = Settings.HaloReplaceShader,
                DefaultBool = Settings.Defaults.HaloReplaceShader,
                Enabled = HalosOn,
                AfterChange = State("replacement halo shader", OnOff(Settings.HaloReplaceShader)),
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Halos,
                Label = "Tightness (higher = smaller)",
                Tooltip = "Steepens the glow's falloff: keeps the bright core and sheds the wide haze. This " +
                          "is the control that makes a halo read as small.",
                Float = Settings.HaloTightness,
                DefaultFloat = Settings.Defaults.HaloTightness,
                Min = 0.5f, Max = 6f, Step = 0.05f,
                Format = Times2,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Label = "Brightness",
                Tooltip = "How bright the glow is. With a high tightness it takes a lot of this to see " +
                          "anything at all.",
                Float = Settings.HaloBrightness,
                DefaultFloat = Settings.Defaults.HaloBrightness,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Label = "Size (world radius)",
                Tooltip = "The glow's radius in the world. A halo has a fixed world size, which is why a " +
                          "lamp close to the camera needs the near rows below.",
                Float = Settings.HaloRadius,
                DefaultFloat = Settings.Defaults.HaloRadius,
                Min = 10f, Max = 150f, Step = 5f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Halos,
                Label = "Fog amount (0 = clear night)",
                Tooltip = "The fog the halos think they are in, on the game's own scale, independent of the " +
                          "world's weather. It is only a brightness knob, and it stops at -0.49 because the " +
                          "game's shader turns anything lower into a solid white box.",
                Float = Settings.HaloFogAmount,
                DefaultFloat = Settings.Defaults.HaloFogAmount,
                Min = HaloOverride.MinSafeFog, Max = 2f, Step = 0.01f,
                Format = v => v.ToString("F2"),
                Enabled = HalosOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Halos,
                Group = "The same lamps, close to the camera",
                Label = "Near lights: closer than",
                Tooltip = "Lamps nearer than this take the three multipliers below in full, fading to none " +
                          "at twice the distance. 0 switches the whole near blend off.",
                Float = Settings.HaloNearLightDistance,
                DefaultFloat = Settings.Defaults.HaloNearDistance,
                Min = 0f, Max = 1000f, Step = 10f,
                Format = NearDistance,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Label = "Near lights: tightness",
                Tooltip = "Of the tightness above.",
                Float = Settings.HaloNearLightTightness,
                DefaultFloat = Settings.Defaults.HaloNearTightness,
                Min = 50f, Max = 300f, Step = 5f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Label = "Near lights: brightness",
                Tooltip = "Of the brightness above. Below about 100% a tight halo fades out fast.",
                Float = Settings.HaloNearLightBrightness,
                DefaultFloat = Settings.Defaults.HaloNearBrightness,
                Min = 0f, Max = 200f, Step = 1f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Label = "Near lights: size",
                Tooltip = "Of the size above. This is the one that actually shrinks a blob right in front of " +
                          "the camera.",
                Float = Settings.HaloNearLightRadius,
                DefaultFloat = Settings.Defaults.HaloNearRadius,
                Min = 10f, Max = 200f, Step = 5f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Group = "Vehicles and other moving lights",
                Label = "Vehicle light halos",
                Tooltip = "Brightness for dynamic lights that are not lamps, in place of the lamp brightness " +
                          "above. Lamps drawn dynamically -- Intersection Marking Tool's props -- still take " +
                          "every lamp setting.",
                Float = Settings.HaloVehicleBrightness,
                DefaultFloat = Settings.Defaults.HaloVehicleBrightness,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Halos,
                Label = "Adjust dynamic lights (vehicles)",
                Tooltip = "An older, cruder pair of controls over the same lights: their range, and a cutoff " +
                          "that hides the glow of very close ones.",
                Bool = Settings.HaloAdjustEnabled,
                DefaultBool = Settings.Defaults.HaloAdjustEnabled,
                Enabled = HalosOn,
                AfterChange = State("dynamic light adjustment", OnOff(Settings.HaloAdjustEnabled)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Label = "Dynamic light size",
                Tooltip = "Multiplies a dynamic light's range. 100% leaves the game untouched.",
                Float = Settings.HaloRangeScale,
                DefaultFloat = Settings.Defaults.HaloRangeScale,
                Min = 10f, Max = 200f, Step = 5f,
                Enabled = () => HalosOn() && Settings.HaloAdjustEnabled != null && Settings.HaloAdjustEnabled.value,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Halos,
                Label = "Hide dynamic halos within",
                Tooltip = "Dynamic lights closer than this skip their glow pass entirely. 0 is off. Nothing " +
                          "to do with the near rows above: street lamps are never dynamic.",
                Float = Settings.DynamicHaloCutoff,
                DefaultFloat = Settings.Defaults.DynamicHaloCutoff,
                Min = 0f, Max = 500f, Step = 10f,
                Format = Metres,
                Enabled = () => HalosOn() && Settings.HaloAdjustEnabled != null && Settings.HaloAdjustEnabled.value,
            });
        }

        /// <summary>Options -> General. The mod itself, and the reset button at the very bottom.</summary>
        private static void BuildGeneralOptions()
        {
            Add(new Row
            {
                Kind = RowKind.Key,
                Options = OptionsPage.General,
                Group = "The in-game panel",
                Label = "Open this panel",
                Tooltip = "The key that shows and hides the in-game panel.",
                Key = Settings.ToggleKey,
                DefaultKey = Settings.DefaultToggleKey,
                Profiled = false,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.General,
                Label = "Show advanced options in the in-game panel",
                Tooltip = "The in-game panel normally has three tabs: Now, Clouds and Fog. Tick this and it " +
                          "also gets every tab on this page -- Weather, Light, Rendering, Halos and General -- " +
                          "so anything can be tuned while you watch the sky. Everything stays here as well.",
                Bool = Settings.ShowAdvancedInPanel,
                DefaultBool = Settings.Defaults.ShowAdvancedInPanel,
                Profiled = false,
                Sticky = true,
                AfterChange = () =>
                {
                    Log.Msg("setting: advanced options in the in-game panel = " + OnOff(Settings.ShowAdvancedInPanel)());
                    ModController.RebuildPanel();
                },
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.General,
                Label = "Show icon in Unified UI",
                Tooltip = "Puts the button in the Unified UI bar instead of on its own in the corner. " +
                          "Falls back to the corner button if Unified UI is not there.",
                Bool = Settings.ShowInUnifiedUI,
                DefaultBool = Settings.Defaults.ShowInUnifiedUI,
                Profiled = false,
                AfterChange = ModController.RefreshButton,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.General,
                Group = "Diagnostics",
                Label = "Detailed logging",
                Tooltip = "Writes the repeating diagnostic lines to VolumetricClouds.log. Off by default " +
                          "because it writes several lines a second. Tick it, reproduce the problem, then " +
                          "send the log.",
                Bool = Settings.DetailedLogging,
                DefaultBool = Settings.Defaults.DetailedLogging,
                Profiled = false,
                AfterChange = Log.ReportLevel,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.General,
                Label = "Debug: project a checkerboard instead of shadows",
                Tooltip = "Replaces the cloud shadows with a checkerboard, which is how you tell 'the cookie " +
                          "is not reaching the ground' from 'the shadows are faint'.",
                Bool = Settings.DebugChecker,
                DefaultBool = Settings.Defaults.DebugChecker,
                Profiled = false,
                AfterChange = State("debug checkerboard", OnOff(Settings.DebugChecker)),
            });

            Add(new Row
            {
                Kind = RowKind.Button,
                Options = OptionsPage.General,
                Group = "Starting over",
                Label = "Reset all settings to defaults",
                Tooltip = "Puts every setting in the mod back to what it ships with -- including the ones " +
                          "with no row of their own. Two things are left exactly as you set them, because " +
                          "they are about this machine and not about the sky: whether volumetric fog is on, " +
                          "and whether the in-game panel shows the advanced tabs.",
                OnClick = ConfirmReset,
                Profiled = false,
            });
        }

        /// <summary>
        /// Settings that are live but have no row anywhere. They are in the catalog because a
        /// reset -- and, in 1.1, a profile -- walks this list and must reach them too.
        /// </summary>
        private static void BuildHidden()
        {
            Add(new Row
            {
                Kind = RowKind.Percent,
                Label = "Dynamic light brightness",
                Float = Settings.HaloIntensityScale,
                DefaultFloat = Settings.Defaults.HaloIntensityScale,
                Min = 10f, Max = 300f, Step = 5f,
            });
        }

        private static void ConfirmReset()
        {
            try
            {
                ConfirmPanel.ShowModal("Volumetric Clouds",
                    "Put every setting back to the way the mod ships?\n\n" +
                    "This throws away a tuned sky, including anything you have changed in the in-game panel.",
                    (component, result) =>
                    {
                        if (result != 1)
                        {
                            Log.Msg("settings: reset cancelled");
                            return;
                        }

                        ResetAll();
                        RefreshAllUIs();
                    });
            }
            catch (Exception e)
            {
                // Nothing is reset without an answer: this one throws a tuned sky away.
                Log.Error("Could not ask about resetting the settings, so nothing was changed.", e);
            }
        }
    }
}
