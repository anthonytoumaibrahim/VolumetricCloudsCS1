# Volumetric Clouds

Raymarched volumetric clouds for **Cities: Skylines** (the first game) that cast real shadows
on your city, follow the game's weather, and bring their own rain, lightning, fog and night
sky with them.

> **Status:** working in-game, pre-release. Not on the Steam Workshop yet. Performance has
> only been measured on one high-end GPU (see [Performance](#performance)).

> **AI disclosure:** this mod was written almost entirely by an AI under human direction.
> See [AI disclosure](#ai-disclosure) before you rely on it or build on it.

---

## For players

### What it does

- **Volumetric clouds.** A real 3D cloud layer you can fly through, not a sky texture. Altitude,
  thickness, break-up, density and brightness are all adjustable.
- **Real cloud shadows.** The clouds' shadows land on terrain and buildings and drift with the
  wind. Gaps get full sun; nothing is dimmed globally.
- **Follows the game's weather.** Cloud cover, rain, fog and wind direction are read from the
  game, so random weather, disasters and mods such as **Play It!** all drive the sky. You can
  override any of them from the in-game panel, and one button hands control back to the game.
- **Rain that belongs to the clouds.** Rain curtains hang under the clouds that are actually
  raining, streaks fall near the camera, and the rain sound and wet roads follow the clouds
  instead of being everywhere at once. Stands down on winter maps.
- **Lightning.** Flashes light the clouds and rain from inside, the bolt is redrawn, thunder
  arrives after a speed-of-sound delay, and storms get extra visual-only lightning that never
  starts a fire. The game's own strikes (the ones that do burn things) are left untouched.
- **Volumetric fog** *(opt-in, very heavy)*. Fog that lies on the terrain as a cloud layer with
  a top surface, self-shadowing and sun shafts. Off by default; the game's fog carries on as
  normal when it is off.
- **Night sky.** Clouds hide the stars behind them and get a faint pale glow underneath.
- **Light halos** *(opt-in)*. Shrinks the game's oversized night-time halos on street lamps,
  building lights and vehicles, with separate control for lamps close to the camera. Also fixes
  the game bug that turns halos into solid boxes at negative fog values.

### Requirements

| | |
| --- | --- |
| Game | Cities: Skylines (2015). **Windows only** — the shaders are compiled for Direct3D 11. |
| Required | [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402) |
| Optional | [Unified UI](https://steamcommunity.com/sharedfiles/filedetails/?id=2966990700) — the mod's button appears there; without it you get a button on the HUD |

The mod only runs in a loaded city (new game or load game) — not in the editors, and not yet
when starting a scenario. It stores nothing in your savegame, so it can be removed at any time.

### Using it

- **F4** (rebindable) or the Unified UI button opens the in-game panel: three tabs — **Now**
  (what the weather is doing, and every override), **Clouds** and **Fog**. These are the things
  you judge against the sky.
- The mod's page in the game's **Options** screen holds everything else in five tabs:
  **Weather**, **Light**, **Rendering**, **Halos**, **General**.
- *Show advanced options in the in-game panel* (General) puts all five of those tabs in the F4
  panel too, if you like tuning with the sky in view.
- *Quality preset* (Rendering) sets raymarch steps and shadow-map cost together: Low, Medium,
  High.
- *Reset all settings to defaults* (General) restores everything except the two per-machine
  opt-ins (volumetric fog, advanced panel), which only your own click ever turns off.

### Compatibility

- **Render It!, Theme Mixer** — no conflict; the mod never writes the sun's intensity.
- **Play It!** — its rain, fog and cloud sliders drive this mod's sky.
- **Cloud replacer mods** — the game's painted sky layer is left alone. Only the game's
  rain-cloud dome is switched off while these clouds are showing, and restored afterwards.
- **Intersection Marking Tool** — lamps placed as IMT props get the same halo settings as
  ordinary street lamps.
- **Persistent Fog Adjuster** — no longer needed for smaller halos.

Everything the mod changes on game objects (the sun's light cookie, rain and lightning
materials, fog fields, halo materials, star render order) is put back when the city unloads.

### Performance

All tuning so far was done on an RTX 5070 Ti, which hides costs. The **High** preset is the
only one that has been measured; **Low** and **Medium** are provisional numbers. Volumetric
fog is by far the most expensive feature and asks for confirmation before it turns on.

### Known limitations

- Windows / Direct3D 11 only. On macOS and Linux the shaders are not available, so at best you
  get the simple billboard fallback; this is untested.
- No moon shadows: cloud shadows are cast by the sun only.
- 100% intensity is not a sealed overcast — the 3D noise erodes the layer, so sun patches
  remain. This is deliberate.
- Secondary cameras (reflections) also render the cloud volume.

### If something looks wrong

The mod writes its own log, fresh each session:

```
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\VolumetricClouds.log
```

It always records the system, every feature switch and anything that can suppress rendering.
For a bug report, tick *Detailed logging* (Options → General), reproduce the problem, and
attach that file.

---

## For developers

### How it works, in brief

1. **Noise is generated on the CPU at load**: a tiling 384² Perlin-Worley *weather field*
   (where clouds are) and a tiling 64³ 3D noise (their shape and erosion).
2. **One density function.** `SampleDensity` in `CloudCommon.cginc` is the only definition of
   the cloud volume. The visible pass (`CloudRaymarch.shader`) and the shadow pass
   (`CloudShadowMap.shader`) both march it, so clouds and shadows cannot drift apart.
3. **Shadows are a light cookie.** A 2048² render texture marches one sun ray per texel through
   that volume and is assigned as the sun's directional cookie; the game's own deferred
   lighting does the rest. The cookie is the *only* thing that darkens the world.
4. **Coverage is solved, not swept.** A threshold table is bisected against the field's
   histogram so a coverage setting means a share of the sky. Rain reuses the same table with a
   higher threshold, which makes rain a subset of the clouds by construction.
5. **The weather is read, never written.** Writing it would change solar plants and the
   savegame. The single change to the simulation is a Harmony postfix on
   `WeatherManager.SampleRainIntensity`, so rain sound and wet roads follow the clouds.
6. **Rain curtains, lightning sources and fog are marched in the same pass** as the clouds and
   composited front to back. Fog follows the ground through a 256² height texture built from
   the terrain, and is lit through the cloud shadow map — that is where sun shafts come from.
7. **Halos are a shader replacement.** Replacement materials are swapped into the batched light
   layers and the dynamic-light slot every frame (the game reassigns its own on each mesh
   rebuild); the game's materials are never modified.
8. **Shaders cannot be compiled at runtime.** They are built into an AssetBundle by Unity 5.6.6
   and embedded in the DLL. If the bundle is missing, a billboard fallback takes over.

The full technical record — invariants, verified facts about the game read from its IL, and
every dead end — is in [`CLAUDE.md`](CLAUDE.md). The release plan is in
[`POLISH-PLAN.md`](POLISH-PLAN.md). Read the *Invariants* section of `CLAUDE.md` before
changing anything in `Sky/`.

### Building

Requirements: the .NET SDK, and Cities: Skylines installed (the project references the game's
own DLLs; if Steam is not in `C:\Program Files (x86)\Steam`, pass
`-p:GameManaged=<path to Cities_Data\Managed>`).

```
dotnet build VolumetricClouds/VolumetricClouds.csproj
```

This builds **and deploys** to `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds`.
The compiled shader bundle is committed, so Unity is not needed for C# work.

Only after editing anything under `UnityProject\Assets`:

```
.\build-bundle.ps1        # needs Unity 5.6.6 at C:\Program Files\Unity\Editor\Unity.exe
```

then rebuild the mod to embed the new bundle. A shader that fails to compile still produces a
bundle and exit code 0; the script checks Unity's log for that, so do not bypass it.

Hard constraints: **`net35`, C# 7.3** (the game is Unity 5.6 on Mono's .NET 3.5 profile — a
newer target builds fine and is silently ignored by the game), and `AssemblyVersion("1.0.*")`
with `<Deterministic>false</Deterministic>`, which is what lets the game load a rebuilt DLL.

### Layout

| Path | What is there |
| --- | --- |
| `VolumetricClouds/` | The mod. `Sky/` clouds, weather, rain, lightning, fog · `Lighting/` halos · `UI/` the panel, the options page and the settings catalog both are drawn from |
| `UnityProject/` | Unity 5.6 project holding the shaders and the headless bundle builder |
| `libs/` | Third-party DLLs referenced at build time (see [Third-party components](#third-party-components)) |
| `tools/` | PowerShell tools: `ilscan.ps1` (IL "find usages" over the game or any mod), `shaderdump.ps1` (disassembles compiled shaders), `read-settings.ps1` (decodes the `.cgs` settings file), `test-*.ps1` (offline tests of the pure maths) |

### Testing

There is no automated test suite. Pure maths (lightning timing and placement, the brightness
curve, fog coverage) is tested offline by the `tools/test-*.ps1` scripts, which load the built
DLL outside the game. Everything else is verified in the game by reading the mod's log.

---

## AI disclosure

This mod was developed with heavy use of AI. Almost all of the C#, the shaders, the
reverse-engineering tools and the documentation — this file included — were written by
**Claude** (Anthropic), working through Claude Code. I set the goals, made the
design decisions, and did all of the in-game testing and visual judgement; the commits Claude
wrote carry a `Co-Authored-By: Claude` trailer.

What that means in practice:

- The code was not taken on trust. Game APIs were checked by reflection and by reading the
  game's IL, the shader ports were checked by diffing disassembly, and pure maths is tested
  offline. But there is no automated test suite, and the mod has been seen on one machine.
- AI-written code can be confidently wrong. `CLAUDE.md` records several theories that were
  wrong and how each was caught. Read the code before you depend on it.
- The mod is provided as-is, with no warranty of any kind. Use it at your own risk.

---

## Copyright and credits

Copyright © 2026 Anthony Ibrahim.

**No open-source licence has been chosen yet.** Until a `LICENSE` file is added to this
repository, the source is published for reading and learning and all rights are reserved:
please ask before redistributing it, and do not re-upload the mod to the Steam Workshop or
elsewhere.

### Shaders

- `CloudCommon.cginc`, `CloudRaymarch.shader`, `CloudShadowMap.shader`, `RainDrops.shader`,
  `LightningBolt.shader` and `Invisible.shader` were written for this mod and are covered by
  the notice above. The cloud modelling uses published techniques — Perlin-Worley noise and
  remap-based shaping and erosion — introduced by Andrew Schneider (Guerrilla Games) in *The
  Real-Time Volumetric Cloudscapes of Horizon Zero Dawn* (SIGGRAPH 2015). The technique is
  credited here; the implementation was written for this mod.
- **`LightHalo.shader` and `LightHaloDynamic.shader` are different.** They are hand-written
  ports of the game's own halo shader, `Custom/Lights/GroupVolume` (its batched and instanced
  variants), reconstructed from the Direct3D 11 disassembly of the compiled shader and kept
  close to its instruction order, so that they reproduce its output exactly before fixing its
  NaN bug and adding controls. The original shader is the work of **Colossal Order Ltd.**
  These two files exist solely for interoperability with the game, claim no ownership of the
  original's design, and are excluded from any licence applied to the rest of this repository.
  The disassembly itself is never committed; `tools/shaderdump.ps1` regenerates it from your
  own installed copy of the game.

### Third-party components

| Component | Author | Licence | Use |
| --- | --- | --- | --- |
| [`UnifiedUILib.dll`](https://github.com/kianzarrin/UnifiedUI) | kianzarrin | MIT | Shipped with the mod |
| [`CitiesHarmony.API.dll`](https://github.com/boformer/CitiesHarmony) | boformer | MIT | Shipped with the mod |
| [`CitiesHarmony.Harmony.dll`](https://github.com/boformer/CitiesHarmony) | boformer; a fork of [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike | MIT | Compile-time reference only; provided at runtime by the Harmony mod |

### Trademarks

Cities: Skylines is © Colossal Order Ltd. and published by Paradox Interactive AB; the names
are trademarks of their respective owners. Unity is a trademark of Unity Technologies. This
is an unofficial fan-made mod, not affiliated with or endorsed by any of them. Apart from the
two ported shaders described above, this repository contains no game code and no game assets;
the game's DLLs are referenced from your own installation and never copied.
