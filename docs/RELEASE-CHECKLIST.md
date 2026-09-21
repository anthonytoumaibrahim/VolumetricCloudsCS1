# Release checklist — Steam Workshop

Everything that has to be true before this mod is published, in the order the steps block each
other. Nothing in *Publish* is done until everything above it is ticked.

The facts about the game's upload flow below were read from the IL of
`WorkshopModUploadPanel`, `PluginInfo` and `WorkshopHelper`, not from folklore. Where something
has not been done here before, it says so.

## Where each piece of text and art actually lives

This is the part that is easy to get wrong: almost nothing about the Workshop page lives in the
repository, and the one image that does is not in the mod folder.

| Piece | Defined where | Notes |
| --- | --- | --- |
| Mod name in Content Manager | `Mod.Name` in `Mod.cs` | Compiled into the DLL |
| Mod description in Content Manager | `Mod.Description` in `Mod.cs` | Compiled into the DLL. Still the first-day one-liner |
| Button icon | `VolumetricClouds/Resources/CloudIcon.png` | Embedded resource; 40×40 RGBA, transparent background |
| **Workshop title** | Typed into the game's Share panel at upload | Pre-filled from the mod folder name; editable afterwards on the item's Steam page |
| **Workshop description** | Typed into the same panel | **BBCode, not Markdown** — the README cannot be pasted as-is. Editable on the Steam page afterwards |
| **Change note** | Typed into the same panel, per update | |
| **Preview image** | A local file — but in the staging folder the game creates per upload, **not** the mod folder | `PreviewImage.png`, square, 512×512, under 1 MB. A copy kept in the mod folder is only a copy: it rides along into the uploaded content and is never used as the preview |
| **Screenshots and video** | Steam only — the item's page, *Add/Edit images* | The game's panel has no screenshot field. 1920×1080 or larger |
| Tags | The game sends one tag, `Mod`; anything else is set on the Steam page | Verified: `IsCameraScript ? "Cinematic Cameras" : "Mod"` |
| Required items (Harmony) | Steam page only | |
| Visibility | Steam page only — the panel has no control for it | Assume the item is live the moment the upload finishes |

Because the title and description exist only inside Steam, keep the source text in the repo and
treat the Workshop box as a copy, so the next update is an edit, not a rewrite from memory. The
masters live in `Workshop/` at the repo root (tracked, never deployed):

| File | What it is |
| --- | --- |
| `Workshop/PreviewImage.png` | The cover image, dropped into the staging folder at upload |
| `Workshop/Screenshots/` | The gallery, uploaded on the Steam page by hand |
| `Workshop/Description.bbcode.txt` | The description, pasted into the panel. One line per paragraph: Steam keeps every line break. Steam caps it at **8000 characters** |

## Specifications

### Workshop cover image — `PreviewImage.png`

| | |
| --- | --- |
| File name | `PreviewImage.png`, exactly — the game builds the path as `Path.Combine(stagingPath, "PreviewImage.png")` |
| Where it goes | The root of `WorkshopStagingArea\<GUID>\`, over the default the game just wrote. Not the mod folder |
| Dimensions | **512 × 512** recommended. Square is the convention, not a rule |
| Aspect | 1:1. Steam's browse grid crops to a square thumbnail of roughly 270 px, so anything wider loses its edges there |
| Format | PNG. Steam's API also accepts JPG and GIF ("suggested formats include JPG, PNG and GIF") |
| File size | **Under 1 MB.** Steamworks documents that cap for additional preview files and gives no number for the primary one; every `PreviewImage.png` installed on this machine is under it, the largest at 941 KB. Treat 1 MB as the ceiling |
| Colour | sRGB, 8-bit. Transparency is pointless here — it is composited on a dark page |
| Design | It is read at thumbnail size first: one clear image of the sky, the mod's name large enough to survive a 270 px square, no fine text |

Sizes actually shipped by mods installed here, for reference: 512² (Harmony, Unified UI), 500²,
644², 800², 1024², 1440², 2048², and a handful of non-square ones. 512² is the safe default.

### Workshop screenshots

| | |
| --- | --- |
| Where | Steam only — the item's page, *Add/Edit images*. There is no field for these in the game |
| Dimensions | **1920 × 1080** minimum; 2560 × 1440 is fine and looks better on the item page |
| Aspect | 16:9, matching the game. Mixed aspects look untidy in the strip |
| Format | JPEG at quality ~90, or PNG. A 1920 × 1080 PNG of a cloudy sky runs 2–4 MB; the same frame as JPEG is 300–600 KB |
| File size | Keep each **under 1 MB**. That is the documented cap for additional preview files through the API, and it costs nothing to stay under it |
| How many | 5–8. The first one is what appears beside the description |
| Capture | In-game, at your normal resolution, UI hidden where the shot is about the sky. The Steam overlay's own screenshot key is the simplest route |
| Video | Steam Workshop videos are **linked YouTube URLs**, not uploads |

Shots worth having: day clouds with shadows moving across the city · a storm with lightning ·
rain under a broken sky · night with the halos on · volumetric fog with sun shafts · the F4
panel open against the sky · the options page.

### In-game button icon — `CloudIcon.png`

| | |
| --- | --- |
| Path | `VolumetricClouds/Resources/CloudIcon.png` — the resource name is hard-coded in `IconLoader`, so the name and folder cannot change |
| Dimensions | **40 × 40**, the size Unified UI gives the button (`ButtonBase.Awake` sets 40) and the size the HUD fallback uses. **80 × 80** is the safe alternative if you want it sharp when the UI is scaled up above 1080p; it is scaled down cleanly. Nothing larger is useful |
| Aspect | 1:1. The sprite is stretched to the button, so a non-square icon distorts |
| Format | PNG-32, RGBA, **transparent background**. It is loaded with `Texture2D.LoadImage` into ARGB32 |
| States | One image. Unified UI uses it for normal, hovered, pressed and disabled alike and draws its own button background behind it; the HUD fallback puts a game panel sprite behind it. Do not paint a frame or a background into the icon |
| Design | A single flat silhouette in near-white or a very light tint, ~3 px of empty margin all round, no stroke thinner than 2 px, no text. It has to read at 40 px on a dark button |
| File size | Irrelevant — it is embedded in the DLL. The current placeholder is 448 bytes |

### Text

| | |
| --- | --- |
| Workshop description | A subset of **BBCode** — `[h1]`, `[b]`, `[i]`, `[url=…]`, `[list]`/`[*]`, `[img]`. Markdown does nothing, so the README cannot be pasted in |
| `Mod.Name` | One line, shown in the Content Manager and pre-filling the Workshop title |
| `Mod.Description` | One or two sentences, shown under the name in the Content Manager |
| Change note | Plain text, per update |

## 1. Code and content

- [x] `Mod.Description` rewritten (2026-09-21, one line, in `en.xml` and `AssemblyDescription`):
      *"3D clouds that follow the game's weather, with real shadows, rain, lightning, fog and
      light halo controls. Requires Harmony."*
- [x] Button icon replaced — `Resources/CloudIcon.png` (2026-09-21): a colour cloud-and-sun from
      Openclipart's *Weather Symbols* (public domain, recorded in THIRD-PARTY-NOTICES.md), 64 px
      art centred 1:1 on an 80×80 transparent canvas for a margin. Still to see: how it reads on
      the Unified UI bar and the HUD fallback.
- [x] (2026-09-21: nine look values taken -- fixed intensity 97%, speed 3.0x, thickness 400 m,
      break-up 50%, detail 4.00x, density 150%, clear-sky brightness 300%, night glow 50%,
      shadow fullness 3.00x. Kept as shipped: the weather override and halos OFF, the advanced
      panel and detailed logging OFF, preset High.)
      `Settings.Defaults` re-snapshotted from the live `VolumetricClouds.xml`, read with a
      human eye — the file also holds experiments that must not be copied back. Mind the
      units: percentages are written as the slider shows them (47), the constants are
      fractions (0.47f).
- [ ] `CHANGELOG.md`: 1.0.0 dated, entries checked against what actually ships.
- [ ] `git grep -i -E "anthony|ibrahim" -- . ":(exclude)README.md" ":(exclude)LICENSE"` comes
      back empty.

## 2. Verified in the game

- [ ] **A full session in a loaded city with *Detailed logging* on** (Options → General; it is
      currently off in the settings file). Options tabs, the F4 panel with the advanced switch
      both ways, greying, the confirmation dialogs, Reset, fog on and off, halos, lightning
      placement, rain, night. Read the log afterwards, not just the screen.
- [x] (2026-09-21: the author deleted the `.cgs`, `.xml` and `.log` and loaded a city; it
      worked. Repeat it after the defaults are re-snapshotted, since that changes what a first
      run looks like.)
      **A true first run**: move `VolumetricClouds.xml` AND the old `VolumetricClouds.cgs`
      aside, load a city, change nothing. (Normally the import marks the `.cgs` so it is not
      read again, but on the dev machine it ran before that worked, and an unmarked `.cgs`
      would be imported again instead of giving a first run.)
      Clouds and rain on, fog and halos off, and the sky looks right untouched. This has never
      been done on this machine, and it is the whole plug-and-play promise.
      Watch for `CoverageOverride` being left on with `Coverage` at 0 — that is a clear sky and
      reads exactly like a broken mod.
- [ ] **Unload**: leave the city and confirm from the log that the sun's cookie, cookie size and
      position, the rain and lightning materials, the fog fields, the halo materials and the
      star render order were all restored.
- [ ] The log has no `ERROR` or `WARN` lines, and `output_log.txt` has no exceptions from us.
- [ ] Frame-time sweep, two cities, fixed camera, `Quality` 16/32/48/64/96 with fog off and then
      on → real numbers for `Low` and `Medium` in `SettingsCatalog.ApplyPreset`. Until this is
      done, the Workshop description must not claim a performance figure, and the presets stay
      described as provisional.

## 3. The folder that gets uploaded

The Workshop uploads a copy of `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds`
exactly as it stands, so this is the release artifact.

- [ ] Rebuild (`dotnet build VolumetricClouds/VolumetricClouds.csproj`).
- [ ] The folder holds **exactly three files**: `VolumetricClouds.dll`, `UnifiedUILib.dll`,
      `CitiesHarmony.API.dll`. No PDB, no `.mdb`, no stray bundle, no leftovers from an
      experiment.
- [ ] If shaders were rebuilt: `build-bundle.ps1` reported no `Shader error`/`Shader warning`
      **and** the bundle's file hash changed.
- [ ] The deployed copy is the one that was tested in step 2 — not an older or newer build.
- [ ] Decide on a `Source` subfolder. There is none today; if one is ever added, the panel's
      *Include source* checkbox decides whether it is uploaded, and it is deleted from the
      staged copy when unticked.

## 4. Images

Full dimensions, formats and limits are in *Specifications* above.

- [x] `Workshop/PreviewImage.png` made: 512 × 512, PNG, 404 KB. Still to judge: legible as a
      ~270 px thumbnail.
- [ ] 5–8 screenshots in `Workshop/Screenshots/`, 1920 × 1080 or larger, 16:9, each under 1 MB.
      One so far (`1.jpg`, 1920 × 1080, 586 KB).
- [ ] The first screenshot chosen deliberately — it is the one shown beside the description.
- [x] Masters kept outside the mod folder, in `Workshop/`. Anything left in the mod folder is
      uploaded to every subscriber.

## 5. Text, written before the panel is opened

- [ ] Workshop description in BBCode, drafted in `Workshop/Description.bbcode.txt` (2026-09-21,
      modelled on Node Controller Renewal's page), to be read through before it is pasted:
      no images for now (the two imgur ones, a compatibility banner naming game 1.21.1 and a
      GitHub button, were dropped for copyright on 2026-09-21; the author will make his own) ·
      pre-release, tested on one PC · what it does, and
      that everything can be switched off or overridden · requirements (Harmony, with the link,
      and Windows/D3D11 only) · how to use (F4 or the button, the three tabs, Options → Mod
      Settings, Reset) · fog and halos are opt-in · the honest performance paragraph ·
      compatibility (the author's own game with thousands of mods and assets, no conflicts
      found; Render It!, Theme Mixer, Play It! and cloud replacers checked; what the mod takes
      over from the game; removing it should not corrupt a save, with a no-responsibility
      disclaimer and "keep backups") · coming soon (profiles, more languages, Discord) · GitHub link and
      the MIT licence · how to report a bug (`output_log.txt` + `VolumetricClouds.log` with
      *Detailed logging*, hosted, linked in the comments) · the AI disclosure. Not in it: no
      moon shadows. Only Steam's tags, and no list inside a list: a Steam-aware previewer
      splits a list on every `[*]`, so a nested list's own tags showed as text. Keep it under
      8000 characters, counting each line break twice in case the browser sends CRLF (7888 +
      95 lines = 7983 on 2026-09-21: almost full, so anything added needs a cut).
- [ ] Workshop title: **Volumetric Weather**. The Share panel pre-fills it from the folder
      name, `VolumetricClouds`, which stays as it is (files and assembly keep the old name).
- [ ] If the game has updated since, the banner at the top of the description still names the
      version it was tested on.
- [ ] Change note for this upload.

## 6. Publish

The flow, as the game actually implements it:

1. Content Manager → **Mods** → the mod's row → **Share**.
2. The game creates `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\WorkshopStagingArea\<new GUID>\`,
   writes **its own default** `PreviewImage.png` at the root of it, and copies the mod folder
   into `Content\` beneath it.
3. Click the **folder button** in the panel and overwrite that `PreviewImage.png` with yours.
   The panel watches the folder and reloads the thumbnail by itself — confirm it changed on
   screen before continuing. A preview image sitting in the mod folder does **not** do this.
4. Fill in title, description and change note.
5. **Share**, and accept the Workshop legal agreement if it asks. The item is created and the
   contents of `Content\` are uploaded.

## 7. On the Steam page, straight away

- [ ] Set visibility to **Hidden** while you finish the page, if you did not want it listed the
      moment it uploaded.
- [ ] Upload the screenshots.
- [ ] *Add/Remove Required Items* → **Harmony**, `2040656402`.
- [ ] Check the description renders (BBCode tags, working links).
- [ ] The Race Day banner at the top of the description is hosted on YOUR account. It is
      Paradox's artwork, hot-linked from someone else's imgur upload
      (`i.imgur.com/1EqyarL.png`): they can delete it or swap in another picture, and it
      would show on this page. The GitHub button (`DczUXYq.png`) likewise.
- [ ] The preview image's lightning bolt and title font are yours to use. The bolt is not the
      mod's own (it is not in `Screenshots/1.jpg`); if it came from a stock photo or a brush
      pack, its licence has to allow this use.
- [ ] Tags beyond the automatic `Mod`.
- [ ] The GitHub repository is **public**. The description links
      `github.com/anthonytoumaibrahim/VolumetricCloudsCS1`, which answered 404 on 2026-09-21.
- [ ] Set visibility to **Public**.

## 8. After publishing

- [ ] Remove or disable the local copy in `Addons\Mods\VolumetricClouds` so it cannot fight the
      subscribed one. Two copies of the same mod loaded at once is a support ticket waiting to
      happen.
- [ ] Subscribe and test the published copy from a clean start. It lands in
      `steamapps\workshop\content\255710\<published file id>`.
- [ ] Record the published file id in `CHANGELOG.md` and tag the commit that was published.

## Updating later — read this before the first update

A mod's published file id comes from **its folder name**: `PluginInfo`'s constructor takes
`Path.GetFileNameWithoutExtension` of the mod path, so a subscribed item in
`…\content\255710\3412345678` knows its id, while a local folder called `VolumetricClouds` has
`PublishedFileId.invalid`.

The consequence, read out of `WorkshopModUploadPanel.OnShare`: with an invalid id the panel
calls `CreateItem` — **a brand-new Workshop item**. Only a folder that already knows its id goes
straight to `UpdateItem` and updates in place, and the panel then pre-fills the title and
description by asking Steam for the item's current details.

So an update is published from the *subscribed* copy of the mod (its Share button reads
*Update*), with the new build copied into that folder first. This has not been done here yet —
go slowly the first time, and check the item id on the page afterwards rather than assuming a
second item was not created. The preview image must be dropped into the new staging folder again
if it is to change; leaving it alone keeps the current one.
