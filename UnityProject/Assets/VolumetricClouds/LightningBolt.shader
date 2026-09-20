// A lightning channel: a jagged, branching polyline generated per strike (CloudLightning.cs)
// and drawn as camera-facing ribbons, from inside OUR cloud layer down to where the game says
// the strike lands. The game's own bolt is a fixed model whose height knows nothing about
// where our cloud base is.
//
// Drawn BEFORE the cloud pass (Transparent+100) on purpose: the cloud pass then blends over
// it, so the top of the channel disappears into the cloud it comes out of and the rest is
// dimmed by the rain it is seen through.
//
// Mesh layout, four vertices per segment, in WORLD space:
//   POSITION   the segment's end point this vertex belongs to
//   NORMAL     the segment's direction (unit)
//   TEXCOORD0  x = side (-1 / +1), y = core width in metres
//   TEXCOORD1  x = brightness of this branch (1 main channel, less for forks)
Shader "VolumetricClouds/LightningBolt"
{
    SubShader
    {
        Tags { "Queue" = "Transparent+90" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            // The ribbon is this many core-widths wide: a thin white-hot core inside a soft glow.
            #define GLOW_SCALE 6.0

            float4 _BoltColor;       // rgb, HDR
            float _BoltIntensity;    // this frame's flicker, 0..1
            float _PixelAngle;       // radians one pixel subtends

            struct appdata
            {
                float4 vertex : POSITION;
                float3 tangent : NORMAL;
                float2 shape : TEXCOORD0;
                float2 branch : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 across : TEXCOORD0;     // x = -1..1 across the ribbon, y = brightness
            };

            v2f vert(appdata v)
            {
                v2f o;

                float3 world = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float3 toCamera = _WorldSpaceCameraPos - world;
                float dist = length(toCamera);

                // Lightning is seen from kilometres away: never let the core go sub-pixel.
                float width = max(v.shape.y, dist * _PixelAngle * 1.5);

                float3 side = cross(v.tangent, toCamera / max(dist, 1e-3));
                side /= max(length(side), 1e-3);

                world += side * (width * 0.5 * GLOW_SCALE) * v.shape.x;

                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                o.across = float2(v.shape.x, v.branch.x);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float x = abs(i.across.x);
                float core = exp(-pow(x * GLOW_SCALE, 2.0) * 1.2);
                float glow = exp(-x * 3.5) * 0.16;

                float3 colour = _BoltColor.rgb * ((core + glow) * i.across.y * _BoltIntensity);
                return float4(colour, 0.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
