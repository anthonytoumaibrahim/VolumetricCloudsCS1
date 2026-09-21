# Offline check of Sky/AmountReporter.cs -- when our rain and our fog write their always-on
# log lines. The class has no Unity dependency, so the SOURCE is compiled directly (the same
# way CloudNoise3D.cs is), no game and no built DLL needed.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-reporter.ps1

$source = Join-Path $PSScriptRoot "..\VolumetricClouds\Sky\AmountReporter.cs"
Add-Type -TypeDefinition (Get-Content $source -Raw)

$failed = 0

function Lines([double[]]$amounts) {
    $reporter = New-Object VolumetricClouds.Sky.AmountReporter
    $logged = @()
    foreach ($a in $amounts) {
        if ($reporter.Moved([single]$a)) { $logged += [math]::Round($a, 3) }
    }
    return ,$logged
}

function Check([string]$name, $actual, $expected) {
    $a = ($actual -join ' ')
    $e = ($expected -join ' ')
    if ($a -eq $e) { Write-Host "ok    $name  ->  [$a]" }
    else { Write-Host "FAIL  $name  ->  [$a]  expected [$e]"; $script:failed++ }
}

# A slider dragged from 0 to 100% in 1% steps, then held for 500 frames.
$up = @(0..100 | ForEach-Object { $_ / 100.0 }) + @(1..500 | ForEach-Object { 1.0 })
# (0.53 and 0.78, not 0.52 and 0.77: in single precision 0.52 - 0.27 is a hair under 0.25.)
Check "drag up and hold" (Lines $up) @(0.02, 0.27, 0.53, 0.78, 1)

# ...and back down: a line per quarter and one for "gone".
$down = $up + @(100..0 | ForEach-Object { $_ / 100.0 }) + @(1..500 | ForEach-Object { 0.0 })
Check "up, then down" (Lines $down) @(0.02, 0.27, 0.53, 0.78, 1, 0.75, 0.5, 0.25, 0)

# A steady sky says nothing after its first line, and a clear one says nothing at all.
Check "steady 40%" (Lines (@(1..2000 | ForEach-Object { 0.4 }))) @(0.4)
Check "always clear" (Lines (@(1..2000 | ForEach-Object { 0.0 }))) @()

# Noise around the start threshold must not chatter: below 'gone' is the only way out.
$flutter = @(1..400 | ForEach-Object { if ($_ % 2) { 0.021 } else { 0.012 } })
Check "flutter at the threshold" (Lines $flutter) @(0.021)

# The smoothed approach the fog really makes (exponential, 5 s, 60 fps) to 30% and back.
$amount = 0.0; $series = @()
foreach ($target in @(0.3, 0.0)) {
    1..3000 | ForEach-Object {
        $amount = $amount + ($target - $amount) * (1 - [math]::Exp(-(1 / 60.0) / 5.0))
        if ([math]::Abs($target - $amount) -lt 0.0005) { $amount = $target }
        $series += $amount
    }
}
# Four lines: it starts, it has travelled a quarter, it is a quarter back down, it is gone.
$logged = Lines $series
Check "fog rolls in to 30% and out: line count" @($logged.Count) @(4)

if ($failed -gt 0) { Write-Host "$failed check(s) FAILED"; exit 1 }
Write-Host "all checks passed"
