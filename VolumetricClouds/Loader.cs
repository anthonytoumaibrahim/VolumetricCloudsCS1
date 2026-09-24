using ColossalFramework.UI;
using ICities;
using UnityEngine;
using VolumetricClouds.Sky;

namespace VolumetricClouds
{
    /// <summary>
    /// Discovered automatically by the game alongside <see cref="Mod"/>. Owns the
    /// controller GameObject for the lifetime of a loaded city.
    /// </summary>
    public class Loader : LoadingExtensionBase
    {
        private static GameObject _root;
        private static bool _unsupportedShown;

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);

            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame)
                return;

            if (_root != null)
                return;

            if (!CanDrawClouds())
            {
                StandDown();
                return;
            }

            _root = new GameObject("VolumetricCloudsController");
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<ModController>();

            Debug.Log("[VolumetricClouds] Controller created.");

            ShowExperimentalNoticeOnce();
        }

        /// <summary>
        /// True where the mod is new (1.1.1) and has run on very few machines: everything that
        /// is not Windows. Drives the once-only dialog below and the note on the options page.
        /// </summary>
        public static bool IsExperimentalPlatform
        {
            get
            {
                RuntimePlatform platform = Application.platform;
                return platform != RuntimePlatform.WindowsPlayer && platform != RuntimePlatform.WindowsEditor;
            }
        }

        /// <summary>
        /// Mac and Linux (1.1.1): one dialog, the first time a city loads on this machine,
        /// saying the platform is new and what to do about a low frame rate. The author's call
        /// (2026-09-24, after his fanless M1 Air ran an empty city at 30-45 fps): "show a
        /// warning ... that shows up once only, not on every playthrough". Once per INSTALL, so
        /// the flag is a setting in VolumetricClouds.xml (PlatformNoticeShown, no UI; a Reset
        /// clears it). Set only after the dialog really showed.
        /// </summary>
        private static void ShowExperimentalNoticeOnce()
        {
            if (!IsExperimentalPlatform)
                return;

            if (Settings.PlatformNoticeShown != null && Settings.PlatformNoticeShown.value)
            {
                Log.Msg("platform notice: already shown on this computer (" + Application.platform + ")");
                return;
            }

            try
            {
                ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
                panel.SetMessage(Mod.DisplayName, Localization.Get("Mod.ExperimentalPlatform"), false);

                if (Settings.PlatformNoticeShown != null)
                {
                    Settings.PlatformNoticeShown.value = true;
                    SettingsXml.SaveNow();
                }

                Log.Msg("platform notice: shown (" + Application.platform +
                        "); remembered in the settings file, so it does not show again");
            }
            catch (System.Exception e)
            {
                Log.Error("Could not show the experimental-platform notice.", e);
            }
        }

        public override void OnLevelUnloading()
        {
            base.OnLevelUnloading();

            if (_root == null)
                return;

            Object.Destroy(_root);
            _root = null;
        }

        /// <summary>
        /// The clouds are drawn by shaders from the bundle built for this platform: Direct3D 11
        /// or OpenGL Core on Windows, Metal or OpenGL Core on a Mac, OpenGL Core on Linux
        /// (ShaderBundle). Where the game runs on anything else, or the card cannot run
        /// shader model 4 / OpenGL 3.3 (the shaders are `#pragma target 3.5`), the raymarch
        /// shader is unsupported and ShaderBundle returns null. The mod used to carry on with
        /// its billboard fallback there; the author does not want a player to meet it that way
        /// (1.1.0), so without the raymarch shader nothing starts at all.
        /// </summary>
        private static bool CanDrawClouds()
        {
            return ShaderBundle.Get(ShaderBundle.Raymarch) != null;
        }

        /// <summary>
        /// No controller means nothing of ours runs in this city: no clouds, no panel or button,
        /// the game's rain, fog, lightning and halos untouched. The Harmony patches applied in
        /// OnEnabled are inert without it (the rain patch returns the game's own value while no
        /// sky has published a snapshot; the light patches only count). The save gets nothing.
        /// </summary>
        private static void StandDown()
        {
            // Always on: SystemReport, which would have named the machine, belongs to the
            // controller that is not being created.
            Log.Msg("NOT SUPPORTED here: the cloud shader cannot run (" + SystemInfo.operatingSystem +
                    ", graphics " + SystemInfo.graphicsDeviceType + ", GPU '" + SystemInfo.graphicsDeviceName +
                    "'); the mod needs Direct3D 11 or OpenGL 3.3 on Windows, Metal or OpenGL 3.3 on a Mac, " +
                    "OpenGL 3.3 on Linux, and stays off in this city -- nothing of it runs");

            // Once per launch: every city load would be nagging.
            if (_unsupportedShown)
                return;

            _unsupportedShown = true;

            // Three texts. Windows: a launch option, a card older than DirectX 10, or a real
            // bug -- the one case worth a report, so that text names the log to send (the
            // panel has a Copy button). Linux: what the mod needs, and the way out through
            // Proton. A Mac, or anything else: what the mod needs, and no invitation to report
            // (the author's rule for the non-Windows texts).
            try
            {
                string message;
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.WindowsEditor:
                        message = Localization.Get("Mod.UnsupportedWindows", Log.Path);
                        break;
                    case RuntimePlatform.LinuxPlayer:
                    case RuntimePlatform.LinuxEditor:
                        message = Localization.Get("Mod.UnsupportedLinux");
                        break;
                    default:
                        message = Localization.Get("Mod.Unsupported");
                        break;
                }

                ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
                panel.SetMessage(Mod.DisplayName, message, false);
            }
            catch (System.Exception e)
            {
                Log.Error("Could not show the 'not supported' message.", e);
            }
        }
    }
}
