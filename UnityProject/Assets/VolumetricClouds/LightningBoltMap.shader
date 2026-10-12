// The lightning bolts' distance from the camera, drawn into a screen-sized map (Sky/BoltMap.cs, 1.5.0)
// that the cloud pass reads to stop a little behind a bolt on its own pixels (LightningBoltMap.cginc).
// Not drawn to the screen: BoltMap draws it with its own matrix into its own texture.
//
// Pass 0: the bolt meshes (Sky/LightningBoltMesh.cs), widened exactly as LightningBolt.shader widens
//         them (LightningRibbon.cginc), and only their core and inner glow: the outer glow is faint,
//         and there a cloud behind still shows as before. The nearest bolt wins (BlendOp Min).
// Pass 1: the orientation self-check: reads the map at points on a bolt the way the cloud pass does,
//         and at the same points mirrored top to bottom (BoltMap.Check).
Shader "VolumetricClouds/LightningBoltMap"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            BlendOp Min
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5   // not 4.0: no Metal code at 4.0 in Unity 5.6 (see CloudRaymarch.shader)
            #include "UnityCG.cginc"
            #include "LightningRibbon.cginc"
            #include "LightningBoltMap.cginc"

            // How much of the ribbon's half-width counts as the bolt: the core is about 0.2 of it, and
            // at 0.35 the glow is down to a twentieth of the centre's brightness.
            #define BOLT_MAP_CORE 0.35

            float4x4 _BoltMapGPUVP;  // the camera's view-projection for drawing into a texture (GL.GetGPUProjectionMatrix)
            float _PixelAngle;       // radians one pixel subtends

            struct appdata
            {
                float4 vertex : POSITION;      // world space: the mesh is drawn with the identity matrix
                float3 tangent : NORMAL;
                float2 shape : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float across : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 world = RibbonCorner(v.vertex.xyz, v.tangent, v.shape, _VCBoltMapEye.xyz, _PixelAngle);
                o.pos = mul(_BoltMapGPUVP, float4(world, 1.0));
                o.world = world;
                o.across = v.shape.x;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                clip(BOLT_MAP_CORE - abs(i.across));
                return float4(distance(i.world, _VCBoltMapEye.xyz), 0, 0, 0);
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
            #pragma target 3.5   // not 4.0: no Metal code at 4.0 in Unity 5.6 (see CloudRaymarch.shader)
            #include "UnityCG.cginc"
            #include "LightningBoltMap.cginc"

            #define PROBE_POINTS 8

            float4 _ProbePoints[PROBE_POINTS];  // xyz = points on a bolt's channel

            // Pixel 2k reads point k the way the cloud pass would, pixel 2k+1 the same point mirrored
            // top to bottom: 1 where a bolt is found.
            float4 frag(v2f_img i) : SV_Target
            {
                uint pixel = (uint)clamp(i.uv.x * (2 * PROBE_POINTS), 0.0, 2.0 * PROBE_POINTS - 1.0);
                float2 uv = BoltMapUV(_ProbePoints[pixel >> 1].xyz);
                if ((pixel & 1u) != 0u && uv.x >= 0.0)
                    uv.y = 1.0 - uv.y;

                return float4(BoltMapDistance(uv) < BOLT_MAP_NONE ? 1.0 : 0.0, 0, 0, 1);
            }
            ENDCG
        }
    }

    Fallback Off
}
