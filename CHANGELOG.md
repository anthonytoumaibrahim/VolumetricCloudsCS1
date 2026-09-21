# Changelog

Notable changes to Volumetric Weather. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the project uses an
`AssemblyVersion` of `1.0.*`, so the build number moves on every compile and only the entries
below mark a release.

## 1.0.0 — unreleased

First release line. Development history before this point is in the git log; there were no
earlier public versions, so nothing below is a change from something a player has seen.

### Clouds and shadows

- Raymarched volumetric cloud layer with adjustable altitude, thickness, density, break-up and
  brightness, built from a tiling Perlin-Worley weather field and a 3D erosion noise.
- Real cloud shadows, cast by marching the same density function into the sun's directional
  light cookie. Gaps get full sun; nothing is dimmed globally.
- Coverage is solved against a threshold table, so an intensity setting means a share of the
  sky rather than an arbitrary number.
- Automatic cloud brightness driven by cover, with a manual override.

### Weather

- Cloud cover, rain, fog amount and wind direction follow the game's weather, which means
  random weather, disasters and weather mods such as Play It! all drive the sky.
- Per-channel overrides for photo work, and a *Follow the game's weather* button that clears
  all of them at once.
- The weather is read, never written: solar plants and the savegame are unaffected.

### Rain and lightning

- Rain curtains under the clouds that are raining, world-anchored near-camera streaks, and a
  rain mask that is a subset of the cloud cover by construction.
- Rain sound and wet roads follow the clouds, through the mod's single Harmony postfix on
  `WeatherManager.SampleRainIntensity`.
- The game's own rain renderers are silenced and restored; the mod stands down on winter maps.
- Lightning lights the clouds and the rain from inside, with a redrawn bolt, thunder delayed by
  the speed of sound, and visual-only storm lightning that never starts a fire. The game's own
  strikes — the ones that burn things — are untouched.

### Fog *(opt-in)*

- Volumetric fog as a cloud layer lying on the terrain: a real extinction, a top surface,
  self-shadowing, flow, and lighting through the cloud shadow map, which is what produces sun
  shafts under a broken sky.
- Fog amount is an exact share of the map, measured against what the GPU actually samples.
- Level by default, measured from the map's sea level like the game's own fog, so valleys fill
  and hills stand out of it; *Fog follows the ground* drapes it over the terrain instead.
- At night the fog is lit by the city's lights: it glows round street lamps, building lights and
  headlights, in their own colours, only where there is fog. The light halos are not changed. On
  by default with the fog; *Fog lit by the city's lights* switches it off.
- *Fog starts at* raises the underside of the layer, for fog that only exists higher up: around
  the hilltops when level, or hanging over everything like low cloud when it follows the ground.
- Off by default and confirmed before it turns on, because it is the most expensive feature.

### Night

- Clouds occlude the stars behind them instead of letting them leak through.
- A small, even, pale glow on the cloud undersides at night.

### Light halos *(opt-in)*

- Replacement halo shaders for both the batched lights (street lamps, building lights) and the
  dynamic lights (vehicles, and props from mods such as Intersection Marking Tool), with
  brightness, tightness and radius controls and a separate near-camera blend.
- Fixes the game's own bug where a halo becomes a solid box at fog values at or below -0.5.
- Removes the need for the Persistent Fog Adjuster hack, so world fog can be real again.

### Interface

- In-game panel on F4 (rebindable) or the Unified UI button: *Now*, *Clouds* and *Fog*.
- A five-tab page in the game's own mod options — *Weather*, *Light*, *Rendering*, *Halos*,
  *General* — each tab scrolling on its own.
- One settings catalog defines every setting once: its label, default, range, where it appears
  and what it greys out. Both interfaces and *Reset all settings to defaults* are drawn from it.
- Optionally mirror the five options tabs into the in-game panel.
- A quality preset that moves raymarch steps and shadow-map cost together, and *Detailed
  logging* for bug reports.
- Settings are kept in `VolumetricClouds.xml` next to the game's other mod settings, one
  commented line per setting with its range and default. It can be edited by hand, even with
  the game running: changes are picked up within a second. Out-of-range values are pulled
  back into range and bad lines are ignored, each noted in the log; a file that cannot be read
  at all is left untouched rather than overwritten.
- Tooltips in plain words: what each setting does and what raising or lowering it does.
- Ready for translation: every text is in a language file, and *Options → General* has a
  *Language* choice (following the game's language by default). English only for now.

### Known at release

- Windows / Direct3D 11 only; performance measured on one GPU, with the *Low* and *Medium*
  presets still provisional. No moon shadows. Secondary cameras also render the cloud volume.
  Scenarios get no clouds. See the README for the full list.
