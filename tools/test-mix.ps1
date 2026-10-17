# Offline checks of the Mixed cloud style (Sky/CloudMix), run against the BUILT mod DLL -- no game.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-mix.ps1
#
# What it guards: the layer's three covers keep their order (the rain is cut at the smallest, so it
# always falls from drawn cloud -- invariant 2) and at 100% are all Classic's own (the author: "100%
# should mean 100%"); the curves only ever rise with the intensity; the puffs (Soft's, since 2026-10-17)
# are cut as at a share of the intensity that keeps their order across the type's areas; the hash -- part
# of what a city's seed reproduces (invariant 14) -- is in range, steady and even; the type pattern
# repeats with the weather map and bakes the same at the larger pattern.

param(
    [string]$Dll = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds\VolumetricClouds.dll"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
public static class MixResolver
{
    public static string[] Dirs;
    [ThreadStatic] static bool busy;
    public static void Install() { AppDomain.CurrentDomain.AssemblyResolve += Resolve; }
    static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        if (busy) return null;
        busy = true;
        try
        {
            string name = new AssemblyName(e.Name).Name;
            foreach (string dir in Dirs)
            {
                string path = Path.Combine(dir, name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        }
        finally { busy = false; }
    }
}

public static class MixBytes
{
    // The largest step between neighbouring texels of a square byte map, across its wrap too.
    public static int MaxStep(byte[] map, int size)
    {
        int most = 0;
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                int v = map[j * size + i];
                most = Math.Max(most, Math.Abs(v - map[j * size + (i + 1) % size]));
                most = Math.Max(most, Math.Abs(v - map[((j + 1) % size) * size + i]));
            }
        return most;
    }

    public static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
"@
[MixResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[MixResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$mix = $asm.GetType("VolumetricClouds.Sky.CloudMix")
$soft = $asm.GetType("VolumetricClouds.Sky.CloudSoft")
$failed = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { "  ok   $what" } else { "FAIL $what"; $script:failed++ }
}
function Value([string]$name) { return $mix.GetField($name).GetValue($null) }
function Call([string]$name, [object[]]$arguments) {
    $m = @($mix.GetMethods() | Where-Object { $_.Name -eq $name -and $_.GetParameters().Count -eq $arguments.Count })[0]
    return $m.Invoke($null, $arguments)
}

"=== the layer's covers (CloudShaderParams.ApplyMix, CloudStyle.CloudTestCoverage) ==="
$ordered = $true; $risesL = $true; $risesM = $true; $risesC = $true; $underCover = $true; $shareOk = $true
$prev = @(-1.0, -1.0, -1.0)
for ($i = 0; $i -le 200; $i++) {
    $x = [single]($i / 200.0)
    $l = [double](Call "LayerCoverInLayerArea" @($x)); $m = [double](Call "LayerCover" @($x)); $c = [double](Call "LayerCoverInCumulusArea" @($x))
    if (-not ($l -ge $m - 1e-6 -and $m -ge $c - 1e-6)) { $ordered = $false }
    if ($c -gt $x + 1e-6) { $underCover = $false }
    if ($l -lt $prev[0] - 1e-6) { $risesL = $false }; if ($m -lt $prev[1] - 1e-6) { $risesM = $false }
    if ($c -lt $prev[2] - 1e-6) { $risesC = $false }
    if ([Math]::Abs([double](Call "LayerShare" @($x)) * $x - $m) -gt 1e-6) { $shareOk = $false }
    $prev = @($l, $m, $c)
}
Check $ordered "in a layer area >= between >= in a puff area, at every intensity (the rain, cut at the last, is always under drawn layer)"
Check $underCover "the rain's cover is never more than the cover in effect"
Check ($risesL -and $risesM -and $risesC) "the three layer covers only rise with the intensity"
Check $shareOk "the layer's share is its cover over the intensity"
$lightFrom = [double](Value "LightFrom")
Check ([double](Call "LightShare" @([single]$lightFrom)) -eq 0 -and [Math]::Abs([double](Call "LightShare" @([single]1)) - 1) -lt 1e-6) ("the puffs' light rises to the layer's from {0:P0} to 100%" -f $lightFrom)
$at1 = @((Call "LayerCoverInLayerArea" @([single]1)), (Call "LayerCover" @([single]1)), (Call "LayerCoverInCumulusArea" @([single]1)))
Check (($at1 | Where-Object { [Math]::Abs([double]$_ - 1.0) -gt 1e-6 }).Count -eq 0) "at 100% all three covers are 1: the layer is Classic's own 100% (no gaps from below)"
Check ([double](Call "TypeStrength" @([single]1)) -eq 0) "at 100% the type is gone: every stretch of sky the same"
Check ([double](Call "TypeStrength" @([single]0.9)) -eq 1) "up to 90% the type tips the balance fully"
Check ([double](Call "LayerCover" @([single]0.2)) -eq 0) "no layer at 20% (between)"
Check ([Math]::Abs([double](Call "LayerCover" @([single]0.9)) - 0.9) -lt 1e-6) "from 85% the layer is cut at the cover in effect, as Classic's"

"=== the puffs (Soft's, cut as at CloudMix.PuffIntensity) ==="
$table = [single[]](Value "PuffTable")
Check ($table.Length -eq 21 -and ($table | Where-Object { $_ -lt 0 -or $_ -gt 2 }).Count -eq 0) "the puffs' share of the intensity: one value per 5%, 0..2"
$puffOrder = $true; $puffRange = $true; $line = ""
for ($i = 0; $i -le 100; $i++) {
    $x = [single]($i / 100.0)
    $a0 = [double](Call "PuffIntensity" @($x, [int]0)); $a1 = [double](Call "PuffIntensity" @($x, [int]1)); $a2 = [double](Call "PuffIntensity" @($x, [int]2))
    if (-not ($a0 -le $a1 + 1e-6 -and $a1 -le $a2 + 1e-6)) { $puffOrder = $false }
    if ($a0 -lt 0 -or $a2 -gt 1) { $puffRange = $false }
    if ($i % 20 -eq 0 -or $i -eq 47) { $line += (" {0}%: {1:F2}/{2:F2}/{3:F2}" -f $i, $a0, $a1, $a2) }
}
"  cut as at (layer area / between / puff area):$line"
Check $puffOrder "fewer puffs where the type leans to layer, more where it leans to puffs, at every intensity"
Check $puffRange "the intensity they are cut as at stays within 0..1"
Check ([double](Call "PuffIntensity" @([single]0, [int]2)) -eq 0) "no puffs at 0%"
$look = $soft.GetProperty("Default").GetValue($null, $null)
$puffCover = $soft.GetMethod("PuffCoverage")
$c47 = [double]$puffCover.Invoke($null, [object[]]@($look, [single](Call "PuffIntensity" @([single]0.47, [int]1))))
$s47 = [double]$puffCover.Invoke($null, [object[]]@($look, [single]0.47))
Check ($c47 -le $s47 + 1e-6) ("at 47% Mixed's puffs are cut at {0:P0} of the map, Soft's at {1:P0}: Mixed's light sky stays lighter, as before" -f $c47, $s47)

"=== the hash (CloudMix.Hash: the type pattern's lattice) ==="
$offsets = $mix.GetMethod("SeedOffsets")
$seedsOk = $true
foreach ($seed in 0, 1, 12345, -7, 2147483647, -2147483648, 89997) {
    $args3 = [object[]]@([int]$seed, [single]0, [single]0)
    [void]$offsets.Invoke($null, $args3)
    $ox = [double]$args3[1]; $oz = [double]$args3[2]
    if ($ox -lt 17 -or $ox -gt 1040 -or $oz -lt 53 -or $oz -gt 1076 -or $ox -ne [Math]::Floor($ox) -or $oz -ne [Math]::Floor($oz)) { $seedsOk = $false }
}
Check $seedsOk "every seed's offsets are whole numbers in range (small enough for float maths on any GPU)"

$hash = $mix.GetMethod("Hash")
$n = 48; $sum = 0.0; $sum2 = 0.0; $count = 0; $inRange = $true; $pairs = 0.0; $pairsK = 0.0
for ($z = 0; $z -lt $n; $z++) { for ($x = 0; $x -lt $n; $x++) {
    $h = [double]$hash.Invoke($null, [object[]]@([single]$x, [single]$z, [single]1, [single]517, [single]93))
    $hRight = [double]$hash.Invoke($null, [object[]]@([single]($x + 1), [single]$z, [single]1, [single]517, [single]93))
    $hK = [double]$hash.Invoke($null, [object[]]@([single]$x, [single]$z, [single]2, [single]517, [single]93))
    if ($h -lt 0 -or $h -ge 1) { $inRange = $false }
    $sum += $h; $sum2 += $h * $h; $count++
    $pairs += ($h - 0.5) * ($hRight - 0.5); $pairsK += ($h - 0.5) * ($hK - 0.5) } }
$mean = $sum / $count; $var = $sum2 / $count - $mean * $mean
$corr = ($pairs / $count) / $var; $corrK = ($pairsK / $count) / $var
Check $inRange "0 <= hash < 1 over $count lattice points"
Check ([Math]::Abs($mean - 0.5) -lt 0.03) ("its mean is {0:F3} (0.5 for an even spread)" -f $mean)
Check ([Math]::Abs($var - 1.0 / 12.0) -lt 0.01) ("its variance is {0:F4} (1/12 = 0.0833 for an even spread)" -f $var)
Check ([Math]::Abs($corr) -lt 0.08) ("neighbouring points are unrelated (correlation {0:F3})" -f $corr)
Check ([Math]::Abs($corrK) -lt 0.08) ("a point's numbers are unrelated to each other (correlation {0:F3})" -f $corrK)
$again = [double]$hash.Invoke($null, [object[]]@([single]7, [single]11, [single]4, [single]517, [single]93))
$again2 = [double]$hash.Invoke($null, [object[]]@([single]7, [single]11, [single]4, [single]517, [single]93))
Check ($again -eq $again2) "the same point gives the same number every time"

"=== the type pattern (CloudMix.TypeAt, the type map's alpha) ==="
# Over many cities: one weather repeat holds only a few of the pattern's lattice values, so a single
# city leans one way or the other -- but the map drifts through all of it, and over cities neither wins.
$typeOk = $true; $periodic = $true; $sumT = 0.0; $countT = 0; $both = 0; $cities = 24
for ($s = 0; $s -lt $cities; $s++) {
    $seed = 1000003 * $s + 12345
    $args3 = [object[]]@([int]$seed, [single]0, [single]0); [void]$mix.GetMethod("SeedOffsets").Invoke($null, $args3)
    $sx = [single]$args3[1]; $sz = [single]$args3[2]
    $lo = 1.0; $hi = 0.0
    for ($j = 0; $j -lt 24; $j++) { for ($i = 0; $i -lt 24; $i++) {
        $t = [double](Call "TypeAt" @([single]($i / 24.0), [single]($j / 24.0), $sx, $sz))
        if ($t -lt 0 -or $t -gt 1) { $typeOk = $false }
        $lo = [Math]::Min($lo, $t); $hi = [Math]::Max($hi, $t); $sumT += $t; $countT++ } }
    if ($lo -lt 0.3 -and $hi -gt 0.7) { $both++ }
    foreach ($v in 0.1, 0.37, 0.8) {
        $a = [double](Call "TypeAt" @([single]0, [single]$v, $sx, $sz)); $b = [double](Call "TypeAt" @([single]1, [single]$v, $sx, $sz))
        if ([Math]::Abs($a - $b) -gt 1e-4) { $periodic = $false } }
}
Check $typeOk "the type stays within 0..1"
Check $periodic "the type repeats with the weather map (the same at its two edges): it drifts with the clouds"
Check ($both -ge [int]($cities * 0.75)) ("both kinds of sky occur in {0} of {1} cities' repeats" -f $both, $cities)
Check ([Math]::Abs($sumT / $countT - 0.5) -lt 0.08) ("over {0} cities neither kind wins (mean {1:F2})" -f $cities, ($sumT / $countT))

$args3 = [object[]]@([int]12345, [single]0, [single]0); [void]$mix.GetMethod("SeedOffsets").Invoke($null, $args3)
$sx = [single]$args3[1]; $sz = [single]$args3[2]
$size = [int](Value "MapSize")
$sw = [Diagnostics.Stopwatch]::StartNew()
$type = [byte[]](Call "BakeType" @([int]$size, $sx, $sz))
$typeMs = $sw.Elapsed.TotalMilliseconds
$type2 = [byte[]](Call "BakeType" @([int]$size, $sx, $sz))
Check ($type.Length -eq $size * $size -and [MixBytes]::Same($type, $type2)) ("the type map bakes one byte per texel, the same every time ({0:F0} ms here, once per seed)" -f $typeMs)

"=== Larger weather pattern (1.5.0: the weather map and the type map 4x wider, the patches the same size) ==="
$fieldType = $asm.GetType("VolumetricClouds.Sky.CloudDensityField")
$larger = [int]$fieldType.GetField("LargerScale").GetValue($null)
$bigSize = [int](Call "MapSizeFor" @($larger))
$sw = [Diagnostics.Stopwatch]::StartNew()
$bigType = [byte[]](Call "BakeType" @([int]$bigSize, $sx, $sz, $larger))
$bigMs = $sw.Elapsed.TotalMilliseconds
$typeStep = [MixBytes]::MaxStep($bigType, $bigSize); $smallStep = [MixBytes]::MaxStep($type, $size)
Check ($bigType.Length -eq $bigSize * $bigSize -and [Math]::Abs($typeStep - $smallStep) -le 2) ("the larger type pattern: a byte per texel, as smooth per texel (so per metre) as the usual one, seamless over its repeat (largest step {0}, usual {1}; {2:F0} ms)" -f $typeStep, $smallStep, $bigMs)

"=== the log ==="
$shape = [string](Call "DescribeShape" @([single]450, [int]1))
"  " + $shape
Check ($shape -match "puffs" -and $shape -match "generator 2" -and $shape -cmatch '^[\x20-\x7E]+$') "the shape line names the puffs and the generator, ASCII"
"  " + [string](Call "DescribeShape" @([single]450, $larger))
"  " + [string](Call "DescribeMix" @([single]0.47))

""
if ($failed -eq 0) { "all mix checks pass" } else { "$failed mix check(s) FAILED"; exit 1 }
