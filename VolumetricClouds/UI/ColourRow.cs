using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.UI;
using ModsLibCS1.UI;
using UnityEngine;

namespace VolumetricClouds.UI
{
    /// <summary>
    /// One colour row of the in-game panel:
    ///   name | swatch | #RRGGBB field | Copy | Paste | Default
    /// </summary>
    /// <remarks>
    /// The swatch is the game's OWN colour field, cloned from the transport-line template
    /// (PublicTransportLineInfo's "LineColor" in "LineTemplate"), so clicking it opens the game's
    /// own picker. If that template cannot be found the row still works -- field, buttons and
    /// reset -- and the probe line in the log says so. The field reads every spelling
    /// <see cref="ColorText"/> does; the game's text fields already do Ctrl+C / Ctrl+V (IL of
    /// UITextField.OnKeyDown), and Copy / Paste use the same clipboard (ColossalFramework.Clipboard).
    ///
    /// Each pick is logged once, when it is made -- on the picker's mouse-up, Enter, Paste or
    /// reset -- not on every frame of a drag through the picker.
    /// </remarks>
    internal sealed class ColourRow
    {
        private const float SwatchSize = 26f;
        private const float TextWidth = 116f;
        private const float ButtonWidth = 62f;
        private const float Gap = 6f;

        private const string LineTemplate = "LineTemplate";
        private const string LineColour = "LineColor";

        /// <summary>How long a colour the row could not read stays on show, in red.</summary>
        private const float InvalidShownFor = 3f;
        private const float CopiedShownFor = 1.5f;

        private static bool _probed;

        private readonly Builder _ui;
        private readonly Row _row;
        private readonly UILabel _name;
        private readonly UIColorField _swatch;
        private readonly UITextField _text;
        private readonly UIButton _copy;
        private readonly UIButton _reset;
        private readonly List<UIComponent> _parts = new List<UIComponent>();
        private readonly List<UIComponent> _valueParts = new List<UIComponent>();

        /// <summary>
        /// The values this row stands for (1.5.0), when more than its own: the own line of a colour row
        /// with a clock while they are all the same and its buttons are closed (every value), a Day line
        /// while its clear and overcast colours are the same (both), a Clear sky line (its three times).
        /// A pick goes to all of them, so they stay one colour, and the reset puts them all back
        /// (SettingsCatalog.ResetRows). Null: the row's own colour only.
        /// </summary>
        public IList<Row> Targets;

        private Color32 _shown;
        private string _logged;
        private bool _refreshing;
        private float _invalidUntil = -1f;
        private float _copiedUntil = -1f;

        /// <param name="label">The name to write; null: the row's own (a clock's lines write Day, Sunset, Night).</param>
        public ColourRow(Builder ui, UIComponent parent, float y, Row row, string label)
        {
            _ui = ui;
            _row = row;
            _shown = Current;
            _logged = ColorText.Format(_shown);
            string shownName = label ?? row.Label;

            // The page's names' column (CloudsPanel widens it for long names).
            UILabel name = _ui.AddName(parent, shownName, new Vector3(0f, y + 8f), _ui.LabelWidth);
            _parts.Add(name);
            _name = name;

            // The parts end where every row's controls end (before the reset). Beside a wide names'
            // column (the Clouds tab's, in the narrower panel) they reach left into it as far as the
            // row's own name leaves room, as a choice row's buttons do: past it, Paste ran under the
            // reset icon and off the page.
            const float parts = SwatchSize + Gap + TextWidth + Gap + ButtonWidth + Gap + ButtonWidth;
            float column = _ui.LabelWidth + 4f;
            float x = Mathf.Max(Mathf.Min(column, parent.width - _ui.ResetColumn - parts),
                _ui.NameWidth(shownName) + ChoiceFit.NameRoom);
            if (x < column)
                name.width = x - 4f;

            _swatch = CreateSwatch(parent, new Vector3(x, y + 4f));
            if (_swatch != null)
            {
                // The clone still holds the template's colour; Refresh only writes on a change.
                _swatch.selectedColor = _shown;
                _swatch.eventSelectedColorChanged += (component, colour) =>
                {
                    if (!_refreshing)
                        Store(colour);
                };
                _swatch.eventSelectedColorReleased += (component, colour) =>
                {
                    if (_refreshing)
                        return;

                    Store(colour);
                    Commit("picked");
                };
                _parts.Add(_swatch);
                _valueParts.Add(_swatch);
            }

            x += SwatchSize + Gap;

            _text = _ui.AddTextField(parent, new Vector2(TextWidth, 26f), new Vector3(x, y + 4f), 64);
            _text.text = ColorText.Format(_shown);
            _text.eventTextSubmitted += (component, text) => Submit(text);
            _text.eventTextCancelled += (component, text) => ShowValid();
            _parts.Add(_text);
            _valueParts.Add(_text);

            x += TextWidth + Gap;

            _copy = _ui.AddButton(parent, Localization.Get("Colour.Copy"), new Vector2(ButtonWidth, 26f), new Vector3(x, y + 4f));
            _copy.canFocus = false;
            _copy.eventClick += (component, e) => Copy();
            _parts.Add(_copy);
            _valueParts.Add(_copy);

            x += ButtonWidth + Gap;

            UIButton paste = _ui.AddButton(parent, Localization.Get("Colour.Paste"), new Vector2(ButtonWidth, 26f), new Vector3(x, y + 4f));
            paste.canFocus = false;
            paste.eventClick += (component, e) => Paste();
            _parts.Add(paste);
            _valueParts.Add(paste);

            // The row's reset: the same icon as every other row's (1.5.0; before, a "Default" text
            // button), right-aligned, going back to the same place: the picked profile's colour, or
            // the default without one. Not in Parts: it fades on its own rule (already there).
            _reset = _ui.AddReset(parent, y, row.ResetTooltip);
            if (_reset != null)
            {
                _reset.eventClick += (component, e) =>
                {
                    // It logs the change itself; Refresh shows it.
                    if (Targets != null)
                        SettingsCatalog.ResetRows(_row, Targets);
                    else
                        SettingsCatalog.ResetRow(_row);
                    _logged = ColorText.Format(Current);
                };
            }

            // The faint line under the row, in the looks that draw one.
            _ui.AddRowLine(parent, y);

            Probe();
            Refresh();
        }

        /// <summary>Everything that greys out with the row.</summary>
        public IList<UIComponent> Parts
        {
            get { return _parts; }
        }

        /// <summary>The swatch, the text and the two buttons: what a clock's own line hides while its times differ.</summary>
        public IList<UIComponent> ValueParts
        {
            get { return _valueParts; }
        }

        /// <summary>Its name, which a clock's lines move along to make room for their time's icon.</summary>
        public UILabel Name
        {
            get { return _name; }
        }

        /// <summary>Its reset button (null without the library's sheet).</summary>
        public UIButton Reset
        {
            get { return _reset; }
        }

        private Color32 Current
        {
            get { return _row.Colour != null ? _row.Colour.value : _row.DefaultColour; }
        }

        /// <summary>
        /// Re-reads the setting (the panel calls this four times a second): a reset, a hand edit
        /// of VolumetricClouds.xml or the other UI may have changed it. Writes only what changed,
        /// and never over text the player is typing.
        /// </summary>
        public void Refresh()
        {
            _refreshing = true;
            try
            {
                Color32 current = Current;
                if (!ColorText.Same(current, _shown))
                {
                    _shown = current;
                    if (_swatch != null)
                        _swatch.selectedColor = current;
                    if (!_text.hasFocus && _invalidUntil < 0f)
                        ShowValid();
                }

                float now = Time.realtimeSinceStartup;
                if (_invalidUntil >= 0f && now > _invalidUntil && !_text.hasFocus)
                    ShowValid();

                if (_copiedUntil >= 0f && now > _copiedUntil)
                {
                    _copiedUntil = -1f;
                    _copy.text = _ui.Caps(Localization.Get("Colour.Copy"));
                }

                Builder.SetResetEnabled(_reset, ResetWanted);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void Store(Color32 colour)
        {
            if (_row.Colour == null)
                return;

            _row.Colour.value = colour;
            _shown = _row.Colour.value;

            // One colour for all it stands for: the others go with it.
            if (Targets != null)
            {
                foreach (Row value in Targets)
                {
                    if (value.Colour != null)
                        value.Colour.value = colour;
                }
            }

            _refreshing = true;
            try
            {
                if (_swatch != null && !ColorText.Same((Color32)_swatch.selectedColor, _shown))
                    _swatch.selectedColor = _shown;
                if (!_text.hasFocus)
                    ShowValid();
            }
            finally
            {
                _refreshing = false;
            }

            Builder.SetResetEnabled(_reset, ResetWanted);
        }

        /// <summary>
        /// The reset is not in Parts (it greys out on its own rule: already where it goes back to),
        /// so it follows the row's own switch here -- the fog's colour greys out with the fog.
        /// </summary>
        private bool ResetWanted
        {
            get { return (Targets != null ? !SettingsCatalog.AtResetValue(Targets) : !_row.IsAtResetValue) && _row.IsEnabled; }
        }

        /// <summary>Its reset's tooltip, which follows the profile pick (the panel sets it).</summary>
        public string ResetTooltip
        {
            set
            {
                if (_reset != null)
                    _reset.tooltip = value;
            }
        }

        /// <summary>One log line per pick, and only when the colour really moved.</summary>
        private void Commit(string how)
        {
            string hex = ColorText.Format(Current);
            if (hex == _logged)
                return;

            _logged = hex;
            Log.Msg("setting: " + _row.Name + " = " + hex + " (" + how + (Targets != null ? ", with " + Others() : "") + ")");
        }

        /// <summary>For the log: the other values a pick went to.</summary>
        private string Others()
        {
            var names = new List<string>();
            foreach (Row value in Targets)
            {
                if (value != _row)
                    names.Add(value.Name);
            }

            return names.Count > 0 ? string.Join(", ", names.ToArray()) : "nothing else";
        }

        private void Submit(string text)
        {
            if (_refreshing)
                return;

            Color32 colour;
            if (ColorText.TryParse(text, out colour))
            {
                Store(colour);
                Commit("typed");
                ShowValid();
                return;
            }

            // The text may be the colour already on show (Enter on an untouched field).
            if (text == ColorText.Format(_shown))
                return;

            ShowInvalid(text);
        }

        private void Copy()
        {
            try
            {
                Clipboard.text = ColorText.Format(Current);
                _copy.text = _ui.Caps(Localization.Get("Colour.Copied"));
                _copiedUntil = Time.realtimeSinceStartup + CopiedShownFor;
            }
            catch (Exception e)
            {
                Log.Warn("colour: could not copy to the clipboard: " + e.Message);
            }
        }

        private void Paste()
        {
            string text;
            try
            {
                text = Clipboard.text;
            }
            catch (Exception e)
            {
                Log.Warn("colour: could not read the clipboard: " + e.Message);
                return;
            }

            Color32 colour;
            if (ColorText.TryParse(text, out colour))
            {
                Store(colour);
                Commit("pasted");
                ShowValid();
            }
            else
            {
                ShowInvalid(text ?? string.Empty);
            }
        }

        private void ShowValid()
        {
            _invalidUntil = -1f;
            _text.text = ColorText.Format(_shown);
            _text.textColor = _ui.Theme.FieldText;
        }

        /// <summary>What could not be read stays on show in red for a moment, then goes back.</summary>
        private void ShowInvalid(string text)
        {
            string shown = text.Trim();
            if (shown.Length > 40)
                shown = shown.Substring(0, 40);

            _text.text = shown;
            _text.textColor = _ui.Theme.Invalid;
            _invalidUntil = Time.realtimeSinceStartup + InvalidShownFor;
        }

        /// <summary>The game's own colour field, cloned out of the transport-line template.</summary>
        internal static UIColorField CreateSwatch(UIComponent parent, Vector3 position)
        {
            try
            {
                UIComponent template = UITemplateManager.Peek(LineTemplate);
                UIColorField source = template != null ? template.Find<UIColorField>(LineColour) : null;
                if (source == null)
                    return null;

                GameObject copy = UnityEngine.Object.Instantiate(source.gameObject);
                copy.SetActive(true);

                UIColorField field = parent.AttachUIComponent(copy) as UIColorField;
                if (field == null)
                {
                    UnityEngine.Object.Destroy(copy);
                    return null;
                }

                // The template's field arrives anchored Left | Right | CenterVertical (logged
                // 2026-09-23), and an anchored control follows its parent's size: each time a
                // tab page changed height the swatch was re-centred and stretched with it --
                // "the colour picker goes out of place and the second one disappears". A
                // swatch sits where it was put.
                field.anchor = UIAnchorStyle.None;

                field.name = "VolumetricCloudsColour";
                field.size = new Vector2(SwatchSize, SwatchSize);
                field.relativePosition = position;
                field.isVisible = true;
                field.pickerPosition = UIColorField.ColorPickerPosition.RightBelow;
                field.popupTopmostRoot = true;
                return field;
            }
            catch (Exception e)
            {
                Log.Warn("colour: the game's colour field could not be cloned (" + e.GetType().Name + ": " + e.Message +
                         "); the row works without its swatch");
                return null;
            }
        }


        /// <summary>Once per launch: whether the game's picker was found, so a plain-looking row explains itself.</summary>
        private void Probe()
        {
            if (_probed)
                return;

            _probed = true;
            Log.Msg("colour rows: the game's colour field (" + LineTemplate + "/" + LineColour + ") " +
                    (_swatch != null ? "cloned, picker " + (_swatch.colorPicker != null ? "ok" : "MISSING") : "NOT FOUND -- text field only"));
        }
    }

    /// <summary>
    /// One colour in the panel's table (1.5.0, a row's Clear sky and Overcast side by side at a time of
    /// day): the game's swatch and the #RRGGBB field, no name, no buttons -- the table's line has the
    /// reset. A pick is logged once, as ColourRow does.
    /// </summary>
    internal sealed class ColourCell
    {
        private const float SwatchSize = 26f;
        private const float Gap = 6f;

        private readonly Builder _ui;
        private readonly Row _row;
        private readonly UIColorField _swatch;
        private readonly UITextField _text;
        private readonly List<UIComponent> _parts = new List<UIComponent>();
        private Color32 _shown;
        private string _logged;
        private bool _refreshing;

        public ColourCell(Builder ui, UIComponent parent, Row row, float x, float y, float width)
        {
            _ui = ui;
            _row = row;
            _shown = Current;
            _logged = ColorText.Format(_shown);

            _swatch = ColourRow.CreateSwatch(parent, new Vector3(x, y + 4f));
            if (_swatch != null)
            {
                _swatch.selectedColor = _shown;
                _swatch.eventSelectedColorChanged += (component, colour) =>
                {
                    if (!_refreshing)
                        Store(colour);
                };
                _swatch.eventSelectedColorReleased += (component, colour) =>
                {
                    if (_refreshing)
                        return;

                    Store(colour);
                    Commit("picked");
                };
                _parts.Add(_swatch);
            }

            float textX = x + SwatchSize + Gap;
            _text = _ui.AddTextField(parent, new Vector2(Mathf.Max(40f, x + width - textX), 26f), new Vector3(textX, y + 4f), 64);
            _text.text = ColorText.Format(_shown);
            _text.eventTextSubmitted += (component, text) => Submit(text);
            _text.eventTextCancelled += (component, text) => ShowValid();
            _parts.Add(_text);
        }

        /// <summary>Everything that greys out and hides with it.</summary>
        public IList<UIComponent> Parts
        {
            get { return _parts; }
        }

        private Color32 Current
        {
            get { return _row.Colour != null ? _row.Colour.value : _row.DefaultColour; }
        }

        public void Refresh()
        {
            _refreshing = true;
            try
            {
                Color32 current = Current;
                if (!ColorText.Same(current, _shown))
                {
                    _shown = current;
                    if (_swatch != null)
                        _swatch.selectedColor = current;
                    if (!_text.hasFocus)
                        ShowValid();
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void Store(Color32 colour)
        {
            if (_row.Colour == null)
                return;

            _row.Colour.value = colour;
            _shown = _row.Colour.value;

            _refreshing = true;
            try
            {
                if (_swatch != null && !ColorText.Same((Color32)_swatch.selectedColor, _shown))
                    _swatch.selectedColor = _shown;
                if (!_text.hasFocus)
                    ShowValid();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void Submit(string text)
        {
            if (_refreshing)
                return;

            Color32 colour;
            if (ColorText.TryParse(text, out colour))
            {
                Store(colour);
                Commit("typed");
            }

            ShowValid();
        }

        private void Commit(string how)
        {
            string hex = ColorText.Format(Current);
            if (hex == _logged)
                return;

            _logged = hex;
            Log.Msg("setting: " + _row.Name + " = " + hex + " (" + how + ")");
        }

        private void ShowValid()
        {
            _text.text = ColorText.Format(_shown);
            _text.textColor = _ui.Theme.FieldText;
        }
    }
}
