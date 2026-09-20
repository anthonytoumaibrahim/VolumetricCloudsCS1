// Rain streaks near the camera, replacing the game's camera-locked rain.
//
// What made the game's rain read as a filter stuck on the lens, and what this does instead:
//   - its streaks were attached to the camera     -> these live in WORLD space. The mesh is a
//                                                    cloud of random seeds; each is wrapped
//                                                    into a box around the camera, so moving
//                                                    the camera moves THROUGH the rain
//   - it rained everywhere at one strength        -> each drop asks SampleRain (CloudCommon)
//                                                    about its own spot: showers have edges
//   - it drew over everything                     -> depth tested against the scene
//   - it was there at every zoom level            -> fades out with camera height (_DropFade);
//                                                    from high up the rain CURTAINS in
//                                                    CloudRaymarch carry the look
//   - it ignored the wind                         -> falls along _FallDirection, the same
//                                                    slant the curtains lean at
//
// Mesh layout (built in RainDrops.cs), four vertices per drop:
//   POSITION   the drop's seed, 0..1 on each axis
//   TEXCOORD0  x = side (-1 / +1), y = along the streak (0 head .. 1 tail)
//   TEXCOORD1  x = rank (drops with rank above the local rain strength are hidden, so heavier
//                  rain is MORE drops, not brighter ones), y = layer (0 near, 1 far),
//              z = speed variation
Shader "VolumetricClouds/RainDrops"
{
    SubShader
    {
        // After the clouds (Transparent+100): streaks are in front of the curtains.
        Tags { "Queue" = "Transparent+110" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"
            #include "CloudCommon.cginc"

            float3 _BoxNear;          // size of the box the near layer wraps in, metres
            float3 _BoxFar;
            float3 _FallOffset;       // how far the rain has fallen so far (wrapped by the C# side)
            float3 _FallDirection;    // unit vector, downwards and downwind
            float4 _DropColor;        // rgb colour, a = opacity
            float _DropFade;          // 0..1, from camera height
            float _DropDensity;       // 0..1+, scales how many drops show
            float _PixelAngle;        // radians one pixel subtends
            float4 _StreakNear;       // x = length, y = width (metres)
            float4 _StreakFar;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 corner : TEXCOORD0;
                float3 random : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 corner : TEXCOORD0;
                float alpha : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.corner = v.corner;

                float isFar = v.random.y;
                float3 box = lerp(_BoxNear, _BoxFar, isFar);
                float2 streak = lerp(_StreakNear.xy, _StreakFar.xy, isFar);

                // World-anchored: the seed plus the fall so far is a WORLD position; wrapping
                // it relative to the camera keeps the drop in the box without tying it to it.
                float3 fallen = _FallOffset * (0.85 + 0.3 * v.random.z);
                float3 rel = (frac(v.vertex.xyz + (fallen - _WorldSpaceCameraPos) / box + 0.5) - 0.5) * box;
                float3 head = _WorldSpaceCameraPos + rel;

                // EVERYTHING that decides whether and how strongly this drop shows is computed
                // from the head, which all four vertices share. Deciding per corner would let
                // the corners disagree, and a half-collapsed quad is a triangle across the
                // whole screen.
                float dist = length(rel);
                float3 toCamera = -rel / max(dist, 1e-3);

                // Never thinner than a pixel: a 1 cm streak 60 m away would otherwise flicker
                // in and out of existence. Widen it and thin its opacity to match.
                float width = max(streak.y, dist * _PixelAngle);
                float widened = streak.y / width;

                // How hard it is raining where this drop is. Heavier rain shows MORE drops.
                float local = SampleRain(head);
                float shown = saturate((local * _DropDensity - v.random.x) * 8.0);

                // Fade towards the box faces so wrapping never pops, and right at the lens so a
                // drop never smears across the screen.
                float3 edge = abs(rel) / (box * 0.5);
                float boxFade = 1.0 - smoothstep(0.75, 1.0, max(edge.x, max(edge.y, edge.z)));
                float lensFade = smoothstep(0.4, 2.0, dist);

                o.alpha = shown * boxFade * lensFade * widened * _DropFade * _DropColor.a;

                float3 side = cross(_FallDirection, toCamera);
                side /= max(length(side), 1e-3);

                float3 world = head
                             - _FallDirection * streak.x * v.corner.y     // tail trails behind the head
                             + side * width * 0.5 * v.corner.x;

                // Hidden drops collapse outside clip space so they cost no fill.
                o.pos = o.alpha > 0.001 ? mul(UNITY_MATRIX_VP, float4(world, 1.0)) : float4(2, 2, 2, 1);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Soft across the width; bright head, fading tail.
                float across = 1.0 - i.corner.x * i.corner.x;
                float along = 1.0 - smoothstep(0.15, 1.0, i.corner.y);
                return float4(_DropColor.rgb, i.alpha * across * along);
            }
            ENDCG
        }
    }

    Fallback Off
}
