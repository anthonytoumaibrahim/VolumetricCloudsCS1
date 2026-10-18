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

        /// <summary>
        /// A line of text (<see cref="Row.Text"/>). Neither UI draws one: the only one is the
        /// current profile's name, which the panel's profile bar sets.
        /// </summary>
        Text,
    }

    /// <summary>
    /// Which basic tab of the in-game (F4) panel a row appears on. None = not in it. Light was an
    /// advanced tab until 1.3.0 (the author: it should show "even without advanced settings on").
    /// </summary>
    public enum PanelPage { None, Now, Clouds, Fog, Light, DistantClouds }

    /// <summary>
    /// General = the game's mod options page (which has no tabs since 1.1.0). The others are
    /// the in-game panel's ADVANCED tabs, shown with "Show advanced options in the in-game
    /// panel". None = neither. (The name is from when the options page had all five as tabs.)
    /// There was a Rendering tab until 1.3.0: its rows -- the quality preset and its three, and
    /// depth occlusion -- are the options page's now (the author: "its settings are now in the
    /// pause menu"). A code-only name: nothing stored says which page a row is on.
    /// </summary>
    public enum OptionsPage { None, Weather, Halos, General }

    /// <summary>
    /// One setting, everything either UI needs to draw it, and its default.
    /// </summary>
    /// <remarks>
    /// A class rather than a struct: net35, and they are built once and then referenced.
    ///
    /// A row holds no words. Its texts are in Localization/en.xml (and its translations) under
    /// its <see cref="Name"/>: "CloudAltitude.Label", "CloudAltitude.Note", and so on, read
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

        /// <summary>A line of text under the row ("&lt;Name&gt;.Note"), for a warning the label cannot hold (rows have no tooltips).</summary>
        public bool HasNote;

        /// <summary>
        /// On the game's options page: shown there only while this says so (1.3.0: the three rows
        /// the quality preset sets, under Custom only -- the author: "It's not understandable what
        /// is Custom without them"). Null: always.
        /// </summary>
        public Func<bool> OptionsPageShown;

        /// <summary>
        /// Drawn only while this says so, in either UI, taking no room while hidden (1.3.0: the
        /// night's brightness rows, while "Brightness differs at night" is ticked -- the author:
        /// "and then show these"). Null: always. <see cref="Enabled"/> greys a row out instead;
        /// a row can have both.
        /// </summary>
        public Func<bool> Shown;

        /// <summary>
        /// Toggles: runs when the PLAYER ticks the box in either UI, after the value is stored and
        /// before it is saved -- never for a hand edit of the file, a profile or a reset, which set
        /// every value they mean. (1.3.0: the night's brightness starts at the day's.)
        /// </summary>
        public Action OnTicked;

        public FloatSetting Float;
        public BoolSetting Bool;
        public IntSetting Int;
        public SavedInputKey Key;
        public ColorSetting Colour;
        public StringSetting Text;

        // The SAME constants Settings.Init() constructs from. A setting does not remember its
        // own default, so this is the only thing that can put one back.
        public float DefaultFloat;
        public bool DefaultBool;
        public int DefaultInt;
        public int DefaultKey;
        public Color32 DefaultColour;
        public string DefaultText;

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

        /// <summary>
        /// A slider over a value that is NOT a setting (1.3.0: this city's cloud position, kept in
        /// its save): read and written through these, never saved to VolumetricClouds.xml, never
        /// reset, never in a profile -- the row has no setting, and everything that walks the
        /// catalog for settings passes it by.
        /// </summary>
        public Func<float> CityValue;
        public Action<float> SetCityValue;

        /// <summary>
        /// A row added after release whose default is NOT what an existing player had: the value
        /// a settings file that lacks it stands for, when VolumetricClouds.xml is read at startup
        /// (a file with no such element was written before the row existed). Null: the default,
        /// as for every other row. The cloud style is the one: an update never swaps a player's
        /// clouds for new ones.
        /// </summary>
        public int? OlderFileInt;

        /// <summary>
        /// A row added after release whose value for a file that LACKS it is worked out from that
        /// file (1.5.0, the Sunset and Night rows of a clock): given the file's elements by name -- the
        /// retired ones included (SettingsXml.Retired) -- the text it stands for; null: the value its
        /// Day row (<see cref="TimeOf"/>) has just been given. Used where <see cref="OlderFileInt"/>
        /// is: the settings file at startup, and every profile.
        /// </summary>
        public Func<IDictionary<string, string>, string> OlderFile;

        /// <summary>
        /// A row with a clock (1.5.0, Sky.TimeOfDay): its Day, Sunset and Night rows, itself first.
        /// Null on every other row.
        /// </summary>
        public Row[] Times;

        /// <summary>A Sunset or Night row: the row whose clock it belongs to. Null on every other row.</summary>
        public Row TimeOf;

        /// <summary>On a row of a clock: 0 Day, 1 Sunset, 2 Night.</summary>
        public int Time;

        /// <summary>
        /// A row with a clock and a cloud button (1.5.0, Sky.Overcast): its Overcast rows -- under a full
        /// overcast, by day, at sunset, at night -- each the twin of the same time in <see cref="Times"/>,
        /// which are its Clear sky values. Null on every other row.
        /// </summary>
        public Row[] Overcasts;

        /// <summary>An Overcast row: its Clear sky twin, the same time of day (<see cref="Time"/>). Null on every other row.</summary>
        public Row OvercastOf;

        /// <summary>A Sunset, Night or Overcast row: drawn under the row it belongs to, never on its own.</summary>
        public bool IsLine
        {
            get { return TimeOf != null || OvercastOf != null; }
        }

        /// <summary>Live-apply hook. Must be safe with no city loaded: the options page is.</summary>
        public Action AfterChange;

        public Func<string> StatusText;
        public Action OnClick;

        /// <summary>Greys the row out, e.g. the fog rows while fog is off.</summary>
        public Func<bool> Enabled;

        /// <summary>
        /// Carried by a profile (1.3.0). False for the machine (rendering quality) and housekeeping
        /// -- does the row describe the sky, or the computer (invariant 11)? A profile posted in the
        /// Workshop comments must never set someone else's step count -- and for the Now tab: the
        /// weather of the moment, not a look (the author's call).
        /// </summary>
        public bool Profiled = true;

        /// <summary>
        /// The name this row is stored under in VolumetricClouds.xml, the log and a profile's
        /// file, and its key in the language files.
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
                if (Text != null) return Text.name;
                return Id;
            }
        }

        // ---- the texts, in the game's language ------------------------------------------------

        /// <summary>
        /// Its name in the game's language. A Sunset or Night row is its Day row's, "at sunset" / "at
        /// night" (the settings file's comments and the log; the panel writes Day / Sunset / Night).
        /// </summary>
        public string Label
        {
            get
            {
                if (OvercastOf != null)
                    return Localization.Get("Weather.WhenOvercast", OvercastOf.Label);

                if (TimeOf != null)
                    return Localization.Get(Time == 1 ? "Time.AtSunset" : "Time.AtNight", TimeOf.Label);

                return Localization.Get(Name + ".Label");
            }
        }

        /// <summary>A row with a clock (Day, Sunset and Night values).</summary>
        public bool HasClock
        {
            get { return Times != null; }
        }

        /// <summary>A row with a cloud button too (Clear sky and Overcast values at each time of day).</summary>
        public bool HasWeather
        {
            get { return Overcasts != null; }
        }

        /// <summary>Null when the row has none.</summary>
        public string Note
        {
            get { return HasNote ? Localization.Get(Name + ".Note") : null; }
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

            if (OvercastOf != null)
            {
                yield return "Weather.WhenOvercast";
                yield break;
            }

            if (TimeOf != null)
            {
                yield return Time == 1 ? "Time.AtSunset" : "Time.AtNight";
                yield break;
            }

            yield return Name + ".Label";

            if (Times != null)
                yield return "Time.ByDay";

            if (HasNote)
                yield return Name + ".Note";

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

        /// <summary>See <see cref="Shown"/>.</summary>
        public bool IsShown
        {
            get { return Shown == null || Shown(); }
        }

        /// <summary>
        /// In the in-game panel: <see cref="Shown"/>, and on a basic tab (Now, Clouds, Fog, Light) a
        /// row that would be greyed out (<see cref="Enabled"/>) is hidden instead, unless "Show
        /// advanced options in the in-game panel" is ticked -- then it is shown greyed, as before
        /// 1.4.0 (the author: "let's hide the rows instead of disabling them. They can just show when
        /// advanced settings are on"). The advanced tabs grey their rows as they always did.
        /// </summary>
        public bool IsShownInPanel
        {
            get
            {
                if (!IsShown)
                    return false;

                if (Panel == PanelPage.None || IsEnabled)
                    return true;

                return Settings.ShowAdvancedInPanel != null && Settings.ShowAdvancedInPanel.value;
            }
        }

        /// <summary>On the game's options page: <see cref="Shown"/>, and <see cref="OptionsPageShown"/> there.</summary>
        public bool IsShownOnOptionsPage
        {
            get { return IsShown && (OptionsPageShown == null || OptionsPageShown()); }
        }

        /// <summary>The slider position: percent rows are stored as a fraction and shown x100.</summary>
        public float Display
        {
            get
            {
                if (Float == null)
                    return CityValue != null ? CityValue() : 0f;

                return Kind == RowKind.Percent ? Float.value * 100f : Float.value;
            }
        }

        public void Store(float display)
        {
            if (Float == null)
            {
                if (SetCityValue != null)
                    SetCityValue(display);
                return;
            }

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
            if (Text != null) Text.value = DefaultText;
        }

        /// <summary>
        /// A row with a setting of its own the panel's reset button can put back (1.5.0): a slider
        /// (not a city value), a switch, a choice, a key or a colour.
        /// </summary>
        public bool CanReset
        {
            get { return Float != null || Bool != null || Int != null || Key != null || Colour != null; }
        }

        /// <summary>At its shipping default: what fades the row's reset button.</summary>
        public bool IsAtDefault
        {
            get
            {
                if (Float != null && !Mathf.Approximately(Float.value, DefaultFloat)) return false;
                if (Bool != null && Bool.value != DefaultBool) return false;
                if (Int != null && Int.value != DefaultInt) return false;
                if (Key != null && Key.value != DefaultKey) return false;
                if (Colour != null && !IsDefaultColour) return false;
                return true;
            }
        }

        /// <summary>A colour row at its default.</summary>
        public bool IsDefaultColour
        {
            get { return Colour == null || ColorText.Same(Colour.value, DefaultColour); }
        }

        /// <summary>
        /// What the row's reset button puts back, in the settings file's words: the picked profile's
        /// value (Profiles.KeptValueOf). Null: the mod's default.
        /// </summary>
        public string ResetText
        {
            get { return Profiles.KeptValueOf(this); }
        }

        /// <summary>At what its reset button puts back -- the picked profile's value, or the default: what fades the button.</summary>
        public bool IsAtResetValue
        {
            get
            {
                string kept = ResetText;
                return kept != null ? SettingsXml.ValueOf(this) == kept : IsAtDefault;
            }
        }

        /// <summary>The reset button's tooltip: where it goes back to.</summary>
        public string ResetTooltip
        {
            get
            {
                return ResetText != null
                    ? Localization.Get("Reset.TooltipProfile", Profiles.Selected)
                    : Localization.Get("Reset.Tooltip");
            }
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
    /// is never saved), and so does a profile's file (its rows with Row.Profiled).
    ///
    /// The split (1.1.0): everything about the SKY is in the panel -- its basic tabs (Row.Panel;
    /// four since 1.3.0, Light) and its advanced ones (Row.Options other than General); the options
    /// page has the mod itself and, since 1.3.0, what the computer can afford (Row.Options ==
    /// General: the quality preset and its rows first). A row given both fields would be shown on
    /// its basic tab only; none has both since the Rendering tab went (1.3.0).
    ///
    /// The words are in Localization/en.xml, in the same order as the rows here: a new row
    /// needs its Label there (the log says at startup if one is missing).
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
                    " Clouds=" + CountForPanel(PanelPage.Clouds) +
                    " DistantClouds=" + CountForPanel(PanelPage.DistantClouds) + " Fog=" + CountForPanel(PanelPage.Fog) +
                    " Light=" + CountForPanel(PanelPage.Light) +
                    " | advanced Weather=" + CountForOptions(OptionsPage.Weather) +
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
        ///
        /// A picked profile is let go FIRST (1.3.0): the defaults are not that profile's sky, and
        /// its Save must not be one click away from writing them into it. The profiles' files are
        /// all kept; picking one again brings its sky back, and asks nothing (Profiles.AfterReset).
        /// </remarks>
        public static void ResetAll()
        {
            string profile = Profiles.Selected;
            Profiles.Detach("reset all settings");

            foreach (Row row in Rows)
                row.ResetToDefault();

            Profiles.AfterReset();

            // Under the preset's guard: every value is a default now, and the rendering rows'
            // own hooks would otherwise flip the preset (High, reset a moment ago) to Custom.
            // One hook that throws is logged and the rest still run, and the save below still
            // happens: a reset half applied and never written is the worst of both.
            _applyingPreset = true;
            try
            {
                SettingsXml.RunAfterChange(Rows, "a reset");
            }
            finally
            {
                _applyingPreset = false;
            }

            SettingsXml.SaveNow();
            Log.Msg("settings: RESET to defaults (" + Rows.Count + " rows; volumetric fog = " +
                    OnOff(Settings.FogEnabled)() + ", advanced panel = " + OnOff(Settings.ShowAdvancedInPanel)() +
                    ", quality preset = " + PresetName(Settings.QualityPreset != null ? Settings.QualityPreset.value : Settings.Defaults.Preset) +
                    ", profile = none" + (profile.Length > 0 ? " ('" + profile + "' let go first; its file is kept)" : "") + ")");
        }

        /// <summary>
        /// Stores a checkbox the player clicked, in either UI. No switch asks first: until 1.3.0
        /// the volumetric fog did, and the warning beside it says the same.
        /// </summary>
        public static void ApplyToggle(Row row, bool value)
        {
            if (row.Bool == null)
                return;

            bool ticked = value && !row.Bool.value;
            row.Bool.value = value;

            // The player's own tick, and only that (a hand edit, a profile and a reset never come
            // through here): before the save below, so what it sets is saved with the switch.
            if (ticked && row.OnTicked != null)
                row.OnTicked();

            // Written to disk NOW rather than with the slider changes a second later. A switch
            // is a decision, and "I ticked it and it was off again next time" is the one report
            // a settings page must never earn -- whatever happens to the game in the next second.
            SettingsXml.SaveNow();

            if (row.AfterChange != null)
                row.AfterChange();

            RefreshAllUIs();
        }

        /// <summary>
        /// A row's reset button in the panel (1.5.0): that one row back to what the picked profile
        /// holds for it, or to its default with no profile picked (<see cref="Row.ResetText"/>), as
        /// if the player had moved it there -- saved now, its live hook run, both UIs refreshed. A
        /// switch goes through <see cref="ApplyToggle"/> like a click (the fog's switch included:
        /// the reset button is the player's own click).
        /// </summary>
        public static void ResetRow(Row row)
        {
            if (row == null || !row.CanReset || row.IsAtResetValue)
                return;

            if (row.Kind == RowKind.Toggle && row.Bool != null)
            {
                string kept = row.ResetText;
                Log.Msg("setting: " + row.Name + (kept != null
                    ? " reset to " + kept + ", as profile '" + Profiles.Selected + "' has it (the row's reset button)"
                    : " reset to its default (the row's reset button)"));

                bool value;
                if (kept == null || !SettingsXmlFormat.TryParseBool(kept, out value))
                    value = row.DefaultBool;

                ApplyToggle(row, value);
                return;
            }

            ResetValue(row);
            SettingsXml.SaveNow();

            if (row.AfterChange != null)
                row.AfterChange();

            RefreshAllUIs();
        }

        /// <summary>One value back to what the row's reset button puts back; logged. Not saved.</summary>
        private static void ResetValue(Row row)
        {
            string kept = row.ResetText;
            Log.Msg("setting: " + row.Name + (kept != null
                ? " reset to " + kept + ", as profile '" + Profiles.Selected + "' has it (the row's reset button)"
                : " reset to its default (the row's reset button)"));

            string problem = kept != null ? SettingsXml.ApplyValue(row, kept) : null;
            if (kept == null)
                row.ResetToDefault();
            else if (problem != null)
                Log.Warn("setting: " + row.Name + " '" + kept + "' " + problem);
        }

        /// <summary>
        /// The reset button on a row with a clock, closed (1.5.0): its Day, Sunset and Night -- and with
        /// a cloud button its Overcast values -- back to what the picked profile holds (the defaults
        /// without one), as one change, saved once.
        /// </summary>
        public static void ResetClock(Row row)
        {
            if (row != null)
                ResetRows(row, ValuesOf(row));
        }

        /// <summary>
        /// Some of a row's values back to what their reset buttons put back, as one change: a line of the
        /// panel standing for several (1.5.0, a Day line moving the clear and overcast values together).
        /// </summary>
        public static void ResetRows(Row owner, IList<Row> rows)
        {
            if (owner == null || rows == null || AtResetValue(rows))
                return;

            foreach (Row value in rows)
            {
                if (value.CanReset && !value.IsAtResetValue)
                    ResetValue(value);
            }

            SettingsXml.SaveNow();

            if (owner.AfterChange != null)
                owner.AfterChange();

            RefreshAllUIs();
        }

        /// <summary>A row with a clock whose values are all where their reset buttons would put them.</summary>
        public static bool ClockAtResetValue(Row row)
        {
            return AtResetValue(ValuesOf(row));
        }

        public static bool AtResetValue(IList<Row> rows)
        {
            foreach (Row value in rows)
            {
                if (!value.IsAtResetValue)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Every value of a row with a clock: Day, Sunset, Night, then (with a cloud button) the three
        /// Overcast ones. Just the row without a clock.
        /// </summary>
        public static Row[] ValuesOf(Row row)
        {
            if (row.Times == null)
                return new[] { row };

            if (row.Overcasts == null)
                return row.Times;

            var all = new Row[row.Times.Length + row.Overcasts.Length];
            row.Times.CopyTo(all, 0);
            row.Overcasts.CopyTo(all, row.Times.Length);
            return all;
        }

        /// <summary>A time of day's values: its Clear sky one, and its Overcast one with a cloud button.</summary>
        public static Row[] TimeValues(Row row, int time)
        {
            return row.Overcasts != null ? new[] { row.Times[time], row.Overcasts[time] } : new[] { row.Times[time] };
        }

        /// <summary>A weather's values (0 Clear sky, 1 Overcast): Day, Sunset and Night.</summary>
        public static Row[] WeatherValues(Row row, int weather)
        {
            return weather == 0 ? row.Times : row.Overcasts;
        }

        /// <summary>Rows that hold the same value (a slider's or a colour).</summary>
        public static bool SameValues(IList<Row> rows)
        {
            Row first = rows[0];
            for (int i = 1; i < rows.Count; i++)
            {
                Row other = rows[i];
                if (first.Float != null && other.Float != null && !first.Float.value.Equals(other.Float.value))
                    return false;
                if (first.Colour != null && other.Colour != null && !ColorText.Same(first.Colour.value, other.Colour.value))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// A row with a clock whose values are all the same (every existing player's): one value at
        /// every hour and in every weather.
        /// </summary>
        public static bool SameAllDay(Row row)
        {
            return row.Times == null || SameValues(ValuesOf(row));
        }

        /// <summary>Each time of day the same in both weathers (a row without a cloud button: always).</summary>
        public static bool SameInEveryWeather(Row row)
        {
            if (row.Times == null || row.Overcasts == null)
                return true;

            for (int time = 0; time < row.Times.Length; time++)
            {
                if (!SameValues(TimeValues(row, time)))
                    return false;
            }

            return true;
        }

        /// <summary>Each weather the same at every hour: Day, Sunset and Night equal, under a clear sky and under an overcast one.</summary>
        public static bool SameAtEveryHour(Row row)
        {
            if (row.Times == null)
                return true;

            return SameValues(row.Times) && (row.Overcasts == null || SameValues(row.Overcasts));
        }

        /// <summary>
        /// A row with a clock moved while its values are the same and its buttons are closed: all of
        /// them go there together, so it stays one value (the panel's slider in that state).
        /// </summary>
        public static void StoreAllDay(Row row, float display)
        {
            StoreAll(ValuesOf(row), display);
        }

        /// <summary>One slider standing for several values: each goes there.</summary>
        public static void StoreAll(IList<Row> rows, float display)
        {
            foreach (Row value in rows)
                value.Store(display);
        }

        /// <summary>
        /// Re-reads every control in both UIs. Either can be open while the other changes a setting
        /// (the options page over the panel), and one row can move others (the quality preset).
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

        /// <summary>
        /// The preset reads Custom: any value but the three presets (an unknown one reads as the
        /// last choice, see Row.ChoiceIndex). Shows the rows it sets on the options page.
        /// </summary>
        private static bool CustomPreset()
        {
            int preset = Settings.QualityPreset != null ? Settings.QualityPreset.value : Settings.Defaults.Preset;
            return preset < 0 || preset > 2;
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

        private static string SignedKilometres(float value)
        {
            return Localization.Get("Unit.Kilometres", (value / 1000f).ToString("+0.00;-0.00;0"));
        }

        private static float _nextMoveLog;

        /// <summary>A "Cloud position" slider moved: this city's clouds go there. Logged at most every two seconds of a drag.</summary>
        private static void MoveClouds(double x, double z)
        {
            CloudWind.SetShift(x, z);

            float now = Time.realtimeSinceStartup;
            if (now < _nextMoveLog)
                return;

            _nextMoveLog = now + 2f;
            Log.Msg("sky: this city's clouds moved to (" + x.ToString("F0") + ", " + z.ToString("F0") +
                    ") m from where the pattern puts them; saved with the city");
        }

        private static string Times(float value)
        {
            return Localization.Get("Unit.Times", value.ToString("F1"));
        }

        private static string Degrees(float value)
        {
            return Localization.Get("Unit.Degrees", value.ToString("F0"));
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

        private static bool FragmentsOn()
        {
            return CloudFragments.On;
        }

        private static bool FogLampsOn()
        {
            return FogOn() && Settings.FogLampsEnabled != null && Settings.FogLampsEnabled.value;
        }

        private static bool DynamicFogOn()
        {
            return FogOn() && CloudFog.Dynamic;
        }

        private static bool DistantCloudsByHand()
        {
            return !SkyLayer.Dynamic;
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

        /// <summary>Rainbows are drawn in our rain, lit where the cloud shadows say the sun reaches it.</summary>
        private static bool RainbowsPossible()
        {
            return RainOn() && ShadowsOn();
        }

        private static bool HalosOn()
        {
            return Settings.HaloEnabled != null && Settings.HaloEnabled.value;
        }

        /// <summary>The Cumulus style is picked: the rows that shape its heaps.</summary>
        private static bool CumulusPicked()
        {
            return CloudStyle.IsCumulus;
        }

        /// <summary>A style that mixes the Classic layer with clouds of its own is picked: Cumulus, Mixed or Soft.</summary>
        private static bool LayerMixPicked()
        {
            return CloudStyle.UsesNoise;
        }

        // ---- the time of day (1.5.0) ------------------------------------------------------------

        /// <summary>
        /// A slider row with a clock (Sky.TimeOfDay): <paramref name="day"/> as written, then its Sunset
        /// and Night rows -- the same kind, tab, range, readout and greying, each with its own setting
        /// and default. The panel draws them under the row, as its Day / Sunset / Night lines, while its
        /// clock is open; the settings file, profiles and resets take them like any other row. A file
        /// or profile without them carries its own values over (Row.OlderFile): at sunset its Day
        /// value, at night its Day value too, or <paramref name="olderNight"/>'s -- the night an older
        /// file had behind a switch. The Day row is built as a local first and handed in
        /// (tools/test-localization.ps1 reads the rows from that shape as well as from Add's); the two
        /// made here take their names from it (Time.AtSunset, Time.AtNight).
        /// </summary>
        private static void AddTimed(Row day, FloatSetting sunset, float sunsetDefault, FloatSetting night, float nightDefault,
            Func<IDictionary<string, string>, string> olderNight)
        {
            Row atSunset = TimeRow(day, 1);
            atSunset.Float = sunset;
            atSunset.DefaultFloat = sunsetDefault;

            Row atNight = TimeRow(day, 2);
            atNight.Float = night;
            atNight.DefaultFloat = nightDefault;

            AddClock(day, atSunset, atNight, olderNight);
        }

        /// <summary>The same for a colour row.</summary>
        private static void AddTimed(Row day, ColorSetting sunset, Color32 sunsetDefault, ColorSetting night, Color32 nightDefault,
            Func<IDictionary<string, string>, string> olderNight)
        {
            Row atSunset = TimeRow(day, 1);
            atSunset.Colour = sunset;
            atSunset.DefaultColour = sunsetDefault;

            Row atNight = TimeRow(day, 2);
            atNight.Colour = night;
            atNight.DefaultColour = nightDefault;

            AddClock(day, atSunset, atNight, olderNight);
        }

        private static Row TimeRow(Row day, int time)
        {
            return new Row
            {
                Kind = day.Kind,
                Panel = day.Panel,
                Options = day.Options,
                Min = day.Min,
                Max = day.Max,
                Step = day.Step,
                Format = day.Format,
                Enabled = day.Enabled,
                Shown = day.Shown,
                Profiled = day.Profiled,
                AfterChange = day.AfterChange,
                TimeOf = day,
                Time = time,
            };
        }

        private static void AddClock(Row day, Row atSunset, Row atNight, Func<IDictionary<string, string>, string> olderNight)
        {
            day.Times = new[] { day, atSunset, atNight };

            // Null: the Day value the file has just given (SettingsXml.Read).
            atSunset.OlderFile = values => null;
            atNight.OlderFile = olderNight ?? (values => null);

            Add(day);
            Add(atSunset);
            Add(atNight);
        }

        // ---- the weather (1.5.0) ----------------------------------------------------------------

        /// <summary>
        /// A row with a clock gets a cloud button (Sky.Overcast): its three Overcast rows, after its
        /// Day, Sunset and Night (<see cref="AddTimed(Row, FloatSetting, float, FloatSetting, float, Func{IDictionary{string, string}, string})"/>
        /// first) -- the same kind, range and greying, each with its own setting and default, named after
        /// its Clear sky twin + "Overcast". The panel draws them under the row while its cloud button is
        /// open; the settings file, profiles and resets take them like any other row. A file or profile
        /// without them carries the Clear sky value over (Row.OlderFile null: the twin's), so an update
        /// changes nobody's sky.
        /// </summary>
        private static void AddOvercast(Row day, FloatSetting dayOvercast, float dayDefault, FloatSetting sunsetOvercast,
            float sunsetDefault, FloatSetting nightOvercast, float nightDefault)
        {
            var rows = new[] { OvercastRow(day, 0), OvercastRow(day, 1), OvercastRow(day, 2) };
            rows[0].Float = dayOvercast;
            rows[0].DefaultFloat = dayDefault;
            rows[1].Float = sunsetOvercast;
            rows[1].DefaultFloat = sunsetDefault;
            rows[2].Float = nightOvercast;
            rows[2].DefaultFloat = nightDefault;
            AddOvercastRows(day, rows);
        }

        /// <summary>The same for a colour row.</summary>
        private static void AddOvercast(Row day, ColorSetting dayOvercast, Color32 dayDefault, ColorSetting sunsetOvercast,
            Color32 sunsetDefault, ColorSetting nightOvercast, Color32 nightDefault)
        {
            var rows = new[] { OvercastRow(day, 0), OvercastRow(day, 1), OvercastRow(day, 2) };
            rows[0].Colour = dayOvercast;
            rows[0].DefaultColour = dayDefault;
            rows[1].Colour = sunsetOvercast;
            rows[1].DefaultColour = sunsetDefault;
            rows[2].Colour = nightOvercast;
            rows[2].DefaultColour = nightDefault;
            AddOvercastRows(day, rows);
        }

        private static Row OvercastRow(Row day, int time)
        {
            Row clear = day.Times[time];
            return new Row
            {
                Kind = day.Kind,
                Panel = day.Panel,
                Options = day.Options,
                Min = day.Min,
                Max = day.Max,
                Step = day.Step,
                Format = day.Format,
                Enabled = day.Enabled,
                Shown = day.Shown,
                Profiled = day.Profiled,
                AfterChange = day.AfterChange,
                OvercastOf = clear,
                Time = time,
                // Null: the Clear sky value the file has just given (SettingsXml.Read).
                OlderFile = values => null,
            };
        }

        private static void AddOvercastRows(Row day, Row[] rows)
        {
            day.Overcasts = rows;
            foreach (Row row in rows)
                Add(row);
        }

        /// <summary>
        /// What a file or profile written before Cloud brightness (1.5.0) meant by it, from the four rows
        /// it replaced (SettingsXml.Retired): with "Brightness follows the cloud intensity" on (or not
        /// written: on was its default), its clear sky end (<paramref name="overcast"/> false) or its
        /// full overcast end; off, the fixed brightness -- under an overcast sky dimmed by "Clouds dim at
        /// full overcast to", which is exactly what the floor did to the clouds then (straight in the
        /// cover, as the Overcast values blend). Each at <paramref name="time"/> as that file had it: its
        /// Sunset and Night values, or before those its Day value and the 1.3.0 night switch's.
        /// </summary>
        internal static string OlderLight(IDictionary<string, string> values, bool overcast, int time)
        {
            string text;
            bool auto;
            if (values == null || !values.TryGetValue("CloudBrightnessAuto", out text) || !SettingsXmlFormat.TryParseBool(text, out auto))
                auto = Settings.Defaults.BrightnessAuto;

            if (auto)
            {
                return overcast
                    ? OlderTimed(values, "CloudBrightnessOvercast", time, "CloudNightBrightnessOvercast", "50", Settings.Defaults.BrightnessOvercast)
                    : OlderTimed(values, "CloudBrightnessClear", time, "CloudNightBrightnessClear", "150", Settings.Defaults.BrightnessClear);
            }

            string fixedText = OlderTimed(values, "CloudBrightness", time, "CloudNightBrightnessFixed", "25", Settings.Defaults.Brightness);
            if (!overcast)
                return fixedText;

            double brightness, floor;
            if (!SettingsXmlFormat.TryParseNumber(fixedText, out brightness))
                return fixedText;
            if (values == null || !values.TryGetValue("MinIllumination", out text) || !SettingsXmlFormat.TryParseNumber(text, out floor))
                floor = Settings.Defaults.MinIllumination * 100.0;

            // The floor's own clamp (CloudVolume: Mathf.Clamp01 of the stored fraction).
            floor = Math.Max(0.0, Math.Min(100.0, floor));
            return SettingsXmlFormat.FormatNumber((float)(brightness * floor / 100.0));
        }

        /// <summary>
        /// A 1.4.0 row with a clock as a file wrote it at <paramref name="time"/>: its Day element (its
        /// default as a slider number without one), its Sunset element or the Day value, its Night element
        /// or the 1.3.0 switch's night or the Day value -- what the clock's own rows took from it.
        /// </summary>
        private static string OlderTimed(IDictionary<string, string> values, string name, int time, string nightValueName,
            string nightValueDefault, float dayDefault)
        {
            string day;
            if (values == null || !values.TryGetValue(name, out day))
                day = SettingsXmlFormat.FormatNumber(dayDefault * 100f);

            string value;
            if (time == 1)
                return values != null && values.TryGetValue(name + "Sunset", out value) ? value : day;

            if (time == 2)
            {
                if (values != null && values.TryGetValue(name + "Night", out value))
                    return value;

                return TimeOfDay.OlderNight(values, "CloudNightBrightness", nightValueName, nightValueDefault) ?? day;
            }

            return day;
        }

        /// <summary>
        /// An older file's night for a row that was the day half of a 1.3.0 switch: its night value
        /// when the switch was on, the Day value otherwise (Sky.TimeOfDay.OlderNight).
        /// </summary>
        private static Func<IDictionary<string, string>, string> OlderNight(string switchName, string valueName, string valueDefault)
        {
            return values => TimeOfDay.OlderNight(values, switchName, valueName, valueDefault);
        }

        /// <summary>The rows with a clock, for the log and the panel.</summary>
        public static IEnumerable<Row> ClockRows()
        {
            foreach (Row row in Rows)
            {
                if (row.Times != null)
                    yield return row;
            }
        }

        private static bool ShadowsOn()
        {
            return Settings.CloudShadows == null || Settings.CloudShadows.value;
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
            BuildHaloOptions();

            // The options page, top to bottom: quality and drawing first, then the mod itself.
            BuildRenderingOptions();
            BuildGeneralOptions();
            BuildHidden();
        }

        private static void Add(Row row)
        {
            _rows.Add(row);
        }

        /// <summary>
        /// F4 -> Now. The director's tab: every override in the mod, because those are the things
        /// you set while looking at the sky and unset an hour later. Nothing here is a look -- so
        /// nothing here is in a profile either (1.3.0, the author: "let's not save anything under
        /// Now to the profiles"): picking one leaves the weather of the moment as it is, and moving
        /// these never makes a profile "unsaved". 1.5.0: the two status lines, "Follow the game's
        /// weather" and "Test lightning" went (the author's call).
        /// </summary>
        private static void BuildNow()
        {
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Now,
                Bool = Settings.CoverageOverride,
                DefaultBool = Settings.Defaults.CoverageOverride,
                Profiled = false,
                AfterChange = State("ignore the game's weather", OnOff(Settings.CoverageOverride)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Now,
                Float = Settings.Coverage,
                DefaultFloat = Settings.Defaults.Coverage,
                Min = 0f, Max = 100f, Step = 1f,
                Profiled = false,
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
                Profiled = false,
            });

            // 1.5.0 (the author: "the whole clouds just shift direction suddenly"): the game's wind as
            // always, the game's wind turning slowly, or a fixed direction (Sky.CloudWeather). Beside
            // the movement speed and, like it, the weather of the moment: not in a profile (his call).
            // Stored by value, append-only.
            Add(new Row
            {
                Kind = RowKind.Choice,
                Panel = PanelPage.Now,
                Int = Settings.WindDirection,
                DefaultInt = Settings.Defaults.WindDirection,
                ChoiceKeys = new[] { "WindDirection.Follow", "WindDirection.Slowly", "WindDirection.Fixed" },
                ChoiceValues = new[] { CloudWeather.WindFollow, CloudWeather.WindSlowly, CloudWeather.WindFixed },
                Profiled = false,
                // No AfterChange: CloudWeather.AdvanceWind logs each new pick, with the game's wind.
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Now,
                Float = Settings.WindAngle,
                DefaultFloat = Settings.Defaults.WindAngle,
                Min = 0f, Max = 355f, Step = 5f,
                Format = Degrees,
                Profiled = false,
                Shown = () => CloudWeather.WindMode == CloudWeather.WindFixed,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Now,
                Bool = Settings.FogOverride,
                DefaultBool = Settings.Defaults.FogOverride,
                Profiled = false,
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
                Profiled = false,
                Enabled = FogOverridden,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Now,
                Bool = Settings.LightningOverride,
                DefaultBool = Settings.Defaults.LightningOverride,
                Profiled = false,
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
                Profiled = false,
                Enabled = () => LightningOn() && LightningOverridden(),
            });

            // 1.3.0 (the author: "Rainbow chance should be in the Now tab ... default the value to
            // whatever is most like real life"): the share of rains that bring rainbows at all
            // (Sky.Rainbow). 100% is nature. Now-tab, so not in a profile.
            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Now,
                Float = Settings.RainbowChance,
                DefaultFloat = Settings.Defaults.RainbowChance,
                Min = 0f, Max = 100f, Step = 5f,
                Profiled = false,
                Enabled = () => RainbowsPossible() && Sky.Rainbow.Amount > 0f,
            });

            // 1.5.0 (the author: "leave the chance slider in Now tab"): our small waterspouts, for
            // every player (Sky.Waterspouts). 100% is real life on a showery coast; 0% none. Now-tab,
            // so not in a profile. Customisation rows for them would go on the Weather tab (his call),
            // none yet.
            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Now,
                Float = Settings.WaterspoutChance,
                DefaultFloat = Settings.Defaults.WaterspoutChance,
                Min = 0f, Max = 400f, Step = 5f,
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

            // Classic, the layer the mod has always drawn, Cumulus, EVE-Redux V5's model
            // (Sky.CloudStyle), Mixed (1.5.0, Sky.CloudMix): Soft's puffs with the layer coming in as
            // the intensity rises, or Soft (1.5.0, Sky.CloudSoft): puffs with soft, wispy edges. Stored
            // by value, append only. A settings file from before the row means Classic: an update never
            // swaps a player's clouds (Row.OlderFileInt).
            Add(new Row
            {
                Kind = RowKind.Choice,
                Panel = PanelPage.Clouds,
                Int = Settings.CloudStyle,
                DefaultInt = Settings.Defaults.CloudStyle,
                OlderFileInt = Settings.Defaults.CloudStyleForOlderFiles,
                ChoiceKeys = new[] { "CloudStyle.Classic", "CloudStyle.Cumulus", "CloudStyle.Mixed", "CloudStyle.Soft" },
                ChoiceValues = new[] { CloudStyle.Classic, CloudStyle.Cumulus, CloudStyle.Mixed, CloudStyle.Soft },
                AfterChange = State("cloud style", CloudStyle.Describe),
            });

            // Under Cumulus: how much of the Classic layer fills in among the heaps (1.3.0, asked
            // for); under Mixed and Soft (1.5.0) how much of their layer comes in. 100% = as they
            // shipped, the default; shown only while one of them is picked. No AfterChange (a slider's
            // runs on every tick of a drag): the frame line carries it.
            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Clouds,
                Float = Settings.CumulusLayer,
                DefaultFloat = Settings.Defaults.CumulusLayer,
                Min = 0f, Max = 100f, Step = 5f,
                Shown = LayerMixPicked,
            });

            // The cumulus rows (1.5.0): Cumulus's heaps. 100% = the heaps as they were, the defaults.
            // Shown while Cumulus is picked. (They shaped Mixed's separate clouds too until those became
            // Soft's puffs, 2026-10-17.)
            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Clouds,
                Float = Settings.CumulusSize,
                DefaultFloat = Settings.Defaults.CumulusSize,
                Min = 50f, Max = 200f, Step = 5f,
                Shown = CumulusPicked,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Clouds,
                Float = Settings.CumulusHeight,
                DefaultFloat = Settings.Defaults.CumulusHeight,
                Min = 50f, Max = 200f, Step = 5f,
                Shown = CumulusPicked,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Clouds,
                Float = Settings.CumulusAmount,
                DefaultFloat = Settings.Defaults.CumulusAmount,
                Min = 0f, Max = 200f, Step = 5f,
                Shown = CumulusPicked,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Clouds,
                // 1.5.0: a heading of their own, so the advanced panel can fold them (CloudsPanel).
                Group = "CloudShape",
                Float = Settings.CloudAltitude,
                DefaultFloat = Settings.Defaults.Altitude,
                Min = 200f, Max = 3000f, Step = 50f,
                Format = Metres,
            });

            // The rows from here to the fragments shape the Classic layer, which every style draws (under
            // Cumulus beside its heaps, which make their own heights and billows; under Soft and Mixed
            // beside their puffs, whose height the thickness sets too: Sky.CloudSoft).
            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Clouds,
                Float = Settings.CloudThickness,
                DefaultFloat = Settings.Defaults.Thickness,
                Min = 150f, Max = 2000f, Step = 50f,
                Format = Metres,
            });

            // Crisp edges, puffy billows and the light that shows them (Sky.CloudDetail); 0 is
            // the soft look from before, exactly. No AfterChange (a slider's runs per drag tick):
            // CloudShaderParams logs it going on and off.
            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Clouds,
                Float = Settings.CloudDetail,
                DefaultFloat = Settings.Defaults.Detail,
                Min = 0f, Max = 100f, Step = 5f,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Clouds,
                Float = Settings.CloudBreakup,
                DefaultFloat = Settings.Defaults.Breakup,
                Min = 0f, Max = 100f, Step = 5f,
            });

            // Break-up detail: on an Advanced tab in 1.4.0, back here in 1.5.0 (the author: "Remove the
            // Advanced tab and move the things in it back to the other tabs").
            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Clouds,
                Float = Settings.CloudBreakupScale,
                DefaultFloat = Settings.Defaults.BreakupScale,
                Min = 1.5f, Max = 10f, Step = 0.25f,
                Format = Times2,
            });

            // Small, thin shreds round the big clouds, same layer and base (Sky.CloudFragments).
            // The number is a real share of the sky; 0 is off. No AfterChange (a slider's runs
            // per drag tick): CloudShaderParams logs them going on and off.
            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Clouds,
                Float = Settings.CloudFragments,
                DefaultFloat = Settings.Defaults.Fragments,
                Min = 0f, Max = 50f, Step = 1f,
            });

            // Their style: three looks chosen side by side from offline renders (Sky.CloudFragments).
            // Stored by value, append only.
            Add(new Row
            {
                Kind = RowKind.Choice,
                Panel = PanelPage.Clouds,
                Int = Settings.CloudFragmentStyle,
                DefaultInt = Settings.Defaults.FragmentStyle,
                ChoiceKeys = new[] { "CloudFragmentStyle.Clustered", "CloudFragmentStyle.Scattered", "CloudFragmentStyle.Wispy" },
                ChoiceValues = new[] { 0, 1, 2 },
                Enabled = FragmentsOn,
            });

            // Grading: the two lights a cloud stands in, each tinted on its own. The colour
            // changes only the hue; brightness stays the brightness settings' (Sky.CloudTint).
            // No AfterChange: the panel's colour row logs each pick when it is made, not every
            // frame of a drag through the picker.
            // Each with a clock (1.5.0). The 1.3.0 switch "Different colors at night" (asked for: the
            // colours "should be different during day than night") and its Moonlit side / Shaded side
            // at night became their Night values: an older file's night comes over as it was.
            Row sunlit = new Row
            {
                Kind = RowKind.Colour,
                Panel = PanelPage.Clouds,
                Group = "CloudColour",
                Colour = Settings.CloudSunlitColor,
                DefaultColour = Settings.Defaults.SunlitColor,
            };
            AddTimed(sunlit, Settings.CloudSunlitColorSunset, Settings.Defaults.SunlitColorSunset,
                Settings.CloudSunlitColorNight, Settings.Defaults.SunlitColorNight,
                OlderNight("CloudNightColors", "CloudMoonlitColor", "#FFFFFF"));
            AddOvercast(sunlit, Settings.CloudSunlitColorOvercast, Settings.Defaults.SunlitColorOvercast,
                Settings.CloudSunlitColorSunsetOvercast, Settings.Defaults.SunlitColorSunsetOvercast,
                Settings.CloudSunlitColorNightOvercast, Settings.Defaults.SunlitColorNightOvercast);

            Row shade = new Row
            {
                Kind = RowKind.Colour,
                Panel = PanelPage.Clouds,
                Colour = Settings.CloudShadeColor,
                DefaultColour = Settings.Defaults.ShadeColor,
            };
            AddTimed(shade, Settings.CloudShadeColorSunset, Settings.Defaults.ShadeColorSunset,
                Settings.CloudShadeColorNight, Settings.Defaults.ShadeColorNight,
                OlderNight("CloudNightColors", "CloudNightShadeColor", "#FFFFFF"));
            AddOvercast(shade, Settings.CloudShadeColorOvercast, Settings.Defaults.ShadeColorOvercast,
                Settings.CloudShadeColorSunsetOvercast, Settings.Defaults.ShadeColorSunsetOvercast,
                Settings.CloudShadeColorNightOvercast, Settings.Defaults.ShadeColorNightOvercast);

            // DISTANT CLOUDS (1.5.0, Sky.SkyLayer), a tab of their own: photographed skies behind the
            // clouds, a row per kind, and they add up. 0 is off; all four at 0 and nothing of it exists.
            // Each walks through its pictures, thinnest first (Sky.SkyLayerMix). Look rows, so profiled:
            // a profile saved before them reads 0. No AfterChange (a slider's runs per drag tick):
            // SkyLayer logs them.
            // "Dynamic distant clouds" first (the author's call: on by default, in this tab): the mod
            // mixes the four kinds itself (Sky.SkyLayerMix.Dynamic) and the sliders grey -- hidden on the
            // basic tab, as every greyed row is. A look row, so profiled like the sliders.
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.DistantClouds,
                Group = "SkyLayer",
                Bool = Settings.DynamicDistantClouds,
                DefaultBool = Settings.Defaults.DynamicDistantClouds,
                AfterChange = State("dynamic distant clouds", OnOff(Settings.DynamicDistantClouds)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.DistantClouds,
                Float = Settings.SkyCirrus,
                DefaultFloat = Settings.Defaults.SkyCirrus,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = DistantCloudsByHand,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.DistantClouds,
                Float = Settings.SkySheepClouds,
                DefaultFloat = Settings.Defaults.SkySheepClouds,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = DistantCloudsByHand,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.DistantClouds,
                Float = Settings.SkyFarClouds,
                DefaultFloat = Settings.Defaults.SkyFarClouds,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = DistantCloudsByHand,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.DistantClouds,
                Float = Settings.SkyCloudLayers,
                DefaultFloat = Settings.Defaults.SkyCloudLayers,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = DistantCloudsByHand,
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
                // No warning since 1.5.0 (the author: "plenty of users confirmed it doesn't cost a lot").
                Bool = Settings.FogEnabled,
                DefaultBool = Settings.Defaults.FogEnabled,
                // Only the player's own click turns this off, never an update. "Reset all
                // settings" does, since 1.1.0 -- it is his click, and asks first. So does picking
                // a profile that has it off (1.3.0): also his click, on a sky he saved or chose.
                // Switching it on asks nothing since 1.3.0 (the author: "there's already enough
                // warnings for it" -- the label and the note).
                AfterChange = State("volumetric fog", OnOff(Settings.FogEnabled)),
            });

            // Right under the fog switch: it is a part of the fog.
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Fog,
                Bool = Settings.FogLampsEnabled,
                DefaultBool = Settings.Defaults.FogLampsEnabled,
                Enabled = FogOn,
                AfterChange = State("fog lit by the city's lights", OnOff(Settings.FogLampsEnabled)),
            });

            // With a clock (1.5.0), as are the fog's brightness, tint and colour below.
            Row density = new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                // 1.5.0: the fog's rows under three headings, so the advanced panel can fold them.
                Group = "FogShape",
                Float = Settings.FogDensity,
                DefaultFloat = Settings.Defaults.FogDensity,
                Min = 10f, Max = 500f, Step = 5f,
                Enabled = FogOn,
            };
            AddTimed(density, Settings.FogDensitySunset, Settings.Defaults.FogDensitySunset,
                Settings.FogDensityNight, Settings.Defaults.FogDensityNight, null);

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Fog,
                Bool = Settings.FogFollowsGround,
                DefaultBool = Settings.Defaults.FogFollowsGround,
                Enabled = FogOn,
                AfterChange = State("fog follows the ground", OnOff(Settings.FogFollowsGround)),
            });

            // 1.5.0 (his pick, 2026-10-04): fog gathers in valleys, river gorges and at the foot of
            // slopes, up towards the level of the land round them (Sky.FogPlaces). Only with the fog
            // following the ground: a level fog fills low ground by itself. 0% for everyone = the fog
            // as before.
            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Float = Settings.FogInValleys,
                DefaultFloat = Settings.Defaults.FogInValleys,
                Min = 0f, Max = 100f, Step = 5f,
                Enabled = () => FogOn() && CloudFog.FollowsGround && !CloudFog.Overridden,
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
                // 1.4.0 (asked for: "a very thin fog layer on the roads and ground"): from 0 m in
                // 1 m steps, was 20 m in 10 m steps. Widening keeps every saved value.
                Min = 0f, Max = 1000f, Step = 1f,
                Format = Metres,
                Enabled = FogOn,
            });

            // 1.3.1 (asked for: "increase the render distance of the fog ... a fog distance slider,
            // by default to what it is now so performance doesn't degrade"). The least is the
            // default: 9 km is the fog as it always was (Sky.CloudFog.Reach). Metres in the file.
            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Fog,
                Float = Settings.FogDistance,
                DefaultFloat = Settings.Defaults.FogDistance,
                Min = 9000f, Max = 20000f, Step = 500f,
                Format = Kilometres,
                Enabled = FogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Group = "FogLook",
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

            Row brightness = new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Float = Settings.FogBrightness,
                DefaultFloat = Settings.Defaults.FogBrightness,
                Min = 20f, Max = 300f, Step = 5f,
                Enabled = FogOn,
            };
            AddTimed(brightness, Settings.FogBrightnessSunset, Settings.Defaults.FogBrightnessSunset,
                Settings.FogBrightnessNight, Settings.Defaults.FogBrightnessNight, null);
            AddOvercast(brightness, Settings.FogBrightnessOvercast, Settings.Defaults.FogBrightnessOvercast,
                Settings.FogBrightnessSunsetOvercast, Settings.Defaults.FogBrightnessSunsetOvercast,
                Settings.FogBrightnessNightOvercast, Settings.Defaults.FogBrightnessNightOvercast);

            Row tint = new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Fog,
                Float = Settings.FogTint,
                DefaultFloat = Settings.Defaults.FogTint,
                Min = -1f, Max = 1f, Step = 0.05f,
                Format = FogTintName,
                Enabled = FogOn,
            };
            AddTimed(tint, Settings.FogTintSunset, Settings.Defaults.FogTintSunset,
                Settings.FogTintNight, Settings.Defaults.FogTintNight, null);
            AddOvercast(tint, Settings.FogTintOvercast, Settings.Defaults.FogTintOvercast,
                Settings.FogTintSunsetOvercast, Settings.Defaults.FogTintSunsetOvercast,
                Settings.FogTintNightOvercast, Settings.Defaults.FogTintNightOvercast);

            // The fog's own colour (1.1.0), like the clouds' two: hue only, white = as it was,
            // on top of the cool/warm lean above (which stays: a slider in players' files).
            // No AfterChange: the panel's colour row logs each pick itself.
            Row colour = new Row
            {
                Kind = RowKind.Colour,
                Panel = PanelPage.Fog,
                Colour = Settings.FogColor,
                DefaultColour = Settings.Defaults.FogColor,
                Enabled = FogOn,
            };
            AddTimed(colour, Settings.FogColorSunset, Settings.Defaults.FogColorSunset,
                Settings.FogColorNight, Settings.Defaults.FogColorNight, null);
            AddOvercast(colour, Settings.FogColorOvercast, Settings.Defaults.FogColorOvercast,
                Settings.FogColorSunsetOvercast, Settings.Defaults.FogColorSunsetOvercast,
                Settings.FogColorNightOvercast, Settings.Defaults.FogColorNightOvercast);

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Group = "FogLamps",
                Float = Settings.FogLampLight,
                DefaultFloat = Settings.Defaults.FogLampLight,
                Min = 0f, Max = 400f, Step = 5f,
                Enabled = FogLampsOn,
            });

            // Lit radius around a light: on the Advanced tab in 1.4.0, back here in 1.5.0.
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

            // 1.5.0, asked for by a player: fog that is "not just a fixed density of fog moving
            // around" but changes -- denser on autumn nights, "lowered if the sun shines again",
            // light and tall "after a summer rain". The sliders above are the MOST fog (the
            // author's call: "Ceiling"), so these only ever take it lower, thinner or wispier
            // (Sky.FogSources). All 0% for everyone: at 0% the fog is exactly as before. Last on the
            // tab, under their own heading: a heading claims every row below it up to the next one.
            // The three in one switch (1.5.0, the author: "a quick toggle that allows the user to
            // disable the dynamic fog"). Off by default (his call). "Override fog" overrides it too
            // (CloudFog.Dynamic), so it greys with the override on.
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Fog,
                Group = "FogChanges",
                Bool = Settings.DynamicFog,
                DefaultBool = Settings.Defaults.DynamicFog,
                Enabled = () => FogOn() && !CloudFog.Overridden,
                AfterChange = State("dynamic fog", OnOff(Settings.DynamicFog)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Float = Settings.FogChangesWithWeather,
                DefaultFloat = Settings.Defaults.FogChangesWithWeather,
                Min = 0f, Max = 100f, Step = 5f,
                Enabled = DynamicFogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Float = Settings.MorningFog,
                DefaultFloat = Settings.Defaults.MorningFog,
                Min = 0f, Max = 100f, Step = 5f,
                Enabled = DynamicFogOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Float = Settings.MistAfterRain,
                DefaultFloat = Settings.Defaults.MistAfterRain,
                Min = 0f, Max = 100f, Step = 5f,
                Enabled = DynamicFogOn,
            });

            // 1.5.0 (his pick, 2026-10-04): fog banks over the sea, lakes and rivers, drifting ashore
            // with the wind. Fog of its own, in the layer's look, so under the same heading: never
            // more than the sliders above. 0% for everyone = no banks.
            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Fog,
                Float = Settings.FogOverWater,
                DefaultFloat = Settings.Defaults.FogOverWater,
                Min = 0f, Max = 100f, Step = 5f,
                // "Override fog" overrides every amount of fog, this one too (the author's call).
                Enabled = () => FogOn() && !CloudFog.Overridden,
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

            // 1.5.0, asked for by a player zooming far out with ACME, where the 13.5 km pattern came
            // back about five times across the screen: the map 4x wider, the clouds the same size
            // (Sky.CloudDensityField.LargerScale). A separate switch because the slider above stretches
            // the one map (bigger clouds); its number keeps that meaning (invariant 15). Off: no
            // city's pattern changes. ModController builds the map again when it flips.
            // Not in profiles: it is about how far the player's camera goes, not a look, and every
            // flip lays the city's clouds out anew -- a profile from before 1.5.0 would switch it off.
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Weather,
                Bool = Settings.LargerWeatherPattern,
                DefaultBool = Settings.Defaults.LargerWeatherPattern,
                Profiled = false,
                AfterChange = State("larger weather pattern", OnOff(Settings.LargerWeatherPattern)),
            });

            // 1.5.0: the far deck (Sky.CloudVolume, CloudRaymarch MarchFarDeck) -- under a full overcast
            // the deck carries on to the horizon. A test switch, kept and made always on, then a row of
            // its own (the author: "add it as a real option under Weather"). On its own now: it no longer
            // waits for "Clouds hide the sky by day", so ticking it is enough. A look: in profiles (one
            // saved before has none, and reads it on).
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.Weather,
                Bool = Settings.CloudsToHorizon,
                DefaultBool = Settings.Defaults.CloudsToHorizon,
                AfterChange = State("clouds to the horizon", OnOff(Settings.CloudsToHorizon)),
            });

            // 1.3.0 (asked for: "edit the X and Y values" of the clouds' position, "for advanced
            // settings"): moves THIS city's clouds, all of them together (CloudWind.SetShift),
            // saved with the city (SkyState version 2) -- not a setting: every city's pattern is its
            // own, so no profile or reset carries it. Y is the map's second axis (the world's z).
            // +-10 km: the pattern repeats every Weather pattern size (at most 20 km).
            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Weather,
                Group = "CloudPosition",
                Id = "CloudPositionX",
                Min = -10000f, Max = 10000f, Step = 50f,
                Format = SignedKilometres,
                CityValue = () => (float)CloudWind.ShiftX,
                SetCityValue = x => MoveClouds(x, CloudWind.ShiftZ),
                Profiled = false,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Options = OptionsPage.Weather,
                Id = "CloudPositionY",
                Min = -10000f, Max = 10000f, Step = 50f,
                Format = SignedKilometres,
                CityValue = () => (float)CloudWind.ShiftZ,
                SetCityValue = z => MoveClouds(CloudWind.ShiftX, z),
                Profiled = false,
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

            // How a streak looks (1.1.1). All multipliers on what RainDrops always drew, so 100%
            // and white are the old streaks exactly.
            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Float = Settings.RainStreakLength,
                DefaultFloat = Settings.Defaults.RainStreakLength,
                Min = 25f, Max = 400f, Step = 5f,
                Enabled = RainOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Float = Settings.RainStreakWidth,
                DefaultFloat = Settings.Defaults.RainStreakWidth,
                Min = 25f, Max = 400f, Step = 5f,
                Enabled = RainOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Float = Settings.RainStreakOpacity,
                DefaultFloat = Settings.Defaults.RainStreakOpacity,
                // 200% = alpha 1: the shader does not clamp, and above 1 the blend would
                // DARKEN what is behind a streak (OneMinusSrcAlpha goes negative).
                Min = 0f, Max = 200f, Step = 5f,
                Enabled = RainOn,
            });

            Add(new Row
            {
                Kind = RowKind.Colour,
                Options = OptionsPage.Weather,
                Colour = Settings.RainStreakColor,
                DefaultColour = Settings.Defaults.RainStreakColor,
                Enabled = RainOn,
            });

            // Also slides the curtains' pattern: both read CloudRain.FallOffset.
            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Float = Settings.RainFallSpeed,
                DefaultFloat = Settings.Defaults.RainFallSpeed,
                Min = 25f, Max = 300f, Step = 5f,
                Enabled = RainOn,
            });

            // 1.3.0: the rainbows' strength, 100% = nature ("B is perfect!"), 200% more vivid than
            // nature; 0% switches them off (Sky.Rainbow). They need our rain and the cloud shadows.
            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Weather,
                Float = Settings.Rainbows,
                DefaultFloat = Settings.Defaults.Rainbows,
                Min = 0f, Max = 200f, Step = 5f,
                Enabled = RainbowsPossible,
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

        /// <summary>
        /// F4 -> Light (an advanced tab until 1.3.0, now always shown). Shadows, the brightness
        /// curve, and the two night rows.
        /// </summary>
        private static void BuildLightOptions()
        {
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Light,
                Group = "Shadows",
                Bool = Settings.CloudShadows,
                DefaultBool = Settings.Defaults.CloudShadows,
                AfterChange = State("cloud shadows", OnOff(Settings.CloudShadows)),
            });

            // With a clock (1.5.0), as are the brightness, the density, the light inside and the
            // clouds hiding the sky below.
            Row darkness = new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Light,
                Float = Settings.CloudShadowDarkness,
                DefaultFloat = Settings.Defaults.ShadowDarkness,
                Min = 0f, Max = 95f, Step = 5f,
                Enabled = ShadowsOn,
            };
            AddTimed(darkness, Settings.CloudShadowDarknessSunset, Settings.Defaults.ShadowDarknessSunset,
                Settings.CloudShadowDarknessNight, Settings.Defaults.ShadowDarknessNight, null);

            // Shadow fullness: on the Advanced tab in 1.4.0, back here in 1.5.0.
            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Light,
                Float = Settings.CloudShadowFullness,
                DefaultFloat = Settings.Defaults.ShadowFullness,
                Min = 0.5f, Max = 8f, Step = 0.25f,
                Format = Times2,
                Enabled = ShadowsOn,
            });

            // Cloud brightness (1.5.0): one row with a clock and a cloud button where 1.4.0 had four --
            // "Brightness follows the cloud intensity" (Auto), its "clear sky" and "full overcast", and
            // the fixed brightness; "Clouds dim at full overcast to" (below) left the panel with them.
            // Auto's two ends were always a Clear sky and an Overcast value, blended straight in the
            // cover: the cloud button is that, for every time of day. An older file comes over exactly
            // (OlderLight): Auto on, its two ends at each time; Auto off, its fixed value under a clear
            // sky and that value dimmed by the floor under an overcast one, which is what the floor did.
            // 0% at the bottom (it was 5%): a fixed 5% under a 30% floor made 1.5%.
            Row light = new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Light,
                Group = "Brightness",
                Float = Settings.CloudLight,
                DefaultFloat = Settings.Defaults.Light,
                Min = 0f, Max = 300f, Step = 5f,
            };
            AddTimed(light, Settings.CloudLightSunset, Settings.Defaults.LightSunset,
                Settings.CloudLightNight, Settings.Defaults.LightNight, values => OlderLight(values, false, 2));
            light.Times[0].OlderFile = values => OlderLight(values, false, 0);
            light.Times[1].OlderFile = values => OlderLight(values, false, 1);
            AddOvercast(light, Settings.CloudLightOvercast, Settings.Defaults.LightOvercast,
                Settings.CloudLightSunsetOvercast, Settings.Defaults.LightSunsetOvercast,
                Settings.CloudLightNightOvercast, Settings.Defaults.LightNightOvercast);
            for (int time = 0; time < 3; time++)
            {
                int captured = time;
                light.Overcasts[time].OlderFile = values => OlderLight(values, true, captured);
            }

            // Out of the panel since 1.5.0 (the clouds' overcast is Cloud brightness's now), kept for
            // what it still does -- the fog's sunlight at full overcast -- for the players
            // who lowered it (the author: "keep it since it's an old setting"): in the settings file.
            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Light,
                Float = Settings.MinIllumination,
                DefaultFloat = Settings.Defaults.MinIllumination,
                Min = 30f, Max = 100f, Step = 1f,
                Shown = () => false,
            });

            // 1.5.0's clock began here (the author: "during sunset it's too harsh at 100% and needs to
            // be dropped back down to like 20-25%"), and the cloud button ("Density especially is better
            // to be lower if it's really overcast versus just a few clouds"). Only the clouds you see:
            // their shadows are drawn with the clear sky's Day value (CloudShadowMap), and darken by
            // their own row's clock.
            Row density = new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Light,
                Float = Settings.CloudDensity,
                DefaultFloat = Settings.Defaults.Density,
                Min = 20f, Max = 300f, Step = 5f,
            };
            AddTimed(density, Settings.CloudDensitySunset, Settings.Defaults.DensitySunset,
                Settings.CloudDensityNight, Settings.Defaults.DensityNight, null);
            AddOvercast(density, Settings.CloudDensityOvercast, Settings.Defaults.DensityOvercast,
                Settings.CloudDensitySunsetOvercast, Settings.Defaults.DensitySunsetOvercast,
                Settings.CloudDensityNightOvercast, Settings.Defaults.DensityNightOvercast);

            // The light inside the clouds: two rows, each 0% = the light from before, exactly, 100% =
            // the look chosen. The first is three parts on one slider (it was three rows until they
            // were merged before release -- the author: "merge them"): Sky/LightThrough, thin and
            // middling cloud glows with the sun behind it (200%: light twice as deep), and
            // Sky/CloudShade, the sky light in the shade (200%: the base as lit as the top) and the
            // sunlit ground's light on the undersides (200%: twice the albedo).
            Row inside = new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Light,
                Group = "CloudLight",
                Float = Settings.LightInsideClouds,
                DefaultFloat = Settings.Defaults.LightInsideClouds,
                Min = 0f, Max = 200f, Step = 5f,
            };
            AddTimed(inside, Settings.LightInsideCloudsSunset, Settings.Defaults.LightInsideCloudsSunset,
                Settings.LightInsideCloudsNight, Settings.Defaults.LightInsideCloudsNight, null);
            AddOvercast(inside, Settings.LightInsideCloudsOvercast, Settings.Defaults.LightInsideCloudsOvercast,
                Settings.LightInsideCloudsSunsetOvercast, Settings.Defaults.LightInsideCloudsSunsetOvercast,
                Settings.LightInsideCloudsNightOvercast, Settings.Defaults.LightInsideCloudsNightOvercast);

            // Sky/SunsetLight: the clouds keep the low sun's light, redder, until it has set for them
            // (0%: the game's sun, as before; 200%: as if they stood twice as high -- it lasts longer).
            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Light,
                Float = Settings.SunsetLight,
                DefaultFloat = Settings.Defaults.SunsetLight,
                Min = 0f, Max = 200f, Step = 5f,
            });

            // "Clouds hide the sky": by day (1.5.0, asked for by a player with a daytime cubemap: its sky,
            // sun included, showed through a full overcast, where the far clouds fade into whatever is
            // behind them; 0% for everyone = the clouds as before, and not switched on by itself for a
            // sky that is not the game's) and at night (the stars, since 1.0: 100%), one row with a
            // clock since 1.5.0 (CloudVolume.ApplyNight). CloudNightOpacity keeps its name: it already
            // meant the night's. Sunset: an older file's Day value, which is how its twilight began.
            Row hide = new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Light,
                Group = "DayAndNight",
                Float = Settings.CloudDayOpacity,
                DefaultFloat = Settings.Defaults.DayOpacity,
                Min = 0f, Max = 100f, Step = 5f,
            };
            // A file without the night's element (none since release has lacked it) means its default,
            // as it always did -- not the Day value.
            AddTimed(hide, Settings.CloudSunsetOpacity, Settings.Defaults.SunsetOpacity,
                Settings.CloudNightOpacity, Settings.Defaults.NightOpacity,
                values => SettingsXmlFormat.FormatNumber(Settings.Defaults.NightOpacity * 100f));
            AddOvercast(hide, Settings.CloudDayOpacityOvercast, Settings.Defaults.DayOpacityOvercast,
                Settings.CloudSunsetOpacityOvercast, Settings.Defaults.SunsetOpacityOvercast,
                Settings.CloudNightOpacityOvercast, Settings.Defaults.NightOpacityOvercast);

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Light,
                Float = Settings.NightGlow,
                DefaultFloat = Settings.Defaults.NightGlow,
                Min = 0f, Max = 300f, Step = 5f,
            });
        }

        /// <summary>
        /// The game's options page, before its own rows: what the clouds cost, and how they are
        /// drawn. The in-game panel's Rendering tab until 1.3.0; the quality preset moved first
        /// ("so users will notice it first without having to toggle advanced options"), then the
        /// rest, and the tab went ("its settings are now in the pause menu").
        /// </summary>
        private static void BuildRenderingOptions()
        {
            // The first row of the options page. The three rows it sets follow it, shown while it
            // reads Custom ("It's not understandable what is Custom without them").
            Add(new Row
            {
                Kind = RowKind.Choice,
                Options = OptionsPage.General,
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
                Options = OptionsPage.General,
                OptionsPageShown = CustomPreset,
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
                Options = OptionsPage.General,
                OptionsPageShown = CustomPreset,
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
                Options = OptionsPage.General,
                OptionsPageShown = CustomPreset,
                Int = Settings.ShadowMapRate,
                DefaultInt = Settings.Defaults.ShadowMapRate,
                ChoiceUnit = "Unit.Hz",
                ChoiceValues = new[] { 10, 20, 30 },
                Profiled = false,
                Enabled = ShadowsOn,
                AfterChange = MarkCustomPreset,
            });

            // The raymarched clouds are the mod: the switch that turned them into flat
            // billboards, and the billboards' two sliders, went in 1.1.0 ("it defeats the
            // purpose of the mod"). Under its own heading, after the preset's rows.
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.General,
                Group = "Drawing",
                Bool = Settings.CloudDepthOcclusion,
                DefaultBool = Settings.Defaults.DepthOcclusion,
                Profiled = false,
                AfterChange = State("depth occlusion", OnOff(Settings.CloudDepthOcclusion)),
            });

            // Smoother clouds (1.5.0, Sky/CloudTemporal): the machine's choice like the rows above, so
            // never in a profile; off by default, and outside the quality preset.
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.General,
                Bool = Settings.CloudTemporal,
                DefaultBool = Settings.Defaults.Temporal,
                Profiled = false,
                AfterChange = State("smoother clouds", OnOff(Settings.CloudTemporal)),
            });
        }

        /// <summary>
        /// Advanced tab Halos. Street and building lamps are batched at EVERY distance, so this
        /// is one replacement shader doing the whole night; the near rows are a per-light
        /// distance blend inside it.
        /// </summary>
        private static void BuildHaloOptions()
        {
            // The one switch (1.1.0: "just settle for one checkbox"). Its element name stays
            // HaloEnabled, so a player who opted in keeps the halos on. The second switch
            // (HaloReplaceShader: the game's shader with only its fog overridden) and the
            // "Fog amount" slider (HaloFogAmount) are gone; the halos follow the world's fog.
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
                Kind = RowKind.Value,
                Options = OptionsPage.Halos,
                Float = Settings.HaloTightness,
                DefaultFloat = Settings.Defaults.HaloTightness,
                Min = 0.5f, Max = 6f, Step = 0.05f,
                Format = Times2,
                Enabled = HalosOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Float = Settings.HaloBrightness,
                DefaultFloat = Settings.Defaults.HaloBrightness,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = HalosOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Float = Settings.HaloRadius,
                DefaultFloat = Settings.Defaults.HaloRadius,
                Min = 10f, Max = 150f, Step = 5f,
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
                Enabled = HalosOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Float = Settings.HaloNearLightTightness,
                DefaultFloat = Settings.Defaults.HaloNearTightness,
                Min = 50f, Max = 300f, Step = 5f,
                Enabled = HalosOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Float = Settings.HaloNearLightBrightness,
                DefaultFloat = Settings.Defaults.HaloNearBrightness,
                Min = 0f, Max = 200f, Step = 1f,
                Enabled = HalosOn,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Options = OptionsPage.Halos,
                Float = Settings.HaloNearLightRadius,
                DefaultFloat = Settings.Defaults.HaloNearRadius,
                Min = 10f, Max = 200f, Step = 5f,
                Enabled = HalosOn,
            });

            // The "Vehicles and other moving lights" group -- "Vehicle light halos" and "Adjust
            // dynamic lights" with its two sliders -- was here until 1.1.1: removed, see the note
            // in Settings.cs beside HaloNearLightRadius.
        }

        /// <summary>The game's options page, and nothing else: the mod itself, and the reset button at the very bottom.</summary>
        private static void BuildGeneralOptions()
        {
            // First: whoever cannot read the rest must find this one. A change rebuilds the
            // in-game panel and this page (OptionsUI.RebuildForLanguage), both in the new language.
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
                    OptionsUI.RebuildForLanguage();
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

            // The panel's look (1.5.0, ModsLibCS1). Stored by value, append only; listed with the
            // default (Frosted glass) first and the old look last. A change rebuilds the panel where
            // it stands and the HUD button, which wears the skin too.
            Add(new Row
            {
                Kind = RowKind.Choice,
                Options = OptionsPage.General,
                Int = Settings.PanelSkin,
                DefaultInt = Settings.Defaults.PanelSkin,
                ChoiceKeys = new[] { "PanelSkin.Frosted", "PanelSkin.SeeThrough", "PanelSkin.GameBlue", "PanelSkin.Vanilla" },
                ChoiceValues = new[]
                {
                    (int)ModsLibCS1.UI.Skin.Frosted, (int)ModsLibCS1.UI.Skin.SeeThrough,
                    (int)ModsLibCS1.UI.Skin.GameBlue, (int)ModsLibCS1.UI.Skin.Vanilla,
                },
                Profiled = false,
                AfterChange = () =>
                {
                    Log.Msg("setting: panel skin = " + PanelLook.Skin);
                    ModController.RebuildPanel();
                    ModController.RefreshButton();
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
        /// reset and the settings file walk this list and must reach them too.
        /// </summary>
        private static void BuildHidden()
        {
            // The profile picked at the top of the in-game panel (Profiles, 1.3.0), empty for
            // none. The panel's profile bar is its only UI. A hand edit of the element goes
            // through the AfterChange: an existing profile is picked, a new name makes one of the
            // sky as it is, empty lets go. Never carried by a profile: it names one.
            Add(new Row
            {
                Kind = RowKind.Text,
                Text = Settings.CurrentProfile,
                DefaultText = Settings.Defaults.Profile,
                HasNote = true,
                Profiled = false,
                AfterChange = Profiles.OnSelectionEdited,
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

            // 1.5.0: the panel's open group headings (advanced options on). Its headings are its UI;
            // the panel reads the value on every refresh, so a hand edit or a reset shows at once.
            Add(new Row
            {
                Kind = RowKind.Text,
                Text = Settings.PanelOpenGroups,
                DefaultText = Settings.Defaults.PanelOpenGroups,
                Profiled = false,
            });

            // 1.5.0: the rows whose clock is open in the panel (their Day, Sunset and Night lines
            // shown). The clocks are its UI; read on every refresh, like the open headings.
            Add(new Row
            {
                Kind = RowKind.Text,
                Text = Settings.PanelOpenClocks,
                DefaultText = Settings.Defaults.PanelOpenClocks,
                Profiled = false,
            });

            // The same for the rows whose cloud button is open (1.5.0): how the panel looks, not the sky.
            Add(new Row
            {
                Kind = RowKind.Text,
                Text = Settings.PanelOpenWeather,
                DefaultText = Settings.Defaults.PanelOpenWeather,
                Profiled = false,
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
