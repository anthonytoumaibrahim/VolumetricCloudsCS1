# Building Volumetric Clouds

## Requirements

- The .NET SDK (any recent version — the project targets `net35` through reference assemblies
  pulled from NuGet, so no old framework needs to be installed).
- Cities: Skylines installed. The project references the game's own DLLs from
  `C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed`; if Steam
  lives elsewhere, pass `-p:GameManaged=<path to Cities_Data\Managed>`.
- Unity **5.6.6** at `C:\Program Files\Unity\Editor\Unity.exe` — **only** if you edit shaders.
  The compiled bundle is committed, so C# work needs no Unity.

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

Shaders cannot be compiled at runtime. They are built by Unity into an AssetBundle that is
embedded in the DLL as a resource. After editing anything under `UnityProject\Assets`:

```
.\build-bundle.ps1
```

then rebuild the mod to embed the new bundle.

Two traps, both of which have bitten this project:

- **A shader that fails to compile still produces a bundle and exit code 0.** `build-bundle.ps1`
  greps Unity's log for `Shader error` / `Shader warning`. Never bypass that check.
- **The incremental bundle build hashes only `.shader` files.** An edit confined to
  `CloudCommon.cginc` once produced a byte-identical stale bundle with no error, which is why
  `BundleBuilder` keeps `BuildAssetBundleOptions.ForceRebuildAssetBundle`. Confirm the bundle's
  file hash actually changed.

A new shader goes in the one `Shaders` array in `UnityProject/Assets/Editor/BundleBuilder.cs`.
`BuildUncompressed` also writes `Bundles/debug/`, which `tools/shaderdump.ps1 -AssetFile` can
disassemble to prove a shader really compiled.

## Hard constraints

- **`net35`, C# 7.3.** The game is Unity 5.6 on Mono's .NET 3.5 profile. A newer target builds
  fine and is then silently ignored by the game. 7.3 is the newest language version that needs
  no polyfill types. No `ImplicitUsings`, no `Nullable`, no LINQ on hot paths.
- **`AssemblyVersion("1.0.*")` with `<Deterministic>false</Deterministic>`.** The pair must stay
  together: the wildcard is silently ignored under deterministic builds, and without a fresh
  assembly version the game loads its cached copy of the DLL instead of your rebuild.
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
   (Options → General, default off); one-shot lines, state changes and anything that can
   suppress rendering are always logged. If you cannot verify something statically, add a log
   line for it.

## Tools

| Script | What it does |
| --- | --- |
| `tools/ilscan.ps1` | IL "find usages" over Assembly-CSharp or any mod DLL: `-Fields`, `-Methods`, `-Dump … -Listing -Full` for a readable instruction trace |
| `tools/shaderdump.ps1` | Decompresses a compiled D3D11 shader out of the game's assets, or out of our own bundle, and disassembles it |
| `tools/read-settings.ps1` | Decodes the old binary `VolumetricClouds.cgs` (or any `.cgs`, such as the game's own `gameSettings`) without launching the game. The mod's settings are now plain XML |
| `tools/test-*.ps1` | Offline tests of the pure maths, and of the settings file's format (`test-settings.ps1`) |

Disassembly output is never committed — it is the game's code. The tools regenerate it from
your own installed copy.

## Layout

| Path | What is there |
| --- | --- |
| `VolumetricClouds/` | The mod. `Sky/` clouds, weather, rain, lightning, fog · `Lighting/` halos · `UI/` the panel, the options page, and the settings catalog both are drawn from |
| `UnityProject/` | Unity 5.6 project holding the shaders and the headless bundle builder |
| `libs/` | Third-party DLLs referenced at build time (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)) |
| `tools/` | The PowerShell tools above |

`CLAUDE.md` is the full technical record: the invariants, the facts read out of the game's IL,
and every dead end. Read its *Invariants* section before changing anything under `Sky/`.

## Publishing

[docs/RELEASE-CHECKLIST.md](docs/RELEASE-CHECKLIST.md) is the Workshop release checklist: what
has to be verified in the game first, what the uploaded folder must contain, and which parts of
the Workshop page live in the repository and which exist only inside Steam.
