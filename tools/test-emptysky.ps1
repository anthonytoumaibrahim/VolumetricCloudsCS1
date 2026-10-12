# Offline checks of Sky/EmptySky.cs -- the 1.5.0 "skip the empty sky" map the cloud march jumps
# over empty cells with. The class has no Unity dependency, so the SOURCE is compiled directly (as
# test-fogoptions does), no game and no built DLL needed.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-emptysky.ps1
#
# What it guards -- the one promise that keeps the picture the same: wherever a point lies, the
# weather the GPU reads there (bilinear between decoded texels, wrapping) is never above its fine
# cell's bound, nor its coarse cell's; the same for the Cumulus score. Then the pieces: a cell takes
# the texels round it, across the wrap too; a bound is never below what it bounds and at most two
# steps above; the share of empty cells.

$source = Join-Path $PSScriptRoot "..\VolumetricClouds\Sky\EmptySky.cs"
Add-Type -TypeDefinition (Get-Content $source -Raw)

$E = [VolumetricClouds.Sky.EmptySky]
$failed = 0

function Check([string]$name, [bool]$ok, [string]$detail = "") {
    if ($ok) { Write-Host "ok    $name  $detail" }
    else { Write-Host "FAIL  $name  $detail"; $script:failed++ }
}

function Decode([double]$b, [bool]$srgb) {
    $v = $b / 255.0
    if (-not $srgb) { return $v }
    if ($v -le 0.04045) { return $v / 12.92 }
    return [math]::Pow(($v + 0.055) / 1.055, 2.4)
}

function Wrap([int]$i, [int]$n) { $r = $i % $n; if ($r -lt 0) { $r += $n }; return $r }

# ---- the bound, by brute force -------------------------------------------------------------------

$n = 64
$m = $n / $E::CellTexels
$rng = New-Object System.Random 4242
foreach ($srgb in $true, $false) {
    # Lumpy: mostly low, a few high blobs, so cells differ.
    $weather = New-Object 'byte[]' ($n * $n)
    $score = New-Object 'byte[]' ($n * $n)
    for ($i = 0; $i -lt $weather.Length; $i++) {
        $weather[$i] = [byte]([math]::Min(255, [math]::Pow($rng.NextDouble(), 4) * 300))
        $score[$i] = [byte]($rng.Next(256))
    }

    $rgba = $E::Build($weather, $score, $n, $srgb)
    $worst = -1.0
    $ok = $true
    for ($k = 0; $k -lt 6000; $k++) {
        $u = $rng.NextDouble() * 3 - 1   # any tile: the map wraps
        $v = $rng.NextDouble() * 3 - 1

        # The GPU's bilinear read at (u, v): texel centres at (i + 0.5) / n, decoded THEN filtered.
        $x = $u * $n - 0.5; $y = $v * $n - 0.5
        $x0 = [math]::Floor($x); $y0 = [math]::Floor($y)
        $fx = $x - $x0; $fy = $y - $y0
        $w = 0.0; $s = 0.0
        foreach ($dy in 0, 1) { foreach ($dx in 0, 1) {
            $wt = ($(if ($dx) { $fx } else { 1 - $fx })) * ($(if ($dy) { $fy } else { 1 - $fy }))
            $idx = (Wrap ([int]$y0 + $dy) $n) * $n + (Wrap ([int]$x0 + $dx) $n)
            $w += $wt * (Decode $weather[$idx] $srgb)
            $s += $wt * ($score[$idx] / 255.0)
        } }

        # Its fine cell, as the shader finds it: floor of the position in cells, wrapped.
        $cell = (Wrap ([int][math]::Floor($v * $m)) $m) * $m + (Wrap ([int][math]::Floor($u * $m)) $m)
        $fine = $rgba[$cell * 4] / 255.0
        $coarse = $rgba[$cell * 4 + 1] / 255.0
        $fineScore = $rgba[$cell * 4 + 2] / 255.0
        $coarseScore = $rgba[$cell * 4 + 3] / 255.0

        if ($w -gt $fine + 1e-9 -or $w -gt $coarse + 1e-9 -or $s -gt $fineScore + 1e-9 -or $s -gt $coarseScore + 1e-9) { $ok = $false }
        $worst = [math]::Max($worst, $w - $fine)
    }
    Check ("no point reads above its cells' bounds (" + $(if ($srgb) { "decoded" } else { "undecoded" }) + ")") $ok ("closest {0:F4} under" -f (-$worst))
    Check ("the coarse bound is never below the fine one (" + $(if ($srgb) { "decoded" } else { "undecoded" }) + ")") (& {
        for ($i = 0; $i -lt $m * $m; $i++) { if ($rgba[$i * 4 + 1] -lt $rgba[$i * 4] -or $rgba[$i * 4 + 3] -lt $rgba[$i * 4 + 2]) { return $false } }
        return $true })
}

# ---- the pieces --------------------------------------------------------------------------------------

$one = New-Object 'byte[]' (16 * 16)
$one[8 * 16 + 8] = 200
$max = $E::CellMax($one, 16, 4)
$hot = @()
for ($i = 0; $i -lt 16; $i++) { if ($max[$i] -gt 0) { $hot += $i } }
Check "one hot texel lights its cell and the ones whose border it is in" (($hot -join ",") -eq "5,6,9,10") ($hot -join ",")

$corner = New-Object 'byte[]' (16 * 16)
$corner[0] = 99
$max = $E::CellMax($corner, 16, 4)
Check "the border wraps: texel (0,0) is in the last cell's border" (($max[0] -eq 99) -and ($max[15] -eq 99) -and ($max[3] -eq 99) -and ($max[12] -eq 99) -and ($max[5] -eq 0))

$fine = [byte[]](0..15 | ForEach-Object { [byte]$_ })
$coarse = $E::Coarse($fine, 4, 2)
Check "a coarse cell is the most of its fine cells, in each of them" ((($coarse | ForEach-Object { $_ }) -join ",") -eq "5,5,7,7,5,5,7,7,13,13,15,15,13,13,15,15")

$bound = $true
foreach ($srgb in $true, $false) {
    for ($b = 0; $b -le 255; $b++) {
        $up = $E::AsDrawnUp([byte]$b, $srgb) / 255.0
        $d = Decode $b $srgb
        if ($b -lt 255 -and ($up -lt $d + 0.5 / 255 -or $up -gt $d + 2.0 / 255 + 1e-9)) { $bound = $false }
    }
}
Check "a bound is at least half a step above what it bounds, and at most two" $bound
Check "255 stays 255" ($E::AsDrawnUp([byte]255, $true) -eq 255)

$rgba = New-Object 'byte[]' (4 * 4)
$rgba[0] = 10; $rgba[4] = 100; $rgba[8] = 200; $rgba[12] = 255
Check "empty share at a threshold" ([math]::Abs($E::EmptyShare($rgba, 0.5) - 0.5) -lt 1e-6) ("" + $E::EmptyShare($rgba, 0.5))

Write-Host ""
if ($failed -eq 0) { Write-Host "all passed" } else { Write-Host "$failed FAILED"; exit 1 }
