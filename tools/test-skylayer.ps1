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

Check "describe off" ($M::Describe(0, 0) -eq "off")
Check "describe a blend" ($M::Describe(0, 0.5) -eq "cirrus-1 50% + cirrus-2 50%") $M::Describe(0, 0.5)

# The names match what make-skylayer.ps1 wrote and the project embeds.
$folder = Join-Path $PSScriptRoot "..\VolumetricClouds\Resources\SkyLayer"
foreach ($p in $M::Pictures) {
    Check "embedded file for $p" (Test-Path (Join-Path $folder "$p.jpg"))
}
$bytes = (Get-ChildItem $folder -Filter *.jpg | Measure-Object Length -Sum).Sum
Check "pictures within 2 MB together" ($bytes -lt 2MB) ("{0:N0} bytes" -f $bytes)

if ($failed -gt 0) { Write-Host "$failed FAILED"; exit 1 }
Write-Host "all passed"
