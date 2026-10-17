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
        private static readonly int IdMixThreshold = Shader.PropertyToID("_MixThreshold");

        /// <summary>Metres the Cumulus slab reaches above the tallest cloud: the march must not clip its tops.</summary>
        private const float CumulusTopMargin = 25f;

        /// <summary>
        /// The top of what is marched, in metres above the base: the layer's thickness for
        /// Classic; for Cumulus the tallest heap the curve allows or the thickest the layer gets,
        /// whichever is higher; for Soft and Mixed their puffs' tallest or the thickest layer.
        /// </summary>
        public static float LayerHeight
        {
            get
            {
                float thickness = Mathf.Max(50f, Settings.CloudThickness != null ? Settings.CloudThickness.value : Settings.Defaults.Thickness);
                if (CloudStyle.PuffsDrawn)
                    return Mathf.Max(CloudStyle.SoftTallestNow, thickness * CloudStyle.LayerThickest);
                if (CloudStyle.HeapsDrawn)
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
        /// times the same "Classic layer" share (CloudStyle.MixedLayer). Under Soft its layer's cover
        /// (CloudSoft.LayerCover: none below its threshold), times the same share.
        /// </summary>
        public static float LayerCoverage
        {
            get
            {
                if (CloudStyle.SoftDrawn)
                    return CloudSoft.LayerCover(CloudStyle.SoftLook, Coverage, CloudStyle.LayerAmount);
                if (CloudStyle.MixedDrawn)
                    return CloudStyle.MixedLayer(Coverage);
                return CloudStyle.HeapsDrawn ? Coverage * CloudStyle.LayerAmount : Coverage;
            }
        }

        /// <summary>
        /// The cover the PUFFS are cut at (Soft and Mixed, `_SoftThreshold`): Soft's one
        /// (CloudSoft.PuffCoverage), or Mixed's where its type leans to layer (<paramref name="area"/> 0),
        /// neither way (1) or to puffs (2): Soft's at CloudMix.PuffIntensity.
        /// </summary>
        public static float PuffCoverage(int area)
        {
            float intensity = CloudStyle.MixedDrawn ? CloudMix.PuffIntensity(Coverage, area) : Coverage;
            return CloudSoft.PuffCoverage(CloudStyle.SoftLook, intensity);
        }

        /// <summary>
        /// The same, in 2% steps, for the CPU-built flat cookie, which rebuilds when the cover
        /// changes: it now drifts continuously with the weather.
        /// </summary>
        public static float CoverageStepped
        {
            get { return Mathf.Round(CloudWeather.Coverage * 50f) / 50f; }
        }

        /// <summary>The visible clouds: their density as the time of day has it now (Settings.CloudDensityNow).</summary>
        public static void Apply(Material material, CloudDensityField field, Texture3D noise)
        {
            Apply(material, field, noise, Settings.CloudDensityNow);
        }

        /// <summary>
        /// The same at <paramref name="density"/>: the shadow map passes the DAY's (1.5.0), so the
        /// density's clock changes the clouds you see and not their shadows, which darken by their own.
        /// Every density uniform below is made from this one number.
        /// </summary>
        public static void Apply(Material material, CloudDensityField field, Texture3D noise, float density)
        {
            float bottom = Settings.CloudAltitude != null ? Settings.CloudAltitude.value : Settings.Defaults.Altitude;
            float thickness = Settings.CloudThickness != null ? Settings.CloudThickness.value : Settings.Defaults.Thickness;
            float tile = Settings.WeatherTileSize != null ? Settings.WeatherTileSize.value : Settings.Defaults.WeatherTileSize;
            // The map's repeat (Larger weather pattern: 4x the slider's, the map 4x the texels).
            float weatherTile = field.TileFor(tile);
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

            // The layer is drawn in every style, with all of Classic's settings: its fragments and
            // its detail too. Under Cumulus the heaps come on top, under Soft and Mixed the puffs (ApplyStyle).
            ApplyStyle(material, field, thickness, detailScale, density);
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

        /// <summary>
        /// SKIP THE EMPTY SKY (1.5.0, Sky.EmptySky): the cloud march jumps over cells of the weather map
        /// that hold no cloud at this cover. Nothing on screen changes. Off with cloud fragments (they
        /// read the map turned and finer, which a cell's bound says nothing about), off while hardly any
        /// cell is empty. Only the cloud march reads it. Under Soft and Mixed a cell is empty when its
        /// most weather is at or under the lowest threshold any of their clouds starts at (the puffs' or
        /// the layer's, in any of Mixed's type areas): `_EmptySky.w`.
        /// </summary>
        private static void ApplyEmptySky(Material material, CloudDensityField field)
        {
            bool fragments = FragmentShare > 0f;
            bool puffs = CloudStyle.PuffsDrawn;

            float share = 0f;
            float threshold = field.GetThreshold(LayerCoverage);
            if (puffs)
            {
                // Each of these is a cut of the same map; the lowest of them bounds them all (Mixed's
                // type lerps between its three).
                threshold = Mathf.Min(threshold, field.GetThreshold(PuffCoverage(1)));
                if (CloudStyle.MixedDrawn)
                {
                    threshold = Mathf.Min(threshold, field.GetThreshold(CloudStyle.MixedLayerInLayerArea(Coverage)));
                    threshold = Mathf.Min(threshold, Mathf.Min(field.GetThreshold(PuffCoverage(0)), field.GetThreshold(PuffCoverage(2))));
                }
            }

            if (!fragments)
            {
                bool cumulus = CloudStyle.HeapsDrawn;
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
            int cells = field.Resolution / EmptySky.CellTexels;
            material.SetVector(IdEmptySky, new Vector4(on ? 1f : 0f, cells, EmptySky.CoarseCells, puffs ? threshold : 0f));
            if (on)
                material.SetTexture(IdEmptySkyTex, field.EmptySkyTexture);

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
        /// The cloud style (CloudStyle). Classic -- or Cumulus, Mixed or Soft before their noise exists --
        /// writes 0 and nothing else: every use of the Cumulus uniforms sits behind that branch, every
        /// use of the puffs' (Soft's and Mixed's) behind _CloudStyle 2 and 3, of Mixed's type behind 2.
        /// </summary>
        private static void ApplyStyle(Material material, CloudDensityField field, float thickness, float breakupScale, float density)
        {
            // Mixed: its type map is baked off the main thread once per seed and map size.
            if (CloudStyle.MixedWanted)
                CloudMixMap.Update(field, SkyPattern.FieldSeed);

            bool soft = CloudStyle.SoftDrawn;
            bool heaps = CloudStyle.HeapsDrawn;
            bool mixed = CloudStyle.MixedDrawn;
            string name = CloudStyle.Name(CloudStyle.Current);

            // Always on the log: it changes what is drawn. Once per state. Chosen but not there
            // yet is the few seconds its noise takes to make (CloudVolume), or a failure, said once.
            bool failed = CloudStyle.Failed || (CloudStyle.IsMixed && CloudMixMap.Failed);
            string state = soft ? "soft" : mixed ? "mixed" : heaps ? "cumulus" : !CloudStyle.UsesNoise ? "classic" : failed ? "failed" : "waiting";
            if (state != _styleLogged)
            {
                _styleLogged = state;
                Log.Msg(state == "soft"
                    ? "cloud style: Soft -- " + CloudSoft.Describe(CloudStyle.SoftLook, thickness)
                    : state == "mixed"
                    ? "cloud style: Mixed -- " + CloudMix.DescribeShape(thickness, field.Scale)
                    : state == "cumulus"
                        ? "cloud style: Cumulus -- " + CloudStyle.DescribeShape()
                        : state == "classic"
                            ? "cloud style: Classic (the layer as it has always been drawn)"
                            : state == "failed"
                                ? "cloud style: " + name + " chosen but its noise or map failed: drawn Classic"
                                : "cloud style: " + name + " chosen; its noise or map is being made, drawn Classic until it is there");
            }

            material.SetFloat(IdCloudStyle, soft ? 3f : mixed ? 2f : heaps ? 1f : 0f);
            if (!soft && !mixed && !heaps)
                return;

            // The layer beside the heaps or the puffs: Classic's, its thickness following the map. `lod`
            // is the Cumulus noise's (at the heaps' repeat now, Cumulus size, or the puffs'); the layer's
            // detail is finer by the ratio of their texels.
            float noiseTile = heaps ? CloudStyle.NoiseTileNow : CloudStyle.SoftLook.NoiseTile;
            float[] layerLine = CloudStyle.LayerLineCached;
            material.SetVector(IdLayerShape, new Vector4(Mathf.Max(50f, thickness), layerLine[0], layerLine[1], CloudStyle.LayerThinnest));
            material.SetFloat(IdLayerShapeMax, CloudStyle.LayerThickest);
            material.SetFloat(IdDetailLodShift, Mathf.Log(noiseTile / CloudDetail.Tile(breakupScale), 2f));
            material.SetTexture(IdCumulusTex, CloudStyle.Texture);
            material.SetTexture(IdWeatherScoreTex, field.ScoreTexture);

            if (!heaps)
            {
                ApplySoft(material, field, density);
                if (mixed)
                    ApplyMix(material, field);
                return;
            }

            float[] c0 = CloudStyle.Cubics[0];
            float[] c1 = CloudStyle.Cubics[1];

            // The heaps' coverage: a line in the map's normal scores (its own ranks, so every city
            // gets the same amount at the same slider), no more heaps above HeapCap. Cumulus amount
            // (1.5.0) scales the intensity they are cut at.
            float lineA, lineB;
            CloudStyle.Line(CloudStyle.HeapEdge(CloudStyle.HeapCoverage(Coverage)), out lineA, out lineB);

            // Cumulus size and height (1.5.0): the noise's repeat and the coverage curve's span.
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
        /// The Mixed style (CloudMix): its type map (CloudMixMap) and the layer's three thresholds (in a
        /// stretch of sky the type tips towards layer, between, towards puffs) with the type's strength;
        /// the puffs' three are ApplySoft's. Only while it is drawn: the shader reads none of it otherwise.
        /// </summary>
        private static void ApplyMix(Material material, CloudDensityField field)
        {
            float cover = Coverage;
            material.SetTexture(IdMixMap, CloudMixMap.Map);

            // The layer: Classic's own table (the gamma-decoded reading it was tuned on), so at 100%
            // all three are Classic's 100% (times the "Classic layer" share). x/z = a layer area / a
            // puff area; w = the type's strength.
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

        private static readonly int IdSoftThreshold = Shader.PropertyToID("_SoftThreshold");
        private static readonly int IdSoftShape = Shader.PropertyToID("_SoftShape");
        private static readonly int IdSoftNoise = Shader.PropertyToID("_SoftNoise");
        private static readonly int IdSoftCarve = Shader.PropertyToID("_SoftCarve");
        private static readonly int IdSoftWisp = Shader.PropertyToID("_SoftWisp");
        private static readonly int IdSoftFringe = Shader.PropertyToID("_SoftFringe");
        private static readonly int IdSoftWarpMiddle = Shader.PropertyToID("_SoftWarpMiddle");
        private static readonly int IdSoftPhase = Shader.PropertyToID("_SoftPhase");

        /// <summary>
        /// The puffs (CloudSoft), under Soft and Mixed: their look as uniforms for SoftDensity
        /// (CloudCommon.cginc), which cuts the map at _SoftThreshold (PuffCoverage: Soft's one, or Mixed's
        /// three for its type), carves with the Cumulus noise at its own repeat and phase (the fine read
        /// at a whole multiple of them, so it shares the phase) and pushes both lookups about by the 64^3
        /// noise at the Classic layer's lookup (_NoiseTile, _NoisePhase). Only while they are drawn: the
        /// shader reads none of it otherwise.
        /// </summary>
        private static void ApplySoft(Material material, CloudDensityField field, float density)
        {
            CloudSoft.Look look = CloudStyle.SoftLook;
            float tallest = CloudStyle.SoftTallestNow;
            float multiple = Mathf.Max(1f, Mathf.Round(look.WispMultiple));

            float middle = field.GetThreshold(PuffCoverage(1));
            bool mixed = CloudStyle.MixedDrawn;
            material.SetVector(IdSoftThreshold, new Vector4(mixed ? field.GetThreshold(PuffCoverage(0)) : middle, middle,
                mixed ? field.GetThreshold(PuffCoverage(2)) : middle, 0f));

            // The density at full in the units the march multiplies by _Absorption: the default Density
            // slider is look.Extinction per metre, and the slider scales it as it scales the others.
            material.SetVector(IdSoftShape, new Vector4(1f / look.Span, tallest, look.Middle,
                look.Extinction / Absorption * density / Settings.Defaults.Density));
            material.SetVector(IdSoftNoise, new Vector4(1f / look.NoiseTile, look.NoiseMiddle, look.NoiseContrast, look.Erosion));
            material.SetVector(IdSoftCarve, new Vector4(1f / look.Softness, look.Fill, 1f / look.FillSpan, 0f));
            material.SetVector(IdSoftWisp, new Vector4(look.Wisp, multiple, Mathf.Log(multiple, 2f),
                1f / Mathf.Max(look.WispHeight * tallest, 1f)));
            material.SetVector(IdSoftFringe, new Vector4(look.Fringe, 1f / look.FringeReach, Mathf.Clamp01(look.EdgeFade), look.Warp));
            material.SetVector(IdSoftWarpMiddle, new Vector4(look.WarpMiddleR, look.WarpMiddleG, 0f, 0f));

            // It drifts with the 3D noise (1.25x the weather), as Cumulus's heaps: its own phase over its own repeat.
            material.SetVector(IdSoftPhase, CloudWind.NoisePhase(look.NoiseTile, 1f));
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
        /// Classic, the Cumulus noise (at its repeat now: Cumulus size) for Cumulus, the Cumulus noise at
        /// the puffs' own repeat for Soft and Mixed (their fine read is log2 of the multiple finer).
        /// </summary>
        public static float NoiseTexel
        {
            get
            {
                if (CloudStyle.PuffsDrawn)
                    return CloudStyle.SoftLook.NoiseTile / CumulusNoise3D.Size;
                if (CloudStyle.HeapsDrawn)
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
            // own level in the shader (SampleDensityLod). Under Soft and Mixed too: their fine read fades out above 0.
            float lod = Mathf.Log(Mathf.Max(metres, 1e-3f) / NoiseTexel, 2f);
            return CloudStyle.LobesLight ? lod : Mathf.Max(0f, lod);
        }

        /// <summary>
        /// The share of the sky cloud fragments cover now, 0 = none: the slider's (CloudFragments.Share),
        /// under Soft times its layer's share -- they shape the layer, which Soft draws only at high
        /// intensity, so below it Soft's sky stays as it is with fragments on.
        /// </summary>
        public static float FragmentShare
        {
            get
            {
                return CloudStyle.SoftDrawn
                    ? CloudFragments.Share * CloudSoft.LayerShare(CloudStyle.SoftLook, Coverage) * CloudStyle.LayerAmount
                    : CloudFragments.Share;
            }
        }

        /// <summary>
        /// Cloud fragments (CloudFragments), round the layer's clouds in every style. Off, the
        /// amount is written as 0 and nothing else is looked at: every use of them sits behind that
        /// uniform branch.
        /// </summary>
        private static void ApplyFragments(Material material, CloudDensityField field, float weatherTile, float thickness)
        {
            float share = FragmentShare;
            bool on = share > 0f;
            CloudFragments.Style style = CloudFragments.Current;
            bool waiting = !on && CloudFragments.On;
            string logged = on ? style.Name : waiting ? "waiting" : null;
            if (logged != _fragmentsLogged)
            {
                // Always on the log: it changes what is drawn. Once per switch or style, not per
                // material and not per tick of the amount slider.
                _fragmentsLogged = logged;
                Log.Msg("cloud fragments: " + (on ? "ON, " : "") + CloudFragments.Describe() +
                        (waiting ? " -- none drawn until Soft's Classic layer comes in" : ""));
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
