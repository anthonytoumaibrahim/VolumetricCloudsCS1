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
        private static readonly int IdFragAmount = Shader.PropertyToID("_FragAmount");
        private static readonly int IdFragThreshold = Shader.PropertyToID("_FragThreshold");
        private static readonly int IdFragPhase = Shader.PropertyToID("_FragPhase");
        private static readonly int IdFragParams = Shader.PropertyToID("_FragParams");
        private static readonly int IdFragEdge = Shader.PropertyToID("_FragEdge");
        private static readonly int IdFragArea = Shader.PropertyToID("_FragArea");
        private static readonly int IdDetailTex = Shader.PropertyToID("_DetailTex");
        private static readonly int IdDetailAmount = Shader.PropertyToID("_DetailAmount");
        private static readonly int IdDetailParams = Shader.PropertyToID("_DetailParams");
        private static readonly int IdDetailLookup = Shader.PropertyToID("_DetailLookup");
        private static readonly int IdDetailTexPhase = Shader.PropertyToID("_DetailTexPhase");

        /// <summary>
        /// The cover in effect: the weather's, or the slider's when it overrides. Never read
        /// Settings.Coverage for rendering -- that is only what the override would ask for.
        /// </summary>
        public static float Coverage
        {
            get { return CloudWeather.Coverage; }
        }

        /// <summary>
        /// The same, in 2% steps, for the CPU-built flat cookie, which rebuilds when the cover
        /// changes: it now drifts continuously with the weather.
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

            ApplyFragments(material, field, weatherTile, thickness);
            ApplyDetail(material, detailScale);
        }

        /// <summary>
        /// Cloud detail (CloudDetail). Off -- or no texture yet -- the amount is written as 0 and
        /// nothing else is looked at: every use of it sits behind that uniform branch.
        /// </summary>
        private static void ApplyDetail(Material material, float breakupScale)
        {
            Texture3D texture = CloudDetail.Texture;
            float amount = texture != null ? CloudDetail.Amount : 0f;

            // Always on the log: it changes what is drawn. Once per state, not per material and
            // not per tick of the slider. (Before the city's texture exists -- the rain can ask
            // first -- there is nothing to say yet; a texture that FAILED is said once.)
            string state = amount > 0f ? "on" : !CloudDetail.On ? "off" : CloudDetail.Failed ? "failed" : null;
            if (state != null && state != _detailLogged)
            {
                _detailLogged = state;
                Log.Msg(state == "on"
                    ? "cloud detail: ON, " + CloudDetail.Describe() + " -- billows every " +
                      CloudDetail.Tile(breakupScale).ToString("F0") + " m, erosion " +
                      (CloudDetail.ErosionPerBreakup * BreakupValue * amount).ToString("F2") + ", edges " +
                      (CloudDetail.Hardness * amount * 100f).ToString("F0") + "% hard"
                    : state == "off"
                        ? "cloud detail: off (the soft look from before)"
                        : "cloud detail: set to " + CloudDetail.Describe() + " but its texture failed: drawn without it");
            }

            material.SetFloat(IdDetailAmount, amount);
            if (amount <= 0f)
                return;

            float tile = CloudDetail.Tile(breakupScale);
            CloudFragments.Style style = CloudFragments.Current;

            material.SetTexture(IdDetailTex, texture);
            material.SetVector(IdDetailParams, new Vector4(
                CloudDetail.ErosionPerBreakup * BreakupValue * amount,
                1f - CloudDetail.Hardness * amount,
                CloudDetail.DensityGradient * amount,
                CloudDetail.TopErosion));

            // A fragment style with its own break-up gets it in the detail's units too.
            material.SetVector(IdDetailLookup, new Vector4(
                1f / tile,
                CloudDetail.Warp,
                style.Erosion >= 0f ? style.Erosion * CloudDetail.ErosionPerBreakup * amount : -1f,
                CloudDetail.TextureIsAlpha ? 1f : 0f));

            // It drifts with the 3D noise (1.25x the weather): its own phase over its own repeat.
            material.SetVector(IdDetailTexPhase, CloudWind.NoisePhase(tile, 1f));
        }

        private static float BreakupValue
        {
            get { return Settings.CloudBreakup != null ? Mathf.Clamp01(Settings.CloudBreakup.value) : Settings.Defaults.Breakup; }
        }

        /// <summary>
        /// The detail texture's mip level for something that covers <paramref name="metres"/>: a
        /// pixel far away, or a texel of the shadow map. 0 while it is finer than a texel.
        /// </summary>
        public static float DetailLod(float metres)
        {
            float scale = Settings.CloudBreakupScale != null ? Settings.CloudBreakupScale.value : Settings.Defaults.BreakupScale;
            float texel = CloudDetail.Tile(scale) / CloudDetail3D.Size;
            return Mathf.Max(0f, Mathf.Log(Mathf.Max(metres, 1e-3f) / texel, 2f));
        }

        /// <summary>
        /// Cloud fragments (CloudFragments). Off, the amount is written as 0 and nothing else is
        /// looked at: every use of them sits behind that uniform branch.
        /// </summary>
        private static void ApplyFragments(Material material, CloudDensityField field, float weatherTile, float thickness)
        {
            bool on = CloudFragments.On;
            CloudFragments.Style style = CloudFragments.Current;
            string logged = on ? style.Name : null;
            if (logged != _fragmentsLogged)
            {
                // Always on the log: it changes what is drawn. Once per switch or style, not per
                // material and not per tick of the amount slider.
                _fragmentsLogged = logged;
                Log.Msg("cloud fragments: " + (on ? "ON, " : "") + CloudFragments.Describe());
            }

            if (!on)
            {
                material.SetFloat(IdFragAmount, 0f);
                return;
            }

            double a = CloudFragments.Angle * System.Math.PI / 180.0;
            double b = CloudFragments.AreaAngle * System.Math.PI / 180.0;
            Vector2 phase = CloudWind.TurnedPhase(weatherTile, CloudFragments.Multiple, CloudFragments.Angle);
            Vector2 areaPhase = CloudWind.TurnedPhase(weatherTile, CloudFragments.AreaMultiple, CloudFragments.AreaAngle);

            // Shares of the sky, solved against what the GPU reads. Fragments are only let into
            // AreaShare of the sky, so their own share is raised to match: the total is about
            // what the slider says. A style let in everywhere gets an area threshold no weather
            // value is under.
            float share = CloudFragments.Share;
            float softness = CloudDensityField.EdgeSoftness;
            bool everywhere = style.AreaShare >= 1f;
            material.SetFloat(IdFragAmount, share);
            material.SetFloat(IdFragThreshold, field.GetThresholdAsDrawn(Mathf.Min(1f, share / style.AreaShare), softness));

            // The fixed shifts are folded into the phases (the shader reads ... - phase).
            material.SetVector(IdFragPhase, new Vector4(
                Wrap(phase.x - CloudFragments.Offset.x), Wrap(phase.y - CloudFragments.Offset.y),
                Wrap(areaPhase.x - CloudFragments.AreaOffset.x), Wrap(areaPhase.y - CloudFragments.AreaOffset.y)));
            material.SetVector(IdFragParams, new Vector4(CloudFragments.Multiple, (float)System.Math.Cos(a), (float)System.Math.Sin(a),
                Mathf.Min(style.Thickness, Mathf.Max(50f, thickness))));
            material.SetVector(IdFragArea, new Vector4(CloudFragments.AreaMultiple, (float)System.Math.Cos(b), (float)System.Math.Sin(b),
                everywhere ? -1f : field.GetThresholdAsDrawn(style.AreaShare, softness)));
            // w: the fragments' own break-up strength, or -1 for the clouds' own.
            material.SetVector(IdFragEdge, new Vector4(style.EdgeBoost, CloudFragments.EdgeReach, style.Density, style.Erosion));
        }

        private static float Wrap(float phase)
        {
            return phase - Mathf.Floor(phase);
        }

        // Starts "off" (null), so a session that starts with fragments says so once.
        private static string _fragmentsLogged;

        // The same for the detail: its first state is always logged.
        private static string _detailLogged;
    }
}
