// Renders the cloud shadow map that gets assigned as the sun's light cookie.
//
// Unity projects a directional cookie in the light's local XY plane, centred on the light:
//     uv = localXY / cookieSize + 0.5
// so every texel corresponds to one ray parallel to the sun. This pass walks that ray
// through the cloud slab -- using the very same SampleDensity as the visible clouds -- and
// writes how much sunlight survives. Assigned as the cookie, it lands each cloud's shadow
// exactly where the sun would put it.
//
// The built-in lighting passes read the cookie's ALPHA channel.
Shader "VolumetricClouds/CloudShadowMap"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "CloudCommon.cginc"

            float3 _LightPos;
            float3 _LightRight;
            float3 _LightUp;
            float3 _LightForward;   // direction the light travels (from sun towards ground)
            float _CookieSize;
            float _ShadowDepth;     // 0 = no shadow, 1 = thick cloud blocks all direct light
            float _ShadowFullness;  // >1 makes thin cloud cast a fuller shadow, <1 a fainter one
            float _ShadowSteps;

            float4 frag(v2f_img i) : SV_Target
            {
                // The line of constant cookie-uv: every point on it is lit by this texel.
                float2 local = (i.uv - 0.5) * _CookieSize;
                float3 origin = _LightPos + _LightRight * local.x + _LightUp * local.y;
                float3 dir = _LightForward;

                // Sun at or below the horizon: nothing sensible to cast.
                if (dir.y > -0.02)
                    return float4(1, 1, 1, 1);

                float sTop = (_CloudTop - origin.y) / dir.y;
                float sBottom = (_CloudBottom - origin.y) / dir.y;
                float sEnter = min(sTop, sBottom);
                float sExit = max(sTop, sBottom);

                // A low sun makes the path through the slab very long; cap it so the fixed
                // step count doesn't skate over whole clouds.
                float thickness = _CloudTop - _CloudBottom;
                sExit = min(sExit, sEnter + thickness * 8.0);

                float steps = max(_ShadowSteps, 4.0);
                float stepLen = (sExit - sEnter) / steps;
                float s = sEnter + stepLen * 0.5;

                float opticalDepth = 0.0;

                [loop]
                for (int n = 0; n < 32; n++)
                {
                    if ((float)n >= steps)
                        break;

                    opticalDepth += SampleDensity(origin + dir * s, true);
                    s += stepLen;
                }

                // The visible clouds are tuned to look soft, which leaves their edges and
                // thinner parts letting most sunlight through. Shadows read better fuller than
                // that, so they get their own multiplier on the same optical depth.
                float transmittance = exp(-opticalDepth * stepLen * _Absorption * max(_ShadowFullness, 0.01));
                float lit = 1.0 - (1.0 - transmittance) * _ShadowDepth;

                return float4(lit, lit, lit, lit);
            }
            ENDCG
        }
    }

    Fallback Off
}
