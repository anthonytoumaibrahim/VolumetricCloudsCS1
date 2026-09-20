using HarmonyLib;
using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Makes the game's "how hard is it raining HERE" tell the truth.
    /// </summary>
    /// <remarks>
    /// WeatherManager.SampleRainIntensity(pos, ignoreWeather) already takes a position -- the
    /// road AIs call it per segment to build up wetness, and AudioManager.UpdateAmbient calls
    /// it at the listener for the rain sound -- but its whole body is "return ignoreWeather ?
    /// 1 : m_currentRain". Scaling the result by <see cref="CloudRain.LocalRain"/> is all it
    /// takes for roads under a gap in the clouds to dry and for the rain to go quiet in a
    /// clearing.
    ///
    /// This is the one place the mod changes what the SIMULATION sees, which is why it has
    /// its own checkbox. It runs on the simulation thread; LocalRain is built for that. With
    /// the feature off, or no city loaded, LocalRain returns 1 and the game is untouched.
    /// Parameter names must match the game's, which is how Harmony binds them.
    /// </remarks>
    [HarmonyPatch(typeof(WeatherManager), nameof(WeatherManager.SampleRainIntensity))]
    public static class SampleRainIntensityPatch
    {
        public static void Postfix(Vector3 pos, bool ignoreWeather, ref float __result)
        {
            // ignoreWeather asks "what if it rained": not ours to localise.
            if (ignoreWeather || __result <= 0f)
                return;

            __result *= CloudRain.LocalRain(pos);
        }
    }
}
