# Changelog

Notable changes to Volumetric Weather. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Each Workshop upload is one version
below; the mods list shows it after the mod's name, and the first line of
`VolumetricClouds.log` gives it with the exact build.

## 1.5.1 — 2026-10-21

A fix for players whose game draws with OpenGL: native Linux, and the game started with
`-force-glcore`. Nothing changes in `VolumetricClouds.xml`, profiles or saves, and on Windows
(Direct3D 11) the clouds are drawn exactly as before.

### Fixed

- Under OpenGL the clouds were invisible since 1.5.0, in every style and at any intensity, while
  their shadows, the distant clouds, rain and fog still showed. The graphics driver handed the
  cloud pass the wrong position for every pixel, so each ray looked straight down and never
  reached the clouds. The part of the cloud shader behind it, added for *Smoother clouds*, is
  written another way now, and OpenGL draws the clouds again.

## 1.5.0 — 2026-10-20

A new look for the in-game panel, fog that changes on its own, two new cloud styles, more say over
cumulus clouds, distant clouds from photographed skies, clouds that can hide the sky by day, smoother
clouds, a wind direction of your own, a larger weather pattern for zooming far out, the mod's
defaults in the profile list, waterspouts, and a key that hides the weather while you build.
`VolumetricClouds.xml` gains seventy-five lines and loses eleven; profiles saved before are read the
same way. Twenty-four are new settings: `WeatherKey` (Shift+F4), `WaterspoutChance` (100%: everyone may now see a
waterspout now and then), `LargerWeatherPattern` (off), `CloudsToHorizon` (off), `DynamicDistantClouds`
(on: everyone gets the distant clouds now and then, following the weather), `SkyCirrus`, `SkySheepClouds`, `SkyFarClouds` and
`SkyCloudLayers` at 0%, `PanelSkin`
(3: the new *Frosted glass* look, the one change everybody sees; *Vanilla* puts the old panel
back), `PanelOpenGroups` (the advanced panel's open headings), `DynamicFog` (off), `FogChangesWithWeather`, `MorningFog`,
`MistAfterRain`, `FogInValleys`, `FogOverWater` and `CloudDayOpacity`, all at 0%, `CloudTemporal`
(off), `WindDirection` (0: follow the game) with `WindAngle`, and `CumulusSize`, `CumulusHeight` and
`CumulusAmount` at 100%. At those values the fog, the clouds and the wind are exactly as
before, so apart from the distant clouds and the waterspouts nothing changes for anyone until they
move one. Profiles saved before read the new
sliders at those values. Eleven rows get a value at sunset and at night (*Day, sunset and
night*): a Sunset and a Night line each (`CloudNightOpacity`, the old *Clouds hide the stars at
night*, is already one of them), each written with the player's own value of that row, so their
sky is the same at every hour, and `PanelOpenClocks` (empty). The seven lines of the two 1.3.0 night
switches go (`CloudNightBrightness` and its three values, `CloudNightColors`, `CloudMoonlitColor`,
`CloudNightShadeColor`): where a switch was on, its night values move into the new lines and draw
exactly what they drew. Nine of those rows get a value under an overcast sky too (*Clear sky and
overcast*): three Overcast lines each, one per time of day, written with the player's own clear-sky
value, so their sky is the same in every weather, and `PanelOpenWeather` (empty). The four
cloud brightness rows become one, *Cloud brightness* (`CloudLight`, with its sunset, night and
overcast lines among those): with *Brightness
follows the cloud intensity* on, as it is for nearly everyone, its clear sky and full overcast values
move in and draw exactly what they drew; with it off, the fixed brightness moves in under both, dimmed
under an overcast sky by *Clouds dim at full overcast to* -- the same sunlit sides, while the shaded
undersides at full overcast now dim with it too (they didn't). The four old lines go
(`CloudBrightnessAuto`, `CloudBrightness`, `CloudBrightnessClear`, `CloudBrightnessOvercast`). *Clouds dim at full
overcast to* leaves the panel and stays in the settings file, now dimming only the fog's sunlight, as
it always did for anyone with the automatic brightness on. *Cloud style* gains two choices, *Mixed* and *Soft*: nobody's style
changes with the update. Nothing in saved cities changes. The download grows from 1.3 MB to about 6.5 MB (the
photographed skies).

### New: the panel's look

The in-game panel and the mod's button have a new look, *Frosted glass*, after the photo mode of
Cities: Skylines II: blue-grey glass over a blurred view of the city, a dark title bar, tabs with the
open one in blue, names in capitals with a faint line between rows, choices as a row of buttons (a
list when the names don't fit), thin sliders with a blue fill, and *Save* turning blue while there
are unsaved changes. *Panel look* on the options page (under *The in-game panel*) picks it, or
*See-through* (a dark panel the sky shows through, switches, values in a small box), *Blue*, or
*Vanilla*, the panel exactly as it was. What every row stores is unchanged, so settings, profiles and
saved cities are untouched. *Frosted glass* blurs a small copy of the screen only while the panel is
open.

Every setting in the panel now ends in a small reset button (a round arrow) that puts that one
setting back to how the picked profile has it, or to its default when no profile is picked (and for
the *Now* tab, which profiles don't carry); its tooltip says which, and it is faded while the
setting is already there. The colour rows' text
button *Default* is now the same icon. Long setting names are no longer cut off: each tab's name
column is as wide as its longest name needs, and a name that still doesn't fit is drawn smaller.

In the new looks the tabs are icons (a clock for *Now*, a cloud, two clouds for *Distant clouds*,
fog, a sun, a rain cloud for *Weather*, a light bulb for *Halos*), each tab's name its tooltip, and
the open tab's name stands beside the panel's title; *Vanilla* keeps its named tabs. Every icon in
the panel is Lucide's (ISC licence, credited in THIRD-PARTY-NOTICES.md). The *Clouds* and *Fog* tabs
have headings of their own now (*Height and shape*; *Thickness and height*, *Look and movement*,
*City lights in the fog*, now last on the Fog tab with *Fog lit by the city's lights* at its top
instead of under the fog switch). With *Show advanced options* on, a click on a heading folds its settings
away and back. Each tab starts with its first heading open and the rest folded (also open:
*Cloud brightness* on the Light tab, *Cloud position* on Weather, *Near lights* on Halos), so the
advanced panel opens about as short as the basic one. Which are open is kept in `PanelOpenGroups`,
never in a profile. *Frosted glass* no longer loses its blur in cities where another mod keeps something in
the screen's alpha.

### New: changing fog

Three sliders at the bottom of the Fog tab, under *Changing fog*, each 0% to 100%, 0% by default,
and a *Dynamic fog* switch above them (off) that turns all three on and off at once without losing their
values. *Override fog* on the Now tab overrides them too, and *Fog over water* and *Fog fills
valleys* as well: with it on, the fog is the amount set there and nothing else adds to it or
changes it. With any of them on, *Fog density*, *Fog starts at*, *Fog height* and *Fog break-up* are the most fog
you get: these only ever make it thinner, lower or wispier than that.

- *Fog changes with the weather*: the fog is thickest at night and in heavy fog. On a sunny
  afternoon it is thinner, lower and wispier, and a light foggy spell is lighter than a heavy one.
- *Morning fog*: thick, low fog forms overnight and is thickest around sunrise. During the morning
  it lifts off the ground, thins and breaks up, and by mid-morning it is gone. Some mornings are
  foggier than others. Needs the day/night cycle.
- *Mist after rain*: when a rain ends, light, wispy mist rises over the wet ground and fades as the
  ground dries over the next few hours. Not after snow.

Light halos glow in the fog by how thick it is, so thin mist blooms them less than heavy fog.

### New: fog in valleys and over water

Two more sliders on the Fog tab, 0% to 100%, 0% by default.

- *Fog fills valleys*, under *Fog follows the ground* (it needs it): fog gathers in valleys, river
  gorges and at the foot of slopes, filling them up towards the level of the land around them,
  while hilltops and open high ground stay clear. At 100% there is fog only in hollows.
- *Fog over water*, last under *Changing fog*: fog banks over the sea, lakes and rivers that drift
  ashore with the wind and thin out a few hundred meters inland. The number is how much of the
  water has banks. They come whatever the weather, and take the fog's look.

### New: clouds hide the sky by day

- *Clouds hide the sky* on the Light tab, 0% to 100%, by day 0% by default: the daytime twin of
  what was *Clouds hide the stars at night* (now the same row at night, see *Day, sunset and night*). Far clouds fade into the sky behind them, so with a cubemap (or
  any sky that isn't the game's) that sky, its sun included, showed through a full overcast. At
  100% the far clouds stay solid, and only toward the horizon thin out gently into the haze of
  whatever sky is behind them, so a full overcast doesn't end on a line above the horizon. The Light
  tab's *At night* heading is now *Day and night*.
- Even with *Clouds hide the sky* at 0%, a cubemap (or any sky that isn't the game's) no longer
  shows through far clouds: where they fade into the distance they now fade into the game's own
  sky, without the sun's glow, instead of letting through whatever is behind them. With the game's
  sky that's what showed there anyway, so the clouds look as before. For everyone, nothing to turn
  on. Under Real Light's sky the far clouds fade into it as before.
- *Clouds to the horizon* on the Weather tab (advanced panel), off by default: with it on, under a
  full overcast (95% cover or more, fading in from 90%), the cloud deck carries on all the way to
  the horizon, following the curve of the Earth, instead of ending a few degrees above it with clear
  sky underneath. Seen from high up, it also covers the sky beyond the edge of the map. It hides a
  low sun too. Off, as everyone has it after the update, the clouds end above the horizon, as before.

### New: the Mixed cloud style

A third choice for *Cloud style* on the Clouds tab, next to *Classic* and *Cumulus*: one style for
every kind of weather, so there's no need to switch between the other two.

- In a light sky the clouds are the *Soft* style's (below): puffs of many sizes with soft, wispy
  edges, fewer of them than under *Soft* at the same intensity.
- As the intensity rises they grow and gather, and the Classic layer comes in under and between
  them. From about 85% the layer takes over, with puffs rising out of it.
- At 100% the sky is as fully covered as *Classic* at 100%.
- The mix isn't the same everywhere: some stretches of sky lean toward puffs, others toward the
  layer, and the balance changes as the weather moves through.
- Rain, lightning, the cloud shadows and the rainbows follow the clouds as they're drawn. The
  layer's settings (*Layer thickness*, *Cloud detail*, *Break-up*, *Cloud fragments*) shape its
  layer, as they do under *Cumulus*; *Layer thickness* also sets how tall the puffs can get (twice
  the thickness). *Classic layer* sets how much of the layer comes in, as it does under *Cumulus*.
- The clouds are lit at their own level, as under *Soft*, so at the same *Cloud brightness* a *Mixed*
  sky is about as bright as a *Cumulus* one.

### New: the Soft cloud style

A fourth choice for *Cloud style*: fair-weather clouds made of many puffs, with soft, wispy edges.

- Each cloud is a bunch of rounded puffs of different sizes, with sky showing between them, instead
  of one smooth shape. Small loose puffs float around and between the bigger clouds.
- The edges fray into faint, see-through wisps, like cotton candy, most of all along the bottoms.
  The middles of the biggest clouds stay solid.
- A cloud's height follows how strong the weather is there: in a light sky the clouds are small and
  low, and in heavier weather they grow taller. Their bases are nearly flat.
- About as much of the sky is cloudy as under *Cumulus* at the same intensity.
- From about 60% the Classic layer comes in among the puffs, as under *Cumulus*, and from about 80%
  the puffs stop spreading: the layer, which follows the weather's big shapes, carries the heavy end,
  so a full sky doesn't show the same pattern of puffs over and over. At full intensity the sky is a
  broken overcast. *Classic layer* sets how much of the layer comes in (0%: none).
- The clouds are lit at their own level, so at the same *Cloud brightness* a *Soft* sky is about as
  bright as a *Cumulus* one.
- Rain, lightning, the cloud shadows and the rainbows follow the clouds as they do under *Classic*.
- *Layer thickness* sets how tall the clouds can get (twice the thickness). The rows that shape the
  Classic layer (*Cloud detail*, *Break-up*, *Break-up detail*, *Cloud fragments*) shape its layer
  here too; cloud fragments come in with the layer.
- Like *Cumulus*, it takes a few seconds after you pick it (or load a city) to get ready; the clouds
  are drawn *Classic* until then.

### New: cumulus size, height and amount

Three sliders on the Clouds tab, shown under *Cumulus*, 100% by default (the clouds as before). They
shape the heaps; the Classic layer keeps its own settings.

- *Cumulus size*, 50% to 200%: smaller or bigger clouds, wider and taller together, their lobes with them.
- *Cumulus height*, 50% to 200%: flatter or taller for their width.
- *Cumulus amount*, 0% to 200%: fewer or more of them at the same intensity (0%: none, only the layer).

Bigger and taller clouds take more drawing: there's more cloud to light, and the slab the clouds are
worked through grows with the tallest cloud.

*Classic* and *Cumulus* look exactly as before, and *Classic* stays the default. In the settings
file and in profiles the new style is `<CloudStyle>2</CloudStyle>`; an older version of the mod
reading it ignores that one line and draws *Classic*.

### New: distant clouds

A tab of its own in the in-game panel, *Distant clouds*. *Dynamic distant clouds* (on by default, so
every player can see them after the update) brings them by itself, now and then: most of the time
there are none, and every so often a spell of them drifts in and out again. What they are follows the
weather: cirrus and sheep clouds under a clear sky, cumulus on the horizon when it is partly cloudy,
layered skies towards overcast, each more or less from one spell to the next. Turned off, four sliders
take over, each 0% to 100%, 0% by default; with it on they are greyed (hidden unless *Show advanced
options* is on). They draw real skies, photographed, behind the mod's own clouds: *Cirrus* (high, thin
streaks), *Sheep clouds* (small puffs in fields), *Cumulus on the horizon* and *Cloud layers*
(several kinds at once). They add up, so cirrus can stand over sheep clouds over far cumulus. Only
the clouds are taken from the photographs: the game's sun and sky light them, so they turn gold at
sunset, keep the light longest the higher they stand, glow round the sun, and go dark at night,
where they hide the stars. *Cloud brightness* lights them as it lights the mod's clouds, at night too,
and *Glow under the clouds at night* lights them from below, the low ones most. Everything else in the sky, the mod's clouds, the rain, the fog and
lightning, stands in front of them. They travel with the wind at half the speed of the mod's
clouds, and slowly change as they go: the sky never repeats. Each city sees them turned its own
way. The photographs are taken at their full sharpness, and the sun's glare and starburst in them
are left out.

Each slider walks through its photographs, thinnest first: *Cirrus* shows a faint sky at 33%, sharp
streaks at 67% and a sky full of swirls at 100%; the other three have two photographs (one at 50%,
the other at 100%) or one. The photographs are Poly Haven's, free for any use (CC0).

### New: smoother clouds

- *Smoother clouds (blends frames together: less grain)* in the game's options, under *How the clouds
  are drawn*, off by default. The clouds are worked out at half the screen's size each way, every
  frame, and each frame is blended with the ones before it, moved to where the camera and the wind
  have taken the clouds since. The fine grain the clouds are drawn with averages away; the clouds are a
  little softer. Their shapes don't change. Where a building or a hill reaches into the clouds, or a
  lightning bolt shows, the clouds there are drawn as before. In very fast camera moves the clouds can
  trail for a moment. It is used by the game's main camera only.

### New: wind direction

- *Wind direction* on the Now tab, under *Movement speed*: *Follow the game* (the default, as
  before), *Follow the game slowly* or *Fixed*. The game's wind never settles: every few minutes it
  picks a new direction and the whole sky swings round to it. *Follow the game slowly* follows the
  same wind but turns gradually; *Fixed* keeps one direction, set with *Fixed wind direction*. The
  clouds, the fog and the rain all move with it. On winter maps the game's own snowflakes still lean
  with the game's wind. *Follow the game's weather* sets a fixed wind back to *Follow the game*.
  Like the rest of the Now tab, it isn't saved in profiles.

### New: larger weather pattern

- *Larger weather pattern (same cloud size)* on the Weather tab (advanced panel), under *Weather
  pattern size*, off by default. The clouds are laid out from one pattern that repeats every 13.5 km
  (*Weather pattern size*); zoomed far out, with a camera mod such as ACME, the same layout shows up
  several times across the screen. With this on, the pattern repeats four times further apart (54 km
  at the default size) and the clouds stay the same size. *Weather pattern size* keeps doing what it
  did: it makes the clouds bigger or smaller. Switching it gives the city a new layout of clouds, made
  from the same seed in a few seconds; switching it off brings the city's own clouds back exactly. It
  is off for everyone after the update, so no city's clouds change. It isn't saved in profiles, so
  picking one leaves it as it is. With it on the mod uses a few more MB of memory, and a city takes a
  moment longer to load; the clouds cost no more to draw.

### New: the mod's defaults in the profile list

- *(no profile)* is gone from the profile list; it starts with *(mod defaults)*. Picking it puts every
  setting a profile holds back to the mod's default; like picking a profile, it leaves the Now tab
  and the quality settings alone, and turns the volumetric fog off (its default). Change anything
  with no profile picked and the list shows *(unsaved profile)*, with *Unsaved changes* beside it:
  **Save** then names it as a new profile, and picking *(mod defaults)* again throws the changes away.
  *(mod defaults)* can't be saved over or deleted. The list shows it whenever no profile is picked and the
  sky is at the defaults, so a new player starts on it. A profile is let go only by deleting it or by
  *Reset all*.
- A new profile's name can't be all in parentheses, like *(mod defaults)*: those are the list's own.
  Profiles you already have keep their names.
- **Revert**, beside **Save**: while the picked profile has unsaved changes, it puts the profile's
  saved sky back and throws the changes away.

### New: day, sunset and night

- A clock button beside the reset button on eleven rows: *Sunlit side* and *Shaded side* (Clouds),
  *Fog density*, *Fog brightness*, *Fog tint* and *Fog color* (Fog), *Shadow darkness*, *Cloud
  brightness*, *Cloud density*, *Glow and softer shade* and *Clouds hide the sky* (Light).
  Clicking it shows the row's Day, Sunset and Night values under it, each with its own slider (or
  color) and reset; the clouds blend the three by the sun's height: Day while the sun is at full
  strength, Sunset from 3 degrees (where the game's sunlight starts to fade) down to the horizon,
  Night once it has set (the twilight the night colors always
  used). Sunrise works the same way. While the three are the same the row is one slider moving all
  three; once they differ it says so and shows the value in use now. The panel remembers which
  clocks are open. Only the clouds you see take *Cloud density*'s sunset and night: their shadows
  keep the day's and darken by *Shadow darkness*.
- *Brightness differs at night* with its three *At night* rows, and *Different colors at night* with
  *Moonlit side* and *Shaded side at night*, are gone: their nights are the Night values of the
  brightness and color rows now, carried over exactly. *Clouds hide the stars at night* is the Night
  value of *Clouds hide the sky*. Eight rows fewer.
- For everyone who has the mod, every row starts the same all day, so nothing changes until a
  sunset or night value is moved.

### New: clear sky and overcast

- A cloud button beside the clock on nine rows: *Sunlit side* and *Shaded side*, *Cloud brightness*,
  *Cloud density*, *Glow and softer shade*, *Clouds hide the sky*, *Fog brightness*, *Fog tint* and
  *Fog color*. Clicking it shows the row's Clear sky and Overcast values; the clouds blend them
  straight with the cloud cover (0% all Clear sky, 100% all Overcast), at every time of day: the two
  buttons are separate, and with both open a table shows each time's Clear sky and Overcast side by
  side, a slider each. One button open shows only its own lines: a Clear sky slider shows the day's
  value and moves the sunset's and the night's with it while they are the same; a time you set apart
  with the clock keeps its own value, and the other way round for the clock's lines.
- *Cloud brightness* is one row now: *Brightness follows the cloud intensity*, *Brightness: clear
  sky*, *Brightness: full overcast* and *Cloud brightness (fixed)* are gone, and *Clouds dim at full
  overcast to* has left the panel (see above). Four rows fewer.
- The dynamic fog follows the cover too: an overcast afternoon keeps its fog where a sunny one thins
  and lifts it, and an overcast night forms less morning fog (half under a full overcast).
- Shadows don't change with the weather: their darkness is the same in every weather, and they are
  drawn with the clear sky's daytime density.
- New installs: *Cloud density* overcast 80% (clear sky 150%). Everything else the same in both
  weathers until it is set, and for everyone who has the mod every row starts the same in every
  weather, so nothing changes until an Overcast value is moved.

### New: waterspouts

- **Waterspouts**, for everyone: now and then, under a cloudy sky (rain or not), a small, wispy
  funnel hangs from a cloud over the sea or near the shore, never in the rain or behind it, where it
  would be hidden; it rises into the cloud and is lit like it where they meet.
  About half stay up there; three in ten stop part way, with only a faint thread down to a ring of
  spray; one in five reaches the water. They are small (a few metres to 30 m across), last one to
  five minutes, ride along with their cloud, and die when they come ashore.
  Now and then two or three form under one line of cloud. Only to look at: they damage nothing and
  the game does not know about them. *Waterspout chance* on the Now tab: 100% is about as often as
  on a real coast (about one an hour of cloudy weather near water), 0% none, up to 400%.
- They throw up what is under them: spray over the water (sand, leaves, dust or grit where one is
  by the shore), as a cloud round their foot and, near the camera, as flying bits. The water under
  them darkens a little, with faint foam turning into it.
- Nothing of them is drawn without one, and the cloud pass costs what it did on every other frame.
  Natural Disasters' tornadoes stay the game's.

### New: hide the weather while you build

- **Shift+F4** hides all of Volumetric Weather (clouds, their shadows, rain, fog, lightning and
  waterspouts) until it is pressed again, for when the clouds are in the way while you build. It
  works like unticking *Volumetric Weather* on the Clouds tab, but nothing is saved: loading a city
  always brings the weather back. The key can be changed on the options page,
  under *The in-game panel* (*Hide or show Volumetric Weather*; Backspace clears it). The game uses
  no Shift+F4 of its own.

### Changed

- The Clouds tab's *Color (white leaves the clouds as they are)* heading is just *Color*.

- Smoke, steam, fire and the game's other particle effects are drawn as they were before 1.4.0:
  inside the fog again, so a thick fog hides them. 1.4.0 drew them on top of the fog, which kept
  them from disappearing but showed them undimmed through any amount of fog, and looked worse.
- The *Advanced* tab of the in-game panel is gone: its three rows are back where they were before
  1.4.0, *Break-up detail* on the Clouds tab, *Lit radius around a light* on the Fog tab and *Shadow
  fullness* on the Light tab. Their settings are unchanged.
- *Volumetric fog* no longer warns that it is very heavy on performance: players have found it
  costs little.
- The panel is simpler: the *Now* tab's two status lines (the weather and the fog against the
  game's), its *Follow the game's weather* button and *Test lightning* are gone, and so is the
  cloud brightness line on the *Clouds* tab. *Set lightning activity myself* is now *Override
  lightning*; the *Distant clouds* tab's names are shorter (*Cloud layers* is *Mixed distant
  clouds*), and the near lamps' heading on the *Halos* tab is *Near lights*. No setting changes.
- The clouds are cheaper to draw where the sky is clear: the mod no longer works through stretches
  of sky that can't hold any cloud at the current cover. Nothing on screen changes. It helps most at
  fair weather and when looking up; at a full overcast there is nothing to skip. Off while *Cloud
  fragments* are on.

### Fixed

- A lightning bolt was cut into pieces wherever clouds far behind it showed through: every cloud on
  the line of sight was drawn over the bolt, including the ones behind it. Now only the clouds in
  front of a bolt, and those up to a quarter of its distance behind it, cover it. The bolt still
  disappears into the cloud it comes out of, as before. The same goes for rain and fog behind a bolt.
- Cloud shadows flickered at sunrise and sunset. With the sun that low, they move across the
  ground very fast, and the shadow map only kept up 20 times a second. Now it is redrawn every
  frame while the shadows move that fast. Shadows also used to vanish all at once just before
  sunset. Now they fade out over the last two degrees.

## 1.4.0 — 2026-10-06

Light inside the clouds, Spanish, French and Simplified Chinese translations, a tidier panel with
plainer wording, fixes for smoke in the fog and a ring around the sun, and a thinner fog.
`VolumetricClouds.xml` gains two lines, `LightInsideClouds` and `SunsetLight`, both at 100%: the
new cloud light reaches everyone with the update, profiles saved before it included. Setting both
to 0% gives the clouds exactly the light they had before. Every other setting keeps its name and
value; some sliders only moved (see Changed). New players get a brighter fog. Nothing in saved
cities changes.

### New: light inside the clouds

Two sliders on the Light tab, under *Light inside the clouds*, each 0% to 200%, 100% by default.
The ground's cloud shadows are unchanged.

- *Glow and softer shade*: thin and middling cloud glows with the sun behind it, the way real
  cloud does at sunrise and sunset, instead of turning into dark lumps; a thick overcast seen
  against the sun stays dark. The shaded sides and undersides are lighter, and the sunlit city
  lights the cloud bases (most at noon, hardly at all at a low sun). The Cumulus style and *Cloud
  detail* only: Classic with *Cloud detail* at 0% keeps its look.
- *Sunset light*: at sunset and sunrise the clouds keep the low sun's light until the sun has set
  for them -- a little after it has for the city below -- and turn orange, then red, instead of
  going gray with the game's light. The tops of tall clouds stay lit after their bases. Both
  styles; only the clouds: the city, the fog and the shadows keep the game's light. 200% makes it
  last longer.

### New: Spanish, French and Simplified Chinese

The mod's panel, options page and messages are now in Spanish, French and Simplified Chinese as
well as English. A game set to one of them shows it automatically; the *Language* option on the
options page picks one by hand. These translations were made with AI, so some wording may be off -- corrections
are welcome. Any text not yet translated shows in English.

### Changed

- The in-game panel is less crowded. On the Now, Clouds, Fog and Light tabs, a row that does
  nothing at the moment is hidden instead of grayed out -- every fog row while the fog is off, the
  fixed brightness while brightness is automatic, the cover slider until you override the
  weather's. With *Show advanced options in the in-game panel* checked they show grayed, as before.
- A new *Advanced* tab (with the advanced options on) takes three technical sliders off the basic
  tabs: *Break-up detail* (from Clouds), *Lit radius around a light* (from Fog) and *Shadow
  fullness* (from Light). Every setting keeps its value, in the settings file and in profiles.
- No more tooltips: hovering over a row, a profile button or a color field no longer pops up a
  hint. The labels say what each one does. (The mod's button still shows its name.)
- Labels are in American English (*color*, *gray*) with fewer technical words: for
  example *Quality (raymarch steps)* is now *Cloud and fog quality*, and on the Halos tab *Use the
  replacement halo shader* is now *Use the replacement light halos* and *Size (world radius)* is
  now *Size*. Every setting keeps its place.
- *Fog height* now goes down to 0 m (was 20 m), in 1 m steps, for a thin fog lying on the roads
  and the ground. Every saved height stays as it was.
- *Fog brightness* starts at 150% (was 95%), so the fog is lighter: for new players, and after
  *Reset all settings to defaults*. If you already use the mod, your settings file keeps your
  own number.

### Fixed

- Factory smoke, and the game's other particle effects (steam, fire, dust...), disappeared in
  the volumetric fog and the rain curtains: all the fog behind a plume, down to the ground, was
  drawn over it. They are now drawn over the fog and the rain, as the game draws them over its
  own fog, and still under the clouds when the camera is above them. Like with the game's fog,
  smoke deep inside a thick fog is not dimmed by the fog in front of it.
- A large ring showed around the sun: far-off clouds near it stayed solid and darker while the rest
  of the far clouds faded into the sky, most visible with a low sun or a low cloud brightness. The
  far clouds now fade into the distance the same way across the whole sky, and they still hide the
  sun: what shows through them is the sky without the sun's glow, worked out from the game's own
  sky (changing it with Render It! or Theme Mixer is followed). Far clouds toward the sun look a
  little different for everyone.

## 1.3.1 — 2026-09-30

A fog distance setting, and fixes for the fog, the rain and the lightning. `VolumetricClouds.xml`
gains one line, `FogDistance`, at 9 km: the fog reaches exactly as far as before, at the same
cost. Nothing in saved cities changes. What changes for everyone with the update: power lines,
water and lamp glows are inside the fog, the rain and the rainbow again instead of over them,
lightning bolts look as they did in 1.2, and a fog that follows the ground covers high hills to
their tops (see *Fixed* and *Changed*).

### New: fog distance

- *Fog distance* on the Fog tab: how far away the volumetric fog is drawn, from 9 km (the
  default: the fog as it has always been) to 20 km. Farther, the fog stays at full strength
  further out and thins over the last 9 km; it also costs more performance, since the longer
  view through the fog gets more steps so it stays as smooth.

### Fixed

- Power lines, water, the glow of street lamps and smoke showed over the volumetric fog, the
  rain curtains and the rainbow instead of inside them, whenever the camera was under the
  clouds; and the clouds could show over the fog in front of them. 1.3.0 moved the clouds before
  those see-through objects (so power lines no longer fade into the clouds behind them) and
  meant the fog, the rain and the rainbow to stay after them, but they were moved before them
  with the clouds. They are after them again, as in 1.2.
- With *Fog follows the ground*, the fog on high hills was cut off: at eye level on hills higher
  than the camera, when the camera was above the fog, and at one height above the ground round
  the camera, when the camera was in it. It now covers hills up to their tops.

### Changed

- Lightning bolts are drawn as in 1.2 again: before the clouds, which fade the top of the
  channel into the cloud it comes out of. 1.3.0 meant to draw them after the clouds, dimmed only
  by what is in front of them; as released, the cloud in front of a bolt dimmed it twice, and
  drawn as meant it looked as if in front of the cloud.

## 1.3.0 — 2026-09-29

Profiles, rainbows, cloud colours and a cloud brightness of their own at night, snow on winter
maps, and a way to move the clouds. Every value in `VolumetricClouds.xml` is kept, and the file
gains eleven lines: `Profile` (empty: no profile), the three night-colour settings and the four
night-brightness settings (both switched off, so the clouds keep the colours and the brightness
you gave them, day and night), Cumulus's `CumulusLayer` (100%: Cumulus as in 1.2), and
`Rainbows` and `RainbowChance` (both 100%: rainbows as in nature). Saved
cities keep their sky, and are saved with one more
value, where you moved their clouds (none until you do); an older version of the mod reads them
as before. What changes for everyone with the update: rainbows appear where the sun shines on
the rain (see *New: rainbows*); on winter maps the snow falls only under
the clouds and road snow follows it (see *New: snow*); the sun and its sunset glow no longer
shine through the clouds, the game's hazy band above the horizon is gone while the mod's clouds are shown, the
volumetric fog is less grainy (and costs more), lightning bolts are no longer cut off by
the clouds behind them, the Light tab is in the in-game panel without advanced options, the
Rendering tab's settings are on the mod's options page instead, and switching the volumetric
fog on no longer shows a warning popup.
Only a new install, or "Reset all settings", gets the new clear-sky cloud brightness of 150%
(300% before): an existing settings file keeps its value.

### New: profiles

- Save a whole sky under a name and switch between skies with one click. The list is at the
  top of the in-game panel, above the tabs, with **Save**, **+** and **-** beside it.
- **+** saves the sky as it is now as a new profile: type a name and press Enter (Escape
  cancels). The new profile is then the picked one.
- Picking a profile puts its whole sky in, the volumetric fog switch included. A setting the
  profile does not have goes back to its default.
- What you change after that is not saved into the profile until you press **Save**. Until then
  the bar says *Unsaved changes*, and picking another profile asks first, since those changes
  would be lost. Pick "(no profile)" to go on without one: the sky stays as it is.
- **-** deletes the picked profile's file, after asking. The sky stays as it is.
- A profile holds every setting of the sky: every tab of the panel but Now, the colours and the
  switches. The Now tab's overrides are the weather of the moment, so picking a profile leaves
  them as they are. A profile does not hold the settings that describe the computer either (the
  quality settings on the mod's options page, the language, the keys, the button), so a profile
  from a faster computer never slows yours down. Nor does it hold a city's cloud pattern, which
  stays in the city's save.
- Each profile is a file of its own, named after it, in the `VolumetricCloudsProfiles` folder
  beside `VolumetricClouds.xml`: `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\` on Windows,
  `~/Library/Application Support/Colossal Order/Cities_Skylines/` on a Mac,
  `~/.local/share/Colossal Order/Cities_Skylines/` on Linux. Copy one to share a sky, rename one
  to rename it, or drop one in with the game running: the list is read each time it opens.
- Profiles are written like `VolumetricClouds.xml` and can be edited by hand, even with the
  game running: an edit of the picked profile is put in within a second. A file that cannot be
  read never breaks anything: the sky keeps running, the file is left alone until it can be
  read (or you press Save), and the log says why.
- "Reset all settings" lets go of the picked profile first and never changes a profile. Your
  profiles are kept, and picking one brings its sky back.

### New: rainbows

- When the sun shines on a shower, a rainbow stands in it: an arch near where you are looking,
  its feet on the ground and its top just under the clouds, violet inside to red outside, the sky
  a little brighter within it, and a fainter second bow further out with its colours reversed.
  It leans back away from the sun, the more so the higher the sun is. It stays where it is while
  you look around, zoom and circle it. It shows only where it passes through rain in sunshine,
  breaks where the rain behind it is in the shade, and fades out if you go round behind it. When
  you move far away, or the shower drifts out of it, it fades and a new one forms where you are
  looking, if there is sunlit rain there. One at a time. None with the sun more than 60 degrees
  up. Rain or fog in front of it hides it.
- "Rainbows" on the Weather tab (with advanced options on) sets how strong they are: 100%, the
  default, is as in nature, 200% more vivid; 0% switches them off, and nothing of them is drawn.
- "Rainbow chance" on the Now tab is the share of rains that bring rainbows at all: each rain
  rolls its dice when it starts. 100%, the default, is nature, where every rain the sun shines on
  has one.
- Rainbows need the mod's rain and the cloud shadows (the shadows are how the mod knows where the
  sun reaches the rain), so there are none with "Shadow darkness" or "Rain curtains under clouds"
  at 0%. There are none in snow, at night, or by moonlight. The game keeps a rainbow in its
  weather but never draws one; the mod does not read it.

### New: different cloud colours at night

- "Different colours at night" on the Clouds tab, under the two cloud colours, gives the
  clouds two colours of their own at night: the moonlit side and the shaded side, whose colour
  also tints the glow under the clouds (city light at night is often orange). Through dusk and
  dawn the colours change gradually from one pair to the other.
- It starts switched off: the two colours above are then used day and night, exactly as
  before.

### New: a different cloud brightness at night

- "Brightness differs at night" on the Light tab, under the cloud brightness settings, gives
  the clouds a brightness of their own at night. Tick it and the night's sliders appear under it: *At night: clear sky* and *At night: full overcast* with
  automatic brightness, *At night: fixed brightness* without. Through dusk and dawn the
  brightness changes gradually from the day's to the night's, as the night colours do.
- Ticked for the first time, the night's sliders start at the day's values, so nothing changes
  until you move one; if you set them before, they are kept.
- It starts switched off: the brightness settings are then used day and night, exactly as
  before. The brightness line on the Clouds tab shows the brightness in use, the night's at
  night.

### New: the Classic layer in Cumulus

- "Classic layer" on the Clouds tab, under the cloud style, shown while Cumulus is picked: how
  much of the Classic cloud layer fills in among the Cumulus heaps. 100%, the default, is Cumulus
  exactly as in 1.2; lower leaves fewer and smaller pieces of the layer, and 0% the heaps alone.

### New: snow on winter maps

- On a winter map (the Snowfall DLC) the snow now falls only under the clouds, as the rain does
  on other maps: whiter curtains hang from the snowing clouds in the distance, and near the
  camera the game's own snowflakes fall wherever it is snowing and stop where it is not. Above
  the clouds' base there are none. Until now the mod stood down on winter maps and the game's
  snow fell everywhere.
- Road snow follows the clouds: it builds up only where it is snowing, as roads only get wet
  where it rains. This is the existing "Rain sound, wet roads and road snow follow the clouds"
  switch (the Weather tab, with advanced options on), which is on by default; switch it off for
  the game's own snow on every road at once.
- "Replace the game's rain and snow" switched off gives the game's snow everywhere, as before.
  The curtains follow "Rain curtains under clouds" and the fall speed; the streak settings are
  for rain only.

### New: cloud position

- "Cloud position X" and "Cloud position Y" move the city's clouds along the map's two axes, up
  to 10 km each way: to put a cloud over a spot for a screenshot, or a clearing. Everything moves
  together: the clouds, their shadows, the rain and the lightning. They are on the Weather tab,
  with advanced options on.
- Where you moved them is saved with the city, like its cloud pattern, so every city keeps its
  own; profiles and "Reset all settings" leave it alone.

### Changed

- The in-game panel's Rendering tab is gone: its settings are on the mod's options page (Esc,
  Options, then Volumetric Weather under the mods' settings), at the top, where they can be
  found without switching on advanced options. The Quality preset sets the clouds', the fog's
  and the shadows' quality, as before; pick *Custom* and the three settings it moves (the
  raymarch quality, the cloud shadows' resolution and their updates per second) appear under it,
  to set one by one. "Buildings and terrain hide clouds behind them" follows, under "How the
  clouds are drawn". Your values are kept.
- The volumetric fog is much less grainy: its steps now crowd where the view enters the fog,
  which is the part you see, and at the High quality preset it takes twice as many (58). With
  the fog on, High costs more than before, most of all with the camera down in it; Medium takes
  26 steps (19 before) and Low 8 (10 before), so a slower computer pays no more than it did.
  The quality preset sets it (or, under *Custom*, the Quality setting).
- The Light tab (cloud shadows, cloud brightness, the clouds at night) is now always in the
  in-game panel, beside Now, Clouds and Fog. "Show advanced options in the in-game panel" adds
  Weather and Halos.
- "Brightness: clear sky" on the Light tab starts at 150% (it was 300%), for new installs and
  after "Reset all settings". If you run the mod already, your value is kept: set it on the
  Light tab.

### Fixed

- A hand edit of `VolumetricClouds.xml` saved within a second of a change in the game could be
  written over before the mod had read it. The mod now reads the edit first and saves after it.
- Lightning bolts were drawn before the clouds, so the clouds BEHIND a bolt were laid over it
  too: seen from under the clouds, a channel could look cut off or as if it started behind the
  clouds. Bolts are now drawn after the clouds and dimmed only by the cloud, rain and fog in
  front of them. The glow of lightning inside the clouds is unchanged.
- Power lines seen against a cloudy sky no longer fade into the clouds. The game draws its
  power lines, the glow of street lamps, smoke and snowflakes see-through, and the clouds were
  drawn after them and laid over them, the clouds behind them too. From under the clouds they
  are now drawn over the clouds; the rain and fog in front of them still cover them as before.
  From above the clouds, nothing changes.
- The clouds block the sun: the sun and the bright glow round it no longer shine through the
  clouds in front of them. The clouds fade into the distance by becoming see-through, and towards
  the sun that let the sun itself through, even a thick cloud a few kilometres away. Now, wherever
  the sky's glow round the sun is bright (how far depends on the map's sky: about 30 to 50
  degrees round the sun on most maps), the clouds keep their full cover into the distance, as
  they already did at night for the stars. Everywhere else they fade into the distance as before.
- The game's fog draws a hazy band over the lowest part of the sky, which covered the distant
  clouds, most visibly at night. While the mod's clouds are shown ("Volumetric Weather" ticked)
  that band is taken off the sky; the game's fog on the ground and at the map's edge is
  unchanged, and the band comes back as it was when the clouds are hidden. At night the clouds
  now reach as far as they are drawn, instead of fading out about 10 km away.
- With "Shadow darkness" at 0%, the volumetric fog got no sunlight at all and stayed dark. It is
  now lit as it is with the cloud shadows switched off.
- The in-game panel no longer runs off the bottom of the screen on a tall tab. At the game's
  usual interface size, the Weather tab (with advanced options on) ended below the screen, its
  last settings out of reach; the panel now moves up to fit.
- If another mod switches the game's own rain clouds back on, they are now left to it for the
  city, instead of being switched off again every frame.
- Without the Unified UI mod, the mod no longer adds a Unified UI panel of its own to the
  game. Its button goes in the Unified UI bar when the Unified UI mod is enabled, or when
  another mod already shows that bar; otherwise it is the mod's own button on the screen,
  which can be dragged anywhere. If your button was in a Unified UI bar that only this mod
  put there, it is now that on-screen button. Settings are unchanged.

### Removed

- The warning popup when switching "Volumetric fog" on. The switch still says how heavy the
  fog is, and the warning under it stays.

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
