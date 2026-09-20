using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.UI;
using UnityEngine;
using VolumetricClouds.Lighting;
using VolumetricClouds.Sky;

namespace VolumetricClouds.UI
{
    /// <summary>
    /// The mod's settings, in-game. Everything lives here rather than in the Options menu so
    /// a change can be judged against the sky while it is being made.
    /// </summary>
    public class CloudsPanel : UIPanel
    {
        private const float PanelWidth = 470f;
        private const float PanelHeight = 408f;
        private const float TitleBarHeight = 40f;
        private const float TabHeight = 28f;
        private const float Margin = 14f;

        private const float MinContentHeight = PanelHeight - TitleBarHeight - TabHeight - 2f * Margin;

        private readonly List<UIButton> _tabs = new List<UIButton>();
        private readonly List<UIPanel> _pages = new List<UIPanel>();
        private readonly List<float> _contentHeights = new List<float>();

        private const float StatusInterval = 0.25f;
        private UILabel _weatherStatus;
        private float _nextStatusTime;

        /// <summary>
        /// Keeps the "what is the weather doing to the clouds right now" line current. The
        /// cover is no longer a number the player typed, so the panel has to show it.
        /// </summary>
        public override void Update()
        {
            base.Update();

            if (_weatherStatus == null || !isVisible || Time.time < _nextStatusTime)
                return;

            _nextStatusTime = Time.time + StatusInterval;
            _weatherStatus.text = CloudWeather.Describe();
        }

        public override void Start()
        {
            base.Start();

            atlas = UIBuilder.Atlas;
            backgroundSprite = "MenuPanel2";
            size = new Vector2(PanelWidth, PanelHeight);
            canFocus = true;
            isInteractive = true;

            BuildTitleBar();

            _contentHeights.Add(BuildCloudsPage(AddPage("Clouds")));
            _contentHeights.Add(BuildLightPage(AddPage("Light")));
            _contentHeights.Add(BuildRenderingPage(AddPage("Rendering")));
            _contentHeights.Add(BuildHalosPage(AddPage("Halos")));
            _contentHeights.Add(BuildGeneralPage(AddPage("General")));

            LayoutTabs();
            SelectPage(0);
            CenterOnScreen();
        }

        private void BuildTitleBar()
        {
            UILabel title = UIBuilder.AddLabel(this, "Volumetric Clouds", new Vector3(Margin, Margin), 1.1f);
            title.autoSize = false;
            title.size = new Vector2(PanelWidth - TitleBarHeight, TitleBarHeight);

            UIButton close = AddUIComponent<UIButton>();
            close.atlas = atlas;
            close.size = new Vector2(32f, 32f);
            close.relativePosition = new Vector3(PanelWidth - 36f, 4f);
            close.normalBgSprite = "buttonclose";
            close.hoveredBgSprite = "buttonclosehover";
            close.pressedBgSprite = "buttonclosepressed";
            close.eventClick += (component, e) => Hide();

            // Dragging the title bar moves the whole panel.
            UIDragHandle drag = AddUIComponent<UIDragHandle>();
            drag.target = this;
            drag.size = new Vector2(PanelWidth - 40f, TitleBarHeight);
            drag.relativePosition = Vector3.zero;
        }

        private UIPanel AddPage(string tabName)
        {
            int index = _pages.Count;

            UIButton tab = UIBuilder.AddTab(this, tabName, new Vector2(10f, TabHeight), Vector3.zero);
            tab.eventClick += (component, e) => SelectPage(index);
            _tabs.Add(tab);

            UIPanel page = AddUIComponent<UIPanel>();
            page.size = new Vector2(PanelWidth - 2f * Margin, MinContentHeight);
            page.relativePosition = new Vector3(Margin, TitleBarHeight + TabHeight + Margin);
            _pages.Add(page);

            return page;
        }

        private void LayoutTabs()
        {
            float width = (PanelWidth - 2f * Margin) / _tabs.Count;

            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].size = new Vector2(width, TabHeight);
                _tabs[i].relativePosition = new Vector3(Margin + i * width, TitleBarHeight + 2f);
            }
        }

        private void SelectPage(int index)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].isVisible = i == index;
                UIBuilder.SetTabSelected(_tabs[i], i == index);
            }

            // The panel is as tall as the selected page needs, and never shorter than it
            // started: most pages fit, the Halos one has half as many rows again.
            float content = Mathf.Max(MinContentHeight, _contentHeights[index]);
            _pages[index].height = content;
            height = TitleBarHeight + TabHeight + 2f * Margin + content;
        }

        private float BuildCloudsPage(UIPanel page)
        {
            Rows rows = new Rows(page);

            // Intensity follows the game's weather between these two values; the override
            // below is for skies the game cannot give on demand -- cloudy with no rain.
            _weatherStatus = rows.Status(CloudWeather.Describe());
            rows.Percent("Intensity: clear weather", Settings.WeatherFairCoverage, 0f, 100f, 1f);
            rows.Percent("Intensity: rain", Settings.WeatherOvercastCoverage, 0f, 100f, 1f);
            rows.Toggle("Override the weather with a fixed intensity", Settings.CoverageOverride);
            rows.Percent("Fixed intensity", Settings.Coverage, 0f, 100f, 1f);
            rows.Value("Movement speed", Settings.WindSpeed, 0f, 5f, 0.1f, v => v.ToString("F1") + "x");
            rows.Toggle("Show clouds in the sky", Settings.CloudsVisible);
            rows.Value("Cloud altitude", Settings.CloudAltitude, 200f, 3000f, 50f, Metres);
            rows.Value("Layer thickness", Settings.CloudThickness, 150f, 2000f, 50f, Metres);
            rows.Percent("Break-up (solid to ragged)", Settings.CloudBreakup, 0f, 100f, 5f);
            rows.Value("Break-up detail (big to fine)", Settings.CloudBreakupScale, 1.5f, 10f, 0.25f, v => v.ToString("F2") + "x");
            rows.Value("Weather pattern size", Settings.WeatherTileSize, 3000f, 20000f, 500f, Kilometres);
            return rows.Height;
        }

        private static float BuildLightPage(UIPanel page)
        {
            Rows rows = new Rows(page);

            rows.Toggle("Clouds cast shadows on the ground", Settings.CloudShadows);
            rows.Percent("Shadow darkness", Settings.CloudShadowDarkness, 0f, 95f, 5f);
            rows.Value("Shadow fullness", Settings.CloudShadowFullness, 0.5f, 8f, 0.25f, v => v.ToString("F2") + "x");
            rows.Percent("Cloud brightness", Settings.CloudBrightness, 20f, 300f, 5f);
            rows.Percent("Clouds dim at full overcast to", Settings.MinIllumination, 30f, 100f, 1f);
            rows.Percent("Cloud density", Settings.CloudDensity, 20f, 300f, 5f);
            return rows.Height;
        }

        private static float BuildRenderingPage(UIPanel page)
        {
            Rows rows = new Rows(page);

            rows.Toggle("Raymarched volumetric clouds (off = billboards)", Settings.UseVolumetric);
            rows.Value("Quality (raymarch steps)", Settings.CloudQuality, 16f, 96f, 8f, v => v.ToString("F0"));
            rows.Toggle("Buildings and terrain hide clouds behind them", Settings.CloudDepthOcclusion);
            rows.Value("Billboards: puff count", Settings.CloudPuffCount, 0f, 600f, 25f, v => v.ToString("F0"));
            rows.Value("Billboards: puff size", Settings.CloudPuffSize, 200f, 2500f, 50f, Metres);
            return rows.Height;
        }

        private static float BuildHalosPage(UIPanel page)
        {
            Rows rows = new Rows(page);

            // Street and building lights, at every distance (they are never dynamic). With
            // everything at its default the replacement shader reproduces a clear vanilla
            // night, so each slider can be judged against a known starting point. Fog stops at
            // -0.49 because the GAME's shader, still used when the replacement is off, turns
            // anything lower into a solid box.
            rows.Toggle("Customise street and building light halos", Settings.HaloEnabled);
            rows.Toggle("Use the replacement halo shader", Settings.HaloReplaceShader);
            rows.Value("Tightness (higher = smaller)", Settings.HaloTightness, 0.5f, 6f, 0.05f, v => v.ToString("F2") + "x");
            rows.Percent("Brightness", Settings.HaloBrightness, 0f, 300f, 5f);
            rows.Percent("Size (world radius)", Settings.HaloRadius, 10f, 150f, 5f);
            rows.Value("Fog amount (0 = clear night)", Settings.HaloFogAmount, HaloOverride.MinSafeFog, 2f, 0.01f, v => v.ToString("F2"));

            // The same lamps when close to the camera: percentages OF the three sliders above,
            // in full inside the distance and fading out to nothing at twice it. Replacement
            // shader only.
            rows.Value("Near lights: closer than", Settings.HaloNearLightDistance, 0f, 1000f, 10f,
                v => v <= 0f ? "off" : Metres(v));
            rows.Percent("Near lights: tightness", Settings.HaloNearLightTightness, 50f, 300f, 5f);
            rows.Percent("Near lights: brightness", Settings.HaloNearLightBrightness, 0f, 200f, 1f);
            rows.Percent("Near lights: size", Settings.HaloNearLightRadius, 10f, 200f, 5f);

            // Vehicles and other dynamic lights: the only ones LightSystem.DrawLight handles.
            rows.Toggle("Adjust dynamic lights (vehicles)", Settings.HaloAdjustEnabled);
            rows.Percent("Dynamic light size", Settings.HaloRangeScale, 10f, 200f, 5f);
            rows.Value("Hide dynamic halos within", Settings.DynamicHaloCutoff, 0f, 500f, 10f, Metres);
            return rows.Height;
        }

        private static float BuildGeneralPage(UIPanel page)
        {
            Rows rows = new Rows(page);

            rows.KeyBinding("Open this panel", Settings.ToggleKey);
            rows.Toggle("Show icon in Unified UI", Settings.ShowInUnifiedUI, ModController.RefreshButton);
            rows.Toggle("Debug: project a checkerboard instead of shadows", Settings.DebugChecker);
            return rows.Height;
        }

        private static string Metres(float value)
        {
            return value.ToString("F0") + " m";
        }

        private static string Kilometres(float value)
        {
            return (value / 1000f).ToString("F1") + " km";
        }

        private void CenterOnScreen()
        {
            UIView view = GetUIView();
            relativePosition = new Vector3(
                Mathf.Floor((view.fixedWidth - PanelWidth) / 2f),
                Mathf.Floor((view.fixedHeight - PanelHeight) / 2f));
        }

        /// <summary>Stacks controls down a page, binding each straight to its saved setting.</summary>
        private class Rows
        {
            private readonly UIPanel _page;
            private float _y;

            public Rows(UIPanel page)
            {
                _page = page;
            }

            /// <summary>The height of everything added so far.</summary>
            public float Height
            {
                get { return _y; }
            }

            /// <summary>A full-width line of text the panel keeps up to date itself.</summary>
            public UILabel Status(string text)
            {
                UILabel label = UIBuilder.AddLabel(_page, text, new Vector3(0f, _y + 8f), 0.8f);
                label.autoSize = false;
                label.size = new Vector2(_page.width, 20f);
                label.textColor = new Color32(185, 221, 254, 255);
                _y += UIBuilder.RowHeight;
                return label;
            }

            /// <summary>A setting stored as 0..1 (or a multiplier) but shown as a percentage.</summary>
            public void Percent(string label, SavedFloat setting, float min, float max, float step)
            {
                UIBuilder.AddSlider(_page, _y, label, min, max, step, setting.value * 100f,
                    v => v.ToString("F0") + "%",
                    v => setting.value = v / 100f);
                _y += UIBuilder.RowHeight;
            }

            public void Value(string label, SavedFloat setting, float min, float max, float step,
                Func<float, string> format)
            {
                UIBuilder.AddSlider(_page, _y, label, min, max, step, setting.value, format,
                    v => setting.value = v);
                _y += UIBuilder.RowHeight;
            }

            public void Toggle(string label, SavedBool setting, Action afterChange = null)
            {
                UIBuilder.AddCheckbox(_page, _y, label, setting.value, isChecked =>
                {
                    setting.value = isChecked;
                    if (afterChange != null)
                        afterChange();
                });
                _y += UIBuilder.RowHeight;
            }

            public void KeyBinding(string label, SavedInputKey key)
            {
                UIBuilder.AddKeyBinding(_page, _y, label, key);
                _y += UIBuilder.RowHeight;
            }
        }
    }
}
