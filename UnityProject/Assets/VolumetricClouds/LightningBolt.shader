// A lightning channel: a jagged, branching polyline generated per strike (CloudLightning.cs)
// and drawn as camera-facing ribbons, from inside OUR cloud layer down to where the game says
// the strike lands. The game's own bolt is a fixed model whose height knows nothing about
// where our cloud base is.
//
// Drawn AFTER the cloud pass (Transparent+110), and dimmed by what lies IN FRONT of it: the
// clouds, the rain curtains and the fog between the camera and each fragment, marched here
// through the very density functions the cloud pass uses (CloudCommon, CloudMedia) with the
// cloud pass's own values (CloudVolume.ShareMediaWith). So the top of the channel still fades
// into the cloud it comes out of, and a bolt behind a cloud is still hidden by it.
//
// Until 1.2.1 it was drawn BEFORE the cloud pass for that fade -- but the cloud pass blends ALL
// the cloud along a ray over what is behind it, the cloud BEHIND the bolt as well. Seen from
// under the base, the rays through a bolt's upper part run on into the clouds behind it, and
// that cloud covered the channel: "it looks as if it's cut off or spawns behind the clouds".
// A flash is light, and only what is in front of light can dim it.
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
        Tags { "Queue" = "Transparent+110" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 3.5, like every shader here: never 4.0, which builds NO Metal code (an empty blob,
            // no error; tools/bundle-apis.ps1). The march needs more than 3.0's instructions.
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "CloudCommon.cginc"
            #include "CloudMedia.cginc"

            // The ribbon is this many core-widths wide: a thin white-hot core inside a soft glow.
            #define GLOW_SCALE 6.0

            // The marches to the camera. Only the channel's few pixels run them, for the second
            // or so a strike lasts.
            #define CLOUD_STEPS 32
            #define RAIN_STEPS 12
            #define FOG_STEPS 12

            float4 _BoltColor;       // rgb, HDR
            float _BoltIntensity;    // this frame's flicker, 0..1
            float _PixelAngle;       // radians one pixel subtends
            float _BoltSeesMedia;    // 1 once the cloud pass's values are on this material; 0: seen through nothing

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
                float3 world : TEXCOORD1;      // where on the ribbon, in the world
                float4 screenPos : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;

                float3 world = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float3 toCamera = _WorldSpaceCameraPos - world;
                float dist = length(toCamera);

                // Lightning is seen from kilometres away: never let the core go sub-pixel.
                float width = max(v.shape.y, dist * _PixelAngle * 1.5);

                float3 side = cross(v.tangent, toCamera / max(dist, 1e-3));
                side /= max(length(side), 1e-3);

                world += side * (width * 0.5 * GLOW_SCALE) * v.shape.x;

                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                o.across = float2(v.shape.x, v.branch.x);
                o.world = world;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            // What is left of the light from `target` by the time it reaches the camera: the
            // clouds, the rain curtains and the fog between the two, and nothing behind.
            float FrontTransmittance(float3 target, float jitter)
            {
                float3 origin = _WorldSpaceCameraPos;
                float3 toTarget = target - origin;
                float dist = length(toTarget);
                if (dist < 1.0)
                    return 1.0;

                float3 dir = toTarget / dist;
                float dy = abs(dir.y) < 1e-4 ? (dir.y < 0 ? -1e-4 : 1e-4) : dir.y;
                float tA = (_CloudBottom - origin.y) / dy;
                float tB = (_CloudTop - origin.y) / dy;

                // The clouds: the stretch of their slab in front of the bolt, finished as the
                // cloud pass finishes its march (stopped at 2% means opaque, and the clouds fade
                // into the distance).
                float clouds = 1.0;
                float tEnter = max(min(tA, tB), 0.0);
                float tExit = min(min(max(tA, tB), _MaxDistance), dist);
                if (tExit > tEnter)
                {
                    float stepLen = (tExit - tEnter) / CLOUD_STEPS;
                    float t = tEnter + stepLen * jitter;
                    float tau = 0.0;

                    [loop]
                    for (int s = 0; s < CLOUD_STEPS; s++)
                    {
                        tau += SampleDensityLod(origin + dir * t, true, CloudLod(t));
                        t += stepLen;
                    }

                    clouds = saturate((exp(-tau * stepLen * _Absorption) - 0.02) / 0.98);
                    clouds = 1.0 - (1.0 - clouds) * CloudPresence(tEnter, SunCover(dot(dir, _SunDir)));
                }

                // The rain curtains: the part of the ray under the cloud base.
                float rain = 1.0;
                if (_RainAmount > 0.001 && _RainShaftDensity > 0.0)
                {
                    bool below = origin.y < _CloudBottom;
                    float rStart = below ? 0.0 : (dir.y < 0.0 ? tA : dist);
                    float rEnd = (below && dir.y > 0.0) ? min(tA, dist) : dist;
                    rEnd = min(rEnd, _RainMaxDistance);

                    if (rEnd > rStart)
                    {
                        float stepLen = (rEnd - rStart) / RAIN_STEPS;
                        float t = rStart + stepLen * jitter;
                        float tau = 0.0;

                        [loop]
                        for (int s = 0; s < RAIN_STEPS; s++)
                        {
                            tau += RainSigma(origin + dir * t, t);
                            t += stepLen;
                        }

                        rain = exp(-tau * stepLen);
                    }
                }

                // The fog on the ground. A level fog is a slab, as in the cloud pass; one that
                // follows the ground is walked from the camera (FogAt is 0 outside the layer).
                float fog = 1.0;
                if (_FogAmount > 0.001 && _FogDensity > 0.0)
                {
                    float fStart = 0.0;
                    float fEnd = min(dist, _FogMaxDistance);
                    if (_FogFollowGround < 0.5)
                    {
                        float fA = (_FogLevel + _FogBase - origin.y) / dy;
                        float fB = (_FogLevel + _FogBase + _FogHeight - origin.y) / dy;
                        fStart = max(min(fA, fB), 0.0);
                        fEnd = min(fEnd, max(fA, fB));
                    }

                    if (fEnd > fStart)
                    {
                        float stepLen = (fEnd - fStart) / FOG_STEPS;
                        float t = fStart + stepLen * jitter;
                        float tau = 0.0;

                        [loop]
                        for (int s = 0; s < FOG_STEPS; s++)
                        {
                            float3 p = origin + dir * t;
                            tau += FogAt(p, p.y - FogGround(p), false) * saturate(1.0 - t / _FogMaxDistance);
                            t += stepLen;
                        }

                        fog = exp(-tau * FogStrength() * stepLen);
                    }
                }

                return clouds * rain * fog;
            }

            float4 frag(v2f i) : SV_Target
            {
                float x = abs(i.across.x);
                float core = exp(-pow(x * GLOW_SCALE, 2.0) * 1.2);
                float glow = exp(-x * 3.5) * 0.16;

                float3 colour = _BoltColor.rgb * ((core + glow) * i.across.y * _BoltIntensity);

                if (_BoltSeesMedia > 0.5)
                {
                    float2 pixel = i.screenPos.xy / i.screenPos.w * _ScreenParams.xy;
                    colour *= FrontTransmittance(i.world, MarchJitter(pixel));
                }

                return float4(colour, 0.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
