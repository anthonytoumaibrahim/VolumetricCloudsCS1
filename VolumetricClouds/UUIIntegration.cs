using System;
using UnifiedUI.Helpers;
using UnityEngine;

namespace VolumetricClouds
{
    /// <summary>
    /// Registers our button with Unified UI. UnifiedUILib.dll ships alongside this
    /// mod, so the types always resolve; IsUUIEnabled() reports whether the UUI mod
    /// itself is actually running.
    /// </summary>
    public static class UUIIntegration
    {
        private static UUICustomButton _button;

        public static bool IsRegistered => _button != null;

        public static bool IsAvailable()
        {
            try
            {
                return UUIHelpers.IsUUIEnabled();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VolumetricClouds] Unified UI availability check failed: " + e.Message);
                return false;
            }
        }

        public static bool Register(Texture2D icon, Action<bool> onToggle)
        {
            if (_button != null)
                return true;

            if (!IsAvailable())
                return false;

            try
            {
                _button = UUIHelpers.RegisterCustomButton(
                    name: "VolumetricClouds",
                    groupName: null,
                    tooltip: "Volumetric Clouds",
                    icon: icon,
                    onToggle: onToggle,
                    onToolChanged: null,
                    hotkeys: new UUIHotKeys { ActivationKey = Settings.ToggleKey });

                return _button != null;
            }
            catch (Exception e)
            {
                Debug.LogError("[VolumetricClouds] Unified UI registration failed.");
                Debug.LogException(e);
                _button = null;
                return false;
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
                Debug.LogException(e);
            }

            _button = null;
        }
    }
}
