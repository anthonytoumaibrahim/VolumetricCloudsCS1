using System.Collections.Generic;
using ColossalFramework.UI;
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
    /// </remarks>
    public class CloudsPanel : UIPanel
    {
        private const float BasicWidth = 600f;
        private const float AdvancedWidth = 680f;   // was for seven tabs; Weather and Halos are laid out at it
        private const float PanelHeight = 408f;
        private const float TitleBarHeight = 40f;
        private const float BarHeight = ProfileBar.Height;
        private const float TabHeight = 28f;
        private const float Margin = 14f;

        private const float HeadingHeight = 24f;
        private const float NoteHeight = 32f;

        private const float MinContentHeight = PanelHeight - TitleBarHeight - TabHeight - 2f * Margin;

        private static readonly PanelPage[] BasicTabs = { PanelPage.Now, PanelPage.Clouds, PanelPage.Fog, PanelPage.Light };

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

            public bool Shows(Row row)
            {
                if (Panel != PanelPage.None)
                    return row.Panel == Panel;

                // A row that already has a home on a basic tab (the fog switch) stays there.
                return row.Options == Options && row.Panel == PanelPage.None;
            }
        }

        /// <summary>Where to open, when replacing a panel that was already on screen. Null = centred.</summary>
        public Vector3? InitialPosition;

        private readonly List<UIButton> _tabs = new List<UIButton>();
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
            public UISlider Slider;
            public UICheckBox Check;
            public UILabel Readout;
            public UILabel Status;
            public ColourRow Colour;
            public readonly List<UIComponent> Parts = new List<UIComponent>();
            public bool Enabled = true;
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
        }

        public override void Start()
        {
            base.Start();

            _instance = this;

            bool advanced = Settings.ShowAdvancedInPanel != null && Settings.ShowAdvancedInPanel.value;
            _width = advanced ? AdvancedWidth : BasicWidth;

            atlas = UIBuilder.Atlas;
            backgroundSprite = "MenuPanel2";
            size = new Vector2(_width, PanelHeight);
            canFocus = true;
            isInteractive = true;

            BuildTitleBar();
            BuildProfileBar();

            foreach (Tab tab in Tabs(advanced))
            {
                _tabIds.Add(tab.Panel != PanelPage.None ? tab.Panel.ToString() : tab.Options.ToString());
                _contentHeights.Add(BuildPage(AddPage(tab.Name), tab));
            }

            LayoutTabs();

            // Every row is built where it would be if all were shown; the ones hidden now go, and
            // the pages close up, before the first one is shown.
            RefreshShown();
            SelectPage(0);

            if (InitialPosition.HasValue)
                relativePosition = InitialPosition.Value;
            else
                CenterOnScreen();

            eventVisibilityChanged += (component, visible) =>
            {
                if (visible)
                    RefreshValues();
            };

            Log.Msg("panel built: " + _controls.Count + " controls over " + _pages.Count + " tabs (" +
                    (advanced ? "advanced" : "basic") + ")");
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

            try
            {
                foreach (Control control in _controls)
                {
                    Row row = control.Source;

                    if (control.Slider != null)
                    {
                        float display = row.Kind == RowKind.Choice
                            ? row.ChoiceIndex
                            : Mathf.Clamp(row.Display, row.Min, row.Max);

                        if (!Mathf.Approximately(control.Slider.value, display))
                        {
                            control.Slider.value = display;
                            touched++;

                            if (control.Readout != null)
                                control.Readout.text = ReadoutText(row, display);
                        }
                    }

                    if (control.Check != null && row.Bool != null && control.Check.isChecked != row.Bool.value)
                    {
                        control.Check.isChecked = row.Bool.value;
                        touched++;
                    }

                    // It keeps its own guard: its swatch fires events when set.
                    if (control.Colour != null)
                        control.Colour.Refresh();
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
        /// Hides the rows whose Row.Shown says so and closes their page up under them, or opens it
        /// again (1.3.0). Runs with every refresh, but a page is laid out again only when one of
        /// its rows has actually changed.
        /// </summary>
        private void RefreshShown()
        {
            for (int p = 0; p < _layout.Count; p++)
            {
                foreach (Item item in _layout[p])
                {
                    if (item.Control != null && item.Control.Source.Shown != null
                        && item.Control.Source.IsShown != item.Shown)
                    {
                        Relayout(p);
                        break;
                    }
                }
            }
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
                bool shown = item.Control != null ? item.Control.Source.IsShown : HeadingShown(items, i);

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

            Log.Msg("panel: " + _tabIds[index] + " tab laid out again (" + changed + "), " + y.ToString("F0") + " high");
        }

        /// <summary>A heading shows while any row under it, up to the next heading, does.</summary>
        private static bool HeadingShown(List<Item> items, int heading)
        {
            for (int i = heading + 1; i < items.Count && items[i].Control != null; i++)
            {
                if (items[i].Control.Source.IsShown)
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

        private static string ReadoutText(Row row, float display)
        {
            if (row.Kind != RowKind.Choice)
                return row.FormatDisplay(display);

            int index = Mathf.RoundToInt(display);
            return row.Choices != null && index >= 0 && index < row.Choices.Length
                ? row.Choices[index]
                : string.Empty;
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

        private void BuildTitleBar()
        {
            UILabel title = UIBuilder.AddLabel(this, Mod.DisplayName,new Vector3(Margin, Margin), 1.1f);
            title.autoSize = false;
            title.size = new Vector2(_width - TitleBarHeight, TitleBarHeight);

            UIButton close = AddUIComponent<UIButton>();
            close.atlas = atlas;
            close.size = new Vector2(32f, 32f);
            close.relativePosition = new Vector3(_width - 36f, 4f);
            close.normalBgSprite = "buttonclose";
            close.hoveredBgSprite = "buttonclosehover";
            close.pressedBgSprite = "buttonclosepressed";
            close.eventClick += (component, e) => Hide();

            // Dragging the title bar moves the whole panel. Only the title bar: the profile bar
            // under it is controls.
            UIDragHandle drag = AddUIComponent<UIDragHandle>();
            drag.target = this;
            drag.size = new Vector2(_width - 40f, TitleBarHeight);
            drag.relativePosition = Vector3.zero;
        }

        private void BuildProfileBar()
        {
            try
            {
                _bar = new ProfileBar(this, Margin, TitleBarHeight, _width - 2f * Margin);
            }
            catch (System.Exception e)
            {
                // The panel works without it: the sky still saves into the settings file.
                _bar = null;
                Log.Error("panel: the profile bar could not be built", e);
            }
        }

        private UIPanel AddPage(string tabName)
        {
            int index = _pages.Count;

            UIButton tab = UIBuilder.AddTab(this, tabName, new Vector2(10f, TabHeight), Vector3.zero);
            tab.eventClick += (component, e) => SelectPage(index);
            _tabs.Add(tab);

            UIPanel page = AddUIComponent<UIPanel>();
            page.size = new Vector2(_width - 2f * Margin, MinContentHeight);
            page.relativePosition = new Vector3(Margin, TitleBarHeight + BarHeight + TabHeight + Margin);
            _pages.Add(page);

            return page;
        }

        private void LayoutTabs()
        {
            float width = (_width - 2f * Margin) / _tabs.Count;

            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].size = new Vector2(width, TabHeight);
                _tabs[i].relativePosition = new Vector3(Margin + i * width, TitleBarHeight + BarHeight + 2f);
            }
        }

        private void SelectPage(int index)
        {
            _selected = index;

            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].isVisible = i == index;
                UIBuilder.SetTabSelected(_tabs[i], i == index);
            }

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
            height = TitleBarHeight + BarHeight + TabHeight + 2f * Margin + content;
        }

        /// <summary>
        /// Draws every catalog row that belongs on this tab, in catalog order, each where it would
        /// be with every row shown, and records the page's stretches for <see cref="Relayout"/>.
        /// </summary>
        private float BuildPage(UIPanel page, Tab tab)
        {
            float y = 0f;
            List<Item> items = new List<Item>();
            HashSet<UIComponent> claimed = new HashSet<UIComponent>();

            foreach (Row row in SettingsCatalog.Rows)
            {
                // A line of text has no control (the profile's name: the bar above is its UI).
                if (!tab.Shows(row) || row.Kind == RowKind.Text)
                    continue;

                if (row.Group != null)
                {
                    UIBuilder.AddHeading(page, row.GroupTitle, y + 4f);
                    items.Add(Claim(page, claimed, null, y, HeadingHeight));
                    y += HeadingHeight;
                }

                float top = y;
                Control control = Build(page, row, y);
                _controls.Add(control);
                y += UIBuilder.RowHeight;

                if (row.Note != null)
                {
                    UILabel note = UIBuilder.AddNote(page, row.Note, y - 4f, NoteHeight, UIBuilder.WarningColour);
                    control.Parts.Add(note);
                    y += NoteHeight;
                }

                items.Add(Claim(page, claimed, control, top, y - top));
            }

            _layout.Add(items);
            return y;
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

        private Control Build(UIPanel page, Row row, float y)
        {
            Control control = new Control { Source = row };

            switch (row.Kind)
            {
                case RowKind.Status:
                    control.Status = UIBuilder.AddLabel(page,
                        row.StatusText != null ? row.StatusText() : string.Empty, new Vector3(0f, y + 8f), 0.8f);
                    control.Status.autoSize = false;
                    control.Status.size = new Vector2(page.width, 20f);
                    control.Status.textColor = UIBuilder.StatusColour;
                    break;

                case RowKind.Button:
                {
                    UIButton button = UIBuilder.AddButton(page, row.Label, new Vector2(page.width, 26f),
                        new Vector3(0f, y + 4f));
                    button.tooltip = row.Tooltip;
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
                    UICheckBox box = UIBuilder.AddCheckbox(page, y, row.Label,
                        row.Bool != null && row.Bool.value, isChecked =>
                        {
                            if (_refreshing)
                                return;

                            SettingsCatalog.ApplyToggle(captured, isChecked);
                        });
                    box.tooltip = row.Tooltip;
                    control.Check = box;
                    control.Parts.Add(box);
                    break;
                }

                case RowKind.Key:
                {
                    UIButton button = UIBuilder.AddKeyBinding(page, y, row.Label, row.Key);
                    button.tooltip = row.Tooltip;
                    control.Parts.Add(button);
                    break;
                }

                case RowKind.Colour:
                {
                    control.Colour = new ColourRow(page, y, row);
                    control.Parts.AddRange(control.Colour.Parts);
                    break;
                }

                case RowKind.Choice:
                {
                    // A slider over the choices, with the choice's name as the readout. A
                    // dropdown would need sprite names nobody has read out of the atlas; this
                    // needs none, and three or four positions drag perfectly well.
                    Row captured = row;
                    int count = row.Choices != null ? row.Choices.Length : 1;

                    UISlider slider = UIBuilder.AddSlider(page, y, row.Label, 0f, Mathf.Max(1, count - 1), 1f,
                        row.ChoiceIndex,
                        v => ReadoutText(captured, v),
                        v =>
                        {
                            if (_refreshing || captured.Int == null || captured.ChoiceValues == null)
                                return;

                            int index = Mathf.Clamp(Mathf.RoundToInt(v), 0, captured.ChoiceValues.Length - 1);
                            captured.Int.value = captured.ChoiceValues[index];
                            Changed(captured);
                        });
                    slider.tooltip = row.Tooltip;
                    control.Slider = slider;
                    control.Parts.Add(slider);
                    AddSiblings(page, control, slider);
                    break;
                }

                default:
                {
                    Row captured = row;
                    UISlider slider = UIBuilder.AddSlider(page, y, row.Label, row.Min, row.Max, row.Step,
                        Mathf.Clamp(row.Display, row.Min, row.Max),
                        row.FormatDisplay,
                        v =>
                        {
                            if (_refreshing)
                                return;

                            captured.Store(v);
                            Changed(captured);
                        });
                    slider.tooltip = row.Tooltip;
                    control.Slider = slider;
                    control.Parts.Add(slider);

                    // The label and the readout are siblings of the slider, not children of it.
                    AddSiblings(page, control, slider);
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

        /// <summary>
        /// Picks the name label and the value readout out of the page so the whole row greys
        /// out together. AddSlider makes exactly three components, in that order.
        /// </summary>
        private static void AddSiblings(UIPanel page, Control control, UISlider slider)
        {
            int index = -1;
            for (int i = 0; i < page.components.Count; i++)
            {
                if (page.components[i] == slider)
                {
                    index = i;
                    break;
                }
            }

            if (index <= 0)
                return;

            UILabel name = page.components[index - 1] as UILabel;
            if (name != null)
                control.Parts.Add(name);

            if (index + 1 < page.components.Count)
            {
                UILabel readout = page.components[index + 1] as UILabel;
                if (readout != null)
                {
                    control.Readout = readout;
                    control.Parts.Add(readout);
                }
            }
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
