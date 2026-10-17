# Offline checks of Sky/CloudSoft.cs -- the 1.5.0 Soft cloud style's C# twin. The class has no Unity
# dependency, so the SOURCE is compiled directly (as test-emptysky does), no game and no built DLL needed.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-soft.ps1
#
# What it guards: the cover the puffs are cut at is never less than the intensity up to their cap and
# stays there above it; the Classic layer comes in from its threshold, never past the intensity, Classic's
# own cover at the top; the cover the rain is cut at is the intensity itself with the Classic layer row at
# 100% and never more than what is drawn otherwise (invariant 2); the light follows the layer's share;
# the density stays 0..1, is 0 where the map is at or under the threshold (the empty-sky skip's promise)
# and outside the slab, and only grows with the weather and the noise; a small cloud is a shorter one;
# the noise carves holes into small clouds but not into the middles of the biggest; the fringe is faint;
# the fine read's frequency is a whole multiple of the noise's (it shares its phase, invariant 13).

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

# The table: one value per 5%, rising, never under its own intensity up to the cap, the cap's value past
# it; a look without one is Classic's own.
$capIndex = [int][math]::Round($look.PuffCap / 0.05)
$table = $look.AmountTable
$tableOk = $table.Length -eq 21
for ($i = 1; $i -lt $table.Length; $i++) {
    if ($table[$i] -lt $table[$i - 1]) { $tableOk = $false }
    if ($i -le $capIndex -and $table[$i] -lt $i * 0.05 - 1e-6) { $tableOk = $false }
    if ($i -gt $capIndex -and $table[$i] -ne $table[$capIndex]) { $tableOk = $false }
}
Check "edge table: 21 values, rising, never under its intensity up to the cap, flat past it" $tableOk
$bare = $look; $bare.AmountTable = $null
Check "edge cover: no table = the intensity itself" ([math]::Abs($S::EdgeCoverage($bare, 0.47) - 0.47) -lt 1e-6)
Check "edge cover: on a table value, that value" ([math]::Abs($S::EdgeCoverage($look, 0.5) - $table[10]) -lt 1e-5) "($($S::EdgeCoverage($look, 0.5)) vs $($table[10]))"

# ---- the puffs' cut, the layer, the rain's cover, the light ---------------------------------------

$cap = $look.PuffCap
$atCap = $S::PuffCoverage($look, $cap)
$puffOk = $true; $prevP = -1.0; $worst = ""
for ($i = 0; $i -le 1000; $i++) {
    $c = $i / 1000.0
    $p = $S::PuffCoverage($look, $c)
    if ($p -lt $prevP - 1e-7) { $puffOk = $false; $worst = "falls at $c" }
    if ($c -le $cap -and $p -lt $c - 1e-6) { $puffOk = $false; $worst = "under the intensity at $c ($p)" }
    if ($c -gt $cap -and $p -ne $atCap) { $puffOk = $false; $worst = "moves past the cap at $c ($p)" }
    $prevP = $p
}
Check "puffs' cut: 0 at 0, rising, never under the intensity up to the cap, the cap's above it" ($puffOk -and $S::PuffCoverage($look, 0) -eq 0) $worst

$layerOk = $true; $prevL = -1.0; $worst = ""
foreach ($amount in 1.0, 0.5, 0.0) {
    $prevL = -1.0
    for ($i = 0; $i -le 1000; $i++) {
        $c = $i / 1000.0
        $l = $S::LayerCover($look, $c, $amount)
        if ($l -lt $prevL - 1e-7) { $layerOk = $false; $worst = "falls at $c" }
        if ($l -gt $c * $amount + 1e-6) { $layerOk = $false; $worst = "past the intensity x the row at $c" }
        if ($c -le $look.LayerFrom -and $l -ne 0) { $layerOk = $false; $worst = "there below its threshold at $c" }
        if ($c -ge $look.LayerTo -and [math]::Abs($l - $c * $amount) -gt 1e-6) { $layerOk = $false; $worst = "not Classic's own cover x the row at $c" }
        $prevL = $l
    }
}
Check "layer: none up to its threshold, rising, Classic's own cover (x the row) from LayerTo, never past it" $layerOk $worst
Check "layer: at Classic's own cover before the intensity passes the puffs' capped cut (so the rain is as before)" ($look.LayerFrom -lt $look.LayerTo -and $look.LayerTo -le $atCap + 1e-6) "(from $($look.LayerFrom) to $($look.LayerTo), the cap's cut $atCap)"

$rainOk = $true; $rainUnder = $true; $worst = ""
for ($i = 0; $i -le 1000; $i++) {
    $c = $i / 1000.0
    if ([math]::Abs($S::DrawnCoverage($look, $c, 1) - $c) -gt 1e-6) { $rainOk = $false; $worst = "at $c" }
    foreach ($amount in 0.5, 0.0) {
        $d = $S::DrawnCoverage($look, $c, $amount)
        $drawn = [math]::Max([double]$S::PuffCoverage($look, $c), [double]$S::LayerCover($look, $c, $amount))
        if ($d -gt $c + 1e-6 -or $d -gt $drawn + 1e-6) { $rainUnder = $false }
    }
}
Check "rain's cover: the intensity itself with the Classic layer row at 100% (rain as before)" $rainOk $worst
Check "rain's cover: never past the intensity nor past what is drawn, with the row lower" $rainUnder

$lightOk = [math]::Abs($S::LightScaleAt($look, 0) - $look.LightScale) -lt 1e-6 -and [math]::Abs($S::LightScaleAt($look, 1) - $look.LayerLight) -lt 1e-6 -and
           $S::LightScaleAt($look, 0.5) -gt $look.LightScale -and $S::LightScaleAt($look, 0.5) -lt $look.LayerLight -and
           $look.LayerLight -ge $look.LightScale -and $look.LayerLight -le 1
Check "light: the puffs' level with no layer, the layer's where it carries the sky, between" $lightOk "(x$($look.LightScale) .. x$($look.LayerLight))"
$shareOk = $true
for ($i = 0; $i -le 100; $i++) {
    $c = $i / 100.0
    $s1 = $S::LightShare($look, $c, 1); $s0 = $S::LightShare($look, $c, 0)
    if ($s0 -ne 0 -or $s1 -lt 0 -or $s1 -gt 1 -or ($c -le $look.LightFrom -and $s1 -ne 0)) { $shareOk = $false }
}
Check "light: the puffs' level up to LightFrom, the layer's at 100%, none of it with the Classic layer row at 0%" ($shareOk -and [math]::Abs($S::LightShare($look, 1, 1) - 1) -lt 1e-6)

$samples = @(0.25, 0.47, 0.7, 0.8, 0.9, 0.98) | ForEach-Object { "{0:F2}->{1:F3}/{2:F3}" -f $_, $S::PuffCoverage($look, $_), $S::LayerCover($look, $_, 1) }
Write-Host "      puffs' cut / layer cover as built: $($samples -join '  ')"

# ---- the density -----------------------------------------------------------------------------------

$tallest = $S::Tallest($look, 450)
Check "tallest: Layer thickness x its factor" ([math]::Abs($tallest - 450 * $look.TallestPerThickness) -lt 1e-3) "($tallest m)"
Check "tallest: never under 50 m x its factor" ([math]::Abs($S::Tallest($look, 10) - 50 * $look.TallestPerThickness) -lt 1e-3)

$rng = New-Object System.Random 1616
$inRange = $true; $zeroBelow = $true; $zeroOutside = $true; $monoWeather = $true; $monoNoise = $true
for ($k = 0; $k -lt 20000; $k++) {
    $ex = $rng.NextDouble() * 1.2 - 0.4
    $y = $rng.NextDouble() * ($tallest * 1.2) - 0.1 * $tallest
    $noise = $rng.NextDouble(); $fine = $rng.NextDouble(); $wisps = $rng.NextDouble()
    $d = $S::Density($look, $ex, $y, $tallest, $noise, $fine, $wisps)
    if ($d -lt 0 -or $d -gt 1 -or [double]::IsNaN($d)) { $inRange = $false }
    if ($ex -le 0 -and $d -ne 0) { $zeroBelow = $false }
    if (($y -le 0 -or $y -ge $tallest) -and $d -ne 0) { $zeroOutside = $false }

    if ($S::Density($look, $ex + 0.03, $y, $tallest, $noise, $fine, $wisps) -lt $d - 1e-6) { $monoWeather = $false }
    if ($S::Density($look, $ex, $y, $tallest, [math]::Min([double]1, $noise + 0.05), $fine, $wisps) -lt $d - 1e-6) { $monoNoise = $false }
}
Check "density: always 0..1" $inRange
Check "density: 0 where the map is at or under the threshold (the empty-sky skip)" $zeroBelow
Check "density: 0 under the base and over the tallest" $zeroOutside
Check "density: only grows with the weather" $monoWeather
Check "density: only grows with the noise" $monoNoise

# A cloud's top and underside at a given weather: where the density (noise at its fullest, no fine
# read) runs out.
function Extent([double]$ex) {
    $lo = $null; $hi = $null
    for ($y = 1; $y -lt $tallest; $y += 1) {
        if ($S::Density($look, $ex, $y, $tallest, 1, 0, 0) -gt 0) { if ($null -eq $lo) { $lo = $y }; $hi = $y }
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
$holes = $S::Density($look, $look.Span * 0.6, $mid, $tallest, 0, 0, 0)
$solid = $S::Density($look, $look.Span * (1 + $look.FillSpan), $mid, $tallest, 0, 0, 0)
Check "the noise carves holes into a middling cloud" ($holes -eq 0) "($holes)"
Check "the middles of the biggest clouds stay solid" ($solid -gt 0.5) "($solid)"

# No hard edge: as the noise rises, the density passes through in-between values, not in a step.
$between = 0
for ($i = 0; $i -le 1000; $i++) {
    $d = $S::Density($look, $look.Span * 0.8, $mid, $tallest, $i / 1000.0, 0, 0)
    if ($d -gt 0.01 -and $d -lt 0.99) { $between++ }
}
Check "no hard edge: in-between densities over a stretch of the noise" ($between -ge 30) "($between of 1001)"

# The fringe: just outside a puff (the carving leaves nothing), faint strands where the fine read is
# high, none where it is low, never more than the fringe's density.
$outside = 0.0; $low = 0.0
for ($n = 0; $n -le 100; $n++) {
    $v = $S::Density($look, $look.Span * 0.8, $mid, $tallest, $n / 100.0, 1, 1)
    $w = $S::Density($look, $look.Span * 0.8, $mid, $tallest, $n / 100.0, 0, 1)
    $base = $S::Density($look, $look.Span * 0.8, $mid, $tallest, $n / 100.0, 0, 0)
    if ($base -eq 0 -and $v -gt $outside) { $outside = $v }
    if ($base -eq 0 -and $w -gt $low) { $low = $w }
}
Check "fringe: faint strands outside the puffs" ($outside -gt 0 -and $outside -le $look.Fringe + 1e-6) "($outside, at most $($look.Fringe))"
Check "fringe: none where the fine read is low" ($low -eq 0) "($low)"
Check "light scale: in (0, 1]" ($look.LightScale -gt 0 -and $look.LightScale -le 1) "($($look.LightScale))"

# The fine read shares the noise's phase: a whole multiple of its frequency.
Check "fine read: a whole multiple of the noise's frequency" ($look.WispMultiple -ge 1 -and [math]::Abs($look.WispMultiple - [math]::Round($look.WispMultiple)) -lt 1e-6) "($($look.WispMultiple))"

# The log line is plain ASCII and names the tallest and the layer.
$text = $S::Describe($look, 450)
Check "describe: names the tallest and the layer, ASCII" ($text.Contains(([int]$tallest).ToString()) -and $text.Contains("Classic layer") -and $text -cmatch '^[\x20-\x7E]+$') "($text)"

if ($failed -gt 0) { Write-Host "`n$failed FAILED"; exit 1 }
Write-Host "`nall passed"
