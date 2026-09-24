# Building Volumetric Weather

## Requirements

- The .NET SDK (any recent version — the project targets `net35` through reference assemblies
  pulled from NuGet, so no old framework needs to be installed).
- Cities: Skylines installed. The project references the game's own DLLs from
  `C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed`; if Steam
  lives elsewhere, pass `-p:GameManaged=<path to Cities_Data\Managed>`.
- Unity **5.6.6** at `C:\Program Files\Unity\Editor\Unity.exe` with its **Mac** and **Linux
  Build Support** modules of the same version — **only** if you edit shaders. The compiled
  bundles (one per platform) are committed, so C# work needs no Unity.

## Build and deploy

```
dotnet build VolumetricClouds/VolumetricClouds.csproj
```

This builds **and deploys**. The `DeployToGame` target copies `VolumetricClouds.dll`,
`UnifiedUILib.dll` and `CitiesHarmony.API.dll` to

```
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds
```

Those three files are the whole distribution. The PDB is deliberately not deployed: Unity 5.6's
Mono reads `.mdb`, not `.pdb`, so it buys the game nothing, while the Workshop would upload it
along with the local source paths inside it.

## Shaders

Shaders cannot be compiled at runtime. They are built by Unity into AssetBundles embedded in
the DLL as resources: one per platform, because a bundle only holds code for the graphics
APIs of its build target (`volumetricclouds-win.bundle`: Direct3D 11 and OpenGL Core;
`-linux`: OpenGL Core; `-mac`: Metal and OpenGL Core). The mod picks one by platform at run
time. After editing anything under `UnityProject\Assets`:

```
.\build-bundle.ps1
```

then rebuild the mod to embed the new bundles. The script prints each bundle's size, hash
and whether it changed.

Three traps, all of which have bitten this project:

- **A shader that fails to compile still produces a bundle and exit code 0.** `build-bundle.ps1`
  greps Unity's log for `Shader error` / `Shader warning`. Never bypass that check.
- **A shader Unity cannot compile for one API leaves an EMPTY slot and no message at all.**
  `#pragma target 4.0` means "has geometry shaders" and is unsupported on Metal, so 1.1.0's
  shaders came out with 4-byte Metal blobs, exit code 0, and stood the mod down on every Mac.
  Every shader is `#pragma target 3.5` (the same shader model 4.0 on Direct3D 11, and OpenGL
  3.3) or lower, and `tools/bundle-apis.ps1` reads every bundle after the build: every
  shader named in `BundleBuilder.cs`, every platform of its bundle, real code in every slot,
  or nothing is copied. The bundles are built uncompressed precisely so that the file the
  tool verifies is the file that ships.
- **The incremental bundle build hashes only `.shader` files.** An edit confined to
  `CloudCommon.cginc` once produced a byte-identical stale bundle with no error, which is why
  `BundleBuilder` keeps `BuildAssetBundleOptions.ForceRebuildAssetBundle`. Bundle builds are
  deterministic, so the script's UNCHANGED after a shader edit means exactly that.

A new shader goes in the one `Shaders` array in `UnityProject/Assets/Editor/BundleBuilder.cs`;
`build-bundle.ps1` reads the list from there. `tools/shaderdump.ps1 -AssetFile
UnityProject/Bundles/win/volumetricclouds -Name "VolumetricClouds/<shader>"` disassembles the
Direct3D 11 code of any of them, and `tools/bundle-apis.ps1 -Bundle <new> -Compare <old>`
proves, shader by shader, that a change left the Direct3D 11 code untouched.

## Hard constraints

- **`net35`, C# 7.3.** The game is Unity 5.6 on Mono's .NET 3.5 profile. A newer target builds
  fine and is then silently ignored by the game. 7.3 is the newest language version that needs
  no polyfill types. No `ImplicitUsings`, no `Nullable`, no LINQ on hot paths.
- **`AssemblyVersion(Mod.Version + ".*")` with `<Deterministic>false</Deterministic>`.** The
  pair must stay together: the wildcard is silently ignored under deterministic builds, and
  without a fresh assembly version the game loads its cached copy of the DLL instead of your
  rebuild. `Mod.Version` (in `Mod.cs`) is the release, raised once per Workshop upload.
- Game DLLs are referenced with `<Private>false</Private>` so they never reach the Mods folder.
  `libs/UnifiedUILib.dll` and `libs/CitiesHarmony.API.dll` *are* shipped — the ecosystem's
  standard pattern. `libs/CitiesHarmony.Harmony.dll` is compile-time only; the Harmony mod
  provides the real one at runtime.

## Testing

There is no automated test suite. The loop is:

1. **Verify game APIs before writing code against them.** Reflect over the game's managed DLLs,
   or read their IL with `tools/ilscan.ps1` — field *names* mislead, and reflection alone only
   shows signatures.
2. **Test pure maths offline.** `tools/test-lightning.ps1`, `tools/test-placement.ps1` and
   `tools/test-fogcoverage.ps1` load the built DLL in a fresh PowerShell and call its pure
   methods. `Vector3`, `Mathf` and `Randomizer` are managed and work outside the game; anything
   touching `Shader`, `Mesh`, `Material` or a `Singleton<>` is an engine call and throws —
   including a class's static initialiser, so keep testable maths in its own class.
3. **Run the game and read the log.** Everything else is verified in a loaded city through

   ```
   %LOCALAPPDATA%\Colossal Order\Cities_Skylines\VolumetricClouds.log
   ```

   which is written fresh each session. Repeating lines are behind *Detailed logging*
   (on the mod's options page, default off); one-shot lines, state changes and anything that can
   suppress rendering are always logged. If you cannot verify something statically, add a log
   line for it.

## Tools

| Script | What it does |
| --- | --- |
| `tools/ilscan.ps1` | IL "find usages" over Assembly-CSharp or any mod DLL: `-Fields`, `-Methods`, `-Dump … -Listing -Full` for a readable instruction trace |
| `tools/shaderdump.ps1` | Decompresses a compiled D3D11 shader out of the game's assets, or out of our own bundle, and disassembles it |
| `tools/read-settings.ps1` | Decodes the old binary `VolumetricClouds.cgs` (or any `.cgs`, such as the game's own `gameSettings`) without launching the game. The mod's settings are now plain XML |
| `tools/test-*.ps1` | Offline tests of the pure maths, of the settings file's format (`test-settings.ps1`), and of the language files against the code (`test-localization.ps1`) |

Disassembly output is never committed — it is the game's code. The tools regenerate it from
your own installed copy.

## Layout

| Path | What is there |
| --- | --- |
| `VolumetricClouds/` | The mod. `Sky/` clouds, weather, rain, lightning, fog · `Lighting/` halos · `UI/` the panel, the options page, and the settings catalog both are drawn from |
| `UnityProject/` | Unity 5.6 project holding the shaders and the headless bundle builder |
| `libs/` | Third-party DLLs referenced at build time (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)) |
| `tools/` | The PowerShell tools above |

## Publishing

[docs/RELEASE-CHECKLIST.md](docs/RELEASE-CHECKLIST.md) is the Workshop release checklist: what
has to be verified in the game first, what the uploaded folder must contain, and which parts of
the Workshop page live in the repository and which exist only inside Steam.
