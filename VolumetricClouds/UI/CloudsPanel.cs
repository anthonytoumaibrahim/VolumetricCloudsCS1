using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace VolumetricClouds.UI
{
    /// <summary>
    /// The in-game panel: what you set while looking at the sky.
    /// </summary>
    /// <remarks>
    /// By default it has three tabs, and the split from the options page is by KIND rather
    /// than by subject.
    ///   Now    -- every override in the mod, plus the two status lines. Photo mode.
    ///   Clouds -- the shape of the sky.
    ///   Fog    -- the fog, which is a cloud layer lying on the ground.
    /// Everything that is set once and forgotten -- the weather mapping, the light, the
    /// rendering, the halos, the mod itself -- lives in the game's own options page
    /// (<see cref="OptionsUI"/>), which is what a subscriber who installs and forgets expects.
    ///
    /// "Show advanced options in the in-game panel" (Options -> General) adds the options
    /// page's five tabs HERE as well, for the player who tunes the mod against the sky the way
    /// it was built. Nothing moves: the options page always has everything, and a row that is
    /// already on one of the three basic tabs is not repeated on an advanced one.
    ///
    /// Both UIs are drawn from <see cref="SettingsCatalog"/>, so where a row lives is a
    /// one-word edit rather than a second copy of the row.
    /// </remarks>
    public class CloudsPanel : UIPanel
    {
        private const float BasicWidth = 600f;
        private const float AdvancedWidth = 680f;   // eight tabs; "Rendering" is the widest label
        private const float PanelHeight = 408f;
        private const float TitleBarHeight = 40f;
        private const float TabHeight = 28f;
        private const float Margin = 14f;

        private const float HeadingHeight = 24f;
        private const float NoteHeight = 32f;

        private const float MinContentHeight = PanelHeight - TitleBarHeight - TabHeight - 2f * Margin;

        private static readonly PanelPage[] BasicTabs = { PanelPage.Now, PanelPage.Clouds, PanelPage.Fog };

        private static readonly OptionsPage[] AdvancedTabs =
        {
            OptionsPage.Weather, OptionsPage.Light, OptionsPage.Rendering,
            OptionsPage.Halos, OptionsPage.General,
        };

        /// <summary>One tab: a basic page of the panel's own, or one borrowed from the options page.</summary>
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
                return row.Options == Options && row.Panel == PanelPage.None && !row.OptionsOnly;
            }
        }

        /// <summary>Where to open, when replacing a panel that was already on screen. Null = centred.</summary>
        public Vector3? InitialPosition;

        private readonly List<UIButton> _tabs = new List<UIButton>();
        private readonly List<UIPanel> _pages = new List<UIPanel>();
        private readonly List<float> _contentHeights = new List<float>();
        private readonly List<Control> _controls = new List<Control>();

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
            public readonly List<UIComponent> Parts = new List<UIComponent>();
            public bool Enabled = true;
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

            foreach (Tab tab in Tabs(advanced))
                _contentHeights.Add(BuildPage(AddPage(tab.Name), tab));

            LayoutTabs();
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

                    // Not while its confirmation is open: the value is still the old one, and
                    // this would untick the box the player has just ticked.
                    if (control.Check != null && row.Bool != null && !row.Pending
                        && control.Check.isChecked != row.Bool.value)
                    {
                        control.Check.isChecked = row.Bool.value;
                        touched++;
                    }
                }
            }
            finally
            {
                _refreshing = false;
            }

            RefreshEnabled();

            if (touched > 0 && Log.Detailed)
                Log.Detail("panel: refreshed " + _controls.Count + " controls, " + touched + " had changed");
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
            UILabel title = UIBuilder.AddLabel(this, "Volumetric Clouds", new Vector3(Margin, Margin), 1.1f);
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

            // Dragging the title bar moves the whole panel.
            UIDragHandle drag = AddUIComponent<UIDragHandle>();
            drag.target = this;
            drag.size = new Vector2(_width - 40f, TitleBarHeight);
            drag.relativePosition = Vector3.zero;
        }

        private UIPanel AddPage(string tabName)
        {
            int index = _pages.Count;

            UIButton tab = UIBuilder.AddTab(this, tabName, new Vector2(10f, TabHeight), Vector3.zero);
            tab.eventClick += (component, e) => SelectPage(index);
            _tabs.Add(tab);

            UIPanel page = AddUIComponent<UIPanel>();
            page.size = new Vector2(_width - 2f * Margin, MinContentHeight);
            page.relativePosition = new Vector3(Margin, TitleBarHeight + TabHeight + Margin);
            _pages.Add(page);

            return page;
        }

        private void LayoutTabs()
        {
            float width = (_width - 2f * Margin) / _tabs.Count;

            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].size = new Vector2(width, TabHeight);
                _tabs[i].relativePosition = new Vector3(Margin + i * width, TitleBarHeight + 2f);
            }
        }

        private void SelectPage(int index)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].isVisible = i == index;
                UIBuilder.SetTabSelected(_tabs[i], i == index);
            }

            // The panel is as tall as the selected page needs, and never shorter than it
            // started: most pages fit, the Halos one has half as many rows again.
            float content = Mathf.Max(MinContentHeight, _contentHeights[index]);
            _pages[index].height = content;
            height = TitleBarHeight + TabHeight + 2f * Margin + content;
        }

        /// <summary>Draws every catalog row that belongs on this tab, in catalog order.</summary>
        private float BuildPage(UIPanel page, Tab tab)
        {
            float y = 0f;

            foreach (Row row in SettingsCatalog.Rows)
            {
                if (!tab.Shows(row))
                    continue;

                if (row.Group != null)
                {
                    UIBuilder.AddHeading(page, row.GroupTitle, y + 4f);
                    y += HeadingHeight;
                }

                Control control = Build(page, row, y);
                _controls.Add(control);
                y += UIBuilder.RowHeight;

                if (row.Note != null)
                {
                    UILabel note = UIBuilder.AddNote(page, row.Note, y - 4f, NoteHeight, UIBuilder.WarningColour);
                    control.Parts.Add(note);
                    y += NoteHeight;
                }
            }

            return y;
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
                Mathf.Floor((view.fixedHeight - PanelHeight) / 2f));
        }

        public override void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            base.OnDestroy();
        }
    }
}
