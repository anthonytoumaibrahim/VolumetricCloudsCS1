# Offline checks of the Cumulus cloud style (Sky/CloudStyle) and its noise (Sky/CumulusNoise3D,
# made by Sky/WorleyVolume), run against the BUILT mod DLL -- no game needed.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-cumulus.ps1
#
# What it guards: the noise is part of what a city's seed reproduces (invariant 14), so it must be
# the same bytes whatever the thread count, tile without a seam and keep its mean down its mips;
# the style's numbers are EVE-Redux V5's (their cumulus curve, evaluated as Unity does, and the
# cubics the shader evaluates must agree with it); the tallest cloud is where the curve says; the
# light gain keeps a sunlit face as bright as Classic's; and the coverage table (the author's "the
# same amount of cloud") runs from nothing to more, never back.

param(
    [string]$Dll = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds\VolumetricClouds.dll"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
public static class CumulusResolver
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

public static class CumulusStats
{
    // Mean |v(x) - v(x+1)| over each y-z slice; the last one is across the wrap (n-1 -> 0).
    public static double[] Profile(byte[] v, int n)
    {
        double[] s = new double[n];
        for (int x = 0; x < n; x++)
        {
            double sum = 0; int x2 = (x + 1) % n;
            for (int z = 0; z < n; z++)
                for (int y = 0; y < n; y++) { int row = y * n + z * n * n; sum += Math.Abs(v[row + x] - v[row + x2]); }
            s[x] = sum / ((double)n * n);
        }
        return s;
    }

    public static double[] Stats(byte[] v)
    {
        double sum = 0; int min = 255, max = 0;
        foreach (byte b in v) { sum += b; if (b < min) min = b; if (b > max) max = b; }
        return new double[] { sum / v.Length, min, max };
    }

    public static bool Same(byte[][] a, byte[][] b)
    {
        if (a.Length != b.Length) return false;
        for (int l = 0; l < a.Length; l++)
        {
            if (a[l].Length != b[l].Length) return false;
            for (int i = 0; i < a[l].Length; i++) if (a[l][i] != b[l][i]) return false;
        }
        return true;
    }
}
"@
[CumulusResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[CumulusResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$gen = $asm.GetType("VolumetricClouds.Sky.CumulusNoise3D")
$style = $asm.GetType("VolumetricClouds.Sky.CloudStyle")
$failed = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { "  ok   $what" } else { "FAIL $what"; $script:failed++ }
}
function Value([string]$name) { return $style.GetField($name).GetValue($null) }
function Call([string]$name, [object[]]$arguments) { return $style.GetMethod($name).Invoke($null, $arguments) }

$size = [int]$gen.GetField("Size").GetValue($null)
$seedFor = $gen.GetMethod("SeedFor")
$generate = $gen.GetMethod("Generate")
$seed = [int]$seedFor.Invoke($null, [object[]]@(67890))

"=== CumulusNoise3D.Generate($size, seed, threads) ==="
$sw = [Diagnostics.Stopwatch]::StartNew()
$one = [byte[][]]$generate.Invoke($null, [object[]]@($size, $seed, 1))
$tOne = $sw.Elapsed.TotalMilliseconds
$sw.Restart()
$many = [byte[][]]$generate.Invoke($null, [object[]]@($size, $seed, 0))
$tMany = $sw.Elapsed.TotalMilliseconds
"  one thread: $([int]$tOne) ms, all cores ($([Environment]::ProcessorCount), capped at 8): $([int]$tMany) ms"

Check ([CumulusStats]::Same($one, $many)) "the same bytes on one thread and on every core (a seed reproduces it)"
Check ($one.Length -eq 8) "8 mip levels for 128^3 (got $($one.Length))"
$sizesOk = $true
for ($l = 0; $l -lt $one.Length; $l++) { $e = [int][Math]::Pow($size -shr $l, 3); if ($one[$l].Length -ne $e) { $sizesOk = $false } }
Check $sizesOk "each level is half the edge of the one above, down to 1^3"

$s0 = [CumulusStats]::Stats($one[0])
"  level 0: mean $($s0[0].ToString('F1')), min $($s0[1]), max $($s0[2])"
Check (($s0[1] -eq 0) -and ($s0[2] -eq 255)) "level 0 spans the whole 0..255 (min/max normalised, as EVE's)"
Check (($s0[0] -gt 120) -and ($s0[0] -lt 200)) "level 0's mean is puffy spheres, not empty or solid (got $($s0[0].ToString('F1')); the preview's EVE noise had 0.63 x 255 = 162)"

$meansOk = $true
for ($l = 1; $l -lt $one.Length; $l++) { $m = ([CumulusStats]::Stats($one[$l]))[0]; if ([Math]::Abs($m - $s0[0]) -gt 1.5) { $meansOk = $false; "  level $l mean $($m.ToString('F1'))" } }
Check $meansOk "every mip level keeps the mean (box filter): distant billows fade to their average"

$profile = [CumulusStats]::Profile($one[0], $size)
$inner = $profile[0..($size - 2)]
$m = ($inner | Measure-Object -Average).Average
$sd = [Math]::Sqrt((($inner | ForEach-Object { ($_ - $m) * ($_ - $m) }) | Measure-Object -Sum).Sum / $inner.Count)
$wrap = $profile[$size - 1]
$z = ($wrap - $m) / $sd
"  mean step between neighbours per slice: $($m.ToString('F2')) +- $($sd.ToString('F2')); across the wrap $($wrap.ToString('F2')) (z = $($z.ToString('F1')))"
Check ([Math]::Abs($z) -lt 3.0) "it tiles without a seam: the slice across the wrap is like any other slice"

$other = [byte[][]]$generate.Invoke($null, [object[]]@($size, [int]$seedFor.Invoke($null, [object[]]@(12345)), 0))
Check (-not [CumulusStats]::Same($one, $other)) "another city's seed gives another volume"

$detailGen = $asm.GetType("VolumetricClouds.Sky.CloudDetail3D")
$detailLevels = [byte[][]]$detailGen.GetMethod("Generate").Invoke($null, [object[]]@(128, [int]$detailGen.GetMethod("SeedFor").Invoke($null, [object[]]@(67890)), 0))
Check (-not [CumulusStats]::Same($one, $detailLevels)) "it is not cloud detail's texture (its own seed and octaves)"

Check ($tMany -lt 15000) "quick enough for a city load's worker thread here (<15 s; Mono in the game is slower)"

""
"=== CloudStyle: EVE's cumulus coverage curve ==="
$t = [single[]](Value "CurveTime"); $v = [single[]](Value "CurveValue")
$curveOk = $true
for ($i = 0; $i -lt $t.Length; $i++) {
    $got = [single](Call "Curve" @([single]$t[$i]))
    if ([Math]::Abs($got - $v[$i]) -gt 1e-5) { $curveOk = $false; "  key $i at $($t[$i]): $got, not $($v[$i])" }
}
Check $curveOk "the curve passes through every key (Gurdamma's Cumulus: -0.004 at 0.017, 0.976 at 0.087, -0.007 at 0.973)"
Check ([single](Call "Curve" @([single]0.0)) -eq $v[0] -and [single](Call "Curve" @([single]1.0)) -eq $v[$v.Length - 1]) "outside the keys it holds the end values, as Unity's float curves do"

# The shader evaluates the two segments as cubics; they must be the curve.
$cubicOk = $true
for ($k = 0; $k -lt $t.Length - 1; $k++) {
    $c = [single[]](Call "SegmentCubic" @([int]$k))
    for ($j = 0; $j -le 20; $j++) {
        $s = $j / 20.0
        $h = $t[$k] + ($t[$k + 1] - $t[$k]) * $s
        $cubic = (($c[0] * $s + $c[1]) * $s + $c[2]) * $s + $c[3]
        $ref = [single](Call "Curve" @([single]$h))
        if ([Math]::Abs($cubic - $ref) -gt 2e-4) { $cubicOk = $false; "  segment $k at s=$s : cubic $cubic, curve $ref" }
    }
}
Check $cubicOk "the shader's cubics (SegmentCubic) are the curve, both segments, end to end"

# A Hermite through Unity's keys, computed here independently of the mod.
$tin = [single[]](Value "CurveIn"); $tout = [single[]](Value "CurveOut")
function Hermite([double]$x) {
    $i = 1; while ($i -lt $t.Length - 1 -and $x -gt $t[$i]) { $i++ }
    $dt = $t[$i] - $t[$i - 1]; $s = ($x - $t[$i - 1]) / $dt
    $m0 = $tout[$i - 1] * $dt; $m1 = $tin[$i] * $dt
    return (2 * $s * $s * $s - 3 * $s * $s + 1) * $v[$i - 1] + ($s * $s * $s - 2 * $s * $s + $s) * $m0 + (-2 * $s * $s * $s + 3 * $s * $s) * $v[$i] + ($s * $s * $s - $s * $s) * $m1
}
$hermiteOk = $true
foreach ($x in 0.03, 0.05, 0.1, 0.2, 0.3, 0.5, 0.7, 0.9, 0.95) {
    $a = Hermite $x; $b = [single](Call "Curve" @([single]$x))
    if ([Math]::Abs($a - $b) -gt 1e-4) { $hermiteOk = $false; "  h=$x : Hermite $a, mod $b" }
}
Check $hermiteOk "it is Unity's Hermite between the keys (checked against an independent computation)"

$span = [single](Value "Span"); $tallest = [single](Value "Tallest"); $cmax = [single](Value "CoverageMax")
$atTop = [single](Call "Curve" @([single]($tallest / $span)))
"  tallest cloud $($tallest.ToString('F0')) m above the base; curve there $($atTop.ToString('F4')) (1 - most coverage = $((1 - $cmax).ToString('F4')))"
Check ([Math]::Abs($atTop - (1 - $cmax)) -lt 1e-3) "the tallest cloud is where the curve falls to 1 - the most coverage"
$above = [single](Call "Density" @([single]$cmax, [single](($tallest + 5) / $span), [single]1.0))
Check ($above -eq 0) "above it there is no cloud even at the most coverage and the strongest noise (the march may stop there)"
$inside = [single](Call "Density" @([single]$cmax, [single]0.2, [single]1.0))
Check ($inside -eq 1) "deep inside, at the most coverage and a puff's core, the density is full (a surface, not a haze)"
$monoOk = $true; $prev = -1.0
foreach ($n in 0.0, 0.2, 0.4, 0.6, 0.8, 1.0) { $d = [single](Call "Density" @([single]0.3, [single]0.2, [single]$n)); if ($d -lt $prev) { $monoOk = $false }; $prev = $d }
Check $monoOk "more noise, never less cloud: the noise's puffs are the clouds"

""
"=== CloudStyle: the light ==="
function HG([double]$c, [double]$g) { return (1 - $g * $g) / [Math]::Pow(1 + $g * $g - 2 * $g * $c, 1.5) }
$gain = [single](Value "LightGain")
$single0 = 0.1 * (HG 0 0.95) + 0.2 * (HG 0 0.8)
$multiple0 = 3.0 * (HG 0 0.2) + 0.3 * (HG 0 (-0.4))
$old = 0.65 + 0.35 * [Math]::Min((HG 0 0.55), 5.0)
"  side-on: single $($single0.ToString('F4')), multiple $($multiple0.ToString('F4')), gain $($gain.ToString('F4')), Classic's light $($old.ToString('F4'))"
Check ([Math]::Abs($gain * ($single0 + $multiple0) - $old) -lt 1e-4) "gain x EVE's lobes = Classic's light on a sunlit face seen side-on (brightness settings keep their meaning)"
$lit = [single](Call "SunLight" @([single]0.0, [single]0.0))
Check ([Math]::Abs($lit - $old) -lt 1e-4) "a sunlit surface side-on (no cloud towards the sun) gets exactly that"
$rim = [single](Call "SunLight" @([single]1.0, [single]0.0))
$cap = [single](Value "SingleCap")
"  straight into the sun, no cloud in the way: $($rim.ToString('F2')) (single lobes capped at $cap)"
Check ($rim -lt 5) "straight into the sun the silver lining is bright but cannot blow out (< 5 x a side-lit face)"
$deep = [single](Call "SunLight" @([single]0.0, [single]10.0))
Check (($deep -gt 0) -and ($deep -lt 0.02 * $old)) "ten optical depths in, next to no sunlight is left"

""
"=== The weather map's normal scores (CloudDensityField.ScoreBytes): the same for every city ==="
$fieldType = $asm.GetType("VolumetricClouds.Sky.CloudDensityField")
$quantile = $fieldType.GetMethod("NormalQuantile")
$range = [single]$fieldType.GetField("ScoreRange").GetValue($null)
$qOk = $true
foreach ($pair in @(@(0.5, 0.0), @(0.9, 1.2815516), @(0.975, 1.959964), @(0.01, -2.326348), @(0.999, 3.090232))) {
    $got = [single]$quantile.Invoke($null, [object[]]@([single]$pair[0]))
    if ([Math]::Abs($got - $pair[1]) -gt 2e-4) { $qOk = $false; "  NormalQuantile($($pair[0])) = $got, not $($pair[1])" }
}
Check $qOk "NormalQuantile is the standard normal's (0.5 -> 0, 0.9 -> 1.2816, 0.975 -> 1.96, 0.01 -> -2.33)"
$topTenth = [single](Value "TopTenthScore")
Check ([Math]::Abs($topTenth - [single]$quantile.Invoke($null, [object[]]@([single]0.9))) -lt 1e-4) "TopTenthScore is the normal score a tenth of a map lies above"

$any = [Reflection.BindingFlags]"Instance,Public,NonPublic"
$shareOk = $true; $orderOk = $true
foreach ($fs in 12345, 23456, 34567) {
    $f = [Activator]::CreateInstance($fieldType, [object[]]@($fs))
    $scores = [byte[]]$fieldType.GetMethod("ScoreBytes").Invoke($f, @())
    $density = [single[]]$fieldType.GetField("_density", $any).GetValue($f)
    $cut = [int](($topTenth + $range) / (2 * $range) * 255 + 0.5)
    $above = 0; $sum = 0.0
    foreach ($b in $scores) { if ($b -ge $cut) { $above++ }; $sum += $b }
    $share = $above / $scores.Length
    "  map $fs : $($share.ToString('P1')) of it at or above the top-tenth score, mean score byte $(($sum / $scores.Length).ToString('F1'))"
    if ([Math]::Abs($share - 0.1) -gt 0.012) { $shareOk = $false }
    # Rank order: a denser texel never gets a lower score (checked on a spread of pairs).
    $idx = [int[]](0..($density.Length - 1) | Where-Object { $_ % 97 -eq 0 })
    $sorted = [int[]]($idx | Sort-Object { $density[$_] })
    for ($i = 1; $i -lt $sorted.Length; $i++) { if ($scores[$sorted[$i]] -lt $scores[$sorted[$i - 1]]) { $orderOk = $false; break } }
}
Check $shareOk "on every map a tenth of it is at or above the top-tenth score (to 1.2%): the same share for every city"
Check $orderOk "a denser texel never has a lower score: the score is the value's rank"

$line = [single[]]$style.GetMethod("Line", [Type[]]@([single])).Invoke($null, [object[]]@([single]0.1))
$outA = [single]0; $outB = [single]0
$lineArgs = [object[]]@([single]0.1, $outA, $outB)
[void]$style.GetMethod("Line", [Type[]]@([single], [single].MakeByRefType(), [single].MakeByRefType())).Invoke($null, $lineArgs)
Check (($lineArgs[1] -eq $line[0]) -and ($lineArgs[2] -eq $line[1])) "the shader's allocation-free Line gives the same line"
$cubics = [single[][]](Value "Cubics")
Check ((@(0..3 | Where-Object { $cubics[0][$_] -ne ([single[]](Call "SegmentCubic" @([int]0)))[$_] })).Count -eq 0) "the cached cubics are SegmentCubic's"
$tMedian = 0.5
$tTop = ($topTenth + $range) / (2 * $range)
Check ([Math]::Abs(($line[0] + $line[1] * $tMedian) - 0.1) -lt 1e-5) "the coverage line gives the table's value at the map's median"
Check ([Math]::Abs(($line[0] + $line[1] * $tTop) - (0.1 + [single](Value "CoverageSlope"))) -lt 1e-5) "and CoverageSlope more at its top tenth"

""
"=== CloudStyle: the heaps stop at HeapCap, the layer's thickness follows the map ==="
$cap = [single](Value "HeapCap")
$capOk = $true
foreach ($cv in 0.3, 0.6, 0.85, 0.9, 1.0) {
    $expect = [single](Call "EdgeCoverage" @([single][Math]::Min($cv, $cap)))
    if ([single](Call "HeapEdge" @([single]$cv)) -ne $expect) { $capOk = $false; "  HeapEdge($cv) wrong" }
}
Check $capOk "the heaps' coverage is the table's up to $($cap * 100)% and holds there (no more heaps: their repeat never fills the sky)"
$variety = [single](Value "LayerThicknessVariety")
$tMid = [single]0.5
$tTopTenth = [single](($topTenth + $range) / (2 * $range))
Check ([Math]::Abs([single](Call "LayerFactor" @($tMid)) - 1) -lt 1e-5) "at the map's median the layer is Layer thickness exactly"
Check ([Math]::Abs([single](Call "LayerFactor" @($tTopTenth)) - (1 + $variety * $topTenth)) -lt 1e-4) "at its top tenth, (1 + $variety x 1.28) of it: thick where the map is high"
$thin = [single](Value "LayerThinnest"); $thick = [single](Value "LayerThickest")
Check (([single](Call "LayerFactor" @([single]0)) -eq $thin) -and ([single](Call "LayerFactor" @([single]1)) -eq $thick)) "never thinner than $thin x or thicker than $thick x"
$cachedLine = [single[]](Value "LayerLineCached")
$freshLine = [single[]](Call "LayerLine" @())
Check (($cachedLine[0] -eq $freshLine[0]) -and ($cachedLine[1] -eq $freshLine[1])) "the cached layer line the shader parameters use is LayerLine's"

""
"=== CloudStyle: how much cloud (the same amount as Classic) ==="
$table = [single[]](Value "EdgeTable")
Check ($table.Length -eq 21) "one entry per 5% of the Intensity slider, 0% to 100% (got $($table.Length))"
$monotone = $true
for ($i = 1; $i -lt $table.Length; $i++) { if ($table[$i] -lt $table[$i - 1]) { $monotone = $false } }
Check $monotone "more intensity, never less cloud: the table never falls"
Check (($table[0] -ge -0.5) -and ($table[$table.Length - 1] -le $cmax)) "every entry is within what the shader's clamp can use"
$mid = [single](Call "EdgeCoverage" @([single]0.525))
$expect = ($table[10] + $table[11]) / 2
Check ([Math]::Abs($mid - $expect) -lt 1e-5) "between entries it is linear (52.5% = halfway from 50% to 55%)"
Check ([single](Call "EdgeCoverage" @([single]-1.0)) -eq $table[0] -and [single](Call "EdgeCoverage" @([single]2.0)) -eq $table[20]) "outside 0..100% it holds the ends"

""
if ($failed -eq 0) { "all cumulus checks pass" } else { "$failed cumulus check(s) FAILED"; exit 1 }
