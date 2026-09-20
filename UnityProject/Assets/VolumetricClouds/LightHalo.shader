// Replacement for the game's 'Custom/Lights/GroupVolume' shader: the pass that draws the glow
// around street and building lights. Those are baked into per-area meshes and drawn by this
// pass at EVERY distance, right up to the camera -- they never go through the game's dynamic
// light path -- so this one shader owns a lamp's halo from 5 m to 30 km.
//
// This is a hand port of the game's compiled shader, made from its Direct3D 11 disassembly
// (tools/shaderdump.ps1), deliberately kept close to the original instruction order so it can
// be checked against that listing. With _HaloBrightness = _HaloTightness = _HaloRadius = 1 and
// _HaloFog set to the game's fog value it computes the same thing as the original -- so the
// two can be A/B compared in-game -- except for one line: the glow is clamped positive before
// the log(). The original takes log() of a non-positive number once fog <= -0.5, gets NaN, and
// Direct3D's min(NaN, 1) returns 1, painting the light's whole quad at full colour.
//
// What the original gives no control over, and this does:
//   _HaloBrightness  scales the glow amplitude directly, instead of only via fog
//   _HaloTightness   scales the falloff exponent: > 1 keeps the bright core, loses the wide haze
//   _HaloRadius      scales the glow's world radius (the light's baked range / 2)
//   _HaloNear*       multipliers on the three above for lights close to the camera. A halo has
//                    a fixed WORLD size, so settings that make a distant lamp a crisp dot make
//                    a lamp 100 m away a blob. Blended per light, in the vertex shader, from
//                    full effect inside _HaloNearDistance to none at twice that, so nothing
//                    pops as the camera moves. All 1 = no effect.
//
// Batched light mesh vertex layout (written by LightEffect.PopulateGroupData):
//   POSITION   a corner of the light's bounding quad, in group space
//   NORMAL     the light's WORLD position
//   TANGENT    xyz = light direction, w = spot parameter
//   TEXCOORD0  x = 1 / range^2, y = intensity
//   TEXCOORD1  x = switch-on threshold (also the blink phase), y = blink pattern index
//   TEXCOORD2  location in _ObjectColorMap (per-building state; alpha 0 = lights off)
//   COLOR      light colour, a = omnidirectional weight
//
// Render state read out of the original's serialized pass: Blend One One, ZWrite Off,
// ZTest Less, Cull Off, queue Transparent-10.
Shader "VolumetricClouds/LightHalo"
{
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "IgnoreProjector" = "True" }

        Pass
        {
            Blend One One
            ZWrite Off
            ZTest Less
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"

            // Published by the game every frame.
            float _GiantMarshmallow;      // daylight amount; lights switch on below their threshold
            float4 _SimulationTime;
            float4 _CameraRight;
            float4 _CameraUp;
            float4 _CameraForward;
            float4 _WindDirection;
            float4 _WeatherParams;        // x temperature, y rain, z fog, w wetness
            float4 _LightRainParams;
            sampler2D _LightRainTexture;
            sampler2D _ObjectColorMap;
            sampler2D_float _CameraDepthTexture;

            // Ours.
            float _HaloFog;
            float _HaloBrightness;
            float _HaloTightness;
            float _HaloRadius;
            float _HaloNearDistance;
            float _HaloNearBrightness;
            float _HaloNearTightness;
            float _HaloNearRadius;

            // { rampStart, rampEnd, fallEnd, period } per blink pattern.
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
                float4 axis : TANGENT;
                float2 rangeIntensity : TEXCOORD0;
                float2 switching : TEXCOORD1;
                float2 colorUV : TEXCOORD2;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 viewRay : TEXCOORD1;
                float4 light : TEXCOORD2;       // xyz world position, w = 1 / radius^2
                float4 axis : TEXCOORD3;
                float4 color : COLOR0;
                float2 colorUV : TEXCOORD4;
                float2 tuning : TEXCOORD5;      // x brightness, y tightness, for THIS light
            };

            // x*x*(3-2x): the polynomial half of smoothstep, for an already-saturated x.
            float Smooth01(float x) { return x * x * (3.0 - 2.0 * x); }
            float3 Smooth01(float3 x) { return x * x * (3.0 - 2.0 * x); }

            v2f vert(appdata v)
            {
                v2f o;

                // Blink pattern and day/night switching -> brightness.
                int pattern = clamp((int)v.switching.y, 0, 6);
                float4 b = kBlink[pattern];
                float t = frac(v.switching.x * 3.71 + _SimulationTime.x / b.w) * b.w;
                float rise = saturate((t - b.x) / (b.y - b.x));
                float fall = saturate((t - b.w) / (b.z - b.w));
                float blink = 1.0 - Smooth01(rise) * Smooth01(fall);

                float on = saturate((v.switching.x + 0.01 - _GiantMarshmallow) * 50.0);
                float bright = blink * Smooth01(on) * v.rangeIntensity.y;

                // Lights that are off collapse to a point so they cost no fill.
                float visible = bright >= 0.001 ? 1.0 : 0.0;
                float3 local = v.vertex.xyz * visible;
                float3 world = mul(unity_ObjectToWorld, float4(local, v.vertex.w)).xyz;

                float3 toCamera = _WorldSpaceCameraPos - v.lightPos;
                float dist2 = dot(toCamera, toCamera);
                float dist = sqrt(dist2);

                // Not in the original: how "near" this light is, 1 inside _HaloNearDistance
                // falling to 0 at twice that, and the three controls blended accordingly.
                float nearRange = max(_HaloNearDistance, 1.0);
                float nearness = 1.0 - smoothstep(nearRange, 2.0 * nearRange, dist);
                float radiusScale = _HaloRadius * lerp(1.0, _HaloNearRadius, nearness);
                o.tuning = float2(_HaloBrightness * lerp(1.0, _HaloNearBrightness, nearness),
                                  _HaloTightness * lerp(1.0, _HaloNearTightness, nearness));

                // The corner's offset from the light encodes the quad: x,y pick the corner,
                // z is the light's range. The group matrix is a pure translation.
                float3 offset = (world - v.lightPos) * radiusScale;
                float3 groupTranslation = world - local;
                float cornerX = offset.x * 0.5;
                float cornerY = offset.y * 0.5;
                float radius = offset.z * 0.5;

                float tangentLength = sqrt(max(dist2 - radius * radius, 0.001));
                float3 towards = toCamera * rsqrt(dist2);
                float alongView = dot(toCamera, _CameraForward.xyz);

                // Grow the quad so it still covers the sphere's silhouette up close.
                float grow = dist / tangentLength;
                float inside = offset.z >= dist ? 1.0 : 0.0;
                float2 corner = float2(cornerX, cornerY) * grow;

                float3 up = cross(towards, _CameraRight.xyz);
                up *= rsqrt(dot(up, up));
                float3 right = cross(towards, up);

                float3 facing = v.lightPos + right * corner.x + up * corner.y;
                float3 toCamera2 = _WorldSpaceCameraPos - facing;
                toCamera2 *= rsqrt(dot(toCamera2, toCamera2));
                float cosine = max(dot(toCamera2, towards), 0.001);
                facing += toCamera2 * (radius / cosine);           // push to the sphere's front

                // Camera inside the light's range: cover the screen from the near plane instead.
                float3 nearDirection = _CameraForward.xyz + _CameraRight.xyz * cornerX + _CameraUp.xyz * cornerY;
                float nearPlane = _ProjectionParams.y * 1.001;
                float useNear = ((nearPlane + radius) >= alongView ? 1.0 : 0.0) * inside;
                float3 nearPosition = _WorldSpaceCameraPos + nearDirection * nearPlane;

                float3 finalWorld = useNear != 0.0 ? nearPosition : facing;
                float3 finalLocal = finalWorld - groupTranslation;

                o.pos = mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, float4(finalLocal, 1.0)));
                o.screenPos = ComputeScreenPos(o.pos);
                o.viewRay = mul(UNITY_MATRIX_V, mul(unity_ObjectToWorld, float4(finalLocal, v.vertex.w))).xyz
                          * float3(-1.0, -1.0, 1.0);

                float invRange2 = v.rangeIntensity.x / (radiusScale * radiusScale);
                o.light = float4(v.lightPos, invRange2 * 4.0);
                o.axis = v.axis;
                o.color = float4(bright, bright, bright, 1.0) * v.color;
                o.colorUV = v.colorUV;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 camera = _WorldSpaceCameraPos;
                float3 lightPos = i.light.xyz;

                // Two copies of the rain-streak texture, half a cycle apart, cross-faded.
                float time = _SimulationTime.x;
                float phaseA = 0.5 - frac(time);
                float phaseB = 0.5 - frac(time + 0.5);
                float fade = 2.0 * abs(frac(time) - 0.5);

                float windScale = 2.0 * _WindDirection.w;
                float3 rainVelocity = float3(_WindDirection.x * windScale, _LightRainParams.z, _WindDirection.z * windScale);
                float3 rainB = rainVelocity * phaseB + lightPos;
                float3 rainA = rainVelocity * phaseA + lightPos;

                // World position of whatever is behind this pixel.
                float rawDepth = tex2D(_CameraDepthTexture, i.screenPos.xy / i.screenPos.w).x;
                float linearDepth = 1.0 / (_ZBufferParams.x * rawDepth + _ZBufferParams.y);
                float3 viewPosition = i.viewRay * (_ProjectionParams.z / i.viewRay.z) * linearDepth;
                float3 scene = mul(unity_CameraToWorld, float4(viewPosition, 1.0)).xyz;

                float3 ray = scene - camera;
                float3 backwards = camera - scene;
                float rayLength2 = dot(ray, ray);

                float3 nearestB = ray * (dot(rainB - camera, ray) / rayLength2) + camera - rainB;
                float2 uvB = float2(dot(nearestB, _CameraRight.xyz), dot(nearestB, _CameraUp.xyz)) * _LightRainParams.xy + 0.4;
                float streakB = tex2D(_LightRainTexture, uvB).x;

                float3 nearestA = ray * (dot(rainA - camera, ray) / rayLength2) + camera - rainA;
                float2 uvA = float2(dot(nearestA, _CameraRight.xyz), dot(nearestA, _CameraUp.xyz)) * _LightRainParams.xy;
                float streakA = tex2D(_LightRainTexture, uvA).x;

                float streaks = lerp(streakA, streakB, fade);
                float scatter = 2.0 * streaks * _WeatherParams.y + _HaloFog;

                float3 toCamera = camera - lightPos;
                float epsilon = 0.5 - 0.5 / 1.001;

                // THE FIX, part one. The original is "scatter * 0.001 + epsilon" with no floor,
                // which goes negative once fog < -0.5.
                float amplitude = max(scatter * 0.001 + epsilon, 0.0) * i.tuning.x;
                float exponent = (1.0 - 0.2 * scatter) * i.tuning.y;

                // Closest approach of the view ray to the spotlight's axis plane...
                float3 a = i.axis.zxy * 1000.0;
                float3 c = ray.zxy * a.yzx - a * ray;
                a = a.zxy * c.yzx - a * c;

                float denominator = dot(a, ray);
                float direction = (denominator > 0.0 ? 1.0 : 0.0) - (denominator < 0.0 ? 1.0 : 0.0);
                float tSpot = saturate((-1.0 / (direction * 0.00001 + denominator)) * dot(a, toCamera));
                float3 fromLightSpot = ray * tSpot + camera - lightPos;
                float spotDistance2 = dot(fromLightSpot, fromLightSpot);

                // ...and to the light itself, never past the scene surface.
                float tLight = saturate(dot(lightPos - camera, ray) / rayLength2);
                float3 fromLight = ray * tLight + camera - lightPos;
                float lightDistance2 = dot(fromLight, fromLight);

                float xLight = lightDistance2 * i.light.w;
                float xSpot = spotDistance2 * i.light.w;
                float cosSpot = dot(fromLightSpot * rsqrt(spotDistance2), i.axis.xyz);
                float cosLight = dot(fromLight * rsqrt(lightDistance2), i.axis.xyz);

                // Inverse-square glow, zero at the edge of the radius.
                float glowLight = amplitude / min(max(epsilon, xLight), 1.0) - amplitude;
                float glowSpot = amplitude / min(max(epsilon, xSpot), 1.0) - amplitude;

                // Spotlight cone masks.
                float cosScene = dot(backwards * rsqrt(dot(backwards, backwards)), i.axis.xyz);
                float spot = i.axis.w;
                float spotSign = (spot > 0.0 ? 1.0 : 0.0) - (spot < 0.0 ? 1.0 : 0.0);
                float edge = spot * (-spotSign - 0.5) - 0.5;
                float sceneMask = Smooth01(saturate((cosScene - edge) / (spot - edge)));

                float3 masks = Smooth01(saturate(float3(cosSpot + spot, cosLight + spot, cosLight + 1.0)
                                               / float3(1.0 + spot, 1.0 + spot, 2.0)));

                float spotMask = sceneMask * masks.x;
                float lightMask = masks.z * i.color.a + sceneMask * masks.y;

                float glow = max(glowSpot * spotMask, lightMask * glowLight);

                // THE FIX, part two: never hand log() a non-positive number.
                glow = min(pow(max(glow, 1e-12), exponent), 1.0);

                float buildingState = tex2D(_ObjectColorMap, i.colorUV).a;
                return float4(glow * i.color.rgb * buildingState, 0.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
