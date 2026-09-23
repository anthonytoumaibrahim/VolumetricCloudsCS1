using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>A colour: swatch and picker, a text field, Copy, Paste, reset. The F4 panel only.</summary>
        Colour,
    }

    /// <summary>Which tab of the in-game (F4) panel a row appears on. None = not in it.</summary>
    public enum PanelPage { None, Now, Clouds, Fog }

    /// <summary>
    /// General = the game's mod options page (which has no tabs since 1.1.0). The others are
    /// the in-game panel's ADVANCED tabs, shown with "Show advanced options in the in-game
    /// panel". None = neither. (The name is from when the options page had all five as tabs.)
    /// </summary>
    public enum OptionsPage { None, Weather, Light, Rendering, Halos, General }

    /// <summary>
    /// One setting, everything either UI needs to draw it, and its default.
    /// </summary>
    /// <remarks>
    /// A class rather than a struct: net35, and they are built once and then referenced.
    ///
    /// A row holds no words. Its texts are in Localization/en.xml (and its translations) under
    /// its <see cref="Name"/>: "CloudAltitude.Label", "CloudAltitude.Tooltip", and so on, read
    /// in the game's language every time a UI asks. <see cref="SettingsCatalog.LogLayout"/>
    /// checks at startup that English has every key a row needs.
    /// </remarks>
    public class Row
    {
        public RowKind Kind;
        public PanelPage Panel;
        public OptionsPage Options;

        /// <summary>
        /// The name of a row with no setting (a button, a status line). A row with a setting
        /// is named after it. Either way the name is its key in the language files.
        /// </summary>
        public string Id;

        /// <summary>Non-null starts a new group here. Its title is "Group.&lt;Group&gt;" in the language files.</summary>
        public string Group;

        /// <summary>A line of text under the row ("&lt;Name&gt;.Note"), for the warnings a tooltip would hide.</summary>
        public bool HasNote;

        /// <summary>
        /// Toggles: ask "&lt;Name&gt;.Confirm" before switching ON. Never asked when switching off.
        /// </summary>
        public bool Confirm;

        public FloatSetting Float;
        public BoolSetting Bool;
        public IntSetting Int;
        public SavedInputKey Key;
        public ColorSetting Colour;

        // The SAME constants Settings.Init() constructs from. A setting does not remember its
        // own default, so this is the only thing that can put one back.
        public float DefaultFloat;
        public bool DefaultBool;
        public int DefaultInt;
        public int DefaultKey;
        public Color32 DefaultColour;

        public float Min, Max, Step;

        /// <summary>Formats the slider readout. Null means "N%".</summary>
        public Func<float, string> Format;

        /// <summary>Choice rows: the language keys of the choices' names, one per value.</summary>
        public string[] ChoiceKeys;

        /// <summary>Choice rows without names: a unit ("Unit.Hz") to write each value with. Null: the bare number.</summary>
        public string ChoiceUnit;

        /// <summary>Choice rows whose names are not in the language files (the languages' own names): value -> name.</summary>
        public Func<int, string> ChoiceText;

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
        /// True while this row's confirmation dialog is open. The stored value is still the old
        /// one at that point, so a refresh would untick the box the player just ticked.
        /// </summary>
        public bool Pending;

        /// <summary>
        /// The name this row is stored under in VolumetricClouds.xml, the log and (later)
        /// profiles, and its key in the language files.
        /// </summary>
        public string Name
        {
            get
            {
                if (Float != null) return Float.name;
                if (Bool != null) return Bool.name;
                if (Int != null) return Int.name;
                if (Key != null) return Key.name;
                if (Colour != null) return Colour.name;
                return Id;
            }
        }

        // ---- the texts, in the game's language ------------------------------------------------

        public string Label
        {
            get { return Localization.Get(Name + ".Label"); }
        }

        public string Tooltip
        {
            get { return Localization.Get(Name + ".Tooltip"); }
        }

        /// <summary>Null when the row has none.</summary>
        public string Note
        {
            get { return HasNote ? Localization.Get(Name + ".Note") : null; }
        }

        /// <summary>The question asked before switching ON; null when there is none.</summary>
        public string ConfirmOn
        {
            get { return Confirm ? Localization.Get(Name + ".Confirm") : null; }
        }

        /// <summary>Null unless a group starts here.</summary>
        public string GroupTitle
        {
            get { return Group != null ? Localization.Get("Group." + Group) : null; }
        }

        /// <summary>The name of every choice, in order. Null for a row that is not a choice.</summary>
        public string[] Choices
        {
            get
            {
                if (ChoiceValues == null)
                    return null;

                var names = new string[ChoiceValues.Length];
                for (int i = 0; i < names.Length; i++)
                    names[i] = ChoiceName(i);

                return names;
            }
        }

        public string ChoiceName(int index)
        {
            if (ChoiceText != null)
                return ChoiceText(ChoiceValues[index]);

            if (ChoiceKeys != null && index < ChoiceKeys.Length)
                return Localization.Get(ChoiceKeys[index]);

            string value = ChoiceValues[index].ToString(CultureInfo.InvariantCulture);
            return ChoiceUnit != null ? Localization.Get(ChoiceUnit, value) : value;
        }

        /// <summary>Every language key this row needs, for the startup check.</summary>
        public IEnumerable<string> TextKeys()
        {
            if (Kind == RowKind.Status)
                yield break;

            yield return Name + ".Label";

            if (Panel != PanelPage.None || Options != OptionsPage.None)
                yield return Name + ".Tooltip";

            if (HasNote)
                yield return Name + ".Note";

            if (Confirm)
                yield return Name + ".Confirm";

            if (Group != null)
                yield return "Group." + Group;

            if (ChoiceKeys != null)
            {
                foreach (string key in ChoiceKeys)
                    yield return key;
            }

            if (ChoiceUnit != null)
                yield return ChoiceUnit;
        }

        // ---- values ---------------------------------------------------------------------------

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

            return Localization.Get("Unit.Percent", display.ToString("F0"));
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

                // An unknown value (a preset that has been retired; the settings file refuses
                // one typed by hand) reads as the last entry, which is "Custom" wherever there is one.
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
            if (Colour != null) Colour.value = DefaultColour;
        }

        /// <summary>A colour row at its default: what greys out its reset button.</summary>
        public bool IsDefaultColour
        {
            get { return Colour == null || ColorText.Same(Colour.value, DefaultColour); }
        }
    }

    /// <summary>
    /// Every setting in the mod, once, with where it is shown and what its default is.
    /// </summary>
    /// <remarks>
    /// Built because the same ~60 rows are needed by two different UIs with two different
    /// widget vocabularies (the F4 panel's hand-built controls and the options page's
    /// UIHelper factories), and keeping two imperative lists in step is how they drift.
    /// Reset-to-defaults walks this list, so does VolumetricClouds.xml (a setting with no row
    /// is never saved), and so will profiles in 1.1.
    ///
    /// The split (1.1.0): everything about the SKY is in the panel -- its three basic tabs
    /// (Row.Panel) and its four advanced ones (Row.Options other than General); the options
    /// page has only the mod itself (Row.Options == General). A row can have both fields (the
    /// fog switch is on the Fog tab and Rendering); the panel shows it on the basic tab only.
    ///
    /// The words are in Localization/en.xml, in the same order as the rows here: a new row
    /// needs its Label and Tooltip there (the log says at startup if one is missing).
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

        /// <summary>A tab's name, in the game's language.</summary>
        public static string TabName(PanelPage page)
        {
            return Localization.Get("Tab." + page);
        }

        /// <summary>A tab's name, in the game's language.</summary>
        public static string TabName(OptionsPage page)
        {
            return Localization.Get("Tab." + page);
        }

        /// <summary>
        /// One line per page, at build time: the cheapest proof the split is what was meant.
        /// And one warning if English lacks a text any row needs -- a missing key shows on
        /// screen as the key itself, which nobody would report as a bug in the mod.
        /// </summary>
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
                    " | advanced Weather=" + CountForOptions(OptionsPage.Weather) +
                    " Light=" + CountForOptions(OptionsPage.Light) +
                    " Rendering=" + CountForOptions(OptionsPage.Rendering) +
                    " Halos=" + CountForOptions(OptionsPage.Halos) +
                    " | options page=" + CountForOptions(OptionsPage.General) +
                    " | rows with no UI=" + nowhere);

            var missing = new List<string>();
            foreach (Row row in Rows)
            {
                foreach (string key in row.TextKeys())
                {
                    if (!Localization.HasEnglish(key))
                        missing.Add(key);
                }
            }

            foreach (PanelPage page in Enum.GetValues(typeof(PanelPage)))
            {
                if (page != PanelPage.None && !Localization.HasEnglish("Tab." + page))
                    missing.Add("Tab." + page);
            }

            foreach (OptionsPage page in Enum.GetValues(typeof(OptionsPage)))
            {
                if (page != OptionsPage.None && !Localization.HasEnglish("Tab." + page))
                    missing.Add("Tab." + page);
            }

            if (missing.Count > 0)
                Log.Warn("localization: en.xml has no text for " + string.Join(", ", missing.ToArray()));
        }

        /// <summary>
        /// Puts every setting back to its shipping default -- including the rows with no UI at
        /// all, which is the other reason they are in this list. Always logged: a settings file
        /// that changed under the player is the first thing to suspect when a log looks wrong.
        /// </summary>
        /// <remarks>
        /// EVERY row, the two opt-ins included (1.1.0, the author's call): volumetric fog and
        /// "Show advanced options" used to be skipped, and "I reset, so the fog is off" was
        /// what he expected. The reset asks first, so it is the player's own click.
        /// </remarks>
        public static void ResetAll()
        {
            foreach (Row row in Rows)
                row.ResetToDefault();

            // Under the preset's guard: every value is a default now, and the rendering rows'
            // own hooks would otherwise flip the preset (High, reset a moment ago) to Custom.
            _applyingPreset = true;
            try
            {
                foreach (Row row in Rows)
                {
                    if (row.AfterChange != null)
                        row.AfterChange();
                }
            }
            finally
            {
                _applyingPreset = false;
            }

            SettingsXml.SaveNow();
            Log.Msg("settings: RESET to defaults (" + Rows.Count + " rows; volumetric fog = " +
                    OnOff(Settings.FogEnabled)() + ", advanced panel = " + OnOff(Settings.ShowAdvancedInPanel)() +
                    ", quality preset = " + PresetName(Settings.QualityPreset != null ? Settings.QualityPreset.value : Settings.Defaults.Preset) + ")");
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

            if (!value || !row.Confirm)
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

                ConfirmPanel.ShowModal(Mod.DisplayName,row.ConfirmOn,
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
                Log.Warn("Could not show the confirmation for '" + row.Name + "': " + e.Message);
                Settle(row, value);
            }
        }

        private static void Settle(Row row, bool value)
        {
            row.Bool.value = value;

            // Written to disk NOW rather than with the slider changes a second later. A switch
            // is a decision, and "I ticked it and it was off again next time" is the one report
            // a settings page must never earn -- whatever happens to the game in the next second.
            SettingsXml.SaveNow();

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

            // Restored, not cleared: ResetAll holds the same guard around this call.
            bool wasApplying = _applyingPreset;
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
                _applyingPreset = wasApplying;
            }

            Log.Msg("quality preset: " + PresetName(preset) + " -> steps=" + steps.ToString("F0") +
                    " shadowMap=" + resolution + "^2 @ " + rate + " Hz");
        }

        /// <summary>The preset's name for the LOG, in English. The UI's names are in the language files.</summary>
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

        private static Func<string> OnOff(BoolSetting setting)
        {
            return () => setting != null && setting.value ? "ON" : "off";
        }

        // ---- formatters ----------------------------------------------------------------------

        private static string Metres(float value)
        {
            return Localization.Get("Unit.Metres", value.ToString("F0"));
        }

        private static string Kilometres(float value)
        {
            return Localization.Get("Unit.Kilometres", (value / 1000f).ToString("F1"));
        }

        private static string Times(float value)
        {
            return Localization.Get("Unit.Times", value.ToString("F1"));
        }

        private static string Times2(float value)
        {
            return Localization.Get("Unit.Times", value.ToString("F2"));
        }

        private static string Steps(float value)
        {
            return value.ToString("F0");
        }

        private static string FogTintName(float value)
        {
            if (Mathf.Abs(value) < 0.025f)
                return Localization.Get("Readout.Neutral");

            return Localization.Get(value < 0f ? "Readout.Cool" : "Readout.Warm",
                Mathf.RoundToInt(Mathf.Abs(value) * 100f).ToString());
        }

        /// <summary>Shows what the number MEANS: "400%" says nothing, "every 0.8 s" does.</summary>
        private static string ActivityRate(float percent)
        {
            float rate = LightningPlacement.StrikesPerSecond(percent / 100f);
            if (rate <= 0f)
                return Localization.Get("Readout.ActivityNone", percent.ToString("F0"));

            return Localization.Get("Readout.ActivityEvery", percent.ToString("F0"),
                (1f / rate).ToString(rate >= 1f ? "F1" : "F0"));
        }

        private static string NearDistance(float value)
        {
            return value <= 0f ? Localization.Get("Readout.Off") : Metres(value);
        }

        // ---- the switches other rows are greyed out by ----------------------------------------

        private static bool FogOn()
        {
            return Settings.FogEnabled != null && Settings.FogEnabled.value;
        }

        private static bool FogLampsOn()
        {
            return FogOn() && Settings.FogLampsEnabled != null && Settings.FogLampsEnabled.value;
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

            SettingsXml.SaveNow();
            Log.Msg("setting: every override cleared; the weather, the fog and the lightning all follow the game again");
        }

        /// <summary>The line the Clouds tab shows instead of putting the brightness sliders in the panel.</summary>
        public static string DescribeBrightness()
        {
            float coverage = CloudShaderParams.Coverage;
            string brightness = Localization.Get("Unit.Percent", (Settings.EffectiveBrightness(coverage) * 100f).ToString("F0"));

            return Settings.BrightnessAuto
                ? Localization.Get("Status.Brightness.Auto", brightness,
                    Localization.Get("Unit.Percent", (coverage * 100f).ToString("F0")))
                : Localization.Get("Status.Brightness.Manual", brightness);
        }

        // ---- the catalog ----------------------------------------------------------------------

        private static void Build()
        {
            Settings.Init();

            // A first Init reads VolumetricClouds.xml, which is written from this list, so it
            // may have built it already.
            if (_rows != null)
                return;

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
                Id = "FollowTheGame",
                OnClick = FollowTheGame,
                Profiled = false,
            });

            Add(new Row
            {
                Kind = RowKind.Status,
                Panel = PanelPage.Now,
                Id = "WeatherStatus",
                StatusText = CloudWeather.Describe,
                Profiled = false,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Now,
                Bool = Settings.CoverageOverride,
                DefaultBool = Settings.Defaults.CoverageOverride,
                AfterChange = State("ignore the game's weather", OnOff(Settings.CoverageOverride)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Now,
                Float = Settings.Coverage,
                DefaultFloat = Settings.Defaults.Coverage,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = CoverageOverridden,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Now,
                Float = Settings.WindSpeed,
                DefaultFloat = Settings.Defaults.WindSpeed,
                Min = 0f, Max = 5f, Step = 0.1f,
                Format = Times,
            });

            Add(new Row
            {
                Kind = RowKind.Status,
                Panel = PanelPage.Now,
                Id = "FogStatus",
                StatusText = CloudFog.Describe,
                Profiled = false,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Now,
                Bool = Settings.FogOverride,
                DefaultBool = Settings.Defaults.FogOverride,
                Enabled = FogOn,
                AfterChange = State("override fog", OnOff(Settings.FogOverride)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Now,
                Float = Settings.FogAmount,
                DefaultFloat = Settings.Defaults.FogAmount,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = FogOverridden,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Now,
                Bool = Settings.LightningOverride,
                DefaultBool = Settings.Defaults.LightningOverride,
                Enabled = LightningOn,
                AfterChange = State("lightning activity override", OnOff(Settings.LightningOverride)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Now,
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
                Id = "TestLightning",
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
                Bool = Settings.CloudsVisible,
                DefaultBool = Settings.Defaults.CloudsVisible,
                AfterChange = State("show clouds", OnOff(Settings.CloudsVisible)),
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Clouds,
                Float = Settings.CloudAltitude,
                DefaultFloat = Settings.Defaults.Altitude,
                Min = 200f, Max = 3000f, Step = 50f,
                Format = Metres,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Clouds,
                Float = Settings.CloudThickness,
                DefaultFloat = Settings.Defaults.Thickness,
                Min = 150f, Max = 2000f, Step = 50f,
                Format = Metres,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Clouds,
                Float = Settings.CloudBreakup,
                DefaultFloat = Settings.Defaults.Breakup,
                Min = 0f, Max = 100f, Step = 5f,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Clouds,
                Float = Settings.CloudBreakupScale,
                DefaultFloat = Settings.Defaults.BreakupScale,
                Min = 1.5f, Max = 10f, Step = 0.25f,
                Format = Times2,
            });

            Add(new Row
            {
                Kind = RowKind.Status,
                Panel = PanelPage.Clouds,
                Id = "BrightnessStatus",
                StatusText = DescribeBrightness,
                Profiled = false,
            });

            // Grading: the two lights a cloud stands in, each tinted on its own. The colour
            // changes only the hue; brightness stays the brightness settings' (Sky.CloudTint).
            // No AfterChange: the panel's colour row logs each pick when it is made, not every
            // frame of a drag through the picker.
            Add(new Row
            {
                Kind = RowKind.Colour,
                Panel = PanelPage.Clouds,
                Group = "CloudColour",
                Colour = Settings.CloudSunlitColor,
                DefaultColour = Settings.Defaults.SunlitColor,
            });

            Add(new Row
            {
                Kind = RowKind.Colour,
                Panel = PanelPage.Clouds,
                Colour = Settings.CloudShadeColor,
                DefaultColour = Settings.Defaults.ShadeColor,
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
                HasNote = true,
                Confirm = true,
                Bool = Settings.FogEnabled,
                DefaultBool = Settings.Defaults.FogEnabled,
                // Only the player's own click turns this off: never an update, never a profile
                // (1.1). "Reset all settings" does, since 1.1.0 -- it is his click, and asks first.
                AfterChange = State("volumetric fog", OnOff(Settings.FogEnabled)),
            });

            // Right under the fog switch, in both UIs: it is a part of the fog.
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Fog,
                Options = OptionsPage.Rendering,
                Bool = Settings.FogLampsEnabled,
                DefaultBool = Settings.Defaults.FogLampsEnabled,
                Enabled = FogOn,
                AfterChange = State("fog lit by the city's lights", OnOff(Settings.FogLampsEnabled)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Float = Settings.FogDensity,
                DefaultFloat = Settings.Defaults.FogDensity,
                Min = 10f, Max = 500f, Step = 5f,
                Enabled = FogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Fog,
                Bool = Settings.FogFollowsGround,
                DefaultBool = Settings.Defaults.FogFollowsGround,
                Enabled = FogOn,
                AfterChange = State("fog follows the ground", OnOff(Settings.FogFollowsGround)),
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Fog,
                Float = Settings.FogBase,
                DefaultFloat = Settings.Defaults.FogBase,
                Min = 0f, Max = 1000f, Step = 10f,
                Format = Metres,
                Enabled = FogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Fog,
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
                Float = Settings.FogBreakup,
                DefaultFloat = Settings.Defaults.FogBreakup,
                Min = 0f, Max = 100f, Step = 5f,
                Enabled = FogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Fog,
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
                Float = Settings.FogBrightness,
                DefaultFloat = Settings.Defaults.FogBrightness,
                Min = 20f, Max = 300f, Step = 5f,
                Enabled = FogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Fog,
                Float = Settings.FogTint,
                DefaultFloat = Settings.Defaults.FogTint,
                Min = -1f, Max = 1f, Step = 0.05f,
                Format = FogTintName,
                Enabled = FogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Float = Settings.FogLampLight,
                DefaultFloat = Settings.Defaults.FogLampLight,
                Min = 0f, Max = 400f, Step = 5f,
                Enabled = FogLampsOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Fog,
                Float = Settings.FogLampRadius,
                DefaultFloat = Settings.Defaults.FogLampRadius,
                Min = 2f, Max = 40f, Step = 1f,
                Format = Metres,
                Enabled = FogLampsOn,
            });
        }

        /// <summary>
        /// Advanced tab Weather. How the game's weather MAPS to ours, plus the rain and the
        /// lightning: set once to describe the world, not dragged while watching the sky.
        /// </summary>
        private static void BuildWeatherOptions()
        {
            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Group = "WeatherMapping",
                Float = Settings.WeatherFairCoverage,
                DefaultFloat = Settings.Defaults.FairCoverage,
                Min = 0f, Max = 100f, Step = 1f,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Float = Settings.WeatherOvercastCoverage,
                DefaultFloat = Settings.Defaults.OvercastCoverage,
                Min = 0f, Max = 100f, Step = 1f,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Weather,
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
                Bool = Settings.RainEnabled,
                DefaultBool = Settings.Defaults.RainEnabled,
                AfterChange = State("replace the game's rain", OnOff(Settings.RainEnabled)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Float = Settings.RainCurtains,
                DefaultFloat = Settings.Defaults.RainCurtains,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = RainOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Float = Settings.RainStreaks,
                DefaultFloat = Settings.Defaults.RainStreaks,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = RainOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Weather,
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
                Bool = Settings.RainLocalised,
                DefaultBool = Settings.Defaults.RainLocalised,
                AfterChange = State("localised rain sound and wet roads", OnOff(Settings.RainLocalised)),
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Weather,
                Group = "Lightning",
                Bool = Settings.LightningEnabled,
                DefaultBool = Settings.Defaults.LightningEnabled,
                AfterChange = State("lightning", OnOff(Settings.LightningEnabled)),
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Weather,
                Bool = Settings.LightningReplaceBolt,
                DefaultBool = Settings.Defaults.LightningReplaceBolt,
                Enabled = LightningOn,
                AfterChange = State("replace the game's bolt", OnOff(Settings.LightningReplaceBolt)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Float = Settings.LightningBrightness,
                DefaultFloat = Settings.Defaults.LightningBrightness,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = LightningOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Float = Settings.LightningStormScale,
                DefaultFloat = Settings.Defaults.LightningStormScale,
                Min = 0f, Max = 400f, Step = 5f,
                Enabled = LightningOn,
            });
        }

        /// <summary>Advanced tab Light. Shadows, the brightness curve, and the two night rows.</summary>
        private static void BuildLightOptions()
        {
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Light,
                Group = "Shadows",
                Bool = Settings.CloudShadows,
                DefaultBool = Settings.Defaults.CloudShadows,
                AfterChange = State("cloud shadows", OnOff(Settings.CloudShadows)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Float = Settings.CloudShadowDarkness,
                DefaultFloat = Settings.Defaults.ShadowDarkness,
                Min = 0f, Max = 95f, Step = 5f,
                Enabled = ShadowsOn,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Light,
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
                Group = "Brightness",
                Bool = Settings.CloudBrightnessAuto,
                DefaultBool = Settings.Defaults.BrightnessAuto,
                AfterChange = State("automatic cloud brightness", OnOff(Settings.CloudBrightnessAuto)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Float = Settings.CloudBrightnessClear,
                DefaultFloat = Settings.Defaults.BrightnessClear,
                Min = 5f, Max = 300f, Step = 5f,
                Enabled = AutoBrightness,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Float = Settings.CloudBrightnessOvercast,
                DefaultFloat = Settings.Defaults.BrightnessOvercast,
                Min = 5f, Max = 300f, Step = 5f,
                Enabled = AutoBrightness,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Float = Settings.CloudBrightness,
                DefaultFloat = Settings.Defaults.Brightness,
                Min = 5f, Max = 300f, Step = 5f,
                Enabled = ManualBrightness,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Float = Settings.MinIllumination,
                DefaultFloat = Settings.Defaults.MinIllumination,
                Min = 30f, Max = 100f, Step = 1f,
                Enabled = ManualBrightness,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Float = Settings.CloudDensity,
                DefaultFloat = Settings.Defaults.Density,
                Min = 20f, Max = 300f, Step = 5f,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Group = "Night",
                Float = Settings.CloudNightOpacity,
                DefaultFloat = Settings.Defaults.NightOpacity,
                Min = 0f, Max = 100f, Step = 5f,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Light,
                Float = Settings.NightGlow,
                DefaultFloat = Settings.Defaults.NightGlow,
                Min = 0f, Max = 300f, Step = 5f,
            });
        }

        /// <summary>Advanced tab Rendering. What this costs, and the two things that cost the most.</summary>
        private static void BuildRenderingOptions()
        {
            Add(new Row
            {
                Kind = RowKind.Choice,
                Options = OptionsPage.Rendering,
                Group = "Performance",
                Int = Settings.QualityPreset,
                DefaultInt = Settings.Defaults.Preset,
                ChoiceKeys = new[] { "QualityPreset.Low", "QualityPreset.Medium", "QualityPreset.High", "QualityPreset.Custom" },
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
                Int = Settings.ShadowMapResolution,
                DefaultInt = Settings.Defaults.ShadowMapResolution,
                ChoiceValues = new[] { 512, 1024, 2048 },
                Profiled = false,
                Enabled = ShadowsOn,
                AfterChange = MarkCustomPreset,
            });

            Add(new Row
            {
                Kind = RowKind.Choice,
                Options = OptionsPage.Rendering,
                Int = Settings.ShadowMapRate,
                DefaultInt = Settings.Defaults.ShadowMapRate,
                ChoiceUnit = "Unit.Hz",
                ChoiceValues = new[] { 10, 20, 30 },
                Profiled = false,
                Enabled = ShadowsOn,
                AfterChange = MarkCustomPreset,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Rendering,
                Group = "Drawing",
                Bool = Settings.UseVolumetric,
                DefaultBool = Settings.Defaults.UseVolumetric,
                Profiled = false,
                AfterChange = State("raymarched clouds", OnOff(Settings.UseVolumetric)),
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Rendering,
                Bool = Settings.CloudDepthOcclusion,
                DefaultBool = Settings.Defaults.DepthOcclusion,
                Profiled = false,
                AfterChange = State("depth occlusion", OnOff(Settings.CloudDepthOcclusion)),
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Rendering,
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
                Float = Settings.CloudPuffSize,
                DefaultFloat = Settings.Defaults.PuffSize,
                Min = 200f, Max = 2500f, Step = 50f,
                Format = Metres,
                Profiled = false,
                Enabled = Billboards,
            });
        }

        /// <summary>
        /// Advanced tab Halos. Street and building lamps are batched at EVERY distance, so this
        /// is one replacement shader doing the whole night; the near rows are a per-light
        /// distance blend inside it.
        /// </summary>
        private static void BuildHaloOptions()
        {
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Halos,
                Group = "Halos",
                Bool = Settings.HaloEnabled,
                DefaultBool = Settings.Defaults.HaloEnabled,
                AfterChange = State("light halos", OnOff(Settings.HaloEnabled)),
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Halos,
                Bool = Settings.HaloReplaceShader,
                DefaultBool = Settings.Defaults.HaloReplaceShader,
                Enabled = HalosOn,
                AfterChange = State("replacement halo shader", OnOff(Settings.HaloReplaceShader)),
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Halos,
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
                Float = Settings.HaloBrightness,
                DefaultFloat = Settings.Defaults.HaloBrightness,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Float = Settings.HaloRadius,
                DefaultFloat = Settings.Defaults.HaloRadius,
                Min = 10f, Max = 150f, Step = 5f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Halos,
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
                Group = "NearLights",
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
                Float = Settings.HaloNearLightTightness,
                DefaultFloat = Settings.Defaults.HaloNearTightness,
                Min = 50f, Max = 300f, Step = 5f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Float = Settings.HaloNearLightBrightness,
                DefaultFloat = Settings.Defaults.HaloNearBrightness,
                Min = 0f, Max = 200f, Step = 1f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Float = Settings.HaloNearLightRadius,
                DefaultFloat = Settings.Defaults.HaloNearRadius,
                Min = 10f, Max = 200f, Step = 5f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Group = "Vehicles",
                Float = Settings.HaloVehicleBrightness,
                DefaultFloat = Settings.Defaults.HaloVehicleBrightness,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = PortedHalos,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Halos,
                Bool = Settings.HaloAdjustEnabled,
                DefaultBool = Settings.Defaults.HaloAdjustEnabled,
                Enabled = HalosOn,
                AfterChange = State("dynamic light adjustment", OnOff(Settings.HaloAdjustEnabled)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Float = Settings.HaloRangeScale,
                DefaultFloat = Settings.Defaults.HaloRangeScale,
                Min = 10f, Max = 200f, Step = 5f,
                Enabled = () => HalosOn() && Settings.HaloAdjustEnabled != null && Settings.HaloAdjustEnabled.value,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Halos,
                Float = Settings.DynamicHaloCutoff,
                DefaultFloat = Settings.Defaults.DynamicHaloCutoff,
                Min = 0f, Max = 500f, Step = 10f,
                Format = Metres,
                Enabled = () => HalosOn() && Settings.HaloAdjustEnabled != null && Settings.HaloAdjustEnabled.value,
            });
        }

        /// <summary>The game's options page, and nothing else: the mod itself, and the reset button at the very bottom.</summary>
        private static void BuildGeneralOptions()
        {
            // First: whoever cannot read the rest must find this one. The group's title says for
            // now that English is all there is (drop that with the first translation). It is on
            // the heading, not the label, because a heading spans the panel and a slider's label
            // is cut at 196 px.
            Add(new Row
            {
                Kind = RowKind.Choice,
                Options = OptionsPage.General,
                Group = "Language",
                Int = Settings.Language,
                DefaultInt = Settings.Defaults.Language,
                ChoiceValues = Localization.LanguageChoices(),
                ChoiceText = Localization.LanguageName,
                Profiled = false,
                AfterChange = () =>
                {
                    Log.Msg("setting: language = " + Localization.CurrentCode +
                            (Settings.Language != null && Settings.Language.value == 0 ? " (the game's)" : " (chosen)"));
                    ModController.RebuildPanel();
                },
            });

            Add(new Row
            {
                Kind = RowKind.Key,
                Options = OptionsPage.General,
                Group = "Panel",
                Key = Settings.ToggleKey,
                DefaultKey = Settings.DefaultToggleKey,
                Profiled = false,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.General,
                Bool = Settings.ShowAdvancedInPanel,
                DefaultBool = Settings.Defaults.ShowAdvancedInPanel,
                Profiled = false,
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
                Bool = Settings.DetailedLogging,
                DefaultBool = Settings.Defaults.DetailedLogging,
                Profiled = false,
                AfterChange = Log.ReportLevel,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.General,
                Bool = Settings.DebugChecker,
                DefaultBool = Settings.Defaults.DebugChecker,
                Profiled = false,
                AfterChange = State("debug checkerboard", OnOff(Settings.DebugChecker)),
            });

            Add(new Row
            {
                Kind = RowKind.Button,
                Options = OptionsPage.General,
                Group = "StartingOver",
                Id = "ResetAll",
                OnClick = ConfirmReset,
                Profiled = false,
            });

            // Not a setting: the pattern lives in the city's save, so "Reset all settings"
            // leaves it alone and this touches no setting. Grey at the main menu, where there is
            // no sky, and while a new pattern is being generated.
            Add(new Row
            {
                Kind = RowKind.Button,
                Options = OptionsPage.General,
                Id = "ResetPattern",
                OnClick = ConfirmResetPattern,
                Enabled = () => SkyPattern.CanReset,
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
                Float = Settings.HaloIntensityScale,
                DefaultFloat = Settings.Defaults.HaloIntensityScale,
                Min = 10f, Max = 300f, Step = 5f,
            });

            // Where the player dragged the mod's own button (no Unified UI). Wide ranges on
            // purpose: a value out of range is clamped AND written back, so a narrower range
            // would move a saved spot on a wide screen for good. The button keeps itself on
            // screen when it is placed.
            Add(new Row
            {
                Kind = RowKind.Value,
                Float = Settings.HudButtonX,
                DefaultFloat = Settings.Defaults.HudButtonX,
                Min = 0f, Max = 10000f, Step = 1f,
                Format = Steps,
                Profiled = false,
                AfterChange = ModController.PlaceHudButton,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Float = Settings.HudButtonY,
                DefaultFloat = Settings.Defaults.HudButtonY,
                Min = 0f, Max = 10000f, Step = 1f,
                Format = Steps,
                Profiled = false,
                AfterChange = ModController.PlaceHudButton,
            });
        }

        private static void ConfirmReset()
        {
            try
            {
                ConfirmPanel.ShowModal(Mod.DisplayName,Localization.Get("ResetAll.Confirm"),
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

        /// <summary>
        /// Asks first: once the game saves, the old pattern is gone. Until then, reloading the
        /// last save brings it back, which is what the question says.
        /// </summary>
        private static void ConfirmResetPattern()
        {
            if (!SkyPattern.CanReset)
                return;

            try
            {
                ConfirmPanel.ShowModal(Mod.DisplayName,Localization.Get("ResetPattern.Confirm"),
                    (component, result) =>
                    {
                        if (result != 1)
                        {
                            Log.Msg("sky: new cloud pattern cancelled");
                            return;
                        }

                        SkyPattern.RequestReset();
                        RefreshAllUIs();
                    });
            }
            catch (Exception e)
            {
                Log.Error("Could not ask about a new cloud pattern, so the sky was left as it is.", e);
            }
        }
    }
}
