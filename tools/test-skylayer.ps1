# Offline checks of Sky/SkyLayerMix.cs -- which of the sky layer's pictures each kind shows at its amount
# (1.5.0). The class has no Unity dependency, so the SOURCE is compiled directly.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-skylayer.ps1
#
# What it guards: 0 shows nothing; each picture shows alone at its own stop (1/3, 2/3, 1 for three); in
# between, two neighbours at most, their weights adding up to 1 once the first has faded in; more is
# never less cloud in total; every picture belongs to exactly one kind; the names match the embedded files.

$source = Join-Path $PSScriptRoot "..\VolumetricClouds\Sky\SkyLayerMix.cs"
Add-Type -TypeDefinition (Get-Content $source -Raw)

$M = [VolumetricClouds.Sky.SkyLayerMix]
$failed = 0

function Check([string]$name, [bool]$ok, [string]$detail = "") {
    if ($ok) { Write-Host "ok    $name  $detail" }
    else { Write-Host "FAIL  $name  $detail"; $script:failed++ }
}

$n = $M::Pictures.Length
$w = New-Object 'float[]' $n

function Weights([int]$kind, [float]$amount) {
    $M::Weights($kind, $amount, $script:w)
    return ,([float[]]$script:w.Clone())
}

function Sum([float[]]$v) { $s = 0.0; foreach ($x in $v) { $s += $x }; return $s }
function NonZero([float[]]$v) { $c = 0; foreach ($x in $v) { if ($x -gt 0) { $c++ } }; return $c }

# Which pictures each kind ever shows.
$owner = @{}
for ($k = 0; $k -lt $M::Kinds; $k++) {
    $seen = @{}
    for ($i = 0; $i -le 300; $i++) {
        $v = Weights $k ($i / 300.0)
        for ($p = 0; $p -lt $n; $p++) { if ($v[$p] -gt 0) { $seen[$p] = $true } }
    }
    foreach ($p in $seen.Keys) {
        Check "picture $($M::Pictures[$p]) has one kind" (-not $owner.ContainsKey($p) -or $owner[$p] -eq $k) "kind $k"
        $owner[$p] = $k
    }
}
Check "every picture is used" ($owner.Count -eq $n) "$($owner.Count) of $n"

for ($k = 0; $k -lt $M::Kinds; $k++) {
    $name = $M::KindNames[$k]
    Check "$name at 0 shows nothing" ((Sum (Weights $k 0)) -eq 0)
    Check "$name below 0 shows nothing" ((Sum (Weights $k -0.5)) -eq 0)

    $count = 0; for ($p = 0; $p -lt $n; $p++) { if ($owner[$p] -eq $k) { $count++ } }
    for ($stop = 1; $stop -le $count; $stop++) {
        $v = Weights $k ($stop / $count)
        Check "$name at $stop/$count shows one picture, fully" ((NonZero $v) -eq 1 -and [math]::Abs((Sum $v) - 1) -lt 1e-5) ("sum " + (Sum $v))
    }
    $over = Weights $k 1.5
    Check "$name above 100% is 100%" ((NonZero $over) -eq 1 -and [math]::Abs((Sum $over) - 1) -lt 1e-5)

    $prev = -1.0
    $monotone = $true; $atMostTwo = $true; $sumOne = $true
    for ($i = 0; $i -le 1000; $i++) {
        $a = $i / 1000.0
        $v = Weights $k $a
        $s = Sum $v
        if ($s -lt $prev - 1e-5) { $monotone = $false }
        if ((NonZero $v) -gt 2) { $atMostTwo = $false }
        if ($a * $count -ge 1 -and [math]::Abs($s - 1) -gt 1e-4) { $sumOne = $false }
        $prev = $s
    }
    Check "$name never less in total as it rises" $monotone
    Check "$name shows two pictures at most" $atMostTwo
    Check "$name adds up to 1 once in" $sumOne
}

# "Dynamic distant clouds": every kind comes and goes, the total stays capped, nothing jumps, and the
# sky is rarely empty. Sampled over 200 lives of the travelling copies, for three seeds.
$a = New-Object 'float[]' 4
$b = New-Object 'float[]' 4
foreach ($seed in 1, 12345, -987654321) {
    $present = @(0, 0, 0, 0); $empty = 0; $maxTotal = 0.0; $maxStep = 0.0; $inRange = $true
    $samples = 20000
    for ($i = 0; $i -lt $samples; $i++) {
        $M::Dynamic($i * 0.01, $seed, $a)
        $M::Dynamic($i * 0.01 + 0.01, $seed, $b)
        $total = 0.0
        for ($k = 0; $k -lt 4; $k++) {
            if ($a[$k] -lt 0 -or $a[$k] -gt 1) { $inRange = $false }
            if ($a[$k] -gt 0) { $present[$k]++ }
            $total += $a[$k]
            $maxStep = [math]::Max([double]$maxStep, [double][math]::Abs($b[$k] - $a[$k]))
        }
        if ($total -eq 0) { $empty++ }
        $maxTotal = [math]::Max([double]$maxTotal, [double]$total)
    }
    Check "dynamic, seed ${seed}: amounts within 0..1" $inRange
    Check "dynamic, seed ${seed}: total capped at 130%" ($maxTotal -le 1.3001) ("{0:F3}" -f $maxTotal)
    Check "dynamic, seed ${seed}: no jump between frames a hundredth of a life apart" ($maxStep -lt 0.02) ("{0:F4}" -f $maxStep)
    for ($k = 0; $k -lt 4; $k++) {
        $share = $present[$k] / $samples
        Check "dynamic, seed ${seed}: $($M::KindNames[$k]) comes and goes" ($share -gt 0.2 -and $share -lt 0.95) ("{0:P0} of the time" -f $share)
    }
    Check "dynamic, seed ${seed}: the sky is seldom empty" ($empty / $samples -lt 0.1) ("{0:P1} empty" -f ($empty / $samples))
}

Check "describe off" ($M::Describe(0, 0) -eq "off")
Check "describe a blend" ($M::Describe(0, 0.5) -eq "cirrus-1 50% + cirrus-2 50%") $M::Describe(0, 0.5)

# The names match what make-skylayer.ps1 wrote and the project embeds.
$folder = Join-Path $PSScriptRoot "..\VolumetricClouds\Resources\SkyLayer"
foreach ($p in $M::Pictures) {
    Check "embedded file for $p" (Test-Path (Join-Path $folder "$p.jpg"))
}
$bytes = (Get-ChildItem $folder -Filter *.jpg | Measure-Object Length -Sum).Sum
Check "pictures within 5 MB together" ($bytes -lt 5MB) ("{0:N0} bytes" -f $bytes)

if ($failed -gt 0) { Write-Host "$failed FAILED"; exit 1 }
Write-Host "all passed"
