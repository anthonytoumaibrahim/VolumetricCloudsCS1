// A lightning channel: a jagged, branching polyline generated per strike (CloudLightning.cs)
// and drawn as camera-facing ribbons, from inside OUR cloud layer down to where the game says
// the strike lands. The game's own bolt is a fixed model whose height knows nothing about
// where our cloud base is.
//
// Drawn BEFORE the cloud pass on purpose: the cloud pass then blends over it, so the top of the
// channel disappears into the cloud it comes out of and the rest is dimmed by the rain it is
// seen through. Transparent+90 is just before the one draw (Transparent+100); with the camera
// under the cloud base the clouds are drawn early (CloudVolume.UpdateParts), and CloudLightning
// puts the bolt just before them. This is 1.2's shader: 1.3.0 drew the bolt AFTER the clouds,
// dimmed by only what lies in front of it (a march of the cloud, rain and fog, here), and in-game
// that looked "in front" of the cloud (the author, 2026-09-30: "Not a fan"). That version is in
// git history. Since 1.5.0 the cloud pass stops a little BEHIND a bolt on its own pixels
// (LightningBoltMap.shader, CloudRaymarch BoltStop): its top still fades into its own cloud, but a
// cloud far behind it no longer cuts it to pieces. This shader is unchanged by that.
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
            // RibbonCorner and GLOW_SCALE, shared with LightningBoltMap.shader.
            #include "LightningRibbon.cginc"

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
                world = RibbonCorner(world, v.tangent, v.shape, _WorldSpaceCameraPos, _PixelAngle);

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
