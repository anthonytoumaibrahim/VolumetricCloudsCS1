// The city's lights, drawn from above into a camera-centred map that the fog march reads
// (CloudRaymarch: LampLightAt). Our fog is lit by the lamps IN it -- the glow round a street
// light on a foggy night is the fog, not the lamp -- and the halos are not touched.
//
// Each light becomes a soft footprint, exp(-3 r^2 / R^2), of its colour x brightness: 5% at
// R. A Gaussian is separable, so the height the map loses comes back exactly at lookup:
// alpha holds luminance x altitude, and the fog multiplies by exp(-3 dz^2 / R^2) against the
// altitude it recovers (a / luminance). Where lamps at different heights overlap, that is
// their luminance-weighted mean height.
//
// Pass 0: the game's batched light meshes (street and building lamps), decoded exactly as
//         LightHalo.shader decodes them, blinking and day/night switching included.
// Pass 1: dynamic lights (vehicles, IMT's lamps), from a mesh FogLampMap builds each frame
//         out of the LightSystem.DrawLight calls. Spot lights become a beam.
// Pass 2: the orientation self-check: samples the map at two points the way the fog does.
//
// The map's position on screen is worked out here, not by a camera: uv = (xz - min) / size,
// clip = uv * 2 - 1, and clip.y flipped by _LampFlip, which FogLampMap takes from
// GL.GetGPUProjectionMatrix (-1 where render targets are stored top row first, i.e. D3D).
Shader "VolumetricClouds/FogLamps"
{
    SubShader
    {
        Pass
        {
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"
            #include "FogLampsCommon.cginc"

            // Published by the game every frame.
            float _GiantMarshmallow;      // daylight amount; lights switch on below their threshold
            float4 _SimulationTime;
            sampler2D _ObjectColorMap;

            // { rampStart, rampEnd, fallEnd, period } per blink pattern: LightHalo.shader's.
            static const float4 kBlink[7] =
            {
                float4(0.00, -1.00,  2.00,  1.00),
                float4(0.49,  0.50,  0.99,  1.00),
                float4(0.00,  0.50, -0.25,  0.25),
                float4(0.00,  6.00,  4.00, 10.00),
                float4(0.00,  0.30, -0.10,  0.25),
                float4(0.00,  2.50,  2.50,  5.00),
                float4(0.24,  0.25,  0.49,  0.50),
            };

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float3 lightPos : NORMAL;
                float2 rangeIntensity : TEXCOORD0;
                float2 switching : TEXCOORD1;
                float2 colorUV : TEXCOORD2;
            };

            float Smooth01(float x) { return x * x * (3.0 - 2.0 * x); }

            v2f vert(appdata v)
            {
                // Blink pattern and day/night switching -> brightness: LightHalo.shader's lines.
                int pattern = clamp((int)v.switching.y, 0, 6);
                float4 b = kBlink[pattern];
                float t = frac(v.switching.x * 3.71 + _SimulationTime.x / b.w) * b.w;
                float rise = saturate((t - b.x) / (b.y - b.x));
                float fall = saturate((t - b.w) / (b.z - b.w));
                float blink = 1.0 - Smooth01(rise) * Smooth01(fall);

                float on = saturate((v.switching.x + 0.01 - _GiantMarshmallow) * 50.0);
                float bright = blink * Smooth01(on) * v.rangeIntensity.y;

                // Per-building state: an abandoned building's lights are out.
                bright *= tex2Dlod(_ObjectColorMap, float4(v.colorUV, 0, 0)).a;

                // The corner's offset from the light says which corner of the quad this is
                // (LightHalo.shader: offset.xy pick the corner). Lights that are off collapse.
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float2 corner = sign((world - v.lightPos).xy);
                float extent = bright >= 0.001 ? 1.8 * _LampRadius : 0.0;

                return Footprint(v.lightPos, corner, extent, v.color.rgb * bright, float4(0, 0, -2.0, 0));
            }

            float4 frag(v2f i) : SV_Target
            {
                return Splat(i);
            }
            ENDCG
        }

        Pass
        {
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"
            #include "FogLampsCommon.cginc"

            // FogLampMap's own layout, one quad per light.
            struct appdata
            {
                float4 vertex : POSITION;      // xy = corner, -1 or 1
                float4 color : COLOR;          // colour, 0..1 (vertex colours may be stored in 8 bits)
                float3 lightPos : NORMAL;
                float4 beam : TANGENT;         // xyz direction, w = cos of the half angle; -2 = all round
                float2 reach : TEXCOORD0;      // x = the light's range, y = its intensity
            };

            v2f vert(appdata v)
            {
                float2 along = v.beam.xz;
                float flat = length(along);
                bool isBeam = v.beam.w > -1.5 && flat > 0.2;

                // A beam reaches as far as the light's range, and only forwards; a light all
                // round is a footprint like a lamp's.
                float reach = max(v.reach.x, 1.0);
                float extent = isBeam ? 1.2 * reach : 1.8 * _LampRadius;
                float4 spot = isBeam ? float4(along / flat, v.beam.w, 3.0 / (reach * reach)) : float4(0, 0, -2.0, 0);

                return Footprint(v.lightPos, v.vertex.xy, extent, v.color.rgb * v.reach.y, spot);
            }

            float4 frag(v2f i) : SV_Target
            {
                return Splat(i);
            }
            ENDCG
        }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"

            #define PROBE_LAMPS 16

            sampler2D _VCLampMap;
            float4 _ProbeUV[PROBE_LAMPS];  // xy = where a lamp is, zw = the same point mirrored in z

            // Pixel 2k samples lamp k, pixel 2k+1 its mirror image, the way the fog samples the
            // map. Squashed into 0..1 so an 8-bit target can carry it.
            float4 frag(v2f_img i) : SV_Target
            {
                uint pixel = (uint)clamp(i.uv.x * (2 * PROBE_LAMPS), 0.0, 2.0 * PROBE_LAMPS - 1.0);
                float4 probe = _ProbeUV[pixel >> 1];
                float2 uv = (pixel & 1u) == 0u ? probe.xy : probe.zw;
                float3 rgb = tex2Dlod(_VCLampMap, float4(uv, 0, 0)).rgb;
                float lum = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                return float4(lum / (lum + 1.0), 0, 0, 1);
            }
            ENDCG
        }
    }

    Fallback Off
}
