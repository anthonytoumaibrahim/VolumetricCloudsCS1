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
        private CloudBillboards _billboards;
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

            // One field feeds both the shadow cookie and the visible clouds.
            _field = new CloudDensityField(UnityEngine.Random.Range(1, 100000));

            _lighting = gameObject.AddComponent<CloudLighting>();
            _lighting.Initialise(_field);

            _billboards = gameObject.AddComponent<CloudBillboards>();
            _billboards.Initialise(_field);

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

            // Stays open if it was open -- unless a modal is up, which means the switch was
            // flipped from the game's options screen with the panel left open BEHIND it. A new
            // component is born on top of everything, so showing it there made it jump out
            // over the options (first in-game test: "should not open the modal"). From the
            // options screen the change is saved and the panel waits for its key.
            bool show = wasVisible && !UIView.HasModalInput();

            if (show)
                _panel.Show();
            else
                _panel.Hide();

            _panel.eventVisibilityChanged += OnPanelVisibilityChanged;

            // The old panel was unhooked before it went, so Unified UI heard nothing; tell it.
            UUIIntegration.SetPressed(show);
        }

        private void Update()
        {
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
            if (_hudButton != null)
                _hudButton.OnClicked = () => SetPanelVisible(_panel == null || !_panel.isVisible);
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
                return; // a language chosen on Options -> General does not follow the game's

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
