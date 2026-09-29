# Changelog

Notable changes to Volumetric Weather. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Each Workshop upload is one version
below; the mods list shows it after the mod's name, and the first line of
`VolumetricClouds.log` gives it with the exact build.

## 1.2.1 — unreleased

Profiles, cloud colours and a cloud brightness of their own at night, snow on winter maps, and
a way to move the clouds. Every value in `VolumetricClouds.xml` is kept, and the file gains
eight lines: `Profile` (empty: no profile), the three night-colour settings and the four
night-brightness settings (both switched off, so the clouds keep the colours and the brightness
you gave them, day and night). Saved cities keep their sky, and are saved with one more
value, where you moved their clouds (none until you do); an older version of the mod reads them
as before. What changes for everyone with the update: on winter maps the mod now draws the
snow and road snow follows the clouds (see *New: snow*); the sun no longer shines through the
clouds, the game's hazy band above the horizon is gone while the mod's clouds are shown, the
volumetric fog is less grainy (and costs more), and lightning bolts are no longer cut off by
the clouds behind them.

### New: profiles

- Save a whole sky under a name and switch between skies with one click. The list is at the
  top of the in-game panel, above the tabs, with a **+** and a **-** button beside it.
- **+** saves the sky as it is now as a new profile: type a name and press Enter (Escape
  cancels). The new profile is then the picked one.
- While a profile is picked, every change you make in the panel is saved into it as well, so
  there is no Save button and nothing to forget. Pick "(no profile)" to go on without one: the
  sky stays as it is, and the profile stays as it was.
- Picking a profile puts its whole sky in, the volumetric fog switch included, without asking.
  A setting the profile does not have goes back to its default.
- **-** deletes the picked profile's file, after asking. The sky stays as it is.
- A profile holds every setting of the sky: every tab of the panel, the colours and the
  switches. It does not hold the settings that describe the computer (the Rendering tab's
  quality settings, the language, the keys, the button), so a profile from a faster computer
  never slows yours down. Nor does it hold a city's cloud pattern, which stays in the city's
  save.
- Each profile is a file of its own, named after it, in the `VolumetricCloudsProfiles` folder
  beside `VolumetricClouds.xml`: `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\` on Windows,
  `~/Library/Application Support/Colossal Order/Cities_Skylines/` on a Mac,
  `~/.local/share/Colossal Order/Cities_Skylines/` on Linux. Copy one to share a sky, rename one
  to rename it, or drop one in with the game running: the list is read each time it opens.
- Profiles are written like `VolumetricClouds.xml` and can be edited by hand, even with the
  game running. A file that cannot be read never breaks anything: the sky keeps running, the
  file is left alone until it can be read, and the log says why.
- "Reset all settings" lets go of the picked profile first, so it never resets a profile. Your
  profiles are kept, and picking one brings its sky back.

### New: different cloud colours at night

- "Different colours at night" on the Clouds tab, under the two cloud colours, gives the
  clouds two colours of their own at night: the moonlit side and the shaded side, whose colour
  also tints the glow under the clouds (city light at night is often orange). Through dusk and
  dawn the colours change gradually from one pair to the other.
- It starts switched off: the two colours above are then used day and night, exactly as
  before.

### New: a different cloud brightness at night

- "Brightness differs at night" on the Light tab (with advanced options on), under the cloud
  brightness settings, gives the clouds a brightness of their own at night. Tick it and the
  night's sliders appear under it: *At night: clear sky* and *At night: full overcast* with
  automatic brightness, *At night: fixed brightness* without. Through dusk and dawn the
  brightness changes gradually from the day's to the night's, as the night colours do.
- Ticked for the first time, the night's sliders start at the day's values, so nothing changes
  until you move one; if you set them before, they are kept.
- It starts switched off: the brightness settings are then used day and night, exactly as
  before. The brightness line on the Clouds tab shows the brightness in use, the night's at
  night.

### New: snow on winter maps

- On a winter map (the Snowfall DLC) the mod now draws the snow, as it draws the rain on other
  maps: snowfall only under the clouds it falls from, whiter curtains hanging from them in the
  distance, and near the camera round, soft snowflakes that fall slowly and drift from side to
  side. Until now the mod stood down on winter maps and the game drew its own snow everywhere.
- The snowflakes are seen from much higher up than rain streaks: up to 2.5 times the height set
  by "Streaks fade out above" (1250 m by default), since from a city view the flakes are what
  shows the snow.
- Road snow follows the clouds: it builds up only where it is snowing, as roads only get wet
  where it rains. This is the existing "Rain sound, wet roads and road snow follow the clouds"
  switch (the Weather tab, with advanced options on), which is on by default; switch it off for
  the game's own snow on every road at once.
- The rain settings work for the snow too: "Replace the game's rain and snow" switches it all
  off, "Streak width" sets the size of the snowflakes, and the streak brightness and colour and
  the fall speed apply to them.

### New: cloud position

- "Cloud position X" and "Cloud position Y" move the city's clouds along the map's two axes, up
  to 10 km each way: to put a cloud over a spot for a screenshot, or a clearing. Everything moves
  together: the clouds, their shadows, the rain and the lightning. They are on the Weather tab,
  with advanced options on.
- Where you moved them is saved with the city, like its cloud pattern, so every city keeps its
  own; profiles and "Reset all settings" leave it alone.

### Changed

- The Quality preset is now also on the mod's options page (Esc, Options, then Volumetric
  Weather under the mods' settings), at the top, so it can be found without switching on the
  in-game panel's advanced options. It sets the clouds', the fog's and the shadows' quality, as
  before, and is still on the Rendering tab too. Pick *Custom* there and the three settings it
  moves (the raymarch quality, the cloud shadows' resolution and their updates per second)
  appear under it, to set one by one.
- The volumetric fog is much less grainy: its steps now crowd where the view enters the fog,
  which is the part you see, and at the High quality preset it takes twice as many (58). With
  the fog on, High costs more than before, most of all with the camera down in it; Medium takes
  26 steps (19 before) and Low 8 (10 before), so a slower computer pays no more than it did.
  The Quality setting on the Rendering tab sets it.

### Fixed

- A hand edit of `VolumetricClouds.xml` saved within a second of a change in the game could be
  written over before the mod had read it. The mod now reads the edit first and saves after it.
- Lightning bolts were drawn before the clouds, so the clouds BEHIND a bolt were laid over it
  too: seen from under the clouds, a channel could look cut off or as if it started behind the
  clouds. Bolts are now drawn after the clouds and dimmed only by the cloud, rain and fog in
  front of them. The glow of lightning inside the clouds is unchanged.
- The sun no longer shines through distant clouds in the morning and evening: near the sun the
  clouds keep their full cover into the distance, as they already did at night for the stars.
- The game's fog draws a hazy band over the lowest part of the sky, which covered the distant
  clouds, most visibly at night. While the mod's clouds are shown ("Volumetric Weather" ticked)
  that band is taken off the sky; the game's fog on the ground and at the map's edge is
  unchanged, and the band comes back as it was when the clouds are hidden. At night the clouds
  now reach as far as they are drawn, instead of fading out about 10 km away.

- Without the Unified UI mod, the mod no longer adds a Unified UI panel of its own to the
  game. Its button goes in the Unified UI bar when the Unified UI mod is enabled, or when
  another mod already shows that bar; otherwise it is the mod's own button on the screen,
  which can be dragged anywhere. If your button was in a Unified UI bar that only this mod
  put there, it is now that on-screen button. Settings are unchanged.

## 1.2.0 — 2026-09-26

A new cloud style, Cumulus, and much more detailed clouds. Saved cities keep their sky: the
big clouds come from the same pattern as before. Every value in `VolumetricClouds.xml` is kept;
the file gains two lines. "Cloud style" starts at Classic for everyone, new installs and
"Reset all settings" included, so an existing sky only changes by the cloud detail below.
"Cloud detail" starts at 100%.

### New: the Cumulus cloud style

- Heaped cumulus clouds of every size and height, with flat bases and billowing tops that
  shade each other, modelled on the KSP clouds of EVE-Redux, drawn together with the Classic
  cloud layer among and under them. Every heap is carved out of one large noise, and the
  weather pattern decides where they gather. The layer is thicker where the weather pattern is
  heavy and thinner where it is light.
- "Cloud style" at the top of the in-game panel's Clouds tab switches between Classic, the
  cloud layer the mod has always drawn, and Cumulus. It starts at Classic.
- Cumulus gives somewhat more cloud than Classic at the same Intensity, because the heaps come
  on top of the layer. Above 85% no more heaps are added and the layer fills the sky, so a
  near-overcast sky stays natural instead of showing the heaps' repeating pattern. As with
  Classic, 100% keeps a few breaks.
- Every Classic setting still works under Cumulus: Layer thickness, Cloud detail, Break-up,
  Break-up detail and the cloud fragments shape the layer, and Cloud altitude, Cloud density,
  Weather pattern size, the brightness and the colours apply to both.
- The first time Cumulus is chosen in a city, its noise takes a few seconds to make; the
  clouds stay Classic until then.
- Its clouds are taller than Classic's, so they are drawn with twice the steps, and cost up to
  about twice as much where there are clouds. On a slower graphics card, use Classic or lower
  the Quality setting on the Rendering tab.
- Nothing new is stored in saved cities: the noise comes from the pattern the city already
  keeps, and "Reset cloud pattern" renews it with everything else.

### New: cloud detail

- The clouds get crisp edges, puffy billows and real light and shade: bright sunlit tops,
  grey shaded undersides, and small puffs that shade each other. Built from the documented
  techniques behind the best-known real-time clouds (Horizon Zero Dawn's, and the KSP clouds
  of EVE-Redux).
- "Cloud detail" on the in-game panel's Clouds tab, right under "Layer thickness", sets how
  sculpted they are. It starts at 100% for everyone, existing players included; 0% draws the
  soft clouds from before, and the values in between change them gradually from one to the
  other.
- "Break-up" and "Break-up detail" keep their jobs with it on: how torn the clouds are, and
  how big the billows are (500 m at the default 4x).
- It costs more to draw where there are clouds. On a slower graphics card, set it to 0% (the
  values in between cost a little more than 100%), or lower the Quality setting on the
  Rendering tab.
- Nothing new is stored in saved cities: the detail comes from the pattern the city already
  keeps, and "Reset cloud pattern" renews it with everything else.

### Changed

- The fine grain in the clouds, the rain and the volumetric fog is now even, instead of
  gathering into small dots (the steps start at blue-noise offsets). It showed most on the
  Cumulus clouds and in the fog.
- Seen from high above, the clouds now reach far towards the horizon instead of ending about
  20 km out. The higher the camera is over the clouds, the further they reach. From under the
  clouds nothing changes.
- The rain streaks near the camera are smaller and a little dimmer by default: half the length
  and width, 80% of the brightness. This reaches new installs and "Reset all settings"; everyone
  else keeps their streak settings, and 100% is still the old look.

## 1.1.1 — 2026-09-24

Mac and Linux, cloud fragments, and settings for the rain streaks. Saved cities do not change,
and every value in `VolumetricClouds.xml` is kept except those of the two removed vehicle-light
settings (see *Removed* below). Every new setting starts at a value that keeps the sky
exactly as it was (cloud fragments off, the rain streaks at 100% and white). The cloud shaders
were rebuilt for the fragments; with them off, the clouds look as before.

### New: cloud fragments

- Small, thin, see-through shreds of cloud around and between the big clouds, the ragged
  pieces real skies have beside their large clouds. They sit in the same layer, on the same
  flat base, move with the clouds and cast their own small shadows; the big clouds are drawn
  exactly as before.
- "Cloud fragments" on the in-game panel's Clouds tab sets how much of the sky they cover. It
  starts at 0 (none).
- "Fragment style", right under it, picks their look: Clustered (gathered around the edges of
  the big clouds), Scattered (spread over the sky in random patches) or Wispy (scattered, but
  thinner, fainter and more torn).
- They need some open sky: at a very high cloud intensity the big clouds leave no room for
  them. Rain still falls only from the big clouds.
- Nothing new is stored in saved cities: the fragments come from the same pattern and wind
  the city already keeps.

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

### New: rain streak settings

- Five new settings for the raindrops that fall past the camera: streak length, streak width,
  streak brightness, streak colour and rain fall speed. They are in the Rain group of the
  in-game panel's Weather tab, which appears once "Show advanced options in the in-game
  panel" is ticked.
- At their defaults (100%, and white for the colour) the rain looks exactly as before.
- The fall speed also moves the rain curtains seen in the distance, so both stay in step.
- The colour works like the cloud colours: it changes the hue only, and the streaks still
  follow the sky's light.

### Fixes and changes

- On a Mac the mod's log file, `VolumetricClouds.log`, was never written: it looked for a
  folder that does not exist there. It is now written next to `VolumetricClouds.xml` on every
  system (on Windows that is the same folder as before).
- Every tooltip is now one short line. The long ones ran out of the game's tooltip box.
- The switch at the top of the in-game panel's Clouds tab, which turns the whole of this
  mod's sky off and on, is now labelled *Volumetric Weather* instead of *Show clouds*. The
  line under the mod's name in the mods list is shorter, so it no longer wraps.

### Removed

- *Adjust dynamic lights (vehicles)* on the Halos tab, with its two sliders, *Dynamic light
  size* and *Hide dynamic halos within*. It did not do what it said: besides vehicles it also
  shrank lamps placed with Intersection Marking Tool and other props, and it changed the light
  itself rather than only its glow, so switching it on put those lights out. Anyone who had it
  on gets the game's lights back.
- *Vehicle light halos* on the same tab. It made no visible difference anywhere between 0% and
  300%. Vehicle lights keep the glow they had at its default.

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
