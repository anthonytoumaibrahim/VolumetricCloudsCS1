// FUNNELS (1.5.0): our waterspouts (Sky/Waterspouts.cs; Natural Disasters' tornadoes were drawn here
// too until 2026-10-19, kept in the plan), marched in the AIR part of the cloud pass with the rain and
// the fog, so what stands in front of a funnel and behind it composites exactly (a draw of its own
// would be fogged as if it stood on the ground behind it -- the bolt's problem). Sky/FunnelShape.cs is
// the C# twin of the shape below: change one, change the other.
//
// A funnel is a soft tube round a curved axis from the ground up into the cloud it hangs from,
// narrow at the bottom and flaring into a collar under the base, made the fog's way (2026-10-18, the
// author: "too opaque ... I'd like it to look kind of like our volumetric fog ... wispy/more natural"):
// billows of the 3D cloud noise inside a soft shape, a swirl bending them, wisps eating its edges, in
// a turning, rising frame. Under the tip of one that stops part way, a faint wavering thread to the
// surface ("the connection with the sea is a very light/small"); round its foot a skirt -- a spray
// ring on water, a dust whirl on land.
//
// The march is guided by distance: outside a funnel's parts it jumps by how far it is from them
// (no texture read), inside it takes short steps. A thin funnel far away costs a few pixel columns.
// Only in CloudRaymarch.shader's VC_FUNNELS variant; included after CloudRadiance.
//
// It meets the cloud as part of it (2026-10-19, round 3 "both fully in front of the clouds now"):
// from under the base the clouds are drawn first, so what of a funnel stands up inside the cloud is
// dimmed here by the cloud in front of it (MarchFunnel's `shown`); and where it meets the base it is
// lit as the cloud's base is there (CloudRadiance), lower down as the fog is.

#ifndef VOLUMETRIC_CLOUDS_FUNNEL_INCLUDED
#define VOLUMETRIC_CLOUDS_FUNNEL_INCLUDED

#define MAX_FUNNELS 4
#define FUNNEL_TIP 0.15             // FunnelShape.TipLength
#define FUNNEL_COLLAR 0.7           // FunnelShape.CollarStart
#define FUNNEL_COLLAR_CURVE 2.2     // FunnelShape.CollarCurve
#define FUNNEL_NOISE_METRES 200.0   // FunnelShape.NoiseMetres
#define FUNNEL_WALL 0.2             // FunnelShape.WallStart
#define FUNNEL_THREAD_RADIUS 0.35   // FunnelShape.ThreadRadius
#define FUNNEL_THREAD_DENSITY 0.8   // FunnelShape.ThreadDensity
#define FUNNEL_DEPTH 2.5            // FunnelShape.MaxDepth
#define FUNNEL_LIGHT_FROM 0.4       // FunnelShape.CloudLightFrom
#define FUNNEL_STEPS 48

float _FunnelCount;
float4 _FunnelGround[MAX_FUNNELS];      // xyz = where the axis meets the ground or water, w = radius there
float4 _FunnelTop[MAX_FUNNELS];         // xyz = where it meets the cloud base, w = radius under the collar
float4 _FunnelShape[MAX_FUNNELS];       // x = the collar's extra radius, y = exponent, z = where the visible
                                        // funnel ends (share of the height), w = hollow
float4 _FunnelBend[MAX_FUNNELS];        // xy = the axis's bow at mid-height (x, z), z = opacity,
                                        // w = extinction per metre
float4 _FunnelSkirt[MAX_FUNNELS];       // x = radius, y = height, z = ring (0 whirl .. 1 ring), w = strength
float4 _FunnelSkirtColor[MAX_FUNNELS];  // rgb = albedo, a = extinction per metre
float4 _FunnelColor[MAX_FUNNELS];       // rgb = albedo, a = how much of the skirt's colour near the ground
float4 _FunnelSpin[MAX_FUNNELS];        // x = turn (radians), y = rise (noise tiles), z = seed, w = twist
float4 _FunnelBound[MAX_FUNNELS];       // xy = the level circle every part stands in (centre x, z), z = its
                                        // radius, w = how far its edge moves sideways per metre of height
float4 _FunnelLook[MAX_FUNNELS];        // x = the thread (0..1), y = metres up into the cloud, z = wisps (0..1)
float4 _FunnelParams;                   // x = a pixel's angle, y = how far they are drawn, z = fade length
float3 _FunnelAmbient;                  // the air's light, as the fog has it (without the fog's own knobs)
float3 _FunnelSun;                      // the fog's sun

float2 FunnelTurn(float2 v, float a)
{
    float c = cos(a);
    float s = sin(a);
    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

// The radius at height share s before the tip: ground to top by the exponent, and the collar.
float FunnelBodyRadius(int i, float s)
{
    float collar = pow(max(saturate((s - FUNNEL_COLLAR) / (1.0 - FUNNEL_COLLAR)), 1e-6), FUNNEL_COLLAR_CURVE);
    return _FunnelGround[i].w + (_FunnelTop[i].w - _FunnelGround[i].w) * pow(max(saturate(s), 1e-6), max(_FunnelShape[i].y, 0.05))
         + _FunnelShape[i].x * collar;
}

float2 FunnelAxis(int i, float s)
{
    return lerp(_FunnelGround[i].xz, _FunnelTop[i].xz, s) + _FunnelBend[i].xy * (4.0 * s * (1.0 - s));
}

// The tube at x (its share of the radius), `unit` = from its axis over its radius (FunnelShape.Tube).
// `height`: what the noise's vertical scale is measured over (the thread's is shorter: finer).
float FunnelTube(int i, float2 unit, float x, float s, float height)
{
    float wall = 1.0 - smoothstep(FUNNEL_WALL, 1.0, x);
    // Hollow down the middle, but full where it meets the cloud (2026-10-18: a thin, hollow collar read as
    // "an empty space right under the cloud").
    float hollowness = _FunnelShape[i].w * (1.0 - smoothstep(0.6, 0.95, s));
    float hollow = 1.0 - hollowness * (1.0 - smoothstep(0.1, 0.6, x));
    float shaped = wall * hollow;

    float4 spin = _FunnelSpin[i];
    float2 w = FunnelTurn(unit, spin.x + spin.w * s);
    float3 q = float3(w.x * 0.35 + spin.z * 7.13, s * (height / FUNNEL_NOISE_METRES) - spin.y + spin.z * 3.31, w.y * 0.35 + spin.z * 5.37);

    // The swirl (the fog's): a slower look of the same noise bends the lookup. The noise's red and
    // green are gamma-decoded as read (CloudCommon): their square roots are near the values made.
    float2 swirl = sqrt(tex3Dlod(_NoiseTex, float4(q * 0.5 + float3(0.13, 0.29, 0.71), 0)).rg) - 0.5;
    q += float3(swirl.x * 0.25, swirl.x * 0.3, swirl.y * 0.25);

    float2 n = sqrt(tex3Dlod(_NoiseTex, float4(q, 0)).rg);
    float d = shaped * saturate((n.r - (1.0 - shaped)) / max(shaped, 1e-4));
    return d * lerp(1.0, n.g, _FunnelLook[i].z * (0.4 + 0.6 * x));
}

// The funnel's density at p, 0..1 (FunnelShape.StemDensity): the tube, the thread under its tip, and
// up into the cloud fading out. Also how high p is (s, a share of the height to the base). Never
// thicker across than FUNNEL_DEPTH (extinction x radius): a wide collar is as see-through as a thick
// fog, not a solid (round 3: a white goblet).
float FunnelStem(int i, float3 p, float footprint, out float s)
{
    float height = max(_FunnelTop[i].y - _FunnelGround[i].y, 1.0);
    float above = _FunnelLook[i].y / height;
    s = (p.y - _FunnelGround[i].y) / height;
    if (s < 0.0 || s > 1.0 + above)
        return 0.0;

    float sc = min(s, 1.0);
    float tip = sqrt(saturate((sc - _FunnelShape[i].z) / FUNNEL_TIP));
    float body = FunnelBodyRadius(i, sc);
    float2 v = p.xz - FunnelAxis(i, sc);
    float r = body * tip;
    float scale = 1.0;
    float vertical = height;

    // With a thread, ONE tube: the funnel narrows into it (not to a point with a rope beside it: "the
    // transition ... is a bit awkward"), thinning as it goes, wavering more the further below the tip,
    // patchier (a finer noise along it), a little stronger again just over the surface.
    float thread = _FunnelLook[i].x;
    if (thread > 0.0)
    {
        float bottom = _FunnelShape[i].z;
        r = lerp(FUNNEL_THREAD_RADIUS * _FunnelGround[i].w, body, tip);
        float a = _FunnelSpin[i].x;
        v -= float2(sin(sc * 9.0 + a), cos(sc * 7.0 + a + 1.3)) * (0.5 * _FunnelGround[i].w * (1.0 - tip));
        float along = FUNNEL_THREAD_DENSITY * thread * (0.35 + 0.65 * smoothstep(bottom - 0.3, bottom, sc))
                    + 0.3 * thread * (1.0 - smoothstep(0.0, 0.08, sc));
        scale = lerp(along, 1.0, tip);
        vertical = height * lerp(3.0, 1.0, tip);
    }

    if (r <= 0.0)
        return 0.0;

    // Drawn wider than it is (a pixel at the least), it is thinner by as much, evenly.
    float wide = max(r, 0.8 * footprint);
    float x = length(v) / wide;
    if (x >= 1.0)
        return 0.0;

    // Fading out only in the upper part of its reach into the cloud (which hides it from below).
    float depth = min(1.0, FUNNEL_DEPTH / max(_FunnelBend[i].w * r, 1e-4));
    float density = FunnelTube(i, v / wide, x, sc, vertical) * (r / wide) * scale * depth;
    if (s > 1.0)
        density *= 1.0 - smoothstep(0.4, 1.0, (s - 1.0) / max(above, 1e-4));
    return density;
}

// The skirt's density at p, 0..1 (FunnelShape.SkirtDensity): a ring widening upwards (spray) or a
// full whirl (dust), thinning to its top.
float FunnelSkirt(int i, float3 p)
{
    float4 skirt = _FunnelSkirt[i];
    float z = (p.y - _FunnelGround[i].y) / max(skirt.y, 1.0);
    if (z < -0.05 || z > 1.0)
        return 0.0;

    float2 v = (p.xz - _FunnelGround[i].xz) / max(skirt.x, 1.0);
    float x = length(v);
    if (x >= 1.0)
        return 0.0;

    float centre = lerp(0.25, 0.75, sqrt(saturate(z)));
    float d = (x - centre) / 0.18;
    float band = exp(-d * d);
    float full = 1.0 - smoothstep(0.2, 1.0, x);
    float shaped = lerp(full, band, skirt.z) * pow(max(1.0 - saturate(z), 1e-6), 1.5) * smoothstep(-0.05, 0.05, z);

    float4 spin = _FunnelSpin[i];
    float2 w = FunnelTurn(v, 2.0 * spin.x);
    float3 q = float3(w.x * 0.6 + spin.z * 2.91, z * 0.5 - 2.0 * spin.y + spin.z, w.y * 0.6 + spin.z * 1.73);
    float n = sqrt(tex3Dlod(_NoiseTex, float4(q, 0)).g);

    return skirt.w * shaped * saturate((n - (1.0 - shaped)) / max(shaped, 1e-4));
}

// Where this ray meets funnel i's level circle, between its foot and where it fades into the cloud:
// [t0, t1], t1 <= t0 for none.
float2 FunnelSpan(int i, float3 origin, float3 dir, float tEnd)
{
    float2 o = origin.xz - _FunnelBound[i].xy;
    float2 d = dir.xz;
    float radius = _FunnelBound[i].z;
    float a = dot(d, d);
    float b = dot(o, d);
    float c = dot(o, o) - radius * radius;
    float t0 = 0.0;
    float t1 = 1e9;

    if (a > 1e-8)
    {
        float disc = b * b - a * c;
        if (disc <= 0.0)
            return float2(1.0, 0.0);
        float root = sqrt(disc);
        t0 = (-b - root) / a;
        t1 = (-b + root) / a;
    }
    else if (c > 0.0)
    {
        return float2(1.0, 0.0);
    }

    float low = _FunnelGround[i].y - 0.05 * max(_FunnelSkirt[i].y, 1.0) - 1.0;
    float high = _FunnelTop[i].y + _FunnelLook[i].y + 1.0;
    if (abs(dir.y) > 1e-5)
    {
        float ya = (low - origin.y) / dir.y;
        float yb = (high - origin.y) / dir.y;
        t0 = max(t0, min(ya, yb));
        t1 = min(t1, max(ya, yb));
    }
    else if (origin.y < low || origin.y > high)
    {
        return float2(1.0, 0.0);
    }

    return float2(max(t0, 0.0), min(t1, tEnd));
}

// The nearest of the funnels' spans along this ray (1e9 for none): where the rain and the fog are split
// into in front of the funnels and behind them. `nearest`: which funnel that is.
float FunnelsEntry(float3 origin, float3 dir, float tEnd, out int nearest)
{
    float first = 1e9;
    nearest = 0;

    [loop]
    for (int i = 0; i < MAX_FUNNELS; i++)
    {
        if ((float)i >= _FunnelCount)
            break;

        float2 span = FunnelSpan(i, origin, dir, tEnd);
        if (span.y > span.x && span.x < first)
        {
            first = span.x;
            nearest = i;
        }
    }

    return first;
}

// The cloud's own light a little up into its base over funnel i: what a funnel takes as it comes up
// into it (the march's CloudRadiance, so the two meet in the same light). Once a pixel, for the
// nearest funnel on it, at the START of the pixel's work: called after the rain and the fog, while
// their results were held, it cost the AMD compiler a third more registers (168 against 128).
float3 FunnelBaseLight(int i, float t, float cosTheta)
{
    float3 top = float3(_FunnelTop[i].x, _CloudBottom + 0.05 * (_CloudTop - _CloudBottom), _FunnelTop[i].z);
    return CloudRadiance(top, CloudLod(t), LightAlong(cosTheta), false);
}

// The cloud's optical depth along the ray from u0 to u1, inside the slab: `looks` reads of the
// clouds' own density (the march's, detail and all), each standing for its share of the stretch.
float FunnelCloudDepth(float3 origin, float3 dir, float u0, float u1, int looks)
{
    float tau = 0.0;
    float stretch = (u1 - u0) / looks;

    [loop]
    for (int k = 0; k < looks; k++)
    {
        float u = u0 + stretch * (k + 0.5);
        tau += SampleDensityLod(origin + dir * u, true, CloudLod(u)) * stretch;
    }

    return tau * _Absorption;
}

// Funnel i along [t0, t1]: light in rgb, in a what of the background is left. `phase`: the ray's
// forward scattering in the air; `baseLight`: the cloud's own light in its base over the funnel.
float4 MarchFunnel(int i, float3 origin, float3 dir, float t0, float t1, float jitter, float phase, float3 baseLight)
{
    float3 ground = _FunnelGround[i].xyz;
    float height = max(_FunnelTop[i].y - ground.y, 1.0);
    float level = length(dir.xz);
    float closing = level + _FunnelBound[i].w * abs(dir.y) + 1e-4;
    float4 skirt = _FunnelSkirt[i];
    bool skirtOn = skirt.w > 0.001;
    float stemSigma = _FunnelBend[i].w * _FunnelBend[i].z;
    float skirtSigma = _FunnelSkirtColor[i].a * skirt.w;
    // The visible funnel starts here: under it there is only the skirt -- and the thread, all the way down.
    float low = _FunnelLook[i].x > 0.001 ? ground.y : ground.y + max(_FunnelShape[i].z, 0.0) * height;
    float fadeStart = _FunnelParams.y - _FunnelParams.z;

    // The sun's way out of the tube is level for the chord below (the sun's height is dealt with apart).
    float sunLevel = length(_SunDir.xz);
    float2 sunAcross = sunLevel > 1e-3 ? _SunDir.xz / sunLevel : float2(1.0, 0.0);

    // The cloud shadow and a flash's light at the funnel, looked up once where the ray first meets it:
    // a funnel stands under one cloud, and a look a step costs a texture read a step.
    float shaft = -1.0;
    float3 flash = 0;

    // From under the base the clouds are already drawn: what of the funnel is up inside the cloud
    // shows only as much as the cloud in front of it lets through -- its depth from where the ray
    // came up through the base, carried along the march, with the clouds' fade into the distance.
    bool underBase = origin.y < _CloudBottom && dir.y > 1e-4;
    float tBase = underBase ? (_CloudBottom - origin.y) / dir.y : 1e9;
    float presence = underBase ? CloudPresence(tBase) : 0.0;
    float cloudTau = 0.0;
    float cloudTo = tBase;

    float3 light = 0;
    float transmittance = 1.0;
    float shown = 0.0;
    float t = t0;

    [loop]
    for (int k = 0; k < FUNNEL_STEPS; k++)
    {
        if (t >= t1 || transmittance < 0.02)
            break;

        float3 p = origin + dir * t;
        float footprint = t * _FunnelParams.x;

        // How far along the ray each part still is (0: inside it). The tube by its body's radius
        // (never less than the tip's or the thread's), the skirt by its cylinder.
        float s = saturate((p.y - ground.y) / height);
        float body = max(FunnelBodyRadius(i, s), 0.8 * footprint);
        float stemGap = (length(p.xz - FunnelAxis(i, s)) - body) / closing;
        float under = low - p.y;
        float stemStep = max(stemGap, under > 0.0 ? (dir.y > 1e-4 ? under / dir.y : 1e9) : 0.0);

        float skirtStep = 1e9;
        if (skirtOn)
        {
            float z = p.y - ground.y;
            float across = (length(p.xz - ground.xz) - skirt.x) / max(level, 1e-4);
            float over = z - skirt.y;
            float below = -0.05 * skirt.y - z;
            float upDown = over > 0.0 ? (dir.y < -1e-4 ? over / -dir.y : 1e9)
                         : below > 0.0 ? (dir.y > 1e-4 ? below / dir.y : 1e9) : 0.0;
            skirtStep = max(max(across, 0.0), upDown);
        }

        float skip = min(stemStep, skirtStep);
        if (skip > 0.0)
        {
            t += max(skip * 0.95, 0.5);
            continue;
        }

        // Inside one or both: a short step (finer when the ray runs along it), looked at a jittered way into it.
        float dt = 1e9;
        if (stemStep <= 0.0)
            dt = min(dt, min(clamp(0.2 * body / max(level, 0.3), 0.5, 40.0), 25.0 / max(abs(dir.y), 0.15)));
        if (skirtStep <= 0.0)
            dt = min(dt, clamp(0.2 * min(skirt.x, skirt.y), 0.5, 40.0));
        dt = min(dt, t1 - t);

        float3 q = p + dir * (dt * jitter);
        float fade = 1.0 - smoothstep(fadeStart, _FunnelParams.y, t);

        float stemS = 0.0;
        float dStem = stemStep <= 0.0 ? FunnelStem(i, q, footprint, stemS) : 0.0;
        float dSkirt = skirtStep <= 0.0 && skirtOn ? FunnelSkirt(i, q) : 0.0;
        float sigmaStem = dStem * stemSigma * fade;
        float sigmaSkirt = dSkirt * skirtSigma * fade;
        float sigma = sigmaStem + sigmaSkirt;

        if (sigma > 1e-6)
        {
            // Up inside the cloud, from under it: as much of it as the cloud in front lets through.
            // Once that is next to nothing, the rest of this funnel is deeper in: done.
            float shownHere = 1.0;
            if (underBase && q.y > _CloudBottom)
            {
                float u = t + dt * jitter;
                if (u > cloudTo)
                {
                    cloudTau += FunnelCloudDepth(origin, dir, cloudTo, u, (int)clamp(ceil((u - cloudTo) / 150.0), 1.0, 4.0));
                    cloudTo = u;
                }

                shownHere = 1.0 - presence * (1.0 - exp(-cloudTau));
                if (shownHere < 0.02)
                    break;
            }

            if (shaft < 0.0)
            {
                shaft = SunShaft(q);
                if (_FlashCount > 0.5)
                    flash = Lightning(q) * 0.6;
            }

            float3 radiance = 0;

            if (sigmaStem > 0.0)
            {
                // Its self-shadow: the sun's way out of the tube (as wide as it is there), level, then up
                // to the base at most.
                float2 fromAxis = q.xz - FunnelAxis(i, min(stemS, 1.0));
                float wide = max(FunnelBodyRadius(i, min(stemS, 1.0)), 1.0);
                float b = dot(fromAxis, sunAcross);
                float c = dot(fromAxis, fromAxis) - wide * wide;
                float through = max(-b + sqrt(max(b * b - c, 0.0)), 0.0) / max(sunLevel, 1e-3);
                through = min(through, max(_FunnelTop[i].y - q.y, 0.0) / max(_SunDir.y, 0.02));
                float tau = through * stemSigma * 0.4;
                float sunT = max(exp(-tau), 0.5 * exp(-0.25 * tau));

                // Low down lit as the fog round it is, dirtier near the ground where it has picked up what
                // is there; higher, more and more in the cloud's own light over it, all of it where it
                // meets the base. (Round 2 greyed only its top 20%, with a guess at the cloud's light: "an
                // empty space" under the cloud. Round 3 took the clouds' sun -- under Real Light's sky x5
                // and more -- all the way up: a glowing white goblet.)
                float dirt = _FunnelColor[i].a * (1.0 - smoothstep(0.0, 0.3, stemS));
                float3 albedo = lerp(_FunnelColor[i].rgb, _FunnelSkirtColor[i].rgb, dirt);
                float3 air = albedo * (_FunnelAmbient + _FunnelSun * (shaft * sunT * phase));
                radiance += sigmaStem * lerp(air, baseLight, smoothstep(FUNNEL_LIGHT_FROM, 1.0, stemS));
            }

            if (sigmaSkirt > 0.0)
                radiance += sigmaSkirt * _FunnelSkirtColor[i].rgb * (_FunnelAmbient * 0.8 + _FunnelSun * (shaft * phase * 0.8));

            radiance = radiance / sigma + flash;

            // What it hides of the background goes with what it shows: behind the cloud, neither.
            float stepT = exp(-sigma * dt);
            float taken = transmittance * (1.0 - stepT) * shownHere;
            light += radiance * taken;
            shown += taken;
            transmittance *= stepT;
        }

        t += dt;
    }

    return float4(light, 1.0 - shown);
}

// Every funnel along the ray up to tEnd, nearest first (the C# side orders them by distance): light
// in rgb, transmittance in a. `baseLight`: FunnelBaseLight for the nearest (they hang from one deck).
float4 MarchFunnels(float3 origin, float3 dir, float tEnd, float jitter, float cosTheta, float3 baseLight)
{
    float4 result = float4(0, 0, 0, 1);
    float phase = 0.6 + 0.4 * min(HenyeyGreenstein(cosTheta, 0.5), 4.0);

    [loop]
    for (int i = 0; i < MAX_FUNNELS; i++)
    {
        if ((float)i >= _FunnelCount || result.a < 0.02)
            break;

        float2 span = FunnelSpan(i, origin, dir, tEnd);
        if (span.y > span.x)
        {
            float4 one = MarchFunnel(i, origin, dir, span.x, span.y, jitter, phase, baseLight);
            result = float4(result.rgb + result.a * one.rgb, result.a * one.a);
        }
    }

    return result;
}

#endif
