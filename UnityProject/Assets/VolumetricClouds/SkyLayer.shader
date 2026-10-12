// THE SKY LAYER (Sky/SkyLayer.cs, 1.5.0): photographed skies behind everything else in the sky --
// cirrus, sheep clouds, far cumulus, a sky of several layers at once -- drawn the way Cities: Skylines II
// draws its distant clouds (Unity HDRP's Cloud Layer): a picture of the whole sky per kind, how much
// cloud there is in each direction, lit here by the clouds' own sun and sky light. The pictures are cut
// out of CC0 photographs (Poly Haven) by tools/make-skylayer.ps1.
//
// Drawn on the cloud box (a child of it, so every camera that draws the clouds draws this), on sky
// pixels only, after the game's sky and stars and before the lightning bolt, the clouds, the rain and
// the fog, which all lay themselves over it. Its own draw, not part of CloudRaymarch: that pass has
// no sampler left (15 of 16).
Shader "VolumetricClouds/SkyLayer"
{
    Properties
    {
        _SkyPicturesA ("Pictures (four, one per channel)", 2D) = "black" {}
        _SkyPicturesB ("Pictures (four, one per channel)", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha   // premultiplied, as the clouds

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5   // not 4.0: no Metal code at 4.0 in Unity 5.6 (see CloudRaymarch.shader)
            #include "UnityCG.cginc"

            #define SKY_PI 3.14159265

            sampler2D_float _CameraDepthTexture;

            // Eight pictures, four to a texture, one per channel; linear. u goes round the horizon, v from
            // the horizon (0) to straight up (1). Mipmapped, read with the view's own derivatives.
            sampler2D _SkyPicturesA;
            sampler2D _SkyPicturesB;

            // How much of each picture each kind shows (Sky/SkyLayerMix: a kind's amount walks through its
            // pictures, thinnest first): kind k reads [2k] against A's four and [2k + 1] against B's.
            // Kinds: 0 cirrus, 1 sheep clouds, 2 far clouds, 3 cloud layers.
            float4 _SkyWeights[8];

            // Per kind: x = how high it stands (m: the Earth's shadow at sunset leaves the highest lit
            // longest), y = how much its own cloud shades it from the sun, z = its forward scattering (the
            // glow round the sun), w = its greatest opacity (a photographed overcast hid the whole sky).
            float4 _SkyKinds[4];

            float4 _SkyTurn;    // xy = cos, sin of this city's turn of the pictures round the vertical
            float4 _SkyFlow;    // x = the flow's phase 0..1, yz = the wind (x, z) times how far the flow
                                // carries the pictures (radians), w unused

            // The clouds' own light, copied from the cloud material every frame (CloudVolume.ApplyLighting):
            // their sun (or moon) with its brightness and tints, their sky light, Sunset light's edge.
            float3 _SunDir;     // towards the light
            float3 _SunColor;
            float3 _AmbientColor;
            float4 _SunsetEdge;

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

            // Sunset light's Earth shadow at height y (CloudRaymarch.shader's, the same edge).
            float EarthShadow(float y)
            {
                float e = saturate(_SunsetEdge.x + _SunsetEdge.y * sqrt(max(y - _SunsetEdge.z, 0.0)));
                return 1.0 - _SunsetEdge.w * (1.0 - e * e * (3.0 - 2.0 * e));
            }

            // Where a direction is in the pictures, turned by the city's turn. v stops half a texel short
            // of straight up: the textures repeat (for u), and v = 1 would read the horizon's row.
            float2 PictureUV(float3 d)
            {
                float x = d.x * _SkyTurn.x - d.z * _SkyTurn.y;
                float z = d.x * _SkyTurn.y + d.z * _SkyTurn.x;
                float u = atan2(z, x) * (0.5 / SKY_PI);
                float v = min(asin(saturate(d.y)) * (2.0 / SKY_PI), 1.0 - 1.0 / 1024.0);
                return float2(u, v);
            }

            float4 Kinds(float4 a, float4 b)
            {
                return float4(dot(a, _SkyWeights[0]) + dot(b, _SkyWeights[1]),
                              dot(a, _SkyWeights[2]) + dot(b, _SkyWeights[3]),
                              dot(a, _SkyWeights[4]) + dot(b, _SkyWeights[5]),
                              dot(a, _SkyWeights[6]) + dot(b, _SkyWeights[7]));
            }

            // Each kind's cloud in direction d, read with the view's derivatives (sharp to the horizon,
            // mips and anisotropic filtering where the pictures are squeezed).
            float4 KindsGrad(float3 d, float2 gx, float2 gy)
            {
                float2 uv = PictureUV(d);
                return Kinds(tex2Dgrad(_SkyPicturesA, uv, gx, gy), tex2Dgrad(_SkyPicturesB, uv, gx, gy));
            }

            float4 KindsLod(float3 d, float lod)
            {
                float4 uv = float4(PictureUV(d), 0.0, lod);
                return Kinds(tex2Dlod(_SkyPicturesA, uv), tex2Dlod(_SkyPicturesB, uv));
            }

            // How much of each kind's cloud lies between d and the sun, across the picture: four looks
            // along the sky towards it, 0.7 to 7 degrees out, blurred, AVERAGED (0..1) (HDRP's Cloud Layer:
            // "2D raymarching on the Cloud Map to approximate self-shadowing from the sun light"). Summed,
            // as first built, a thick patch had up to 12 of depth: lit at 2% of the sun, dark grey.
            float4 TowardsSun(float3 d)
            {
                float3 along = _SunDir - d * dot(_SunDir, d);
                float len = length(along);
                float4 depth = 0;
                if (len > 1e-4)
                {
                    along /= len;
                    [unroll]
                    for (int k = 1; k <= 4; k++)
                    {
                        float s = 0.012 * (float)k * (0.5 + 0.5 * (float)k);
                        depth += KindsLod(normalize(d + along * s), 3.0);
                    }
                }
                return depth * 0.25;
            }

            // A kind's light: the sun through its own cloud (Beer, with a floor for the light scattered
            // round inside), in the clouds' phase, and the sky light, a little less on its thick parts.
            float3 KindLight(float4 kind, float opacity, float tau, float cosTheta)
            {
                float3 sun = _SunColor;
                if (_SunsetEdge.w > 0.0)
                    sun *= EarthShadow(kind.x);
                float phase = 0.65 + 0.35 * min(HenyeyGreenstein(cosTheta, kind.z), 5.0);
                float depth = tau * kind.y;
                float through = max(exp(-depth), 0.5 * exp(-depth * 0.3));
                return sun * (through * phase) + _AmbientColor * (1.0 - 0.2 * opacity);
            }

            // "a in front of b", premultiplied light + what is left of what is behind.
            float4 Over(float4 a, float4 b)
            {
                return float4(a.rgb + a.a * b.rgb, a.a * b.a);
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.worldPos - _WorldSpaceCameraPos);

                // The derivatives first, before any branch; the jump where u wraps round taken out.
                float2 uv = PictureUV(dir);
                float2 gx = ddx(uv);
                float2 gy = ddy(uv);
                gx.x = frac(gx.x + 0.5) - 0.5;
                gy.x = frac(gy.x + 0.5) - 0.5;

                if (dir.y <= 0.0)
                    return 0;

                // Sky pixels only: at the far plane there is nothing in front.
                float raw = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos));
                if (LinearEyeDepth(raw) < _ProjectionParams.z * 0.98)
                    return 0;

                // The flow (HDRP's): the pictures carried a little way along the wind and back, two looks
                // half a cycle apart, each faded out where it jumps back. Clouds move, the sky never
                // drifts away. Against the wind: the clouds go with it.
                float2 shift = frac(float2(_SkyFlow.x, _SkyFlow.x + 0.5)) - 0.5;
                float3 wind = float3(_SkyFlow.y, 0.0, _SkyFlow.z);
                float3 d1 = normalize(dir - wind * shift.x);
                float3 d2 = normalize(dir - wind * shift.y);
                float blend = abs(shift.x) * 2.0;

                float4 opacity = saturate(lerp(KindsGrad(d1, gx, gy), KindsGrad(d2, gx, gy), blend)) *
                                 float4(_SkyKinds[0].w, _SkyKinds[1].w, _SkyKinds[2].w, _SkyKinds[3].w);

                // Into the haze at the horizon: gone at it, 60% there 3 degrees up, all of it from 10.
                opacity *= smoothstep(0.0, 0.05, dir.y) * lerp(0.6, 1.0, smoothstep(0.0, 0.17, dir.y));
                if (max(max(opacity.x, opacity.y), max(opacity.z, opacity.w)) < 0.002)
                    return 0;

                float4 tau = lerp(TowardsSun(d1), TowardsSun(d2), blend);
                float cosTheta = dot(dir, _SunDir);

                // Back to front: the cirrus highest, then the sheep clouds, the layered sky, and the far
                // clouds lowest.
                float4 cirrus = float4(KindLight(_SkyKinds[0], opacity.x, tau.x, cosTheta) * opacity.x, 1.0 - opacity.x);
                float4 sheep = float4(KindLight(_SkyKinds[1], opacity.y, tau.y, cosTheta) * opacity.y, 1.0 - opacity.y);
                float4 far = float4(KindLight(_SkyKinds[2], opacity.z, tau.z, cosTheta) * opacity.z, 1.0 - opacity.z);
                float4 layers = float4(KindLight(_SkyKinds[3], opacity.w, tau.w, cosTheta) * opacity.w, 1.0 - opacity.w);
                float4 result = Over(far, Over(layers, Over(sheep, cirrus)));

                return float4(result.rgb, 1.0 - result.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
