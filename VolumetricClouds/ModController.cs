using ColossalFramework;
using ColossalFramework.Globalization;
using ColossalFramework.UI;
using UnityEngine;
using VolumetricClouds.Lighting;
using VolumetricClouds.Sky;
using VolumetricClouds.UI;

namespace VolumetricClouds
{
    /// <summary>
    /// Watches for the toggle hotkey and owns the settings panel and its button.
    /// </summary>
    public class ModController : MonoBehaviour
    {
        public static ModController Instance { get; private set; }

        private CloudsPanel _panel;
        private ModButton _hudButton;
        private CloudLighting _lighting;
        private CloudVolume _volume;
        private CloudDensityField _field;
        private bool _hotkeyWasDown;

        /// <summary>Rebuilds the button after the UUI preference changes.</summary>
        public static void RefreshButton()
        {
            if (Instance != null)
                Instance.BuildButton();
        }

        /// <summary>
        /// Rebuilds the in-game panel after "Show advanced options" changes: which tabs it has
        /// is decided when it is built. Safe with no city loaded -- the switch lives on the
        /// options page, which is reachable from the main menu.
        /// </summary>
        public static void RebuildPanel()
        {
            if (Instance != null)
                Instance.CreatePanel();
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            Settings.Init();
            Log.ReportLevel();
            SystemReport.Write();
            SettingsCatalog.LogLayout();

            CreatePanel();

            // The panel's words are fixed when it is built; when the game's language changes and
            // ours follows it, build it again. (A static event: removed in OnDestroy.)
            LocaleManager.eventLocaleChanged += OnLocaleChanged;

            // This city's pattern: its save's, or a new one. Before anything is generated.
            SkySaveData.BeginCity();

            // One field feeds both the shadow cookie and the visible clouds.
            _field = new CloudDensityField(SkyPattern.FieldSeed);

            _lighting = gameObject.AddComponent<CloudLighting>();
            _lighting.Initialise(_field);

            _volume = gameObject.AddComponent<CloudVolume>();
            _volume.Initialise(_field);
            gameObject.AddComponent<GameCloudDome>();
            gameObject.AddComponent<RainDrops>().Initialise(_field);
            gameObject.AddComponent<GameRain>();
            gameObject.AddComponent<CloudLightning>().Initialise(_field);
            gameObject.AddComponent<TerrainHeightMap>();
            gameObject.AddComponent<GameFog>();
            gameObject.AddComponent<GameStars>();
            gameObject.AddComponent<HaloController>();
            gameObject.AddComponent<HaloOverride>();
            gameObject.AddComponent<FogLampMap>();

            BuildButton();
        }

        /// <summary>
        /// Creates the panel, replacing the one that exists. The replacement opens where the old
        /// one stood and stays open if it was: from the player's side the tabs change, nothing
        /// else.
        /// </summary>
        private void CreatePanel()
        {
            bool wasVisible = false;
            Vector3? position = null;

            if (_panel != null)
            {
                wasVisible = _panel.isVisible;
                position = _panel.relativePosition;
                _panel.eventVisibilityChanged -= OnPanelVisibilityChanged;
                Destroy(_panel.gameObject);
            }

            _panel = UIView.GetAView().AddUIComponent(typeof(CloudsPanel)) as CloudsPanel;
            if (_panel == null)
                return;

            _panel.InitialPosition = position;

            // Stays open if it was open. From the game's options screen (a modal) that needs
            // one more step: a new component is born on top of everything, so the rebuilt panel
            // jumped out over the options (first in-game test: "should not open the modal").
            // It stays open BEHIND them instead, where the old one was.
            if (wasVisible)
            {
                _panel.Show();
                KeepModalOnTop();
            }
            else
            {
                _panel.Hide();
            }

            _panel.eventVisibilityChanged += OnPanelVisibilityChanged;

            // The old panel was unhooked before it went, so Unified UI heard nothing; tell it.
            UUIIntegration.SetPressed(wasVisible);
        }

        /// <summary>
        /// Puts the open modal (the options screen) and its dimming layer back above
        /// everything, our new panel included. Read from the IL of UIView.BringToFront: a
        /// top-level component is re-numbered to the top, and when it is THE modal component
        /// the modal effect goes directly under it. Only those two move; the new panel stays
        /// above the rest of the game's UI, so it is simply there when the options close.
        /// </summary>
        private static void KeepModalOnTop()
        {
            try
            {
                UIComponent modal = UIView.GetModalComponent();
                if (modal == null)
                    return;

                UIComponent root = modal.GetRootContainer() ?? modal;
                UIComponent effect = UIView.GetAView().panelsLibraryModalEffect;

                if (effect != null && effect != root && effect.isVisible)
                    effect.BringToFront();

                root.BringToFront();

                Log.Msg("panel: rebuilt behind the open '" + root.name + "'" +
                        (effect != null && effect.isVisible ? " and its dimming" : ""));
            }
            catch (System.Exception e)
            {
                Log.Warn("panel: could not keep the options screen on top: " + e.Message);
            }
        }

        private void Update()
        {
            ApplyNewPattern();

            // When the button is registered, Unified UI owns the hotkey (we hand it
            // over as UUIHotKeys.ActivationKey). Polling it here as well would toggle
            // the panel twice per press, which looks like the key doing nothing.
            if (UUIIntegration.IsRegistered)
                return;

            // Don't steal the key while a text field has focus or a modal is up,
            // otherwise typing into a rename box toggles the panel.
            if (UIView.HasModalInput() || UIView.HasInputFocus())
            {
                _hotkeyWasDown = false;
                return;
            }

            bool down = IsHotkeyDown();
            if (down && !_hotkeyWasDown)
                SetPanelVisible(_panel != null && !_panel.isVisible);

            _hotkeyWasDown = down;
        }

        /// <summary>
        /// "Reset cloud pattern": a pattern finished on its worker thread goes to everything
        /// drawn from the old one in the same frame -- the field (clouds, shadows, rain, fog,
        /// lightning) and the 3D noise -- so nothing is ever half old, half new.
        /// </summary>
        private void ApplyNewPattern()
        {
            bool wasWorking = SkyPattern.Working;

            CloudDensityField field;
            byte[] noise;
            Color32[][] detail;
            Color32[][] cumulus;
            if (SkyPattern.TryTake(_volume == null || _volume.CanReplaceNoise, out field, out noise, out detail, out cumulus))
            {
                if (_field != null)
                    _field.Adopt(field);
                if (_volume != null)
                    _volume.ReplaceNoise(noise, detail, cumulus);
            }

            // The button greys out while a pattern is on its way; give it back.
            if (wasWorking && !SkyPattern.Working)
                SettingsCatalog.RefreshAllUIs();
        }

        private static bool IsHotkeyDown()
        {
            SavedInputKey key = Settings.ToggleKey;
            if (key == null || key.Key == KeyCode.None)
                return false;

            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            return Input.GetKey(key.Key)
                && control == key.Control
                && shift == key.Shift
                && alt == key.Alt;
        }

        private void BuildButton()
        {
            UUIIntegration.Unregister();
            DestroyHudButton();

            bool registered = false;
            if (Settings.ShowInUnifiedUI != null && Settings.ShowInUnifiedUI.value)
                registered = UUIIntegration.Register(IconLoader.Load(), OnUUIToggled);

            // A fresh button starts inactive, but this also runs from the "Show icon in
            // Unified UI" checkbox -- which lives in the panel, so the panel is open.
            if (registered && _panel != null)
                UUIIntegration.SetPressed(_panel.isVisible);

            // Falling back also covers "wanted UUI but it isn't installed", which
            // would otherwise leave the player with no button at all.
            if (!registered)
                CreateHudButton();

            Log.Msg("panel button: " + (registered ? "Unified UI" : "HUD button") +
                    " (Unified UI wanted=" + (Settings.ShowInUnifiedUI != null && Settings.ShowInUnifiedUI.value) + ")");
        }

        private void CreateHudButton()
        {
            _hudButton = UIView.GetAView().AddUIComponent(typeof(ModButton)) as ModButton;
            if (_hudButton == null)
                return;

            _hudButton.OnClicked = () => SetPanelVisible(_panel == null || !_panel.isVisible);
        }

        /// <summary>
        /// Puts the HUD button where the settings say, after a Reset or a hand edit of
        /// VolumetricClouds.xml. A drag writes those settings itself and does not come here.
        /// </summary>
        public static void PlaceHudButton()
        {
            if (Instance != null && Instance._hudButton != null)
                Instance._hudButton.Place();
        }

        private void DestroyHudButton()
        {
            if (_hudButton == null)
                return;

            Destroy(_hudButton.gameObject);
            _hudButton = null;
        }

        /// <summary>
        /// The panel is the single source of truth for "open". Unified UI keeps a flag of its
        /// own and toggles on that, so it is told about every change, whoever made it -- the
        /// close button, the hotkey, the HUD button. See <see cref="UUIIntegration.SetPressed"/>.
        /// </summary>
        private void OnPanelVisibilityChanged(UIComponent component, bool visible)
        {
            UUIIntegration.SetPressed(visible);
        }

        private void OnUUIToggled(bool active)
        {
            Log.Msg("panel: Unified UI toggled it " + (active ? "on" : "off"));

            if (_panel == null)
                return;

            if (active)
            {
                _panel.Show();
                _panel.BringToFront();
            }
            else
            {
                _panel.Hide();
            }
        }

        public void SetPanelVisible(bool visible)
        {
            if (_panel == null)
                return;

            if (visible)
            {
                _panel.Show();
                _panel.BringToFront();
            }
            else
            {
                _panel.Hide();
            }
        }

        private void OnLocaleChanged()
        {
            if (Settings.Language != null && Settings.Language.value != 0)
                return; // a language chosen on the options page does not follow the game's

            Log.Msg("localization: the game's language changed to '" + Localization.CurrentCode + "'; rebuilding the panel");
            CreatePanel();
        }

        private void OnDestroy()
        {
            LocaleManager.eventLocaleChanged -= OnLocaleChanged;
            UUIIntegration.Unregister();
            DestroyHudButton();

            if (_panel != null)
            {
                Destroy(_panel.gameObject);
                _panel = null;
            }

            if (Instance == this)
                Instance = null;
        }
    }
}
