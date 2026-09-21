// Shared by FogLamps.shader's two drawing passes: where a light's footprint goes in the map,
// and what it writes there.

#ifndef VOLUMETRIC_CLOUDS_FOG_LAMPS_INCLUDED
#define VOLUMETRIC_CLOUDS_FOG_LAMPS_INCLUDED

float4 _LampMapRect;       // xy = the map's minimum corner (world xz), z = 1 / its size
float _LampFlip;           // clip-space y sign for this render target
float _LampRadius;         // metres: R, where a lamp's light in the fog is down to 5%

struct v2f
{
    float4 pos : SV_POSITION;
    float2 world : TEXCOORD0;      // world xz under this pixel
    float4 light : TEXCOORD1;      // xyz the light, w = 3 / R^2
    float3 color : TEXCOORD2;      // colour x brightness
    float4 spot : TEXCOORD3;       // xy beam direction, z = cos of half angle (-2 = all round), w = 3 / reach^2
};

v2f Footprint(float3 lightPos, float2 corner, float extent, float3 color, float4 spot)
{
    v2f o;

    float2 xz = lightPos.xz + corner * extent;
    float2 uv = (xz - _LampMapRect.xy) * _LampMapRect.z;
    float2 clip = uv * 2.0 - 1.0;
    clip.y *= _LampFlip;

    o.pos = float4(clip, 0.5, 1.0);
    o.world = xz;
    o.light = float4(lightPos, 3.0 / (_LampRadius * _LampRadius));
    o.color = color;
    o.spot = spot;
    return o;
}

// rgb = the light here; a = luminance x the light's altitude, so the fog can recover the
// height of what lights it (a / luminance) and fall off above and below it.
float4 Splat(v2f i)
{
    float2 d = i.world - i.light.xz;
    float r2 = dot(d, d);
    float f;

    if (i.spot.z > -1.5)
    {
        // A beam: forwards only, inside the cone, fading with its reach. The first metre is
        // let through all round, so the light does not start at a knife edge.
        float len = sqrt(r2) + 1e-3;
        float c = dot(d / len, i.spot.xy);
        float cone = smoothstep(i.spot.z, lerp(i.spot.z, 1.0, 0.35), c);
        f = exp(-r2 * i.spot.w) * max(cone, saturate(1.0 - len));
    }
    else
    {
        f = exp(-r2 * i.light.w);
    }

    float3 rgb = i.color * f;
    float lum = dot(rgb, float3(0.2126, 0.7152, 0.0722));
    return float4(rgb, lum * i.light.y);
}

#endif
