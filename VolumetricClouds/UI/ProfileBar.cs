using System;
using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace VolumetricClouds.UI
{
    /// <summary>
    /// The profile bar of the in-game panel (1.3.0), under the title and above the tabs, so it is
    /// there on every tab:
    ///   Profile  [ Storm           v ]  [Save] [+] [-]   a short message
    /// </summary>
    /// <remarks>
    /// The author's design (2026-09-24): no Profiles tab ("we already have what seems as too many
    /// tabs"), a dropdown at the top left with + and - beside it, "- disabled if no profiles
    /// exist" -- "basically mirror RenderIt". Save came with his first test (2026-09-29): a profile
    /// is a saved sky, and Save writes the sky into the picked one (<see cref="Profiles"/>). The
    /// dropdown is Render It!'s own recipe (<see cref="UIBuilder.AddDropDown"/>); should its
    /// sprites ever be missing, a button that shows the picked profile and moves to the next one
    /// on each click takes its place.
    ///
    /// Picking asks nothing -- it is what you do ten times while comparing skies -- unless the sky
    /// has changes not saved anywhere, which the pick would throw away: then it asks first. Save
    /// is lit only while there is something to save, and the message says "Unsaved changes"
    /// meanwhile. "-" asks, since it deletes a file. "+" swaps the dropdown for a name field:
    /// Enter or a click elsewhere saves, Escape cancels, and a name that cannot be used turns red
    /// and the bar says why. While the field is open the buttons are off: a click on one would
    /// first take the field's focus (submitting it) and then act, and the order of those two is
    /// not ours to rely on.
    ///
    /// The folder is listed when the bar is built, just before the list opens (so a file dropped
    /// in with the game running is there), and after anything that adds or removes one -- never
    /// while the list is open: setting a dropdown's items closes its list (UIDropDown.set_items).
    /// </remarks>
    internal sealed class ProfileBar
    {
        public const float Height = 38f;

        private const float LabelWidth = 58f;
        private const float DropWidth = 230f;
        private const float ControlHeight = 30f;
        private const float SaveWidth = 50f;
        private const float ButtonWidth = 28f;
        private const float Gap = 6f;
        private const int VisibleItems = 12;

        /// <summary>A longer name is cut in the dropdown, which is a fixed width (the author's call).</summary>
        private const int ShownCharacters = 28;

        private const float MessageShownFor = 5f;
        private const float InvalidShownFor = 3f;

        private static bool _loggedFallback;

        private readonly UIDropDown _drop;
        private readonly UIButton _cycle;
        private readonly UITextField _field;
        private readonly UIButton _save;
        private readonly UIButton _add;
        private readonly UIButton _remove;
        private readonly UILabel _message;

        /// <summary>What each entry of the list is: "" for "(no profile)", then the names.</summary>
        private readonly List<string> _entries = new List<string>();

        private bool _refreshing;
        private bool _naming;
        private bool _refreshFailed;

        /// <summary>Read the folder again at the next refresh with the list closed (never from inside its own click).</summary>
        private bool _resync;

        /// <summary>
        /// The open list, null when closed. Kept rather than a flag: a dropdown that is disabled
        /// destroys its list WITHOUT raising the close event (UIDropDown.OnDisable ->
        /// ClosePopup(false)), and a destroyed object reads as null here.
        /// </summary>
        private UIListBox _popup;
        private float _messageUntil = -1f;
        private float _invalidUntil = -1f;
        private string _lasting;

        public ProfileBar(UIComponent parent, float x, float y, float width)
        {
            UILabel label = UIBuilder.AddLabel(parent, Localization.Get("Profiles.Label"), new Vector3(x, y + 12f), 0.8f);
            label.autoSize = false;
            label.size = new Vector2(LabelWidth, 20f);
            label.tooltip = Localization.Get("Profiles.Tooltip");

            float left = x + LabelWidth;
            Vector3 at = new Vector3(left, y + 4f);
            Vector2 size = new Vector2(DropWidth, ControlHeight);

            string missing;
            _drop = UIBuilder.AddDropDown(parent, size, at, VisibleItems, out missing);
            if (_drop != null)
            {
                _drop.tooltip = Localization.Get("Profiles.Tooltip");
                _drop.eventSelectedIndexChanged += (component, index) =>
                {
                    if (!_refreshing)
                        Picked(index);
                };

                // Just before the list opens: its click comes after this press.
                _drop.triggerButton.eventMouseDown += (component, e) =>
                {
                    if (!ListOpen)
                        Relist();
                };

                _drop.eventDropdownOpen += (UIDropDown dropdown, UIListBox popup, ref bool overridden) => _popup = popup;
                _drop.eventDropdownClose += (UIDropDown dropdown, UIListBox popup, ref bool overridden) => _popup = null;
            }
            else
            {
                _cycle = UIBuilder.AddButton(parent, string.Empty, size, at);
                _cycle.canFocus = false;
                _cycle.tooltip = Localization.Get("Profiles.Fallback.Tooltip");
                _cycle.eventClick += (component, e) => Cycle();

                if (!_loggedFallback)
                {
                    _loggedFallback = true;
                    Log.Warn("profile bar: the atlas has no '" + missing + "', so the profile list is a button " +
                             "that moves to the next profile on each click");
                }
            }

            _field = UIBuilder.AddTextField(parent, size, at, ProfileName.MaxLength);
            _field.tooltip = Localization.Get("Profiles.Field.Tooltip");
            _field.isVisible = false;
            _field.eventTextSubmitted += (component, text) => Submitted(text);
            _field.eventTextCancelled += (component, text) => StopNaming(null);

            left += DropWidth + Gap;
            _save = UIBuilder.AddButton(parent, Localization.Get("Profiles.Save"), new Vector2(SaveWidth, ControlHeight), new Vector3(left, y + 4f));
            _save.canFocus = false;
            _save.tooltip = Localization.Get("Profiles.Save.Tooltip");
            _save.eventClick += (component, e) => SaveClicked();

            left += SaveWidth + 4f;
            _add = UIBuilder.AddButton(parent, "+", new Vector2(ButtonWidth, ControlHeight), new Vector3(left, y + 4f));
            _add.textScale = 1.1f;
            _add.canFocus = false;
            _add.tooltip = Localization.Get("Profiles.Add.Tooltip");
            _add.eventClick += (component, e) => StartNaming();

            left += ButtonWidth + 4f;
            _remove = UIBuilder.AddButton(parent, "-", new Vector2(ButtonWidth, ControlHeight), new Vector3(left, y + 4f));
            _remove.textScale = 1.1f;
            _remove.canFocus = false;
            _remove.tooltip = Localization.Get("Profiles.Remove.Tooltip");
            _remove.eventClick += (component, e) => ConfirmRemove();

            left += ButtonWidth + Gap + 2f;
            _message = UIBuilder.AddLabel(parent, string.Empty, new Vector3(left, y + 4f), 0.7f);
            _message.autoSize = false;
            _message.wordWrap = true;
            _message.verticalAlignment = UIVerticalAlignment.Middle;
            _message.size = new Vector2(Mathf.Max(40f, x + width - left), ControlHeight);

            Relist();
            Refresh();
        }

        /// <summary>From the panel's refresh, four times a second: the pick, the buttons, the message.</summary>
        public void Refresh()
        {
            try
            {
                RefreshNow();
            }
            catch (Exception e)
            {
                // Once: this runs four times a second.
                if (!_refreshFailed)
                {
                    _refreshFailed = true;
                    Log.Error("profile bar: refreshing failed", e);
                }
            }
        }

        private bool ListOpen
        {
            get { return _popup != null; }
        }

        private void RefreshNow()
        {
            string picked = Profiles.Selected;
            int index = IndexOf(picked);

            // Picked some other way -- a hand edit, a reset, a file gone -- or new since the list
            // was read; or a pick that did not work or waits for an answer, which shows the one in
            // force meanwhile.
            if ((index < 0 || _resync) && !ListOpen)
            {
                _resync = false;
                Relist();
                index = IndexOf(picked);
            }

            if (index >= 0 && !ListOpen)
                ShowPick(index);

            bool unsaved = picked.Length > 0 && Profiles.HasUnsavedChanges;
            RefreshButtons(unsaved);

            float now = Time.realtimeSinceStartup;
            if (_invalidUntil >= 0f && now > _invalidUntil)
            {
                _invalidUntil = -1f;
                _field.textColor = UIBuilder.TextFieldColour;
            }

            if (_messageUntil >= 0f && now > _messageUntil)
            {
                _messageUntil = -1f;
                _message.text = string.Empty;
            }

            // What lasts while it is so: a picked profile whose file cannot be read, or one the
            // sky has moved away from.
            if (_messageUntil < 0f && !_naming)
            {
                string lasting = Profiles.State == Profiles.Status.Unreadable ? Localization.Get("Profiles.Unreadable")
                    : unsaved ? Localization.Get("Profiles.Unsaved")
                    : string.Empty;

                if (lasting != _lasting)
                {
                    _lasting = lasting;
                    _message.textColor = UIBuilder.WarningColour;
                    _message.text = lasting;
                }
            }
        }

        private void RefreshButtons(bool unsaved)
        {
            _save.isEnabled = !_naming && unsaved;
            _add.isEnabled = !_naming;
            _remove.isEnabled = !_naming && Profiles.Selected.Length > 0;
        }

        // ---- the list --------------------------------------------------------------------------

        /// <summary>Reads the folder and puts "(no profile)" and every name in the list, the pick selected.</summary>
        private void Relist()
        {
            _entries.Clear();
            _entries.Add(string.Empty);
            _entries.AddRange(Profiles.List());

            // A pick the folder does not list (its file went a moment ago) stays in view until it
            // is let go, so the bar never shows another pick than the one in force.
            string picked = Profiles.Selected;
            if (picked.Length > 0 && IndexOf(picked) < 0)
                _entries.Add(picked);

            if (_drop != null)
            {
                var shown = new string[_entries.Count];
                shown[0] = Localization.Get("Profiles.None");
                for (int i = 1; i < shown.Length; i++)
                    shown[i] = Shorten(_entries[i]);

                _refreshing = true;
                try
                {
                    _drop.items = shown;
                }
                finally
                {
                    _refreshing = false;
                }
            }

            ShowPick(Mathf.Max(0, IndexOf(picked)));
        }

        private void ShowPick(int index)
        {
            if (_drop != null)
            {
                if (_drop.selectedIndex == index)
                    return;

                _refreshing = true;
                try
                {
                    _drop.selectedIndex = index;
                }
                finally
                {
                    _refreshing = false;
                }
            }
            else if (_cycle != null)
            {
                string text = index > 0 ? Shorten(_entries[index]) : Localization.Get("Profiles.None");
                if (_cycle.text != text)
                    _cycle.text = text;
            }
        }

        private int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;

            for (int i = 1; i < _entries.Count; i++)
            {
                if (ProfileName.Same(_entries[i], name))
                    return i;
            }

            return -1;
        }

        private static string Shorten(string name)
        {
            return name.Length <= ShownCharacters ? name : name.Substring(0, ShownCharacters - 3) + "...";
        }

        /// <summary>An entry was picked in the list.</summary>
        private void Picked(int index)
        {
            try
            {
                if (index < 0 || index >= _entries.Count)
                    return;

                string name = _entries[index];
                string picked = Profiles.Selected;
                if (name.Length == 0 ? picked.Length == 0 : ProfileName.Same(name, picked) && Profiles.State == Profiles.Status.Selected)
                    return;

                if (name.Length == 0)
                {
                    Profiles.Detach("\"(no profile)\" picked in the panel");
                    SettingsXml.SaveNow();
                    SettingsCatalog.RefreshAllUIs();
                    return;
                }

                // The list shows the pick in force again at the next refresh -- not here: this runs
                // inside the list's own click, and re-listing closes it.
                _resync = true;

                // Picking puts the profile's sky in: ask first when that throws away changes kept
                // nowhere else.
                if (Profiles.HasUnsavedChanges)
                {
                    AskBeforePicking(name, picked);
                    return;
                }

                PickNow(name);
            }
            catch (Exception e)
            {
                Log.Error("profile bar: picking an entry failed", e);
                _resync = true;
            }
        }

        private void AskBeforePicking(string name, string picked)
        {
            string question = picked.Length > 0
                ? Localization.Get("Profiles.Confirm.Discard", picked, name)
                : Localization.Get("Profiles.Confirm.Replace", name);

            try
            {
                ConfirmPanel.ShowModal(Mod.DisplayName, question, (component, result) =>
                {
                    if (result != 1)
                    {
                        Log.Msg("profile: picking '" + name + "' cancelled; the sky keeps its changes");
                        return;
                    }

                    try
                    {
                        PickNow(name);
                    }
                    catch (Exception e)
                    {
                        Log.Error("profile bar: picking '" + name + "' failed", e);
                    }
                });
            }
            catch (Exception e)
            {
                // Nothing is thrown away without an answer.
                Log.Error("Could not ask before picking the profile, so it was not picked.", e);
            }
        }

        /// <summary>Nothing changed when it fails, and the bar says so.</summary>
        private void PickNow(string name)
        {
            if (!Profiles.Select(name))
                Say(Localization.Get("Profiles.Unreadable"), UIBuilder.WarningColour);

            _resync = true;
        }

        /// <summary>The stand-in for the dropdown: each click picks the next entry.</summary>
        private void Cycle()
        {
            Relist();
            if (_entries.Count < 2)
                return;

            int next = (Mathf.Max(0, IndexOf(Profiles.Selected)) + 1) % _entries.Count;
            Picked(next);
            Relist();
        }

        // ---- Save ------------------------------------------------------------------------------

        private void SaveClicked()
        {
            if (_naming || Profiles.Selected.Length == 0)
                return;

            try
            {
                if (Profiles.SaveSelected())
                    Say(Localization.Get("Profiles.Saved"), UIBuilder.StatusColour);
                else
                    Say(Localization.Get("Profiles.Failed"), UIBuilder.WarningColour);

                RefreshButtons(Profiles.HasUnsavedChanges);
            }
            catch (Exception e)
            {
                Log.Error("profile bar: saving the profile failed", e);
            }
        }

        // ---- + ---------------------------------------------------------------------------------

        private void StartNaming()
        {
            if (_naming)
                return;

            try
            {
                _naming = true;
                if (_drop != null)
                    _drop.Hide();
                else
                    _cycle.Hide();

                RefreshButtons(false);

                _field.text = ProfileName.NextFree(Profiles.List(), Localization.Get("Profiles.NewName"));
                _field.textColor = UIBuilder.TextFieldColour;
                _invalidUntil = -1f;
                _field.Show();
                _field.Focus(); // selects the whole name (selectOnFocus): typing replaces it

                Say(Localization.Get("Profiles.Naming"), UIBuilder.StatusColour, 0f);
            }
            catch (Exception e)
            {
                Log.Error("profile bar: could not open the name field", e);
                StopNaming(null);
            }
        }

        private void Submitted(string text)
        {
            if (!_naming)
                return;

            // Enter, or a click somewhere else: the field has lost its focus either way by now.
            bool byEnter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

            try
            {
                string error;
                if (Profiles.Add(text, out error))
                {
                    StopNaming(Localization.Get("Profiles.Added"));
                    Relist();
                    SettingsCatalog.RefreshAllUIs();
                    return;
                }

                if (byEnter)
                {
                    // It stays open, in red, with the reason beside it: fix the name, or Escape.
                    _field.textColor = UIBuilder.InvalidColour;
                    _invalidUntil = Time.realtimeSinceStartup + InvalidShownFor;
                    Say(error, UIBuilder.WarningColour, 0f);
                    _field.Focus();
                    return;
                }

                // A click elsewhere with a name that cannot be used: nothing is made, and the bar
                // says why.
                StopNaming(null);
                Say(error, UIBuilder.WarningColour);
            }
            catch (Exception e)
            {
                Log.Error("profile bar: making a profile failed", e);
                StopNaming(null);
            }
        }

        private void StopNaming(string message)
        {
            _naming = false;
            _invalidUntil = -1f;
            _field.Hide();

            if (_drop != null)
                _drop.Show();
            else if (_cycle != null)
                _cycle.Show();

            RefreshButtons(Profiles.Selected.Length > 0 && Profiles.HasUnsavedChanges);

            if (message != null)
                Say(message, UIBuilder.StatusColour);
            else
                Say(string.Empty, UIBuilder.StatusColour);
        }

        // ---- - ---------------------------------------------------------------------------------

        private void ConfirmRemove()
        {
            string name = Profiles.Selected;
            if (name.Length == 0 || _naming)
                return;

            try
            {
                ConfirmPanel.ShowModal(Mod.DisplayName, Localization.Get("Profiles.Confirm.Remove", name),
                    (component, result) =>
                    {
                        if (result != 1)
                        {
                            Log.Msg("profile: removing '" + name + "' cancelled");
                            return;
                        }

                        try
                        {
                            if (!Profiles.Remove(name))
                                Say(Localization.Get("Profiles.Failed"), UIBuilder.WarningColour);

                            Relist();
                            SettingsCatalog.RefreshAllUIs();
                        }
                        catch (Exception e)
                        {
                            Log.Error("profile bar: removing '" + name + "' failed", e);
                        }
                    });
            }
            catch (Exception e)
            {
                // Nothing is deleted without an answer.
                Log.Error("Could not ask about removing the profile, so nothing was removed.", e);
            }
        }

        // ---- the message -------------------------------------------------------------------------

        /// <summary>A short line beside the buttons: gone after a few seconds, or with 0, when something replaces it.</summary>
        private void Say(string text, Color32 colour, float seconds = MessageShownFor)
        {
            _message.textColor = colour;
            _message.text = text ?? string.Empty;
            _lasting = null;
            _messageUntil = string.IsNullOrEmpty(text) ? -1f
                : seconds > 0f ? Time.realtimeSinceStartup + seconds
                : float.MaxValue;
        }
    }
}
