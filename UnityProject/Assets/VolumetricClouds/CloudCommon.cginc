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

// `lod` is the detail texture's mip level: distant detail averages away instead of aliasing
// (the raymarch works it out from the pixel's footprint, the shadow map from its texel).
float SampleDensityLod(float3 p, bool detailed, float lod)
{
    float h = (p.y - _CloudBottom) / (_CloudTop - _CloudBottom);
    if (h <= 0.0 || h >= 1.0)
        return 0.0;

    float2 uvWeather = p.xz / _WeatherTile - _WeatherPhase;
    float weather = tex2Dlod(_WeatherTex, float4(uvWeather, 0, 0)).r;
    float coverage = saturate((weather - _Threshold) / _Softness);

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
            float shiftWeather = (weather - _Threshold) * _DetailLookup.y * 2.0;
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

            d = saturate(Remap(d, erosion * erode, 1.0, 0.0, 1.0));
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
