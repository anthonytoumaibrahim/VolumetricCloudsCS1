// Shared by CloudRaymarch.shader (what you see) and CloudShadowMap.shader (what lands on the
// ground). Both must march the *same* density function, or clouds and their shadows drift
// apart -- which is the whole reason this lives in one place.
//
//   _WeatherTex : 2D tiling density field. Thresholded here with _Threshold/_Softness, so
//                 coverage is a single uniform rather than a texture rewrite.
//   _NoiseTex   : 3D tiling noise. R = Perlin-Worley base shape, G = Worley detail.

#ifndef VOLUMETRIC_CLOUDS_COMMON_INCLUDED
#define VOLUMETRIC_CLOUDS_COMMON_INCLUDED

sampler2D _WeatherTex;
sampler3D _NoiseTex;

float _CloudBottom;
float _CloudTop;
float _Threshold;
float _Softness;
float _WeatherTile;
float _NoiseTile;
float _DensityScale;
float _Absorption;
float _DetailStrength;   // how hard the fine noise eats into the cloud: 0 = solid blocks, ~0.3 default, higher = ragged and wispy
float _DetailScale;      // frequency of that fine noise relative to the base shape

// The wind, as each lookup sees it: how far through ONE REPEAT of that lookup the drift has
// gone, 0..1 per axis (CloudWind / Drift.Phase, worked out in doubles on the CPU). A lookup is
// p / tile - phase; the true drift differs by whole repeats, which read the same texel. The
// drift itself is never sent: a city keeps its sky in its save, so it grows without limit,
// and a float at 8000 km moves in whole-metre steps.
float2 _WeatherPhase;    // over the weather map (_WeatherTile)
float3 _NoisePhase;      // over the 3D noise, which drifts 1.25x as fast (CloudWind.NoiseDrift)
float3 _DetailPhase;     // the same, for the erosion lookup at _DetailScale x the frequency

float Remap(float v, float a, float b, float c, float d)
{
    return c + (v - a) / max(b - a, 1e-4) * (d - c);
}

// Rounded base, tapering top: what makes a slab read as cumulus rather than fog.
float HeightShape(float h)
{
    return saturate(h * 5.0) * saturate((1.0 - h) * 2.5);
}

// CLOUD FRAGMENTS (Sky/CloudFragments.cs): small, thin shreds clustered round the big clouds,
// in the same layer and on the same base. _FragAmount == 0 means off: every use sits behind
// that uniform branch, and nothing else of it is even set.
float _FragAmount;
float _FragThreshold;    // weather value where a fragment starts (solved: a share of the sky)
float4 _FragPhase;       // xy: the fragment lookup's turned drift, zw: the area lookup's (shifts folded in)
float4 _FragParams;      // x = multiple (whole), yz = cos, sin of the turn, w = height in metres
float4 _FragArea;        // the area lookup: x = multiple (whole), yz = cos, sin, w = its threshold
float4 _FragEdge;        // x = threshold drop at a big cloud's edge, y = how far out, z = density,
                         // w = the fragments' own break-up strength, or -1 for the clouds' own

// The weather map read turned by (c, s) at a whole-number multiple of its frequency. Turning is
// linear, so the drift's phase for it is the drift turned too (CloudWind.TurnedPhase).
float TurnedWeather(float2 xz, float multiple, float c, float s, float2 phase)
{
    float2 q = xz / _WeatherTile;
    q = float2(q.x * c - q.y * s, q.x * s + q.y * c);
    return tex2Dlod(_WeatherTex, float4(q * multiple - phase, 0, 0)).r;
}

// A fragment's shape at p, 0..1 like `shaped` below: the same weather map read again, turned,
// finer and shifted so its pieces line up with nothing of the big pattern; a little more likely
// just outside a big cloud; only in the random areas a coarser lookup lets them into, so no two
// stretches of sky look alike; a short column from the same base.
float FragmentShape(float3 p, float weather, float h01Frag)
{
    float wf = TurnedWeather(p.xz, _FragParams.x, _FragParams.y, _FragParams.z, _FragPhase.xy);
    float nearBig = saturate((weather - (_Threshold - _FragEdge.y)) / _FragEdge.y);
    float cover = saturate((wf - _FragThreshold + _FragEdge.x * nearBig) / _Softness);
    if (cover <= 0.0)
        return 0.0;

    float wa = TurnedWeather(p.xz, _FragArea.x, _FragArea.y, _FragArea.z, _FragPhase.zw);
    cover *= saturate((wa - _FragArea.w) / _Softness);

    return cover * HeightShape(h01Frag);
}

// CLOUD DETAIL (Sky/CloudDetail.cs, 1.2): crisp edges and puffy billows. Documented techniques,
// each chosen from offline renders of this formula: Horizon Zero Dawn's erosion rule (wispy in
// the bottom tenth, billowy above), EVE-Redux V5's "spherical" Worley texture, edge hardness and
// density curve. _DetailAmount == 0 means off: every use sits behind that uniform branch, and
// SampleDensity then computes exactly what it did before the feature existed.
sampler3D _DetailTex;    // Sky/CloudDetail3D: 128^3 ARGB32, mipmapped, puffy Worley; the value is
                         // read from ALPHA, which is never sRGB-decoded (the RGB copies are)
float _DetailAmount;     // 0..1
float4 _DetailParams;    // x = erosion strength, y = edge softness (1 - hardness),
                         // z = density lost at the base (denser tops), w = erosion multiplier on the tops
float4 _DetailLookup;    // x = 1 / its repeat in metres, y = the anti-tiling shift (repeats),
                         // z = the fragments' own erosion in these units (-1 = the clouds'),
                         // w = 1: the value is in alpha (always, since R8 crashed Unity 5.6;
                         // 0 would read red, for a single-channel texture)
float3 _DetailTexPhase;  // the wind's drift over one repeat of it (CloudWind.NoisePhase), like _DetailPhase

// THE CUMULUS STYLE (Sky/CloudStyle.cs, 1.2): EVE-Redux V5's cloud model for the HEAPS -- every one
// carved out of ONE big noise, the weather map only saying where they gather, gently (EVE's
// "in-between" coverage: the noise decides the shapes, so each gets its own height, a flat base
// and a heaped top) -- with the CLASSIC LAYER among and under them (the author's idea: "combine the
// classic and cumulus clouds"), its thickness following the map. _CloudStyle == 0 is Classic: the
// layer alone, computed exactly as it always was.
float _CloudStyle;          // 0 Classic, 1 Cumulus, 2 Mixed, 3 Soft (below); above 0.5 the Cumulus light
float4 _LayerShape;         // the layer under the Cumulus style: x = Layer thickness in metres; its
                            // factor on that is a line in the map's score, y + z * score (thick where
                            // the map is high), within w .. _LayerShapeMax
float _LayerShapeMax;
float _DetailLodShift;      // under the Cumulus style `lod` is the Cumulus noise's; the detail's is this
                            // much more (log2 of the ratio of their texels)
sampler3D _CumulusTex;      // Sky/CumulusNoise3D: 128^3 ARGB32, mipmapped, 8 octaves of puffy
                            // Worley; the value is read from ALPHA, which is never sRGB-decoded

// A 2D texture read at level 0 through ANOTHER texture's sampler (1.5.0): the cloud pass uses 15 of
// the 16 samplers a pixel shader may have, so a new data texture brings no sampler of its own.
// Every API the bundles carry (D3D11; GLCore and Metal through HLSLcc) has separate textures and
// samplers -- HLSLSupport.cginc's own test; the other branch only keeps the file compiling elsewhere.
#if defined(SHADER_API_D3D11) || defined(SHADER_API_XBOXONE) || defined(UNITY_COMPILER_HLSLCC) || defined(SHADER_API_PSSL)
    #define VC_TEX2D_LOD0(tex, samplertex, uv) tex.SampleLevel(sampler##samplertex, uv, 0)
#else
    #define VC_TEX2D_LOD0(tex, samplertex, uv) tex2Dlod(tex, float4(uv, 0, 0))
#endif

UNITY_DECLARE_TEX2D(_WeatherScoreTex); // CloudDensityField.ScoreTexture: the weather map's NORMAL SCORES (each
                            // texel's rank on its own map as a normal z, 0..1 stored), LINEAR, read
                            // from alpha; the same share of every city's map lies above any score.
                            // Bilinear, repeating: the Mixed map borrows its sampler.
float4 _CumulusShape;       // xy = the coverage as a line in the score, x + y * score (CloudStyle.Line),
                            // z = the most coverage anywhere, w = 1 / the coverage curve's span in metres
float4 _CumulusNoise;       // x = 1 / its repeat in metres, y = erosion depth, z = 1 / (1 - edge
                            // hardness), w = the density at full, in _Absorption's units
float3 _CumulusPhase;       // the wind's drift over one repeat of it (CloudWind.NoisePhase)
float4 _CumulusCurve0;      // EVE's cumulus coverage curve, first segment, as a cubic in s: a, b, c, d
float4 _CumulusCurve1;      // ... second segment
float4 _CumulusCurveKeys;   // x, y, z = the keys' heights (fractions of the span); w = the value past
                            // the last key (before the first it is the first segment's d)

// THE MIXED STYLE (Sky/CloudMix.cs, 1.5.0): the Soft style's puffs (SoftDensity below) with the Classic
// layer coming in as the intensity rises (from ~25%, Classic's own cover from ~85%, its 100% at 100%).
// A slow TYPE pattern (Sky/CloudMixMap: baked once per city's seed, the map's alpha) tips each stretch
// of sky towards the layer (it comes in earlier, fewer puffs) or towards the puffs: both cuts are lerped
// between three thresholds by it, and it fades out by 100%. _CloudStyle == 2; nothing of it is read
// otherwise. (Until 2026-10-17 its puffs were separate clouds baked into the map per intensity -- "ugly
// in low intensity", similar-sized cut-outs with hard edges.)
UNITY_DECLARE_TEX2D_NOSAMPLER(_MixMap); // bilinear, LINEAR, through the score texture's sampler: a = the
                        // type (0 tips towards layer, 1 towards puffs); r, g, b unused
float4 _MixThreshold;   // the layer's threshold: x in a layer area, y between, z in a puff area;
                        // w = the type's strength (0 at 100%)

// The type at the map's uv, its strength applied: 0 tips towards layer, 1 towards puffs, 0.5 neither.
float MixType(float2 uvWeather)
{
    return 0.5 + (VC_TEX2D_LOD0(_MixMap, _WeatherScoreTex, uvWeather).a - 0.5) * _MixThreshold.w;
}

// A threshold where the type is t: between its value in a layer area (x), between (y) and in a puff
// area (z) -- the middle one once the type's strength is gone.
float MixThresholdAt(float4 thresholds, float t)
{
    return t < 0.5 ? lerp(thresholds.x, thresholds.y, t * 2.0) : lerp(thresholds.y, thresholds.z, t * 2.0 - 1.0);
}

// Coverage over height: the cloud's silhouette. A cloud only reaches as high as its coverage lets
// it (cg below): what makes every cloud its own height.
float CumulusCurve(float h)
{
    if (h <= _CumulusCurveKeys.x)
        return _CumulusCurve0.w;
    if (h >= _CumulusCurveKeys.z)
        return _CumulusCurveKeys.w;

    bool first = h < _CumulusCurveKeys.y;
    float4 k = first ? _CumulusCurve0 : _CumulusCurve1;
    float start = first ? _CumulusCurveKeys.x : _CumulusCurveKeys.y;
    float end = first ? _CumulusCurveKeys.y : _CumulusCurveKeys.z;
    float s = (h - start) / (end - start);
    return ((k.x * s + k.y) * s + k.z) * s + k.w;
}

// EVE V5: cg = saturate(coverage + curve(h) - 1); the noise carves erosion-depth deep into it,
// and the edge hardness gives what is left a surface. CloudStyle.Density is its C# twin.
// `score`: the map's normal score here (the same lookup as the weather map's: repeat and drift).
float CumulusDensity(float3 p, float lod, float score)
{
    float coverage = clamp(_CumulusShape.x + _CumulusShape.y * score, 0.0, _CumulusShape.z);
    if (coverage <= 0.0)
        return 0.0;

    float cg = saturate(coverage + CumulusCurve((p.y - _CloudBottom) * _CumulusShape.w) - 1.0);
    if (cg <= 0.0)
        return 0.0;

    float3 uv = p * _CumulusNoise.x - _CumulusPhase;
    float noise = tex3Dlod(_CumulusTex, float4(uv, lod)).a;
    return saturate((cg - (1.0 - noise) * _CumulusNoise.y) * _CumulusNoise.z) * _CumulusNoise.w;
}

// THE CLASSIC LAYER at p: `h` its height through the layer (0 at the base, 1 at the top), `weather`
// the map there, `lod` cloud detail's mip level, `threshold` the map value it starts at (_Threshold,
// except under Mixed, whose type varies it across the sky). The layer the mod has always drawn; the
// Cumulus, Mixed and Soft styles draw it too, beside their own clouds.
float LayerDensity(float3 p, bool detailed, float lod, float h, float weather, float threshold)
{
    float coverage = saturate((weather - threshold) / _Softness);

    // The big clouds' shape. (Zero exactly when coverage is, so with fragments off this returns
    // in the same cases, with the same numbers, as before they existed.)
    float shaped = coverage * HeightShape(h);

    // Fragments: where both are present the larger shape wins, so the big clouds are untouched.
    // fragWeight says how much of this point is fragment rather than big cloud (a soft blend,
    // so no seam where one meets the other): there the density is the fragments' own.
    float fragWeight = 0.0;
    if (_FragAmount > 0.0)
    {
        float hFrag = (p.y - _CloudBottom) / _FragParams.w;
        if (hFrag < 1.0)
        {
            float fragShape = FragmentShape(p, weather, hFrag);
            fragWeight = saturate((fragShape - shaped) / 0.1);
            shaped = max(shaped, fragShape);
        }
    }

    if (shaped <= 0.0)
        return 0.0;

    // The volume drifts slightly faster than the weather so clouds churn instead of
    // sliding as a rigid sheet (the 1.25 is in _NoisePhase).
    float3 uvNoise = p / _NoiseTile - _NoisePhase;
    float base = tex3Dlod(_NoiseTex, float4(uvNoise, 0)).r;

    float d = saturate(Remap(base, 1.0 - shaped, 1.0, 0.0, 1.0)) * shaped;

    if (_DetailAmount > 0.0)
    {
        if (detailed && d > 0.0)
        {
            // Two values this lookup has already read -- the base noise (hundreds of metres) and
            // the weather (kilometres) -- shift where each stretch of cloud reads the detail, in
            // different directions: its 500 m repeat made a grid of billows in the distance.
            float3 uvDetail = p * _DetailLookup.x - _DetailTexPhase;
            float shiftBase = (base - 0.5) * _DetailLookup.y;
            float shiftWeather = (weather - threshold) * _DetailLookup.y * 2.0;
            uvDetail += float3(shiftBase + shiftWeather * 0.6, shiftBase * 0.5, shiftWeather - shiftBase * 0.8);

            float4 texel = tex3Dlod(_DetailTex, float4(uvDetail, lod));
            float billow = max(texel.r, texel.a * _DetailLookup.w);

            // HZD: carve the billows' centres at the very base (wisps), between them above (puffs).
            float erosion = lerp(billow, 1.0 - billow, saturate(h * 10.0));

            // Gentler on the tops: from above, full erosion read as clutter; from below you see
            // the bases and sides, which keep all of it.
            float erode = _DetailParams.x * lerp(1.0, _DetailParams.w, saturate((h - 0.45) * 2.5));
            if (_FragAmount > 0.0 && _DetailLookup.z >= 0.0)
                erode = lerp(erode, _DetailLookup.z, fragWeight);
            float threshold = erosion * erode;

            // Below 100% the old break-up (detail off's, below) keeps the share the amount leaves.
            // This erosion alone is scaled by the amount: just above 0% the clouds had almost no
            // break-up left -- smooth blobs, and floating shreds the old one eats -- and at 0% they
            // changed shape again. With it 0% hands over to 100% without a step (Sky/CloudDetail).
            if (_DetailAmount < 1.0 && _DetailStrength > 0.0)
            {
                float3 uvOld = p / _NoiseTile * _DetailScale - _DetailPhase;
                float oldBreakup = tex3Dlod(_NoiseTex, float4(uvOld, 0)).g;
                float oldErode = _DetailStrength;
                if (_FragAmount > 0.0 && _FragEdge.w >= 0.0)
                    oldErode = lerp(_DetailStrength, _FragEdge.w, fragWeight);
                threshold += (1.0 - _DetailAmount) * oldBreakup * oldErode;
            }

            d = saturate(Remap(d, threshold, 1.0, 0.0, 1.0));
        }

        // Edge hardness: full density a short way in, so a cloud has a surface rather than an
        // inside made of noise. Then the density curve: denser tops, softer bases.
        d = saturate(d / _DetailParams.y);
        d *= 1.0 - _DetailParams.z * (1.0 - h);
    }
    // Detail off, the soft look from before: subtract fine cellular noise from the solid shape.
    // This is what breaks a cloud up from one smooth mass into lobes, tufts and ragged edges.
    // Thin parts of the cloud vanish first, so raising the strength also opens gaps through it.
    else if (detailed && d > 0.0 && _DetailStrength > 0.0)
    {
        // Its own phase: _DetailScale is not a whole number, so uvNoise * _DetailScale would
        // jump every time _NoisePhase wraps.
        float3 uvDetail = p / _NoiseTile * _DetailScale - _DetailPhase;
        float detail = tex3Dlod(_NoiseTex, float4(uvDetail, 0)).g;

        // A fragment style may tear its shreds harder than the big clouds are torn.
        float erode = _DetailStrength;
        if (_FragAmount > 0.0 && _FragEdge.w >= 0.0)
            erode = lerp(_DetailStrength, _FragEdge.w, fragWeight);

        d = saturate(Remap(d, detail * erode, 1.0, 0.0, 1.0));
    }

    // Fragments are see-through: a full shape (a faint one is eaten whole by the noise above)
    // at a fraction of the density. Inside the branch, so with them off nothing changes.
    if (_FragAmount > 0.0)
        d *= lerp(1.0, _FragEdge.z, fragWeight);

    return d * _DensityScale;
}

// THE SOFT STYLE (Sky/CloudSoft.cs, 1.5.0): fair-weather clouds made of puffs, soft at the edges. The
// weather map over the puffs' threshold (_SoftThreshold: CloudSoft.PuffCoverage, never inside Classic's
// clouds up to its cap) says where they gather and how big they may grow (c); the Cumulus noise at
// Soft's own repeat carves them deep -- a cloud is a bunch of clumps, small lone puffs where the map is
// low -- and a fine read of it frays the edges (strands near the base, billows higher, a faint fringe
// round every puff), both lookups pushed about by the 64^3 noise. Beside them, at high intensity, the
// Classic layer (cut at _Threshold: CloudSoft.LayerCover), as under Cumulus (SampleDensityLod).
// _CloudStyle == 3, and Mixed's puffs (2); nothing of it is read otherwise. CloudSoft.Density is its
// C# twin: change one, change the other.
float4 _SoftThreshold;  // the puffs' threshold: under Soft y, the one cut; under Mixed x in a layer area,
                        // y between, z in a puff area (MixThresholdAt)
float4 _SoftShape;      // x = 1 / the span (weather over the cut where c reaches 1), y = the tallest cloud
                        // (m above the base), z = the widest level (share of the tallest), w = the density
                        // at full, in _Absorption's units
float4 _SoftNoise;      // the Cumulus noise: x = 1 / its repeat (m), y = its middle, z = its contrast, w = how deep it carves
float4 _SoftCarve;      // x = 1 / the softness, y = the carving gone deep in the biggest clouds, z = 1 / the span
                        // of that past c = 1
float4 _SoftWisp;       // the fine read: x = how deep, y = its multiple (whole), z = log2 of that,
                        // w = 1 / the height (m) over which its strands turn to billows
float4 _SoftFringe;     // x = the fringe's density, y = 1 / its reach, z = the edge fade, w = the push (m)
float2 _SoftWarpMiddle; // the 64^3 noise's R and G middles, as read (sRGB-decoded)
float3 _SoftPhase;      // the Cumulus noise's drift over Soft's repeat (CloudWind.NoisePhase)

// The Cumulus noise as read, stretched round its middle (CloudSoft: (n - middle) x contrast + 0.5).
float SoftStretch(float n)
{
    return saturate((n - _SoftNoise.y) * _SoftNoise.z + 0.5);
}

// `lod`: the Cumulus noise's mip level at Soft's repeat (CloudShaderParams.NoiseTexel; negative near).
// The fine read's is _SoftWisp.z more and fades out above 0, where it would only shimmer. Not `detailed`
// (the light's looks): a level blurrier, no fine read. `weather`: the map at p; `threshold`: the puffs' cut.
float SoftDensity(float3 p, bool detailed, float lod, float weather, float threshold)
{
    float excess = weather - threshold;
    if (excess <= 0.0)
        return 0.0;

    // How big the cloud is here (CloudSoft.Size), and its body at this height: c less how far it is
    // from the widest level, the underside rounding up quickly (Body).
    float size = excess * _SoftShape.x;
    float c = saturate(size);
    float y = p.y - _CloudBottom;
    float h = y / _SoftShape.y;
    float mid = _SoftShape.z;
    float off = h < mid ? (mid - h) / mid : (h - mid) / (1.0 - mid);
    float body = saturate(c - (h < mid ? off * off : off));
    if (body <= 0.0)
        return 0.0;

    // The push: both lookups moved by the 64^3 noise at the Classic layer's lookup.
    float2 push = tex3Dlod(_NoiseTex, float4(p / _NoiseTile - _NoisePhase, 0)).rg - _SoftWarpMiddle;
    float3 uv = (p + float3(push.x, (push.x - push.y) * 0.5, push.y) * _SoftFringe.w) * _SoftNoise.x - _SoftPhase;

    // The carving, eased off deep inside the biggest clouds (c past 1).
    float fill = _SoftCarve.y * saturate((size - 1.0) * _SoftCarve.z);
    float n = SoftStretch(tex3Dlod(_CumulusTex, float4(uv, max(detailed ? lod : lod + 1.0, 0.0))).a);
    float v = body - _SoftNoise.w * (1.0 - fill) * (1.0 - n);

    float wisps = 0.0;
    float f = 0.0;
    if (detailed)
    {
        float fineLod = lod + _SoftWisp.z;
        wisps = saturate(1.0 - fineLod * 0.5);
        [branch]
        if (wisps > 0.0)
        {
            // Strands at the base (the fine puffs' middles carved, their borders left), billows higher.
            f = SoftStretch(tex3Dlod(_CumulusTex, float4(uv * _SoftWisp.y, max(fineLod, 0.0))).a);
            v -= _SoftWisp.x * wisps * (f + (1.0 - 2.0 * f) * saturate(y * _SoftWisp.w));
        }
    }

    float d = saturate(v * _SoftCarve.x);

    // The fringe: a faint, see-through edge of strands round every puff.
    if (wisps > 0.0 && _SoftFringe.x > 0.0)
        d = max(d, _SoftFringe.x * wisps * f * f * saturate(1.0 + v * _SoftFringe.y));

    // The puffs out where the map is low fainter (the edge fade).
    return d * (1.0 - _SoftFringe.z * (1.0 - c)) * _SoftShape.w;
}

// `lod` is the mip level of the style's noise (Classic: cloud detail's texture; Cumulus: the
// Cumulus noise, the detail's being _DetailLodShift more; Soft and Mixed: the Cumulus noise at the
// puffs' repeat, the detail's _DetailLodShift more): distant detail averages away instead of aliasing
// (the raymarch works it out from the pixel's footprint, the shadow map from its texel).
float SampleDensityLod(float3 p, bool detailed, float lod)
{
    float h = (p.y - _CloudBottom) / (_CloudTop - _CloudBottom);
    if (h <= 0.0 || h >= 1.0)
        return 0.0;

    float2 uvWeather = p.xz / _WeatherTile - _WeatherPhase;

    [branch]
    if (_CloudStyle > 1.5)
    {
        // Soft and Mixed: the puffs, and the Classic layer beside them where the map passes its cut (none
        // at Soft's low intensities: the score is not even read there). Where both are, the denser wins.
        // Under Mixed the type tips both cuts.
        float weather = tex2Dlod(_WeatherTex, float4(uvWeather, 0, 0)).r;
        float threshold = _Threshold;
        float puffThreshold = _SoftThreshold.y;
        [branch]
        if (_CloudStyle < 2.5)
        {
            float t = MixType(uvWeather);
            threshold = MixThresholdAt(_MixThreshold, t);
            puffThreshold = MixThresholdAt(_SoftThreshold, t);
        }

        float puffs = SoftDensity(p, detailed, lod, weather, puffThreshold);
        [branch]
        if (weather > threshold || _FragAmount > 0.0)
        {
            float score = VC_TEX2D_LOD0(_WeatherScoreTex, _WeatherScoreTex, uvWeather).a;
            float thicknessHere = _LayerShape.x * clamp(_LayerShape.y + _LayerShape.z * score, _LayerShape.w, _LayerShapeMax);
            float hHere = (p.y - _CloudBottom) / thicknessHere;
            if (hHere < 1.0)
                puffs = max(puffs, LayerDensity(p, detailed, max(lod + _DetailLodShift, 0.0), hHere, weather, threshold));
        }

        return puffs;
    }

    if (_CloudStyle > 0.5)
    {
        // Cumulus: the heaps, and the Classic layer among and under them. Where both are, the denser wins.
        float score = VC_TEX2D_LOD0(_WeatherScoreTex, _WeatherScoreTex, uvWeather).a;
        float clouds = CumulusDensity(p, max(lod, 0.0), score);

        // The layer is thick where the map is high and thin where it is low: variety over
        // kilometres, so a full sky is not one even carpet of the same puffs.
        float thickness = _LayerShape.x * clamp(_LayerShape.y + _LayerShape.z * score, _LayerShape.w, _LayerShapeMax);
        float hLayer = (p.y - _CloudBottom) / thickness;
        if (hLayer >= 1.0)
            return clouds;

        float weatherHere = tex2Dlod(_WeatherTex, float4(uvWeather, 0, 0)).r;
        return max(clouds, LayerDensity(p, detailed, max(lod + _DetailLodShift, 0.0), hLayer, weatherHere, _Threshold));
    }

    float weather = tex2Dlod(_WeatherTex, float4(uvWeather, 0, 0)).r;
    return LayerDensity(p, detailed, lod, h, weather, _Threshold);
}

float SampleDensity(float3 p, bool detailed)
{
    return SampleDensityLod(p, detailed, 0.0);
}

// ---------------------------------------------------------------------------------------------
// Rain. One definition of WHERE it rains, used by the rain curtains (CloudRaymarch), the
// streaks near the camera (RainDrops) and -- ported to C# in CloudRain.LocalRain -- the rain
// sound and wet roads. Same rule as the density above: never fork it.
//
// It rains where the weather field clears a HIGHER threshold than the clouds' own, i.e. under
// the thickest part of the cover. The C# side picks that threshold from the same solved table
// (the threshold for a smaller coverage), so the rain cells are a subset of the clouds by
// construction: light rain is a few showers under the biggest clouds, heavy rain is
// everywhere there is cloud.
float _RainAmount;       // the game's rain, 0..1
float _RainThreshold;
float3 _RainSlant;       // xz: metres a drop drifts downwind per metre it falls

// How hard it is raining at p, 0..1. Zero above the cloud base.
float SampleRain(float3 p)
{
    float below = _CloudBottom - p.y;
    if (below < 0.0)
        return 0.0;

    // A drop at this height left the cloud base upwind of where it is now, so the curtains
    // lean with the wind exactly as the streaks fall.
    float2 xz = p.xz - _RainSlant.xz * below;
    float2 uv = xz / _WeatherTile - _WeatherPhase;
    float weather = tex2Dlod(_WeatherTex, float4(uv, 0, 0)).r;
    return _RainAmount * saturate((weather - _RainThreshold) / _Softness);
}

#endif
