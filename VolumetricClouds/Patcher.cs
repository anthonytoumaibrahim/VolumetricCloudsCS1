using System;
using CitiesHarmony.API;
using HarmonyLib;
using UnityEngine;

namespace VolumetricClouds
{
    /// <summary>
    /// Harmony bootstrap. The CitiesHarmony mod provides the actual Harmony assembly at
    /// runtime, so patching is deferred until it reports ready rather than assumed.
    /// </summary>
    public static class Patcher
    {
        private const string HarmonyId = "volumetricclouds.lighting";

        private static bool _patched;

        public static bool IsPatched => _patched;

        public static void PatchOnReady()
        {
            try
            {
                HarmonyHelper.DoOnHarmonyReady(Patch);
            }
            catch (Exception e)
            {
                Log.Error("Could not schedule Harmony patching.", e);
            }
        }

        /// <summary>Prompts the player to subscribe if the Harmony mod is missing.</summary>
        public static void EnsureHarmony()
        {
            try
            {
                if (!HarmonyHelper.IsHarmonyInstalled)
                {
                    Log.Warn("Harmony is NOT installed; light halo options will not work.");
                    HarmonyHelper.EnsureHarmonyInstalled();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void Patch()
        {
            if (_patched)
                return;

            try
            {
                Harmony harmony = new Harmony(HarmonyId);
                harmony.PatchAll(typeof(Patcher).Assembly);
                _patched = true;
                Log.Msg("Harmony patches applied (" + HarmonyId + "). Harmony installed=" + HarmonyHelper.IsHarmonyInstalled);
            }
            catch (Exception e)
            {
                Log.Error("Harmony patching failed; light halo options disabled.", e);
            }
        }

        public static void Unpatch()
        {
            if (!_patched)
                return;

            try
            {
                new Harmony(HarmonyId).UnpatchAll(HarmonyId);
                Log.Msg("Harmony patches removed.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            _patched = false;
        }
    }
}
