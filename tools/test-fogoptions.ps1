# Offline checks of Sky/FogSources.cs -- the 1.5.0 "Changing fog" rows: Fog changes with the
# weather, Morning fog, Mist after rain. The class has no Unity dependency, so the SOURCE is
# compiled directly (as test-reporter does), no game and no built DLL needed.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-fogoptions.ps1
#
# What it guards: with the rows at 0% the look is the sliders' own, bit for bit (an update changes
# nobody's fog); whatever the inputs, a look never asks for more fog than the sliders allow (the
# author's "Ceiling"); the curves are continuous (nothing pops), and they do what the plan says.

$source = Join-Path $PSScriptRoot "..\VolumetricClouds\Sky\FogSources.cs"
Add-Type -TypeDefinition (Get-Content $source -Raw)

$S = [VolumetricClouds.Sky.FogSources]
$N = [VolumetricClouds.Sky.FogLook]::Neutral
$failed = 0

function Check([string]$name, [bool]$ok, [string]$detail = "") {
    if ($ok) { Write-Host "ok    $name  $detail" }
    else { Write-Host "FAIL  $name  $detail"; $script:failed++ }
}

function Near([double]$a, [double]$b, [double]$eps = 1e-5) { return [math]::Abs($a - $b) -le $eps }

function Text($l) { return "({0:F3}, {1:F3}, {2:F3}, {3:F3})" -f $l.Bottom, $l.Top, $l.Density, $l.Wisp }

function InRoom($l) {
    return ($l.Bottom -ge 0) -and ($l.Bottom -le $l.Top) -and ($l.Top -le 1) -and
           ($l.Density -gt 0) -and ($l.Density -le 1) -and ($l.Wisp -ge 0) -and ($l.Wisp -le 1)
}

function Hours([double]$step) { $h = 0.0; while ($h -lt 24) { $h; $h += $step } }

# ---- 0% is today, bit for bit -------------------------------------------------------------------

$exact = $true
foreach ($h in (Hours 0.25)) {
    foreach ($a in 0.0, 0.25, 0.6, 1.0) {
        foreach ($dn in $true, $false) {
            $l = $S::WeatherLook([single]0, [single]$a, [single]$h, $dn)
            if (-not $l.IsNeutral) { $exact = $false }
            $b = $S::Blend([single]$a, $l, [single]0, $S::MorningLook([single]$h), [single]0, $S::MistLook)
            if (-not $b.IsNeutral) { $exact = $false }
        }
    }
}
Check "rows at 0%: the look is exactly neutral at every hour and amount" $exact
Check "neutral eased towards neutral stays exactly neutral" ($N.Towards($N, [single]0.37).IsNeutral)

# ---- the ceiling ---------------------------------------------------------------------------------

$rng = New-Object System.Random 12345
$worst = ""
$inside = $true
for ($i = 0; $i -lt 4000; $i++) {
    $h = [single]($rng.NextDouble() * 24)
    $dn = $rng.NextDouble() -lt 0.8
    $w = $S::WeatherLook([single]$rng.NextDouble(), [single]$rng.NextDouble(), $h, $dn)
    $m = $S::MorningLook($h)
    $l = $S::Blend([single]$rng.NextDouble(), $w, [single]($rng.NextDouble() * ($rng.NextDouble() -lt 0.5)), $m,
                   [single]($rng.NextDouble() * ($rng.NextDouble() -lt 0.5)), $S::MistLook)
    foreach ($x in $w, $m, $l) {
        if (-not (InRoom $x)) { $inside = $false; $worst = Text $x }
    }
}
Check "4000 random skies: every look stays inside the sliders' room" $inside $worst
Check "mist look inside the room" (InRoom $S::MistLook) (Text $S::MistLook)

# ---- Fog changes with the weather ----------------------------------------------------------------

$l = $S::WeatherLook([single]1, [single]1, [single]2, $true)
Check "100%, night, full spell: the sliders' fog" ($l.IsNeutral) (Text $l)
$l = $S::WeatherLook([single]1, [single]0.25, [single]14, $true)
Check "100%, 14:00, 25% spell: density 0.375, top 0.7, wisp 0.5" ((Near $l.Density 0.375) -and (Near $l.Top 0.7) -and (Near $l.Wisp 0.5) -and ($l.Bottom -eq 0)) (Text $l)
$l = $S::WeatherLook([single]1, [single]1, [single]14, $true)
Check "100%, 14:00, full spell: density 0.6" (Near $l.Density 0.6) (Text $l)
$l = $S::WeatherLook([single]1, [single]0.25, [single]14, $false)
Check "day/night off: no time of day, only the spell (density 0.625, top 1, wisp 0)" ((Near $l.Density 0.625) -and ($l.Top -eq 1) -and ($l.Wisp -eq 0)) (Text $l)
$l = $S::WeatherLook([single]0.5, [single]0.25, [single]14, $true)
Check "50%: half way (density 0.6875)" (Near $l.Density 0.6875) (Text $l)

Check "afternoon: none at 07:00 and 20:00, whole at 12, 14, 16" (($S::Afternoon(7) -eq 0) -and ($S::Afternoon(20) -eq 0) -and ($S::Afternoon(12) -eq 1) -and ($S::Afternoon(14) -eq 1) -and ($S::Afternoon(16) -eq 1))
$jump = 0.0
foreach ($h in (Hours 0.01)) { $d = [math]::Abs($S::Afternoon([single]($h + 0.01)) - $S::Afternoon([single]$h)); if ($d -gt $jump) { $jump = $d } }
Check "afternoon: continuous (largest step in 0.01 h)" ($jump -lt 0.01) ("{0:F4}" -f $jump)

# ---- Morning fog -------------------------------------------------------------------------------

Check "morning share: 0 at 12:00 and 20:00, 1 at 05:00, 0 at 10:00" (($S::MorningShare(12) -eq 0) -and ($S::MorningShare(20) -eq 0) -and ($S::MorningShare(5) -eq 1) -and ($S::MorningShare(10) -eq 0))
$rising = $true; $prev = -1.0
foreach ($h in 21.0, 22.0, 23.0, 23.99, 0.0, 1.0, 2.0, 3.0) { $v = $S::MorningShare([single]$h); if ($v -lt $prev) { $rising = $false }; $prev = $v }
Check "morning share: rising 21:00 -> 03:00 through midnight" $rising
Check "morning share: falling after 06:30" (($S::MorningShare(7) -lt 1) -and ($S::MorningShare(8) -lt $S::MorningShare(7)) -and ($S::MorningShare(9.5) -lt $S::MorningShare(8)))
$jump = 0.0
foreach ($h in (Hours 0.01)) { $d = [math]::Abs($S::MorningShare([single]($h + 0.01)) - $S::MorningShare([single]$h)); if ($d -gt $jump) { $jump = $d } }
Check "morning share: continuous, midnight included (largest step in 0.01 h)" ($jump -lt 0.01) ("{0:F4}" -f $jump)

$l = $S::MorningLook([single]4)
Check "morning look overnight: lower 40%, full density, the slider's break-up" ((Near $l.Bottom 0) -and (Near $l.Top 0.4) -and ($l.Density -eq 1) -and ($l.Wisp -eq 0)) (Text $l)
$l = $S::MorningLook([single]10)
Check "morning look at 10:00: lifted to the top, thinner, wispy" ((Near $l.Top 1) -and (Near $l.Bottom 0.6) -and (Near $l.Density 0.5) -and (Near $l.Wisp 0.8)) (Text $l)
$slab = $true
foreach ($h in (Hours 0.1)) { $x = $S::MorningLook([single]$h); if (-not (Near ($x.Top - $x.Bottom) 0.4)) { $slab = $false } }
Check "morning look: the slab keeps its 40% all the way up" $slab
Check "lift: 0 before 06:30 and after dark, 1 from 10:00" (($S::MorningLift(6) -eq 0) -and ($S::MorningLift(22) -eq 0) -and ($S::MorningLift(10) -eq 1) -and ($S::MorningLift(15) -eq 1))

Check "the evening and the morning of one fog share a key" ($S::MorningKey([uint32]41, [single]22) -eq $S::MorningKey([uint32]42, [single]5))
Check "the next evening has the next key" ($S::MorningKey([uint32]42, [single]22) -ne $S::MorningKey([uint32]42, [single]5))
$min = 2.0; $max = -1.0; $sum = 0.0
for ($k = 0; $k -lt 1000; $k++) { $p = $S::MorningPeak([uint32]$k); if ($p -lt $min) { $min = $p }; if ($p -gt $max) { $max = $p }; $sum += $p }
Check "morning peaks: inside 60-100% and spread across it" (($min -ge 0.6) -and ($max -le 1) -and ($min -lt 0.62) -and ($max -gt 0.98) -and (Near ($sum / 1000) 0.8 0.03)) ("min {0:F3} max {1:F3} mean {2:F3}" -f $min, $max, ($sum / 1000))
Check "morning peaks: the same key, the same morning" ($S::MorningPeak([uint32]7) -eq $S::MorningPeak([uint32]7))

# ---- Mist after rain ------------------------------------------------------------------------------

Check "mist: none while it rains (10%, 50%, 100%) on soaked ground" (($S::MistShare([single]1, [single]0.1, $false) -eq 0) -and ($S::MistShare([single]1, [single]0.5, $false) -eq 0) -and ($S::MistShare([single]1, [single]1, $false) -eq 0))
Check "mist: none after snow" ($S::MistShare([single]1, [single]0, $true) -eq 0)
Check "mist: none below 5% wet" (($S::MistShare([single]0.04, [single]0, $false) -eq 0) -and ($S::MistShare([single]0, [single]0, $false) -eq 0))
Check "mist: all of it from 60% wet with no rain" (($S::MistShare([single]0.6, [single]0, $false) -eq 1) -and ($S::MistShare([single]1, [single]0, $false) -eq 1))
$rising = $true; $prev = -1.0
foreach ($w in 0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6) { $v = $S::MistShare([single]$w, [single]0, $false); if ($v -lt $prev) { $rising = $false }; $prev = $v }
Check "mist: more the wetter the ground" $rising
$rising = $true; $prev = -1.0
foreach ($r in 0.1, 0.08, 0.06, 0.04, 0.02, 0.0) { $v = $S::MistShare([single]1, [single]$r, $false); if ($v -lt $prev) { $rising = $false }; $prev = $v }
Check "mist: rising as the rain eases off" $rising

# ---- Blend -------------------------------------------------------------------------------------

$w = $S::WeatherLook([single]1, [single]0.5, [single]14, $true)
$m = $S::MorningLook([single]4)
$l = $S::Blend([single]0, $w, [single]0.7, $m, [single]0, $S::MistLook)
Check "blend: morning fog alone is its own look" ((Text $l) -eq (Text $m)) (Text $l)
$l = $S::Blend([single]0, $w, [single]0, $m, [single]0.3, $S::MistLook)
Check "blend: mist alone is its own look" ((Text $l) -eq (Text $S::MistLook)) (Text $l)
$a = $S::Blend([single]0.5, $w, [single]0.000001, $m, [single]0, $S::MistLook)
Check "blend: a source fading out hands over smoothly" ((Near $a.Density $w.Density 1e-4) -and (Near $a.Top $w.Top 1e-4)) ("{0} vs {1}" -f (Text $a), (Text $w))
$l = $S::Blend([single]0.5, $w, [single]0.5, $m, [single]0, $S::MistLook)
Check "blend: equal amounts, the mean" ((Near $l.Top (($w.Top + $m.Top) / 2)) -and (Near $l.Density (($w.Density + $m.Density) / 2))) (Text $l)

# ---- Easing --------------------------------------------------------------------------------------

$x = $m
for ($i = 0; $i -lt 400; $i++) { $x = $x.Towards($N, [single]0.05) }
Check "a look easing back to neutral lands on it exactly" ($x.IsNeutral) (Text $x)

if ($failed -gt 0) { Write-Host "$failed check(s) FAILED"; exit 1 }
Write-Host "all checks passed"
