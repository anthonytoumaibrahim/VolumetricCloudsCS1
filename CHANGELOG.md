# Changelog

Notable changes to Volumetric Weather. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Each Workshop upload is one version
below; the mods list shows it after the mod's name, and the first line of
`VolumetricClouds.log` gives it with the exact build.

## 1.1.1 — unreleased

Mac and Linux. Nothing in `VolumetricClouds.xml` or in saved cities changes, and on Windows
the clouds are drawn by exactly the same compiled shaders as in 1.1.0.

### New: Mac and Linux

- The mod now runs on the Mac version of the game and on the native Linux version. Until now
  it switched itself off there with a message. Anyone who kept it subscribed gets the clouds
  on their next launch, with the default settings (the volumetric fog and the advanced panel
  are off until you turn them on, as on Windows).
- Windows started with the `-force-glcore` launch option also gets the clouds now, instead of
  the message.
- If the clouds still cannot be drawn on a computer, the message now says what the mod needs
  on that system; on Linux it also says how to run the Windows version through Proton.
- Mac and Linux are new and experimental, and the mod's options page says so on those
  systems, with what to do if the game stutters.

### Fixes

- On a Mac the mod's log file, `VolumetricClouds.log`, was never written: it looked for a
  folder that does not exist there. It is now written next to `VolumetricClouds.xml` on every
  system (on Windows that is the same folder as before).

## 1.1.0 — 2026-09-23

Every value in `VolumetricClouds.xml` and everything in saved cities is kept as it is, and the
clouds look exactly as before until you pick a colour. The file gains five lines (the two
cloud colours, the fog colour, and where the mod's own button sits) and loses six, for
settings that no longer exist (see *Removed* below; the file's other values are untouched).
The settings that were on the options page's Weather, Light, Rendering and Halos tabs are now
in the in-game panel (see below).

### New: cloud and fog colours

- Two colours on the in-game panel's **Clouds** tab: the **sunlit side** (the tops and the
  sides facing the sun or the moon) and the **shaded side** (the undersides, lit by the sky,
  and the faint glow under the clouds at night -- over a city that is often orange). Warm tops
  over cool bases, a golden hour, a blue-grey storm, or pink clouds for fun.
- A colour changes only the hue: how bright the clouds are stays with the brightness settings.
  White, the default, leaves the clouds exactly as they were; greys do the same. The most
  saturated colours (pure red, pure blue) come out darker, to keep them from glowing.
- Each colour has the game's own colour picker, a field that takes `#FFC0CB`,
  `rgb(255, 192, 203)`, `255, 192, 203` or a name such as `pink` (Ctrl+C and Ctrl+V work in
  it), **Copy** and **Paste** buttons, and a **Default** button back to white.
  *Reset all settings to defaults* puts both back to white too.
- The volumetric fog has a colour of its own too, on the **Fog** tab under its cool/warm
  tint (which stays as it was): the same picker, the same rules, white leaves it as it is. The
  glow of the city's lights inside the fog keeps the lights' own colours.
- Rain, lightning and the cloud shadows on the ground keep their own colours.

### Fixes and changes

- Thunder is now quieter the further away the strike is: full volume within a kilometre,
  half at four, with the delay it already had. Until now every clap was as loud as one
  overhead. (The game's own strikes, in heavy rain, keep the game's flat volume.)
- Fixed street and building light halos flashing for a frame, every few seconds, while
  *Customise street and building light halos* is on and the city is growing. Whenever a
  building was built or changed, the game rebuilt the lights of that part of the map with its
  own halo material and drew them once before the mod could swap it back.
- On a Mac, on Linux, or on Windows forced onto OpenGL, where the clouds cannot be drawn, the
  mod now switches itself off when a city loads and says so in a message (once per launch),
  instead of carrying on with flat stand-in clouds. Nothing of it runs in that city. On
  Windows the message lists the usual causes and where the log is, for a report.
- The mod's version is now shown after its name in the mods list and on the options page, and
  at the top of its log, so a bug report can say which release it came from.
- The sky is now set in one place, the in-game panel. The mod's page in the game's options
  keeps only the mod itself: language, the panel's key and button, diagnostics and the reset
  buttons. The Weather, Light, Rendering and Halos settings are the in-game panel's advanced
  tabs; tick *Show advanced options in the in-game panel* on the options page to see them.
  Every value you set before is kept. The in-game panel no longer has a General tab.
- *Reset all settings to defaults* now also turns off *Volumetric fog* and *Show advanced
  options in the in-game panel* (it asks first, as before). Until now the reset left both as
  they were. After a reset the quality preset also reads *High* again, not *Custom*.
- Ticking or unticking *Show advanced options in the in-game panel* while the in-game panel
  is open now leaves it open, behind the options, instead of closing it.
- The mod's own button (the one shown when it is not in Unified UI) now shows its icon, matches
  Play It!'s button in size and style, and can be dragged anywhere on screen; it stays where
  you leave it.
- The log now also gives the computer's memory, and names any other mod that changes the same
  game code as this one, so a crash report can point at the cause.

### Removed

- The light halos have one switch, *Use the replacement halo shader*, instead of two: the
  second one (the game's own glow with only its fog changed) is gone. Anyone who had the halos
  on keeps them on. The *Fog amount* slider is gone too: the halos now grow in foggy weather
  the way the game's own do, following the game's fog or Play It!'s fog slider (or this mod's
  volumetric fog while it is on), instead of a fog value of their own.
- The flat billboard clouds are gone, with the *Raymarched volumetric clouds* switch and the
  two *Billboards* sliders. The 3D clouds are the mod; anyone who had switched to the flat
  pictures gets the real clouds.
- The *Debug: project a checkerboard instead of shadows* switch is gone from the options page.

## 1.0.0 — 2026-09-21

Published on the Steam Workshop as item 3805863507. First release line. Development history
before this point is in the git log; there were no earlier public versions, so nothing below
is a change from something a player has seen.

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
- The game's weather is read, never written, so solar plants and the weather stored in the
  savegame are exactly as they would be without the mod.

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

### Saved with the city

- Each city keeps its own sky in its savegame: the cloud and fog pattern, how far the wind has
  carried them, and the current fog amount. A loaded city looks as it did when it was saved; a
  city with no saved sky gets a new pattern.
- The record is 61 bytes under the mod's own key, and nothing of the game's data is written.
  Removing the mod should not corrupt a save: the game loads it the same way and writes the
  record back unread on the next save.
- *Reset cloud pattern* (Options → General) gives the loaded city a new arrangement of clouds
  and fog without changing any setting. *Reset all settings to defaults* leaves the pattern
  alone.
- Wind and fog drift are kept in double precision and handed to the shaders as wrapping
  phases, so a sky that has drifted for the whole life of a city stays as sharp as a new one,
  and the fog keeps its shape however long a session runs.

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
