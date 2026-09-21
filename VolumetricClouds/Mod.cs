using ICities;
using VolumetricClouds.UI;

namespace VolumetricClouds
{
    public class Mod : IUserMod
    {
        public string Name => "Volumetric Clouds";
        public string Description => "Adds volumetric clouds to the game.";

        /// <summary>Called by the game via reflection when the mod is enabled.</summary>
        public void OnEnabled()
        {
            Log.Start();
            Log.Msg("Mod enabled. Log file: " + Log.Path);

            // Reads VolumetricClouds.xml (or imports the old .cgs, once) and starts the saver
            // that writes changes back and picks up edits made to the file by hand.
            Settings.Init();
            SettingsXml.StartSaver(); // again after a disable/enable, when Init has nothing left to do

            Patcher.EnsureHarmony();
            Patcher.PatchOnReady();
        }

        /// <summary>Called by the game via reflection when the mod is disabled.</summary>
        public void OnDisabled()
        {
            Patcher.Unpatch();
            SettingsXml.Shutdown();
        }

        /// <summary>
        /// Called by the game via reflection when the options page is (re)built -- at least
        /// once per scene, and again whenever any mod is enabled or disabled. Everything that
        /// is configured once lives here; the overrides and the live look are in the in-game
        /// panel, where they can be judged against the sky.
        /// </summary>
        public void OnSettingsUI(UIHelperBase helper)
        {
            OptionsUI.Build(helper);
        }
    }
}
