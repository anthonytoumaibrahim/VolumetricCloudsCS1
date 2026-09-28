using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.IO;
using VolumetricClouds.UI;

namespace VolumetricClouds
{
    /// <summary>
    /// PROFILES (1.2.1): whole skies saved under a name and picked at the top of the in-game
    /// panel. Asked for "the way Render It! does": every setting of the sky, the opt-ins such as
    /// the volumetric fog included, in files of their own.
    /// </summary>
    /// <remarks>
    /// THE MODEL: the picked profile IS the sky, edited live. VolumetricClouds.xml stays the whole
    /// truth -- every row, as always, plus &lt;Profile&gt;, which one is picked -- and while one is
    /// picked every save of it writes the profile's file too, from the same walk of the catalog,
    /// with the rows a profile carries (Row.Profiled: the sky, never the computer, invariant 11).
    /// So there is no Save button, and nothing is ever unsaved. "+" makes a profile of the sky as
    /// it is and picks it; "-" deletes the picked one's file, the sky staying as it is; "(no
    /// profile)" lets go -- which is where every player was before profiles.
    ///
    /// Picking one puts ALL its values in (a value it lacks goes back to its default: a profile is
    /// a complete sky), the fog switch included and without the fog's question: the player's own
    /// click on a sky they saved or chose, like "Reset all" (invariant 12's wording since 1.2.1).
    ///
    /// Each profile is VolumetricCloudsProfiles\&lt;name&gt;.xml beside the settings file, in the
    /// settings file's own format: a player can send one to a friend, write one by hand, or drop
    /// one in the folder with the game running (the list is read again each time it opens). The
    /// name IS the file's name (<see cref="ProfileName"/>).
    ///
    /// TWO FILES, ONE TRUTH. At startup the settings file is read, then the picked profile's on
    /// top: the two are written together, so they differ only by a hand edit made with the game
    /// closed, and the profile is where a player who picked "Storm" expects Storm to be. While
    /// the game runs both are watched once a second, and a hand edit of either applies live and
    /// reaches the other. A profile's file that cannot be read is never written over (it may hold
    /// an afternoon of tuning with one bracket missing), and one deleted on disk is let go, never
    /// written back.
    ///
    /// ERROR-PROOF (the author: "if someone messes up the xml file manually, the mod shouldn't
    /// crash"): every file operation is caught, logged with the file and what was being done,
    /// and leaves the sky working.
    /// </remarks>
    public static class Profiles
    {
        public const string FolderName = "VolumetricCloudsProfiles";
        private const string Extension = ".xml";

        public enum Status
        {
            /// <summary>No profile picked: the settings file alone.</summary>
            None,

            /// <summary>Picked, read, and written with every change.</summary>
            Selected,

            /// <summary>Picked, but its file cannot be read: left alone until it can, the settings file's values running.</summary>
            Unreadable,
        }

        private static string _folder;

        /// <summary>The picked profile's file; null while none is picked.</summary>
        private static WatchedFile _file;
        private static Status _state;
        private static string _problem;

        private static string _lastSaveError;
        private static string _lastListError;
        private static bool _loggedOddFile;

        /// <summary>Beside VolumetricClouds.xml and the log, on every system (DataLocation.localApplicationData).</summary>
        public static string Folder
        {
            get
            {
                if (_folder == null)
                    _folder = Path.Combine(DataLocation.localApplicationData, FolderName);

                return _folder;
            }
        }

        /// <summary>The picked profile's name; empty for none.</summary>
        public static string Selected
        {
            get { return Settings.CurrentProfile != null ? Settings.CurrentProfile.value : string.Empty; }
        }

        public static Status State
        {
            get { return _state; }
        }

        public static string PathOf(string name)
        {
            return Path.Combine(Folder, name + Extension);
        }

        /// <summary>For the log's features line: which profile, and whether its file is well.</summary>
        public static string Describe()
        {
            switch (_state)
            {
                case Status.Selected:
                    return "'" + Selected + "'";
                case Status.Unreadable:
                    return "'" + Selected + "' (its file cannot be read: " + _problem + ")";
                default:
                    return "none";
            }
        }

        // ---- the folder ------------------------------------------------------------------------

        /// <summary>
        /// Every profile in the folder, by name, sorted without regard to case; empty when there is
        /// no folder yet. Never throws: a folder that cannot be read is logged (once per reason)
        /// and lists nothing.
        /// </summary>
        public static List<string> List()
        {
            var names = new List<string>();

            try
            {
                if (!Directory.Exists(Folder))
                    return names;

                foreach (string path in Directory.GetFiles(Folder, "*" + Extension))
                {
                    // "*.xml" also matches "a.xmlx" on Windows (its rule for three-letter extensions).
                    if (!string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string name = Path.GetFileNameWithoutExtension(path);
                    if (!ProfileName.IsListable(name))
                    {
                        if (!_loggedOddFile)
                        {
                            _loggedOddFile = true;
                            Log.Msg("profiles: '" + Path.GetFileName(path) + "' in " + Folder + " is not listed: its name is " +
                                    "empty, starts or ends with a space, or holds a control character, and could not be kept " +
                                    "as the picked profile (rename the file)");
                        }

                        continue;
                    }

                    names.Add(name);
                }

                _lastListError = null;
            }
            catch (Exception e)
            {
                if (e.Message != _lastListError)
                {
                    _lastListError = e.Message;
                    Log.Warn("profiles: could not list " + Folder + ": " + e.Message);
                }
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>The name as the folder spells it, when a profile of that name is there; else null.</summary>
        private static string Listed(string name)
        {
            foreach (string listed in List())
            {
                if (ProfileName.Same(listed, name))
                    return listed;
            }

            return null;
        }

        // ---- the player's clicks ---------------------------------------------------------------

        /// <summary>
        /// Picks a profile from the list: every value it holds is put in (a value it lacks goes
        /// back to its default), then both files are saved. False when its file cannot be read,
        /// and then nothing has changed; the log says why. Never throws.
        /// </summary>
        public static bool Select(string name)
        {
            try
            {
                return SelectFrom(name, "picked in the panel");
            }
            catch (Exception e)
            {
                Log.Error("profile: picking '" + name + "' failed; the sky is left as it is", e);
                return false;
            }
        }

        /// <summary>
        /// "(no profile)": lets the picked profile go. No value changes and its file stays: the
        /// settings file alone runs the sky again. The caller saves.
        /// </summary>
        public static void Detach(string why)
        {
            string previous = Selected;
            if (previous.Length == 0 && _file == null)
                return;

            LetGo();
            Log.Msg("profile: no profile picked (" + why + ")" +
                    (previous.Length > 0 ? "; '" + previous + "' keeps its file" : "") + "; the sky stays as it is");
        }

        /// <summary>
        /// "+": a new profile of the sky as it is now, which is then the picked one. False when
        /// the name cannot be used or the file cannot be written -- <paramref name="error"/> is
        /// then a sentence for the player -- and nothing has changed. Never throws.
        /// </summary>
        public static bool Add(string typed, out string error)
        {
            try
            {
                return AddAs(typed, "made with +", out error);
            }
            catch (Exception e)
            {
                error = Localization.Get("Profiles.Failed");
                Log.Error("profile: making '" + typed + "' failed", e);
                return false;
            }
        }

        /// <summary>
        /// "-": deletes a profile's file. When it is the picked one it is let go, and the sky stays
        /// as it is. False when the file cannot be deleted; the log says why. Never throws.
        /// </summary>
        public static bool Remove(string name)
        {
            try
            {
                if (!ProfileName.IsFileName(name))
                    return false;

                string path = PathOf(name);
                bool had = File.Exists(path);
                if (had)
                    File.Delete(path);

                bool wasPicked = ProfileName.Same(Selected, name);
                if (wasPicked)
                    LetGo();

                Log.Msg("profile: " + (had ? "removed '" + name + "' (" + path + ")" : "'" + name + "' had no file left to remove (" + path + ")") +
                        (wasPicked ? "; no profile picked, the sky stays as it is" : ""));

                SettingsXml.SaveNow();
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("profile: could not remove '" + name + "' (" + PathOf(name) + "): " + e.Message);
                return false;
            }
        }

        // ---- the settings file's side ----------------------------------------------------------

        /// <summary>
        /// Once, when the settings file has been read (SettingsXml.Load): the picked profile's
        /// file on top of it. Gone: let go. Cannot be read: kept picked and left alone, with the
        /// settings file's values running. Never throws.
        /// </summary>
        internal static void Startup()
        {
            try
            {
                ReadAtStartup();
            }
            catch (Exception e)
            {
                Log.Error("profiles: could not read the picked profile at startup; running on the settings file", e);
            }
        }

        private static void ReadAtStartup()
        {
            string name = Selected;
            string where = Directory.Exists(Folder)
                ? List().Count + " in " + Folder
                : "no folder yet (" + Folder + ")";

            if (name.Length == 0)
            {
                Log.Msg("profiles: " + where + "; none picked");
                return;
            }

            if (!ProfileName.IsListable(name))
            {
                LetGo();
                Log.Warn("profiles: " + where + "; " + SettingsXml.FileName + " names '" + name +
                         "', which cannot be a profile's file name; no profile picked");
                SettingsXml.SaveLiveOnly();
                return;
            }

            string listed = Listed(name);
            if (listed == null)
            {
                LetGo();
                Log.Msg("profiles: " + where + "; '" + name + "' was picked, but its file is gone (" + PathOf(name) +
                        "); no profile picked");
                SettingsXml.SaveLiveOnly();
                return;
            }

            if (listed != name)
                Settings.CurrentProfile.value = listed;

            _file = new WatchedFile(PathOf(listed));
            _state = Status.Unreadable; // until it has been read

            string text;
            try
            {
                text = File.ReadAllText(_file.Path);
            }
            catch (Exception e)
            {
                // Not remembered, so the watcher tries again every second, quietly.
                _problem = e.Message;
                Log.Warn("profiles: " + where + "; '" + listed + "' is picked, but its file cannot be opened (" + e.Message +
                         "); running on the settings file, and the profile's file is left alone until it can be read");
                return;
            }

            var changed = new List<Row>();
            string error;
            SettingsXml.ReadResult result = SettingsXml.ReadProfile(text, out error, false, changed);
            if (result == null)
            {
                MarkUnreadable(error);
                Log.Warn("profiles: " + where + "; '" + listed + "' is picked, but its file cannot be read (" + error +
                         "); running on the settings file, and the profile's file is left alone until it can be read. " +
                         "Fix it and it is read within a second.");
                return;
            }

            _file.Remember(text);
            _state = Status.Selected;
            _problem = null;

            Log.Msg("profiles: " + where + "; '" + listed + "' picked, read on top of " + SettingsXml.FileName + " -- " +
                    (changed.Count == 0 ? "no value changed" : changed.Count + " changed: " + result.Changes()) +
                    result.Describe());
            result.LogProblems("profile");

            // The settings file follows at once; the profile's own text waits for the next change.
            if (changed.Count > 0)
                SettingsXml.SaveLiveOnly();
        }

        /// <summary>Once a second (SettingsXml.Tick): has the picked profile's file been edited, or deleted?</summary>
        internal static void Check()
        {
            if (_file == null)
                return;

            try
            {
                var info = new FileInfo(_file.Path);
                if (!info.Exists)
                {
                    Deleted();
                    return;
                }

                if (!_file.Moved(info))
                    return;
            }
            catch (Exception e)
            {
                if (Log.Detailed)
                    Log.Detail("profile: could not look at " + _file.Path + ": " + e.Message);
                return;
            }

            try
            {
                Reload();
            }
            catch (Exception e)
            {
                Log.Error("profile: taking the edit of '" + Selected + "' failed", e);
            }
        }

        /// <summary>
        /// From every save of the settings file (SettingsXml.SaveNow): the picked profile's file
        /// with the sky as it is, if that differs from what is there. Not while it cannot be read,
        /// and never to bring back a file that was deleted. Never throws.
        /// </summary>
        internal static void Save()
        {
            if (_state != Status.Selected || _file == null)
                return;

            try
            {
                var info = new FileInfo(_file.Path);
                if (!info.Exists)
                {
                    Deleted();
                    return;
                }

                // Edited on disk since we last read or wrote it: Check reads that edit first,
                // within a second, and this save comes after it. Never write over an edit unread
                // -- above all one that cannot be read (the author's rule).
                if (_file.DiskText != null && _file.Moved(info))
                {
                    SettingsXml.MarkDirty();
                    return;
                }

                string text = SettingsXml.ComposeProfile();
                if (text == _file.DiskText)
                    return;

                SettingsXml.WriteReplacing(_file.Path, text);
                _file.Remember(text);
                _lastSaveError = null;

                if (Log.Detailed)
                    Log.Detail("profile: saved '" + Selected + "' (" + _file.Path + ")");
            }
            catch (Exception e)
            {
                // Once per distinct failure: this runs with every save.
                if (e.Message != _lastSaveError)
                {
                    _lastSaveError = e.Message;
                    Log.Warn("profile: could not save '" + Selected + "' to " + _file.Path + ": " + e.Message);
                }
            }
        }

        /// <summary>
        /// The Profile row's AfterChange: &lt;Profile&gt; was edited by hand in the settings file
        /// (the panel's bar sets it directly and never comes here). A name whose file is there is
        /// picked, as the dropdown would; a new name makes a profile of the sky as it is -- a
        /// typed name is a wish for one; empty lets go. Never throws.
        /// </summary>
        internal static void OnSelectionEdited()
        {
            string current = _file != null ? Path.GetFileNameWithoutExtension(_file.Path) : string.Empty;

            try
            {
                string wanted = Selected;
                if (wanted == current)
                    return;

                if (wanted.Length == 0)
                {
                    LetGo();
                    Log.Msg("profile: <Profile> was emptied in " + SettingsXml.FileName + "; no profile picked, the sky stays as it is");
                    return;
                }

                if (!ProfileName.IsListable(wanted))
                {
                    Log.Warn("profile: <Profile> in " + SettingsXml.FileName + " names '" + wanted +
                             "', which cannot be a profile's file name; left as it was");
                    Restore(current);
                    return;
                }

                string listed = Listed(wanted);
                if (listed != null)
                {
                    if (!SelectFrom(listed, "named in " + SettingsXml.FileName))
                        Restore(current);
                    return;
                }

                string error;
                if (!AddAs(wanted, "named in " + SettingsXml.FileName + ", which had no file of that name", out error))
                {
                    Log.Warn("profile: <Profile> in " + SettingsXml.FileName + " names '" + wanted +
                             "', which has no file, and none could be made (" + error + "); left as it was");
                    Restore(current);
                }
            }
            catch (Exception e)
            {
                Log.Error("profile: taking <Profile> from " + SettingsXml.FileName + " failed", e);
                Restore(current);
            }
        }

        // ---- inside --------------------------------------------------------------------------

        private static bool SelectFrom(string name, string how)
        {
            if (Settings.CurrentProfile == null || !ProfileName.IsListable(name))
                return false;

            string path = PathOf(name);
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Log.Warn("profile: '" + name + "' cannot be opened (" + path + ": " + e.Message + "); nothing was changed");
                return false;
            }

            var changed = new List<Row>();
            string error;
            SettingsXml.ReadResult result = SettingsXml.ReadProfile(text, out error, true, changed);
            if (result == null)
            {
                Log.Warn("profile: '" + name + "' cannot be read (" + error + ", in " + path +
                         "); nothing was changed, and the file is left as it is");
                return false;
            }

            string previous = Selected;
            Settings.CurrentProfile.value = name;
            _file = new WatchedFile(path);
            _file.Remember(text);
            _state = Status.Selected;
            _problem = null;
            _lastSaveError = null;

            Log.Msg("profile: picked '" + name + "' (" + how +
                    (previous.Length > 0 && previous != name ? "; was '" + previous + "'" : "") + ") -- " +
                    (changed.Count == 0 ? "no value changed" : changed.Count + " changed: " + result.Changes()) +
                    result.Describe());
            result.LogProblems("profile");

            SettingsXml.RunAfterChange(changed, "the profile");

            // Both files at once, like a switch: the settings file records the pick, and the
            // profile's own file is completed (a value it lacked, one pulled into its range).
            SettingsXml.SaveNow();
            SettingsCatalog.RefreshAllUIs();
            return true;
        }

        private static bool AddAs(string typed, string how, out string error)
        {
            error = null;
            if (Settings.CurrentProfile == null)
            {
                error = Localization.Get("Profiles.Failed");
                return false;
            }

            string why = ProfileName.Why(typed);
            if (why != null)
            {
                error = Localization.Get(why);
                return false;
            }

            string name = ProfileName.Clean(typed);
            if (Listed(name) != null)
            {
                error = Localization.Get("Profiles.Invalid.Exists");
                return false;
            }

            string path = PathOf(name);
            string text;
            try
            {
                if (!Directory.Exists(Folder))
                {
                    Directory.CreateDirectory(Folder);
                    Log.Msg("profiles: made the folder " + Folder);
                }

                // Made by someone else since the list was read: never write over a profile.
                if (File.Exists(path))
                {
                    error = Localization.Get("Profiles.Invalid.Exists");
                    return false;
                }

                text = SettingsXml.ComposeProfile();
                SettingsXml.WriteReplacing(path, text);
            }
            catch (Exception e)
            {
                error = Localization.Get("Profiles.Failed");
                Log.Warn("profile: could not make '" + name + "' (" + path + "): " + e.Message);
                return false;
            }

            string previous = Selected;
            Settings.CurrentProfile.value = name;
            _file = new WatchedFile(path);
            _file.Remember(text);
            _state = Status.Selected;
            _problem = null;
            _lastSaveError = null;

            Log.Msg("profile: added '" + name + "' (" + how + "; " + SettingsXml.ProfiledCount + " values, the sky as it is) -> " +
                    path + (previous.Length > 0 && previous != name ? "; '" + previous + "' is no longer picked" : "") +
                    "; it is now the picked profile");

            SettingsXml.SaveNow();
            return true;
        }

        /// <summary>The file changed on disk and it was not us: take what it says (a missing value stays as it is).</summary>
        private static void Reload()
        {
            string text;
            try
            {
                text = File.ReadAllText(_file.Path);
            }
            catch (Exception)
            {
                return; // an editor is still writing it or holds it; look again in a second
            }

            // Saved again unchanged, or only its date moved: nothing to take.
            bool same = text == _file.DiskText;
            _file.Remember(text);
            if (same)
                return;

            string name = Selected;
            var changed = new List<Row>();
            string error;
            SettingsXml.ReadResult result = SettingsXml.ReadProfile(text, out error, false, changed);
            if (result == null)
            {
                MarkUnreadable(error);
                Log.Warn("profile: '" + name + "' was edited and cannot be read (" + error + "). Nothing was taken from it, " +
                         "and it will not be saved over until it can be read again.");
                return;
            }

            bool wasUnreadable = _state == Status.Unreadable;
            _file.Broken = false;
            _state = Status.Selected;
            _problem = null;
            _lastSaveError = null;

            Log.Msg("profile: '" + name + "' was edited" + (wasUnreadable ? " and can be read again" : "") + " -- " +
                    (changed.Count == 0 ? "no value changed" : changed.Count + " changed: " + result.Changes()) +
                    result.Describe());
            result.LogProblems("profile");

            SettingsXml.RunAfterChange(changed, "the profile's file");

            // The settings file follows at once; this file stays as the player wrote it until the
            // next change, as the settings file does after a hand edit.
            SettingsXml.SaveLiveOnly();
            SettingsCatalog.RefreshAllUIs();
        }

        /// <summary>The picked profile's file is gone from disk: let it go, never write it back.</summary>
        private static void Deleted()
        {
            string name = Selected;
            LetGo();
            Log.Msg("profile: '" + name + "' was deleted on disk; no profile picked, the sky stays as it is");

            SettingsXml.SaveLiveOnly();
            SettingsCatalog.RefreshAllUIs();
        }

        /// <summary>
        /// Kept picked, but not read and never written until it reads again. Its write time and
        /// length are remembered with no text, so the next look reads it only once it changes.
        /// </summary>
        private static void MarkUnreadable(string error)
        {
            _file.Remember(null);
            _file.Broken = true;
            _state = Status.Unreadable;
            _problem = error;
        }

        private static void LetGo()
        {
            if (Settings.CurrentProfile != null)
                Settings.CurrentProfile.value = string.Empty;

            _file = null;
            _state = Status.None;
            _problem = null;
            _lastSaveError = null;
        }

        /// <summary>Puts &lt;Profile&gt; back to what the watcher follows, after a hand edit that could not be taken.</summary>
        private static void Restore(string current)
        {
            try
            {
                if (Settings.CurrentProfile != null)
                    Settings.CurrentProfile.value = current;

                SettingsXml.SaveLiveOnly();
            }
            catch (Exception e)
            {
                Log.Error("profile: could not put <Profile> back to '" + current + "'", e);
            }
        }
    }
}
