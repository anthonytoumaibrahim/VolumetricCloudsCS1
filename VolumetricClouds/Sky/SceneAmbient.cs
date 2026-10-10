using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// The scene's sky ambient as the clouds, and as the air under them, take it (gamma, as
    /// RenderSettings holds it).
    /// </summary>
    /// <remarks>
    /// Real Light (the author's own lighting mod) writes a much brighter trilight over the game's for
    /// the city's shade -- ~20x by day with Render It!'s sun -- which flattened the clouds' shaded side
    /// into their sunlit one. While it does, it publishes the game's own colour as the global
    /// _RLGameAmbientSky with _RLAmbientHeld 1; the clouds keep that, the light they were tuned in.
    ///
    /// The rain curtains, the rain streaks and the fog do NOT (<see cref="Air"/>): they hang in the
    /// city's air and are seen against the city, so they take the light the city is lit with. Given
    /// the game's colour too, they were lit ~20x dimmer than the city behind them, and the curtains
    /// all but vanished ("when it rains it's super clear", 2026-10-09).
    ///
    /// Real Light's own sky is drawn as bright as the sun the city is really lit with. The game
    /// combines a light's colour and intensity before it linearises them ((colour x intensity).linear,
    /// GraphicsSettings.lightsUseLinearIntensity false), while the clouds take colour.linear x
    /// intensity: with an intensity of 4.36 the real sun is 5.89x theirs. Against the game's sky,
    /// which never passes white, that never showed; against Real Light's (2-3x white) the clouds'
    /// shaded bases were ~8x darker than the sky behind them, which came through every thin part
    /// and flattened them (2026-10-10). While its sky is drawn Real Light publishes that ratio as
    /// _RLCloudLight (<see cref="CloudLight"/>), eased in with the sky's daytime opacity; the clouds'
    /// sun and shade are both multiplied by it, so their own contrast is as tuned.
    ///
    /// Without Real Light the flag reads 0, the two are the same colour, the multiplier is 1, and
    /// nothing changes.
    /// </remarks>
    internal static class SceneAmbient
    {
        private static readonly int IdRealLightHeld = Shader.PropertyToID("_RLAmbientHeld");
        private static readonly int IdRealLightGameSky = Shader.PropertyToID("_RLGameAmbientSky");
        private static readonly int IdRealLightCloudLight = Shader.PropertyToID("_RLCloudLight");

        /// <summary>The clouds': the game's own sky ambient, also while Real Light brightens the city's.</summary>
        public static Color Sky()
        {
            if (IsHeld)
                return Shader.GetGlobalColor(IdRealLightGameSky);

            return Air();
        }

        /// <summary>The air under the clouds' (rain, streaks, fog): the ambient the city is lit with now.</summary>
        public static Color Air()
        {
            return RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat
                ? RenderSettings.ambientLight
                : RenderSettings.ambientSkyColor;
        }

        /// <summary>True while Real Light holds the game's colour apart from the city's.</summary>
        public static bool IsHeld
        {
            get { return Shader.GetGlobalFloat(IdRealLightHeld) > 0.5f; }
        }

        /// <summary>
        /// What the clouds' sun and shade are multiplied by to stand in the light Real Light's sky is
        /// drawn in; 1 without it.
        /// </summary>
        public static float CloudLight
        {
            get
            {
                float k = Shader.GetGlobalFloat(IdRealLightCloudLight);
                return k > 0f ? k : 1f;
            }
        }
    }
}
