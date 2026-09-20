// Raymarched volumetric cloud layer for Cities: Skylines (Unity 5.6, built-in pipeline).
//
// Drawn on a box that follows the camera, so every pixel gets a fragment. Each fragment
// intersects its view ray with the cloud slab [_CloudBottom, _CloudTop] and marches it, then
// marches the rain hanging underneath: one pass, one view ray, one depth read, so the two
// media composite correctly against each other and the scene.
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
            #pragma target 4.0
            #include "UnityCG.cginc"
            #include "CloudCommon.cginc"

            sampler2D_float _CameraDepthTexture;

            float _Steps;
            float _MaxDistance;
            float _UseDepth;
            float3 _SunDir;        // towards the light
            float3 _SunColor;
            float3 _AmbientColor;

            // Rain curtains under the cloud base. Where it rains comes from CloudCommon.
            float _RainShaftDensity;   // extinction per metre where it rains at full strength
            float _RainSteps;
            float _RainMaxDistance;
            float _RainFloor;
            float _RainFall;           // metres fallen so far; slides the curtain pattern down
            float _RainCurtainScale;
            float3 _RainAmbient;
            float3 _RainSun;

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

            // The cloud slab between tEnter and tExit. Returns premultiplied light in rgb and
            // what is left of the background in a.
            float4 MarchClouds(float3 origin, float3 dir, float tEnter, float tExit, float jitter, float cosTheta)
            {
                if (tExit <= tEnter)
                    return float4(0, 0, 0, 1);

                float steps = max(_Steps, 8.0);
                float stepLen = (tExit - tEnter) / steps;

                // Per-pixel jitter trades banding for fine noise, which reads as softness.
                float t = tEnter + stepLen * jitter;

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

                // Dissolve into the distance rather than ending on a hard line.
                float fade = saturate(1.0 - tEnter / _MaxDistance);
                fade *= fade;

                return float4(light * fade, 1.0 - (1.0 - transmittance) * fade);
            }

            // Rain hanging under the clouds: the grey curtains seen from a distance, and the
            // loss of visibility from inside a shower. Same return convention as MarchClouds.
            float4 MarchRain(float3 origin, float3 dir, float tStart, float tEnd, float jitter, float cosTheta)
            {
                if (tEnd <= tStart)
                    return float4(0, 0, 0, 1);

                float steps = clamp(_RainSteps, 4.0, 32.0);
                float len = tEnd - tStart;

                // Rain scatters forwards far more than cloud does: bright towards the sun.
                float phase = 0.5 + 0.5 * min(HenyeyGreenstein(cosTheta, 0.7), 6.0);
                float3 radiance = _RainAmbient + _RainSun * phase;

                float transmittance = 1.0;
                float3 light = 0;
                float tPrev = tStart;

                [loop]
                for (int s = 0; s < 32; s++)
                {
                    if ((float)s >= steps || transmittance < 0.02)
                        break;

                    // Segments lengthen with distance: the edge of a shower 500 m away needs
                    // precision, the far end of a 10 km ray does not.
                    float f = ((float)s + 1.0) / steps;
                    float tNext = tStart + len * f * f;
                    float segment = tNext - tPrev;
                    float t = tPrev + segment * jitter;

                    float3 p = origin + dir * t;
                    float rain = SampleRain(p);

                    if (rain > 0.001)
                    {
                        // Uneven curtains, stretched tall, sliding down as the rain falls.
                        // The 6 is CloudRain.CurtainStretch; the fall wraps on a multiple of it.
                        float3 q = float3(p.x, (p.y + _RainFall) / 6.0, p.z) / _RainCurtainScale;
                        float curtain = 0.5 + tex3Dlod(_NoiseTex, float4(q, 0)).r;

                        float fade = saturate(1.0 - t / _RainMaxDistance);
                        float sigma = _RainShaftDensity * rain * curtain * fade * fade;

                        float stepT = exp(-sigma * segment);
                        light += transmittance * radiance * (1.0 - stepT);
                        transmittance *= stepT;
                    }

                    tPrev = tNext;
                }

                return float4(light, transmittance);
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 origin = _WorldSpaceCameraPos;
                float3 dir = normalize(i.worldPos - origin);

                // Slab intersection. tA is where the ray crosses the cloud base.
                float dy = abs(dir.y) < 1e-4 ? (dir.y < 0 ? -1e-4 : 1e-4) : dir.y;
                float tA = (_CloudBottom - origin.y) / dy;
                float tB = (_CloudTop - origin.y) / dy;
                float tEnter = max(min(tA, tB), 0.0);
                float tExit = min(max(tA, tB), _MaxDistance);

                // Stop at scene geometry, so buildings and hills hide what is behind them.
                float sceneDist = 1e9;
                if (_UseDepth > 0.5)
                {
                    float raw = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos));
                    float eye = LinearEyeDepth(raw);
                    float3 forward = -UNITY_MATRIX_V[2].xyz;
                    // Leave the sky alone: at the far plane there is nothing to hide behind.
                    if (eye < _ProjectionParams.z * 0.98)
                        sceneDist = eye / max(dot(dir, forward), 1e-4);
                }
                tExit = min(tExit, sceneDist);

                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float jitter = Hash12(screenUV * _ScreenParams.xy);
                float cosTheta = dot(dir, _SunDir);

                float4 clouds = MarchClouds(origin, dir, tEnter, tExit, jitter, cosTheta);

                // The part of the ray under the cloud base: from the camera when it is below
                // the base, otherwise from where the ray comes down through it.
                float4 rain = float4(0, 0, 0, 1);
                bool cameraBelowBase = origin.y < _CloudBottom;
                if (_RainAmount > 0.001 && _RainShaftDensity > 0.0)
                {
                    float rStart = cameraBelowBase ? 0.0 : (dir.y < 0.0 ? tA : 1e9);
                    float rEnd = (cameraBelowBase && dir.y > 0.0) ? tA : 1e9;

                    // With depth occlusion off there is no ground to stop at; use sea level.
                    if (dir.y < 0.0)
                        rEnd = min(rEnd, (_RainFloor - origin.y) / dy);

                    rEnd = min(rEnd, min(sceneDist, _RainMaxDistance));
                    rain = MarchRain(origin, dir, rStart, rEnd, jitter, cosTheta);
                }

                // The two media never overlap along a ray, so compositing is just "nearer
                // over farther": rain first from under the clouds, clouds first from above.
                float4 front = cameraBelowBase ? rain : clouds;
                float4 back = cameraBelowBase ? clouds : rain;

                float3 light = front.rgb + front.a * back.rgb;
                float alpha = 1.0 - front.a * back.a;
                return float4(light, alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
