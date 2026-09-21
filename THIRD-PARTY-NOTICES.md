# Third-Party Notices

Volumetric Weather is an unofficial mod for Cities: Skylines 1. This file records what in the
repository is not original work, and what is credited rather than incorporated.

Apart from the two shader ports described below, and a few lines of a third shader copied from
one of them, this repository contains no game code and no game assets. The game's DLLs are referenced from your own installation at build time and are
never copied into the repository or the distribution.

## Shipped with the mod

Both of these are redistributed alongside `VolumetricClouds.dll`, which is the standard pattern
in this ecosystem.

| Component | Author | Licence | Use |
| --- | --- | --- | --- |
| [`UnifiedUILib.dll`](https://github.com/kianzarrin/UnifiedUI) | kianzarrin | MIT | Registers the mod's button with Unified UI, and hosts the button panel itself when the Unified UI mod is not enabled |
| [`CitiesHarmony.API.dll`](https://github.com/boformer/CitiesHarmony) | boformer | MIT | Bootstrap shim: waits for the Harmony mod and prompts the player to install it if it is missing |

## Build-time only

| Component | Author | Licence | Use |
| --- | --- | --- | --- |
| [`CitiesHarmony.Harmony.dll`](https://github.com/boformer/CitiesHarmony) | boformer; a repackaging of [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike | MIT | Compile-time reference only. The real assembly is provided at runtime by the Harmony mod (Workshop 2040656402) and is never shipped by us. |
| [`Microsoft.NETFramework.ReferenceAssemblies.net35`](https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net35) | Microsoft | MIT | net35 reference assemblies, so the project builds without an old framework installed |

## Shaders reconstructed from the game

`UnityProject/Assets/VolumetricClouds/LightHalo.shader` and `LightHaloDynamic.shader` are
hand-written ports of Cities: Skylines' own light halo shader, `Custom/Lights/GroupVolume` —
the batched variant and the instanced variant respectively. They were reconstructed from the
Direct3D 11 disassembly of the compiled original and deliberately kept close to its instruction
order, so that with their controls at neutral values they reproduce the original's output and
can be A/B tested against it in-game. The changes are a fix for the shader's NaN case (which
paints a light's whole quad solid at fog values at or below -0.5), the added brightness,
tightness and radius controls, and a per-light near-camera blend.

They exist solely for interoperability: the game reads its halo material at draw time, so
replacing the material is the only way to change how halos are drawn. The original shader is
the work of **Colossal Order Ltd.**, who hold whatever rights attach to it. These two files
are excluded from the MIT licence that covers the rest of this repository (see `LICENSE`), and
no ownership of the original's design is claimed.

`FogLamps.shader` draws the same batched light meshes into the fog's light map, so it has to
switch each lamp on and off exactly as the halo does. Its blink table (`kBlink`) and the blink
and switch-on lines of its first pass's vertex shader are copied from `LightHalo.shader`, and
are excluded from the MIT licence in the same way. The rest of that file is original and
MIT-licensed.

The disassembly itself is never committed. `tools/shaderdump.ps1` regenerates it from your own
installed copy of the game, and is the only way to verify a port.

## Techniques credited, not copied

The cloud modelling uses published techniques — Perlin-Worley noise, remap-based shaping and
erosion, a weather field controlling coverage — introduced by Andrew Schneider (Guerrilla
Games) in *The Real-Time Volumetric Cloudscapes of Horizon Zero Dawn*, SIGGRAPH 2015. The
technique is acknowledged here; the implementation in `CloudCommon.cginc`,
`CloudRaymarch.shader` and `CloudShadowMap.shader` was written for this mod.

Other mods were read to understand the game's behaviour and to stay compatible with them —
Unified UI, CitiesHarmony, Render It!, Theme Mixer, Intersection Marking Tool, Persistent Fog
Adjuster and Play It! among them. That study informed integration and compatibility decisions;
no source code from them is incorporated into this project. Where such a mod's behaviour
mattered, it was read from its IL.

## Trademarks

Cities: Skylines is © Colossal Order Ltd. and published by Paradox Interactive AB; the names
are trademarks of their respective owners. Unity is a trademark of Unity Technologies. This
project is unofficial, fan-made, and not affiliated with or endorsed by any of them.
