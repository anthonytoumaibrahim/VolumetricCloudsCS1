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
float3 _WindOffset;

float Remap(float v, float a, float b, float c, float d)
{
    return c + (v - a) / max(b - a, 1e-4) * (d - c);
}

// Rounded base, tapering top: what makes a slab read as cumulus rather than fog.
float HeightShape(float h)
{
    return saturate(h * 5.0) * saturate((1.0 - h) * 2.5);
}

float SampleDensity(float3 p, bool detailed)
{
    float h = (p.y - _CloudBottom) / (_CloudTop - _CloudBottom);
    if (h <= 0.0 || h >= 1.0)
        return 0.0;

    float2 uvWeather = (p.xz - _WindOffset.xz) / _WeatherTile;
    float weather = tex2Dlod(_WeatherTex, float4(uvWeather, 0, 0)).r;
    float coverage = saturate((weather - _Threshold) / _Softness);
    if (coverage <= 0.0)
        return 0.0;

    float shaped = coverage * HeightShape(h);

    // The volume drifts slightly faster than the weather so clouds churn instead of
    // sliding as a rigid sheet.
    float3 uvNoise = (p - _WindOffset * 1.25) / _NoiseTile;
    float base = tex3Dlod(_NoiseTex, float4(uvNoise, 0)).r;

    float d = saturate(Remap(base, 1.0 - shaped, 1.0, 0.0, 1.0)) * shaped;

    // Erosion: subtract fine cellular noise from the solid shape. This is what breaks a
    // cloud up from one smooth mass into lobes, tufts and ragged edges. Thin parts of the
    // cloud vanish first, so raising the strength also opens gaps through it.
    if (detailed && d > 0.0 && _DetailStrength > 0.0)
    {
        float detail = tex3Dlod(_NoiseTex, float4(uvNoise * _DetailScale, 0)).g;
        d = saturate(Remap(d, detail * _DetailStrength, 1.0, 0.0, 1.0));
    }

    return d * _DensityScale;
}

#endif
