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
        private static readonly int IdWindOffset = Shader.PropertyToID("_WindOffset");

        public static float Coverage
        {
            get { return Settings.Coverage != null ? Mathf.Clamp01(Settings.Coverage.value) : 0.5f; }
        }

        public static void Apply(Material material, CloudDensityField field, Texture3D noise)
        {
            float bottom = Settings.CloudAltitude != null ? Settings.CloudAltitude.value : 900f;
            float thickness = Settings.CloudThickness != null ? Settings.CloudThickness.value : 600f;
            float tile = Settings.WeatherTileSize != null ? Settings.WeatherTileSize.value : 10000f;
            float density = Settings.CloudDensity != null ? Settings.CloudDensity.value : 1f;

            material.SetTexture(IdWeatherTex, field.DensityTexture);
            material.SetTexture(IdNoiseTex, noise);
            material.SetFloat(IdCloudBottom, bottom);
            material.SetFloat(IdCloudTop, bottom + Mathf.Max(50f, thickness));
            material.SetFloat(IdThreshold, field.GetThreshold(Coverage));
            material.SetFloat(IdSoftness, CloudDensityField.EdgeSoftness);
            material.SetFloat(IdWeatherTile, Mathf.Max(500f, tile));
            material.SetFloat(IdNoiseTile, NoiseTile);
            material.SetFloat(IdDensityScale, density);
            material.SetFloat(IdAbsorption, Absorption);
            material.SetVector(IdWindOffset, CloudWind.Offset);
        }
    }
}
