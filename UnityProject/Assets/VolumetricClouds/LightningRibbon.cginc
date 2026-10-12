// The bolt's ribbon (Sky/LightningBoltMesh.cs): each mesh vertex sits ON the channel and is moved
// sideways, square to the view, out to the ribbon's edge. LightningBolt.shader draws the bolt with it
// and LightningBoltMap.shader the bolt's distance (1.5.0), so the two cover the same pixels.
#ifndef VC_LIGHTNING_RIBBON_INCLUDED
#define VC_LIGHTNING_RIBBON_INCLUDED

// The ribbon is this many core-widths wide: a thin white-hot core inside a soft glow.
#define GLOW_SCALE 6.0

// world: the vertex's point on the channel; tangent: the channel's direction there (unit);
// shape: x = side (-1 / +1), y = core width in metres; eye: the camera; pixelAngle: radians a pixel subtends.
float3 RibbonCorner(float3 world, float3 tangent, float2 shape, float3 eye, float pixelAngle)
{
    float3 toCamera = eye - world;
    float dist = length(toCamera);

    // Lightning is seen from kilometres away: never let the core go sub-pixel.
    float width = max(shape.y, dist * pixelAngle * 1.5);

    float3 side = cross(tangent, toCamera / max(dist, 1e-3));
    side /= max(length(side), 1e-3);

    return world + side * (width * 0.5 * GLOW_SCALE) * shape.x;
}

#endif
