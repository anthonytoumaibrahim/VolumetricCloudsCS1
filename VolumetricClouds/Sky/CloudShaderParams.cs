using UnityEngine;

namespace VolumetricClouds.Sky
{
    /// <summary>
    /// Sets the uniforms declared in CloudCommon.cginc. The visible clouds and the shadow
    /// map both take their density parameters from here and nowhere else, which is what
    /// keeps a cloud and its shadow describing the same thing.
    /// </summary>
    public static class CloudShaderParams
    {
        public const float NoiseTile = 2600f;
        public const float Absorption = 0.03f;

        private static readonly int IdWeatherTex = Shader.PropertyToID("_WeatherTex");
        private static readonly int IdNoiseTex = Shader.PropertyToID("_NoiseTex");
        private static readonly int IdCloudBottom = Shader.PropertyToID("_CloudBottom");
        private static readonly int IdCloudTop = Shader.PropertyToID("_CloudTop");
        private static readonly int IdThreshold = Shader.PropertyToID("_Threshold");
        private static readonly int IdSoftness = Shader.PropertyToID("_Softness");
        private static readonly int IdWeatherTile = Shader.PropertyToID("_WeatherTile");
        private static readonly int IdNoiseTile = Shader.PropertyToID("_NoiseTile");
        private static readonly int IdDensityScale = Shader.PropertyToID("_DensityScale");
        private static readonly int IdAbsorption = Shader.PropertyToID("_Absorption");
        private static readonly int IdDetailStrength = Shader.PropertyToID("_DetailStrength");
        private static readonly int IdDetailScale = Shader.PropertyToID("_DetailScale");
        private static readonly int IdWeatherPhase = Shader.PropertyToID("_WeatherPhase");
        private static readonly int IdNoisePhase = Shader.PropertyToID("_NoisePhase");
        private static readonly int IdDetailPhase = Shader.PropertyToID("_DetailPhase");
        private static readonly int IdRainAmount = Shader.PropertyToID("_RainAmount");
        private static readonly int IdRainThreshold = Shader.PropertyToID("_RainThreshold");
        private static readonly int IdRainSlant = Shader.PropertyToID("_RainSlant");

        /// <summary>
        /// The cover in effect: the weather's, or the slider's when it overrides. Never read
        /// Settings.Coverage for rendering -- that is only what the override would ask for.
        /// </summary>
        public static float Coverage
        {
            get { return CloudWeather.Coverage; }
        }

        /// <summary>
        /// The same, in 2% steps, for the CPU fallbacks (flat cookie, billboards) that rebuild
        /// when the cover changes: it now drifts continuously with the weather.
        /// </summary>
        public static float CoverageStepped
        {
            get { return Mathf.Round(CloudWeather.Coverage * 50f) / 50f; }
        }

        public static void Apply(Material material, CloudDensityField field, Texture3D noise)
        {
            float bottom = Settings.CloudAltitude != null ? Settings.CloudAltitude.value : Settings.Defaults.Altitude;
            float thickness = Settings.CloudThickness != null ? Settings.CloudThickness.value : Settings.Defaults.Thickness;
            float tile = Settings.WeatherTileSize != null ? Settings.WeatherTileSize.value : Settings.Defaults.WeatherTileSize;
            float density = Settings.CloudDensity != null ? Settings.CloudDensity.value : Settings.Defaults.Density;
            float weatherTile = Mathf.Max(500f, tile);
            float detailScale = Settings.CloudBreakupScale != null ? Settings.CloudBreakupScale.value : Settings.Defaults.BreakupScale;

            material.SetTexture(IdWeatherTex, field.DensityTexture);
            material.SetTexture(IdNoiseTex, noise);
            material.SetFloat(IdCloudBottom, bottom);
            material.SetFloat(IdCloudTop, bottom + Mathf.Max(50f, thickness));
            material.SetFloat(IdThreshold, field.GetThreshold(Coverage));
            material.SetFloat(IdSoftness, CloudDensityField.EdgeSoftness);
            material.SetFloat(IdWeatherTile, weatherTile);
            material.SetFloat(IdNoiseTile, NoiseTile);
            material.SetFloat(IdDensityScale, density);
            material.SetFloat(IdAbsorption, Absorption);
            material.SetFloat(IdDetailStrength, Settings.CloudBreakup != null ? Settings.CloudBreakup.value : Settings.Defaults.Breakup);
            material.SetFloat(IdDetailScale, detailScale);

            // The wind as each lookup sees it: how far through ITS repeat the drift has gone,
            // worked out in doubles. The tiles here must be the ones set above -- a phase for
            // one tile read against another is a jump every repeat.
            material.SetVector(IdWeatherPhase, CloudWind.WeatherPhase(weatherTile));
            material.SetVector(IdNoisePhase, CloudWind.NoisePhase(NoiseTile, 1f));
            material.SetVector(IdDetailPhase, CloudWind.NoisePhase(NoiseTile, detailScale));

            // Where it rains (SampleRain): the same field, thresholded for the smaller
            // coverage it rains under, so rain is always beneath cloud.
            material.SetFloat(IdRainAmount, CloudRain.Amount);
            material.SetFloat(IdRainThreshold, field.GetThreshold(CloudRain.RainCoverage));
            material.SetVector(IdRainSlant, CloudRain.Slant);
        }
    }
}
