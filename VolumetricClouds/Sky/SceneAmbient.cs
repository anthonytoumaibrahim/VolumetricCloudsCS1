using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The scene's sky ambient as the clouds and the rain take it (gamma, as RenderSettings holds it).
    /// </summary>
    /// <remarks>
    /// Real Light (the author's own lighting mod) writes a much brighter trilight over the game's for
    /// the city's shade -- ~20x by day with Render It!'s sun -- which flattened the clouds' shaded side
    /// into their sunlit one. While it does, it publishes the game's own colour as the global
    /// _RLGameAmbientSky with _RLAmbientHeld 1; the clouds keep that, the light they were tuned in.
    /// Without Real Light the flag reads 0 and nothing changes.
    /// </remarks>
    internal static class SceneAmbient
    {
        private static readonly int IdRealLightHeld = Shader.PropertyToID("_RLAmbientHeld");
        private static readonly int IdRealLightGameSky = Shader.PropertyToID("_RLGameAmbientSky");

        public static Color Sky()
        {
            if (Shader.GetGlobalFloat(IdRealLightHeld) > 0.5f)
                return Shader.GetGlobalColor(IdRealLightGameSky);

            return RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat
                ? RenderSettings.ambientLight
                : RenderSettings.ambientSkyColor;
        }
    }
}
