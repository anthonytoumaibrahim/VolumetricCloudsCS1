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

            // Lightning: point sources INSIDE the cloud layer (CloudLightning.cs). The clouds
            // only ever knew the sun, the moon and the ambient, so the game's strikes lit the
            // whole city and left the sky dark.
            #define MAX_FLASHES 4
            float _FlashCount;
            float4 _FlashPos[MAX_FLASHES];     // xyz world position, w = reach in metres
            float4 _FlashColor[MAX_FLASHES];   // rgb, already scaled by this frame's flicker

            // Night. Clouds dissolve into the distance by going TRANSPARENT, which by day lets
            // sky colour through and reads as haze -- and by night lets the stars through.
            // _NightOpacity (0 by day) takes that transparency away, out to the edge of the
            // box the clouds are drawn on (_CloudExtent), where they still have to end softly.
            float _NightOpacity;
            float _CloudExtent;

            // City glow: a small map of where the city's lights are (CityLights.cs), blurred
            // to the spread ground light has by the time it reaches the cloud base. Not
            // simulated light transport -- a tint on whatever hangs over the city at night.
            sampler2D _CityLightTex;
            float3 _CityGlow;          // colour * strength * how much it is night; 0 by day
            float _CityMapSize;        // metres the map spans, centred on the world origin

            // Fog: built like the clouds, not like a screen effect. BANKS of it are cut from the
            // same weather field with a threshold solved for the share of the ground they
            // cover, eroded into billows by the 3D noise, lie on _FogBase and stand taller
            // where they are thicker. They drift on the wind and slowly turn over, and are lit
            // through the clouds' own shadow map -- which is what makes shafts of sun stand in
            // them under a broken sky.
            float _FogAmount;          // 0..1: fades the whole thing in and out
            float _FogDensity;         // extinction per metre in the heart of a bank
            float _FogThreshold;       // weather-field threshold for the fog's coverage
            float _FogTile;            // metres one tile of the weather field spans, for fog
            float3 _FogOffset;         // how far the fog has drifted
            float _FogBoil;            // slow turnover of the billows
            float _FogBase;
            float _FogHeight;          // metres over which a full bank thins by e
            float _FogSteps;
            float _FogMaxDistance;
            float _FogPatchiness;
            float3 _FogAmbient;
            float3 _FogSun;

            sampler2D _CloudShadowTex; // CloudShadowMap: light reaching the ground, 1 - darkness .. 1
            float _ShadowAvailable;
            float3 _ShadowOrigin;
            float3 _ShadowRight;
            float3 _ShadowUp;
            float _ShadowSize;
            float _ShadowDarkness;

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

            // Light reaching p from the lightning sources. An inverse-square core, softened so
            // the source itself never blows out, times an exponential reach standing in for
            // the cloud the light has to cross. There is no march towards the source: the
            // cloud between the glow and the CAMERA already dims it, because this is added to
            // the radiance inside the front-to-back march -- which is what makes a flash read
            // as coming from within the cloud rather than painted on it.
            float3 Lightning(float3 p)
            {
                float3 sum = 0;

                for (int k = 0; k < MAX_FLASHES; k++)
                {
                    if ((float)k >= _FlashCount)
                        break;

                    float reach = _FlashPos[k].w;
                    float r = length(p - _FlashPos[k].xyz);
                    float core = reach * 0.2;
                    sum += _FlashColor[k].rgb * (exp(-r / reach) / (1.0 + (r * r) / (core * core)));
                }

                return sum;
            }

            // How much city light there is under p, 0..1.
            float CityLight(float3 p)
            {
                float2 uv = p.xz / _CityMapSize + 0.5;
                return tex2Dlod(_CityLightTex, float4(uv, 0, 0)).r;
            }

            // Direct sun reaching p through the clouds, 0..1, from the same map that shadows
            // the ground: CloudShadowMap marches one sun ray per texel, and every point on a
            // sun ray shares its texel, so this is exact for anything below the clouds. The
            // map stores 1 - darkness .. 1; undo that, or the shafts would only be as strong
            // as the ground shadows are set to be.
            float SunShaft(float3 p)
            {
                if (_ShadowAvailable < 0.5)
                    return 1.0;

                float3 rel = p - _ShadowOrigin;
                float2 uv = float2(dot(rel, _ShadowRight), dot(rel, _ShadowUp)) / _ShadowSize + 0.5;
                float lit = tex2Dlod(_CloudShadowTex, float4(uv, 0, 0)).r;
                return saturate((lit - (1.0 - _ShadowDarkness)) / max(_ShadowDarkness, 0.01));
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
                        if (_FlashCount > 0.5)
                            radiance += Lightning(p);

                        // City light comes from below: strongest on the base, gone by the top.
                        if (_CityGlow.r + _CityGlow.g + _CityGlow.b > 0.0)
                            radiance += _CityGlow * (CityLight(p) * (1.0 - h) * (1.0 - h));

                        float stepT = exp(-d * stepLen * _Absorption);
                        // Energy-conserving integration across the step.
                        light += transmittance * radiance * (1.0 - stepT);
                        transmittance *= stepT;
                    }

                    t += stepLen;
                }

                // The march stops at 2% transmittance, and 2% of an HDR star is still a star.
                // Stretch what is left so that "stopped early" means opaque.
                transmittance = saturate((transmittance - 0.02) / 0.98);

                // Dissolve into the distance rather than ending on a hard line.
                float fade = saturate(1.0 - tEnter / _MaxDistance);
                fade *= fade;

                // At night that dissolve only happens where the clouds actually run out. The
                // same factor scales the light and the coverage, as premultiplied alpha needs.
                float edge = 1.0 - smoothstep(0.7, 1.0, tEnter / max(_CloudExtent, 1.0));
                float presence = lerp(fade, max(fade, edge), _NightOpacity);

                return float4(light * presence, 1.0 - (1.0 - transmittance) * presence);
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

                        // A strike lights the rain around it as well as the cloud above it.
                        float3 lit = radiance;
                        if (_FlashCount > 0.5)
                            lit += Lightning(p) * 0.6;
                        if (_CityGlow.r + _CityGlow.g + _CityGlow.b > 0.0)
                            lit += _CityGlow * (CityLight(p) * 0.5);

                        float stepT = exp(-sigma * segment);
                        light += transmittance * lit * (1.0 - stepT);
                        transmittance *= stepT;
                    }

                    tPrev = tNext;
                }

                return float4(light, transmittance);
            }

            // Fog density at p, 0..1 before _FogDensity. The clouds' recipe, one storey down.
            float FogAt(float3 p)
            {
                // Where the banks are: the clouds' weather field, read at another scale and
                // another place, so a fog bank is not simply the footprint of a cloud.
                float2 uv = (p.xz - _FogOffset.xz) / _FogTile + float2(0.37, 0.61);
                float weather = tex2Dlod(_WeatherTex, float4(uv, 0, 0)).r;
                float bank = saturate((weather - _FogThreshold) / 0.22);

                // Patchiness 0 is the old even blanket; 1 is banks with clear air between.
                bank = lerp(1.0, bank, _FogPatchiness);
                if (bank <= 0.0)
                    return 0.0;

                // A thick bank stands taller than a thin one, so banks are domed, not slabs.
                float scale = _FogHeight * (0.45 + 0.9 * bank);
                float profile = exp(-max(p.y - _FogBase, 0.0) / scale);

                // Billows: the same erosion that shapes the clouds -- where the bank is thin
                // only the strongest noise survives, so edges come out ragged and wispy. The
                // noise drifts a little faster than the banks and slowly turns over.
                float3 q = (p - _FogOffset * 1.3) / float3(650.0, 260.0, 650.0);
                q.y += _FogBoil;
                float billow = tex3Dlod(_NoiseTex, float4(q, 0)).r;
                float shaped = saturate(Remap(billow, 1.0 - bank, 1.0, 0.0, 1.0)) * bank * 1.6;

                return lerp(1.0, shaped, _FogPatchiness) * profile;
            }

            // Fog lying on the ground. Same return convention as the other two.
            float4 MarchFog(float3 origin, float3 dir, float tStart, float tEnd, float jitter, float cosTheta)
            {
                if (tEnd <= tStart)
                    return float4(0, 0, 0, 1);

                float steps = clamp(_FogSteps, 4.0, 32.0);
                float len = tEnd - tStart;

                // Fog glows around the sun: a strong forward lobe over a flat base.
                float phase = 0.35 + 0.65 * min(HenyeyGreenstein(cosTheta, 0.65), 6.0);

                float transmittance = 1.0;
                float3 light = 0;
                float tPrev = tStart;

                [loop]
                for (int s = 0; s < 32; s++)
                {
                    if ((float)s >= steps || transmittance < 0.02)
                        break;

                    // Lengthening segments, as for the rain: at street level it is the fog
                    // within a few hundred metres that has to be right.
                    float f = ((float)s + 1.0) / steps;
                    float tNext = tStart + len * f * f;
                    float segment = tNext - tPrev;
                    float t = tPrev + segment * jitter;

                    float3 p = origin + dir * t;

                    // _FogAmount fades the banks in over its first fifth; how MUCH ground they
                    // cover is in _FogThreshold.
                    float fade = saturate(1.0 - t / _FogMaxDistance);
                    float sigma = _FogDensity * saturate(_FogAmount * 5.0) * FogAt(p) * fade;

                    if (sigma > 1e-7)
                    {
                        float3 lit = _FogAmbient + _FogSun * (SunShaft(p) * phase);
                        if (_FlashCount > 0.5)
                            lit += Lightning(p) * 0.5;
                        if (_CityGlow.r + _CityGlow.g + _CityGlow.b > 0.0)
                            lit += _CityGlow * (CityLight(p) * 0.8);

                        float stepT = exp(-sigma * segment);
                        light += transmittance * lit * (1.0 - stepT);
                        transmittance *= stepT;
                    }

                    tPrev = tNext;
                }

                return float4(light, transmittance);
            }

            // "a in front of b", both as premultiplied light + remaining transmittance.
            float4 Over(float4 a, float4 b)
            {
                return float4(a.rgb + a.a * b.rgb, a.a * b.a);
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

                // The part of the ray inside the fog layer, found the same way: below its top.
                // The thickest bank has a scale height of 1.35 * _FogHeight; four of those up,
                // 2% of it is left.
                float4 fog = float4(0, 0, 0, 1);
                float fogTop = _FogBase + _FogHeight * 5.4;
                bool cameraInFog = origin.y < fogTop;
                if (_FogAmount > 0.001 && _FogDensity > 0.0)
                {
                    float tTop = (fogTop - origin.y) / dy;
                    float fStart = cameraInFog ? 0.0 : (dir.y < 0.0 ? tTop : 1e9);
                    float fEnd = (cameraInFog && dir.y > 0.0) ? tTop : 1e9;

                    // Without depth there is no ground; stop a little under the fog's base.
                    if (dir.y < 0.0)
                        fEnd = min(fEnd, (_FogBase - 50.0 - origin.y) / dy);

                    fEnd = min(fEnd, min(sceneDist, _FogMaxDistance));
                    fog = MarchFog(origin, dir, fStart, fEnd, jitter, cosTheta);
                }

                // Clouds never overlap the other two along a ray, so they composite exactly:
                // "nearer over farther". Fog and rain DO share the air under the clouds; they
                // are marched apart (each needs its samples in a different place) and layered
                // by whichever the camera is inside, which is what dominates the view there.
                float4 lower = cameraInFog ? Over(fog, rain) : Over(rain, fog);
                float4 result = cameraBelowBase ? Over(lower, clouds) : Over(clouds, lower);

                return float4(result.rgb, 1.0 - result.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
