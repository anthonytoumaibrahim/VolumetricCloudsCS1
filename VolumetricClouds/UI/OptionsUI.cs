using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace VolumetricClouds.UI
{
    /// <summary>
    /// The mod's page in the game's own options: the mod itself (language, the panel's key and
    /// its advanced tabs, the button, diagnostics, the resets). Every setting of the SKY is in
    /// the in-game panel (<see cref="CloudsPanel"/>), where it can be judged against the sky.
    /// </summary>
    /// <remarks>
    /// Until 1.1.0 this page had five tabs (Weather, Light, Rendering, Halos, General) and the
    /// panel borrowed them behind "Show advanced options". The author's call: one place to
    /// change the sky, the panel. The four sky tabs are now the panel's advanced tabs only.
    ///
    /// Two things about this page decide how it has to be written:
    ///
    /// 1. It is REBUILT, not cached. OptionsMainPanel.OnEnable -> RefreshPlugins ->
    ///    CreateCategories destroys every page and calls OnSettingsUI again, and the panel
    ///    also subscribes to eventPluginsChanged / eventPluginsStateChanged. So this runs at
    ///    least once per scene and whenever the player enables ANY mod. Every call starts from
    ///    an empty control list, nothing static holds a control from a previous call, and
    ///    nothing here subscribes to a manager (events on our own controls die with them).
    /// 2. It can run with NO CITY LOADED -- it is reachable from the main menu. Every
    ///    AfterChange in the catalog has to survive that, and so does everything here.
    ///
    /// Rows come from <see cref="SettingsCatalog"/>; the widgets are the game's own UIHelper
    /// factories, which style correctly in both contexts. UIHelper has exactly seven of them
    /// (checkbox, slider, dropdown, textfield, button, space, group) -- no key binding and no
    /// plain label -- so the key row is built by hand and group titles carry any text.
    /// The page is short enough for the game's own scroll panel; the tabbed version needed a
    /// scroll area per tab (UITabContainer forces every page to its own size) -- in git history.
    /// </remarks>
    public class OptionsUI
    {
        /// <summary>The page as it exists right now, or null before the first build.</summary>
        public static OptionsUI Instance { get; private set; }

        private static int _buildCount;
        private static bool _loggedSliderChildren;

        private readonly List<Control> _controls = new List<Control>();
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
                Log.Error("Building the options page threw.", e);
            }
        }

        /// <summary>A warning the player should read: a warm yellow.</summary>
        private static readonly Color32 WarningColour = new Color32(255, 213, 74, 255);

        private void BuildPage(UIHelperBase helper)
        {
            BuildRows(helper, OptionsPage.General);

            UIComponent host = PanelOf(helper);

            // Mac and Linux (1.1.1): one warning at the BOTTOM of the page, in yellow, wrapping.
            // Not a group title (this page's usual way to show prose, see BuildRows): a title
            // is one line and was cut off at the page's edge. The author's call, 2026-09-24:
            // no dialog, the Workshop page carries the warning, and this line does here.
            // Only where the mod can run at all: a platform with no bundle, or one that has
            // already stood down in this launch, gets the stand-down message instead.
            bool experimental = host != null && Loader.IsExperimentalPlatform &&
                                Sky.ShaderBundle.BundleFor(Application.platform) != null &&
                                !Loader.HasStoodDown;
            if (experimental)
            {
                try
                {
                    AddPlatformWarning(host);
                }
                catch (Exception e)
                {
                    // A warning label must never cost the page its refresh hook below.
                    Log.Error("Could not add the platform warning to the options page.", e);
                }
            }

            if (host != null)
            {
                host.eventVisibilityChanged += (component, visible) =>
                {
                    if (visible)
                        RefreshValues();
                };
            }

            // Not left to the first visibility event: "Reset cloud pattern" starts greyed out
            // at the main menu.
            RefreshEnabled();

            if (_buildCount == 1)
                Log.Msg("options page: built with " + _controls.Count + " controls");
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
                    current = page.AddGroup(row.GroupTitle);
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

        /// <summary>
        /// A UILabel of our own on the page's scroll panel, which lays its children out top to
        /// bottom, so a label added after every row lands last. Fixed width, auto height,
        /// word wrap: the three together are what makes it wrap instead of running off the
        /// page. The width is logged once, with the height the text got, so a Mac log shows
        /// whether it wrapped.
        /// </summary>
        private static void AddPlatformWarning(UIComponent host)
        {
            UIScrollablePanel scroll = host as UIScrollablePanel;
            RectOffset layoutPadding = scroll != null && scroll.autoLayoutPadding != null
                ? scroll.autoLayoutPadding
                : new RectOffset();

            UILabel label = host.AddUIComponent<UILabel>();
            label.name = "VolumetricCloudsPlatformWarning";
            label.autoSize = false;
            label.autoHeight = true;
            label.wordWrap = true;
            label.width = host.width - layoutPadding.horizontal - 24f;
            label.padding = new RectOffset(8, 8, 12, 8);
            label.textColor = WarningColour;
            label.text = Localization.Get("Mod.ExperimentalPlatformNote");

            if (_buildCount == 1)
            {
                Log.Msg("options page: platform warning added at the bottom (page " + host.width +
                        " wide, layout padding " + layoutPadding.horizontal + "; label " + label.width + " wide)");

                // The height is only known once the label has laid its text out, which is
                // after this call returns: log it then, once. One line of text is ~20 units,
                // so a height of 60-80 says it wrapped and a height of 20 says it did not.
                label.eventSizeChanged += (component, size) =>
                {
                    if (_warningSizeLogged)
                        return;
                    _warningSizeLogged = true;
                    Log.Msg("options page: platform warning laid out at " + size.x + " x " + size.y);
                };
            }
        }

        private static bool _warningSizeLogged;

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
                Log.Msg("options page: slider '" + row.Name + "' parent=" + parent.name +
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
                button.text = row.Label + ":  " + Localization.Get("Key.Press");
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
                SettingsXml.SaveNow();
            };

            return button;
        }

        private static bool IsModifier(KeyCode code)
        {
            return code == KeyCode.LeftControl || code == KeyCode.RightControl
                || code == KeyCode.LeftShift || code == KeyCode.RightShift
                || code == KeyCode.LeftAlt || code == KeyCode.RightAlt;
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
