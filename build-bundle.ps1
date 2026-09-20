# Rebuilds the cloud shader AssetBundle with Unity 5.6 and drops it where the mod embeds it.
# Run this after editing anything under UnityProject\Assets, then rebuild the mod.
#
#   .\build-bundle.ps1
#
# A shader that fails to compile still produces a bundle (containing a broken shader), so
# the log is checked explicitly rather than trusting the exit code alone.

param(
    [string]$Unity = "C:\Program Files\Unity\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "UnityProject"
$log = Join-Path $project "build.log"
$bundle = Join-Path $project "Bundles\volumetricclouds"
$target = Join-Path $root "VolumetricClouds\Resources\volumetricclouds.bundle"

if (-not (Test-Path $Unity)) { throw "Unity not found at $Unity" }

if (Test-Path $log) { Remove-Item $log -Force }

Write-Host "Building bundle with Unity (headless)..."
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
    throw "Shader compiled with errors or warnings; bundle not copied."
}

if (-not (Select-String -Path $log -Pattern "BUNDLE BUILD OK" -SimpleMatch -Quiet)) {
    throw "Build marker missing from the log. See $log"
}

Copy-Item $bundle $target -Force
Write-Host ("OK: {0} ({1} bytes)" -f $target, (Get-Item $target).Length) -ForegroundColor Green
Write-Host "Now rebuild the mod to embed it."
