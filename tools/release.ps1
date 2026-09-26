# Stages a Workshop update, so that the only thing left to do by hand is the click in the
# game's Content Manager. Every step checks itself and the script STOPS at the first thing
# that is not right; nothing it does is hard to undo (the one folder it takes away is moved,
# not deleted).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -DryRun   # every check, no copy, no move
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1           # the real thing
#
# What it does, in order:
#   1. Checks the release is ready: the game is closed, the git tree is committed, Mod.Version
#      is newer than the last v* tag, and CHANGELOG.md has that version dated (not "unreleased").
#   2. Builds (which deploys to Addons\Mods) and checks the deployed DLL carries Mod.Version.
#   3. Runs every offline test; one failure stops the release.
#   4. Copies exactly the three shipped files into the SUBSCRIBED Workshop folder, then reads
#      the version and the file hashes back out of that folder to prove the copy landed.
#   5. Moves the local Addons\Mods copy out of the way, so the game loads ONE copy of the mod --
#      the subscribed one, whose Content Manager button reads "Update". A local copy beside it
#      is how a "Share" button (= a brand-new Workshop item) appears; with the folder gone that
#      click cannot be made.
#   6. Prints what is left to do by hand.
#
# Why the subscribed folder: a mod's published id comes from its folder name (PluginInfo), so
# only the copy in steamapps\workshop\content\255710\3805863507 knows it is item 3805863507.

param(
    [switch]$DryRun
)

# Not "Stop": under it, PowerShell 5.1 turns anything git or dotnet write to stderr (git's
# line-ending warnings, for one) into an exception. Every step below checks its own result.
$ErrorActionPreference = "Continue"

$root = Split-Path -Parent $PSScriptRoot
$workshopId = "3805863507"
$workshop = "C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\$workshopId"
$deployed = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds"
$parked = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\VolumetricClouds-dev-parked"
$shipped = @("VolumetricClouds.dll", "UnifiedUILib.dll", "CitiesHarmony.API.dll")
$tests = @("color", "settings", "skystate", "lightning", "placement", "localization", "reporter", "detail")

$problems = 0

function Step([string]$text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "   ok    $text" }
function Stop-Release([string]$text) {
    Write-Host "   STOP  $text" -ForegroundColor Red
    if ($DryRun) { $script:problems++; return }
    Write-Host ""
    Write-Host "Nothing was changed after this point. Fix it and run again." -ForegroundColor Red
    exit 1
}

function DllVersion([string]$path) {
    return [System.Reflection.AssemblyName]::GetAssemblyName($path).Version
}

function ReleaseOf([System.Version]$v) { return "{0}.{1}.{2}" -f $v.Major, $v.Minor, $v.Build }

if ($DryRun) { Write-Host "DRY RUN: every check runs, nothing is copied or moved." -ForegroundColor Yellow }

# ---- 1. Is the release ready? ---------------------------------------------------------------

Step "The game is closed"
if (Get-Process -Name "Cities" -ErrorAction SilentlyContinue) {
    Stop-Release "Cities: Skylines is running. Close it: a running game holds the DLLs and would load the old copy."
} else { Ok "not running" }

Step "The git tree is committed"
$dirty = @(git -C $root status --porcelain)
if ($dirty.Count -gt 0) {
    Stop-Release "$($dirty.Count) uncommitted change(s). Commit first, so the tag can point at what was uploaded."
} else { Ok ("clean, at " + (git -C $root rev-parse --short HEAD)) }

Step "The version is new"
$modCs = Get-Content (Join-Path $root "VolumetricClouds\Mod.cs") -Raw
if ($modCs -notmatch 'public const string Version = "(\d+\.\d+\.\d+)"') { Stop-Release "Mod.Version not found in Mod.cs" }
$version = $Matches[1]
$tags = @(git -C $root tag --list "v*" --sort=-v:refname)
$lastTag = if ($tags.Count -gt 0) { $tags[0] } else { "(no tag yet)" }
if ($lastTag -eq "v$version") {
    Stop-Release "Mod.Version is $version and the last tag is ${lastTag}: raise the version first (Mod.cs). A number is never reused."
} else { Ok "Mod.Version $version, last tag $lastTag" }

Step "The changelog has this version dated"
$changelog = Get-Content (Join-Path $root "CHANGELOG.md") -Raw -Encoding UTF8
# The heading is "## 1.1.0 <em dash> 2026-09-23". The dash is built from its code point so this
# file stays pure ASCII: PowerShell 5.1 reads a script with no byte-order mark as ANSI.
$dash = [string][char]0x2014
$heading = "(?m)^## " + [regex]::Escape($version) + " " + $dash + " (.+)$"
if ($changelog -notmatch $heading) {
    Stop-Release "CHANGELOG.md has no '## $version -- ...' section"
} elseif ($Matches[1] -match "unreleased") {
    Stop-Release "CHANGELOG.md still says '## $version -- unreleased': put the date in"
} else { Ok "## $version, dated $($Matches[1].Trim())" }

# ---- 2. Build ---------------------------------------------------------------------------------

Step "Build (deploys to Addons\Mods)"
$build = @(& dotnet build (Join-Path $root "VolumetricClouds\VolumetricClouds.csproj"))
if ($LASTEXITCODE -ne 0 -or ($build -match "error CS")) {
    $build | Where-Object { $_ -match "error" } | ForEach-Object { Write-Host "   $_" }
    Stop-Release "the build failed"
} else {
    $warnings = @($build | Where-Object { $_ -match "warning CS" })
    Ok "built, $($warnings.Count) compiler warning(s)"
}

Step "The deployed DLL carries Mod.Version"
$deployedDll = Join-Path $deployed "VolumetricClouds.dll"
if (-not (Test-Path $deployedDll)) { Stop-Release "$deployedDll is not there" }
$deployedVersion = DllVersion $deployedDll
if ((ReleaseOf $deployedVersion) -ne $version) {
    Stop-Release ("the deployed DLL is $deployedVersion, not " + $version + ".*")
} else { Ok "$deployedVersion" }

# The csproj embeds each bundle only if its file exists (Condition="Exists(...)"), so a
# missing or renamed Resources\volumetricclouds-<p>.bundle gives a clean build, 0 warnings,
# every test green -- and a stand-down in every city on that platform. Read the DLL.
Step "The deployed DLL embeds the three shader bundles"
$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($deployedDll)
$resourceNames = @($asm.GetManifestResourceNames())
foreach ($platform in "win", "linux", "mac") {
    $res = "VolumetricClouds.Resources.volumetricclouds-$platform.bundle"
    if ($resourceNames -notcontains $res) {
        Stop-Release "$res is not embedded (run build-bundle.ps1, then rebuild)"
    }
    $stream = $asm.GetManifestResourceStream($res)
    $length = $stream.Length
    $stream.Close()
    if ($length -lt 10000) { Stop-Release "$res is only $length bytes" }
    Ok "$res ($length bytes)"
}

Step "The deployed folder holds exactly the three shipped files"
$present = @(Get-ChildItem $deployed -File | Select-Object -ExpandProperty Name)
$extra = @($present | Where-Object { $shipped -notcontains $_ })
$missing = @($shipped | Where-Object { $present -notcontains $_ })
if ($extra.Count -gt 0) { Stop-Release "extra file(s) in Addons\Mods\VolumetricClouds, which would be uploaded: $($extra -join ', ')" }
elseif ($missing.Count -gt 0) { Stop-Release "missing from Addons\Mods\VolumetricClouds: $($missing -join ', ')" }
else { Ok ($shipped -join ", ") }

# ---- 3. Tests ---------------------------------------------------------------------------------

Step "Every offline test passes"
foreach ($t in $tests) {
    $script = Join-Path $root "tools\test-$t.ps1"
    $takesDll = (Get-Content $script -Raw) -match '\$Dll'
    if ($takesDll) { $out = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $script -Dll $deployedDll) }
    else { $out = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $script) }
    $last = ($out | Select-Object -Last 1) -join ""
    $failed = ($LASTEXITCODE -ne 0) -or (@($out | Where-Object { "$_" -match "^FAIL" }).Count -gt 0) -or ($last -notmatch "pass")
    if ($failed) {
        $out | Where-Object { "$_" -match "^FAIL" } | ForEach-Object { Write-Host "   $_" }
        Stop-Release "test-$t failed ($last)"
    } else { Ok "test-$t : $last" }
}

# ---- 4. Into the subscribed folder ----------------------------------------------------------

Step "The subscribed Workshop folder (item $workshopId)"
if (-not (Test-Path $workshop)) {
    Stop-Release "$workshop does not exist: subscribe to the item (and let Steam download it) first"
} else {
    $liveDll = Join-Path $workshop "VolumetricClouds.dll"
    if (Test-Path $liveDll) { Ok "holds $(DllVersion $liveDll) now" } else { Ok "exists, no DLL in it yet" }
    $others = @(Get-ChildItem $workshop -File | Where-Object { $shipped -notcontains $_.Name } | Select-Object -ExpandProperty Name)
    if ($others.Count -gt 0) { Write-Host "   note  other files there, left alone: $($others -join ', ')" }
}

Step "Copy the three files in and read them back"
if ($DryRun) {
    Write-Host "   dry   would copy $($shipped -join ', ') -> $workshop"
} else {
    foreach ($name in $shipped) {
        Copy-Item (Join-Path $deployed $name) (Join-Path $workshop $name) -Force -ErrorAction Stop
    }
    $bad = 0
    foreach ($name in $shipped) {
        $a = (Get-FileHash (Join-Path $deployed $name) -Algorithm SHA256).Hash
        $b = (Get-FileHash (Join-Path $workshop $name) -Algorithm SHA256).Hash
        if ($a -ne $b) { Write-Host "   BAD   $name differs after the copy"; $bad++ }
    }
    if ($bad -gt 0) { Stop-Release "the copy did not land; is Steam re-verifying the folder? Try again." }
    $liveVersion = DllVersion (Join-Path $workshop "VolumetricClouds.dll")
    if ($liveVersion -ne $deployedVersion) { Stop-Release "the Workshop folder reads $liveVersion, expected $deployedVersion" }
    Ok "the subscribed folder now holds $liveVersion, hashes identical"
}

# ---- 5. One copy only -----------------------------------------------------------------------

Step "Park the local Addons\Mods copy so the game loads only the subscribed one"
if ($DryRun) {
    Write-Host "   dry   would move $deployed -> $parked"
} else {
    if (Test-Path $parked) { Remove-Item $parked -Recurse -Force -ErrorAction Stop }
    Move-Item $deployed $parked -ErrorAction Stop
    if (Test-Path $deployed) { Stop-Release "Addons\Mods\VolumetricClouds is still there" }
    Ok "moved to $parked (the next 'dotnet build' recreates the local copy)"
}

# ---- 6. What is left --------------------------------------------------------------------------

Write-Host ""
if ($DryRun -and $problems -gt 0) {
    Write-Host "DRY RUN finished with $problems problem(s) above. A real run would have stopped at the first." -ForegroundColor Yellow
    exit 1
}
if ($DryRun) { Write-Host "DRY RUN finished: every check passed. Run it without -DryRun to stage the release." -ForegroundColor Green; exit 0 }

Write-Host "Staged: $version ($deployedVersion) is in the subscribed folder and the local copy is parked." -ForegroundColor Green
Write-Host ""
Write-Host "By hand, in this order:"
Write-Host "  1. Skyve: the Workshop item must be ENABLED (it was disabled for development)."
Write-Host "  2. Launch the game and load a city. The first log line must read '$version'; the settings:"
Write-Host "     line must have no 'ignored' or 'pulled into its range'. (Claude reads the log.)"
Write-Host "  3. Content Manager -> Mods -> Volumetric Weather. The button must read UPDATE."
Write-Host "     If it reads Share, STOP: that is a local copy and would create a second item."
Write-Host "  4. Title 'Volumetric Weather'; description = Workshop\Description.bbcode.txt; paste the"
Write-Host "     change note; the preview image stays. Click Update."
Write-Host "  5. On the Steam page: still ONE item ($workshopId), with the new change note."
Write-Host "  6. Tell Claude: tag v$version and open the next changelog section."
