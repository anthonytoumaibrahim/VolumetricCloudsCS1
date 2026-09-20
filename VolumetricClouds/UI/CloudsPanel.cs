using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.UI;
using UnityEngine;
using VolumetricClouds.Lighting;

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

        private readonly List<UIButton> _tabs = new List<UIButton>();
        private readonly List<UIPanel> _pages = new List<UIPanel>();

        public override void Start()
        {
            base.Start();

            atlas = UIBuilder.Atlas;
            backgroundSprite = "MenuPanel2";
            size = new Vector2(PanelWidth, PanelHeight);
            canFocus = true;
            isInteractive = true;

            BuildTitleBar();

            BuildCloudsPage(AddPage("Clouds"));
            BuildLightPage(AddPage("Light"));
            BuildRenderingPage(AddPage("Rendering"));
            BuildHalosPage(AddPage("Halos"));
            BuildGeneralPage(AddPage("General"));

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
            page.size = new Vector2(PanelWidth - 2f * Margin,
                                    PanelHeight - TitleBarHeight - TabHeight - 2f * Margin);
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
        }

        private static void BuildCloudsPage(UIPanel page)
        {
            Rows rows = new Rows(page);

            rows.Percent("Intensity", Settings.Coverage, 0f, 100f, 1f);
            rows.Value("Movement speed", Settings.WindSpeed, 0f, 5f, 0.1f, v => v.ToString("F1") + "x");
            rows.Toggle("Show clouds in the sky", Settings.CloudsVisible);
            rows.Value("Cloud altitude", Settings.CloudAltitude, 200f, 3000f, 50f, Metres);
            rows.Value("Layer thickness", Settings.CloudThickness, 150f, 2000f, 50f, Metres);
            rows.Percent("Break-up (solid to ragged)", Settings.CloudBreakup, 0f, 100f, 5f);
            rows.Value("Break-up detail (big to fine)", Settings.CloudBreakupScale, 1.5f, 10f, 0.25f, v => v.ToString("F2") + "x");
            rows.Value("Weather pattern size", Settings.WeatherTileSize, 3000f, 20000f, 500f, Kilometres);
        }

        private static void BuildLightPage(UIPanel page)
        {
            Rows rows = new Rows(page);

            rows.Toggle("Clouds cast shadows on the ground", Settings.CloudShadows);
            rows.Percent("Shadow darkness", Settings.CloudShadowDarkness, 0f, 95f, 5f);
            rows.Value("Shadow fullness", Settings.CloudShadowFullness, 0.5f, 8f, 0.25f, v => v.ToString("F2") + "x");
            rows.Percent("Cloud brightness", Settings.CloudBrightness, 20f, 300f, 5f);
            rows.Percent("Clouds dim at full overcast to", Settings.MinIllumination, 30f, 100f, 1f);
            rows.Percent("Cloud density", Settings.CloudDensity, 20f, 300f, 5f);
        }

        private static void BuildRenderingPage(UIPanel page)
        {
            Rows rows = new Rows(page);

            rows.Toggle("Raymarched volumetric clouds (off = billboards)", Settings.UseVolumetric);
            rows.Value("Quality (raymarch steps)", Settings.CloudQuality, 16f, 96f, 8f, v => v.ToString("F0"));
            rows.Toggle("Buildings and terrain hide clouds behind them", Settings.CloudDepthOcclusion);
            rows.Value("Billboards: puff count", Settings.CloudPuffCount, 0f, 600f, 25f, v => v.ToString("F0"));
            rows.Value("Billboards: puff size", Settings.CloudPuffSize, 200f, 2500f, 50f, Metres);
        }

        private static void BuildHalosPage(UIPanel page)
        {
            Rows rows = new Rows(page);

            // Distant (batched) street and building lights. With everything at its default the
            // replacement shader reproduces a clear vanilla night, so each slider can be judged
            // against a known starting point. Fog stops at -0.49 because the GAME's shader,
            // still used when the replacement is off, turns anything lower into a solid box.
            rows.Toggle("Customise distant light halos", Settings.HaloEnabled);
            rows.Toggle("Use the replacement halo shader", Settings.HaloReplaceShader);
            rows.Value("Tightness (higher = smaller)", Settings.HaloTightness, 0.5f, 6f, 0.05f, v => v.ToString("F2") + "x");
            rows.Percent("Brightness", Settings.HaloBrightness, 0f, 300f, 5f);
            rows.Percent("Size (world radius)", Settings.HaloRadius, 10f, 150f, 5f);
            rows.Value("Fog amount (0 = clear night)", Settings.HaloFogAmount, HaloOverride.MinSafeFog, 2f, 0.01f, v => v.ToString("F2"));

            // Vehicles and other dynamic lights: the only ones LightSystem.DrawLight handles.
            rows.Toggle("Adjust dynamic lights (vehicles)", Settings.HaloAdjustEnabled);
            rows.Percent("Dynamic light size", Settings.HaloRangeScale, 10f, 200f, 5f);
            rows.Value("Hide dynamic halos within", Settings.HaloNearDistance, 0f, 500f, 10f, Metres);
        }

        private static void BuildGeneralPage(UIPanel page)
        {
            Rows rows = new Rows(page);

            rows.KeyBinding("Open this panel", Settings.ToggleKey);
            rows.Toggle("Show icon in Unified UI", Settings.ShowInUnifiedUI, ModController.RefreshButton);
            rows.Toggle("Debug: project a checkerboard instead of shadows", Settings.DebugChecker);
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
