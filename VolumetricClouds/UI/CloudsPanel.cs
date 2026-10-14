using System.Collections.Generic;
using ColossalFramework.UI;
using ModsLibCS1.UI;
using UnityEngine;

namespace VolumetricClouds.UI
{
    /// <summary>
    /// The in-game panel: what you set while looking at the sky.
    /// </summary>
    /// <remarks>
    /// This is the ONLY place the sky is set (1.1.0). By default it has four tabs:
    ///   Now    -- every override in the mod, plus the two status lines. Photo mode.
    ///   Clouds -- the shape of the sky.
    ///   Fog    -- the fog, which is a cloud layer lying on the ground.
    ///   Light  -- shadows and brightness (an advanced tab until 1.3.0).
    /// "Show advanced options in the in-game panel" (on the game's options page) adds two
    /// more: Weather and Halos -- the rows whose Row.Options names them. A row that is already
    /// on one of the basic tabs is not repeated on an advanced one. (A third, Rendering, went in
    /// 1.3.0: its rows are on the options page.) The mod itself and what the computer can
    /// afford (quality, language, key, button, diagnostics, the resets) are on the options page
    /// (<see cref="OptionsUI"/>) alone: Row.Options == General.
    ///
    /// Both UIs are drawn from <see cref="SettingsCatalog"/>, so where a row lives is a
    /// one-word edit rather than a second copy of the row.
    ///
    /// Under the title, on every tab: the profile bar (1.3.0, <see cref="ProfileBar"/>).
    ///
    /// Every control comes from ModsLibCS1 (1.5.0), in the skin the "Panel look" row picks
    /// (<see cref="PanelLook"/>); Vanilla is the panel as it was before. Every look grows with the
    /// tab shown. (A fixed height with the settings scrolling inside it, ModsLibCS1's ScrollView,
    /// was tried 2026-10-13 and undone the same day, the author: "it was better before".)
    /// </remarks>
    public class CloudsPanel : UIPanel
    {
        private const float BasicWidth = 600f;
        private const float AdvancedWidth = 680f;   // was for seven tabs; Weather and Halos are laid out at it

        /// <summary>
        /// The new looks' width (1.5.0), basic or advanced: the icon tabs need no room, the names keep
        /// their full size and the sliders give way (the author: "decrease the width ... the sliders
        /// themselves can be smaller", then "reduce the window width just a bit more").
        /// </summary>
        private const float NewLooksWidth = 570f;
        private const float PanelHeight = 408f;
        private const float TitleBarHeight = 40f;
        private const float BarHeight = ProfileBar.Height;
        private const float TabHeight = 28f;

        /// <summary>The new looks' icon tabs (1.5.0): taller, for icons the author asked to be bigger.</summary>
        private const float IconTabHeight = 36f;
        private const float Margin = 14f;

        private const float HeadingHeight = 24f;

        /// <summary>Room above and below a folding heading's strip (advanced options on; the author: "some margin").</summary>
        private const float FoldMarginTop = 8f;
        private const float FoldMarginBottom = 4f;

        /// <summary>The reset button's column at the right end of each row, gap included.</summary>
        private const float ResetColumn = 28f;

        /// <summary>
        /// The clock's column (1.5.0), left of the reset's, gap included: on a page with a row that has
        /// a clock, every row keeps it free, so the sliders end in one line.
        /// </summary>
        private const float ClockColumn = 26f;

        /// <summary>How far a clock's Day / Sunset / Night line's name stands in, its time's icon before it.</summary>
        private const float LineIndent = 22f;
        private const float LineIconSize = 14f;

        private const string ClockSprite = "icon-clock";
        private static readonly string[] LineSprites = { "icon-sun", "icon-sunset", "icon-moon" };
        private static readonly string[] LineKeys = { "Time.Day", "Time.Sunset", "Time.Night" };

        /// <summary>The time's icon on its line: a warm sun, an orange sunset, a pale moon.</summary>
        private static readonly Color32[] LineColours =
        {
            new Color32(255, 211, 107, 255), new Color32(255, 154, 90, 255), new Color32(195, 205, 255, 255),
        };

        /// <summary>
        /// The cloud button's column (1.5.0), left of the clock's: on a page with a row that has one, every
        /// row keeps it free, as the clock's.
        /// </summary>
        private const float WeatherColumn = 26f;

        private const string WeatherSprite = "icon-cloud";
        private static readonly string[] WeatherLineSprites = { "icon-cloud-sun", "icon-cloudy" };
        private static readonly string[] WeatherLineKeys = { "Weather.Clear", "Weather.Overcast" };

        /// <summary>The weather's icon on its line: a pale sky blue, a cloud grey.</summary>
        private static readonly Color32[] WeatherLineColours =
        {
            new Color32(159, 220, 255, 255), new Color32(207, 215, 224, 255),
        };

        /// <summary>
        /// What a row with a clock shows under its own line (1.5.0): nothing; its Day / Sunset / Night lines
        /// (the clock open); its Clear sky / Overcast lines (the cloud button open); the table, Clear sky and
        /// Overcast across, Day / Sunset / Night down -- both open, or one open while the other's values
        /// differ (the author: opening one button must always show sliders).
        /// </summary>
        private const int ModeClosed = 0;
        private const int ModeTimes = 1;
        private const int ModeWeathers = 2;
        private const int ModeTable = 3;

        /// <summary>The table's names column (the time's icon and name) and its heading line's height.</summary>
        private const float TableNameWidth = 132f;
        private const float TableHeadHeight = 24f;

        /// <summary>A table cell's readout column, beside its slider.</summary>
        private const float CellValueWidth = 44f;

        /// <summary>The shortest a slider gets in the new looks, where the names take what they need.</summary>
        private const float MinSlider = 70f;
        private const float NoteHeight = 32f;

        /// <summary>
        /// The new looks' readout column, per page: as wide as the page's widest readout plus this
        /// (a readout can run a little wider than the ones measured), never narrower than
        /// <see cref="MinValueWidth"/>, never wider than the skin's own. Narrow readouts ("47%") left
        /// a wide empty column after every slider (the author: "a bit empty space around the sliders").
        /// </summary>
        private const float ReadoutRoom = 8f;
        private const float MinValueWidth = 40f;

        /// <summary>The tabs' height in this panel's look: Vanilla's as it always was, the new looks' taller.</summary>
        private float _tabHeight = TabHeight;

        private float MinContentHeight
        {
            get { return PanelHeight - TitleBarHeight - _tabHeight - 2f * Margin; }
        }

        private static readonly PanelPage[] BasicTabs =
        {
            PanelPage.Now, PanelPage.Clouds, PanelPage.DistantClouds, PanelPage.Fog, PanelPage.Light,
        };

        private static readonly OptionsPage[] AdvancedTabs =
        {
            OptionsPage.Weather, OptionsPage.Halos,
        };

        /// <summary>One tab: a basic page, or an advanced one.</summary>
        private struct Tab
        {
            public string Name;
            public PanelPage Panel;
            public OptionsPage Options;

            /// <summary>
            /// Its icon in the new looks (1.5.0): a Lucide name the library draws (ModsLibCS1's
            /// THIRD-PARTY-NOTICES.md). Vanilla keeps the names.
            /// </summary>
            public string Icon
            {
                get
                {
                    switch (Panel)
                    {
                        case PanelPage.Now: return "clock";
                        case PanelPage.Clouds: return "cloud";
                        case PanelPage.DistantClouds: return "cloudy";
                        case PanelPage.Fog: return "cloud-fog";
                        case PanelPage.Light: return "sun";
                    }

                    switch (Options)
                    {
                        case OptionsPage.Weather: return "cloud-sun-rain";
                        case OptionsPage.Halos: return "lightbulb";
                    }

                    return null;
                }
            }

            public bool Shows(Row row)
            {
                if (Panel != PanelPage.None)
                    return row.Panel == Panel;

                // A row that already has a home on a basic tab stays there (none since 1.3.0).
                return row.Options == Options && row.Panel == PanelPage.None;
            }
        }

        /// <summary>Where to open, when replacing a panel that was already on screen. Null = centred.</summary>
        public Vector3? InitialPosition;

        private Builder _ui;
        private Frame _frame;
        private TabStrip _tabs;
        private readonly List<string> _tabNames = new List<string>();
        private readonly List<UIPanel> _pages = new List<UIPanel>();
        private readonly List<float> _contentHeights = new List<float>();
        private readonly List<Control> _controls = new List<Control>();
        private ProfileBar _bar;

        /// <summary>Each page's stretches, top to bottom (<see cref="Item"/>), and its tab for the log.</summary>
        private readonly List<List<Item>> _layout = new List<List<Item>>();
        private readonly List<string> _tabIds = new List<string>();
        private int _selected;

        private const float RefreshInterval = 0.25f;
        private float _nextRefreshTime;
        private float _width = BasicWidth;

        /// <summary>
        /// Guards the refresh against its own callbacks: writing a UISlider's value fires
        /// eventValueChanged, which would write the setting straight back -- quantised to the
        /// slider's step, and with every AfterChange fired for a change nobody made.
        /// </summary>
        private bool _refreshing;

        /// <summary>The profile pick the reset buttons' tooltips were written for.</summary>
        private string _resetTipsFor;

        private static CloudsPanel _instance;

        /// <summary>Re-reads every control, if the panel exists. Safe to call from anywhere.</summary>
        public static void RefreshOpenPanel()
        {
            if (_instance != null)
                _instance.RefreshValues();
        }

        /// <summary>One built row: whatever components it made, so they can be refreshed and greyed.</summary>
        private class Control
        {
            public Row Source;
            public SliderRow Slider;
            public ChoiceRow Choice;
            public UICheckBox Check;
            public UIButton Reset;
            public KeyRow Key;
            public string KeyShown;
            public UILabel Status;
            public ColourRow Colour;
            public readonly List<UIComponent> Parts = new List<UIComponent>();
            public bool Enabled = true;

            /// <summary>Its stretch of the page, and the page's index.</summary>
            public Item Item;
            public int Page;

            /// <summary>
            /// A row with a clock's own line (1.5.0): the clock, and the note ("Changes with the time
            /// of day") and value in use now shown instead of its slider or colour while its three times
            /// differ or the clock is open. <see cref="ClockMode"/>: what was last shown.
            /// </summary>
            public UIButton Clock;
            public UISprite ClockIcon;
            public UILabel ClockNote;
            public UILabel ClockNow;
            public int ClockMode = -1;

            /// <summary>
            /// A Day / Sunset / Night line, a Clear sky / Overcast one (<see cref="LineAxis"/> 1), or a table
            /// line: the row whose buttons show it, which time (or weather), its name and icon.
            /// </summary>
            public Row LineOf;
            public int LineTime;
            public int LineAxis;
            public UILabel LineName;
            public string LineText;
            public UISprite LineIcon;

            /// <summary>
            /// The values its slider or colour stands for (1.5.0), when more than its own: the own line of a
            /// row with a clock (all of them), a Day line (its clear and overcast), a Clear sky line (its three
            /// times). Its reset puts all of them back. Null: its own row's only.
            /// </summary>
            public Row[] Targets;

            /// <summary>The row's cloud button (1.5.0), beside its clock.</summary>
            public UIButton Weather;
            public UISprite WeatherIcon;

            /// <summary>A table line (1.5.0): its two values, Clear sky and Overcast, and their cells.</summary>
            public Row[] TableCells;
            public SliderRow[] CellSliders;
            public ColourCell[] CellColours;

            /// <summary>The table's heading line: its two column names and their texts.</summary>
            public UILabel[] HeadNames;
            public string[] HeadTexts;

            public bool IsClockTop
            {
                get { return Clock != null; }
            }
        }

        /// <summary>
        /// One stretch of a page: a group heading, or a row with its note -- every component the
        /// builders put on the page for it, so a row can be hidden and everything under it moved
        /// up as a whole (Row.Shown, 1.3.0). Rows sit at fixed heights on their page; nothing
        /// lays them out but <see cref="Relayout"/>.
        /// </summary>
        private class Item
        {
            public Control Control;   // null for a heading
            public readonly List<UIComponent> Components = new List<UIComponent>();
            public readonly List<UIComponent> HiddenByUs = new List<UIComponent>();
            public float Y;
            public float Height;
            public bool Shown = true;

            /// <summary>The group (Row.Group) this heading starts, or this row sits under. Null above the first heading.</summary>
            public string Group;

            /// <summary>A heading's own controls; folding (advanced options on) when it has an arrow.</summary>
            public HeadingRow Heading;

            /// <summary>
            /// A line under a row with a clock (1.5.0): that row, and the <see cref="Mode"/> it is shown in
            /// (ModeTimes, ModeWeathers, ModeTable).
            /// </summary>
            public Row ClockOf;
            public int Mode;
        }

        /// <summary>
        /// The groups left open (Settings.PanelOpenGroups, 1.5.0), as last read; every other one is
        /// folded away. Only while the advanced options are on -- the basic panel is short enough,
        /// and its headings stay plain.
        /// </summary>
        private readonly List<string> _open = new List<string>();
        private string _openRead;
        private bool _advanced;

        /// <summary>The rows whose clock is open (Settings.PanelOpenClocks, 1.5.0), as last read.</summary>
        private readonly List<string> _openClocks = new List<string>();
        private string _openClocksRead;

        /// <summary>The rows whose cloud button is open (Settings.PanelOpenWeather, 1.5.0), as last read.</summary>
        private readonly List<string> _openWeather = new List<string>();
        private string _openWeatherRead;

        /// <summary>
        /// What each row with a clock shows under it (ModeClosed...), decided when a button is clicked or
        /// the open ones are read, and kept: a value moved in the table never folds it back into lines
        /// under the player's pointer. Lines that stop standing for one value (a profile picked) become
        /// the table (<see cref="CheckModes"/>).
        /// </summary>
        private readonly Dictionary<string, int> _modes = new Dictionary<string, int>();

        public override void Start()
        {
            base.Start();

            _instance = this;

            bool advanced = Settings.ShowAdvancedInPanel != null && Settings.ShowAdvancedInPanel.value;
            _advanced = advanced;
            ReadOpen();
            ReadOpenClocks();
            ReadOpenWeather();

            _ui = PanelLook.NewBuilder();
            _tabHeight = _ui.Skin == Skin.Vanilla ? TabHeight : IconTabHeight;

            // Vanilla as it always was. The new looks: one width, basic or advanced (NewLooksWidth),
            // the names keeping their full size (BuildPage).
            _width = _ui.Skin != Skin.Vanilla ? NewLooksWidth : advanced ? AdvancedWidth : BasicWidth;

            size = new Vector2(_width, PanelHeight);
            canFocus = true;
            isInteractive = true;

            // Background, title, close button; dragging the title bar moves the whole panel.
            // Only the title bar: the profile bar under it is controls.
            _frame = _ui.AddFrame(this, Mod.DisplayName, Hide);
            BuildProfileBar();

            // Every setting row ends in its reset button (1.5.0): room for it on every row.
            _ui.ResetColumn = ResetColumn;

            List<Tab> tabs = Tabs(advanced);
            List<string> icons = new List<string>();
            foreach (Tab tab in tabs)
            {
                _tabNames.Add(tab.Name);
                icons.Add(tab.Icon);
            }

            // The new looks draw each tab as its icon, its name the tooltip and, for the open one,
            // beside the panel's title (SelectPage). Vanilla: named tabs, as before.
            _tabs = _ui.AddTabStrip(this, new Vector3(Margin, TitleBarHeight + BarHeight + 2f), _width - 2f * Margin,
                _tabHeight, _tabNames, SelectPage, icons);
            FitTabs();

            foreach (Tab tab in tabs)
            {
                _tabIds.Add(tab.Panel != PanelPage.None ? tab.Panel.ToString() : tab.Options.ToString());
                _contentHeights.Add(BuildPage(AddPage(), tab));
            }

            // Every row is built where it would be if all were shown; the ones hidden now go, and
            // the pages close up, before the first one is shown.
            RefreshShown();
            SelectPage(0);

            if (InitialPosition.HasValue)
                relativePosition = InitialPosition.Value;
            else
                CenterOnScreen();

            KeepOnScreen();

            eventVisibilityChanged += (component, visible) =>
            {
                if (visible)
                    RefreshValues();
            };

            Log.Msg("panel built: " + _controls.Count + " controls over " + _pages.Count + " tabs (" +
                    (advanced ? "advanced" : "basic") + ", " + _ui.Skin + ")");
        }

        /// <summary>
        /// A tab name wider than its tab is drawn smaller until it fits (1.5.0: five tabs, and
        /// "DISTANT CLOUDS" / "NUAGES LOINTAINS" in capitals): a button clips what runs past its edge.
        /// </summary>
        private void FitTabs()
        {
            foreach (UIButton tab in _tabs.Tabs)
            {
                float room = tab.width - 8f;
                float wide = _ui.Measure(tab.text, tab.textScale) + tab.characterSpacing * tab.text.Length;
                if (wide > room && wide > 0f)
                {
                    float scale = tab.textScale * room / wide;
                    Log.Msg("panel: tab '" + tab.text + "' drawn at " + (100f * scale / tab.textScale).ToString("F0") +
                            "% to fit its " + tab.width.ToString("F0") + " px");
                    tab.textScale = scale;
                }
            }
        }

        private static List<Tab> Tabs(bool advanced)
        {
            List<Tab> tabs = new List<Tab>();

            foreach (PanelPage page in BasicTabs)
                tabs.Add(new Tab { Name = SettingsCatalog.TabName(page), Panel = page });

            if (advanced)
            {
                foreach (OptionsPage page in AdvancedTabs)
                    tabs.Add(new Tab { Name = SettingsCatalog.TabName(page), Options = page });
            }

            return tabs;
        }

        /// <summary>
        /// Keeps the status lines, the greyed-out rows and the values current. All three depend
        /// on things the panel does not own: the weather, checkboxes on another tab, and the
        /// options page, which can change any of these settings while this panel stays open
        /// behind it (so no visibility event ever says to look again).
        /// </summary>
        public override void Update()
        {
            base.Update();

            if (!isVisible || Time.time < _nextRefreshTime)
                return;

            _nextRefreshTime = Time.time + RefreshInterval;

            foreach (Control control in _controls)
            {
                if (control.Status != null && control.Source.StatusText != null)
                    control.Status.text = control.Source.StatusText();
            }

            RefreshValues();
        }

        /// <summary>
        /// Re-reads every control from its setting. Cheap enough to run four times a second:
        /// a comparison per control, and a write only where something really changed.
        /// </summary>
        public void RefreshValues()
        {
            _refreshing = true;
            int touched = 0;

            // The reset buttons go back to the picked profile's values, or the defaults without
            // one: their tooltips say which, again whenever the pick changes.
            string pick = Profiles.Selected + "|" + Profiles.State;
            bool retip = pick != _resetTipsFor;
            _resetTipsFor = pick;

            try
            {
                foreach (Control control in _controls)
                {
                    Row row = control.Source;

                    // A row with a clock's own line (1.5.0): its own rules.
                    if (control.IsClockTop)
                    {
                        touched += RefreshClockTop(control, retip);
                        continue;
                    }

                    // The table (1.5.0): its heading, and each line's two cells.
                    if (control.HeadNames != null)
                    {
                        RefreshTableHead(control);
                        continue;
                    }

                    if (control.TableCells != null)
                    {
                        touched += RefreshTableLine(control, retip);
                        continue;
                    }

                    // A Day / Sunset / Night line: lit by its time's share now, then as any row.
                    if (control.LineOf != null)
                        RefreshLine(control);

                    // Both set quietly: the row's own callback never runs for a refresh.
                    if (control.Slider != null)
                    {
                        float display = Mathf.Clamp(row.Display, row.Min, row.Max);
                        if (!Mathf.Approximately(control.Slider.Slider.value, display))
                        {
                            control.Slider.SetValue(display);
                            touched++;
                        }
                    }

                    if (control.Choice != null && control.Choice.Index != row.ChoiceIndex)
                    {
                        control.Choice.SetIndex(row.ChoiceIndex);
                        touched++;
                    }

                    if (control.Check != null && row.Bool != null && control.Check.isChecked != row.Bool.value)
                    {
                        control.Check.isChecked = row.Bool.value;
                        touched++;
                    }

                    // It keeps its own guard: its swatch fires events when set.
                    if (control.Colour != null)
                        control.Colour.Refresh();

                    // A key reset by its button, the options page or a reset all.
                    if (control.Key != null && row.Key != null)
                    {
                        string shown = row.Key.ToLocalizedString("KEYNAME");
                        if (shown != control.KeyShown && !control.Key.Button.hasFocus)
                        {
                            control.KeyShown = shown;
                            control.Key.Button.text = shown;
                        }
                    }

                    // Lit while the row is off what it would go back to (and not greyed out). A line standing
                    // for several values: while any of them is.
                    if (control.Reset != null)
                    {
                        bool atReset = control.Targets != null ? SettingsCatalog.AtResetValue(control.Targets) : row.IsAtResetValue;
                        Builder.SetResetEnabled(control.Reset, row.IsEnabled && !atReset);
                        if (retip)
                            control.Reset.tooltip = row.ResetTooltip;
                    }

                    if (retip && control.Colour != null)
                        control.Colour.ResetTooltip = row.ResetTooltip;
                }
            }
            finally
            {
                _refreshing = false;
            }

            // Its own guard too, and it never throws.
            if (_bar != null)
                _bar.Refresh();

            RefreshEnabled();
            RefreshShown();

            if (touched > 0 && Log.Detailed)
                Log.Detail("panel: refreshed " + _controls.Count + " controls, " + touched + " had changed");
        }

        /// <summary>
        /// Hides the rows Row.IsShownInPanel says to -- Row.Shown (1.3.0), and since 1.4.0 a basic
        /// tab's greyed-out rows while the advanced options are off -- and closes their page up under
        /// them, or opens it again. Runs with every refresh, but a page is laid out again only when
        /// one of its rows has actually changed.
        /// </summary>
        private void RefreshShown()
        {
            // The open clocks changed under the panel (a reset, a hand edit): read again; the walk below
            // then shows or hides their lines.
            if (Settings.PanelOpenClocks != null && Settings.PanelOpenClocks.value != _openClocksRead)
                ReadOpenClocks();
            if (Settings.PanelOpenWeather != null && Settings.PanelOpenWeather.value != _openWeatherRead)
                ReadOpenWeather();

            // Lines that no longer stand for one value each become the table (the walk below lays it out).
            CheckModes();

            // The open groups changed under the panel: a reset, a hand edit of the file.
            if (Settings.PanelOpenGroups != null && Settings.PanelOpenGroups.value != _openRead)
            {
                ReadOpen();
                foreach (List<Item> items in _layout)
                {
                    foreach (Item item in items)
                    {
                        if (item.Heading != null)
                            item.Heading.SetOpen(!IsFolded(item.Group));
                    }
                }
            }

            for (int p = 0; p < _layout.Count; p++)
            {
                foreach (Item item in _layout[p])
                {
                    if (item.Control != null && RowShown(item) != item.Shown)
                    {
                        Relayout(p);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// A row: shown by its own rules (Row.IsShownInPanel), and its group not folded away. A line under
        /// a row with a clock: and that row showing its kind of lines now (1.5.0, <see cref="ModeOf"/>).
        /// </summary>
        private bool RowShown(Item item)
        {
            return item.Control.Source.IsShownInPanel && !IsFolded(item.Group) &&
                   (item.ClockOf == null || ModeOf(item.ClockOf) == item.Mode);
        }

        private bool IsClockOpen(Row row)
        {
            return _openClocks.Contains(row.Name);
        }

        private bool IsWeatherOpen(Row row)
        {
            return row.HasWeather && _openWeather.Contains(row.Name);
        }

        private void ReadOpenClocks()
        {
            _openClocksRead = Settings.PanelOpenClocks != null ? Settings.PanelOpenClocks.value : string.Empty;
            ReadNames(_openClocksRead, _openClocks);
            _modes.Clear();
        }

        private void ReadOpenWeather()
        {
            _openWeatherRead = Settings.PanelOpenWeather != null ? Settings.PanelOpenWeather.value : string.Empty;
            ReadNames(_openWeatherRead, _openWeather);
            _modes.Clear();
        }

        private static void ReadNames(string text, List<string> names)
        {
            names.Clear();
            if (string.IsNullOrEmpty(text))
                return;

            foreach (string name in text.Split(','))
            {
                string row = name.Trim();
                if (row.Length > 0 && !names.Contains(row))
                    names.Add(row);
            }
        }

        /// <summary>What a row with a clock shows under it now: as decided last (<see cref="_modes"/>), or decided now.</summary>
        private int ModeOf(Row row)
        {
            int mode;
            if (!_modes.TryGetValue(row.Name, out mode))
            {
                mode = DecideMode(row);
                _modes[row.Name] = mode;
            }

            return mode;
        }

        /// <summary>
        /// Both buttons closed: nothing. Both open: the table. One open: its lines, where each of them stands
        /// for one value -- the clock's while each time is the same in both weathers, the cloud's while each
        /// weather is the same at every hour; otherwise the table, so every value has a slider of its own.
        /// </summary>
        private int DecideMode(Row row)
        {
            bool clock = IsClockOpen(row);
            bool weather = IsWeatherOpen(row);
            if (!clock && !weather)
                return ModeClosed;
            if (clock && weather)
                return ModeTable;
            if (clock)
                return SettingsCatalog.SameInEveryWeather(row) ? ModeTimes : ModeTable;

            return SettingsCatalog.SameAtEveryHour(row) ? ModeWeathers : ModeTable;
        }

        /// <summary>
        /// Lines that no longer stand for one value each (a profile picked, a reset, a hand edit of the
        /// file under them) become the table, never the other way round (<see cref="_modes"/>).
        /// </summary>
        private void CheckModes()
        {
            foreach (Control control in _controls)
            {
                if (!control.IsClockTop)
                    continue;

                Row row = control.Source;
                int mode;
                if (!_modes.TryGetValue(row.Name, out mode))
                    continue;

                if ((mode == ModeTimes && !SettingsCatalog.SameInEveryWeather(row)) ||
                    (mode == ModeWeathers && !SettingsCatalog.SameAtEveryHour(row)))
                {
                    _modes[row.Name] = ModeTable;
                    Log.Msg("panel: " + row.Name + "'s values now differ under its open button: shown as the table");
                }
            }
        }

        /// <summary>
        /// A cloud button clicked (1.5.0): its Clear sky and Overcast values shown or hidden, kept in the
        /// settings file as the clocks are, the page closed up. No value changes.
        /// </summary>
        private void ToggleWeather(Control control)
        {
            Row row = control.Source;
            bool open = !IsWeatherOpen(row);
            if (open)
                _openWeather.Add(row.Name);
            else
                _openWeather.Remove(row.Name);

            _openWeatherRead = string.Join(",", _openWeather.ToArray());
            if (Settings.PanelOpenWeather != null)
                Settings.PanelOpenWeather.value = _openWeatherRead;
            SettingsXml.SaveNow();

            _modes.Remove(row.Name);
            Log.Msg("panel: " + row.Name + "'s cloud button " + (open ? "opened" : "closed") + " (open now: " +
                    (_openWeatherRead.Length > 0 ? _openWeatherRead : "none") + "; " + Sky.Overcast.Describe() +
                    "; shown as " + ModeName(ModeOf(row)) + ")");
            Relayout(control.Page);
        }

        private static string ModeName(int mode)
        {
            switch (mode)
            {
                case ModeTimes: return "day, sunset and night";
                case ModeWeathers: return "clear sky and overcast";
                case ModeTable: return "the table";
                default: return "one line";
            }
        }

        /// <summary>
        /// A clock clicked (1.5.0): its Day / Sunset / Night lines shown or hidden, kept in the settings
        /// file so the panel opens them again next time (the author's call), the page closed up. No
        /// value changes.
        /// </summary>
        private void ToggleClock(Control control)
        {
            Row row = control.Source;
            bool open = !IsClockOpen(row);
            if (open)
                _openClocks.Add(row.Name);
            else
                _openClocks.Remove(row.Name);

            _openClocksRead = string.Join(",", _openClocks.ToArray());
            if (Settings.PanelOpenClocks != null)
                Settings.PanelOpenClocks.value = _openClocksRead;
            SettingsXml.SaveNow();

            _modes.Remove(row.Name);
            Log.Msg("panel: " + row.Name + "'s clock " + (open ? "opened" : "closed") + " (open now: " +
                    (_openClocksRead.Length > 0 ? _openClocksRead : "none") + "; " + Sky.TimeOfDay.Describe() +
                    "; shown as " + ModeName(ModeOf(row)) + ")");
            Relayout(control.Page);
        }

        private bool IsFolded(string group)
        {
            return _advanced && group != null && !_open.Contains(group);
        }

        private void ReadOpen()
        {
            _open.Clear();
            _openRead = Settings.PanelOpenGroups != null ? Settings.PanelOpenGroups.value : string.Empty;
            if (string.IsNullOrEmpty(_openRead))
                return;

            foreach (string name in _openRead.Split(','))
            {
                string group = name.Trim();
                if (group.Length > 0 && !_open.Contains(group))
                    _open.Add(group);
            }
        }

        /// <summary>A heading clicked: its group folded or opened, kept in the settings file, the page closed up.</summary>
        private void SetFolded(int page, string group, bool folded)
        {
            if (folded != _open.Contains(group))
                return;

            if (folded)
                _open.Remove(group);
            else
                _open.Add(group);

            _openRead = string.Join(",", _open.ToArray());
            Settings.PanelOpenGroups.value = _openRead;
            SettingsXml.SaveNow();

            Log.Msg("panel: " + group + " " + (folded ? "folded" : "opened") + " (open now: " +
                    (_openRead.Length > 0 ? _openRead : "none") + ")");
            Relayout(page);
        }

        /// <summary>
        /// Walks a page top to bottom: each stretch shown is moved to where the ones above it end,
        /// each hidden one is hidden and takes no room. A heading goes with its group's last row.
        /// </summary>
        private void Relayout(int index)
        {
            List<Item> items = _layout[index];
            float y = 0f;
            string changed = string.Empty;

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                bool shown = item.Control != null ? RowShown(item) : HeadingShown(items, i);

                if (shown != item.Shown)
                {
                    SetShown(item, shown);
                    changed += (changed.Length == 0 ? "" : ", ") + (shown ? "+" : "-") +
                               (item.Control != null ? item.Control.Source.Name : "heading");
                }

                if (!shown)
                    continue;

                float dy = y - item.Y;
                if (dy != 0f)
                {
                    foreach (UIComponent component in item.Components)
                        component.relativePosition += new Vector3(0f, dy);

                    item.Y = y;
                }

                y += item.Height;
            }

            _contentHeights[index] = y;
            if (index == _selected)
                FitPage(index);

            // A clock's own line shown again keeps what it had on show: what it should show now.
            foreach (Item item in items)
            {
                if (item.Shown && item.Control != null && item.Control.IsClockTop)
                {
                    item.Control.ClockMode = -1;
                    ShowClockMode(item.Control);
                }
            }

            Log.Msg("panel: " + _tabIds[index] + " tab laid out again (" + changed + "), " + y.ToString("F0") + " high");
        }

        /// <summary>
        /// A heading shows while any row under it, up to the next heading, would by its own rules:
        /// a folded heading stays, to be opened again.
        /// </summary>
        private static bool HeadingShown(List<Item> items, int heading)
        {
            for (int i = heading + 1; i < items.Count && items[i].Control != null; i++)
            {
                if (items[i].Control.Source.IsShownInPanel)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Hides a stretch's components, remembering which were showing, and shows exactly those
        /// again: a component its own row keeps hidden stays hidden.
        /// </summary>
        private static void SetShown(Item item, bool shown)
        {
            item.Shown = shown;

            if (!shown)
            {
                item.HiddenByUs.Clear();
                foreach (UIComponent component in item.Components)
                {
                    if (!component.isVisibleSelf)
                        continue;

                    component.isVisible = false;
                    item.HiddenByUs.Add(component);
                }

                return;
            }

            foreach (UIComponent component in item.HiddenByUs)
                component.isVisible = true;

            item.HiddenByUs.Clear();
        }

        /// <summary>Greys the rows whose switch is off, e.g. every fog row while fog is off.</summary>
        private void RefreshEnabled()
        {
            foreach (Control control in _controls)
            {
                bool wanted = control.Source.IsEnabled;
                if (wanted == control.Enabled)
                    continue;

                control.Enabled = wanted;

                foreach (UIComponent part in control.Parts)
                {
                    part.isEnabled = wanted;
                    part.opacity = wanted ? 1f : 0.45f;
                }
            }
        }

        private void BuildProfileBar()
        {
            try
            {
                _bar = new ProfileBar(_ui, this, Margin, TitleBarHeight, _width - 2f * Margin);
            }
            catch (System.Exception e)
            {
                // The panel works without it: the sky still saves into the settings file.
                _bar = null;
                Log.Error("panel: the profile bar could not be built", e);
            }
        }

        private UIPanel AddPage()
        {
            UIPanel page = AddUIComponent<UIPanel>();
            page.size = new Vector2(_width - 2f * Margin, MinContentHeight);
            page.relativePosition = new Vector3(Margin, TitleBarHeight + BarHeight + _tabHeight + Margin);
            _pages.Add(page);

            return page;
        }

        private void SelectPage(int index)
        {
            _selected = index;

            for (int i = 0; i < _pages.Count; i++)
                _pages[i].isVisible = i == index;

            _tabs.Select(index);
            _ui.SetSubtitle(_frame, _tabNames[index]);
            FitPage(index);
        }

        /// <summary>
        /// The panel is as tall as the selected page needs, and never shorter than it started:
        /// most pages fit, the Halos one has half as many rows again. Again whenever rows of the
        /// page are shown or hidden.
        /// </summary>
        private void FitPage(int index)
        {
            float content = Mathf.Max(MinContentHeight, _contentHeights[index]);
            _pages[index].height = content;
            height = TitleBarHeight + BarHeight + _tabHeight + 2f * Margin + content;

            KeepOnScreen();
        }

        /// <summary>
        /// It grows downwards from where it stands: kept on the screen. Centred on the Now tab at
        /// the game's own 1080-high UI, the advanced Weather tab (~910 high) ended ~100 under the
        /// bottom edge, its last rows out of reach. Also once it is placed (Start): a panel rebuilt
        /// where a shorter one stood.
        /// </summary>
        private void KeepOnScreen()
        {
            UIView view = GetUIView();
            if (view != null && relativePosition.y + height > view.fixedHeight)
                relativePosition = new Vector3(relativePosition.x, Mathf.Max(0f, Mathf.Floor(view.fixedHeight - height)));
        }

        /// <summary>
        /// Draws every catalog row that belongs on this tab, in catalog order, each where it would
        /// be with every row shown, and records the page's stretches for <see cref="Relayout"/>.
        /// </summary>
        private float BuildPage(UIPanel page, Tab tab)
        {
            float y = 0f;
            string group = null;
            List<Item> items = new List<Item>();
            HashSet<UIComponent> claimed = new HashSet<UIComponent>();

            // The Distant clouds tab is short: its headings stay plain, never folding (1.5.0).
            bool folding = _advanced && tab.Panel != PanelPage.DistantClouds;

            // The names' column, per page: as wide as the page's longest name needs (capitals in
            // Frosted take more room), at least the skin's 196. At most 46% of the page in Vanilla;
            // in the new looks as much as leaves a slider MinSlider long (the names come first, the
            // sliders give way). A name still too long is drawn smaller, then on two lines, never
            // cut off (Builder.AddName).
            // A clock's Sunset and Night rows are drawn under their row, as its lines (named Day, Sunset,
            // Night): they take no part in measuring.
            float widest = 0f;
            float widestValue = 0f;
            bool clocks = false;
            bool weather = false;
            foreach (Row row in SettingsCatalog.Rows)
            {
                if (!tab.Shows(row) || row.IsLine)
                    continue;

                if (UsesNameColumn(row.Kind))
                    widest = Mathf.Max(widest, _ui.NameWidth(row.Label));
                if (row.Kind == RowKind.Percent || row.Kind == RowKind.Value)
                    widestValue = Mathf.Max(widestValue, WidestReadout(row));
                if (row.HasClock)
                    clocks = true;
                if (row.HasWeather)
                    weather = true;
            }

            // A page with a clock keeps its column free on every row (1.5.0), so the sliders end in one
            // line; with a cloud button, its column too.
            _ui.ResetColumn = ResetColumn + (clocks ? ClockColumn : 0f) + (weather ? WeatherColumn : 0f);

            // The new looks: the readouts' column as wide as this page's readouts need, before the
            // names take theirs; the slider gets the rest. Vanilla keeps the skin's.
            if (_ui.Skin != Skin.Vanilla)
                _ui.ValueWidth = Mathf.Clamp(widestValue + ReadoutRoom, MinValueWidth, _ui.Theme.ValueWidth);

            float most = _ui.Skin == Skin.Vanilla
                ? Mathf.Floor(page.width * 0.46f)
                : page.width - _ui.ResetColumn - _ui.ValueWidth - 3f * _ui.Theme.Gap - MinSlider;
            _ui.LabelWidth = Mathf.Clamp(Mathf.Ceil(widest) + 8f, _ui.Theme.LabelWidth, most);
            Log.Msg("panel: " + (tab.Panel != PanelPage.None ? tab.Panel.ToString() : tab.Options.ToString()) +
                    " tab names " + _ui.LabelWidth.ToString("F0") + " wide (longest " + widest.ToString("F0") +
                    "), values " + _ui.ValueWidth.ToString("F0") + " (widest " + widestValue.ToString("F0") + ")");

            int pageNumber = _layout.Count;   // this page's index, once it is added below

            foreach (Row row in SettingsCatalog.Rows)
            {
                // A line of text has no control (the profile's name: the bar above is its UI). A clock's
                // Sunset, Night and Overcast rows are built under their row, below.
                if (!tab.Shows(row) || row.Kind == RowKind.Text || row.IsLine)
                    continue;

                if (row.Group != null)
                {
                    // With the advanced options on, a click folds the group away (1.5.0).
                    // A plain heading's rows are in no group: nothing can fold them away.
                    group = folding ? row.Group : null;
                    HeadingRow heading;
                    float height = HeadingHeight;
                    if (folding)
                    {
                        // Its strip (Builder.FoldHeight), with room above and below it between groups.
                        string captured = group;
                        int pageIndex = _layout.Count;   // this page's, once it is added below
                        heading = _ui.AddFoldingHeading(page, row.GroupTitle, y + FoldMarginTop + Builder.FoldAbove,
                            !IsFolded(group), open => SetFolded(pageIndex, captured, !open));
                        height = FoldMarginTop + Builder.FoldHeight + FoldMarginBottom;
                    }
                    else
                    {
                        heading = _ui.AddHeading(page, row.GroupTitle, y + 4f);
                    }

                    Item item = Claim(page, claimed, null, y, height);
                    item.Group = group;
                    item.Heading = heading;
                    items.Add(item);
                    y += height;
                }

                float top = y;
                Control control = Build(page, row, y);
                _controls.Add(control);
                y += _ui.RowHeight;

                if (row.Note != null)
                {
                    UILabel note = _ui.AddNote(page, row.Note, y - 4f, NoteHeight, _ui.Theme.Warning);
                    control.Parts.Add(note);
                    y += NoteHeight;
                }

                Item stretch = Claim(page, claimed, control, top, y - top);
                stretch.Group = group;
                items.Add(stretch);
                control.Item = stretch;
                control.Page = pageNumber;

                // A row with a clock (1.5.0): every line it can show under it, each a row of its own --
                // its Day, Sunset and Night (the clock open), its Clear sky and Overcast (the cloud button
                // open), the table (both, or one while the other's values differ) -- shown by ModeOf. No
                // clock (the icon missing): no lines, the row is one value.
                if (row.HasClock && control.IsClockTop)
                {
                    for (int time = 0; time < row.Times.Length; time++)
                        y = AddLine(page, claimed, items, group, pageNumber, row, ModeTimes, y,
                            BuildLine(page, row, 0, time, y), _ui.RowHeight);

                    if (row.HasWeather)
                    {
                        for (int weatherIndex = 0; weatherIndex < 2; weatherIndex++)
                            y = AddLine(page, claimed, items, group, pageNumber, row, ModeWeathers, y,
                                BuildLine(page, row, 1, weatherIndex, y), _ui.RowHeight);

                        y = AddLine(page, claimed, items, group, pageNumber, row, ModeTable, y,
                            BuildTableHead(page, row, y), TableHeadHeight);
                        for (int time = 0; time < row.Times.Length; time++)
                            y = AddLine(page, claimed, items, group, pageNumber, row, ModeTable, y,
                                BuildTableLine(page, row, time, y), _ui.RowHeight);
                    }
                }
            }

            _layout.Add(items);
            return y;
        }

        /// <summary>A line under a row with a clock, its stretch of the page claimed: where the next one starts.</summary>
        private float AddLine(UIPanel page, HashSet<UIComponent> claimed, List<Item> items, string group, int pageNumber,
            Row clock, int mode, float y, Control line, float height)
        {
            _controls.Add(line);
            Item lineItem = Claim(page, claimed, line, y, height);
            lineItem.Group = group;
            lineItem.ClockOf = clock;
            lineItem.Mode = mode;
            items.Add(lineItem);
            line.Item = lineItem;
            line.Page = pageNumber;
            return y + height;
        }

        /// <summary>
        /// A clock's Day, Sunset or Night line (<paramref name="axis"/> 0), or a cloud button's Clear sky or
        /// Overcast line (1): that time's or weather's row as any row is built (its slider or colour, its
        /// reset), standing for all its values -- a time's clear and overcast, a weather's three times --
        /// named after it, its name stood in for its icon.
        /// </summary>
        private Control BuildLine(UIPanel page, Row clock, int axis, int index, float y)
        {
            Row[] targets = axis == 0 ? SettingsCatalog.TimeValues(clock, index) : SettingsCatalog.WeatherValues(clock, index);
            string key = axis == 0 ? LineKeys[index] : WeatherLineKeys[index];
            Control control = Build(page, targets[0], y, Localization.Get(key), targets.Length > 1 ? targets : null);
            control.LineOf = clock;
            control.LineAxis = axis;
            control.LineTime = index;

            UILabel name = control.Slider != null ? control.Slider.Name : control.Colour != null ? control.Colour.Name : null;
            if (name != null)
            {
                name.relativePosition += new Vector3(LineIndent, 0f);
                name.width = Mathf.Max(20f, name.width - LineIndent);
                control.LineName = name;
                control.LineText = name.text;
            }

            control.LineIcon = AddLineIcon(page, axis == 0 ? LineSprites[index] : WeatherLineSprites[index],
                axis == 0 ? LineColours[index] : WeatherLineColours[index], 0f, y, _ui.RowHeight);
            return control;
        }

        /// <summary>A line's icon, centred in the room the line's name stands in from <paramref name="x"/>; null without the mod's icons.</summary>
        private static UISprite AddLineIcon(UIPanel page, string sprite, Color32 colour, float x, float y, float height)
        {
            UITextureAtlas ours = ModsLibCS1.UI.Atlas.Ours;
            if (ours == null || ours[sprite] == null)
                return null;

            UISprite icon = page.AddUIComponent<UISprite>();
            icon.atlas = ours;
            icon.spriteName = sprite;
            icon.size = new Vector2(LineIconSize, LineIconSize);
            icon.color = colour;
            icon.relativePosition = new Vector3(x + Mathf.Round((LineIndent - LineIconSize) * 0.5f),
                y + Mathf.Round((height - LineIconSize) * 0.5f));
            icon.isInteractive = false;
            return icon;
        }

        /// <summary>Where the table's two columns stand: the first's left edge, each one's width.</summary>
        private void TableColumns(UIPanel page, out float left, out float width)
        {
            left = TableNameWidth;
            float right = page.width - _ui.ResetColumn;
            width = Mathf.Floor((right - left - _ui.Theme.Gap) * 0.5f);
        }

        /// <summary>The table's heading line: "Clear sky" and "Overcast" over their columns, with their icons and shares now.</summary>
        private Control BuildTableHead(UIPanel page, Row clock, float y)
        {
            Control control = new Control { Source = clock, LineOf = clock };
            float left, width;
            TableColumns(page, out left, out width);

            control.HeadNames = new UILabel[2];
            control.HeadTexts = new string[2];
            for (int w = 0; w < 2; w++)
            {
                float x = left + w * (width + _ui.Theme.Gap);
                AddLineIcon(page, WeatherLineSprites[w], WeatherLineColours[w], x - 4f, y, TableHeadHeight);
                string text = Localization.Get(WeatherLineKeys[w]);
                UILabel name = _ui.AddName(page, text, new Vector3(x + LineIndent - 4f, y + 4f), width - LineIndent);
                control.HeadNames[w] = name;
                control.HeadTexts[w] = name.text;
                control.Parts.Add(name);
            }

            return control;
        }

        /// <summary>
        /// A table line: the time's icon and name, then its Clear sky and Overcast values side by side -- a
        /// half-width slider each (or the colour's swatch and text) -- and one reset for the two.
        /// </summary>
        private Control BuildTableLine(UIPanel page, Row clock, int time, float y)
        {
            Row[] cells = SettingsCatalog.TimeValues(clock, time);
            Control control = new Control
            {
                Source = cells[0],
                LineOf = clock,
                LineTime = time,
                Targets = cells,
                TableCells = cells,
            };

            UILabel name = _ui.AddName(page, Localization.Get(LineKeys[time]), new Vector3(LineIndent, y + 8f), TableNameWidth - LineIndent - 4f);
            control.LineName = name;
            control.LineText = name.text;
            control.Parts.Add(name);
            control.LineIcon = AddLineIcon(page, LineSprites[time], LineColours[time], 0f, y, _ui.RowHeight);

            float left, width;
            TableColumns(page, out left, out width);

            if (clock.Kind == RowKind.Colour)
            {
                control.CellColours = new ColourCell[2];
                for (int w = 0; w < 2; w++)
                {
                    var cell = new ColourCell(_ui, page, cells[w], left + w * (width + _ui.Theme.Gap), y, width);
                    control.CellColours[w] = cell;
                    control.Parts.AddRange(cell.Parts);
                }
            }
            else
            {
                control.CellSliders = new SliderRow[2];
                for (int w = 0; w < 2; w++)
                {
                    SliderRow slider = AddCellSlider(page, cells[w], left + w * (width + _ui.Theme.Gap), width, y, w == 0);
                    control.CellSliders[w] = slider;
                    control.Parts.AddRange(slider.Parts);
                }
            }

            control.Reset = _ui.AddReset(page, y, cells[0].ResetTooltip);
            if (control.Reset != null)
            {
                Row[] captured = cells;
                control.Reset.eventClick += (component, e) => SettingsCatalog.ResetRows(captured[0], captured);
            }

            return control;
        }

        /// <summary>
        /// A half-width slider for one table cell: the builder's own slider row, with its names' column,
        /// readout and right edge set for the cell while it is made (put back after), and no name.
        /// </summary>
        private SliderRow AddCellSlider(UIPanel page, Row row, float x, float width, float y, bool keepLine)
        {
            float labelWidth = _ui.LabelWidth;
            float valueWidth = _ui.ValueWidth;
            float resetColumn = _ui.ResetColumn;
            SliderRow slider;
            try
            {
                _ui.LabelWidth = x - _ui.Theme.Gap;
                _ui.ValueWidth = CellValueWidth;
                _ui.ResetColumn = page.width - (x + width);

                Row captured = row;
                slider = _ui.AddSlider(page, y, string.Empty, row.Min, row.Max, row.Step,
                    Mathf.Clamp(row.Display, row.Min, row.Max), row.FormatDisplay,
                    v =>
                    {
                        if (_refreshing)
                            return;

                        captured.Store(v);
                        Changed(captured);
                    });
            }
            finally
            {
                _ui.LabelWidth = labelWidth;
                _ui.ValueWidth = valueWidth;
                _ui.ResetColumn = resetColumn;
            }

            // The line has one name (the time's) and one rule under it.
            if (slider.Name != null)
                slider.Name.isVisible = false;
            if (!keepLine && slider.Line != null)
                slider.Line.isVisible = false;

            return slider;
        }

        /// <summary>
        /// A row with a clock's own line (1.5.0): its clock button (left of the reset), and the note shown
        /// instead of its slider or colour while its times differ or the clock is open, with the value in
        /// use now. Without the mod's icons there is no clock: the row works as one value all day.
        /// </summary>
        private void AddClock(UIPanel page, Control control, float y)
        {
            UITextureAtlas ours = ModsLibCS1.UI.Atlas.Ours;
            if (ours == null || ours[ClockSprite] == null)
            {
                if (!_clockMissingLogged)
                {
                    _clockMissingLogged = true;
                    Log.Msg("panel: no clock icon in the atlas (" + (ours == null ? "no atlas" : "'" + ClockSprite + "' missing") +
                            ") -- the rows with a clock show one value all day");
                }
                return;
            }

            const float size = 22f;
            UISprite icon;
            UIButton button = AddIconButton(page, ours, ClockSprite, page.width - ResetColumn - size, y, out icon);
            Control captured = control;
            button.eventClick += (component, e) => ToggleClock(captured);

            control.Clock = button;
            control.ClockIcon = icon;
            control.Parts.Add(button);

            // The cloud button (1.5.0), left of the clock, on a row with Clear sky and Overcast values.
            // Without its icon the row keeps its clock, its values the same in every weather on show.
            if (control.Source.HasWeather && ours[WeatherSprite] != null)
            {
                UISprite cloud;
                UIButton weather = AddIconButton(page, ours, WeatherSprite, page.width - ResetColumn - ClockColumn - size, y, out cloud);
                weather.eventClick += (component, e) => ToggleWeather(captured);
                control.Weather = weather;
                control.WeatherIcon = cloud;
                control.Parts.Add(weather);
            }

            // Where the slider (or the colour's parts) stands: the note, and for a colour the colour in
            // use now beside it (a slider's readout shows its value).
            float x = _ui.LabelWidth + _ui.Theme.Gap;
            float right = page.width - _ui.ResetColumn;
            UILabel note = _ui.AddLabel(page, Localization.Get("Time.Changes"), new Vector3(x, y + 9f), _ui.Theme.TextScale * 0.9f);
            note.autoSize = false;
            note.size = new Vector2(Mathf.Max(40f, right - x - (control.Colour != null ? 80f : _ui.ValueWidth + _ui.Theme.Gap)), 20f);
            _ui.Paint(note, _ui.Theme.Status);
            note.isVisible = false;
            control.ClockNote = note;
            control.Parts.Add(note);

            if (control.Colour != null)
            {
                UILabel now = _ui.AddLabel(page, string.Empty, new Vector3(right - 76f, y + 9f), _ui.Theme.TextScale * 0.9f);
                now.autoSize = false;
                now.size = new Vector2(76f, 20f);
                now.textAlignment = UIHorizontalAlignment.Right;
                _ui.Paint(now, _ui.Theme.Value);
                now.isVisible = false;
                control.ClockNow = now;
                control.Parts.Add(now);
            }
        }

        private bool _clockMissingLogged;

        /// <summary>A row's 22-unit icon button (the clock, the cloud), the skin's hover behind it.</summary>
        private UIButton AddIconButton(UIPanel page, UITextureAtlas ours, string sprite, float x, float y, out UISprite icon)
        {
            const float size = 22f;
            UIButton button = page.AddUIComponent<UIButton>();
            button.atlas = ours;
            button.size = new Vector2(size, size);
            button.relativePosition = new Vector3(x, y + Mathf.Round((_ui.RowHeight - size) * 0.5f));
            button.canFocus = false;

            SkinSprites sprites = _ui.Theme.Sprites;
            string hover = sprites.OwnSheet ? sprites.ButtonHover : "st-button-hover";
            if (hover != null && ours[hover] != null)
            {
                button.hoveredBgSprite = hover;
                button.pressedBgSprite = hover;
            }

            icon = button.AddUIComponent<UISprite>();
            icon.atlas = ours;
            icon.spriteName = sprite;
            icon.size = new Vector2(16f, 16f);
            icon.relativePosition = new Vector3(Mathf.Round((size - 16f) * 0.5f), Mathf.Round((size - 16f) * 0.5f));
            icon.isInteractive = false;
            return button;
        }

        /// <summary>A row's button lit while open (the skin's accent behind it, the icon white); closed, a softer icon.</summary>
        private void Light(UIButton button, UISprite icon, bool open)
        {
            SkinSprites sprites = _ui.Theme.Sprites;
            bool own = sprites.OwnSheet && sprites.Accent != null;
            string lit = own ? sprites.Accent : "st-accent";
            string litHover = own && sprites.AccentHover != null ? sprites.AccentHover : "st-accent-hover";
            string hover = sprites.OwnSheet ? sprites.ButtonHover : "st-button-hover";
            UITextureAtlas ours = ModsLibCS1.UI.Atlas.Ours;
            bool showLit = open && ours != null && ours[lit] != null;
            button.normalBgSprite = showLit ? lit : string.Empty;
            string over = showLit && ours[litHover] != null ? litHover : hover;
            if (over != null && ours != null && ours[over] != null)
            {
                button.hoveredBgSprite = over;
                button.pressedBgSprite = over;
            }
            icon.color = open ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 175);
        }

        /// <summary>
        /// What a clock's own line shows: one slider (or colour) moving all its values while they are the
        /// same and its buttons are closed; otherwise the note and the value in use now. Its reset only
        /// while closed (the lines have their own). Only on a line on show: a hidden one keeps what it hides.
        /// </summary>
        private void ShowClockMode(Control control)
        {
            if (control.Item == null || !control.Item.Shown)
                return;

            Row row = control.Source;
            bool clockOpen = IsClockOpen(row);
            bool weatherOpen = IsWeatherOpen(row);
            bool open = clockOpen || weatherOpen;
            bool allDay = !open && SettingsCatalog.SameAllDay(row);
            int mode = (open ? 2 : 0) + (allDay ? 1 : 0) + (clockOpen ? 4 : 0) + (weatherOpen ? 8 : 0);
            if (mode == control.ClockMode)
                return;

            control.ClockMode = mode;

            if (control.Slider != null)
                control.Slider.Slider.isVisible = allDay;

            if (control.Colour != null)
            {
                foreach (UIComponent part in control.Colour.ValueParts)
                    part.isVisible = allDay;
                if (control.Colour.Reset != null)
                    control.Colour.Reset.isVisible = !open;
            }

            if (control.ClockNote != null)
                control.ClockNote.isVisible = !allDay;
            if (control.ClockNow != null)
                control.ClockNow.isVisible = !allDay;
            if (control.Reset != null)
                control.Reset.isVisible = !open;

            // Each button lit while it is open.
            Light(control.Clock, control.ClockIcon, clockOpen);
            control.Clock.tooltip = Localization.Get(clockOpen ? "Time.Close" : "Time.Open");
            if (control.Weather != null)
            {
                Light(control.Weather, control.WeatherIcon, weatherOpen);
                control.Weather.tooltip = Localization.Get(weatherOpen ? "Weather.Close" : "Weather.Open");
            }
        }

        /// <summary>
        /// The own line's note (1.5.0): what the row's values change with -- the time of day, the weather or
        /// both -- or, with them all the same, what its open button shows.
        /// </summary>
        private string ChangesText(Row row)
        {
            bool byTime = !SettingsCatalog.SameAtEveryHour(row);
            bool byWeather = !SettingsCatalog.SameInEveryWeather(row);
            if (byTime && byWeather)
                return Localization.Get("Weather.ChangesBoth");
            if (byWeather || (!byTime && IsWeatherOpen(row) && !IsClockOpen(row)))
                return Localization.Get("Weather.Changes");

            return Localization.Get("Time.Changes");
        }

        /// <summary>
        /// A clock's own line, four times a second: what it shows (<see cref="ShowClockMode"/>), the slider
        /// or colour while it is one value all day, otherwise the value in use now; its reset.
        /// </summary>
        private int RefreshClockTop(Control control, bool retip)
        {
            Row row = control.Source;
            int touched = 0;
            ShowClockMode(control);
            bool allDay = control.ClockMode == 1;

            if (control.Slider != null)
            {
                if (allDay)
                {
                    float display = Mathf.Clamp(row.Display, row.Min, row.Max);
                    if (!Mathf.Approximately(control.Slider.Slider.value, display))
                    {
                        control.Slider.SetValue(display);
                        touched++;
                    }
                    else
                    {
                        string text = row.FormatDisplay(control.Slider.Slider.value);
                        if (control.Slider.Value.text != text)
                            control.Slider.Value.text = text;
                    }
                }
                else
                {
                    // The value the clouds use now, between the three -- and the two weathers.
                    float now = row.HasWeather
                        ? Settings.Now(row.Times[0].Float, row.Times[1].Float, row.Times[2].Float,
                            row.Overcasts[0].Float, row.Overcasts[1].Float, row.Overcasts[2].Float, row.DefaultFloat)
                        : Settings.Now(row.Times[0].Float, row.Times[1].Float, row.Times[2].Float, row.DefaultFloat);
                    string text = row.FormatDisplay(row.Kind == RowKind.Percent ? now * 100f : now);
                    if (control.Slider.Value.text != text)
                        control.Slider.Value.text = text;
                }
            }

            if (!allDay && control.ClockNote != null)
            {
                string changes = ChangesText(row);
                if (control.ClockNote.text != changes)
                    control.ClockNote.text = changes;
            }

            if (control.Colour != null)
            {
                control.Colour.Targets = SettingsCatalog.ValuesOf(row);
                control.Colour.Refresh();
                if (!allDay && control.ClockNow != null)
                {
                    string text = NowColour(row);
                    if (control.ClockNow.text != text)
                        control.ClockNow.text = text;
                }

                if (retip)
                    control.Colour.ResetTooltip = row.ResetTooltip;
            }

            if (control.Reset != null)
            {
                Builder.SetResetEnabled(control.Reset, row.IsEnabled && !SettingsCatalog.ClockAtResetValue(row));
                if (retip)
                    control.Reset.tooltip = row.ResetTooltip;
            }

            return touched;
        }

        /// <summary>
        /// The colour in use now, written as a picked one: Day, Sunset and Night -- each its Clear sky and
        /// Overcast colours by the cover first -- blended as the players' colours (sRGB), for the eye; the
        /// light itself blends their tints (Settings.TintNow).
        /// </summary>
        private static string NowColour(Row row)
        {
            float[] shares = { Sky.TimeOfDay.Day, Sky.TimeOfDay.Sunset, Sky.TimeOfDay.Night };
            float overcast = row.HasWeather ? Sky.Overcast.Share : 0f;
            float r = 0f, g = 0f, b = 0f;
            for (int time = 0; time < 3; time++)
            {
                Color32 clear = row.Times[time].Colour.value;
                Color32 grey = row.HasWeather ? row.Overcasts[time].Colour.value : clear;
                r += shares[time] * Mathf.Lerp(clear.r, grey.r, overcast);
                g += shares[time] * Mathf.Lerp(clear.g, grey.g, overcast);
                b += shares[time] * Mathf.Lerp(clear.b, grey.b, overcast);
            }

            var blended = new Color32((byte)Mathf.Clamp(Mathf.Round(r), 0f, 255f), (byte)Mathf.Clamp(Mathf.Round(g), 0f, 255f),
                (byte)Mathf.Clamp(Mathf.Round(b), 0f, 255f), 255);
            return ColorText.Format(blended);
        }

        /// <summary>The share of a line's time (axis 0) or weather (axis 1) now.</summary>
        private static float LineShare(int axis, int index)
        {
            if (axis == 1)
                return index == 0 ? 1f - Sky.Overcast.Share : Sky.Overcast.Share;

            return index == 0 ? Sky.TimeOfDay.Day : index == 1 ? Sky.TimeOfDay.Sunset : Sky.TimeOfDay.Night;
        }

        /// <summary>A line: its icon lit by its time's (or weather's) share now, the share after its name while it blends.</summary>
        private void RefreshLine(Control control)
        {
            float share = LineShare(control.LineAxis, control.LineTime);

            if (control.LineName != null)
            {
                string text = ShareText(control.LineText, share);
                if (control.LineName.text != text)
                    control.LineName.text = text;
            }

            if (control.LineIcon != null)
            {
                float lit = (0.4f + 0.6f * share) * (control.Enabled ? 1f : 0.45f);
                if (!Mathf.Approximately(control.LineIcon.opacity, lit))
                    control.LineIcon.opacity = lit;
            }
        }

        private static string ShareText(string name, float share)
        {
            return share > 0.005f && share < 0.995f ? name + "  " + Mathf.RoundToInt(share * 100f) + "%" : name;
        }

        /// <summary>The table's heading: each weather's share now after its name.</summary>
        private void RefreshTableHead(Control control)
        {
            for (int w = 0; w < 2; w++)
            {
                string text = ShareText(control.HeadTexts[w], LineShare(1, w));
                if (control.HeadNames[w].text != text)
                    control.HeadNames[w].text = text;
            }
        }

        /// <summary>A table line: its time's share, its two cells, its reset (both values back).</summary>
        private int RefreshTableLine(Control control, bool retip)
        {
            int touched = 0;
            RefreshLine(control);

            if (control.CellSliders != null)
            {
                for (int w = 0; w < control.CellSliders.Length; w++)
                {
                    Row cell = control.TableCells[w];
                    SliderRow slider = control.CellSliders[w];
                    float display = Mathf.Clamp(cell.Display, cell.Min, cell.Max);
                    if (!Mathf.Approximately(slider.Slider.value, display))
                    {
                        slider.SetValue(display);
                        touched++;
                    }
                }
            }

            if (control.CellColours != null)
            {
                foreach (ColourCell cell in control.CellColours)
                    cell.Refresh();
            }

            if (control.Reset != null)
            {
                Builder.SetResetEnabled(control.Reset, control.Source.IsEnabled && !SettingsCatalog.AtResetValue(control.TableCells));
                if (retip)
                    control.Reset.tooltip = control.Source.ResetTooltip;
            }

            return touched;
        }

        /// <summary>
        /// A new stretch: every component on the page that no earlier stretch has claimed -- what
        /// the builders just added, whatever each keeps track of itself (a status line, a key's
        /// name, a colour row's six parts).
        /// </summary>
        private static Item Claim(UIPanel page, HashSet<UIComponent> claimed, Control control, float y, float height)
        {
            Item item = new Item { Control = control, Y = y, Height = height };

            for (int i = 0; i < page.components.Count; i++)
            {
                UIComponent component = page.components[i];
                if (claimed.Add(component))
                    item.Components.Add(component);
            }

            return item;
        }

        /// <summary>
        /// The widest a slider row's readout is drawn, over its range (both ends, the quarters
        /// between them, and where it stands now): its text follows the value ("9 km", "20.0 km").
        /// </summary>
        private float WidestReadout(Row row)
        {
            float widest = _ui.ReadoutWidth(row.FormatDisplay(Mathf.Clamp(row.Display, row.Min, row.Max)));
            for (int i = 0; i <= 4; i++)
                widest = Mathf.Max(widest, _ui.ReadoutWidth(row.FormatDisplay(Mathf.Lerp(row.Min, row.Max, i * 0.25f))));

            return widest;
        }

        /// <summary>The rows whose name stands in the names' column (a tick box left of its name does not).</summary>
        private bool UsesNameColumn(RowKind kind)
        {
            switch (kind)
            {
                case RowKind.Status:
                case RowKind.Button:
                case RowKind.Text:
                    return false;
                case RowKind.Toggle:
                    return _ui.Theme.Toggle != ToggleShape.TickLeft;
                default:
                    return true;
            }
        }

        /// <param name="label">The name to write; null: the row's (a clock's lines write Day, Sunset, Night...).</param>
        /// <param name="targets">A line standing for several values (1.5.0): all of them, the row first.</param>
        private Control Build(UIPanel page, Row row, float y, string label = null, Row[] targets = null)
        {
            // A row with a clock's own line stands for all its values (it shows its slider only while they
            // are the same and its buttons are closed). Not its Day line: the same row under another name.
            bool own = row.HasClock && label == null;
            if (own)
                targets = SettingsCatalog.ValuesOf(row);

            Control control = BuildControl(page, row, y, label ?? row.Label, targets);

            // The row's reset button. A colour row has its own (it logs the pick its own way). On a
            // line standing for several values it puts them all back (1.5.0).
            if (row.CanReset && row.Kind != RowKind.Colour && row.Kind != RowKind.Status &&
                row.Kind != RowKind.Button && row.Kind != RowKind.Text)
            {
                control.Reset = _ui.AddReset(page, y, row.ResetTooltip);
                if (control.Reset != null)
                {
                    Row captured = row;
                    Row[] all = targets;
                    control.Reset.eventClick += (component, e) =>
                    {
                        if (all != null)
                            SettingsCatalog.ResetRows(captured, all);
                        else
                            SettingsCatalog.ResetRow(captured);
                    };
                    Builder.SetResetEnabled(control.Reset, row.IsEnabled && !(all != null ? SettingsCatalog.AtResetValue(all) : row.IsAtResetValue));
                }
            }

            if (own)
                AddClock(page, control, y);
            else
                control.Targets = targets;

            return control;
        }

        /// <param name="targets">
        /// The values its slider or colour moves together (a row with a clock's own line: all of them; a
        /// Day line: its clear and overcast; a Clear sky line: its three times). Null: its own row's alone.
        /// </param>
        private Control BuildControl(UIPanel page, Row row, float y, string label, Row[] targets)
        {
            Control control = new Control { Source = row };

            switch (row.Kind)
            {
                case RowKind.Status:
                    control.Status = _ui.AddStatus(page, y, row.StatusText != null ? row.StatusText() : string.Empty);
                    break;

                case RowKind.Button:
                {
                    UIButton button = _ui.AddButton(page, row.Label, new Vector2(page.width, 26f),
                        new Vector3(0f, y + 4f));
                    Row captured = row;
                    button.eventClick += (component, e) =>
                    {
                        if (captured.OnClick == null)
                            return;

                        captured.OnClick();
                        SettingsCatalog.RefreshAllUIs();
                    };
                    control.Parts.Add(button);
                    break;
                }

                case RowKind.Toggle:
                {
                    Row captured = row;
                    ToggleRow toggle = _ui.AddToggle(page, y, row.Label,
                        row.Bool != null && row.Bool.value, isChecked =>
                        {
                            if (_refreshing)
                                return;

                            SettingsCatalog.ApplyToggle(captured, isChecked);
                        });
                    control.Check = toggle.Check;
                    control.Parts.AddRange(toggle.Parts);
                    break;
                }

                case RowKind.Key:
                {
                    KeyRow key = _ui.AddKeyBinding(page, y, row.Label, row.Key, Localization.Get("Key.Press"),
                        SettingsXml.SaveNow);
                    control.Key = key;
                    control.KeyShown = key.Button.text;
                    control.Parts.AddRange(key.Parts);
                    break;
                }

                case RowKind.Colour:
                {
                    control.Colour = new ColourRow(_ui, page, y, row, label);
                    control.Colour.Targets = targets;
                    control.Parts.AddRange(control.Colour.Parts);
                    break;
                }

                case RowKind.Choice:
                {
                    // Vanilla: a slider over the choices, the choice's name as its value. The new
                    // looks: a row of buttons, or a dropdown when the names don't fit (measured,
                    // so it follows the language). Either way the row hands back an index, and
                    // what is stored is that choice's VALUE (choice rows are stored by value).
                    Row captured = row;
                    ChoiceRow choice = _ui.AddChoice(page, y, row.Label, row.Choices, row.ChoiceIndex, index =>
                    {
                        if (_refreshing || captured.Int == null || captured.ChoiceValues == null)
                            return;

                        index = Mathf.Clamp(index, 0, captured.ChoiceValues.Length - 1);
                        captured.Int.value = captured.ChoiceValues[index];
                        Changed(captured);
                    });
                    control.Choice = choice;
                    control.Parts.AddRange(choice.Parts);
                    break;
                }

                default:
                {
                    Row captured = row;
                    Row[] all = targets;
                    SliderRow slider = _ui.AddSlider(page, y, label, row.Min, row.Max, row.Step,
                        Mathf.Clamp(row.Display, row.Min, row.Max),
                        row.FormatDisplay,
                        v =>
                        {
                            if (_refreshing)
                                return;

                            if (all != null)
                                SettingsCatalog.StoreAll(all, v);
                            else
                                captured.Store(v);
                            Changed(captured);
                        });
                    control.Slider = slider;
                    control.Parts.AddRange(slider.Parts);
                    break;
                }
            }

            return control;
        }

        /// <summary>
        /// After a slider stored its value. A row with a live-apply hook may have changed OTHER
        /// rows (the quality preset writes three of them; moving one of those three flips the
        /// preset to Custom), so both UIs look again. Rows without one change nothing but
        /// themselves, and are left alone -- this runs on every tick of a drag.
        /// </summary>
        private static void Changed(Row row)
        {
            if (row.AfterChange == null)
                return;

            row.AfterChange();
            SettingsCatalog.RefreshAllUIs();
        }

        private void CenterOnScreen()
        {
            UIView view = GetUIView();
            relativePosition = new Vector3(
                Mathf.Floor((view.fixedWidth - _width) / 2f),
                Mathf.Floor((view.fixedHeight - height) / 2f));
        }

        public override void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            base.OnDestroy();
        }
    }
}
