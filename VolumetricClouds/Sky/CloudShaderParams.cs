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
        private static readonly int IdCloudStyle = Shader.PropertyToID("_CloudStyle");
        private static readonly int IdCumulusTex = Shader.PropertyToID("_CumulusTex");
        private static readonly int IdWeatherScoreTex = Shader.PropertyToID("_WeatherScoreTex");
        private static readonly int IdCumulusShape = Shader.PropertyToID("_CumulusShape");
        private static readonly int IdCumulusNoise = Shader.PropertyToID("_CumulusNoise");
        private static readonly int IdCumulusPhase = Shader.PropertyToID("_CumulusPhase");
        private static readonly int IdCumulusCurve0 = Shader.PropertyToID("_CumulusCurve0");
        private static readonly int IdCumulusCurve1 = Shader.PropertyToID("_CumulusCurve1");
        private static readonly int IdCumulusCurveKeys = Shader.PropertyToID("_CumulusCurveKeys");
        private static readonly int IdLayerShape = Shader.PropertyToID("_LayerShape");
        private static readonly int IdLayerShapeMax = Shader.PropertyToID("_LayerShapeMax");
        private static readonly int IdDetailLodShift = Shader.PropertyToID("_DetailLodShift");

        /// <summary>Metres the Cumulus slab reaches above the tallest cloud: the march must not clip its tops.</summary>
        private const float CumulusTopMargin = 25f;

        /// <summary>
        /// The top of what is marched, in metres above the base: the layer's thickness for
        /// Classic; for Cumulus the tallest heap the curve allows or the thickest the layer gets,
        /// whichever is higher.
        /// </summary>
        public static float LayerHeight
        {
            get
            {
                float thickness = Mathf.Max(50f, Settings.CloudThickness != null ? Settings.CloudThickness.value : Settings.Defaults.Thickness);
                if (CloudStyle.Drawn)
                    return Mathf.Max(CloudStyle.Tallest + CumulusTopMargin, thickness * CloudStyle.LayerThickest);

                return thickness;
            }
        }

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
            material.SetFloat(IdCloudTop, bottom + LayerHeight);
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

            // The layer is drawn in both styles, with all of Classic's settings: its fragments and
            // its detail too. Under Cumulus the heaps come on top (ApplyStyle).
            ApplyStyle(material, field, weatherTile, thickness, detailScale);
            ApplyFragments(material, field, weatherTile, thickness);
            ApplyDetail(material, detailScale);
        }

        /// <summary>
        /// The cloud style (CloudStyle). Classic -- or Cumulus before its noise exists -- writes
        /// 0 and nothing else: every use of the Cumulus uniforms sits behind that branch.
        /// </summary>
        private static void ApplyStyle(Material material, CloudDensityField field, float weatherTile, float thickness, float breakupScale)
        {
            bool drawn = CloudStyle.Drawn;

            // Always on the log: it changes what is drawn. Once per state. Chosen but not there
            // yet is the few seconds its noise takes to make (CloudVolume), or a failure, said once.
            string state = drawn ? "cumulus" : !CloudStyle.IsCumulus ? "classic" : CloudStyle.Failed ? "failed" : "waiting";
            if (state != _styleLogged)
            {
                _styleLogged = state;
                Log.Msg(state == "cumulus"
                    ? "cloud style: Cumulus -- " + CloudStyle.DescribeShape()
                    : state == "classic"
                        ? "cloud style: Classic (the layer as it has always been drawn)"
                        : state == "failed"
                            ? "cloud style: Cumulus chosen but its noise failed: drawn Classic"
                            : "cloud style: Cumulus chosen; its noise is being made, drawn Classic until it is there");
            }

            material.SetFloat(IdCloudStyle, drawn ? 1f : 0f);
            if (!drawn)
                return;

            float density = Settings.CloudDensity != null ? Settings.CloudDensity.value : Settings.Defaults.Density;
            float[] c0 = CloudStyle.Cubics[0];
            float[] c1 = CloudStyle.Cubics[1];

            // The heaps' coverage: a line in the map's normal scores (its own ranks, so every city
            // gets the same amount at the same slider), no more heaps above HeapCap.
            float lineA, lineB;
            CloudStyle.Line(CloudStyle.HeapEdge(Coverage), out lineA, out lineB);

            // The layer beside them: Classic's, its thickness following the map. `lod` is the
            // Cumulus noise's; the layer's detail is finer by the ratio of their texels.
            float[] layerLine = CloudStyle.LayerLineCached;
            material.SetVector(IdLayerShape, new Vector4(Mathf.Max(50f, thickness), layerLine[0], layerLine[1], CloudStyle.LayerThinnest));
            material.SetFloat(IdLayerShapeMax, CloudStyle.LayerThickest);
            material.SetFloat(IdDetailLodShift, Mathf.Log(CloudStyle.NoiseTile / CloudDetail.Tile(breakupScale), 2f));

            material.SetTexture(IdCumulusTex, CloudStyle.Texture);
            material.SetTexture(IdWeatherScoreTex, field.ScoreTexture);
            material.SetVector(IdCumulusShape, new Vector4(lineA, lineB, CloudStyle.CoverageMax, 1f / CloudStyle.Span));

            // Density in the units the march multiplies by _Absorption: the default Density slider
            // is BaseExtinction per metre, and the slider scales it as it scales Classic's.
            material.SetVector(IdCumulusNoise, new Vector4(
                1f / CloudStyle.NoiseTile, CloudStyle.ErosionDepth, 1f / (1f - CloudStyle.Hardness),
                CloudStyle.BaseExtinction / Absorption * density / Settings.Defaults.Density));

            // It drifts with the 3D noise (1.25x the weather): its own phase over its own repeat.
            material.SetVector(IdCumulusPhase, CloudWind.NoisePhase(CloudStyle.NoiseTile, 1f));

            material.SetVector(IdCumulusCurve0, new Vector4(c0[0], c0[1], c0[2], c0[3]));
            material.SetVector(IdCumulusCurve1, new Vector4(c1[0], c1[1], c1[2], c1[3]));
            material.SetVector(IdCumulusCurveKeys, new Vector4(
                CloudStyle.CurveTime[0], CloudStyle.CurveTime[1], CloudStyle.CurveTime[2], CloudStyle.CurveValue[2]));
        }

        /// <summary>
        /// Cloud detail (CloudDetail), on the layer in both styles. Off -- or no texture yet -- the
        /// amount is written as 0 and nothing else is looked at: every use of it sits behind that
        /// uniform branch.
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
        /// Metres per texel of the noise the style in use carves with: cloud detail's texture for
        /// Classic, the Cumulus noise for Cumulus.
        /// </summary>
        public static float NoiseTexel
        {
            get
            {
                if (CloudStyle.Drawn)
                    return CloudStyle.NoiseTile / CumulusNoise3D.Size;

                float scale = Settings.CloudBreakupScale != null ? Settings.CloudBreakupScale.value : Settings.Defaults.BreakupScale;
                return CloudDetail.Tile(scale) / CloudDetail3D.Size;
            }
        }

        /// <summary>
        /// The style's noise mip level for something that covers <paramref name="metres"/>: a
        /// pixel far away, or a texel of the shadow map. 0 while it is finer than a texel.
        /// </summary>
        public static float DetailLod(float metres)
        {
            // Under Cumulus unclamped: the layer's detail, finer than the Cumulus noise, clamps its
            // own level in the shader (SampleDensityLod).
            float lod = Mathf.Log(Mathf.Max(metres, 1e-3f) / NoiseTexel, 2f);
            return CloudStyle.Drawn ? lod : Mathf.Max(0f, lod);
        }

        /// <summary>
        /// Cloud fragments (CloudFragments), round the layer's clouds in both styles. Off, the
        /// amount is written as 0 and nothing else is looked at: every use of them sits behind that
        /// uniform branch.
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

        // And the style's.
        private static string _styleLogged;
    }
}
