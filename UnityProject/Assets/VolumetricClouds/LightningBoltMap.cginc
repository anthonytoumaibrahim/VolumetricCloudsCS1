// Where the lightning bolts are, as the cloud pass sees them (Sky/BoltMap.cs, 1.5.0).
//
// The bolt is drawn BEFORE the clouds (LightningBolt.shader), so the cloud pass lays every cloud on
// the line of sight over it: its top fades into the cloud it comes out of -- and a cloud far BEHIND
// it cut it to pieces (the author, 2026-10-12: "the clouds behind are rendering on TOP of the
// lightning bolt"). So each frame a bolt shows, its core is drawn again into a screen-sized map of
// its distance from the camera (LightningBoltMap.shader), and on those pixels the clouds, rain and
// fog stop a little behind it: BoltStop. Globals, set in the same frame as the map is drawn.
#ifndef VC_LIGHTNING_BOLT_MAP_INCLUDED
#define VC_LIGHTNING_BOLT_MAP_INCLUDED

sampler2D _VCBoltMap;       // r = metres from the camera to the bolt drawn there; cleared far above BOLT_MAP_NONE
float4x4 _VCBoltMapVP;      // the camera's view-projection the map was drawn with (OpenGL convention)
float4 _VCBoltMapEye;       // xyz = where the camera was then; w = the distance along a ray it is looked up at
float4 _VCBoltMapParams;    // x = 1 while a bolt shows (0: nothing of this runs), y = how far the clouds still
                            // count, as a multiple of the bolt's distance, z = the map's up (+1 / -1), w unused

#define BOLT_MAP_NONE 60000.0

// The map's texel for a point in the world; (-1, -1) off the map.
float2 BoltMapUV(float3 p)
{
    float4 clip = mul(_VCBoltMapVP, float4(p, 1.0));
    if (clip.w <= 1e-3)
        return float2(-1.0, -1.0);

    float2 ndc = clip.xy / clip.w;
    float2 uv = float2(ndc.x * 0.5 + 0.5, ndc.y * _VCBoltMapParams.z * 0.5 + 0.5);
    return (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) ? float2(-1.0, -1.0) : uv;
}

// Metres from the camera to the bolt at this texel; above BOLT_MAP_NONE where there is none or off the map.
float BoltMapDistance(float2 uv)
{
    return uv.x < 0.0 ? 1e9 : tex2Dlod(_VCBoltMap, float4(uv, 0, 0)).r;
}

// Where a ray from the camera stops counting clouds, rain and fog: a little behind a bolt on its own
// pixels, 1e9 everywhere else. Looked up at a point on the ray as far out as the bolts are, through
// the matrix the map was drawn with, so a camera that moved after the map still finds them. Another
// camera (somewhere else) never does.
float BoltStop(float3 origin, float3 dir)
{
    if (_VCBoltMapParams.x < 0.5)
        return 1e9;

    float3 away = origin - _VCBoltMapEye.xyz;
    if (dot(away, away) > 200.0 * 200.0)
        return 1e9;

    float d = BoltMapDistance(BoltMapUV(origin + dir * _VCBoltMapEye.w));
    return d < BOLT_MAP_NONE ? d * _VCBoltMapParams.y : 1e9;
}

#endif
