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

            // Night glow: a small, even, pale luminance on the underside of the clouds, which
            // is how clouds over settled land look at night -- lit faintly from below by
            // diffuse skyglow, not black. Deliberately NOT derived from where the city is: a
            // first version projected a map of the buildings onto the cloud base in sodium
            // orange, and an orange slab following the street plan looked "very weird".
            float3 _NightGlow;         // colour * strength * how much it is night; 0 by day

            // Fog: a CLOUD LAYER LYING ON THE GROUND, not a haze. Two earlier versions -- a
            // height-limited blanket, then soft kilometre-wide banks with an exponential
            // falloff -- both read as a coating, for the reasons a cloud does not: they were
            // translucent, had no boundary, and did not shade themselves. So this one is
            // dense, measured from the GROUND up (_TerrainTex) to a defined, lumpy top, shaded
            // by its own thickness towards the sun, and its billows are pushed around by a
            // drifting swirl so they shear, curl and merge instead of sliding past as one
            // rigid pattern.
            float _FogAmount;          // 0..1: fades the whole thing in and out
            float _FogDensity;         // extinction per metre inside the fog
            float _FogThreshold;       // weather-field threshold: how much of the map has fog
            float _FogTile;            // metres one tile of the weather field spans, for fog
            float3 _FogOffset;         // how far the fog has drifted
            float _FogBoil;            // phase of the swirl, 0..1
            float _FogPool;            // fog gathers on ground below this level
            float _FogHeight;          // metres from the ground to the top of the layer
            float _FogBreakup;         // 0 solid .. 1 wispy
            float _FogSteps;
            float _FogMaxDistance;
            float3 _FogAmbient;
            float3 _FogSun;

            sampler2D _TerrainTex;     // TerrainHeightMap: ground (or water) height in metres
            float _TerrainMapSize;
            float _FogFloor;           // just under the lowest ground on the map

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

                        // It comes from below: strongest on the base, gone by the top.
                        radiance += _NightGlow * ((1.0 - h) * (1.0 - h));

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

                        float stepT = exp(-sigma * segment);
                        light += transmittance * lit * (1.0 - stepT);
                        transmittance *= stepT;
                    }

                    tPrev = tNext;
                }

                return float4(light, transmittance);
            }

            // Height of the ground (or the water on it) under p.
            float GroundAt(float3 p)
            {
                return tex2Dlod(_TerrainTex, float4(p.xz / _TerrainMapSize + 0.5, 0, 0)).r;
            }

            // Fog density at p, 0..1 before _FogDensity: the clouds' recipe, lying on the ground.
            // `above` is p's height over the ground, which every caller already has.
            float FogAt(float3 p, float above, bool detailed)
            {
                float h = above / _FogHeight;
                if (h >= 1.0 || h < -0.25)
                    return 0.0;
                h = max(h, 0.0);

                // Where there is fog: the clouds' weather field, read at another scale and
                // another place so a fog patch is not a cloud's footprint. It gathers on low
                // ground: a little extra wherever the ground is below the pooling level.
                float2 uv = (p.xz - _FogOffset.xz) / _FogTile + float2(0.37, 0.61);
                float weather = tex2Dlod(_WeatherTex, float4(uv, 0, 0)).r;
                float pooling = saturate((_FogPool - (p.y - above)) / 150.0) * 0.1;
                float cover = saturate((weather + pooling - _FogThreshold) / 0.1);
                if (cover <= 0.0)
                    return 0.0;

                // Solid from the ground up, rounding off into the top: a boundary, which an
                // exponential falloff never has.
                float shaped = cover * (1.0 - smoothstep(0.4, 1.0, h));

                // FLOW. The noise lookup is displaced by a larger, slower swirl that drifts on
                // its own and turns over with _FogBoil, so billows shear, curl and merge; and
                // the upper part of the layer is carried further than the ground layer, which
                // drags. A pattern that only translated would slide past like a texture.
                float3 carried = p - _FogOffset * (1.0 + 0.35 * h);
                float3 s = (p - _FogOffset * 0.55) / 1300.0;   // slower than what it displaces
                s.y += _FogBoil;
                float2 swirl = tex3Dlod(_NoiseTex, float4(s, 0)).rg - 0.5;

                float3 q = carried / float3(360.0, 210.0, 360.0);
                q.xz += swirl * 0.75;
                q.y -= _FogBoil * 2.0;
                float base = tex3Dlod(_NoiseTex, float4(q, 0)).r;

                // The clouds' own shaping: where the cover is thin only the strongest noise
                // survives, so the layer breaks into lumps with a billowing top.
                float d = saturate(Remap(base, 1.0 - shaped, 1.0, 0.0, 1.0)) * shaped;

                // Wisps: fine noise eats into it, moving faster than the billows and rising.
                if (detailed && d > 0.0 && _FogBreakup > 0.0)
                {
                    float3 w = (p - _FogOffset * 1.8) / 85.0;
                    w.y -= _FogBoil * 9.0;
                    float wisp = tex3Dlod(_NoiseTex, float4(w, 0)).g;
                    d = saturate(Remap(d, wisp * _FogBreakup, 1.0, 0.0, 1.0));
                }

                return d;
            }

            // Fog lying on the ground, along the ray up to tEnd. Same return convention as the
            // other two.
            //
            // The layer follows the terrain, so the part of the ray inside it cannot be found
            // by intersecting a slab. Instead the ray is walked coarsely -- one height lookup a
            // step -- until it first comes within the layer's height of the ground, and the
            // real march starts from the step before. From above that puts every sample in the
            // last stretch before the ground; from inside the fog it starts at the camera.
            float4 MarchFog(float3 origin, float3 dir, float tEnd, float jitter, float cosTheta)
            {
                if (tEnd <= 0.0)
                    return float4(0, 0, 0, 1);

                float tStart = -1.0;
                if (origin.y - GroundAt(origin) < _FogHeight)
                {
                    tStart = 0.0;
                }
                else
                {
                    float tBefore = 0.0;

                    [loop]
                    for (int c = 1; c <= 14; c++)
                    {
                        // Finer near the camera, where a missed patch would be seen.
                        float f = (float)c / 14.0;
                        float t = tEnd * f * f;
                        float3 p = origin + dir * t;

                        if (p.y - GroundAt(p) < _FogHeight)
                        {
                            tStart = tBefore;
                            break;
                        }

                        tBefore = t;
                    }
                }

                if (tStart < 0.0)
                    return float4(0, 0, 0, 1);

                // A ray climbing out of the fog leaves it for good soon after.
                if (dir.y > 0.02)
                    tEnd = min(tEnd, tStart + (_FogHeight * 1.5 + 60.0) / dir.y);

                float steps = clamp(_FogSteps, 6.0, 40.0);
                float len = tEnd - tStart;
                bool fromInside = tStart <= 0.0;

                // Fog glows around the sun: a strong forward lobe over a flat base.
                float phase = 0.35 + 0.65 * min(HenyeyGreenstein(cosTheta, 0.65), 6.0);
                float strength = _FogDensity * saturate(_FogAmount * 5.0);

                float transmittance = 1.0;
                float3 light = 0;
                float tPrev = tStart;

                [loop]
                for (int s = 0; s < 40; s++)
                {
                    if ((float)s >= steps || transmittance < 0.02)
                        break;

                    // From inside, segments lengthen with distance: it is the fog within a few
                    // hundred metres that has to be right. From outside the stretch is short
                    // and every part of it matters equally.
                    float f = ((float)s + 1.0) / steps;
                    float tNext = tStart + len * (fromInside ? f * f : f);
                    float segment = tNext - tPrev;
                    float t = tPrev + segment * jitter;

                    float3 p = origin + dir * t;
                    float above = p.y - GroundAt(p);
                    float d = FogAt(p, above, true);

                    if (d > 0.001)
                    {
                        float fade = saturate(1.0 - t / _FogMaxDistance);
                        float sigma = strength * d * fade;

                        // Self-shadowing: how much fog lies between here and the sun, from one
                        // look a short way towards it. This is what gives the billows form --
                        // lit tops and flanks, dim hollows -- instead of an even glow.
                        float3 sunward = p + _SunDir * (_FogHeight * 0.4);
                        float shade = FogAt(sunward, sunward.y - GroundAt(sunward), false);
                        float sun = exp(-shade * strength * _FogHeight * 0.9);

                        float h = saturate(above / _FogHeight);
                        float3 lit = _FogAmbient * (0.55 + 0.45 * h)
                                   + _FogSun * (SunShaft(p) * sun * phase);

                        if (_FlashCount > 0.5)
                            lit += Lightning(p) * 0.5;

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

                // The fog follows the terrain, so MarchFog finds its own stretch of the ray; all
                // it needs is where the ray ends.
                float4 fog = float4(0, 0, 0, 1);
                bool cameraInFog = false;
                if (_FogAmount > 0.001 && _FogDensity > 0.0)
                {
                    cameraInFog = origin.y - GroundAt(origin) < _FogHeight;

                    // A ray that starts above the fog and never descends will not meet it
                    // (bar a fogged mountainside above the camera, which is not worth fourteen
                    // lookups on every pixel of sky).
                    if (cameraInFog || dir.y < 0.0)
                    {
                        float fEnd = min(sceneDist, _FogMaxDistance);

                        // With depth occlusion off there is no ground to stop at; use the
                        // lowest ground on the map.
                        if (dir.y < 0.0)
                            fEnd = min(fEnd, (_FogFloor - origin.y) / dy);

                        fog = MarchFog(origin, dir, fEnd, jitter, cosTheta);
                    }
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
