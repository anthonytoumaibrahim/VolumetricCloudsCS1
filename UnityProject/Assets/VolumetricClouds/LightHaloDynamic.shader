// Replacement for the INSTANCED variant of the game's 'Custom/Lights/GroupVolume' (keyword
// MULTI_INSTANCE): the glow around DYNAMIC lights, drawn by LightSystem.DrawLight /
// EndRendering as one mesh of up to 32 quads with per-light arrays in a property block.
//
// Why it exists: LightHalo.shader replaces the BATCHED variant, which covers every street and
// building lamp -- but not lamps that are drawn without an instance id. Intersection Marking
// Tool renders its props with a default (empty) InstanceID, and LightEffect.RenderEffect only
// takes its early return for "m_batchedLight && !id.IsEmpty": so an IMT lamp's light goes
// down this dynamic path, with the game's own shader, and ignored every halo setting. Vehicles
// and non-batched effect lights come through here too.
//
// A hand port of VS [1] + PS [2] of the game's compiled shader (tools/shaderdump.ps1), kept
// close to the original's order like LightHalo.shader, and with the same changes: the glow is
// clamped positive before the log() (the NaN "box"), plus brightness / tightness / radius and
// the near-camera multipliers. Differences from the batched variant, all from the listing:
//   - the light comes from arrays indexed by POSITION.z, not from vertex attributes
//   - no blink / switch-on logic: LightEffect.RenderEffect does that on the CPU
//   - the rain streaks are offset by the light's own velocity, and the rain term is
//     min(5 * streaks * rain, 2) where the batched one has 2 * streaks * rain
//   - no _ObjectColorMap (per-building state) lookup
//
// LAMP TAG. IMT's lamps must look exactly like the street lamps next to them, while vehicle
// headlights must not suddenly take the lamps' settings. Nothing the shader receives tells the
// two apart, so HaloAdjuster tags lamp-type lights on the CPU as they are submitted: velocity
// = (0, LAMP_TAG, 0). DrawLight only stores the velocity into _LightVel (checked in the IL),
// and no vehicle falls upwards at 40 km/s. Tagged lights get the lamp controls; the rest get
// _HaloOtherBrightness in place of _HaloBrightness.
Shader "VolumetricClouds/LightHaloDynamic"
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
            #pragma target 3.5   // not 4.0: no Metal code at 4.0 in Unity 5.6 (see CloudRaymarch.shader)
            #include "UnityCG.cginc"

            #define MAX_LIGHTS 32
            #define LAMP_TAG 40000.0

            // Set per draw by the game, through its MaterialPropertyBlock.
            float4 _LightPos[MAX_LIGHTS];      // xyz world position, w = range (negative: no halo)
            float4 _LightDir[MAX_LIGHTS];      // xyz direction, w = spot parameter
            float4 _LightVel[MAX_LIGHTS];      // xyz velocity -- or the lamp tag
            float4 _LightColor[MAX_LIGHTS];    // rgb colour * intensity, a = omnidirectional weight

            // Published by the game every frame.
            float4 _SimulationTime;
            float4 _CameraRight;
            float4 _CameraUp;
            float4 _CameraForward;
            float4 _WindDirection;
            float4 _WeatherParams;        // x temperature, y rain, z fog, w wetness
            float4 _LightRainParams;
            sampler2D _LightRainTexture;
            sampler2D_float _CameraDepthTexture;

            // Ours: the same as LightHalo.shader, plus the brightness for untagged lights.
            float _HaloFog;
            float _HaloBrightness;
            float _HaloOtherBrightness;
            float _HaloTightness;
            float _HaloRadius;
            float _HaloNearDistance;
            float _HaloNearBrightness;
            float _HaloNearTightness;
            float _HaloNearRadius;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 viewRay : TEXCOORD1;
                float4 light : TEXCOORD2;       // xyz world position, w = 1 / radius^2
                float4 axis : TEXCOORD3;
                float4 color : COLOR0;
                float3 velocity : TEXCOORD4;
                float2 tuning : TEXCOORD5;      // x brightness, y tightness, for THIS light
            };

            // x*x*(3-2x): the polynomial half of smoothstep, for an already-saturated x.
            float Smooth01(float x) { return x * x * (3.0 - 2.0 * x); }
            float3 Smooth01(float3 x) { return x * x * (3.0 - 2.0 * x); }

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;

                int index = (int)vertex.z;
                float3 lightPos = _LightPos[index].xyz;
                float range = _LightPos[index].w;

                // The lamp tag rides in the velocity; take it out before anything uses it.
                float3 velocity = _LightVel[index].xyz;
                bool lamp = velocity.y > LAMP_TAG * 0.5;
                if (lamp)
                    velocity = 0;

                float3 toCamera = _WorldSpaceCameraPos - lightPos;
                float dist2 = dot(toCamera, toCamera);
                float dist = sqrt(dist2);
                float3 towards = toCamera * rsqrt(dist2);
                float alongView = dot(toCamera, _CameraForward.xyz);

                // Not in the original: how "near" this light is, and the controls blended
                // accordingly -- identical to LightHalo.shader, so a tagged lamp matches the
                // batched lamps around it at every distance.
                float nearRange = max(_HaloNearDistance, 1.0);
                float nearness = 1.0 - smoothstep(nearRange, 2.0 * nearRange, dist);
                float radiusScale = _HaloRadius * lerp(1.0, _HaloNearRadius, nearness);
                float brightness = lamp ? _HaloBrightness : _HaloOtherBrightness;
                o.tuning = float2(brightness * lerp(1.0, _HaloNearBrightness, nearness),
                                  _HaloTightness * lerp(1.0, _HaloNearTightness, nearness));

                // A negative range means "no halo for this light": the quad collapses.
                float signedRadius = 0.5 * range * radiusScale;
                float radius = abs(signedRadius);
                float2 cornerFlat = max(signedRadius, 0.0) * vertex.xy;

                float3 up = cross(towards, _CameraRight.xyz);
                up *= rsqrt(dot(up, up));
                float3 right = cross(towards, up);

                // Grow the quad so it still covers the sphere's silhouette up close.
                float tangentLength = sqrt(max(dist2 - radius * radius, 0.001));
                float2 corner = cornerFlat * (dist / tangentLength);

                float3 facing = lightPos + right * corner.x + up * corner.y;
                float3 toCamera2 = _WorldSpaceCameraPos - facing;
                toCamera2 *= rsqrt(dot(toCamera2, toCamera2));
                float cosine = max(dot(toCamera2, towards), 0.001);
                facing += toCamera2 * (radius / cosine);           // push to the sphere's front

                // Camera inside the light's range: cover the screen from the near plane instead.
                float nearPlane = _ProjectionParams.y * 1.001;
                float inside = (radius + radius) >= dist ? 1.0 : 0.0;
                float useNear = ((nearPlane + radius) >= alongView ? 1.0 : 0.0) * inside;
                float3 nearDirection = _CameraForward.xyz + _CameraRight.xyz * cornerFlat.x + _CameraUp.xyz * cornerFlat.y;
                float3 nearPosition = _WorldSpaceCameraPos + nearDirection * nearPlane;

                float3 finalWorld = useNear != 0.0 ? nearPosition : facing;

                o.pos = mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, float4(finalWorld, 1.0)));
                o.screenPos = ComputeScreenPos(o.pos);
                o.viewRay = mul(UNITY_MATRIX_V, mul(unity_ObjectToWorld, float4(finalWorld, vertex.w))).xyz
                          * float3(-1.0, -1.0, 1.0);

                float scaledRange = range * radiusScale;
                o.light = float4(lightPos, 4.0 / (scaledRange * scaledRange));
                o.axis = _LightDir[index];
                o.color = _LightColor[index];
                o.velocity = velocity;
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

                // The streaks fall with the wind AND trail behind a moving light.
                float windScale = 2.0 * _WindDirection.w;
                float3 rainVelocity = float3(_WindDirection.x * windScale + i.velocity.x,
                                             _LightRainParams.z + i.velocity.y,
                                             _WindDirection.z * windScale + i.velocity.z);
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
                float scatter = min(5.0 * streaks * _WeatherParams.y, 2.0) + _HaloFog;

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

                return float4(glow * i.color.rgb, 0.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
