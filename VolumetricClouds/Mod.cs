using ICities;

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
            Patcher.EnsureHarmony();
            Patcher.PatchOnReady();
        }

        /// <summary>Called by the game via reflection when the mod is disabled.</summary>
        public void OnDisabled()
        {
            Patcher.Unpatch();
        }

        /// <summary>
        /// Called by the game via reflection when the options page opens. The settings
        /// themselves live in the in-game panel, where a change can be judged against the
        /// sky; this page only says where to find them. UIHelperBase has no plain label, so
        /// group titles carry the text.
        /// </summary>
        public void OnSettingsUI(UIHelperBase helper)
        {
            Settings.Init();

            string key = Settings.ToggleKey != null ? Settings.ToggleKey.ToLocalizedString("KEYNAME") : "F4";

            helper.AddGroup("All settings are in the in-game panel.");
            helper.AddGroup("Load a city, then press " + key + " or use the Volumetric Clouds button.");
        }
    }
}
