using HarmonyLib;
using UnityEngine;

namespace VolumetricClouds.Lighting
{
    /// <summary>
    /// Adjusts the size of dynamic lights and suppresses their volume pass near the camera.
    /// Parameter names must match the game's, which is how Harmony binds them. The prefix
    /// returns true: returning false would skip the original and the light with it.
    /// </summary>
    [HarmonyPatch(typeof(LightSystem), nameof(LightSystem.DrawLight), new[]
    {
        typeof(LightType), typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(Color),
        typeof(float), typeof(float), typeof(float), typeof(float), typeof(bool),
    })]
    public static class DrawLightParametersPatch
    {
        public static bool Prefix(Vector3 pos, ref float intensity, ref float range, ref bool volume)
        {
            return HaloAdjuster.Adjust(pos, ref intensity, ref range, ref volume);
        }
    }

    /// <summary>
    /// Counts calls only. Confirmed unused in practice, but kept as a tripwire in case
    /// another code path starts routing through it.
    /// </summary>
    [HarmonyPatch(typeof(LightSystem), nameof(LightSystem.DrawLight), new[] { typeof(LightData) })]
    public static class DrawLightDataPatch
    {
        public static void Prefix()
        {
            HaloAdjuster.CountLightDataCall();
        }
    }
}
