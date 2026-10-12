# Offline checks of Sky/FogPlaces.cs -- where the 1.5.0 "Fog fills valleys" and "Fog over water"
# rows put their fog: the hollows of the ground and the water. The class has no Unity dependency,
# so the SOURCE is compiled directly (as test-fogoptions does), no game and no built DLL needed.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-fogplaces.ps1
#
# What it guards: a flat map has no hollows and no water; a valley is a hollow at its floor and
# none on its ridges; a hollow is never negative; the sea is all water, far inland none, the
# shore about half; an average of a constant is that constant (the edges included).

$source = Join-Path $PSScriptRoot "..\VolumetricClouds\Sky\FogPlaces.cs"
Add-Type -TypeDefinition (Get-Content $source -Raw)

$P = [VolumetricClouds.Sky.FogPlaces]
$failed = 0

function Check([string]$name, [bool]$ok, [string]$detail = "") {
    if ($ok) { Write-Host "ok    $name  $detail" }
    else { Write-Host "FAIL  $name  $detail"; $script:failed++ }
}

function Map([int]$n, [scriptblock]$f) {
    $a = New-Object 'single[]' ($n * $n)
    for ($z = 0; $z -lt $n; $z++) { for ($x = 0; $x -lt $n; $x++) { $a[$z * $n + $x] = [single](& $f $x $z) } }
    return ,$a
}

$n = 64
$radius = 6

# ---- the map's scale ------------------------------------------------------------------------------

Check "1.5 km on the 256 map of 17.28 km is 22 texels" ($P::Texels(1500, 256, 17280) -eq 22) ("" + $P::Texels(1500, 256, 17280))
Check "340 m is 5 texels" ($P::Texels(340, 256, 17280) -eq 5) ("" + $P::Texels(340, 256, 17280))
Check "never 0 texels" ($P::Texels(1, 256, 17280) -eq 1)

# ---- averages ---------------------------------------------------------------------------------------

$flat = Map $n { param($x, $z) 120.5 }
$avg = $P::SoftAverage($flat, $n, $radius)
$same = $true
foreach ($v in $avg) { if ([math]::Abs($v - 120.5) -gt 1e-3) { $same = $false } }
Check "the average of a constant is the constant, edges included" $same

# ---- hollows ----------------------------------------------------------------------------------------

$h = $P::Hollows($flat, $n, $radius)
Check "a flat map has no hollows" ($P::Max($h) -lt 1e-3) ("deepest " + $P::Max($h))

# A valley running along z down the middle, ridges a quarter of the way in from each side.
$valley = Map $n { param($x, $z) $d = [math]::Abs($x - 32); if ($d -le 16) { 100 + 5 * $d } else { 180 - 5 * ($d - 16) } }
$h = $P::Hollows($valley, $n, $radius)
$floor = $h[20 * $n + 32]
$ridge = $h[20 * $n + 16]
Check "the valley floor is a hollow" ($floor -gt 10) ("{0:F1} m" -f $floor)
Check "its ridge is none" ($ridge -lt 1e-3) ("{0:F3} m" -f $ridge)
$negative = $false
foreach ($v in $h) { if ($v -lt 0) { $negative = $true } }
Check "a hollow is never negative" (-not $negative)
Check "the deepest is the floor's" ([math]::Abs($P::Max($h) - $floor) -lt 1e-3) ("{0:F1} m" -f $P::Max($h))

# ---- water ------------------------------------------------------------------------------------------

$ground = Map $n { param($x, $z) if ($x -lt 32) { 30 } else { 60 } }
$surface = Map $n { param($x, $z) if ($x -lt 32) { 40 } else { 60 } }
$w = $P::Water($surface, $ground, $n, $radius)
$sea = $w[10 * $n + 2]
$inland = $w[10 * $n + 60]
$shore = 0.5 * ($w[10 * $n + 31] + $w[10 * $n + 32])
Check "the open sea is all water" ([math]::Abs($sea - 1) -lt 1e-3) ("{0:F3}" -f $sea)
Check "far inland is none" ($inland -lt 1e-3) ("{0:F3}" -f $inland)
Check "the shore is about half" ([math]::Abs($shore - 0.5) -lt 0.05) ("{0:F3}" -f $shore)

$dry = $P::Water($flat, $flat, $n, $radius)
Check "a dry map has no water" ($P::Max($dry) -lt 1e-6)
$puddle = Map $n { param($x, $z) 120.6 }
Check "water shallower than 0.2 m is none" ($P::Max($P::Water($puddle, $flat, $n, $radius)) -lt 1e-6)

# ---- bytes -------------------------------------------------------------------------------------------

Check "hollow bytes: metres, rounded" (($P::HollowByte(12.4) -eq 12) -and ($P::HollowByte(12.6) -eq 13))
Check "hollow bytes: clamped" (($P::HollowByte(-3) -eq 0) -and ($P::HollowByte(900) -eq 255))
Check "share bytes" (($P::ShareByte(0) -eq 0) -and ($P::ShareByte(1) -eq 255) -and ($P::ShareByte(0.5) -eq 128))

Write-Host ""
if ($failed -eq 0) { Write-Host "all passed" } else { Write-Host "$failed FAILED"; exit 1 }
