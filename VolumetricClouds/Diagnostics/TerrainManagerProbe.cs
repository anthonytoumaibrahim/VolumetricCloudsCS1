using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;

namespace VolumetricClouds.Diagnostics
{
    /// <summary>
    /// TEMPORARY DIAGNOSTIC (2026-09-21) -- delete this file once it has caught one failure.
    /// </summary>
    /// <remarks>
    /// On some first city loads after a game launch the game logs "Simulation error: Object
    /// reference not set..." in TerrainManager.Managers_TerrainUpdated, 5 ms after FreeFill
    /// logs "ground-clear terrain manager registered". The leading explanation is a race:
    /// FreeFill adds its manager to the game's terrain-manager list from the main thread
    /// (RegisterTerrainManager = an unlocked FastList.Add, which stores the new size BEFORE the
    /// item) while the simulation thread loops over that list for a road update. But that gap
    /// is nanoseconds wide and the error came on 2 of 4 first loads, so the explanation is
    /// incomplete, and the author asked for evidence rather than an argument.
    ///
    /// So this records, always-on and into our own log:
    /// - every registration into that list: who, at which index, on which thread, when;
    /// - if the loop reads an EMPTY slot: the list at that instant, and the slot re-read a
    ///   moment later (filled by then = the registration race, caught in the act);
    /// - if a manager THROWS: which one, with the full stack -- including the frames inside
    ///   the manager, which the game's own "Simulation error" line would not show if the
    ///   fault were in its callee.
    ///
    /// The loop is the game's own, line for line (m_size and m_buffer re-read every step), so
    /// nothing changes: an empty slot still throws the same NullReferenceException, and any
    /// exception still reaches the game's handler, which still logs its "Simulation error".
    /// </remarks>
    internal static class TerrainManagerProbe
    {
        private const int MaxReports = 5;

        /// <summary>
        /// Next to VolumetricClouds.log, but APPENDED to, never restarted: the mod log starts
        /// fresh every launch, and the author should not have to report a failure before
        /// launching the game again for it to survive.
        /// </summary>
        private const string FileName = "VolumetricClouds-terrain-probe.log";

        private static readonly object FileSync = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static FastList<ITerrainManager> _list;
        private static bool _listUnreadable;
        private static int _reports;

        /// <summary>
        /// Called by Patcher after PatchAll, in its own try/catch: the probe's classes carry no
        /// [HarmonyPatch], so a failure here can only lose the probe, never the rain and halo
        /// patches.
        /// </summary>
        internal static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo register = typeof(TerrainManager).GetMethod(nameof(TerrainManager.RegisterTerrainManager),
                    BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ITerrainManager) }, null);
                MethodInfo updated = typeof(TerrainManager).GetMethod(nameof(TerrainManager.Managers_TerrainUpdated),
                    BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(TerrainArea), typeof(TerrainArea), typeof(TerrainArea) }, null);

                if (register == null || updated == null)
                {
                    Log.Warn("terrain probe: the game's methods were not found; the probe is off.");
                    return;
                }

                harmony.Patch(register,
                    prefix: new HarmonyMethod(typeof(RegisterTerrainManagerProbe), nameof(RegisterTerrainManagerProbe.Prefix)),
                    postfix: new HarmonyMethod(typeof(RegisterTerrainManagerProbe), nameof(RegisterTerrainManagerProbe.Postfix)));
                harmony.Patch(updated,
                    prefix: new HarmonyMethod(typeof(TerrainUpdatedProbe), nameof(TerrainUpdatedProbe.Prefix)));

                Note("=== game launched " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                     ": TEMPORARY diagnostic applied -- it logs every terrain-manager registration and, " +
                     "if the load-time 'Object reference' error comes back, what was in the list when it happened");
            }
            catch (Exception e)
            {
                Log.Error("terrain probe: could not be applied; it is off, everything else is unaffected.", e);
            }
        }

        /// <summary>The game's private static TerrainManager.m_managers. Null if it cannot be read.</summary>
        internal static FastList<ITerrainManager> List
        {
            get
            {
                if (_list == null && !_listUnreadable)
                {
                    try
                    {
                        FieldInfo field = typeof(TerrainManager).GetField("m_managers", BindingFlags.NonPublic | BindingFlags.Static);
                        _list = field != null ? field.GetValue(null) as FastList<ITerrainManager> : null;
                    }
                    catch (Exception)
                    {
                        _list = null;
                    }

                    if (_list == null)
                    {
                        _listUnreadable = true;
                        Log.Warn("terrain probe: TerrainManager.m_managers could not be read; the game's own loop runs unwatched.");
                    }
                }

                return _list;
            }
        }

        internal static void Registered(ITerrainManager manager, int sizeBefore)
        {
            if (manager == null)
                return;

            FastList<ITerrainManager> list = List;
            Note("registered " + manager.GetType().FullName +
                    " at index " + sizeBefore + " (list now " + (list != null ? list.m_size.ToString() : "?") + ")" +
                    " " + Where());
        }

        /// <summary>The loop read an empty slot. Log the list as it is NOW, and the slot again.</summary>
        internal static void EmptySlot(int index, int sizeSeen, FastList<ITerrainManager> list)
        {
            if (!TakeReport())
                return;

            ITerrainManager[] buffer = list.m_buffer;
            ITerrainManager again = buffer != null && index < buffer.Length ? buffer[index] : null;

            Note("EMPTY SLOT read at index " + index + " (size was " + sizeSeen + ", now " + list.m_size +
                    ", buffer " + (buffer != null ? buffer.Length.ToString() : "null") + ") -- re-read now: " +
                    (again != null ? again.GetType().FullName + " (it filled in a moment later: a registration racing this loop)" : "still empty") +
                    " " + Where() + " | list: " + Describe(list));
        }

        internal static void Threw(int index, ITerrainManager manager, Exception e)
        {
            if (!TakeReport())
                return;

            FastList<ITerrainManager> list = List;
            Note("manager #" + index + " " + (manager != null ? manager.GetType().FullName : "(empty slot)") +
                    " threw " + e.GetType().Name + ": " + e.Message + " " + Where() +
                    (list != null ? " | list: " + Describe(list) : "") +
                    "\n" + e.StackTrace);
        }

        /// <summary>Our log (fresh each launch) AND the probe's own file (kept across launches). Any thread.</summary>
        private static void Note(string line)
        {
            Log.Msg("terrain probe: " + line);

            try
            {
                lock (FileSync)
                {
                    File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Log.Path), FileName),
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // The line is in the mod log already; losing the copy must not disturb the game.
            }
        }

        private static bool TakeReport()
        {
            return Interlocked.Increment(ref _reports) <= MaxReports;
        }

        private static string Where()
        {
            Thread thread = Thread.CurrentThread;
            return "[t=" + Clock.Elapsed.TotalMilliseconds.ToString("F3") + " ms, thread " + thread.ManagedThreadId +
                   (string.IsNullOrEmpty(thread.Name) ? "" : " '" + thread.Name + "'") + "]";
        }

        private static string Describe(FastList<ITerrainManager> list)
        {
            ITerrainManager[] buffer = list.m_buffer;
            int size = list.m_size;
            if (buffer == null)
                return "size " + size + ", no buffer";

            StringBuilder text = new StringBuilder("size " + size + ": ");
            int shown = Math.Min(buffer.Length, size + 2);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                    text.Append(", ");
                text.Append(i).Append('=').Append(buffer[i] != null ? buffer[i].GetType().Name : "EMPTY");
            }

            return text.ToString();
        }
    }

    /// <summary>Every addition to the game's terrain-manager list, with its thread and time. Applied by TerrainManagerProbe.Apply.</summary>
    public static class RegisterTerrainManagerProbe
    {
        public static void Prefix(out int __state)
        {
            FastList<ITerrainManager> list = TerrainManagerProbe.List;
            __state = list != null ? list.m_size : -1;
        }

        public static void Postfix(ITerrainManager manager, int __state)
        {
            TerrainManagerProbe.Registered(manager, __state);
        }
    }

    /// <summary>
    /// The game's loop over the terrain managers, line for line, with the two checks above.
    /// Returns false (the original is skipped) only when it has run the loop itself. Applied
    /// by TerrainManagerProbe.Apply.
    /// </summary>
    public static class TerrainUpdatedProbe
    {
        public static bool Prefix(TerrainArea heightArea, TerrainArea surfaceArea, TerrainArea zoneArea)
        {
            FastList<ITerrainManager> list = TerrainManagerProbe.List;
            if (list == null)
                return true;

            // "for (i = 0; i < m_size; i++) m_buffer[i].TerrainUpdated(...)", with the size the
            // check saw kept for the report.
            for (int i = 0; ; i++)
            {
                int size = list.m_size;
                if (i >= size)
                    break;

                ITerrainManager manager = list.m_buffer[i];
                if (manager == null)
                    TerrainManagerProbe.EmptySlot(i, size, list);

                try
                {
                    manager.TerrainUpdated(heightArea, surfaceArea, zoneArea);
                }
                catch (Exception e)
                {
                    TerrainManagerProbe.Threw(i, manager, e);
                    throw;
                }
            }

            return false;
        }
    }
}
