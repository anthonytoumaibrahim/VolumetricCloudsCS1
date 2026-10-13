// Raymarched volumetric cloud layer for Cities: Skylines (Unity 5.6, built-in pipeline).
//
// Drawn on a box that follows the camera, so every pixel gets a fragment. Each fragment
// intersects its view ray with the cloud slab [_CloudBottom, _CloudTop] and marches it, then
// marches the rain hanging underneath: one pass, one view ray, one depth read, so the two
// media composite correctly against each other and the scene. (With the camera under the
// cloud base the same pass is drawn twice, clouds and air apart: see _DrawPart.)
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
            // 3.5, not 4.0: Unity 5.6 builds NO Metal code at target 4.0 (a geometry-shader
            // level, unsupported on Metal: an EMPTY blob, no error). 3.5 is the same SM4.0
            // on D3D11 (blobs byte-identical), OpenGL 3.3, and Metal. tools/bundle-apis.ps1.
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "CloudCommon.cginc"
            // The distance fade, the rain curtains, the fog and the jitter (CloudMedia.cginc).
            #include "CloudMedia.cginc"
            // Where the lightning bolts are: BoltStop (1.5.0).
            #include "LightningBoltMap.cginc"

            // The stretch of ray the fog's step count (Quality^2 / 160) is set for: 9 km, as far as
            // the fog went until its distance became a setting. A longer stretch gets more steps.
            #define FOG_STEP_STRETCH 9000.0

            sampler2D_float _CameraDepthTexture;

            float _Steps;
            float _UseDepth;

            // Which part of the picture this draw makes (Sky/CloudVolume.cs, 1.3.0): 0 = all of it,
            // 1 = the clouds alone, 2 = the air under them alone (rain, fog, the rainbow's arch).
            // With the camera under the cloud base the clouds are drawn on their own BEFORE the
            // city's see-through objects and the air after them, as two blends that make exactly
            // Over(lower, clouds) below: power lines, halos and smoke are never behind a cloud from
            // there, but drawn under the one pass they had the whole cloud behind them painted over
            // them (the author: the clouds "blend" with the power lines).
            float _DrawPart;

            // SMOOTHER CLOUDS (Sky/CloudTemporal.cs, 1.5.0): the clouds marched into a half-size target just
            // before the main camera draws (mode 1), blended with the last frames' (mode 2), and read back by
            // the camera's own draw (mode 0 with _TemporalOn) wherever nothing of the scene stands inside the
            // cloud slab and no bolt shows. Modes 1 and 2 draw a quad over the target, not the box.
            float _TemporalMode;        // 0 = the camera's draw, 1 = march into the target, 2 = blend
            float _TemporalOn;          // mode 0: 1 while there is a blend to read -- by the camera standing at
                                        // _TemporalEye only (the main camera): any other marches
            float _TemporalTint;        // mode 0, TEMPORARY test switch: blended clouds red, marched ones blue
            sampler2D _TemporalTex;     // mode 0: the blend; mode 2: this frame's march (rgb = light,
                                        // a = 1 - transmittance, before the distance fade)
            float4x4 _TemporalVP;       // mode 0: the view-projection it was marched with (OpenGL convention)
            float4 _TemporalCorner0;    // modes 1, 2: the camera's rays (world, not normalised) at the target's
            float4 _TemporalCorner1;    // corners (0, 0), (1, 0), (0, 1), (1, 1); mode 1's moved by this frame's
            float4 _TemporalCorner2;    // sub-texel shift
            float4 _TemporalCorner3;
            float4 _TemporalEye;        // modes 1, 2: xyz = the camera; w = the target's up (+1 / -1)
            float4 _TemporalSize;       // modes 1, 2: the target's width, height, 1 / width, 1 / height
            float _TemporalJitter;      // mode 1: this frame's shift of the march's jitter, 0..1
            // Mode 2 reads two targets, and a pass has 16 samplers: the clouds, fog, rain, bolt and rainbow
            // take 14, _TemporalTex the 15th. So the blend reads this frame's march through _TemporalTex and
            // the blend until last frame through the rainbow's _BowTex, which it never reads as the rainbow
            // (Sky/CloudTemporal sets both on its own material; the camera's draw keeps its own).
            float4x4 _TemporalPrevVP;   // mode 2: last frame's view-projection
            float4 _TemporalParams;     // mode 2: x = how much of the last frames is kept (0: none), yz = how far
                                        // the clouds drifted since then (m, x and z)

            float3 _SunColor;      // (_SunDir, towards the light, is in CloudMedia)
            float3 _AmbientColor;

            // Lightning: point sources INSIDE the cloud layer (CloudLightning.cs). The clouds
            // only ever knew the sun, the moon and the ambient, so the game's strikes lit the
            // whole city and left the sky dark.
            #define MAX_FLASHES 4
            float _FlashCount;
            float4 _FlashPos[MAX_FLASHES];     // xyz world position, w = reach in metres
            float4 _FlashColor[MAX_FLASHES];   // rgb, already scaled by this frame's flicker

            // Night glow: a small, even, pale luminance on the underside of the clouds, which
            // is how clouds over settled land look at night -- lit faintly from below by
            // diffuse skyglow, not black. Deliberately NOT derived from where the city is: a
            // first version projected a map of the buildings onto the cloud base in sodium
            // orange, and an orange slab following the street plan looked "very weird".
            float3 _NightGlow;         // colour * strength * how much it is night; 0 by day

            // The city's lights, as the fog sees them (FogLampMap + FogLamps.shader): a map,
            // centred on the camera, of the light each lamp and vehicle sheds into the air
            // round it. Globals, set in the same frame as the map is drawn, so the two can
            // never disagree about where the map is.
            sampler2D _VCLampMap;      // rgb = light, a = luminance x the altitude it comes from
            float4 _VCLampMapRect;     // xy = minimum corner (world xz), z = 1 / size
            float4 _VCLampParams;      // x = strength, y = 3 / R^2, z = 1 while on, w = how far out

            sampler2D _CloudShadowTex; // CloudShadowMap: light reaching the ground, 1 - darkness .. 1
            float _ShadowAvailable;
            float3 _ShadowOrigin;
            float3 _ShadowRight;
            float3 _ShadowUp;
            float _ShadowSize;
            float _ShadowDarkness;

            // The rainbow (Sky/Rainbow.cs, Sky/RainbowTable.cs, 1.3.0): its colours by the angle from
            // the point opposite the sun, one row per colour (R, G, B, an empty fourth), the value
            // in every channel of a LINEAR texture and read from alpha, never sRGB-decoded.
            sampler2D _BowTex;
            float4 _BowParams;     // x = the table's width per radian, y = what 1.0 in it stands for,
                                   // z = 1 while a bow can show (0: nothing of it runs),
                                   // w = the height of the ground the arch stands on
            float3 _BowSun;        // the cloud's sun, untinted like the rain's, x the Rainbows setting
                                   // x how far the arch is in
            float4 _BowArch;       // the arch (Sky/RainbowArch.cs): xyz = the spot it is seen from,
                                   // w = how far from it it stands

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;

                // Smoother clouds' passes: a quad whose corners are 0..1 over the half-size target, the
                // camera's rays between them (Sky/CloudTemporal.cs). Its up as GL.GetGPUProjectionMatrix
                // says the target has it (probed in the 5.6 editor, 2026-10-12: -1 on D3D11), so a texel
                // written at uv is read back at uv.
                if (_TemporalMode > 0.5)
                {
                    float2 uv = v.vertex.xy;
                    o.pos = float4(uv.x * 2.0 - 1.0, (uv.y * 2.0 - 1.0) * _TemporalEye.w, 0.5, 1.0);
                    o.worldPos = _TemporalEye.xyz + lerp(lerp(_TemporalCorner0.xyz, _TemporalCorner1.xyz, uv.x),
                                                         lerp(_TemporalCorner2.xyz, _TemporalCorner3.xyz, uv.x), uv.y);
                    o.screenPos = float4(uv, 0.0, 1.0);
                    return o;
                }

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

            // Cloud detail's light (Sky/CloudDetail.cs; only while _DetailAmount > 0).
            float4 _DetailLight;    // x, y, z = Wrenninge's a, b, c; w = the gain that keeps a
                                    // side-lit sunny face as bright as the light below made it
                                    // (_DetailLight2, the rest of it, is in CloudMedia.cginc,
                                    // with CloudLod, which reads its lod offset)

            // The Cumulus style's light (Sky/CloudStyle.cs; only while _CloudStyle > 0): EVE-Redux
            // V5's four phase lobes -- two of single scattering (the silver lining), two of multiple
            // scattering (the soft glow deep inside, through a reduced extinction).
            float4 _CumulusLight;   // x = the multiple scattering's share of the extinction, y = the
                                    // gain that keeps a side-lit sunny face as bright as before,
                                    // z = the single lobes' cap, w = 1 / the span the sky light's
                                    // height is measured over
            float4 _CumulusLobes;   // strengths: single 1, single 2, multiple 1, multiple 2
            float4 _CumulusLobeG;   // their eccentricities, in the same order

            // SUNLIGHT THROUGH THE CLOUDS (Sky/LightThrough.cs; only while _LightThrough.x > 0): the
            // multiple-scattering light falls off no faster than 1 / (1 + k tau)^2, so thin and
            // middling cloud glows with the sun behind it and a thick deck's far side stays dark.
            // Never darker than without it; tau = 0, a sunlit face, unchanged.
            float4 _LightThrough;   // x = amount (0 = off), y = k

            float Soak(float transmittance, float tau)
            {
                float tail = 1.0 / (1.0 + _LightThrough.y * tau);
                return transmittance + _LightThrough.x * (max(transmittance, tail * tail) - transmittance);
            }

            // THE LIGHT IN THE SHADE (Sky/CloudShade.cs), on the same two lights: the sky light at the
            // base as a share of the top's (0.45 before), and the sunlit ground's light on the
            // undersides, strongest at the base, none at the top (Frostbite's ground ambient).
            float4 _ShadeLight;     // x = the sky light at the base (0.45 = as before), y = the ground's
                                    // light as a share of _SunColor (0 = none)

            // The sky light at height hSky, before its occlusion by the cloud above.
            float3 ShadeSky(float hSky)
            {
                return _AmbientColor * (_ShadeLight.x + (1.0 - _ShadeLight.x) * hSky);
            }

            // SUNSET LIGHT (Sky/SunsetLight.cs; only while _SunsetEdge.w > 0, round sunset and sunrise):
            // the Earth's shadow. A point y metres up still sees the sun sqrt(2 y / R) radians under the
            // horizontal, so the tops stay lit after the bases. 1 while the sun is up for it, 0 once
            // it has set for it, smooth over the sun's disc.
            float4 _SunsetEdge;     // x + y sqrt(y - z) = the edge's 0..1; w = how much of it is used

            float EarthShadow(float y)
            {
                float e = saturate(_SunsetEdge.x + _SunsetEdge.y * sqrt(max(y - _SunsetEdge.z, 0.0)));
                return 1.0 - _SunsetEdge.w * (1.0 - e * e * (3.0 - 2.0 * e));
            }

            // A forward peak (silver linings towards the sun, capped so the rim cannot blow out)
            // over a weak back lobe; octave c flattens both (Wrenninge 2013).
            float DetailPhase(float cosTheta, float c)
            {
                return 0.75 * min(HenyeyGreenstein(cosTheta, 0.8 * c), 10.0)
                     + 0.25 * HenyeyGreenstein(cosTheta, -0.3 * c);
            }

            // The sun's optical depth at p: seven stretches towards it, 0-6-12-24 m read WITH the
            // detail (the small billows shading each other), then four growing ones out to about
            // the layer's thickness from the big shape only.
            float SunDepth(float3 p, float lod)
            {
                float tau = 0.0;
                float prev = 0.0;
                float s = 6.0;

                [loop]
                for (int k = 0; k < 7; k++)
                {
                    float segment = s - prev;
                    tau += SampleDensityLod(p + _SunDir * (prev + segment * 0.5), k < 3, lod) * segment;
                    prev = s;
                    s *= k < 2 ? 2.0 : _DetailLight2.y;
                }

                return tau * _Absorption;
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

            // SunShaft for the rainbow, which is only drawn with the map there: nothing where the
            // map has no answer. Past its tile (26 km round the map's centre) the lookup would wrap
            // onto the far side of the city and light a drop that is in shadow, or darken a lit one.
            float BowShaft(float3 p)
            {
                float3 rel = p - _ShadowOrigin;
                float2 uv = float2(dot(rel, _ShadowRight), dot(rel, _ShadowUp)) / _ShadowSize + 0.5;
                float2 inside = saturate((0.5 - abs(uv - 0.5)) * 40.0);
                float lit = tex2Dlod(_CloudShadowTex, float4(uv, 0, 0)).r;
                return saturate((lit - (1.0 - _ShadowDarkness)) / max(_ShadowDarkness, 0.01)) * inside.x * inside.y;
            }

            // The rainbow's colours at an angle from the point opposite the sun (a brighter sky inside
            // the bow, violet to red at 40.6-42.5 degrees, the dark band, the reversed secondary at
            // 50-53.5), for a direction at cosTheta to the sun, times the light. Nothing beyond the table.
            float3 BowColour(float cosTheta)
            {
                float3 colour = 0;
                float u = acos(clamp(-cosTheta, -1.0, 1.0)) * _BowParams.x;
                if (u < 1.0)
                {
                    colour = float3(tex2Dlod(_BowTex, float4(u, 0.125, 0, 0)).a,
                                    tex2Dlod(_BowTex, float4(u, 0.375, 0, 0)).a,
                                    tex2Dlod(_BowTex, float4(u, 0.625, 0, 0)).a) * (_BowParams.y * _BowSun);
                }

                return colour;
            }

            // THE RAINBOW ARCH (Sky/Rainbow.cs, Sky/RainbowArch.cs, 1.3.0): the bow a spot on the ground
            // sees, drawn on a thin shell round that spot, so it stands fixed in the world and stays
            // sharp from every camera. (A bow per camera followed the camera -- "on the camera lens";
            // one pinned inside the rain smeared from anywhere but the spot.) Where this ray crosses
            // the shell: two distances, -1 for none.
            float2 ArchCrossings(float3 origin, float3 dir)
            {
                float3 l = origin - _BowArch.xyz;
                float b = dot(l, dir);
                float c = dot(l, l) - _BowArch.w * _BowArch.w;
                float disc = b * b - c;
                float2 t = -1.0;
                if (disc > 0.0)
                {
                    float root = sqrt(disc);
                    t = float2(-b - root, -b + root);
                }

                return t;
            }

            // The arch's light at a crossing t (before tEnd, where the scene or the rain ends): its
            // colour at that point's angle from the spot's antisolar axis, times what the spot sees
            // along its line of sight through it -- rain (the curtains' own unevenness and distance
            // fade) the sun reaches, read at four depths round the shell (RainbowArch.Depths) between
            // the arch's ground (_BowParams.w) and the cloud base -- fading out as the camera goes
            // round behind it. Read at the shell alone, it was cut off sharp along the edge of every
            // cloud shadow crossing it ("the rainbow is rendering 'behind' some clouds").
            float3 ArchLight(float3 origin, float3 dir, float t, float tEnd)
            {
                float3 light = 0;
                if (t > 0.0 && t < tEnd)
                {
                    float3 p = origin + dir * t;
                    float3 v = (p - _BowArch.xyz) / _BowArch.w;
                    float3 colour = BowColour(dot(v, _SunDir));
                    if (max(colour.r, max(colour.g, colour.b)) > 1e-6)
                    {
                        float lit = 0.0;
                        float count = 0.0;
                        [unroll]
                        for (int k = 0; k < 4; k++)
                        {
                            float3 q = _BowArch.xyz + v * (_BowArch.w * (0.6 + (float)k * (0.8 / 3.0)));
                            if (q.y > _BowParams.w && q.y < _CloudBottom)
                            {
                                count += 1.0;
                                float rain = RainSigma(q, t) / max(_RainShaftDensity * _RainAmount, 1e-9);
                                lit += BowShaft(q) * min(rain, 1.5);
                            }
                        }

                        float see = smoothstep(0.0, 0.5, dot(dir, v));
                        light = colour * (see * lit / max(count, 1.0));
                    }
                }

                return light;
            }

            // What the light of a cloud depends on along one view ray: its phases.
            struct RayLight
            {
                float phase;
                float3 octaves;
                float2 lobes;
            };

            RayLight LightAlong(float cosTheta)
            {
                RayLight r;

                // Mostly isotropic with a forward lobe: gives the bright rim towards the
                // sun without leaving clouds black everywhere else.
                r.phase = 0.65 + 0.35 * min(HenyeyGreenstein(cosTheta, 0.55), 5.0);

                // Cloud detail: three octaves of multiple scattering, each dimmed by a, reaching
                // deeper (extinction x b) and scattering more evenly (phase flattened by c) --
                // bright surfaces, soft insides, real shade. Their phases depend on the ray only.
                r.octaves = 0;
                if (_DetailAmount > 0.0)
                {
                    float a = _DetailLight.x;
                    float c = _DetailLight.z;
                    r.octaves = float3(DetailPhase(cosTheta, 1.0), a * DetailPhase(cosTheta, c),
                                       a * a * DetailPhase(cosTheta, c * c)) * _DetailLight.w;
                }

                // The Cumulus style: its lobes depend on the ray only (CloudStyle.Single/Multiple).
                r.lobes = 0;
                if (_CloudStyle > 0.5)
                {
                    float single = _CumulusLobes.x * HenyeyGreenstein(cosTheta, _CumulusLobeG.x)
                                 + _CumulusLobes.y * HenyeyGreenstein(cosTheta, _CumulusLobeG.y);
                    float multiple = _CumulusLobes.z * HenyeyGreenstein(cosTheta, _CumulusLobeG.z)
                                   + _CumulusLobes.w * HenyeyGreenstein(cosTheta, _CumulusLobeG.w);
                    r.lobes = float2(min(single, _CumulusLight.z), multiple) * _CumulusLight.y;
                }

                return r;
            }

            // The far deck's light (MarchFarDeck below) is a thick deck's, not one point's: read off the
            // noise at one point per direction, it streaked the band and shimmered with the wind.
            float4 _FarLight;   // at the base: x = the optical depth towards the sun, y = the cloud above
                                // (density x metres); less of both higher up (h, 0..1 through the layer)

            float FarSunTransmittance(float h)
            {
                float tau = _FarLight.x * (1.0 - 0.8 * h);
                return max(exp(-tau), 0.7 * exp(-tau * 0.25));
            }

            // The light a cloud at p sends along the ray (before its density). One definition, for
            // the march and the far deck alike, so the two meet in the same colour. `far`: the far
            // deck's, from _FarLight's depths instead of looks through the clouds (and no lightning).
            float3 CloudRadiance(float3 p, float lod, RayLight r, bool far)
            {
                bool detail = _DetailAmount > 0.0;
                bool cumulus = _CloudStyle > 0.5;

                // Sunlight through the clouds (Soak), on both lights below.
                bool soak = _LightThrough.x > 0.0;

                float h = saturate((p.y - _CloudBottom) / (_CloudTop - _CloudBottom));

                // Undersides get less sky light than tops. (A Cumulus cloud's height is
                // measured over the whole span its curve runs over, not over the part
                // the march covers.)
                float hSky = cumulus ? saturate((p.y - _CloudBottom) * _CumulusLight.w) : h;
                float3 ambient = _AmbientColor * (0.45 + 0.55 * hSky);
                float3 sunColor = _SunColor;
                // Sunset light's Earth shadow, on every light's sun.
                if (_SunsetEdge.w > 0.0)
                    sunColor *= EarthShadow(p.y);
                float3 radiance;
                if (cumulus)
                {
                    float tau = far ? _FarLight.x * (1.0 - 0.8 * h) : SunDepth(p, lod);
                    float multipleT = exp(-tau * _CumulusLight.x);
                    if (soak)
                        multipleT = Soak(multipleT, tau);
                    float sunLit = r.lobes.x * exp(-tau) + r.lobes.y * multipleT;

                    // The sky light is blocked by the cloud above: two looks straight up.
                    float above = far ? _FarLight.y * (1.0 - h) : SampleDensityLod(p + float3(0.0, 15.0, 0.0), false, lod) * 30.0
                                + SampleDensityLod(p + float3(0.0, 60.0, 0.0), false, lod) * 60.0;
                    radiance = sunColor * sunLit + ShadeSky(hSky) * exp(-above * _Absorption * _DetailLight2.x);
                    if (_ShadeLight.y > 0.0)
                        radiance += sunColor * (_ShadeLight.y * (1.0 - hSky) * (1.0 - hSky));
                }
                else if (detail)
                {
                    float tau = far ? _FarLight.x * (1.0 - 0.8 * h) : SunDepth(p, lod);
                    float b = _DetailLight.y;
                    float octave1T = exp(-tau * b);
                    float octave2T = exp(-tau * b * b);
                    if (soak)
                    {
                        octave1T = Soak(octave1T, tau);
                        octave2T = Soak(octave2T, tau);
                    }
                    float sunLit = r.octaves.x * exp(-tau) + r.octaves.y * octave1T + r.octaves.z * octave2T;

                    // The sky light is blocked by the cloud above: two looks straight up.
                    float above = far ? _FarLight.y * (1.0 - h) : SampleDensityLod(p + float3(0.0, 15.0, 0.0), false, lod) * 30.0
                                + SampleDensityLod(p + float3(0.0, 60.0, 0.0), false, lod) * 60.0;
                    radiance = sunColor * sunLit + ShadeSky(hSky) * exp(-above * _Absorption * _DetailLight2.x);
                    if (_ShadeLight.y > 0.0)
                        radiance += sunColor * (_ShadeLight.y * (1.0 - hSky) * (1.0 - hSky));

                    // A low amount: the old light (detail off's, below) handed over to this
                    // one, as the old break-up is in CloudCommon -- no step at 0%.
                    if (_DetailLight2.w < 1.0)
                        radiance = lerp(sunColor * (far ? FarSunTransmittance(h) : SunTransmittance(p)) * r.phase + ambient, radiance, _DetailLight2.w);
                }
                else
                {
                    float sun = far ? FarSunTransmittance(h) : SunTransmittance(p);
                    radiance = sunColor * sun * r.phase + ambient;
                }

                if (!far && _FlashCount > 0.5)
                    radiance += Lightning(p);

                // It comes from below: strongest on the base, gone by the top.
                radiance += _NightGlow * ((1.0 - h) * (1.0 - h));

                return radiance;
            }

            // The cloud slab between tEnter and tExit, before the distance fade (FinishClouds): light in
            // rgb, transmittance in a. `tStop`: a lightning bolt's BoltStop (1e9 without one): the march
            // ends there but keeps the steps of the whole ray, so the cloud in front of a bolt is marched
            // exactly as on the pixels beside it. Smoother clouds blend this, then finish it per pixel.
            float4 MarchCloudsRaw(float3 origin, float3 dir, float tEnter, float tExit, float tStop, float jitter, float cosTheta)
            {
                if (tExit <= tEnter)
                    return float4(0, 0, 0, 1);

                float steps = max(_Steps, 8.0);
                float stepLen = (tExit - tEnter) / steps;

                // Per-pixel jitter trades banding for fine noise, which reads as softness.
                float t = tEnter + stepLen * jitter;

                RayLight rayLight = LightAlong(cosTheta);

                float transmittance = 1.0;
                float3 light = 0;

                // Skip the empty sky (1.5.0, CloudMedia EmptyAhead): the ray in the map's fine cells per
                // metre, and where it asks the map next -- not again inside a cell that may hold cloud.
                bool skipEmpty = _EmptySky.x > 0.5;
                float2 dc = dir.xz * (_EmptySky.y / _WeatherTile);
                float askAt = -1.0;

                // 192: the Quality setting's 96 at most, twice that under Cumulus (CloudStyle.StepFactor).
                [loop]
                for (int s = 0; s < 192; s++)
                {
                    if ((float)s >= steps || transmittance < 0.02 || t > tStop)
                        break;

                    float3 p = origin + dir * t;

                    if (skipEmpty && t >= askAt)
                    {
                        float askAgain;
                        float empty = EmptyAhead(p, dc, askAgain);
                        if (empty > 0.0)
                        {
                            // This sample and every whole step still inside the empty cell would find
                            // nothing: go past them. The 0.999 keeps the last one off the cell's wall.
                            float n = min(floor(empty * 0.999 / stepLen), steps);
                            s += (int)n;
                            t += (n + 1.0) * stepLen;
                            continue;
                        }

                        askAt = t + askAgain;
                    }

                    // The noise's mip level from this pixel's footprint here (CloudMedia).
                    float lod = CloudLod(t);
                    float d = SampleDensityLod(p, true, lod);

                    if (d > 0.001)
                    {
                        float3 radiance = CloudRadiance(p, lod, rayLight, false);

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

                return float4(light, transmittance);
            }

            // MarchCloudsRaw's light and transmittance, dissolved into the distance. Returns premultiplied
            // light in rgb and what is left of the background in a. `skyBehind`: the ray ends in the sky,
            // not on the ground or a building.
            float4 FinishClouds(float4 raw, float tEnter, float3 dir, bool skyBehind)
            {
                float3 light = raw.rgb;
                float transmittance = raw.a;

                // Dissolve into the distance rather than ending on a hard line; at night only where
                // the clouds run out (CloudMedia).
                float presence = CloudPresence(tEnter);

                // What dissolves lets the sky behind it through -- all of it but the sun's glow and
                // disc (CloudMedia SkyGlowPass, 1.4.0), so the clouds still block the sun.
                float sky = 1.0;
                if (skyBehind && _SkyGlowMode.y > 0.5 && presence < 1.0 && transmittance < 1.0)
                    sky = SkyGlowPass(dir);

                return float4(light * presence, transmittance + (1.0 - transmittance) * (1.0 - presence) * sky);
            }

            // The cloud slab between tEnter and tExit, marched and finished (see the two above).
            float4 MarchClouds(float3 origin, float3 dir, float tEnter, float tExit, float tStop, float jitter, float cosTheta, bool skyBehind)
            {
                if (tExit <= tEnter)
                    return float4(0, 0, 0, 1);

                return FinishClouds(MarchCloudsRaw(origin, dir, tEnter, tExit, tStop, jitter, cosTheta), tEnter, dir, skyBehind);
            }

            // SMOOTHER CLOUDS, the camera's draw: the blended clouds on this pixel's ray, looked up through
            // the matrix they were marched with this frame (Sky/CloudTemporal.cs), before the distance fade.
            // a < 0: none here (off the target), march as usual.
            float4 TemporalClouds(float3 dir)
            {
                float4 clip = mul(_TemporalVP, float4(dir, 0.0));
                if (clip.w <= 1e-4)
                    return float4(0, 0, 0, -1);

                float2 uv = clip.xy / clip.w * 0.5 + 0.5;
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    return float4(0, 0, 0, -1);

                float4 blended = tex2Dlod(_TemporalTex, float4(uv, 0, 0));
                return float4(blended.rgb, 1.0 - blended.a);
            }

            // How far along a ray from `eye` its clouds were seen, for moving them to last frame's view:
            // from under the base, the base (it is flat); from over the top, the top; from inside, close
            // by. Exact for the bases, which is what a camera under them sees most of; elsewhere the
            // neighbourhood clamp in TemporalResolve keeps what is off from smearing.
            float TemporalDistance(float3 eye, float3 dir)
            {
                float dy = abs(dir.y) < 1e-4 ? (dir.y < 0 ? -1e-4 : 1e-4) : dir.y;
                float tA = (_CloudBottom - eye.y) / dy;
                float tB = (_CloudTop - eye.y) / dy;
                float tEnter = max(min(tA, tB), 0.0);
                float tExit = min(max(tA, tB), _MaxDistance);
                if (tExit <= tEnter)
                    return 100000.0;

                return tEnter > 0.0 ? tEnter : min(tExit, 1000.0);
            }

            // SMOOTHER CLOUDS, mode 2: this frame's march blended with the last frames', those moved to where
            // the same clouds were seen then (the camera's move and the wind's), and held inside what this
            // frame's march has round the texel (the neighbourhood clamp), so a cloud that moved or changed
            // never leaves a ghost. A texel with no history (new on screen, or a reset) takes the march.
            // (This frame's march comes in through _TemporalTex, the blend until last frame through _BowTex:
            // see their declarations.)
            float4 TemporalResolve(float2 uv, float3 worldPos)
            {
                float2 texel = _TemporalSize.zw;
                float4 now = tex2Dlod(_TemporalTex, float4(uv, 0, 0));
                float4 low = now;
                float4 high = now;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        if (x != 0 || y != 0)
                        {
                            float4 s = tex2Dlod(_TemporalTex, float4(uv + float2(x, y) * texel, 0, 0));
                            low = min(low, s);
                            high = max(high, s);
                        }
                    }
                }

                float3 eye = _TemporalEye.xyz;
                float3 dir = normalize(worldPos - eye);
                float3 then = eye + dir * TemporalDistance(eye, dir) - float3(_TemporalParams.y, 0.0, _TemporalParams.z);

                float keep = 0.0;
                float4 history = now;
                float4 clip = mul(_TemporalPrevVP, float4(then, 1.0));
                if (clip.w > 1e-3)
                {
                    float2 was = clip.xy / clip.w * 0.5 + 0.5;
                    if (was.x >= 0.0 && was.x <= 1.0 && was.y >= 0.0 && was.y <= 1.0)
                    {
                        history = clamp(tex2Dlod(_BowTex, float4(was, 0, 0)), low, high);
                        keep = _TemporalParams.x;
                    }
                }

                // Nothing invalid ever goes into the history: there it would stay, and spread. On D3D11
                // min/max return the operand that is a number, so a NaN becomes 0 (not isnan, which the
                // compiler may fold away).
                float4 blended = lerp(now, history, keep);
                return min(max(blended, 0.0), float4(60000.0, 60000.0, 60000.0, 1.0));
            }

            // THE FAR DECK (Sky/CloudVolume.cs, 1.5.0 trial): the clouds past where the march thins,
            // out to the horizon. A real deck 2 km up reaches ~160 km, where the Earth's curve takes it
            // under the horizon; ours ended at the reach (30 km), 4-5 degrees up, and under a deck that
            // hides the sky the clear band below it showed ("the horizon should be full of clouds").
            // Not a march: one smooth band. The first trial marched the same clouds out to 250 km, and
            // the weather map, which repeats every _WeatherTile (13.5 km), came back ~15 times along each
            // ray, as evenly spaced arcs fanning into the vanishing point ("the clouds curve in a really
            // ugly way"), grainy from 12 coarse jittered steps. That far out a real deck shows no shapes
            // anyway: seen edge-on, it closes up into a band, more solid towards the horizon (a line of
            // sight crosses more and more of the layer). So: how much of the sky the deck covers along
            // the ray (the cover over the stretch of layer it crosses, its heights measured over the
            // curved Earth, x + d^2 / 2R), lit as a thick deck by the clouds' own light (nothing read from
            // the maps or the noise: anything read per direction shows as streaks down the band, and
            // shimmers as the wind moves it). Under the lowest ray that still crosses it, the band
            // holds on, so it covers the sky under the map's edge seen from high up, as the sky does.
            // It is drawn BEHIND the march and begins at a fixed distance, where the near clouds already
            // thin out seen low over the horizon (their far detail averages away, ~10-13 km for Cumulus).
            // It began where the march's own fade said, per ray: that moved its edge up as the ray came
            // down, squeezing the edge's fade into a few pixels, and it came in only after the near
            // clouds had gone -- a strip of sky and a line between the two ("as if it's a different
            // layer"). Seen from under the deck, in it or over it (the line of sight's height over the
            // curved Earth is a parabola: its stretch in the layer is at most two pieces). The near
            // clouds stay flat.
            float4 _FarDeck;    // x = how much of it (0: off; "Clouds to the horizon" x the cover's 90-95% fade-in),
                                // y = where it begins (m), z = how far it goes (m), w = the haze it ends in, 0..1
            float4 _FarDeck2;   // y = how much layer crossed makes it 63% there (m), w = how far out its light
                                // is worked out (m); x, z unused
            #define EARTH_RADIUS 6371000.0
            #define FAR_NEVER 1e10
            #define FAR_EDGE 0.015

            // Where, across the ground, a ray from height originY rising k metres per metre is at height y
            // over the curved Earth: the roots of d^2 / 2R + k d + (originY - y) = 0, the nearer first, the
            // one that would cancel worked out from their product (2 R c). (FAR_NEVER, FAR_NEVER): never.
            float2 FarLevel(float originY, float k, float y)
            {
                float c = originY - y;
                float disc = k * k - 2.0 * c / EARTH_RADIUS;
                if (disc < 0.0)
                    return float2(FAR_NEVER, FAR_NEVER);

                float s = sqrt(disc);
                float2 d;
                if (k < 0.0)
                {
                    d.y = EARTH_RADIUS * (s - k);
                    d.x = 2.0 * EARTH_RADIUS * c / d.y;
                }
                else
                {
                    d.x = -EARTH_RADIUS * (k + s);
                    d.y = 2.0 * EARTH_RADIUS * c / min(d.x, -1e-3);
                }
                return d;
            }

            // `tStop`: a lightning bolt's BoltStop (1e9 without one), along the ray: no deck behind it.
            float4 MarchFarDeck(float3 origin, float3 dir, float cosTheta, float tStop)
            {
                float lxz = max(length(dir.xz), 1e-4);
                float2 way = dir.xz / lxz;

                // The ray's rise per metre across the ground, held at the lowest that is at the layer's
                // top at the far end: under it the band's last row, so it covers the sky under the map's
                // edge. (Held where it met the base there, it crossed none: a clear strip under the band.)
                float end = _FarDeck.z;
                float kLowest = (_CloudTop - origin.y) / end - end / (2.0 * EARTH_RADIUS);
                float k = max(dir.y / lxz, kLowest);

                // Where the band begins (see above): the same for every ray.
                float from = _FarDeck.y;

                // In the layer: under the top (between its two levels) and not under the base (outside
                // its two). Before the ray dips under the base, and after it comes back up.
                float2 top = FarLevel(origin.y, k, _CloudTop);
                if (top.x >= FAR_NEVER)
                    return float4(0, 0, 0, 1);
                float2 base = FarLevel(origin.y, k, _CloudBottom);
                float stop = min(end, tStop * lxz);
                float2 first = float2(max(top.x, from), min(min(top.y, base.x), stop));
                float2 second = float2(max(max(top.x, base.y), from), min(top.y, stop));
                float lengthFirst = max(first.y - first.x, 0.0);
                float lengthSecond = max(second.y - second.x, 0.0);
                if (lengthFirst + lengthSecond <= 0.0)
                    return float4(0, 0, 0, 1);

                // It fills up with the layer crossed, from none: a line of sight that only just reaches the
                // layer meets almost nothing, so lower and lower down the band builds up over degrees.
                // (Counted per "cloud" at the cover, it was ~90% there within 0.01 degree: a line.) The
                // same all round: anything read by direction is the same all the way down a column, and
                // a column-by-column change (the weather map on wide rings) streaked. (The cover needs no
                // say: the band only shows over a full overcast.)
                float coveredFirst = 1.0 - exp(-lengthFirst / _FarDeck2.y);
                float coveredSecond = 1.0 - exp(-lengthSecond / _FarDeck2.y);

                // Each piece thins into the haze by how far out it starts; the nearer in front.
                float spread = end - _FarDeck.y;
                float hazeFirst = 1.0 - _FarDeck.w * saturate((first.x - _FarDeck.y) / spread);
                float hazeSecond = 1.0 - _FarDeck.w * saturate((second.x - _FarDeck.y) / spread);
                float covered = coveredFirst + (1.0 - coveredFirst) * coveredSecond;
                float alpha = coveredFirst * hazeFirst + (1.0 - coveredFirst) * coveredSecond * hazeSecond;

                // Its edge -- where the line of sight stops reaching the layer past `from` (from over the
                // clouds: their own horizon) -- comes in over FAR_EDGE of rise, about 0.9 degrees: there
                // (seen from in or over the clouds) the layer crossed can grow from nothing to tens of
                // kilometres within a few thousandths of a degree. The same for every ray of a frame: an
                // edge that moved with the ray squeezed this fade into a few pixels.
                float kEdge = (_CloudTop - origin.y) / from - from / (2.0 * EARTH_RADIUS);
                if (origin.y > _CloudTop)
                    kEdge = max(kEdge, -sqrt(2.0 * (origin.y - _CloudTop) / EARTH_RADIUS));
                float edge = 1.0 - smoothstep(kEdge - FAR_EDGE, kEdge, k);
                // As much of it as is shown (fading in over 90-95% cover, while "Clouds to the horizon" is on).
                covered *= edge * _FarDeck.x;
                alpha *= edge * _FarDeck.x;
                if (alpha <= 0.001)
                    return float4(0, 0, 0, 1);

                // The clouds' own light for a thick deck (_FarLight): its bases from under it, its tops from
                // over it. It changes only with the angle to the sun.
                float hCamera = saturate((origin.y - _CloudBottom) / (_CloudTop - _CloudBottom));
                float3 p = float3(origin.x + way.x * _FarDeck2.w,
                                  _CloudBottom + lerp(0.2, 0.8, hCamera) * (_CloudTop - _CloudBottom),
                                  origin.z + way.y * _FarDeck2.w);
                float3 radiance = CloudRadiance(p, 0.0, LightAlong(cosTheta), true);

                // What it thins away by (the haze) lets the sky behind through, but not the sun's glow and
                // disc (as the near deck's fade). So do its "gaps", as much as it covers: they are an
                // average, not holes, and a 2% gap still showed a sun low behind a full overcast
                // (2026-10-12, "can the endless clouds hide the sun too?"). From nothing where it covers
                // nothing, so no edge round the sun.
                float sky = _SkyGlowMode.y > 0.5 ? SkyGlowPass(dir) : 1.0;
                return float4(radiance * alpha, (1.0 - covered) * lerp(1.0, sky, covered) + (covered - alpha) * sky);
            }

            // Rain hanging under the clouds: the grey curtains seen from a distance, and the
            // loss of visibility from inside a shower. Same return convention as MarchClouds.
            // `archT` (1.3.0): where the ray crosses the rainbow's arch (-1: not); `toArch` comes back
            // with what the rain leaves of the view up to each crossing -- the rain in front of the
            // arch dims it.
            float4 MarchRain(float3 origin, float3 dir, float tStart, float tEnd, float jitter, float cosTheta, float2 archT, out float2 toArch)
            {
                toArch = 1.0;
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

                    // Uneven curtains, stretched tall, sliding down as the rain falls (CloudMedia).
                    float sigma = RainSigma(p, t);

                    if (sigma > 0.0)
                    {
                        // A strike lights the rain around it as well as the cloud above it.
                        float3 lit = radiance;
                        if (_FlashCount > 0.5)
                            lit += Lightning(p) * 0.6;

                        float stepT = exp(-sigma * segment);

                        // The rain before the arch: up to each crossing, or all of this step.
                        toArch = archT > tPrev ? transmittance * exp(-sigma * min(archT - tPrev, segment)) : toArch;

                        light += transmittance * lit * (1.0 - stepT);
                        transmittance *= stepT;
                    }

                    tPrev = tNext;
                }

                // The march stopped, or the rain ended, before a crossing: all of it is in front.
                toArch = archT >= tPrev ? transmittance : toArch;
                return float4(light, transmittance);
            }

            // (GroundAt, FogGround, InFogLayer and FogAt -- where the fog is and how dense -- are
            // in CloudMedia.cginc.)

            // The light the city's lamps shed into the fog at p. The map is flat; the height it
            // loses comes back here, because its footprints are Gaussian and so separable: the
            // altitude of what lights this spot is a / luminance, and the light falls off above
            // and below it exactly as it does sideways. Fades out over the map's outer 5%.
            float3 LampLightAt(float3 p)
            {
                float2 uv = (p.xz - _VCLampMapRect.xy) * _VCLampMapRect.z;
                float2 inside = saturate(min(uv, 1.0 - uv) * 20.0);
                float edge = inside.x * inside.y;

                float4 m = tex2Dlod(_VCLampMap, float4(uv, 0, 0));
                float lum = dot(m.rgb, float3(0.2126, 0.7152, 0.0722));
                float dz = p.y - m.a / max(lum, 1e-6);
                return m.rgb * (exp(-dz * dz * _VCLampParams.y) * edge);
            }

            // Lamp light over one step of the march, from `tStart` for `segment` metres: four
            // looks spread along it rather than one, because a street lamp's glow is a few
            // metres across and a step 200 m out is tens of metres long -- one sample would hit
            // or miss it by chance and the glow would sparkle.
            float3 LampLight(float3 origin, float3 dir, float tStart, float segment, float jitter)
            {
                float3 sum = 0;

                [unroll]
                for (int k = 0; k < 4; k++)
                    sum += LampLightAt(origin + dir * (tStart + segment * (((float)k + jitter) * 0.25)));

                return sum * (0.25 * _VCLampParams.x);
            }

            // Fog lying on the ground, along the ray up to tEnd. Same return convention as the
            // other two.
            //
            // A LEVEL fog is a slab, and the part of the ray inside it is an intersection.
            //
            // One that follows the terrain is not. There the ray is walked coarsely -- one
            // height lookup a step -- until it first comes within the layer's top of the ground,
            // and the real march starts from the step before. From above that puts every sample
            // in the last stretch before the ground; from inside the fog it starts at the camera.
            // Either way only the stretch under _FogCeiling is looked at: above it there is none.
            //
            // `camAbove` is the camera's height over FogGround(origin), which frag already has.
            //
            // `toArch` (1.3.0): what the fog leaves of the view up to each of the rainbow arch's
            // crossings `archT` -- which frag dims the arch by, so a fog bank in front of a shower
            // hides its rainbow (the author: "make sure the rainbow isn't 'over' the fog").
            float4 MarchFog(float3 origin, float3 dir, float tEnd, float camAbove, float jitter, float cosTheta, float2 archT, out float2 toArch)
            {
                toArch = 1.0;
                if (tEnd <= 0.0)
                    return float4(0, 0, 0, 1);

                float top = _FogBase + _FogHeight;
                bool fromInside = InFogLayerAt(origin, camAbove);
                float tStart = -1.0;

                // Valley fog stands up to this much taller (1.5.0, "Fog fills valleys"; 0 without):
                // the walks below look for it that much higher. Its ceiling is _FogCeiling's still.
                float reach = top + _FogValleyDepth;

                if (_FogFollowGround < 0.5)
                {
                    float dy = abs(dir.y) < 1e-4 ? (dir.y < 0 ? -1e-4 : 1e-4) : dir.y;
                    float tA = (_FogLevel + _FogBase - origin.y) / dy;
                    float tB = (_FogLevel + top - origin.y) / dy;
                    tStart = max(min(tA, tB), 0.0);
                    tEnd = min(tEnd, max(tA, tB));
                }
                else
                {
                    // Nothing of a fog that follows the ground is above _FogCeiling, the highest
                    // ground on the map plus the layer's top, so a ray has only its stretch under
                    // that to look at: one coming down from above starts where it enters it, one
                    // climbing stops where it leaves it at the latest. (Until 1.3.1 a ray from above
                    // the layer was not marched at all going up, and one from inside stopped as if
                    // the ground were level: the fog on higher hills was cut off at one height.)
                    float tLow = 0.0;
                    if (dir.y > 1e-4)
                        tEnd = min(tEnd, (_FogCeiling - origin.y) / dir.y);
                    else if (dir.y < -1e-4)
                        tLow = max((_FogCeiling - origin.y) / dir.y, 0.0);

                    if (tEnd <= tLow)
                        return float4(0, 0, 0, 1);

                    if (camAbove < reach)
                    {
                        tStart = 0.0;

                        // Under a raised layer, looking up: there is nothing before the ray reaches
                        // the underside. Where that is assumes level ground from here, so the start
                        // is pulled well in for ground that falls away ahead. (Over the layer's top
                        // but under a valley's, the march starts here.)
                        if (!fromInside && camAbove < _FogBase && dir.y > 0.02)
                            tStart = 0.6 * (_FogBase - camAbove) / dir.y;

                        // A climbing ray leaves the layer over level ground for good soon after: what
                        // is left of it above where the ray starts, and half as much again plus 60 m
                        // for ground that rises under it. Past that only a HILL brings it back within
                        // the layer, so the rest of its stretch is looked at coarsely -- one height
                        // lookup a look -- and the march reaches past the last place it is. With level
                        // ground all round it stops where it always did, its steps as fine.
                        if (dir.y > 0.02)
                        {
                            float startAbove = max(camAbove, 0.0) + tStart * dir.y;
                            float tLevel = tStart + (reach - startAbove + top * 0.5 + 60.0) / dir.y;
                            if (tLevel < tEnd)
                            {
                                float stride = (tEnd - tLevel) / 14.0;
                                float tFar = tLevel;

                                [loop]
                                for (int c = 1; c <= 14; c++)
                                {
                                    float t = tLevel + stride * (float)c;
                                    float3 p = origin + dir * t;

                                    // Within the top here, or within what the ray climbs between two
                                    // looks of it: a hill between them.
                                    if (p.y - GroundAt(p) < reach + dir.y * stride)
                                        tFar = min(t + stride, tEnd);
                                }

                                tEnd = tFar;
                            }
                        }
                    }
                    else
                    {
                        float tBefore = tLow;
                        float span = tEnd - tLow;

                        [loop]
                        for (int c = 1; c <= 14; c++)
                        {
                            // Finer near where it starts, where a missed patch would be seen.
                            float f = (float)c / 14.0;
                            float t = tLow + span * f * f;
                            float3 p = origin + dir * t;

                            // Within the top here -- or, climbing, within what the ray climbed since
                            // the last look: a hill between two looks.
                            if (p.y - GroundAt(p) < reach + max(dir.y, 0.0) * (t - tBefore))
                            {
                                tStart = tBefore;
                                break;
                            }

                            tBefore = t;
                        }
                    }
                }

                if (tStart < 0.0 || tStart >= tEnd)
                    return float4(0, 0, 0, 1);

                // Whole steps (CloudVolume.ApplyFog: Quality^2 / 160) for a stretch of up to 9 km,
                // and more for a longer one -- a Fog distance beyond 9 km (1.3.1) -- so its steps
                // are as long as at 9 km: the quadratic spacing's step at a distance grows with the
                // square root of the stretch. Up to 9 km, the steps it always had.
                float len = tEnd - tStart;
                float steps = clamp(round(_FogSteps * max(1.0, sqrt(len / FOG_STEP_STRETCH))), 6.0, 96.0);

                // Fog glows around the sun: a strong forward lobe over a flat base.
                float phase = 0.35 + 0.65 * min(HenyeyGreenstein(cosTheta, 0.65), 6.0);
                float strength = FogStrength();

                float transmittance = 1.0;
                float3 light = 0;
                float tPrev = tStart;

                // 96: the Quality setting's 96^2 / 160 = 58 at most, times up to 1.49 for a 20 km
                // stretch (1.3.1; 64 in 1.3.0, 40 before).
                [loop]
                for (int s = 0; s < 96; s++)
                {
                    if ((float)s >= steps || transmittance < 0.02)
                        break;

                    // Segments lengthen with distance from where the ray meets the fog: it is the
                    // first few hundred metres of fog that are seen, the rest lies behind them.
                    // (Until 1.2.1 a ray from OUTSIDE took even steps, "the stretch is short" --
                    // but at a shallow angle it runs for kilometres, and dense fog stopped the
                    // march after a few steps hundreds of metres long, each landing on a random
                    // point of 85 m wisps: the grain.)
                    float f = ((float)s + 1.0) / steps;
                    float tNext = tStart + len * f * f;
                    float segment = tNext - tPrev;
                    float t = tPrev + segment * jitter;

                    float3 p = origin + dir * t;
                    float above = p.y - FogGround(p);
                    float layerHeight;
                    float d = FogAt(p, above, true, layerHeight);

                    if (d > 0.001)
                    {
                        float fade = FogFade(t);
                        float sigma = strength * d * fade;

                        // Self-shadowing: how much fog lies between here and the sun, from one
                        // look a short way towards it. This is what gives the billows form --
                        // lit tops and flanks, dim hollows -- instead of an even glow.
                        float3 sunward = p + _SunDir * (_FogHeight * 0.4);
                        float sunwardHeight;
                        float shade = FogAt(sunward, sunward.y - FogGround(sunward), false, sunwardHeight);
                        float sun = exp(-shade * strength * _FogHeight * 0.9);

                        // Lit by how high in the layer it is -- the valley's taller layer where there
                        // is one (1.5.0).
                        float h = saturate((above - _FogBase) / layerHeight);
                        float3 lit = _FogAmbient * (0.55 + 0.45 * h)
                                   + _FogSun * (SunShaft(p) * sun * phase);

                        if (_FlashCount > 0.5)
                            lit += Lightning(p) * 0.5;

                        // The city's lights, in the fog round them. Night only, and only
                        // within the map round the camera (FogLampMap).
                        if (_VCLampParams.z > 0.5 && t < _VCLampParams.w)
                            lit += LampLight(origin, dir, tPrev, segment, jitter);

                        // The fog before the arch: up to each crossing, or all of this step.
                        toArch = archT > tPrev ? transmittance * exp(-sigma * min(archT - tPrev, segment)) : toArch;

                        float stepT = exp(-sigma * segment);
                        light += transmittance * lit * (1.0 - stepT);
                        transmittance *= stepT;
                    }

                    tPrev = tNext;
                }

                // The march stopped, or the fog ended, before a crossing: all of it is in front.
                toArch = archT >= tPrev ? transmittance : toArch;

                return float4(light, transmittance);
            }

            // "a in front of b", both as premultiplied light + remaining transmittance.
            float4 Over(float4 a, float4 b)
            {
                return float4(a.rgb + a.a * b.rgb, a.a * b.a);
            }

            float4 frag(v2f i) : SV_Target
            {
                // Smoother clouds' blend (mode 2): no march at all.
                if (_TemporalMode > 1.5)
                    return TemporalResolve(i.screenPos.xy, i.worldPos);

                bool marchOnly = _TemporalMode > 0.5;
                float3 origin = marchOnly ? _TemporalEye.xyz : _WorldSpaceCameraPos;
                float3 dir = normalize(i.worldPos - origin);

                // Slab intersection. tA is where the ray crosses the cloud base.
                float dy = abs(dir.y) < 1e-4 ? (dir.y < 0 ? -1e-4 : 1e-4) : dir.y;
                float tA = (_CloudBottom - origin.y) / dy;
                float tB = (_CloudTop - origin.y) / dy;
                float tEnter = max(min(tA, tB), 0.0);
                float tExit = min(max(tA, tB), _MaxDistance);

                // Smoother clouds' march (mode 1): the clouds alone, before the distance fade, through
                // the whole slab -- no scene (the camera's draw marches every pixel where the scene stands
                // in the slab itself), no bolt -- with the jitter moved on every frame so the blend
                // averages it away.
                if (marchOnly)
                {
                    float2 texelPos = i.screenPos.xy * _TemporalSize.xy;
                    float shifted = frac(MarchJitter(texelPos) + _TemporalJitter);
                    float4 raw = MarchCloudsRaw(origin, dir, tEnter, tExit, 1e9, shifted, dot(dir, _SunDir));
                    return float4(raw.rgb, 1.0 - raw.a);
                }

                // The slab's own end, before the scene cuts it: what smoother clouds marched to.
                float tExitClear = tExit;

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

                // On a lightning bolt's own pixels the clouds, rain and fog end a little behind it
                // (LightningBoltMap.cginc, 1.5.0): the cloud it comes out of still covers its top, a
                // cloud far behind it no longer cuts it. 1e9 everywhere else and with no bolt showing.
                float boltStop = BoltStop(origin, dir);

                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float2 pixel = screenUV * _ScreenParams.xy;
                float jitter = MarchJitter(pixel);
                float cosTheta = dot(dir, _SunDir);

                // A part left out of this draw is empty (no light, all passed through), which
                // leaves the other one exactly as it is in the Over below.
                bool drawClouds = _DrawPart < 1.5;
                bool drawAir = _DrawPart < 0.5 || _DrawPart > 1.5;

                float4 clouds = float4(0, 0, 0, 1);
                if (drawClouds)
                {
                    // Smoother clouds (Sky/CloudTemporal.cs): the blend, where it is the same as a march
                    // here would be -- the scene ends the ray past the slab (or never), and no bolt stops
                    // it. Where a building or a hill stands inside the slab, or a bolt shows, a march.
                    // Only the camera it was made for reads it: the one standing where it was marched from.
                    // (A flag set per camera before its draw was not enough: another camera culled between
                    // the main camera's preparation and its draw would turn it off.)
                    float3 fromEye = origin - _TemporalEye.xyz;
                    bool temporalCamera = _TemporalOn > 0.5 && dot(fromEye, fromEye) < 1.0;

                    float4 raw = float4(0, 0, 0, -1);
                    if (temporalCamera && tExit > tEnter && sceneDist >= tExitClear && boltStop >= 1e9)
                        raw = TemporalClouds(dir);

                    clouds = raw.a >= 0.0
                        ? FinishClouds(raw, tEnter, dir, sceneDist >= 1e9)
                        : MarchClouds(origin, dir, tEnter, tExit, boltStop, jitter, cosTheta, sceneDist >= 1e9);

                    // TEMPORARY test switch: which way each cloud pixel was drawn.
                    if (_TemporalTint > 0.5 && tExit > tEnter)
                        clouds.rgb *= raw.a >= 0.0 ? float3(1.0, 0.3, 0.3) : float3(0.3, 0.3, 1.0);
                }

                // The far deck, behind them, wherever there is sky (from any height: until 1.5.0's second
                // trial only from under the base, and it vanished as the camera rose into the clouds).
                if (drawClouds && _FarDeck.x > 0.0 && sceneDist >= 1e9)
                    clouds = Over(clouds, MarchFarDeck(origin, dir, cosTheta, boltStop));

                // The part of the ray under the cloud base: from the camera when it is below
                // the base, otherwise from where the ray comes down through it.
                float4 rain = float4(0, 0, 0, 1);
                bool cameraBelowBase = origin.y < _CloudBottom;
                float rStart = cameraBelowBase ? 0.0 : (dir.y < 0.0 ? tA : 1e9);
                float rEnd = (cameraBelowBase && dir.y > 0.0) ? tA : 1e9;

                // With depth occlusion off there is no ground to stop at; use sea level.
                if (dir.y < 0.0)
                    rEnd = min(rEnd, (_RainFloor - origin.y) / dy);

                rEnd = min(rEnd, min(min(sceneDist, boltStop), _RainMaxDistance));

                // The rainbow's arch: where this ray crosses it, before the marches, which say how
                // much rain and fog stands in front of each crossing.
                bool arch = drawAir && _BowParams.z > 0.5;
                float2 archT = arch ? ArchCrossings(origin, dir) : float2(-1.0, -1.0);
                float2 rainToArch = 1.0;

                if (drawAir && _RainAmount > 0.001 && _RainShaftDensity > 0.0)
                    rain = MarchRain(origin, dir, rStart, rEnd, jitter, cosTheta, archT, rainToArch);

                // MarchFog finds its own stretch of the ray, level fog or draped; all it needs is
                // where the ray ends (and where the arch is, to say how much fog is in front of it).
                float4 fog = float4(0, 0, 0, 1);
                float2 fogToArch = 1.0;
                bool cameraInFog = false;
                if (drawAir && _FogAmount > 0.001 && _FogDensity > 0.0)
                {
                    float camAbove = origin.y - FogGround(origin);
                    cameraInFog = InFogLayerAt(origin, camAbove);

                    // A ray that starts above a LEVEL fog and never descends will not meet it. One
                    // that follows the ground can drape a hill higher than the camera, so there only
                    // a camera above _FogCeiling skips those rays (1.3.1: until then all were
                    // skipped, and a hill's fog ended at eye level). One from UNDER a raised layer
                    // can meet either.
                    if (camAbove < _FogBase + _FogHeight || dir.y < 0.0 || (_FogFollowGround > 0.5 && origin.y < _FogCeiling))
                    {
                        float fEnd = min(min(sceneDist, boltStop), _FogMaxDistance);

                        // With depth occlusion off there is no ground to stop at; use the
                        // lowest ground on the map.
                        if (dir.y < 0.0)
                            fEnd = min(fEnd, (_FogFloor - origin.y) / dy);

                        fog = MarchFog(origin, dir, fEnd, camAbove, jitter, cosTheta, archT, fogToArch);
                    }
                }

                // Clouds never overlap the other two along a ray, so they composite exactly:
                // "nearer over farther". Fog and rain DO share the air under the clouds; they
                // are marched apart (each needs its samples in a different place) and layered
                // by whichever the camera is inside, which is what dominates the view there.
                float4 lower = cameraInFog ? Over(fog, rain) : Over(rain, fog);

                // The arch stands in the rain but is layered apart from it: dimmed by exactly the rain
                // and fog in front of it (1.3.0; the author: "make sure the rainbow isn't 'over' the
                // fog but rather behind it"). With the rain it would go over a fog bank standing
                // between the camera and the shower whenever the camera is out of the fog.
                if (arch)
                {
                    float2 inFront = rainToArch * fogToArch;
                    lower.rgb += ArchLight(origin, dir, archT.x, rEnd) * inFront.x
                               + ArchLight(origin, dir, archT.y, rEnd) * inFront.y;
                }

                float4 result = cameraBelowBase ? Over(lower, clouds) : Over(clouds, lower);

                return float4(result.rgb, 1.0 - result.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
