# Volumetric Weather

**Volumetric clouds, weather and fog for Cities: Skylines 1** on Windows, Mac and Linux.

Raymarched 3D clouds that cast real shadows on your city, follow the game's weather, and bring
their own rain, snow, lightning, rainbows and fog with them.

**[Subscribe on the Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3805863507)**
· [Changelog](CHANGELOG.md) · [Building from source](BUILDING.md)

> Features marked **1.5** are in this repository and come to the Workshop with the next update.

## Features

### Clouds

- **Volumetric clouds**: a real 3D layer you can fly through, not a painted sky. Height,
  thickness, break-up, density and brightness are all yours to set.
- **Real cloud shadows** on the ground and on buildings, drifting with the wind. The gaps get full
  sun; nothing is dimmed across the whole map.
- **Four cloud styles**:
  - *Classic*, the default: an even, broken layer.
  - *Cumulus*: heaped clouds of every size, with flat bases and billowing tops, standing among the
    Classic layer. **1.5** adds sliders for their size, height and amount.
  - *Soft* (**1.5**): fair-weather clouds made of many puffs, with wispy, cotton-candy edges. As the
    weather gets heavier the Classic layer comes in among them.
  - *Mixed* (**1.5**): one style for every kind of weather. Soft puffs in a light sky grow and
    gather into the Classic layer toward overcast, and the balance changes across the sky.
- **Cloud detail and fragments**: crisp edges and billows, and small shreds of cloud around and
  between the big ones.
- **Light inside the clouds**: thin cloud glows with the sun behind it, shaded sides are softer,
  and the sunlit city lights the cloud bases. At sunset the clouds keep the low sun's light, turning
  orange and then red, a little after the sun has set for the city below.
- **Distant clouds** (**1.5**): photographed skies behind the mod's own clouds, with cirrus, sheep
  clouds, cumulus on the horizon and layered skies. They're lit by the game's sun, go gold at
  sunset, drift with the wind and slowly change as they go. By default they come and go on their
  own, following the weather; or set each kind yourself.
- **To the horizon**: under a full overcast the cloud deck carries on to the horizon, following
  the curve of the Earth. *Clouds hide the sky* (**1.5**) keeps a cubemap's sun from shining through
  a thick daytime overcast.
- **Cloud colors**: tint the sunlit and shaded sides separately, for warm tops over cool bases, a
  golden hour, or an orange city glow at night. Only the hue changes; white leaves the clouds as
  they are.

### Weather

- **Follows the game's weather**: cloud cover, rain, fog and wind come from the game, so random
  weather, disasters and mods such as Play It! drive the sky. Any of them can be overridden from
  the in-game panel.
- **Wind direction** (**1.5**): follow the game's wind, follow it slowly so the sky doesn't swing
  round every few minutes, or fix it in one direction.
- **Rain from the clouds**: rain curtains hang under the clouds that are actually raining, streaks
  fall near the camera, and the rain sound and wet roads follow the showers instead of covering
  the whole map at once. On winter maps the snow does the same.
- **Lightning**: flashes light the clouds and rain from inside, bolts are redrawn, and thunder
  arrives after the right delay for its distance. Storm lightning is visual only and never starts
  a fire.
- **Rainbows**: an arch standing in a sunlit shower near where you look, with its fainter second
  bow, colored by the physics of real raindrops.
- **Larger weather pattern** (**1.5**): the cloud layout repeats four times further apart, for
  zooming far out with a camera mod.

### Fog *(opt-in)*

- **Volumetric fog**: fog as a layer of its own with a top surface, self-shadowing and sun shafts.
  Level from the sea, or draped over the terrain. At night it glows around street lamps, building
  lights and headlights.
- **Changing fog** (**1.5**): fog that changes with the weather and the time of day, morning fog
  that lifts as the day warms up, and mist rising from the wet ground after rain.
- **Fog fills valleys** and **fog over water** (**1.5**): fog that gathers in hollows and river
  gorges, and fog banks that drift ashore from the sea, lakes and rivers.

### Day, night and weather

- **Day, sunset and night** (**1.5**): a clock button on eleven settings (cloud colors, brightness,
  density, shadows, the fog and more) gives each a value for the day, the sunset and the night,
  blended by the sun's height.
- **Clear sky and overcast** (**1.5**): a cloud button on nine of them gives a value for a clear
  sky and one for a full overcast, blended by the cloud cover.
- **Night sky**: the clouds hide the stars behind them and carry a faint glow underneath.

### The panel

- **One panel for everything**: press **F4** (or use the mod's button) for the in-game panel.
  Every setting has its own reset button.
- **Out of the way while you build** (**1.5**): **Shift+F4** hides all of the weather until you
  press it again. Both keys can be changed on the options page.
- **Four looks** (**1.5**): *Frosted glass* (the default), *See-through*, *Blue*, or *Vanilla*, the
  game's own style.
- **Profiles**: save the whole sky under a name and switch skies from a list. Each profile is a
  plain XML file, easy to share. The list also has the mod's defaults (**1.5**).
- **Languages**: English, Spanish, French and Simplified Chinese.
- **Light halos** *(opt-in)*: smaller night halos on street lamps, buildings and vehicles, and a fix
  for the game bug that turns a halo into a solid box.
- **Smoother clouds** (**1.5**, opt-in): blends frames together for cleaner, grain-free clouds.

## Requirements and installation

| | |
| --- | --- |
| Game | Cities: Skylines (2015). **Windows** (Direct3D 11, or OpenGL with `-force-glcore`), **Mac** (Metal) and **native Linux** (OpenGL 3.3). Proton and the Steam Deck run the Windows version. |
| Required | [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402) |
| Optional | [Unified UI](https://steamcommunity.com/sharedfiles/filedetails/?id=2966990700): the mod's button goes there; without it, the button sits on the HUD |

The easiest way is to subscribe on the
[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3805863507). To install by
hand, put `VolumetricClouds.dll`, `UnifiedUILib.dll` and `CitiesHarmony.API.dll` from a
[release](../../releases) in their own folder under
`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\`, then enable the mod in
Content Manager → Mods. Use only one copy: if you subscribe later, remove the local one first.

The mod runs in a loaded city (new game or saved game), not in the editors or scenarios.

Each city keeps a small record of its sky in the savegame (77 bytes under the mod's own key: the
cloud pattern, how far the wind has carried it, and where you moved the clouds), so a city looks
the same when you come back. None of the game's own data is changed, and a save still loads with
the mod removed. The mod is provided as-is; keep backups of the cities you care about.

## Quick start

1. **Load a city.** The clouds replace the game's weather right away; nothing needs setting up.
2. **Press F4** (rebindable), or click the mod's button, to open the panel.
3. **Now** shows what the weather is doing and lets you override it. **Clouds**, **Distant
   clouds** and **Fog** set the look; **Light** sets the shadows and how the clouds are lit.
4. For every setting, tick *Show advanced options in the in-game panel* on the mod's page in the
   game's **Options**: the *Weather* and *Halos* tabs join the panel. That page also has the quality
   preset, the panel's look, the language, the panel's key and the reset buttons.

Volumetric fog and light halos stay off until you turn them on. *Reset all settings to defaults*
on the options page puts everything back.

## Compatibility

- **Render It!, Theme Mixer**: no conflict. The mod never changes the sun's strength, and whatever
  Render It! sets for the game's fog comes back when the clouds are hidden.
- **Play It!**: its rain, fog and cloud sliders drive the sky.
- **AD Cloud Replacer and other cloud replacers**: the game's painted sky is left alone; only its
  rain-cloud dome is switched off while these clouds show.
- **Fog Controller**: tested together, no conflict.
- **Intersection Marking Tool**: lamps placed as IMT props get the same halo settings as street
  lamps.

Everything the mod touches on game objects (the sun's light cookie, rain and lightning materials,
fog fields, halo materials, star render order) is put back when the city unloads.

## Performance

The **Quality preset** at the top of the mod's options page sets the clouds', the fog's and the
shadows' quality together; set it to *Custom* for finer control. *Classic* is the cheapest cloud
style; *Cumulus*, *Soft*, *Mixed* and *Cloud detail* cost more wherever there is cloud. Clear sky
costs little: the mod skips the parts of the sky that can't hold any cloud. Volumetric fog and
*Smoother clouds* are off by default. Macs have less graphics power to spare, so expect a lower
frame rate there than on a Windows PC.

## Known limitations

- Mac support has been tested on an Apple Silicon MacBook Air; native Linux is supported but less
  tested. Where the graphics can't run the shaders (they need shader model 4, Metal or OpenGL 3.3)
  the mod switches itself off and says so once per launch, leaving the city unchanged.
- Cloud shadows come from the sun only, not the moon.
- 100% intensity is a heavy overcast, not a sealed one: some sun patches remain, on purpose.
- Reflection cameras also draw the clouds.

## Bug reports

The mod writes its own log, fresh each session:

```
Windows:  %LOCALAPPDATA%\Colossal Order\Cities_Skylines\VolumetricClouds.log
Mac:      ~/Library/Application Support/Colossal Order/Cities_Skylines/VolumetricClouds.log
Linux:    ~/.local/share/Colossal Order/Cities_Skylines/VolumetricClouds.log
```

Tick *Detailed logging* on the mod's options page, make the problem happen again, and attach that
file together with the game's `output_log.txt`, your graphics card, your other mods and a
screenshot.

## How it works

1. **Noise is made at load**: a tiling Perlin-Worley *weather map* (where the clouds are) and
   tiling 3D noise (their shape, billows and erosion), built on the CPU off the main thread.
2. **One density function.** `SampleDensity` in `CloudCommon.cginc` is the only definition of the
   cloud volume. The visible pass and the shadow pass both march it, so clouds and their shadows
   always match.
3. **Shadows are a light cookie.** A 2048² texture marches one sun ray per texel through the volume
   and becomes the sun's cookie, the only thing that darkens the world.
4. **Coverage is solved, not guessed.** A threshold is solved against the weather map's histogram,
   so a coverage setting means a share of the sky. Rain uses the same table with a higher
   threshold, so it can only fall from clouds.
5. **The weather is read, never written.** The one change to the simulation is a Harmony postfix
   on `WeatherManager.SampleRainIntensity`, so rain sound, wet roads and road snow follow the
   clouds.
6. **Rain, lightning, fog and rainbows share the cloud pass** and are blended front to back. The fog
   is lit through the cloud shadow map, which is where the sun shafts come from. The rainbow is an
   arch fixed in the world, colored by Airy's theory of light in a raindrop.
7. **Halos are a shader swap** into the game's light layers; the game's own materials are never
   modified.

What happens before each release is in [`docs/RELEASE-CHECKLIST.md`](docs/RELEASE-CHECKLIST.md).

## Translations

Every text the mod shows lives in
[`VolumetricClouds/Localization/en.xml`](VolumetricClouds/Localization/en.xml), with Spanish
(`es.xml`), French (`fr.xml`) and Simplified Chinese (`zh.xml`) beside it. To add a language, copy
`en.xml` under the game's code for it (`de.xml`, `pt.xml`, …) and translate the text between the
tags; the comment at the top explains the rest. Anything not yet translated shows in English.
Corrections from native speakers are welcome.

## Building

See [BUILDING.md](BUILDING.md). In short, clone with `--recurse-submodules` (the panel's controls
come from the [ModsLibCS1](https://github.com/anthonytoumaibrahim/ModsLibCS1) submodule), then
`dotnet build VolumetricClouds/VolumetricClouds.csproj` builds **and** deploys to the game's mods
folder. The compiled shader bundles are committed, so
Unity 5.6.6 is only needed after editing `UnityProject\Assets`. Pure logic is tested offline by
`tools/test-*.ps1`; the rest is checked in the game through the mod's log.

## Credits and license

Made with the help of AI tools (Claude Code). The goals, design decisions and all of the in-game
testing and tuning are mine.

The source is under the [MIT License](LICENSE), copyright © 2026 Anthony Ibrahim, except
`LightHalo.shader`, `LightHaloDynamic.shader` and the few lines of `FogLamps.shader` taken from
them, which are ports of the game's own halo shader made for compatibility. Those, the shipped
third-party DLLs, the icons and the published cloud-rendering techniques this mod builds on are
credited in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The distant clouds' photographed
skies are from [Poly Haven](https://polyhaven.com) (CC0).

Cities: Skylines is © Colossal Order Ltd., published by Paradox Interactive AB. Unity is a
trademark of Unity Technologies. This is an unofficial fan-made mod, not affiliated with or
endorsed by either. Apart from the ported shader code, this repository holds no game code or
assets; the game's DLLs are referenced from your own installation and never copied.
