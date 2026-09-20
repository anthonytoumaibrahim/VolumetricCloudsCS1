using ColossalFramework;
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

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            Settings.Init();

            _panel = UIView.GetAView().AddUIComponent(typeof(CloudsPanel)) as CloudsPanel;
            _panel.Hide();

            // One field feeds both the shadow cookie and the visible clouds.
            _field = new CloudDensityField(UnityEngine.Random.Range(1, 100000));

            _lighting = gameObject.AddComponent<CloudLighting>();
            _lighting.Initialise(_field);

            _billboards = gameObject.AddComponent<CloudBillboards>();
            _billboards.Initialise(_field);

            _volume = gameObject.AddComponent<CloudVolume>();
            _volume.Initialise(_field);
            gameObject.AddComponent<HaloController>();
            gameObject.AddComponent<HaloFogOverride>();

            BuildButton();
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

        private void OnUUIToggled(bool active)
        {
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

        private void OnDestroy()
        {
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
