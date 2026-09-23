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
        /// The clouds are drawn by shaders built for Direct3D 11 only (the bundle holds nothing
        /// else). On a Mac or Linux -- or Windows forced onto OpenGL -- the bundle does not load
        /// or its shaders are unsupported, and ShaderBundle returns null. The mod used to carry
        /// on with its billboard fallback there; the author does not want a player to meet it
        /// that way (1.1.0), so without the raymarch shader nothing starts at all.
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
                    "'); the mod needs Windows with Direct3D 11 and stays off in this city -- nothing of it runs");

            // Once per launch: every city load would be nagging.
            if (_unsupportedShown)
                return;

            _unsupportedShown = true;

            // Two texts. On a Mac or Linux nothing can be done yet, so no invitation to report
            // it. On Windows it is a launch option, a card older than DirectX 10, or a real
            // bug -- the one case worth a report, so that text names the log to send (the
            // panel has a Copy button).
            try
            {
                bool windows = Application.platform == RuntimePlatform.WindowsPlayer;
                string message = windows
                    ? Localization.Get("Mod.UnsupportedWindows", Log.Path)
                    : Localization.Get("Mod.Unsupported");

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
