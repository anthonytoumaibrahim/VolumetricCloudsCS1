// Raymarched volumetric cloud layer for Cities: Skylines (Unity 5.6, built-in pipeline).
//
// Drawn on a box that follows the camera, so every pixel gets a fragment. Each fragment
// intersects its view ray with the cloud slab [_CloudBottom, _CloudTop] and marches it.
//
//   _WeatherTex : 2D tiling density field, the same one the shadow cookie is cut from.
//                 Thresholded here with _Threshold/_Softness so coverage is one uniform.
//   _NoiseTex   : 3D tiling noise. R = Perlin-Worley base shape, G = Worley detail.
Shader "VolumetricClouds/CloudRaymarch"
{
    Properties
    {
        _WeatherTex ("Weather (R)", 2D) = "black" {}
        _NoiseTex ("Noise (RG)", 3D) = "" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha   // premultiplied

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "CloudCommon.cginc"

            sampler2D_float _CameraDepthTexture;

            float _Steps;
            float _MaxDistance;
            float _UseDepth;
            float3 _SunDir;        // towards the light
            float3 _SunColor;
            float3 _AmbientColor;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5);
            }

            // How much sunlight reaches p: a short march towards the light.
            float SunTransmittance(float3 p)
            {
                const int LIGHT_STEPS = 5;
                float stepLen = (_CloudTop - _CloudBottom) * 0.11;
                float sum = 0.0;

                for (int i = 1; i <= LIGHT_STEPS; i++)
                {
                    // Lengthening steps: nearby detail matters, the far end only has to
                    // know "is there a lot of cloud in the way".
                    float3 lp = p + _SunDir * stepLen * (float)i * (0.6 + 0.2 * (float)i);
                    sum += SampleDensity(lp, false);
                }

                float depth = sum * stepLen * _Absorption;
                // Beer plus a slower-falling term so thick clouds keep some interior
                // light instead of going pitch black.
                return max(exp(-depth), 0.7 * exp(-depth * 0.25));
            }

            float Hash12(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 origin = _WorldSpaceCameraPos;
                float3 dir = normalize(i.worldPos - origin);

                // Slab intersection.
                float dy = abs(dir.y) < 1e-4 ? (dir.y < 0 ? -1e-4 : 1e-4) : dir.y;
                float tA = (_CloudBottom - origin.y) / dy;
                float tB = (_CloudTop - origin.y) / dy;
                float tEnter = max(min(tA, tB), 0.0);
                float tExit = min(max(tA, tB), _MaxDistance);

                // Stop at scene geometry, so buildings and hills hide clouds behind them.
                if (_UseDepth > 0.5)
                {
                    float raw = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos));
                    float eye = LinearEyeDepth(raw);
                    float3 forward = -UNITY_MATRIX_V[2].xyz;
                    float sceneDist = eye / max(dot(dir, forward), 1e-4);
                    // Leave the sky alone: at the far plane there is nothing to hide behind.
                    if (eye < _ProjectionParams.z * 0.98)
                        tExit = min(tExit, sceneDist);
                }

                if (tExit <= tEnter)
                    return float4(0, 0, 0, 0);

                float steps = max(_Steps, 8.0);
                float stepLen = (tExit - tEnter) / steps;

                // Per-pixel jitter trades banding for fine noise, which reads as softness.
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float t = tEnter + stepLen * Hash12(screenUV * _ScreenParams.xy);

                float cosTheta = dot(dir, _SunDir);
                // Mostly isotropic with a forward lobe: gives the bright rim towards the
                // sun without leaving clouds black everywhere else.
                float phase = 0.65 + 0.35 * min(HenyeyGreenstein(cosTheta, 0.55), 5.0);

                float transmittance = 1.0;
                float3 light = 0;

                [loop]
                for (int s = 0; s < 96; s++)
                {
                    if ((float)s >= steps || transmittance < 0.02)
                        break;

                    float3 p = origin + dir * t;
                    float d = SampleDensity(p, true);

                    if (d > 0.001)
                    {
                        float sun = SunTransmittance(p);
                        float h = saturate((p.y - _CloudBottom) / (_CloudTop - _CloudBottom));

                        // Undersides get less sky light than tops.
                        float3 ambient = _AmbientColor * (0.45 + 0.55 * h);
                        float3 radiance = _SunColor * sun * phase + ambient;

                        float stepT = exp(-d * stepLen * _Absorption);
                        // Energy-conserving integration across the step.
                        light += transmittance * radiance * (1.0 - stepT);
                        transmittance *= stepT;
                    }

                    t += stepLen;
                }

                float alpha = 1.0 - transmittance;

                // Dissolve into the distance rather than ending on a hard line.
                float fade = saturate(1.0 - tEnter / _MaxDistance);
                fade *= fade;

                return float4(light * fade, alpha * fade);
            }
            ENDCG
        }
    }

    Fallback Off
}
