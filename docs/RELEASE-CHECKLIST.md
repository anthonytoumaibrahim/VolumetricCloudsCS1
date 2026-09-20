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
treat the Workshop box as a copy: draft it as `docs/workshop-description.txt` (not written yet)
so the next update is an edit, not a rewrite from memory.

## 1. Code and content

- [ ] `Mod.Description` rewritten — it is what the Content Manager shows, and it still reads
      *"Adds volumetric clouds to the game."* Mention rain, lightning, fog, halos, and that
      Harmony is required.
- [ ] Button icon replaced (`Resources/CloudIcon.png`, 40×40 or 80×80, RGBA, transparent, light
      silhouette, ~3 px margin). The current file is a 448-byte placeholder.
- [ ] `Settings.Defaults` re-snapshotted from the live `.cgs` with `tools/read-settings.ps1`,
      read with a human eye — the file also holds experiments and dead keys
      (`DebugSkipDrawLight`, the old `WeatherOvercastCoverage`) that must not be copied back.
- [ ] `CHANGELOG.md`: 1.0.0 dated, entries checked against what actually ships.
- [ ] `git grep -i -E "anthony|ibrahim" -- . ":(exclude)README.md" ":(exclude)LICENSE"` comes
      back empty.

## 2. Verified in the game

- [ ] **A full session in a loaded city with *Detailed logging* on** (Options → General; it is
      currently off in the settings file). Options tabs, the F4 panel with the advanced switch
      both ways, greying, the confirmation dialogs, Reset, fog on and off, halos, lightning
      placement, rain, night. Read the log afterwards, not just the screen.
- [ ] **A true first run**: move `VolumetricClouds.cgs` aside, load a city, change nothing.
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

- [ ] `PreviewImage.png` — square, 512×512, under 1 MB, readable as a small thumbnail. (Harmony
      and Unified UI both ship 512×512; 644² and 1024² are the other common sizes.)
- [ ] 5–8 screenshots at 1920×1080 or larger: day clouds with shadows across the city, a storm
      with lightning, rain, night with the halos on, volumetric fog with sun shafts, the F4
      panel, the options page.
- [ ] Keep the masters somewhere outside the mod folder — anything left in it is uploaded to
      every subscriber.

## 5. Text, written before the panel is opened

- [ ] Workshop description in BBCode: what it does · requirements (Harmony, with the link, and
      Windows/D3D11 only) · how to open the panel (F4 or the Unified UI button) · fog and halos
      are opt-in · the honest performance paragraph · compatibility (Render It!, Theme Mixer,
      Play It!, cloud replacers, IMT, Persistent Fog Adjuster no longer needed) · known
      limitations including no scenarios and no moon shadows · how to report a bug (tick
      *Detailed logging*, reproduce, attach `VolumetricClouds.log`) · the AI disclosure · a link
      to the GitHub repository and the MIT licence.
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
- [ ] Tags beyond the automatic `Mod`.
- [ ] Link the GitHub repository and state the licence.
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
