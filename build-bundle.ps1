# Rebuilds the cloud shader AssetBundles with Unity 5.6 -- one per platform -- checks what
# each one holds, and drops them where the mod embeds them. Run this after editing anything
# under UnityProject\Assets, then rebuild the mod.
#
#   .\build-bundle.ps1
#
# What it checks, and why:
# - Unity's log for "Shader error" / "Shader warning": a shader that fails to compile still
#   produces a bundle (containing a broken shader) and exit code 0.
# - tools\bundle-apis.ps1 on the uncompressed twin of each bundle: every shader must carry
#   every graphics API its platform needs. A shader that fails for ONE API can still leave a
#   bundle behind, and a bundle without the API the game runs is a stand-down in every city.
# - Each bundle's hash before and after, printed: an edit confined to a .cginc once produced
#   a byte-identical stale bundle (ForceRebuild in BundleBuilder is what prevents it now, and
#   the printed state is how to see that it did). Bundle builds are deterministic, so
#   UNCHANGED after a shader edit is a problem and UNCHANGED without one is expected.

param(
    [string]$Unity = "C:\Program Files\Unity\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "UnityProject"
$log = Join-Path $project "build.log"
$resources = Join-Path $root "VolumetricClouds\Resources"
$apisTool = Join-Path $root "tools\bundle-apis.ps1"

# One row per bundle: its name, and the platform ids every shader in it must carry
# (1 D3D9, 4 D3D11, 14 Metal, 15 OpenGLCore, 18 Vulkan). Must match BundleBuilder.Targets.
$targets = @(
    @{ Name = "win";   Platforms = "4,15";  Needs = "Direct3D 11 + OpenGL Core" },
    @{ Name = "linux"; Platforms = "15";    Needs = "OpenGL Core" },
    @{ Name = "mac";   Platforms = "14,15"; Needs = "Metal + OpenGL Core" }
)

if (-not (Test-Path $Unity)) { throw "Unity not found at $Unity" }
if (-not (Test-Path $apisTool)) { throw "Missing $apisTool" }

$before = @{}
foreach ($t in $targets) {
    $f = Join-Path $resources ("volumetricclouds-" + $t.Name + ".bundle")
    $before[$t.Name] = if (Test-Path $f) { (Get-FileHash $f -Algorithm SHA256).Hash } else { $null }
}

if (Test-Path $log) { Remove-Item $log -Force }

Write-Host "Building the three bundles with Unity (headless)..."
$p = Start-Process -FilePath $Unity -Wait -PassThru -ArgumentList @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", "`"$project`"",
    "-executeMethod", "BundleBuilder.Build",
    "-logFile", "`"$log`"")

if ($p.ExitCode -ne 0) {
    Get-Content $log -Tail 25
    throw "Unity exited with code $($p.ExitCode). See $log"
}

$shaderProblems = Select-String -Path $log -Pattern "Shader error", "Shader warning" -SimpleMatch
if ($shaderProblems) {
    $shaderProblems | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
    throw "A shader compiled with errors or warnings; nothing copied."
}

foreach ($t in $targets) {
    foreach ($marker in @(("BUNDLE BUILD OK: " + $t.Name), ("DEBUG BUNDLE OK: " + $t.Name))) {
        if (-not (Select-String -Path $log -Pattern $marker -SimpleMatch -Quiet)) {
            throw "Marker '$marker' missing from the log. See $log"
        }
    }
}

Write-Host "Checking what each bundle holds..."
foreach ($t in $targets) {
    $debug = Join-Path $project ("Bundles\debug\" + $t.Name + "\volumetricclouds")
    & powershell -NoProfile -ExecutionPolicy Bypass -File $apisTool -Bundle $debug -Expect $t.Platforms
    if ($LASTEXITCODE -ne 0) {
        throw ("The " + $t.Name + " bundle does not hold " + $t.Needs + " for every shader; nothing copied.")
    }
}

foreach ($t in $targets) {
    $src = Join-Path $project ("Bundles\" + $t.Name + "\volumetricclouds")
    $dst = Join-Path $resources ("volumetricclouds-" + $t.Name + ".bundle")
    Copy-Item $src $dst -Force
    $hash = (Get-FileHash $dst -Algorithm SHA256).Hash
    $state = if ($null -eq $before[$t.Name]) { "new" } elseif ($before[$t.Name] -eq $hash) { "UNCHANGED" } else { "changed" }
    Write-Host ("OK: {0} ({1} bytes, {2}) sha256 {3}" -f $dst, (Get-Item $dst).Length, $state, $hash) -ForegroundColor Green
}

Write-Host "Now rebuild the mod to embed them."
