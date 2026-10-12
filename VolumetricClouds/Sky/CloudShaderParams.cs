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
        private static readonly int IdMixMap = Shader.PropertyToID("_MixMap");
        private static readonly int IdMixScale = Shader.PropertyToID("_MixScale");
        private static readonly int IdMixCarve = Shader.PropertyToID("_MixCarve");
        private static readonly int IdMixNoise = Shader.PropertyToID("_MixNoise");
        private static readonly int IdMixPhase = Shader.PropertyToID("_MixPhase");
        private static readonly int IdMixNoise2 = Shader.PropertyToID("_MixNoise2");
        private static readonly int IdMixPhase2 = Shader.PropertyToID("_MixPhase2");
        private static readonly int IdMixEnvelope = Shader.PropertyToID("_MixEnvelope");
        private static readonly int IdMixThreshold = Shader.PropertyToID("_MixThreshold");

        /// <summary>Metres the Cumulus slab reaches above the tallest cloud: the march must not clip its tops.</summary>
        private const float CumulusTopMargin = 25f;

        /// <summary>
        /// The top of what is marched, in metres above the base: the layer's thickness for
        /// Classic; for Cumulus the tallest heap the curve allows or the thickest the layer gets,
        /// whichever is higher; for Mixed its tallest separate cloud or the thickest layer.
        /// </summary>
        public static float LayerHeight
        {
            get
            {
                float thickness = Mathf.Max(50f, Settings.CloudThickness != null ? Settings.CloudThickness.value : Settings.Defaults.Thickness);
                if (CloudStyle.MixedDrawn)
                    return Mathf.Max(CloudMixMap.BakedTallest + CumulusTopMargin, thickness * CloudStyle.LayerThickest);
                if (CloudStyle.Drawn)
                    return Mathf.Max(CloudStyle.TallestNow + CumulusTopMargin, thickness * CloudStyle.LayerThickest);

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
        /// The cover the Classic LAYER is cut at (`_Threshold`): the cover in effect, times the
        /// "Classic layer" share while Cumulus is drawn (1.3.0: 100% = as in 1.2, 0% = the heaps
        /// alone). The heaps, the rain and lightning's cloud test keep the full cover. Under Mixed the
        /// layer's own cover for the intensity (CloudMix.LayerCover; the type tips it per stretch of sky),
        /// times the same "Classic layer" share (CloudStyle.MixedLayer).
        /// </summary>
        public static float LayerCoverage
        {
            get
            {
                if (CloudStyle.MixedDrawn)
                    return CloudStyle.MixedLayer(Coverage);
                return CloudStyle.Drawn ? Coverage * CloudStyle.LayerAmount : Coverage;
            }
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
            material.SetFloat(IdThreshold, field.GetThreshold(LayerCoverage));
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
            ApplyEmptySky(material, field);
        }

        private static readonly int IdEmptySkyTex = Shader.PropertyToID("_EmptySkyTex");
        private static readonly int IdEmptySky = Shader.PropertyToID("_EmptySky");

        /// <summary>
        /// Below this share of empty cells the skip is not asked for: at a full overcast a ray would
        /// only pay for asking (one look per cell it crosses) and never jump.
        /// </summary>
        private const float EmptySkyMinShare = 0.02f;

        private static string _emptySkyLogged;
        private static float _emptyShareThreshold = float.NaN;
        private static float _emptyShareLineA, _emptyShareLineB;
        private static bool _emptyShareCumulus;
        private static int _emptyShareVersion = -1;
        private static float _emptyShare;
        private static byte[] _emptyShareMixBytes;

        /// <summary>
        /// SKIP THE EMPTY SKY (1.5.0, Sky.EmptySky): the cloud march jumps over cells of the weather map
        /// that hold no cloud at this cover. Nothing on screen changes. Off with cloud fragments (they
        /// read the map turned and finer, which a cell's bound says nothing about), off while hardly any
        /// cell is empty. Only the cloud march reads it. Under the
        /// Mixed style its own copy of the map (CloudMixMap: where the separate clouds reach), and only
        /// while that copy was made from the field's current one.
        /// </summary>
        private static void ApplyEmptySky(Material material, CloudDensityField field)
        {
            bool fragments = CloudFragments.On;
            bool mixed = CloudStyle.MixedDrawn;
            byte[] mixBytes = mixed && CloudMixMap.EmptySkyFor(field.EmptySkyBytes) ? CloudMixMap.EmptySkyBytes : null;

            float share = 0f;
            if (!fragments && mixed)
            {
                // The layer's lowest threshold anywhere (its type varies it): the shader's test (CellEmpty).
                float lowest = field.GetThreshold(CloudStyle.MixedLayerInLayerArea(Coverage));
                if (mixBytes == null)
                    _emptyShare = 0f;
                else if (lowest != _emptyShareThreshold || !ReferenceEquals(mixBytes, _emptyShareMixBytes))
                    _emptyShare = EmptySky.EmptyShareMixed(mixBytes, lowest);

                // (Remembered for the other styles' test below too: it then works its share out again.)
                _emptyShareThreshold = mixBytes != null ? lowest : float.NaN;
                _emptyShareMixBytes = mixBytes;
                _emptyShareVersion = -1;
                share = _emptyShare;
            }
            else if (!fragments)
            {
                _emptyShareMixBytes = null;
                bool cumulus = CloudStyle.Drawn;
                float threshold = field.GetThreshold(LayerCoverage);
                float lineA = 0f, lineB = 0f;
                if (cumulus)
                    CloudStyle.Line(CloudStyle.HeapEdge(CloudStyle.HeapCoverage(Coverage)), out lineA, out lineB);

                // Worked out again only when what it depends on moves: three materials ask every frame.
                if (threshold != _emptyShareThreshold || cumulus != _emptyShareCumulus || lineA != _emptyShareLineA ||
                    lineB != _emptyShareLineB || field.Version != _emptyShareVersion)
                {
                    _emptyShare = field.EmptyShare(threshold, cumulus, lineA, lineB);
                    _emptyShareThreshold = threshold;
                    _emptyShareCumulus = cumulus;
                    _emptyShareLineA = lineA;
                    _emptyShareLineB = lineB;
                    _emptyShareVersion = field.Version;
                }

                share = _emptyShare;
            }

            // Half the share to switch off as to switch on: a cover hovering round it does not flicker
            // the state (and the log).
            bool on = !fragments && share >= (_emptySkyLogged == "on" ? EmptySkyMinShare * 0.5f : EmptySkyMinShare);
            int cells = CloudDensityField.Resolution / EmptySky.CellTexels;
            material.SetVector(IdEmptySky, new Vector4(on ? 1f : 0f, cells, EmptySky.CoarseCells, 0f));
            if (on)
                material.SetTexture(IdEmptySkyTex, mixed ? CloudMixMap.EmptySkyTexture : field.EmptySkyTexture);

            // Always on the log, once per state: it is the one thing in the march that decides what is
            // NOT looked at, so a hole in the sky after this would start here.
            string state = on ? "on" : fragments ? "fragments" : "full";
            if (state != _emptySkyLogged)
            {
                _emptySkyLogged = state;
                Log.Msg(state == "on"
                    ? "empty-sky skip: on -- map " + cells + "x" + cells + " (cells of " + EmptySky.CellTexels + " and " +
                      EmptySky.CellTexels * EmptySky.CoarseCells + " weather texels), built in " + field.EmptySkyBuildMs.ToString("F1") +
                      " ms; " + (share * 100f).ToString("F0") + "% of the cells are empty now"
                    : state == "fragments"
                        ? "empty-sky skip: off while cloud fragments are on"
                        : "empty-sky skip: off, almost no cell is empty at this cover");
            }
        }

        /// <summary>The share of the weather map's cells the cloud march can skip now, 0..1 (0 while it skips none).</summary>
        public static float EmptyShareNow
        {
            get { return _emptySkyLogged == "on" ? _emptyShare : 0f; }
        }

        /// <summary>
        /// The cloud style (CloudStyle). Classic -- or Cumulus or Mixed before their noise exists --
        /// writes 0 and nothing else: every use of the Cumulus uniforms sits behind that branch, and
        /// every use of the Mixed ones behind _CloudStyle 2.
        /// </summary>
        private static void ApplyStyle(Material material, CloudDensityField field, float weatherTile, float thickness, float breakupScale)
        {
            // Mixed: its cloud map is baked off the main thread as the weather moves (once a frame at most).
            if (CloudStyle.MixedWanted)
                CloudMixMap.Update(field, Coverage, weatherTile, SkyPattern.FieldSeed, CloudStyle.MixShape);

            bool drawn = CloudStyle.Drawn;
            bool mixed = CloudStyle.MixedDrawn;
            string name = CloudStyle.Name(CloudStyle.Current);

            // Always on the log: it changes what is drawn. Once per state. Chosen but not there
            // yet is the few seconds its noise takes to make (CloudVolume), or a failure, said once.
            bool failed = CloudStyle.Failed || (CloudStyle.IsMixed && CloudMixMap.Failed);
            string state = mixed ? "mixed" : drawn ? "cumulus" : !CloudStyle.UsesNoise ? "classic" : failed ? "failed" : "waiting";
            if (state != _styleLogged)
            {
                _styleLogged = state;
                Log.Msg(state == "mixed"
                    ? "cloud style: Mixed -- " + CloudMix.DescribeShape(weatherTile, CloudStyle.MixShape)
                    : state == "cumulus"
                        ? "cloud style: Cumulus -- " + CloudStyle.DescribeShape()
                        : state == "classic"
                            ? "cloud style: Classic (the layer as it has always been drawn)"
                            : state == "failed"
                                ? "cloud style: " + name + " chosen but its noise or map failed: drawn Classic"
                                : "cloud style: " + name + " chosen; its noise or map is being made, drawn Classic until it is there");
            }

            material.SetFloat(IdCloudStyle, mixed ? 2f : drawn ? 1f : 0f);
            if (!drawn)
                return;

            if (mixed)
                ApplyMix(material, field);

            float density = Settings.CloudDensity != null ? Settings.CloudDensity.value : Settings.Defaults.Density;
            float[] c0 = CloudStyle.Cubics[0];
            float[] c1 = CloudStyle.Cubics[1];

            // The heaps' coverage: a line in the map's normal scores (its own ranks, so every city
            // gets the same amount at the same slider), no more heaps above HeapCap. Cumulus amount
            // (1.5.0) scales the intensity they are cut at.
            float lineA, lineB;
            CloudStyle.Line(CloudStyle.HeapEdge(CloudStyle.HeapCoverage(Coverage)), out lineA, out lineB);

            // The layer beside them: Classic's, its thickness following the map. `lod` is the
            // Cumulus noise's (at its repeat now: Cumulus size); the layer's detail is finer by the
            // ratio of their texels.
            float noiseTile = CloudStyle.NoiseTileNow;
            float[] layerLine = CloudStyle.LayerLineCached;
            material.SetVector(IdLayerShape, new Vector4(Mathf.Max(50f, thickness), layerLine[0], layerLine[1], CloudStyle.LayerThinnest));
            material.SetFloat(IdLayerShapeMax, CloudStyle.LayerThickest);
            material.SetFloat(IdDetailLodShift, Mathf.Log(noiseTile / CloudDetail.Tile(breakupScale), 2f));

            // Cumulus size and height (1.5.0): the noise's repeat and the coverage curve's span.
            material.SetTexture(IdCumulusTex, CloudStyle.Texture);
            material.SetTexture(IdWeatherScoreTex, field.ScoreTexture);
            material.SetVector(IdCumulusShape, new Vector4(lineA, lineB, CloudStyle.CoverageMax, 1f / CloudStyle.SpanNow));

            // Density in the units the march multiplies by _Absorption: the default Density slider
            // is BaseExtinction per metre, and the slider scales it as it scales Classic's.
            material.SetVector(IdCumulusNoise, new Vector4(
                1f / noiseTile, CloudStyle.ErosionDepth, 1f / (1f - CloudStyle.Hardness),
                CloudStyle.BaseExtinction / Absorption * density / Settings.Defaults.Density));

            // It drifts with the 3D noise (1.25x the weather): its own phase over its own repeat.
            material.SetVector(IdCumulusPhase, CloudWind.NoisePhase(noiseTile, 1f));

            material.SetVector(IdCumulusCurve0, new Vector4(c0[0], c0[1], c0[2], c0[3]));
            material.SetVector(IdCumulusCurve1, new Vector4(c1[0], c1[1], c1[2], c1[3]));
            material.SetVector(IdCumulusCurveKeys, new Vector4(
                CloudStyle.CurveTime[0], CloudStyle.CurveTime[1], CloudStyle.CurveTime[2], CloudStyle.CurveValue[2]));
        }

        /// <summary>
        /// The Mixed style (CloudMix): its cloud map (CloudMixMap) and how to read it, the noise that
        /// carves the clouds, and the layer's three thresholds (in a stretch of sky the type tips towards
        /// layer, between, towards separate clouds) with the type's strength. Only while it is drawn:
        /// the shader reads none of it otherwise. The scales are the map's own: the weather repeat it was
        /// baked for.
        /// </summary>
        private static void ApplyMix(Material material, CloudDensityField field)
        {
            float cover = Coverage;
            float density = Settings.CloudDensity != null ? Settings.CloudDensity.value : Settings.Defaults.Density;
            float bakedTile = CloudMixMap.BakedTile;
            CloudMix.Shape shape = CloudMixMap.BakedShape;
            float noiseTile = CloudMix.NoiseTile(bakedTile, shape);

            material.SetTexture(IdMixMap, CloudMixMap.Map);

            // Metres per unit of what the map stores: x = a top (the tallest cloud the bake drew), y = a
            // band (for the shape it was baked with); z = the base's soft metres.
            material.SetVector(IdMixScale, new Vector4(CloudMixMap.BakedTallest, CloudMix.BandScale(bakedTile, shape),
                CloudMix.BaseSoftness, 0f));

            // The carving (EVE's in-between coverage), and the density at full in the units the march
            // multiplies by _Absorption, scaled by the Density slider as the Cumulus heaps are.
            material.SetVector(IdMixCarve, new Vector4(CloudMix.CoreCover, CloudMix.ErosionDepth, 1f / (1f - CloudMix.Hardness),
                CloudMix.Extinction / Absorption * density / Settings.Defaults.Density));

            // The noise: 1 / its repeat, its middle and stretch, and w the mip levels it sits above the
            // Cumulus noise's (the march works its level out for that repeat). It moves WITH the clouds
            // (the weather's drift over its own repeat): each cloud keeps its shape as it drifts.
            // TEXTURE VARIETY (1.5.0): a finer repeat where the type leans to layer, a chunkier one where
            // it leans to separate clouds (_MixNoise2), blended; at 0% one repeat, one read, as before.
            float variety = CloudStyle.TextureVariety;
            float fineTile = noiseTile * (1f - CloudMix.TextureFine * variety);
            float chunkyTile = noiseTile * (1f + CloudMix.TextureChunky * variety);
            float cumulusTile = CloudStyle.NoiseTileNow;
            material.SetVector(IdMixNoise, new Vector4(1f / fineTile, CloudMix.NoiseMiddle, CloudMix.NoiseContrast,
                Mathf.Log(cumulusTile / fineTile, 2f)));
            material.SetVector(IdMixNoise2, new Vector4(1f / chunkyTile, Mathf.Log(cumulusTile / chunkyTile, 2f), variety > 0f ? 1f : 0f, 0f));
            Vector2 phase = CloudWind.WeatherPhase(fineTile);
            material.SetVector(IdMixPhase, new Vector4(phase.x, 0f, phase.y, 0f));
            Vector2 phase2 = CloudWind.WeatherPhase(chunkyTile);
            material.SetVector(IdMixPhase2, new Vector4(phase2.x, 0f, phase2.y, 0f));
            material.SetVector(IdMixEnvelope, new Vector4(CloudMix.EnvelopeStart, CloudMix.EnvelopeGain, 0f, 0f));

            // The layer: Classic's own table (the gamma-decoded reading it was tuned on), so at 100%
            // all three are Classic's 100% (times the "Classic layer" share). x/z = a layer area / a
            // separate-cloud area (x is the lowest, which the empty-sky skip tests against); w = the
            // type's strength.
            material.SetVector(IdMixThreshold, new Vector4(
                field.GetThreshold(CloudStyle.MixedLayerInLayerArea(cover)),
                field.GetThreshold(CloudStyle.MixedLayer(cover)),
                field.GetThreshold(CloudStyle.MixedLayerInCumulusArea(cover)),
                CloudMix.TypeStrength(cover)));

            // The mix on the detailed log when it moves by a percent of intensity or more.
            int percent = Mathf.RoundToInt(cover * 100f);
            if (Log.Detailed && percent != _mixLogged)
            {
                _mixLogged = percent;
                Log.Detail("mixed style at " + percent + "%: " + CloudMix.DescribeMix(cover));
            }
        }

        private static int _mixLogged = -1;

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
        /// Classic, the Cumulus noise (at its repeat now: Cumulus size) for Cumulus and Mixed.
        /// </summary>
        public static float NoiseTexel
        {
            get
            {
                if (CloudStyle.Drawn)
                    return CloudStyle.NoiseTileNow / CumulusNoise3D.Size;

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
