using System;
using UnifiedUI.Helpers;
using UnityEngine;

namespace VolumetricClouds
{
    /// <summary>
    /// Registers our button with Unified UI.
    /// </summary>
    /// <remarks>
    /// UnifiedUILib.dll ships alongside this mod, so the types always resolve. Registration
    /// is deliberately NOT gated on UUIHelpers.IsUUIEnabled(): that only reports whether the
    /// Unified UI *mod* is enabled, and the library hosts a panel of its own when it isn't
    /// (it picks the newest UnifiedUILib loaded by any mod). Gating on it meant players
    /// without the mod enabled saw every other mod's button in that panel except ours.
    /// </remarks>
    public static class UUIIntegration
    {
        private static UUICustomButton _button;

        public static bool IsRegistered => _button != null;

        public static bool Register(Texture2D icon, Action<bool> onToggle)
        {
            if (_button != null)
                return true;

            try
            {
                bool modEnabled = UUIHelpers.IsUUIEnabled();

                _button = UUIHelpers.RegisterCustomButton(
                    name: "VolumetricClouds",
                    groupName: null,
                    tooltip: "Volumetric Clouds",
                    icon: icon,
                    onToggle: onToggle,
                    onToolChanged: null,
                    hotkeys: new UUIHotKeys { ActivationKey = Settings.ToggleKey });

                Log.Msg("Unified UI: registered=" + (_button != null) +
                        " (Unified UI mod enabled=" + modEnabled +
                        ", icon=" + (icon == null ? "MISSING" : icon.width + "x" + icon.height) + ")");

                return _button != null;
            }
            catch (Exception e)
            {
                Log.Error("Unified UI registration failed; falling back to the HUD button.", e);
                _button = null;
                return false;
            }
        }

        /// <summary>
        /// Tells Unified UI whether our panel is open.
        /// </summary>
        /// <remarks>
        /// Read from UnifiedUILib's IL: the hotkey and a click both end in ButtonBase.Toggle,
        /// which is "if (IsActive) Deactivate() else Activate()" on the BUTTON's own flag -- it
        /// never asks us. So when the panel is closed any other way (its own close button),
        /// the button is left active, the next press "deactivates" a panel that is already
        /// hidden, and only the press after that opens it. Setting IsPressed flips the flag
        /// and the sprites without invoking the toggle callback, so this cannot loop.
        /// </remarks>
        public static void SetPressed(bool pressed)
        {
            if (_button == null)
                return;

            try
            {
                if (_button.IsPressed == pressed)
                    return;

                _button.IsPressed = pressed;
                Log.Msg("Unified UI: button state corrected to " + (pressed ? "active" : "inactive") +
                        " to match the panel");
            }
            catch (Exception e)
            {
                Log.Error("Syncing the Unified UI button state threw.", e);
            }
        }

        public static void Unregister()
        {
            if (_button == null)
                return;

            try
            {
                _button.Release();
            }
            catch (Exception e)
            {
                Log.Error("Releasing the Unified UI button threw.", e);
            }

            _button = null;
        }
    }
}
