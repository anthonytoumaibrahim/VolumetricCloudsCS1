// The air round the clouds: how far the clouds reach before they fade, the rain curtains under
// them, the fog on the ground, and the marches' jitter. The cloud pass marches all of it
// (CloudRaymarch.shader). One definition of each, like the density in CloudCommon: never fork
// them. (1.3.0 shared it with the lightning bolt, which dimmed itself by what lay in front of it;
// since 1.3.1 the bolt is drawn before the clouds again, as in 1.2, and they are laid over it.)
//
// The values are CloudVolume's, set on the cloud material (and copied to the air's draw).
#ifndef VOLUMETRIC_CLOUDS_MEDIA_INCLUDED
#define VOLUMETRIC_CLOUDS_MEDIA_INCLUDED

#include "CloudCommon.cginc"

float _MaxDistance;
float3 _SunDir;        // towards the light

// Night. Clouds dissolve into the distance by going TRANSPARENT, which by day lets
// sky colour through and reads as haze -- and by night lets the stars through.
// _NightOpacity takes that transparency away, out to where the march ends
// (_CloudExtent, the reach), where they still have to end softly. By day it is
// "Clouds hide the sky by day" (1.5.0; 0 unless a player sets it: a cubemap's sky
// and painted sun showed through a full overcast), through twilight to the night's.
float _NightOpacity;
float _CloudExtent;
float _EdgeStart;          // where that soft end starts, a share of _CloudExtent (1.5.0): 0.7 at night, as
                           // always; by day further in, so a deck that hides the sky does not end on a
                           // line above the horizon but thins into the haze behind it

// The same towards the sun, by day: a far cloud faded to 20% lets 20% of the sky behind it
// through, and there that sky is the sun's disc and its glow -- so bright that the clouds burned
// white (the author: "like in real life: the clouds should block the sun"). 1.2.1 to 1.3.x kept the
// far clouds solid in a cone round the sun; that showed as a disc round it, "a huge ring", darkest
// at a low brightness (2026-10-06). Since 1.4.0 they fade as everywhere else, but what dissolves
// lets through only the part of the sky that is NOT the sun's: the game's own sky worked out with
// and without the glow (SkyGlowPass, C# twin Sky/SkyGlow.cs). Smooth everywhere, so no edge.
//
// The game's sky (Hidden/DayNight/Skybox, a uSky): DayNightProperties sets these globally for it
// each frame. Read only -- the cloud material never sets them, so they are always the game's.
float3 _BetaR;
float3 _BetaM;
float3 _MiePhase_g;          // x = the phase's scale, y = 1 + g^2, z = 2g
float3 _MieConst;
float4 _NightZenithColor;
float4 _NightHorizonColor;
float4 _SkyMultiplier;       // x = sunset share, y = the sky's brightness
float2 _ColorCorrection;

float4 _SkyGlow;             // xyz = towards the game's sun (not _SunDir: the moon's at night),
                             // w = the sky's _SunSize (32 / DayNightProperties.m_SunSize)
float4 _SkyGlowMode;         // y = 1 while it applies (0: no game sky; the clouds fade into whatever
                             // is there)

// The game's sky colour from its light, graded as its last lines do -- but never compressed, even
// where its HDR_OFF variant would squeeze it under white: worked out on compressed values, the share
// at the sun was 64% (2026-10-06, a sun 8.6 deg up) and the sun showed clearly through the clouds,
// so the sun reaching the screen is far brighter than white. Uncompressed, the share is exact for an
// HDR sky and for a compressed one holds back more round the sun, never less: a smooth darkening.
float3 SkyToneMap(float3 v)
{
    return pow(max(v * _ColorCorrection.x, 1e-10), _ColorCorrection.y);
}

// The share of the game's sky along `dir` that is not the sun's, 0..1: 1 away from the sun, small
// at its disc. The skybox's own maths, once as it is and once with the sun's disc taken out and
// the Mie glow held at its value 90 degrees from the sun; by brightness, since a blend weight has
// one number for all three colours. The C# twin (SkyGlow.Pass) must stay the same.
float SkyGlowPass(float3 dir)
{
    float c = dot(dir, _SkyGlow.xyz);
    float h = max(dir.y + 0.06, 0.06);
    float hw = max(dir.y, 0.0);
    float sR = 8.0 / h;
    float sM = 1.2 / h;

    float3 x = sR * _NightZenithColor.xyz;
    float3 q = x * (2.0 - x);
    float3 tr = exp(-(_BetaR * sR + _BetaM * sM));
    float3 e = q * tr + _SkyMultiplier.x * ((1.0 - tr) - tr * q);
    float ex = abs(e.x) < 1e-6 ? 1e-6 : e.x;
    float3 mie = sM * e / ex * _MieConst;

    float phase = _MiePhase_g.x * pow(max(_MiePhase_g.y - _MiePhase_g.z * c, 1e-6), -1.5);
    float side = _MiePhase_g.x * pow(max(_MiePhase_g.y, 1e-6), -1.5);
    float ray = (1.0 + c * c) * _SkyMultiplier.y;
    float3 dusk = _SkyGlow.y < 0.25 ? _NightHorizonColor.xyz * q : float3(0.0, 0.0, 0.0);

    float3 full = (0.75 * e + phase * mie) * ray + dusk;
    float3 glowless = (0.75 * e + min(phase, side) * mie) * ray + dusk;
    if (_SkyGlow.y > -0.1)
        full += min(mie, hw) * min(pow(max(_SkyGlow.w * (1.0 - c), 1e-6), -1.5), 1000.0) * tr;

    float3 luma = float3(0.2126, 0.7152, 0.0722);
    float lumFull = dot(SkyToneMap(full), luma);
    float lumGlowless = dot(SkyToneMap(glowless), luma);
    return lumFull > 1e-6 ? saturate(lumGlowless / lumFull) : 1.0;
}

float4 _DetailLight2;   // x = how strongly the cloud above takes the sky light away,
                        // y = growth of the light steps beyond 24 m (to about the layer's
                        // thickness), z = log2(pixel angle / detail texel): lod offset,
                        // w = this light's share (below 1, at a low amount, the old light
                        // is handed over to it: no step at 0%)

// Rain curtains under the cloud base. Where it rains comes from CloudCommon.
float _RainShaftDensity;   // extinction per metre where it rains at full strength
float _RainSteps;
float _RainMaxDistance;
float _RainFloor;
float _RainFall;           // metres fallen so far; slides the curtain pattern down
float _RainCurtainScale;
float3 _RainAmbient;
float3 _RainSun;

// Fog: a CLOUD LAYER LYING ON THE GROUND, not a haze. Two earlier versions -- a
// height-limited blanket, then soft kilometre-wide banks with an exponential
// falloff -- both read as a coating, for the reasons a cloud does not: they were
// translucent, had no boundary, and did not shade themselves. So this one is
// dense, measured from a reference up -- the map's sea level, or with
// _FogFollowGround the GROUND (_TerrainTex) -- to a defined, lumpy top, shaded
// by its own thickness towards the sun, and its billows are pushed around by a
// drifting swirl so they shear, curl and merge instead of sliding past as one
// rigid pattern.
float _FogAmount;          // 0..1: fades the whole thing in and out
float _FogDensity;         // extinction per metre inside the fog
float _FogThreshold;       // weather-field threshold: how much of the map has fog
float _FogTile;            // metres one tile of the weather field spans, for fog
float _FogBoil;            // phase of the swirl, 0..1

// How far the fog has drifted, as each of its lookups sees it: the share of ONE
// repeat of that lookup, 0..1 per axis (CloudFog / Drift.Phase, in doubles on the
// CPU) -- never the drift itself, which grows for the life of the city. The
// tiles come from the same C# constants the phases are worked out against.
float2 _FogPhase;          // placement, over _FogTile
float3 _FogBillowTile;     // metres per repeat of the billow noise (x, y, z)
float2 _FogBillowPhase;
float2 _FogLead;           // metres the top of the layer is carried beyond its base (Drift.FogLead)
float _FogSwirlTile;
float2 _FogSwirlPhase;     // the swirl drifts at 0.55x
float _FogWispTile;
float2 _FogWispPhase;      // the wisps at 1.8x
float _FogPool;            // fog gathers on ground below this level
float _FogFollowGround;    // 1: heights are above the ground. 0: above _FogLevel
float _FogLevel;           // the map's sea level: what a LEVEL fog is measured from
float _FogBase;            // metres from that reference to the layer's underside
float _FogHeight;          // metres from the underside to the top of the layer
float _FogBreakup;         // 0 solid .. 1 wispy
float _FogSteps;
float _FogMaxDistance;     // how far the fog is drawn: the "Fog distance" setting (1.3.1; 9 km before)
float _FogFadeStart;       // where it starts to fade out (0 at 9 km: from the camera on, as always) ...
float _FogFadeLength;      // ... over this many metres, ending at _FogMaxDistance
float _FogCeiling;         // no fog that follows the ground is above this: the highest ground + the top
float3 _FogAmbient;
float3 _FogSun;

sampler2D _TerrainTex;     // TerrainHeightMap: ground (or water) height in metres
float _TerrainMapSize;
float _FogFloor;           // just under the lowest ground on the map

// Where fog gathers by itself (1.5.0, Sky/FogPlaces; TerrainHeightMap.Places, the terrain map's
// layout): r = how far the ground lies below the land round it, metres / 255; g = how much of the
// ground nearby is water. Both rows are uniform branches, off at 0%: the fog as before.
sampler2D _FogPlaceTex;
float _FogValley;          // "Fog fills valleys", 0..1 (0 for a level fog)
float _FogValleyDepth;     // _FogValley x the deepest hollow on the map: how much higher valley fog reaches
float _FogValleyGather;    // how much more of the weather's fog a deep hollow holds: 0.35 x _FogValley,
                           // faded in with the weather's fog (no weather fog, none to gather)
float _FogWater;           // 1 while "Fog over water" has banks to show
float _FogWaterThreshold;  // weather-field threshold for the banks' share of the water
float2 _FogWaterPush;      // metres the banks are carried downwind of the water

float Hash12(float2 p)
{
    return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

// The marches' jitter (Sky/BlueNoise): a tile of blue noise, point-sampled, the value in
// alpha. White noise clumped the steps' error into dots; blue noise spreads it evenly.
sampler2D _BlueNoiseTex;
float _BlueNoiseScale;     // 1 / the tile's size in pixels; 0 until it is there (the hash)

// A global, 0 except while the "grain changes every frame" test switch is on (Sky/CloudTemporal.cs,
// 1.5.0): then this frame's shift, so the same pixel's jitter walks through 0..1 over the frames and a
// temporal anti-aliasing (Render It!'s TAA) can average the grain away. At 0, the jitter as before.
float _VCJitterShift;

float MarchJitter(float2 pixel)
{
    float jitter = _BlueNoiseScale > 0.0
        ? tex2Dlod(_BlueNoiseTex, float4(pixel * _BlueNoiseScale, 0, 0)).a
        : Hash12(pixel);
    return _VCJitterShift > 0.0 ? frac(jitter + _VCJitterShift) : jitter;
}

// SKIP THE EMPTY SKY (1.5.0, Sky/EmptySky): per cell of the weather map, the most cloud it can
// hold, so the march jumps whole steps over cells that hold none -- only samples that would have
// been exactly 0, so the picture is the same. 96 x 96, point-filtered, repeating like the weather
// map; a fine cell is 4 x 4 of its texels and the ones round it (bilinear reads them), a coarse one
// 4 x 4 fine cells. r, g = the most weather (as read, rounded up) in the fine and the coarse cell;
// b, a = the same for the Cumulus score.
sampler2D _EmptySkyTex;
float4 _EmptySky;          // x = 1: skip (0 with fragments, or at a full cover), y = fine cells along a
                           // weather tile, z = fine cells along a coarse cell, w = under Soft and Mixed
                           // the lowest threshold any of their clouds starts at (puffs or layer, any type)

// No cloud at all in a cell with these most-weather and most-score: the layer is cut at _Threshold
// (SampleDensityLod, LayerDensity), and under Cumulus the heaps' coverage line (CumulusDensity) is
// at or under 0 at its highest score -- it only rises with the score, which the C# side checks
// is so (_CumulusShape.y >= 0). Under Soft and Mixed the puffs and the layer are both cuts of the map
// (SoftDensity, LayerDensity), none under _EmptySky.w.
bool CellEmpty(float weather, float score)
{
    if (_CloudStyle > 1.5)
        return weather <= _EmptySky.w;

    if (weather > _Threshold)
        return false;

    if (_CloudStyle > 0.5)
        return _CumulusShape.y >= 0.0 && _CumulusShape.x + _CumulusShape.y * score <= 0.0;

    return true;
}

// How far along the ray (in t) from c -- a place in fine cells -- to the far wall of the cell
// [lo, lo + size)^2; dc is the ray in fine cells per unit t. 1e9 along an axis it does not move on.
float CellExit(float2 c, float2 dc, float2 lo, float size)
{
    float2 hi = lo + size;
    float tx = dc.x > 1e-12 ? (hi.x - c.x) / dc.x : (dc.x < -1e-12 ? (lo.x - c.x) / dc.x : 1e9);
    float ty = dc.y > 1e-12 ? (hi.y - c.y) / dc.y : (dc.y < -1e-12 ? (lo.y - c.y) / dc.y : 1e9);
    return min(tx, ty);
}

// How far the ray can go from p before it can meet cloud: 0 if it can here. When it can, `askAgain`
// says how far it can go before the map has anything new to say (the fine cell's far wall). The
// map's texel is read at its centre, picked by the same floor that finds the cell's walls: the GPU
// rounding a point on a wall the other way can never pair one cell's bound with another's walls.
float EmptyAhead(float3 p, float2 dc, out float askAgain)
{
    askAgain = 0.0;
    float2 c = (p.xz / _WeatherTile - _WeatherPhase) * _EmptySky.y;
    float2 fine = floor(c);
    float4 most = tex2Dlod(_EmptySkyTex, float4((fine + 0.5) / _EmptySky.y, 0, 0));

    if (CellEmpty(most.g, most.a))
        return CellExit(c, dc, floor(fine / _EmptySky.z) * _EmptySky.z, _EmptySky.z);

    float fineExit = CellExit(c, dc, fine, 1.0);
    if (CellEmpty(most.r, most.b))
        return fineExit;

    askAgain = fineExit;
    return 0.0;
}

// The noise's mip level at distance t from this pixel's footprint (the detail's, or the Cumulus
// style's: _DetailLight2.z is set for whichever is in use): far billows average away instead of
// sparkling. Under Cumulus it goes in unclamped: the layer's detail is _DetailLodShift levels
// finer, and clamps there itself. Under Soft too: its fine read fades out above 0 (SoftDensity).
float CloudLod(float t)
{
    if (_CloudStyle > 0.5)
        return log2(max(t, 1.0)) + _DetailLight2.z;

    return _DetailAmount > 0.0 ? max(log2(max(t, 1.0)) + _DetailLight2.z, 0.0) : 0.0;
}

// How much of the clouds there is at distance tEnter, 0..1: they dissolve into the distance
// rather than ending on a hard line -- and at night that only happens where the clouds actually
// run out. The same factor scales the light and the coverage, as premultiplied alpha needs.
float CloudPresence(float tEnter)
{
    float fade = saturate(1.0 - tEnter / _MaxDistance);
    fade *= fade;

    float edge = 1.0 - smoothstep(_EdgeStart, 1.0, tEnter / max(_CloudExtent, 1.0));
    return lerp(fade, max(fade, edge), _NightOpacity);
}

// The rain curtains' extinction per metre at p, t metres along the view ray: uneven
// curtains, stretched tall, sliding down as the rain falls, fading out with distance.
float RainSigma(float3 p, float t)
{
    float rain = SampleRain(p);
    if (rain <= 0.001)
        return 0.0;

    // The 6 is CloudRain.CurtainStretch; the fall wraps on a multiple of it.
    float3 q = float3(p.x, (p.y + _RainFall) / 6.0, p.z) / _RainCurtainScale;
    float curtain = 0.5 + tex3Dlod(_NoiseTex, float4(q, 0)).r;

    float fade = saturate(1.0 - t / _RainMaxDistance);
    return _RainShaftDensity * rain * curtain * fade * fade;
}

// Height of the ground (or the water on it) under p.
float GroundAt(float3 p)
{
    return tex2Dlod(_TerrainTex, float4(p.xz / _TerrainMapSize + 0.5, 0, 0)).r;
}

// What the fog's heights are measured from under p: the ground, so the layer drapes
// over hills, or one level for the whole map (its sea level), so it lies flat and
// the hills stand out of it. A uniform branch: the level fog never reads the map.
float FogGround(float3 p)
{
    // One return: this compiler calls two "potentially uninitialized".
    float ground = _FogLevel;
    if (_FogFollowGround > 0.5)
        ground = GroundAt(p);

    return ground;
}

// Whether a point `above` its reference is inside the layer's height range. With the
// base at 0 there is no underside to be below: the terrain map is coarse (67 m a
// texel), so a camera on a slope can read as a little under the ground.
bool InFogLayer(float above)
{
    return above < _FogBase + _FogHeight && (_FogBase <= 0.0 || above >= _FogBase);
}

// How far the ground at p lies below the land round it, metres (1.5.0, "Fog fills valleys").
float HollowAt(float3 p)
{
    return tex2Dlod(_FogPlaceTex, float4(p.xz / _TerrainMapSize + 0.5, 0, 0)).r * 255.0;
}

// InFogLayer at p, where valley fog may stand taller than the layer (1.5.0).
bool InFogLayerAt(float3 p, float above)
{
    float extra = 0.0;
    if (_FogValley > 0.0)
        extra = _FogValley * HollowAt(p);

    return above < _FogBase + _FogHeight + extra && (_FogBase <= 0.0 || above >= _FogBase);
}

// Fog density at p, 0..1 before _FogDensity: the clouds' recipe, lying on the ground.
// `above` is p's height over FogGround(p), which every caller already has. `layerHeight` comes
// back with the layer's height at p: _FogHeight, or more in a valley (1.5.0).
float FogAt(float3 p, float above, bool detailed, out float layerHeight)
{
    // In a valley the layer reaches up towards the level of the land round it (1.5.0): the top is
    // where the fog is, not how much of it there is, so this one goes past "Fog height" on purpose.
    // The march's ceiling still holds: the ground plus its hollow is the land round it, never
    // higher than the highest ground.
    layerHeight = _FogHeight;
    float hollow = 0.0;
    if (_FogValley > 0.0)
    {
        hollow = HollowAt(p);
        layerHeight += _FogValley * hollow;
    }

    float h = (above - _FogBase) / layerHeight;
    if (h >= 1.0 || h < -0.25)
        return 0.0;

    // A raised layer has an UNDERSIDE, rounded off like its top but over a shorter
    // reach, and never over more than the gap beneath it -- so raising the base off
    // zero grows an underside instead of popping one in. A layer that starts at its
    // reference has none: "a little below the ground" is still ground (see above).
    float under = 1.0;
    if (_FogBase > 0.0)
    {
        float u = saturate((above - _FogBase) / max(min(_FogBase, _FogHeight * 0.25), 1.0));
        under = u * u * (3.0 - 2.0 * u);
        if (under <= 0.0)
            return 0.0;
    }

    h = max(h, 0.0);

    // Where there is fog: the clouds' weather field, read at another scale and
    // another place so a fog patch is not a cloud's footprint. It gathers on low
    // ground: a little extra wherever the ground is below the pooling level.
    float2 uv = p.xz / _FogTile - _FogPhase + float2(0.37, 0.61);
    float weather = tex2Dlod(_WeatherTex, float4(uv, 0, 0)).r;
    float pooling = saturate((_FogPool - (p.y - above)) / 150.0) * 0.1;
    float gathered = weather + pooling;

    // Valleys (1.5.0): the weather's fog gathers in hollows -- more of it there -- and leaves the
    // open high ground, all of it at 100%.
    float valley = 0.0;
    if (_FogValley > 0.0)
    {
        valley = saturate(hollow / 40.0);
        gathered += _FogValleyGather * valley;
    }

    float cover = saturate((gathered - _FogThreshold) / 0.1);
    if (_FogValley > 0.0)
        cover *= lerp(1.0, valley, _FogValley);

    // Banks over the water (1.5.0): fog of their own, from the same weather read with their own
    // threshold, near the water as it lies upwind -- so they come ashore downwind and thin out a few
    // hundred metres inland. Open water is no valley, so the valley's rule above leaves them be.
    if (_FogWater > 0.5)
    {
        float2 wuv = (p.xz - _FogWaterPush) / _TerrainMapSize + 0.5;
        float near = smoothstep(0.05, 0.6, tex2Dlod(_FogPlaceTex, float4(wuv, 0, 0)).g);
        cover = max(cover, saturate((weather - _FogWaterThreshold) / 0.1) * near);
    }

    if (cover <= 0.0)
        return 0.0;

    // Solid from the ground up, rounding off into the top: a boundary, which an
    // exponential falloff never has.
    float shaped = cover * (1.0 - smoothstep(0.4, 1.0, h)) * under;

    // FLOW. The noise lookup is displaced by a larger, slower swirl that drifts on
    // its own and turns over with _FogBoil, so billows shear, curl and merge; and
    // the upper part of the layer is carried further than the ground layer, which
    // drags. A pattern that only translated would slide past like a texture.
    //
    // "Further" is _FogLead, which swings between 400 m ahead and 400 m behind.
    // It used to be 0.35 x the whole drift, which never stopped growing: minutes
    // into a session the top was kilometres ahead of a base a few hundred metres
    // below it, and the billows had been sheared into sheets, then stripes.
    float3 s = p / _FogSwirlTile;   // slower than what it displaces
    s.xz -= _FogSwirlPhase;
    s.y += _FogBoil;
    float2 swirl = tex3Dlod(_NoiseTex, float4(s, 0)).rg - 0.5;

    float3 q = p / _FogBillowTile;
    q.xz -= _FogBillowPhase + h * _FogLead / _FogBillowTile.xz;
    q.xz += swirl * 0.75;
    q.y -= _FogBoil * 2.0;
    float base = tex3Dlod(_NoiseTex, float4(q, 0)).r;

    // The clouds' own shaping: where the cover is thin only the strongest noise
    // survives, so the layer breaks into lumps with a billowing top.
    float d = saturate(Remap(base, 1.0 - shaped, 1.0, 0.0, 1.0)) * shaped;

    // Wisps: fine noise eats into it, moving faster than the billows and rising.
    if (detailed && d > 0.0 && _FogBreakup > 0.0)
    {
        float3 w = p / _FogWispTile;
        w.xz -= _FogWispPhase;
        w.y -= _FogBoil * 9.0;
        float wisp = tex3Dlod(_NoiseTex, float4(w, 0)).g;
        d = saturate(Remap(d, wisp * _FogBreakup, 1.0, 0.0, 1.0));
    }

    return d;
}

// The fog's extinction per metre at full density, before FogAt's 0..1: it fades in with a
// little fog rather than switching on.
float FogStrength()
{
    return _FogDensity * saturate(_FogAmount * 5.0);
}

// How much of the fog is left t metres along the view ray: all of it up to _FogFadeStart, none at
// _FogMaxDistance. At the default distance (9 km) the fade starts at the camera -- exactly the
// 1 - t / 9000 it always was; a farther one (CloudVolume.ApplyFog) keeps the fog whole further out.
float FogFade(float t)
{
    return saturate(1.0 - (t - _FogFadeStart) / _FogFadeLength);
}

#endif
