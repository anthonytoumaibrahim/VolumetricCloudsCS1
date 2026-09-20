using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace VolumetricClouds.UI
{
    /// <summary>
    /// The mod's page in the game's own options, in tabs. Everything that is set once and
    /// forgotten lives here; the in-game panel keeps the overrides and the live look.
    /// </summary>
    /// <remarks>
    /// Three things about this page decide how it has to be written:
    ///
    /// 1. It is REBUILT, not cached. OptionsMainPanel.OnEnable -> RefreshPlugins ->
    ///    CreateCategories destroys every page and calls OnSettingsUI again, and the panel
    ///    also subscribes to eventPluginsChanged / eventPluginsStateChanged. So this runs at
    ///    least once per scene and whenever the player enables ANY mod. Every call starts from
    ///    an empty control list, nothing static holds a control from a previous call, and
    ///    nothing here subscribes to a manager (events on our own controls die with them).
    /// 2. It can run with NO CITY LOADED -- it is reachable from the main menu. Every
    ///    AfterChange in the catalog has to survive that, and so does everything here.
    /// 3. The atlas at the main menu need not be the one the in-game panel's sprite names were
    ///    read from, so the tab sprites are PROBED rather than assumed, and there is a
    ///    fallback to a single scrolling page of groups.
    ///
    /// Rows come from <see cref="SettingsCatalog"/>; the widgets are the game's own UIHelper
    /// factories, which style correctly in both contexts. UIHelper has exactly seven of them
    /// (checkbox, slider, dropdown, textfield, button, space, group) -- no key binding and no
    /// plain label -- so the key row is built by hand and group titles carry any text.
    /// </remarks>
    public class OptionsUI
    {
        /// <summary>The page as it exists right now, or null before the first build.</summary>
        public static OptionsUI Instance { get; private set; }

        private static readonly string[] TabSprites =
        {
            "GenericTab", "GenericTabHovered", "GenericTabPressed", "GenericTabFocused",
        };

        private static readonly OptionsPage[] Tabs =
        {
            OptionsPage.Weather, OptionsPage.Light, OptionsPage.Rendering,
            OptionsPage.Halos, OptionsPage.General,
        };

        private const float TabHeight = 32f;
        private const float FallbackPageHeight = 600f;

        private const string ScrollbarTrack = "ScrollbarTrack";
        private const string ScrollbarThumb = "ScrollbarThumb";

        private static int _buildCount;
        private static bool _probed;
        private static bool _loggedSliderChildren;

        private readonly List<Control> _controls = new List<Control>();
        private UITabstrip _strip;
        private UITabContainer _container;
        private bool _refreshing;

        private class Control
        {
            public Row Source;
            public UISlider Slider;
            public UICheckBox Check;
            public UIDropDown Drop;
            public UIButton Button;
            public UILabel Readout;
            public UIComponent Grey;
            public bool Enabled = true;
        }

        /// <summary>Called from Mod.OnSettingsUI. Builds a whole new page every time.</summary>
        public static void Build(UIHelperBase helper)
        {
            Settings.Init();

            _buildCount++;
            Log.Msg("options page: build #" + _buildCount +
                    " (city " + (ModController.Instance != null ? "loaded" : "not loaded") + ")");

            if (_buildCount == 1)
            {
                Log.ReportLevel();
                SettingsCatalog.LogLayout();
            }

            OptionsUI page = new OptionsUI();
            Instance = page;

            try
            {
                page.BuildPage(helper);
            }
            catch (Exception e)
            {
                Log.Error("Building the options page threw; falling back to one plain page.", e);
                page.DropTabs();
                page.BuildFlat(helper);
            }
        }

        /// <summary>Clears away a half-built tabstrip so the fallback page is not drawn over it.</summary>
        private void DropTabs()
        {
            if (_container != null)
                UnityEngine.Object.Destroy(_container.gameObject);
            if (_strip != null)
                UnityEngine.Object.Destroy(_strip.gameObject);

            _container = null;
            _strip = null;
        }

        private void BuildPage(UIHelperBase helper)
        {
            UIHelper root = helper as UIHelper;
            UIComponent host = root != null ? root.self as UIComponent : null;

            Probe(host);

            if (host == null || !TabSpritesExist())
            {
                Log.Warn("options page: no usable host or tab sprites; using one page of groups instead");
                BuildFlat(helper);
                return;
            }

            UITabstrip strip = host.AddUIComponent<UITabstrip>();
            _strip = strip;
            strip.atlas = UIBuilder.Atlas;
            strip.width = host.width - 20f;
            strip.height = TabHeight;
            strip.padding = new RectOffset(0, 0, 0, 0);

            // The strip and the pages together fill the host exactly, so the game's own scroll
            // panel has nothing to scroll and each TAB scrolls instead, under a strip that
            // stays put. The first version gave the pages a fixed 680 and no scrolling of their
            // own: UITabContainer.ArrangeTabs (read from IL) forces every page to the
            // container's size, so a long tab simply ran off the bottom where nothing could
            // reach it.
            float pageHeight = host.height > 300f ? host.height - TabHeight - 28f : FallbackPageHeight;

            UITabContainer container = host.AddUIComponent<UITabContainer>();
            _container = container;
            container.width = host.width - 20f;
            container.height = pageHeight;
            strip.tabPages = container;

            float tabWidth = (host.width - 20f) / Tabs.Length;

            for (int i = 0; i < Tabs.Length; i++)
            {
                UIButton tab = strip.AddTab(Tabs[i].ToString());
                StyleTab(tab, tabWidth);

                // AddTab creates the page inside the container; it is the i-th child of it.
                UIPanel panel = i < container.components.Count ? container.components[i] as UIPanel : null;
                if (panel == null)
                {
                    Log.Warn("options page: tab '" + Tabs[i] + "' produced no page; using one page of groups instead");
                    DropTabs();
                    BuildFlat(helper);
                    return;
                }

                UIScrollablePanel scroll = AddScrollArea(panel, container.width, pageHeight);
                BuildRows(new UIHelper(scroll), Tabs[i]);
            }

            SelectFirstTab(strip, container);

            Log.Msg("options page: host " + host.width.ToString("F0") + "x" + host.height.ToString("F0") +
                    ", each tab scrolls inside " + container.width.ToString("F0") + "x" + pageHeight.ToString("F0") +
                    "; scrollbar sprites " + (ScrollbarSpritesExist() ? "ok" : "MISSING (mouse wheel only)"));

            host.eventVisibilityChanged += (component, visible) =>
            {
                if (visible)
                    RefreshValues();
            };

            // Not left to the first visibility event: the halo rows, for one, start greyed out.
            RefreshEnabled();

            Log.Msg("options page: built with " + Tabs.Length + " tabs and " + _controls.Count + " controls");
        }

        /// <summary>
        /// Opens the first tab FOR REAL, which "selectedIndex = 0" does not.
        /// </summary>
        /// <remarks>
        /// The bug this fixes looked like a layout fault -- "all the settings are overlapping;
        /// switching to another tab fixes it" -- and was not one: every tab's rows were in
        /// order, and all five PAGES were being drawn at once, on top of each other. Read from
        /// the IL, three things add up to that:
        ///   UITabContainer.AddTabPage(string, UIPanel)  never hides the page it creates (only
        ///       the GameObject overload does);
        ///   UITabContainer.m_SelectedIndex              has no initialiser, so it starts at 0;
        ///   both selectedIndex setters                  return at once when the value is
        ///       unchanged, so selecting 0 never reaches SelectPageByIndex -- the only thing
        ///       that hides pages. The player's first tab switch is the first time it runs.
        /// So the selection is moved away and back, on the strip (which also sets the tab
        /// buttons' states) and then on the container itself, because the strip's own starting
        /// index is not something this should depend on. The pages are then set by hand as
        /// well: it costs nothing, and this bug has already survived one wrong diagnosis.
        /// </remarks>
        private static void SelectFirstTab(UITabstrip strip, UITabContainer container)
        {
            int visibleBefore = VisiblePages(container);

            strip.selectedIndex = -1;
            strip.selectedIndex = 0;

            container.selectedIndex = -1;
            container.selectedIndex = 0;

            for (int i = 0; i < container.components.Count; i++)
                container.components[i].isVisible = i == 0;

            Log.Msg("options page: first tab selected; pages showing before=" + visibleBefore +
                    " after=" + VisiblePages(container) + " of " + container.components.Count);
        }

        private static int VisiblePages(UITabContainer container)
        {
            int visible = 0;

            for (int i = 0; i < container.components.Count; i++)
            {
                if (container.components[i].isVisibleSelf)
                    visible++;
            }

            return visible;
        }

        /// <summary>
        /// A scrolling area filling one tab's page, with its scrollbar down the right-hand edge.
        /// The rows go inside it; the UIHelper factories only need something that lays its
        /// children out vertically, which this does.
        /// </summary>
        /// <remarks>
        /// Two things here are read from the IL of UIScrollablePanel.OnMouseWheel rather than
        /// guessed: the handler returns at once unless builtinKeyNavigation is set (so without
        /// that line the wheel does nothing at all), and the step is the scrollbar's
        /// incrementAmount when one is attached, else scrollWheelAmount. The scrollbar's two
        /// sprite names are in the game's asset data but are still probed against the live
        /// atlas: if they are missing the bar is left out and the wheel still scrolls.
        /// </remarks>
        private static UIScrollablePanel AddScrollArea(UIPanel page, float width, float height)
        {
            const float barWidth = 12f;
            const int wheelStep = 60;

            page.autoLayout = false;
            page.clipChildren = true;

            UIScrollablePanel scroll = page.AddUIComponent<UIScrollablePanel>();
            scroll.relativePosition = Vector3.zero;
            scroll.size = new Vector2(width - barWidth - 6f, height);
            scroll.autoLayout = true;
            scroll.autoLayoutDirection = LayoutDirection.Vertical;
            scroll.autoLayoutPadding = new RectOffset(0, 0, 0, 4);
            scroll.clipChildren = true;
            scroll.builtinKeyNavigation = true;
            scroll.scrollWheelDirection = UIOrientation.Vertical;
            scroll.scrollWheelAmount = wheelStep;

            if (!ScrollbarSpritesExist())
                return scroll;

            UIScrollbar bar = page.AddUIComponent<UIScrollbar>();
            bar.orientation = UIOrientation.Vertical;
            bar.size = new Vector2(barWidth, height);
            bar.relativePosition = new Vector3(width - barWidth, 0f);
            bar.minValue = 0f;
            bar.value = 0f;
            bar.incrementAmount = wheelStep;
            bar.autoHide = true;

            UISlicedSprite track = bar.AddUIComponent<UISlicedSprite>();
            track.atlas = UIBuilder.Atlas;
            track.spriteName = ScrollbarTrack;
            track.relativePosition = Vector3.zero;
            track.size = bar.size;
            track.fillDirection = UIFillDirection.Vertical;
            bar.trackObject = track;

            UISlicedSprite thumb = track.AddUIComponent<UISlicedSprite>();
            thumb.atlas = UIBuilder.Atlas;
            thumb.spriteName = ScrollbarThumb;
            thumb.relativePosition = Vector3.zero;
            thumb.width = barWidth;
            thumb.fillDirection = UIFillDirection.Vertical;
            bar.thumbObject = thumb;

            scroll.verticalScrollbar = bar;
            return scroll;
        }

        private static bool ScrollbarSpritesExist()
        {
            UITextureAtlas atlas = UIBuilder.Atlas;
            return atlas != null && atlas[ScrollbarTrack] != null && atlas[ScrollbarThumb] != null;
        }

        /// <summary>
        /// What ships if the tabs cannot be built: one scrolling page, a group per tab. Not as
        /// tidy, but every setting is still reachable, which is the point.
        /// </summary>
        private void BuildFlat(UIHelperBase helper)
        {
            _controls.Clear();

            foreach (OptionsPage page in Tabs)
            {
                UIHelperBase group = helper.AddGroup(page.ToString());
                BuildRows(group, page);
            }

            UIComponent host = PanelOf(helper);
            if (host != null)
            {
                host.eventVisibilityChanged += (component, visible) =>
                {
                    if (visible)
                        RefreshValues();
                };
            }

            RefreshEnabled();
            Log.Msg("options page: built FLAT (no tabs) with " + _controls.Count + " controls");
        }

        private void BuildRows(UIHelperBase page, OptionsPage which)
        {
            UIHelperBase current = page;
            UIComponent currentPanel = PanelOf(page);

            foreach (Row row in SettingsCatalog.Rows)
            {
                if (row.Options != which)
                    continue;

                if (row.Group != null)
                {
                    current = page.AddGroup(row.Group);
                    currentPanel = PanelOf(current);
                }

                // UIHelper has no plain label, so a row that carries a warning gets a group of
                // its own with the warning as the title -- above the control rather than under
                // it, which is if anything the better place for a sentence you must read.
                if (row.Note != null)
                {
                    current = page.AddGroup(row.Note);
                    currentPanel = PanelOf(current);
                }

                Control control = BuildRow(current, row, currentPanel);
                if (control != null)
                    _controls.Add(control);
            }
        }

        private static UIComponent PanelOf(UIHelperBase helper)
        {
            UIHelper concrete = helper as UIHelper;
            return concrete != null ? concrete.self as UIComponent : null;
        }

        private Control BuildRow(UIHelperBase helper, Row row, UIComponent groupPanel)
        {
            Control control = new Control { Source = row };

            switch (row.Kind)
            {
                case RowKind.Status:
                    // Status lines are live text; the panel has them, this page does not.
                    return null;

                case RowKind.Toggle:
                {
                    Row captured = row;
                    UICheckBox box = helper.AddCheckbox(row.Label, row.Bool != null && row.Bool.value,
                        isChecked =>
                        {
                            if (_refreshing)
                                return;

                            SettingsCatalog.ApplyToggle(captured, isChecked);
                        }) as UICheckBox;

                    if (box == null)
                        return null;

                    box.tooltip = row.Tooltip;
                    control.Check = box;
                    control.Grey = box;
                    break;
                }

                case RowKind.Button:
                {
                    Row captured = row;
                    UIButton button = helper.AddButton(row.Label, () =>
                    {
                        if (captured.OnClick == null)
                            return;

                        captured.OnClick();
                        SettingsCatalog.RefreshAllUIs();
                    }) as UIButton;

                    if (button == null)
                        return null;

                    button.tooltip = row.Tooltip;
                    control.Button = button;
                    control.Grey = button;
                    break;
                }

                case RowKind.Key:
                    control.Button = AddKeyRow(helper, row);
                    control.Grey = control.Button;
                    if (control.Button == null)
                        return null;
                    break;

                case RowKind.Choice:
                {
                    Row captured = row;
                    UIDropDown drop = helper.AddDropdown(row.Label, row.Choices, row.ChoiceIndex,
                        index =>
                        {
                            if (_refreshing || captured.Int == null || captured.ChoiceValues == null)
                                return;

                            if (index < 0 || index >= captured.ChoiceValues.Length)
                                return;

                            captured.Int.value = captured.ChoiceValues[index];
                            if (captured.AfterChange != null)
                                captured.AfterChange();

                            SettingsCatalog.RefreshAllUIs();
                        }) as UIDropDown;

                    if (drop == null)
                        return null;

                    drop.tooltip = row.Tooltip;
                    control.Drop = drop;
                    control.Grey = RowPanel(drop, groupPanel);
                    break;
                }

                default:
                {
                    Row captured = row;
                    float value = Mathf.Clamp(row.Display, row.Min, row.Max);

                    UISlider slider = helper.AddSlider(row.Label, row.Min, row.Max, row.Step, value,
                        v =>
                        {
                            if (_refreshing)
                                return;

                            captured.Store(v);
                            UpdateReadout(captured, v);

                            // A row with a live-apply hook may have moved OTHER rows (dragging
                            // Quality flips the preset to Custom), so both UIs look again.
                            if (captured.AfterChange != null)
                            {
                                captured.AfterChange();
                                SettingsCatalog.RefreshAllUIs();
                            }
                        }) as UISlider;

                    if (slider == null)
                        return null;

                    slider.tooltip = row.Tooltip;
                    control.Slider = slider;
                    control.Readout = FindSliderLabel(slider, row);
                    control.Grey = RowPanel(slider, groupPanel);

                    // The UIHelper slider has no readout of its own, so the row's own label
                    // carries the value: "Quality (raymarch steps): 96".
                    if (control.Readout != null)
                        control.Readout.text = row.Label + ": " + row.FormatDisplay(value);
                    break;
                }
            }

            return control;
        }

        /// <summary>
        /// Greys the whole row where the template gave the control a panel of its own, and
        /// just the control where it did not -- greying a group's own panel would take every
        /// other row in the group with it.
        /// </summary>
        private static UIComponent RowPanel(UIComponent control, UIComponent groupPanel)
        {
            UIComponent parent = control.parent;
            return parent != null && parent != groupPanel ? parent : control;
        }

        private void UpdateReadout(Row row, float display)
        {
            foreach (Control control in _controls)
            {
                if (control.Source == row && control.Readout != null)
                    control.Readout.text = row.Label + ": " + row.FormatDisplay(display);
            }
        }

        /// <summary>
        /// The slider's caption. Its name is not relied on: it is the first UILabel beside the
        /// slider, and the first time round every child's name goes in the log so the guess can
        /// be checked against the game.
        /// </summary>
        private static UILabel FindSliderLabel(UISlider slider, Row row)
        {
            UIComponent parent = slider.parent;
            if (parent == null)
                return null;

            UILabel found = null;
            string names = string.Empty;

            for (int i = 0; i < parent.components.Count; i++)
            {
                UIComponent child = parent.components[i];
                names += (names.Length == 0 ? "" : ", ") + child.name + ":" + child.GetType().Name;

                if (found == null)
                    found = child as UILabel;
            }

            if (!_loggedSliderChildren)
            {
                _loggedSliderChildren = true;
                Log.Msg("options page: slider '" + row.Label + "' parent=" + parent.name +
                        " (" + parent.GetType().Name + ") children=[" + names + "] label=" +
                        (found == null ? "NOT FOUND" : found.name));
            }

            return found;
        }

        /// <summary>
        /// A key-binding row, which UIHelper cannot make. A plain options button that captures
        /// the next key press: Escape cancels, Backspace unbinds. Modal while it listens, so
        /// the key being bound does not also fire as a game hotkey.
        /// </summary>
        private static UIButton AddKeyRow(UIHelperBase helper, Row row)
        {
            SavedInputKey key = row.Key;
            if (key == null)
                return null;

            // The click is handled by eventMouseDown below; UIHelper calls its callback with no
            // arguments and a null one would throw, so it gets an empty delegate.
            UIButton button = helper.AddButton(row.Label + ":  " + key.ToLocalizedString("KEYNAME"),
                () => { }) as UIButton;
            if (button == null)
                return null;

            button.tooltip = row.Tooltip;

            bool editing = false;

            button.eventMouseDown += (component, e) =>
            {
                if (editing)
                    return;

                e.Use();
                editing = true;
                button.buttonsMask = UIMouseButton.Left | UIMouseButton.Right | UIMouseButton.Middle;
                button.text = row.Label + ":  press a key";
                button.Focus();
                UIView.PushModal(button);
            };

            button.eventKeyDown += (component, e) =>
            {
                if (!editing)
                    return;

                e.Use();

                if (e.keycode == KeyCode.Backspace)
                    key.value = SavedInputKey.Encode(KeyCode.None, false, false, false);
                else if (IsModifier(e.keycode))
                    return;
                else if (e.keycode != KeyCode.Escape)
                    key.value = SavedInputKey.Encode(e.keycode, e.control, e.shift, e.alt);

                editing = false;
                UIView.PopModal();
                button.buttonsMask = UIMouseButton.Left;
                button.text = row.Label + ":  " + key.ToLocalizedString("KEYNAME");
                GameSettings.SaveAll();
            };

            return button;
        }

        private static bool IsModifier(KeyCode code)
        {
            return code == KeyCode.LeftControl || code == KeyCode.RightControl
                || code == KeyCode.LeftShift || code == KeyCode.RightShift
                || code == KeyCode.LeftAlt || code == KeyCode.RightAlt;
        }

        private static void StyleTab(UIButton tab, float width)
        {
            if (tab == null)
                return;

            tab.atlas = UIBuilder.Atlas;
            tab.size = new Vector2(width, TabHeight);
            tab.autoSize = false;
            tab.textScale = 0.85f;
            tab.textPadding = new RectOffset(4, 4, 8, 4);
            tab.normalBgSprite = "GenericTab";
            tab.hoveredBgSprite = "GenericTabHovered";
            tab.pressedBgSprite = "GenericTabPressed";
            tab.focusedBgSprite = "GenericTabFocused";
            tab.disabledBgSprite = "GenericTab";
            tab.textColor = UIBuilder.StatusColour;
            tab.hoveredTextColor = UIBuilder.HeadingColour;
            tab.focusedTextColor = UIBuilder.HeadingColour;
            tab.pressedTextColor = UIBuilder.HeadingColour;
        }

        private static bool TabSpritesExist()
        {
            UITextureAtlas atlas = UIBuilder.Atlas;
            if (atlas == null)
                return false;

            foreach (string sprite in TabSprites)
            {
                if (atlas[sprite] == null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// What the page is actually being built into. Logged once, because the options page is
        /// reachable from the main menu, where none of the in-game panel's assumptions hold.
        /// </summary>
        private static void Probe(UIComponent host)
        {
            if (_probed)
                return;

            _probed = true;

            UITextureAtlas atlas = UIBuilder.Atlas;
            string sprites = string.Empty;

            foreach (string sprite in TabSprites)
            {
                sprites += (sprites.Length == 0 ? "" : ", ") + sprite + "=" +
                           (atlas != null && atlas[sprite] != null ? "ok" : "MISSING");
            }

            Log.Msg("options page probe: atlas='" + (atlas == null ? "none" : atlas.name) +
                    "' host=" + (host == null ? "none" : host.GetType().FullName + " width=" + host.width.ToString("F0")) +
                    " | tab sprites: " + sprites);
        }

        /// <summary>
        /// Re-reads every control. The same settings can be changed from the in-game panel, and
        /// the controls go stale between the page's own rebuilds.
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

                    if (control.Slider != null && row.Float != null)
                    {
                        float display = Mathf.Clamp(row.Display, row.Min, row.Max);
                        if (!Mathf.Approximately(control.Slider.value, display))
                        {
                            control.Slider.value = display;
                            touched++;
                        }

                        if (control.Readout != null)
                            control.Readout.text = row.Label + ": " + row.FormatDisplay(display);
                    }

                    if (control.Check != null && row.Bool != null && !row.Pending
                        && control.Check.isChecked != row.Bool.value)
                    {
                        control.Check.isChecked = row.Bool.value;
                        touched++;
                    }

                    if (control.Drop != null && row.Int != null && control.Drop.selectedIndex != row.ChoiceIndex)
                    {
                        control.Drop.selectedIndex = row.ChoiceIndex;
                        touched++;
                    }

                    if (control.Source.Kind == RowKind.Key && control.Button != null && row.Key != null)
                        control.Button.text = row.Label + ":  " + row.Key.ToLocalizedString("KEYNAME");
                }
            }
            finally
            {
                _refreshing = false;
            }

            RefreshEnabled();

            if (touched > 0 && Log.Detailed)
                Log.Detail("options page: refreshed " + _controls.Count + " controls, " + touched + " had changed");
        }

        private void RefreshEnabled()
        {
            foreach (Control control in _controls)
            {
                bool wanted = control.Source.IsEnabled;
                if (wanted == control.Enabled || control.Grey == null)
                    continue;

                control.Enabled = wanted;
                control.Grey.isEnabled = wanted;
                control.Grey.opacity = wanted ? 1f : 0.45f;
            }
        }
    }
}
