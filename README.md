# Volumetric Weather

**Cities: Skylines 1 — Windows, and since 1.1.1 Mac and Linux (experimental)**

Raymarched volumetric clouds that cast real shadows on your city, follow the game's weather,
and bring their own rain, lightning, fog and night sky with them.

> **Status:** on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3805863507).
> Performance has only ever been measured on one high-end Windows GPU and one MacBook Air.
> This mod was written almost entirely by an AI under human direction — read the
> [AI disclosure](#ai-disclosure) before you rely on it.

## Features

- **Volumetric clouds** — a real 3D layer you can fly through, not a sky texture. Altitude,
  thickness, break-up, density and brightness are all adjustable.
- **Real cloud shadows** on terrain and buildings, drifting with the wind. Gaps get full sun;
  nothing is dimmed globally.
- **Follows the game's weather** — cover, rain, fog and wind direction are read from the game,
  so random weather, disasters and mods such as Play It! all drive the sky. Any channel can be
  overridden from the in-game panel, and one button hands control back to the game.
- **Rain that belongs to the clouds** — curtains hang under the clouds that are actually
  raining, streaks fall near the camera, and rain sound and wet roads follow the clouds instead
  of falling everywhere at once. Stands down on winter maps.
- **Lightning** — flashes light the clouds and rain from inside, the bolt is redrawn, and
  thunder arrives after a speed-of-sound delay. Storm lightning is visual-only and never starts
  a fire; the game's own strikes are left untouched.
- **Volumetric fog** *(opt-in, expensive)* — fog as a cloud layer, with a top surface,
  self-shadowing and sun shafts. Level from sea level, or draped over the terrain; from the
  bottom up, or only above a height you choose. At night it glows round street lamps, building
  lights and headlights. Off by default.
- **Night sky** — clouds hide the stars behind them and carry a faint pale glow underneath.
- **Cloud colours** — tint the sunlit side and the shaded side of the clouds separately: warm
  tops over cool bases, a golden hour, an orange city glow under the clouds at night. Only the
  hue changes, never the brightness; white, the default, leaves the clouds as they are. The
  game's own colour picker, a field for `#FFC0CB` or `255, 192, 203`, Copy, Paste and a reset.
  The volumetric fog has a colour of its own, the same way.
- **Light halos** *(opt-in)* — shrinks the game's oversized night halos on street lamps,
  building lights and vehicles, with separate control for lamps close to the camera. Also fixes
  the game bug that turns a halo into a solid box at negative fog values.
- **One place to tune it** — an F4 panel with the sky's settings, and four advanced tabs for
  the rest when you want them. One quality preset moves the expensive settings together.

## Requirements and installation

| | |
| --- | --- |
| Game | Cities: Skylines (2015). **Windows** (Direct3D 11, or OpenGL with `-force-glcore`). **Mac** (Metal) and the **native Linux** version (OpenGL 3.3) since 1.1.1 — new and experimental: tested on one Apple Silicon MacBook Air, not yet on native Linux; the mod's options page says so there. Proton on Linux and Steam Deck runs the Windows version as before. |
| Required | [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402) (Workshop 2040656402) |
| Optional | [Unified UI](https://steamcommunity.com/sharedfiles/filedetails/?id=2966990700) — the mod's button appears there; without it you get a button on the HUD |

For a manual install, put `VolumetricClouds.dll`, `UnifiedUILib.dll` and
`CitiesHarmony.API.dll` from the release in their own folder under
`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\`, then enable the mod in
Content Manager → Mods. Use only one installation: if you later subscribe on the Workshop,
remove or disable the local copy first.

The mod runs in a loaded city — new game or load game — not in the editors, and not yet when
starting a scenario.

It keeps a small record of each city's sky in the savegame (61 bytes under its own key: the
cloud pattern and how far the wind has carried it), so a city looks the same when you come
back. Nothing of the game's own data is written. Removing the mod should not corrupt a save:
the game keeps mod data as opaque key/value pairs, loads the save the same way without the
mod, and writes the record back unread on the next save. That said, the mod is provided as-is
and I am not responsible for anything that goes wrong, lost or damaged saves included. Keep
backups of the cities you care about.

## Quick start

1. Load a city. The clouds replace the game's dynamic weather straight away; nothing needs
   configuring.
2. Press **F4** (rebindable), or the Unified UI button, to open the in-game panel.
3. **Now** — what the weather is doing, and every override. **Clouds** and **Fog** — the look,
   the cloud colours included.
4. For every other setting, tick *Show advanced options in the in-game panel* on the mod's
   page in the game's **Options** screen: *Weather*, *Light*, *Rendering* and *Halos* join
   the F4 panel. That page also has the language, the panel's key and the reset buttons.

*Volumetric fog* and *Light halos* are off until you turn them on (the fog asks for
confirmation first). *Reset all settings to defaults* (on the mod's options page) restores
everything, including turning volumetric fog and *Show advanced options in the in-game panel*
off.

## Compatibility

- **Render It!, Theme Mixer** — no conflict; the mod never writes the sun's intensity.
- **Play It!** — its rain, fog and cloud sliders drive this mod's sky.
- **Cloud replacer mods** — the game's painted sky layer is left alone. Only the rain-cloud
  dome is switched off while these clouds show, and it is restored afterwards.
- **Intersection Marking Tool** — lamps placed as IMT props get the same halo settings as
  ordinary street lamps.
- **Persistent Fog Adjuster** — no longer needed for smaller halos.

Everything the mod touches on game objects — the sun's light cookie, rain and lightning
materials, fog fields, halo materials, star render order — is put back when the city unloads.

## Performance

All tuning so far was done on an RTX 5070 Ti, which hides costs. The **High** preset is the
only one that has been measured; **Low** and **Medium** are provisional. On a fanless
MacBook Air M1 an empty city ran at 22 to 33 ms a frame on High at 1680x1050, more with the
fog on: expect a lower frame rate on a Mac than on a Windows PC. Volumetric fog is by far
the most expensive feature, which is why it is opt-in. The raymarch quality slider and the
shadow map's resolution and update rate (the in-game panel's Rendering tab, shown with
*Show advanced options in the in-game panel*) are the levers if frames are tight.

## Known limitations

- Mac and Linux support is new (1.1.1) and experimental: one Mac tested, no native Linux
  machine yet, and the clouds cost more there than on a Windows PC. Where the graphics
  cannot run the shaders at all (they need shader model 4, Metal, or OpenGL 3.3) the mod
  switches itself off when a city loads and says so once per launch; nothing in the city is
  changed by it.
- No moon shadows — cloud shadows are cast by the sun only.
- 100% intensity is not a sealed overcast: the 3D noise erodes the layer, so sun patches
  remain. That is deliberate.
- Secondary cameras (reflections) also render the cloud volume.
- Scenarios get no clouds.

## Bug reports

The mod writes its own log, fresh each session:

```
Windows:  %LOCALAPPDATA%\Colossal Order\Cities_Skylines\VolumetricClouds.log
Mac:      ~/Library/Application Support/Colossal Order/Cities_Skylines/VolumetricClouds.log
Linux:    ~/.local/share/Colossal Order/Cities_Skylines/VolumetricClouds.log
```

It always records the system, every feature switch and anything that can suppress rendering.
For a report, tick *Detailed logging* (on the mod's options page), reproduce the problem, and
attach that file together with your game version, GPU and a screenshot. A screenshot on its
own rarely identifies the cause.

## How it works

1. **Noise is generated on the CPU at load**: a tiling 384² Perlin-Worley *weather field*
   (where the clouds are) and a tiling 64³ 3D noise (their shape and erosion).
2. **One density function.** `SampleDensity` in `CloudCommon.cginc` is the only definition of
   the cloud volume; the visible pass and the shadow pass both march it, so clouds and shadows
   cannot drift apart.
3. **Shadows are a light cookie.** A 2048² render texture marches one sun ray per texel through
   that volume and becomes the sun's directional cookie — the only thing that darkens the world.
4. **Coverage is solved, not swept.** A threshold table is bisected against the field's
   histogram, so a coverage setting means a share of the sky. Rain reuses that table with a
   higher threshold, which makes rain a subset of the clouds by construction.
5. **The weather is read, never written** — writing it would change solar plants and the
   savegame. The single change to the simulation is a Harmony postfix on
   `WeatherManager.SampleRainIntensity`, so rain sound and wet roads follow the clouds.
6. **Rain, lightning and fog share the cloud pass** and composite front to back. Fog is a level
   slab above the map's sea level, or follows the ground through a 256² height texture, and is lit through the cloud shadow map — that is where
   the sun shafts come from.
7. **Halos are a shader replacement**, swapped into the batched light layers and the
   dynamic-light slot each frame. The game's own materials are never modified.

What has to happen before a release is in
[`docs/RELEASE-CHECKLIST.md`](docs/RELEASE-CHECKLIST.md).

## Translations

The mod is English only for now, but every text it shows is in one file,
[`VolumetricClouds/Localization/en.xml`](VolumetricClouds/Localization/en.xml). To translate it,
copy that file under the game's code for your language (`de.xml`, `fr.xml`, …) and translate
the text between the tags; the comment at the top of the file explains the rest. A language
with gaps still works: anything not yet translated shows in English.

## Build and test

See [BUILDING.md](BUILDING.md). In short: `dotnet build VolumetricClouds/VolumetricClouds.csproj`
builds **and** deploys; the compiled shader bundles (one per platform) are committed, so Unity
is only needed after editing `UnityProject\Assets`. There is no automated test suite — pure maths is tested offline
by `tools/test-*.ps1`, everything else is verified in the game by reading the mod's log.

## AI disclosure

This mod was developed with heavy use of AI. Almost all of the C#, the shaders, the
reverse-engineering tools and the documentation — this file included — were written by
**Claude** (Anthropic) through Claude Code. I set the goals, made the design decisions, and did
all of the in-game testing and visual judgement; the commits Claude wrote carry a
`Co-Authored-By: Claude` trailer.

What that means in practice:

- The code was not taken on trust. Game APIs were checked by reflection and by reading the
  game's IL, the shader ports were checked by diffing disassembly, and pure maths is tested
  offline. But there is no automated test suite, and the mod has been seen on two machines,
  a Windows PC and a Mac.
- AI-written code can be confidently wrong. Several theories during development were, and
  were only caught in the game or in its IL. Read the code before you depend on it.
- The mod is provided as-is, with no warranty of any kind. Use it at your own risk.

## Credits and licence

The source is distributed under the [MIT License](LICENSE), copyright © 2026 Anthony Ibrahim.

Two files are excluded: `LightHalo.shader` and `LightHaloDynamic.shader` are hand-written ports
of the game's own halo shader, reconstructed for interoperability. So are the few lines of
`FogLamps.shader` copied from the first of them (its blink table and lamp switch-on). Those,
the shipped third-party DLLs, and the published cloud-rendering technique this mod builds on
are all credited in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Cities: Skylines is © Colossal Order Ltd., published by Paradox Interactive AB. Unity is a
trademark of Unity Technologies. This is an unofficial fan-made mod, not affiliated with or
endorsed by any of them. Apart from the ported shader code, this repository contains no game
code and no game assets; the game's DLLs are referenced from your own installation and never
copied.

Release history is in [CHANGELOG.md](CHANGELOG.md).
