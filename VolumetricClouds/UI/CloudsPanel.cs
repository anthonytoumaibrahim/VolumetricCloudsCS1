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

            /// <summary>A Day / Sunset / Night line: the row whose clock shows it, which time, its name and icon.</summary>
            public Row LineOf;
            public int LineTime;
            public UILabel LineName;
            public string LineText;
            public UISprite LineIcon;

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

            /// <summary>A Day / Sunset / Night line (1.5.0): the row whose clock shows it, only while that is open.</summary>
            public Row ClockOf;
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

        public override void Start()
        {
            base.Start();

            _instance = this;

            bool advanced = Settings.ShowAdvancedInPanel != null && Settings.ShowAdvancedInPanel.value;
            _advanced = advanced;
            ReadOpen();
            ReadOpenClocks();

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

                    // Lit while the row is off what it would go back to (and not greyed out).
                    if (control.Reset != null)
                    {
                        Builder.SetResetEnabled(control.Reset, row.IsEnabled && !row.IsAtResetValue);
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
        /// A row: shown by its own rules (Row.IsShownInPanel), and its group not folded away. A clock's
        /// Day / Sunset / Night line: and its clock open (1.5.0).
        /// </summary>
        private bool RowShown(Item item)
        {
            return item.Control.Source.IsShownInPanel && !IsFolded(item.Group) &&
                   (item.ClockOf == null || IsClockOpen(item.ClockOf));
        }

        private bool IsClockOpen(Row row)
        {
            return _openClocks.Contains(row.Name);
        }

        private void ReadOpenClocks()
        {
            _openClocks.Clear();
            _openClocksRead = Settings.PanelOpenClocks != null ? Settings.PanelOpenClocks.value : string.Empty;
            if (string.IsNullOrEmpty(_openClocksRead))
                return;

            foreach (string name in _openClocksRead.Split(','))
            {
                string row = name.Trim();
                if (row.Length > 0 && !_openClocks.Contains(row))
                    _openClocks.Add(row);
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

            Log.Msg("panel: " + row.Name + "'s clock " + (open ? "opened" : "closed") + " (open now: " +
                    (_openClocksRead.Length > 0 ? _openClocksRead : "none") + "; " + Sky.TimeOfDay.Describe() + ")");
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
            foreach (Row row in SettingsCatalog.Rows)
            {
                if (!tab.Shows(row) || row.TimeOf != null)
                    continue;

                if (UsesNameColumn(row.Kind))
                    widest = Mathf.Max(widest, _ui.NameWidth(row.Label));
                if (row.Kind == RowKind.Percent || row.Kind == RowKind.Value)
                    widestValue = Mathf.Max(widestValue, WidestReadout(row));
                if (row.HasClock)
                    clocks = true;
            }

            // A page with a clock keeps its column free on every row (1.5.0), so the sliders end in one line.
            _ui.ResetColumn = clocks ? ResetColumn + ClockColumn : ResetColumn;

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
                // Sunset and Night rows are built under their row, below.
                if (!tab.Shows(row) || row.Kind == RowKind.Text || row.TimeOf != null)
                    continue;

                if (row.Group != null)
                {
                    // With the advanced options on, a click folds the group away (1.5.0).
                    group = row.Group;
                    HeadingRow heading;
                    float height = HeadingHeight;
                    if (_advanced)
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

                // A row with a clock (1.5.0): its Day, Sunset and Night lines under it, each a row of its
                // own (slider or colour, and reset), shown while the clock is open. No clock (the icon
                // missing): no lines, the row is one value all day.
                if (row.HasClock && control.IsClockTop)
                {
                    for (int time = 0; time < row.Times.Length; time++)
                    {
                        float lineTop = y;
                        Control line = BuildLine(page, row, time, y);
                        _controls.Add(line);
                        y += _ui.RowHeight;

                        Item lineItem = Claim(page, claimed, line, lineTop, y - lineTop);
                        lineItem.Group = group;
                        lineItem.ClockOf = row;
                        items.Add(lineItem);
                        line.Item = lineItem;
                        line.Page = pageNumber;
                    }
                }
            }

            _layout.Add(items);
            return y;
        }

        /// <summary>
        /// A clock's Day, Sunset or Night line: that time's row as any row is built (its slider or
        /// colour, its reset), named after the time, its name stood in for the time's icon.
        /// </summary>
        private Control BuildLine(UIPanel page, Row clock, int time, float y)
        {
            Row row = clock.Times[time];
            Control control = Build(page, row, y, Localization.Get(LineKeys[time]));
            control.LineOf = clock;
            control.LineTime = time;

            UILabel name = control.Slider != null ? control.Slider.Name : control.Colour != null ? control.Colour.Name : null;
            if (name != null)
            {
                name.relativePosition += new Vector3(LineIndent, 0f);
                name.width = Mathf.Max(20f, name.width - LineIndent);
                control.LineName = name;
                control.LineText = name.text;
            }

            UITextureAtlas ours = ModsLibCS1.UI.Atlas.Ours;
            if (ours != null && ours[LineSprites[time]] != null)
            {
                UISprite icon = page.AddUIComponent<UISprite>();
                icon.atlas = ours;
                icon.spriteName = LineSprites[time];
                icon.size = new Vector2(LineIconSize, LineIconSize);
                icon.color = LineColours[time];
                icon.relativePosition = new Vector3(Mathf.Round((LineIndent - LineIconSize) * 0.5f),
                    y + Mathf.Round((_ui.RowHeight - LineIconSize) * 0.5f));
                icon.isInteractive = false;
                control.LineIcon = icon;
            }

            return control;
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
            UIButton button = page.AddUIComponent<UIButton>();
            button.atlas = ours;
            button.size = new Vector2(size, size);
            button.relativePosition = new Vector3(page.width - ResetColumn - size, y + Mathf.Round((_ui.RowHeight - size) * 0.5f));
            button.canFocus = false;

            SkinSprites sprites = _ui.Theme.Sprites;
            string hover = sprites.OwnSheet ? sprites.ButtonHover : "st-button-hover";
            if (hover != null && ours[hover] != null)
            {
                button.hoveredBgSprite = hover;
                button.pressedBgSprite = hover;
            }

            UISprite icon = button.AddUIComponent<UISprite>();
            icon.atlas = ours;
            icon.spriteName = ClockSprite;
            icon.size = new Vector2(16f, 16f);
            icon.relativePosition = new Vector3(Mathf.Round((size - 16f) * 0.5f), Mathf.Round((size - 16f) * 0.5f));
            icon.isInteractive = false;

            Control captured = control;
            button.eventClick += (component, e) => ToggleClock(captured);

            control.Clock = button;
            control.ClockIcon = icon;
            control.Parts.Add(button);

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

        /// <summary>
        /// What a clock's own line shows: one slider (or colour) moving all three while they are the same
        /// and the clock is closed; otherwise the note and the value in use now. Its reset only while
        /// closed (the lines have their own). Only on a line on show: a hidden one keeps what it hides.
        /// </summary>
        private void ShowClockMode(Control control)
        {
            if (control.Item == null || !control.Item.Shown)
                return;

            Row row = control.Source;
            bool open = IsClockOpen(row);
            bool allDay = !open && SettingsCatalog.SameAllDay(row);
            int mode = (open ? 2 : 0) + (allDay ? 1 : 0);
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

            // Lit while open: the skin's accent behind it, the icon white; closed, a softer icon.
            SkinSprites sprites = _ui.Theme.Sprites;
            bool own = sprites.OwnSheet && sprites.Accent != null;
            string lit = own ? sprites.Accent : "st-accent";
            string litHover = own && sprites.AccentHover != null ? sprites.AccentHover : "st-accent-hover";
            string hover = sprites.OwnSheet ? sprites.ButtonHover : "st-button-hover";
            UITextureAtlas ours = ModsLibCS1.UI.Atlas.Ours;
            bool showLit = open && ours != null && ours[lit] != null;
            control.Clock.normalBgSprite = showLit ? lit : string.Empty;
            string over = showLit && ours[litHover] != null ? litHover : hover;
            if (over != null && ours != null && ours[over] != null)
            {
                control.Clock.hoveredBgSprite = over;
                control.Clock.pressedBgSprite = over;
            }
            control.ClockIcon.color = open ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 175);
            control.Clock.tooltip = Localization.Get(open ? "Time.Close" : "Time.Open");
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
                    // The value the clouds use now, between the three.
                    float now = Settings.Now(row.Times[0].Float, row.Times[1].Float, row.Times[2].Float, row.DefaultFloat);
                    string text = row.FormatDisplay(row.Kind == RowKind.Percent ? now * 100f : now);
                    if (control.Slider.Value.text != text)
                        control.Slider.Value.text = text;
                }
            }

            if (control.Colour != null)
            {
                control.Colour.AllDay = true;
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
        /// The colour in use now, written as a picked one: Day, Sunset and Night blended as the players'
        /// colours (sRGB), for the eye -- the light itself blends their tints (Settings.TintNow).
        /// </summary>
        private static string NowColour(Row row)
        {
            Color32 day = row.Times[0].Colour.value;
            Color32 sunset = row.Times[1].Colour.value;
            Color32 night = row.Times[2].Colour.value;
            float d = Sky.TimeOfDay.Day, s = Sky.TimeOfDay.Sunset, n = Sky.TimeOfDay.Night;
            var blended = new Color32(
                (byte)Mathf.Clamp(Mathf.Round(day.r * d + sunset.r * s + night.r * n), 0f, 255f),
                (byte)Mathf.Clamp(Mathf.Round(day.g * d + sunset.g * s + night.g * n), 0f, 255f),
                (byte)Mathf.Clamp(Mathf.Round(day.b * d + sunset.b * s + night.b * n), 0f, 255f), 255);
            return ColorText.Format(blended);
        }

        /// <summary>A Day / Sunset / Night line: its icon lit by its time's share now, the share after its name while it blends.</summary>
        private void RefreshLine(Control control)
        {
            int time = control.LineTime;
            float share = time == 0 ? Sky.TimeOfDay.Day : time == 1 ? Sky.TimeOfDay.Sunset : Sky.TimeOfDay.Night;

            if (control.LineName != null)
            {
                string text = share > 0.005f && share < 0.995f
                    ? control.LineText + "  " + Mathf.RoundToInt(share * 100f) + "%"
                    : control.LineText;
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

        /// <param name="label">The name to write; null: the row's (a clock's lines write Day, Sunset, Night).</param>
        private Control Build(UIPanel page, Row row, float y, string label = null)
        {
            Control control = BuildControl(page, row, y, label ?? row.Label, row.HasClock && label == null);

            // The row's reset button. A colour row has its own (it logs the pick its own way). On a
            // row with a clock's own line it puts all three times back (1.5.0).
            if (row.CanReset && row.Kind != RowKind.Colour && row.Kind != RowKind.Status &&
                row.Kind != RowKind.Button && row.Kind != RowKind.Text)
            {
                control.Reset = _ui.AddReset(page, y, row.ResetTooltip);
                if (control.Reset != null)
                {
                    Row captured = row;
                    bool clock = row.HasClock && label == null;
                    control.Reset.eventClick += (component, e) =>
                    {
                        if (clock)
                            SettingsCatalog.ResetClock(captured);
                        else
                            SettingsCatalog.ResetRow(captured);
                    };
                    Builder.SetResetEnabled(control.Reset, row.IsEnabled && !(clock ? SettingsCatalog.ClockAtResetValue(row) : row.IsAtResetValue));
                }
            }

            // A row with a clock's own line (not its Day line, which is the same row under another name).
            if (row.HasClock && label == null)
            {
                if (control.Colour != null)
                    control.Colour.AllDay = true;
                AddClock(page, control, y);
            }

            return control;
        }

        /// <param name="allDay">
        /// A row with a clock's own line: its slider moves all three times (it shows only while they are
        /// the same and the clock is closed). Its Day line, the same row, moves the Day value alone.
        /// </param>
        private Control BuildControl(UIPanel page, Row row, float y, string label, bool allDay)
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
                    control.Colour.AllDay = allDay;
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
                    SliderRow slider = _ui.AddSlider(page, y, label, row.Min, row.Max, row.Step,
                        Mathf.Clamp(row.Display, row.Min, row.Max),
                        row.FormatDisplay,
                        v =>
                        {
                            if (_refreshing)
                                return;

                            if (allDay)
                                SettingsCatalog.StoreAllDay(captured, v);
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
