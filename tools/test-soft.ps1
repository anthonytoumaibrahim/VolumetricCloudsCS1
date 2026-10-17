# Offline checks of Sky/CloudSoft.cs -- the 1.5.0 Soft cloud style's C# twin. The class has no Unity
# dependency, so the SOURCE is compiled directly (as test-emptysky does), no game and no built DLL needed.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-soft.ps1
#
# What it guards: the cover the map is cut at is never less than the intensity (Classic's clouds, and
# the rain under a smaller cover still, lie inside Soft's: invariant 2), rising from 0 to 1; the overcast
# floor only comes in near 100%; the density stays 0..1, is 0 where the map is at or under the threshold
# whatever the floor (the empty-sky skip's promise) and outside the slab, and only grows with the
# weather, the noise and the floor; a small cloud is a shorter one; the noise carves holes into small
# clouds but not into the middles of the biggest; the fringe is faint; the fine read's frequency is a
# whole multiple of the noise's (it shares its phase, invariant 13).

$source = Join-Path $PSScriptRoot "..\VolumetricClouds\Sky\CloudSoft.cs"
Add-Type -TypeDefinition (Get-Content $source -Raw)

$S = [VolumetricClouds.Sky.CloudSoft]
$look = $S::Default
$failed = 0

function Check([string]$name, [bool]$ok, [string]$detail = "") {
    if ($ok) { Write-Host "ok    $name  $detail" }
    else { Write-Host "FAIL  $name  $detail"; $script:failed++ }
}

# ---- the cover it is cut at ------------------------------------------------------------------------

$e0 = $S::EdgeCoverage($look, 0); $e1 = $S::EdgeCoverage($look, 1)
Check "edge cover: 0 at 0, 1 at 1" ($e0 -eq 0 -and [math]::Abs($e1 - 1) -lt 1e-6) "($e0, $e1)"

$prev = -1.0; $rising = $true; $atLeast = $true; $worst = ""
for ($i = 0; $i -le 1000; $i++) {
    $c = $i / 1000.0
    $e = $S::EdgeCoverage($look, $c)
    if ($e -lt $prev - 1e-7) { $rising = $false; $worst = "falls at $c" }
    if ($e -lt $c - 1e-6) { $atLeast = $false; $worst = "under the intensity at $c ($e)" }
    if ($e -gt 1) { $atLeast = $false; $worst = "over 1 at $c" }
    $prev = $e
}
Check "edge cover: rises all the way" $rising $worst
Check "edge cover: never under the intensity, never over 1 (Classic's clouds and the rain inside)" $atLeast $worst

# The table: one value per 5%, rising, never under its own intensity; a look without one is Classic's own.
$table = $look.AmountTable
$tableOk = $table.Length -eq 21
for ($i = 1; $i -lt $table.Length; $i++) { if ($table[$i] -lt $table[$i - 1] -or $table[$i] -lt $i * 0.05 - 1e-6) { $tableOk = $false } }
Check "edge table: 21 values, rising, never under its intensity" $tableOk
$bare = $look; $bare.AmountTable = $null
Check "edge cover: no table = the intensity itself" ([math]::Abs($S::EdgeCoverage($bare, 0.47) - 0.47) -lt 1e-6)
Check "edge cover: on a table value, that value" ([math]::Abs($S::EdgeCoverage($look, 0.5) - $table[10]) -lt 1e-5) "($($S::EdgeCoverage($look, 0.5)) vs $($table[10]))"

$samples = @(0.25, 0.47, 0.7, 0.9, 0.98) | ForEach-Object { "{0:F2}->{1:F3}" -f $_, $S::EdgeCoverage($look, $_) }
Write-Host "      edge cover as built: $($samples -join '  ')"

# ---- the overcast floor ----------------------------------------------------------------------------

$floorOk = $true; $prevF = -1.0
for ($i = 0; $i -le 1000; $i++) {
    $c = $i / 1000.0
    $f = $S::Floor($look, $c)
    if ($f -lt $prevF - 1e-7 -or $f -lt 0) { $floorOk = $false }
    if ($c -le $look.OvercastFrom -and $f -ne 0) { $floorOk = $false }
    $prevF = $f
}
Check "overcast floor: 0 up to where it starts, then rising" $floorOk "(from $($look.OvercastFrom))"
Check "overcast floor: its full value at 100%" ([math]::Abs($S::Floor($look, 1) - $look.OvercastFloor) -lt 1e-6) "($($S::Floor($look, 1)))"

# ---- the density -----------------------------------------------------------------------------------

$tallest = $S::Tallest($look, 450)
Check "tallest: Layer thickness x its factor" ([math]::Abs($tallest - 450 * $look.TallestPerThickness) -lt 1e-3) "($tallest m)"
Check "tallest: never under 50 m x its factor" ([math]::Abs($S::Tallest($look, 10) - 50 * $look.TallestPerThickness) -lt 1e-3)

$rng = New-Object System.Random 1616
$inRange = $true; $zeroBelow = $true; $zeroOutside = $true; $monoWeather = $true; $monoNoise = $true; $monoFloor = $true
for ($k = 0; $k -lt 20000; $k++) {
    $ex = $rng.NextDouble() * 1.2 - 0.4
    $fl = $(if ($rng.NextDouble() -lt 0.5) { 0.0 } else { $rng.NextDouble() * 1.6 })
    $y = $rng.NextDouble() * ($tallest * 1.2) - 0.1 * $tallest
    $noise = $rng.NextDouble(); $fine = $rng.NextDouble(); $wisps = $rng.NextDouble()
    $d = $S::Density($look, $ex, $fl, $y, $tallest, $noise, $fine, $wisps)
    if ($d -lt 0 -or $d -gt 1 -or [double]::IsNaN($d)) { $inRange = $false }
    if ($ex -le 0 -and $d -ne 0) { $zeroBelow = $false }
    if (($y -le 0 -or $y -ge $tallest) -and $d -ne 0) { $zeroOutside = $false }

    if ($S::Density($look, $ex + 0.03, $fl, $y, $tallest, $noise, $fine, $wisps) -lt $d - 1e-6) { $monoWeather = $false }
    if ($S::Density($look, $ex, $fl, $y, $tallest, [math]::Min([double]1, $noise + 0.05), $fine, $wisps) -lt $d - 1e-6) { $monoNoise = $false }
    if ($S::Density($look, $ex, $fl + 0.1, $y, $tallest, $noise, $fine, $wisps) -lt $d - 1e-6) { $monoFloor = $false }
}
Check "density: always 0..1" $inRange
Check "density: 0 where the map is at or under the threshold, floor or not (the empty-sky skip)" $zeroBelow
Check "density: 0 under the base and over the tallest" $zeroOutside
Check "density: only grows with the weather" $monoWeather
Check "density: only grows with the noise" $monoNoise
Check "density: only grows with the overcast floor" $monoFloor

# A cloud's top and underside at a given weather: where the density (noise at its fullest, no fine
# read) runs out.
function Extent([double]$ex) {
    $lo = $null; $hi = $null
    for ($y = 1; $y -lt $tallest; $y += 1) {
        if ($S::Density($look, $ex, 0, $y, $tallest, 1, 0, 0) -gt 0) { if ($null -eq $lo) { $lo = $y }; $hi = $y }
    }
    return @($lo, $hi)
}
$big = Extent ($look.Span * 1.5)
$small = Extent ($look.Span * 0.3)
Check "a full cloud reaches from about the base to about the tallest" ($big[0] -le 5 -and $big[1] -ge $tallest - 5) "($($big[0])..$($big[1]) m)"
Check "a small cloud is shorter: its top lower, its underside higher" ($small[1] -lt $big[1] - 100 -and $small[0] -gt $big[0] + 5) "($($small[0])..$($small[1]) m)"
$mid = $tallest * $look.Middle
Check "a small cloud still holds its widest level" ($small[0] -le $mid -and $small[1] -ge $mid)

# The noise makes the shapes: at its lowest it leaves nothing of a middling cloud (sky between the
# clumps), while deep inside the biggest the carving has eased off (their middles solid).
$holes = $S::Density($look, $look.Span * 0.6, 0, $mid, $tallest, 0, 0, 0)
$solid = $S::Density($look, $look.Span * (1 + $look.FillSpan), 0, $mid, $tallest, 0, 0, 0)
Check "the noise carves holes into a middling cloud" ($holes -eq 0) "($holes)"
Check "the middles of the biggest clouds stay solid" ($solid -gt 0.5) "($solid)"
# At 100% the floor leaves holes only where the noise is at its lowest (it reads 0.50 at its 10th
# percentile, 0.70 at its middle): a broken overcast everywhere inside the cut, as Cumulus keeps gaps.
$at100 = $S::Floor($look, 1)
Check "at 100% (the floor) the weakest stretch of the map is cloud wherever the noise is past 0.6" ($S::Density($look, 0.001, $at100, $mid, $tallest, 0.6, 0, 0) -gt 0)

# No hard edge: as the noise rises, the density passes through in-between values, not in a step.
$between = 0
for ($i = 0; $i -le 1000; $i++) {
    $d = $S::Density($look, $look.Span * 0.8, 0, $mid, $tallest, $i / 1000.0, 0, 0)
    if ($d -gt 0.01 -and $d -lt 0.99) { $between++ }
}
Check "no hard edge: in-between densities over a stretch of the noise" ($between -ge 30) "($between of 1001)"

# The fringe: just outside a puff (the carving leaves nothing), faint strands where the fine read is
# high, none where it is low, never more than the fringe's density.
$outside = 0.0; $low = 0.0
for ($n = 0; $n -le 100; $n++) {
    $v = $S::Density($look, $look.Span * 0.8, 0, $mid, $tallest, $n / 100.0, 1, 1)
    $w = $S::Density($look, $look.Span * 0.8, 0, $mid, $tallest, $n / 100.0, 0, 1)
    $base = $S::Density($look, $look.Span * 0.8, 0, $mid, $tallest, $n / 100.0, 0, 0)
    if ($base -eq 0 -and $v -gt $outside) { $outside = $v }
    if ($base -eq 0 -and $w -gt $low) { $low = $w }
}
Check "fringe: faint strands outside the puffs" ($outside -gt 0 -and $outside -le $look.Fringe + 1e-6) "($outside, at most $($look.Fringe))"
Check "fringe: none where the fine read is low" ($low -eq 0) "($low)"
Check "light scale: in (0, 1]" ($look.LightScale -gt 0 -and $look.LightScale -le 1) "($($look.LightScale))"

# The fine read shares the noise's phase: a whole multiple of its frequency.
Check "fine read: a whole multiple of the noise's frequency" ($look.WispMultiple -ge 1 -and [math]::Abs($look.WispMultiple - [math]::Round($look.WispMultiple)) -lt 1e-6) "($($look.WispMultiple))"

# The log line is plain ASCII and names the tallest.
$text = $S::Describe($look, 450)
Check "describe: names the tallest, ASCII" ($text.Contains(([int]$tallest).ToString()) -and $text -cmatch '^[\x20-\x7E]+$') "($text)"

if ($failed -gt 0) { Write-Host "`n$failed FAILED"; exit 1 }
Write-Host "`nall passed"
