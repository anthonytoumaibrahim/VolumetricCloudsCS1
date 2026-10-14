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

# "Dynamic distant clouds": there are distant clouds less than half the time (the author's call) but
# some of the time; the cover picks the kinds; the total stays capped and nothing jumps. Sampled over
# 300 lives of the travelling copies, for three seeds.
$a = New-Object 'float[]' 4
$b = New-Object 'float[]' 4
foreach ($seed in 1, 12345, -987654321) {
    $shown = 0; $maxTotal = 0.0; $maxStep = 0.0; $inRange = $true
    $samples = 30000
    for ($i = 0; $i -lt $samples; $i++) {
        $cover = 0.5 + 0.5 * [math]::Sin($i * 0.0007)
        $M::Dynamic($i * 0.01, $seed, $cover, $a)
        $M::Dynamic($i * 0.01 + 0.01, $seed, $cover, $b)
        $total = 0.0
        for ($k = 0; $k -lt 4; $k++) {
            if ($a[$k] -lt 0 -or $a[$k] -gt 1) { $inRange = $false }
            $total += $a[$k]
            $maxStep = [math]::Max([double]$maxStep, [double][math]::Abs($b[$k] - $a[$k]))
        }
        if ($total -gt 0) { $shown++ }
        $maxTotal = [math]::Max([double]$maxTotal, [double]$total)
    }
    $share = $shown / $samples
    Check "dynamic, seed ${seed}: amounts within 0..1" $inRange
    Check "dynamic, seed ${seed}: total capped at 130%" ($maxTotal -le 1.3001) ("{0:F3}" -f $maxTotal)
    Check "dynamic, seed ${seed}: no jump between frames a hundredth of a life apart" ($maxStep -lt 0.02) ("{0:F4}" -f $maxStep)
    Check "dynamic, seed ${seed}: distant clouds some of the time, under half" ($share -gt 0.15 -and $share -lt 0.5) ("{0:P0} of the time" -f $share)
}

# Which kinds each cover gets, summed over a long stretch (seed 1).
function KindsAt([float]$cover) {
    $sum = @(0.0, 0.0, 0.0, 0.0)
    for ($i = 0; $i -lt 20000; $i++) {
        $M::Dynamic($i * 0.013, 1, $cover, $script:a)
        for ($k = 0; $k -lt 4; $k++) { $sum[$k] += $script:a[$k] }
    }
    return ,$sum
}
function Top($sum) { $best = 0; for ($k = 1; $k -lt 4; $k++) { if ($sum[$k] -gt $sum[$best]) { $best = $k } }; return $best }
$clear = KindsAt 0.1; $fair = KindsAt 0.35; $partly = KindsAt 0.55; $overcast = KindsAt 0.95
Check "dynamic: a clear sky gets mostly cirrus" ((Top $clear) -eq 0) $M::KindNames[(Top $clear)]
Check "dynamic: fair weather gets cirrus or sheep clouds" ((Top $fair) -le 1) $M::KindNames[(Top $fair)]
Check "dynamic: a partly cloudy sky gets cumulus on the horizon" ((Top $partly) -eq 2) $M::KindNames[(Top $partly)]
Check "dynamic: towards overcast, cloud layers" ((Top $overcast) -eq 3) $M::KindNames[(Top $overcast)]
Check "dynamic: no cirrus under overcast" ($overcast[0] -eq 0)

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
