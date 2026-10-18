// What a funnel throws up near the camera, and the water under it (1.5.0, Sky/FunnelDebris.cs).
//
// _Part 0, THE FLECKS: one mesh of seeds, four vertices each --
//   POSITION   the fleck's seed, 0..1 on each axis
//   TEXCOORD0  the corner, -1..1 each way
//   TEXCOORD1  three more random numbers
// -- each flown on a spiral round the funnel's foot: in along the ground, up round the funnel, flung
// out and falling, over and over. Spray, sand, leaves, grit or snow by the ground under it (the C#
// side's GroundLook). Drawn after the air, faded by the fog in front of it (_DebrisColor.a).
//
// _Part 1, THE WATER: one quad on the water under the funnel -- faint, broken foam turning in towards
// it over water a little darker. Drawn just before the air, like the water itself.
//
// Both premultiplied: the water's patch darkens what is under it.
Shader "VolumetricClouds/FunnelDebris"
{
    SubShader
    {
        Tags { "Queue" = "Transparent+96" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5   // not 4.0: no Metal code at 4.0 in Unity 5.6 (see CloudRaymarch.shader)
            #include "UnityCG.cginc"

            float _Part;            // 0 = the flecks, 1 = the water
            float4 _DebrisGround;   // xyz = where the funnel meets the ground or the water, w = its skirt's radius
            float4 _DebrisSize;     // x = the skirt's height, y = the funnel's radius at the ground,
                                    // z = how strong the skirt is (0..1), w = which way round it turns (+1 / -1)
            float4 _DebrisColor;    // rgb = the flecks' albedo, a = their opacity x what the fog in front of them leaves
            float4 _FleckLook;      // x = size (m), y = length over width, z = flutter, w = how high they
                                    // rise (share of the skirt's height)
            float4 _DebrisTime;     // x = seconds (wraps at 1000), y = the funnel's turn (radians),
                                    // z = its seed, w = its opacity
            float3 _DebrisAmbient;
            float3 _DebrisSun;      // already dimmed by the cloud above it
            float4 _DebrisStem;     // xy = the funnel's axis at the ground (x, z), z = its radius there
            float _PixelAngle;      // radians one pixel subtends
            float4 _DebrisReach;    // x = where the flecks start to fade, y = how far they are drawn (m)

            struct appdata
            {
                float4 vertex : POSITION;
                float2 corner : TEXCOORD0;
                float3 extra : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 corner : TEXCOORD0;
                float4 colour : TEXCOORD1;   // flecks: premultiplied; water: rgb = light, a = strength
                float leaf : TEXCOORD2;      // flecks: how square (a leaf) rather than round
            };

            v2f VertFlecks(appdata v)
            {
                v2f o;
                o.corner = v.corner;
                o.leaf = _FleckLook.z;

                float3 seed = v.vertex.xyz;
                float3 e = v.extra;

                // How many show: more the stronger the skirt.
                float strength = _DebrisSize.z * _DebrisTime.w;
                float shown = saturate((strength - e.x) * 6.0);

                // Where in its flight it is, 0..1, over 3-10 s: a whole number of thousandths a second,
                // so its cycle carries on through the clock's wrap.
                float rate = floor(100.0 + 200.0 * e.y) / 1000.0;
                float u = frac(seed.x + _DebrisTime.x * rate);

                float reach = _DebrisGround.w;
                float rise = _DebrisSize.x * _FleckLook.w;
                float orbit = _DebrisSize.y * (1.3 + 1.8 * seed.y);
                float r, h, climb;
                if (u < 0.35)
                {
                    // In along the ground.
                    float k = u / 0.35;
                    r = lerp(reach * (0.8 + 0.4 * seed.z), orbit, k * k);
                    h = 0.5 + 0.04 * _DebrisSize.x * k;
                    climb = 0.05;
                }
                else if (u < 0.75)
                {
                    // Up round the funnel.
                    float k = (u - 0.35) / 0.4;
                    r = orbit;
                    h = lerp(0.04 * _DebrisSize.x, rise, k * (2.0 - k));
                    climb = 0.6;
                }
                else
                {
                    // Flung out, falling.
                    float k = (u - 0.75) / 0.25;
                    r = lerp(orbit, reach * (1.0 + 0.5 * seed.z), k);
                    h = rise * (1.0 - k * k);
                    climb = -0.4;
                }

                float turns = _DebrisSize.w;
                float angle = seed.z * 6.2831853 + turns * (_DebrisTime.y * 1.5 + u * 9.0);
                float3 centre = _DebrisGround.xyz + float3(cos(angle) * r, h, sin(angle) * r);
                float3 flight = normalize(float3(-sin(angle) * turns, climb, cos(angle) * turns));

                float3 toCamera = _WorldSpaceCameraPos - centre;
                float dist = length(toCamera);
                toCamera /= max(dist, 1e-3);

                // Never smaller than a pixel: wider and fainter instead.
                float size = _FleckLook.x * (0.6 + 0.8 * e.z);
                float pixel = dist * _PixelAngle;
                float wide = max(size, pixel);
                float thin = size / wide;
                float lengthwise = max(size * _FleckLook.y, pixel);

                // Leaves turn as they fly: their width comes and goes.
                float flutter = lerp(1.0, abs(sin(_DebrisTime.x * (5.0 + 6.0 * e.y) + seed.y * 40.0)), _FleckLook.z);

                // Behind the funnel's foot as the camera sees it: mostly hidden by it.
                float3 rel = centre - float3(_DebrisStem.x, centre.y, _DebrisStem.y);
                float lateral = length(rel - toCamera * dot(rel, toCamera));
                float hidden = (dot(rel, toCamera) < 0.0 && lateral < _DebrisStem.z * 1.2) ? 0.35 : 1.0;

                float fade = smoothstep(0.0, 0.06, u) * (1.0 - smoothstep(0.9, 1.0, u)) * shown * thin * hidden
                           * (1.0 - smoothstep(_DebrisReach.x, _DebrisReach.y, dist)) * smoothstep(0.5, 3.0, dist);

                float3 across = cross(flight, toCamera);
                across /= max(length(across), 1e-3);
                float3 along = cross(toCamera, across);

                float3 world = centre + across * (v.corner.x * 0.5 * wide * flutter) + along * (v.corner.y * 0.5 * lengthwise);

                float alpha = fade * _DebrisColor.a;
                float3 lit = _DebrisAmbient + _DebrisSun * 0.6;
                o.colour = float4(_DebrisColor.rgb * (0.75 + 0.5 * e.z) * lit * alpha, alpha);

                // Hidden flecks collapse outside clip space so they cost no fill.
                o.pos = alpha > 0.001 ? mul(UNITY_MATRIX_VP, float4(world, 1.0)) : float4(2, 2, 2, 1);
                return o;
            }

            v2f VertWater(appdata v)
            {
                v2f o;
                o.corner = v.corner;
                o.leaf = 0.0;

                float radius = _DebrisGround.w * 1.8;
                float3 world = _DebrisGround.xyz + float3(v.corner.x * radius, 0.3, v.corner.y * radius);
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));

                float3 lit = _DebrisAmbient + _DebrisSun * 0.6;
                o.colour = float4(lit, _DebrisSize.z * _DebrisTime.w);
                return o;
            }

            v2f vert(appdata v)
            {
                if (_Part > 0.5)
                    return VertWater(v);
                return VertFlecks(v);
            }

            float4 FragFlecks(v2f i)
            {
                // Soft: a droplet or a grain is a blur at this size, not an edge (2026-10-18, the author:
                // "the splashes/drops themselves are too defined"). Leaves keep a shape.
                float2 c = i.corner;
                float d2 = dot(c, c);
                float soft = exp(-3.0 * d2) * saturate(1.0 - d2);
                float square = saturate(1.0 - pow(max(max(abs(c.x), abs(c.y)), 1e-6), 4.0));
                return i.colour * lerp(soft, square, i.leaf);
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Smooth value noise, 0..1.
            float ValueNoise(float2 p)
            {
                float2 c = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(c), Hash21(c + float2(1, 0)), f.x),
                            lerp(Hash21(c + float2(0, 1)), Hash21(c + float2(1, 1)), f.x), f.y);
            }

            float4 FragWater(v2f i)
            {
                float2 c = i.corner;
                float rho = length(c);
                clip(1.0 - rho);

                float strength = i.colour.a;
                float turns = _DebrisSize.w;

                // Broken foam drawn in towards the middle as it turns: patches of noise, twisted more the
                // nearer the middle (a spiral without arms), turning with the funnel -- faint (the spiral
                // arms and rings until 2026-10-18 were "like blades, it's too opaque"). And the water a
                // little darker under it (the "dark spot" a waterspout makes before its funnel comes down).
                float a = turns * 2.0 * _DebrisTime.y + 2.5 * log(rho + 0.05);
                float ca = cos(a);
                float sa = sin(a);
                float2 r = float2(c.x * ca - c.y * sa, c.x * sa + c.y * ca) * 5.0;
                float n = ValueNoise(r) * 0.6 + ValueNoise(r * 2.3 + 7.1) * 0.4;
                float foam = smoothstep(0.58, 0.9, n) * smoothstep(0.08, 0.35, rho) * (1.0 - smoothstep(0.45, 1.0, rho)) * 0.4 * strength;
                float dark = (1.0 - smoothstep(0.15, 1.0, rho)) * 0.1 * strength;

                return float4(float3(0.9, 0.92, 0.95) * i.colour.rgb * foam, saturate(foam + dark));
            }

            float4 frag(v2f i) : SV_Target
            {
                return _Part > 0.5 ? FragWater(i) : FragFlecks(i);
            }
            ENDCG
        }
    }

    Fallback Off
}
