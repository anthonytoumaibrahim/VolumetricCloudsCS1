# Offline checks of the Mixed cloud style (Sky/CloudMix), run against the BUILT mod DLL -- no game.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-mix.ps1
#
# What it guards: the march's slab holds every top; the layer's three covers keep their order (the
# rain is cut at the smallest, so it always falls from drawn cloud -- invariant 2; the empty-sky skip
# tests the layer at the largest) and at 100% are all Classic's own (the author: "100% should mean
# 100%"); the curves only ever rise with the intensity; the cells and the noise fit every weather
# repeat the slider allows; the hash -- part of what a city's seed reproduces (invariant 14) -- is in
# range, steady and even; the bake is the same every time, its map holds the type and a top under
# every profile, and the empty-sky cells it marks hold every point that can read a cloud (a missed
# one would be a hole in the sky); and the light's cheap looks are the carving's mean (1.5.0).

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
    public static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // Texels with a profile whose bilinear footprint (a texel each way) reaches an unmarked empty-sky
    // cell: the corners of that square name every cell it touches (a cell is wider than the square).
    public static int Missed(byte[] map, int size, byte[] presence, int m)
    {
        int missed = 0;
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                if (map[(j * size + i) * 4] == 0) continue;
                bool ok = true;
                for (int c = 0; c < 4 && ok; c++)
                {
                    double x = i + 0.5 + ((c & 1) == 0 ? -0.999 : 0.999), y = j + 0.5 + ((c & 2) == 0 ? -0.999 : 0.999);
                    int cx = Wrap((int)Math.Floor(x / size * m), m), cy = Wrap((int)Math.Floor(y / size * m), m);
                    if (presence[cy * m + cx] == 0) ok = false;
                }
                if (!ok) missed++;
            }
        return missed;
    }

    static int Wrap(int v, int n) { int r = v % n; return r < 0 ? r + n : r; }

    // The carving's mean over a noise volume at one profile (the shader's formula; the test checks it
    // is the DLL's twin, CloudMix.CarvedDensity, before trusting it).
    public static double Mean(byte[] noise, double profile, double cc, double ed, double hard, double mid, double con)
    {
        var hist = new long[256];
        foreach (byte b in noise) hist[b]++;
        double sum = 0;
        for (int i = 0; i < 256; i++)
        {
            if (hist[i] == 0) continue;
            double n = Math.Max(0, Math.Min(1, (i / 255.0 - mid) * con + 0.5));
            sum += Math.Max(0, Math.Min(1, (profile * cc - ed * (1 - n)) / (1 - hard))) * hist[i];
        }
        return sum / noise.Length;
    }
}
"@
[MixResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[MixResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$mix = $asm.GetType("VolumetricClouds.Sky.CloudMix")
$failed = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { "  ok   $what" } else { "FAIL $what"; $script:failed++ }
}
function Value([string]$name) { return $mix.GetField($name).GetValue($null) }
function Call([string]$name, [object[]]$arguments) {
    $m = @($mix.GetMethods() | Where-Object { $_.Name -eq $name -and $_.GetParameters().Count -eq $arguments.Count })[0]
    return $m.Invoke($null, $arguments)
}

"=== the bubbles ==="
$sideExtent = [double](Value "SatelliteDistance") + [double](Value "SatelliteDistanceSpan") + [double](Value "SatelliteRadius") + [double](Value "SatelliteRadiusSpan")
Check ([Math]::Abs([double](Value "Extent") - $sideExtent) -lt 1e-6) ("the bake reaches {0:F2} x a cloud's radius: as far as its side bubbles go" -f $sideExtent)
$sideHeight = [double](Value "SatelliteHeight") + [double](Value "SatelliteHeightSpan")
$tallest = [double](Call "Tallest" @([single]13500)); $cell = [double](Call "CellSize" @([single]13500))
$expected = [double](Value "RadiusMax") * $cell * ([double](Value "Aspect") + [double](Value "AspectVariety") / 2) * [Math]::Max(1.0, $sideHeight)
Check ([Math]::Abs($tallest - $expected) -lt 0.5) ("the tallest cloud ({0:F0} m) is the largest cloud's tallest bubble: the march's slab holds every top" -f $tallest)

"=== the layer's covers (CloudShaderParams.ApplyMix, CloudStyle.CloudTestCoverage) ==="
$ordered = $true; $risesL = $true; $risesM = $true; $risesC = $true; $risesS = $true; $underCover = $true
$prev = @(-1.0, -1.0, -1.0, -1.0)
for ($i = 0; $i -le 200; $i++) {
    $x = [single]($i / 200.0)
    $l = [double](Call "LayerCoverInLayerArea" @($x)); $m = [double](Call "LayerCover" @($x)); $c = [double](Call "LayerCoverInCumulusArea" @($x))
    $s = [double](Call "FieldShare" @($x))
    if (-not ($l -ge $m - 1e-6 -and $m -ge $c - 1e-6)) { $ordered = $false }
    if ($c -gt $x + 1e-6) { $underCover = $false }
    if ($l -lt $prev[0] - 1e-6) { $risesL = $false }; if ($m -lt $prev[1] - 1e-6) { $risesM = $false }
    if ($c -lt $prev[2] - 1e-6) { $risesC = $false }; if ($s -lt $prev[3] - 1e-6) { $risesS = $false }
    $prev = @($l, $m, $c, $s)
}
Check $ordered "in a layer area >= between >= in a separate-cloud area, at every intensity (the rain, cut at the last, is always under drawn layer)"
Check $underCover "the rain's cover is never more than the cover in effect"
Check ($risesL -and $risesM -and $risesC) "the three layer covers only rise with the intensity"
Check $risesS "the share of cells holding a cloud only rises with the intensity"
Check ([double](Call "FieldShare" @([single]0)) -eq 0) "no separate clouds at 0%"
$at1 = @((Call "LayerCoverInLayerArea" @([single]1)), (Call "LayerCover" @([single]1)), (Call "LayerCoverInCumulusArea" @([single]1)))
Check (($at1 | Where-Object { [Math]::Abs([double]$_ - 1.0) -gt 1e-6 }).Count -eq 0) "at 100% all three covers are 1: the layer is Classic's own 100% (no gaps from below)"
Check ([double](Call "TypeStrength" @([single]1)) -eq 0) "at 100% the type is gone: every stretch of sky the same"
Check ([double](Call "TypeStrength" @([single]0.9)) -eq 1) "up to 90% the type tips the balance fully"
Check ([double](Call "LayerCover" @([single]0.2)) -eq 0) "no layer at 20% (between)"
Check ([Math]::Abs([double](Call "LayerCover" @([single]0.9)) - 0.9) -lt 1e-6) "from 85% the layer is cut at the cover in effect, as Classic's"

"=== the cells over every weather repeat the slider allows (3-20 km) ==="
$cellsOk = $true; $worst = ""; $noiseOk = $true
foreach ($tile in 3000, 3500, 4500, 7000, 10000, 13500, 16750, 20000) {
    $n = [int](Call "Cells" @([single]$tile)); $size = [double](Call "CellSize" @([single]$tile)); $tall = [double](Call "Tallest" @([single]$tile))
    $noise = [double](Call "NoiseTile" @([single]$tile))
    if ($n -lt 1 -or $size -lt 600 -or $size -gt 1500 -or [Math]::Abs($n * $size - $tile) -gt 0.5 -or $tall -gt 1100) { $cellsOk = $false; $worst += " $tile" }
    if ($noise -lt 300 -or $noise -gt 800) { $noiseOk = $false }
    "  {0,6} m: {1,2} cells of {2,4:F0} m, tallest cloud {3,4:F0} m, noise every {4,3:F0} m" -f $tile, $n, $size, $tall, $noise
}
Check $cellsOk "a whole number of cells per repeat, 600-1500 m each, the tallest cloud under 1.1 km$worst"
Check $noiseOk "the clouds' noise repeats every 300-800 m: lobes at the clouds' own scale"

"=== the hash (CloudMix.Hash: where the bake puts the clouds) ==="
$sx = [single]0; $sz = [single]0
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
Check $inRange "0 <= hash < 1 over $count cells"
Check ([Math]::Abs($mean - 0.5) -lt 0.03) ("its mean is {0:F3} (0.5 for an even spread)" -f $mean)
Check ([Math]::Abs($var - 1.0 / 12.0) -lt 0.01) ("its variance is {0:F4} (1/12 = 0.0833 for an even spread)" -f $var)
Check ([Math]::Abs($corr) -lt 0.08) ("neighbouring cells are unrelated (correlation {0:F3})" -f $corr)
Check ([Math]::Abs($corrK) -lt 0.08) ("a cell's numbers are unrelated to each other (correlation {0:F3})" -f $corrK)
$again = [double]$hash.Invoke($null, [object[]]@([single]7, [single]11, [single]4, [single]517, [single]93))
$again2 = [double]$hash.Invoke($null, [object[]]@([single]7, [single]11, [single]4, [single]517, [single]93))
Check ($again -eq $again2) "the same cell gives the same number every time"

"=== the type pattern (CloudMix.TypeAt, the cloud map's alpha) ==="
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
$typeBytes = [byte[]](Call "BakeType" @([int]64, [single]517, [single]93))
Check ($typeBytes.Length -eq 64 * 64) "the type pattern bakes one byte per texel (the map's alpha)"

"=== the bake (CloudMix.Baker, the maps CloudMixMap uploads) ==="
$fieldType = $asm.GetType("VolumetricClouds.Sky.CloudDensityField")
$field = [Activator]::CreateInstance($fieldType, [object[]]@([int]12345))
$scores = [byte[]]$fieldType.GetMethod("ScoreBytes").Invoke($field, @())
$res = [int]$fieldType.GetField("Resolution").GetValue($null)
$emptyType = $asm.GetType("VolumetricClouds.Sky.EmptySky")
$cells = [int]($res / [int]$emptyType.GetField("CellTexels").GetValue($null))
$bakerType = $mix.GetNestedType("Baker")
$size = [int](Value "MapSize")
$args3 = [object[]]@([int]12345, [single]0, [single]0); [void]$mix.GetMethod("SeedOffsets").Invoke($null, $args3)
$sx = [single]$args3[1]; $sz = [single]$args3[2]
$type = [byte[]](Call "BakeType" @([int]$size, $sx, $sz))
$bake7 = @($bakerType.GetMethods() | Where-Object { $_.Name -eq "Bake" -and $_.GetParameters().Count -eq 7 })[0]
function Bake($baker, [double]$intensity) {
    [void]$bake7.Invoke($baker, [object[]]@($scores, $res, $type, [single]$intensity, [single]13500, $sx, $sz))
}
$a = [Activator]::CreateInstance($bakerType, [object[]]@($size, $cells)); $b = [Activator]::CreateInstance($bakerType, [object[]]@($size, $cells))
$sw = [Diagnostics.Stopwatch]::StartNew(); Bake $a 0.47; $ms = $sw.Elapsed.TotalMilliseconds
Bake $b 0.47
$same = [MixBytes]::Same([byte[]]$a.Map, [byte[]]$b.Map) -and [MixBytes]::Same([byte[]]$a.Presence, [byte[]]$b.Presence)
Check $same ("the same map every time for the same seed and intensity ({0:F0} ms here; {1} clouds over {2:F1}% of the map, reaching {3:F0}% of the empty-sky cells)" -f $ms, $a.Clouds, (100.0 * $a.Covered / ($size * $size)), (100.0 * $a.CellsReached / ($cells * $cells)))
Check ($ms -lt 500) "a bake is quick enough to redo as the weather moves"

Bake $b 0
Check ($b.Clouds -eq 0 -and $b.Covered -eq 0) "no clouds at 0%"
$prevClouds = -1; $prevCovered = -1; $rises = $true; $line = ""
foreach ($x in 0.15, 0.30, 0.47, 0.65, 0.80, 0.90) {
    Bake $b $x; $line += (" {0:P0}: {1}/{2:F1}%" -f $x, $b.Clouds, (100.0 * $b.Covered / ($size * $size)))
    if ($b.Clouds -lt $prevClouds -or $b.Covered -lt $prevCovered) { $rises = $false }
    $prevClouds = $b.Clouds; $prevCovered = $b.Covered }
"  clouds / share of the map:$line"
Check $rises "more clouds, over more of the map, as the intensity rises"

$missedAll = 0; $topsOk = $true; $typeKept = $true; $seam = $false
foreach ($x in 0.30, 0.65, 0.90) {
    Bake $b $x
    $map = [byte[]]$b.Map
    for ($k = 0; $k -lt $size * $size; $k++) {
        $o = $k * 4
        if ($map[$o + 3] -ne $type[$k]) { $typeKept = $false }
        if ($map[$o] -gt 0 -and $map[$o + 1] -eq 0) { $topsOk = $false } }
    $missedAll += [MixBytes]::Missed($map, $size, [byte[]]$b.Presence, $cells)
    if ($x -eq 0.65) {
        for ($j = 0; $j -lt $size -and -not $seam; $j++) {
            if ($map[($j * $size) * 4] -gt 0 -and $map[($j * $size + $size - 1) * 4] -gt 0) { $seam = $true } } }
}
Check $typeKept "the map's alpha is the type pattern, everywhere, whatever the intensity"
Check $topsOk "wherever a cloud has a profile it has a top"
Check ($missedAll -eq 0) "every point that can read a cloud from the map lies in a marked empty-sky cell (at 30, 65, 90%; missed: $missedAll)"
Check $seam "a cloud across the map's edge is drawn on both sides of it (the map repeats with the weather map)"

"=== the player's shape (1.5.0: Cumulus size / height / amount, Big clouds, Cloud shapes) ==="
$shapeType = $mix.GetNestedType("Shape")
function NewShape([double]$s, [double]$h, [double]$a, [double]$big, [double]$out) {
    return [Activator]::CreateInstance($shapeType, [object[]]@([single]$s, [single]$h, [single]$a, [single]$big, [single]$out)) }
$bake8 = @($bakerType.GetMethods() | Where-Object { $_.Name -eq "Bake" -and $_.GetParameters().Count -eq 8 })[0]
function BakeShaped($baker, [double]$intensity, $shape) {
    [void]$bake8.Invoke($baker, [object[]]@($scores, $res, $type, [single]$intensity, [single]13500, $sx, $sz, $shape))
}
$tallest2 = @($mix.GetMethods() | Where-Object { $_.Name -eq "Tallest" -and $_.GetParameters().Count -eq 2 })[0]
Bake $a 0.65
BakeShaped $b 0.65 (NewShape 1 1 1 0 0)
Check ([MixBytes]::Same([byte[]]$a.Map, [byte[]]$b.Map) -and [MixBytes]::Same([byte[]]$a.Presence, [byte[]]$b.Presence)) "100% size, height, amount and no variety bake exactly the clouds as first built"
BakeShaped $b 0.65 (NewShape 1 1 0 0 0)
Check ($b.Clouds -eq 0) "Cumulus amount 0%: no separate clouds"
$shapesOk = $true; $tallOk = $true; $line = ""
foreach ($sh in @(@(1.5, 1.5, 1.5, 1, 1), @(0.5, 0.5, 0.5, 0, 1), @(2, 2, 2, 1, 0), @(1, 1, 1, 0.5, 0.5))) {
    $shape = NewShape $sh[0] $sh[1] $sh[2] $sh[3] $sh[4]
    foreach ($x in 0.30, 0.65) {
        BakeShaped $b $x $shape
        $missed = [MixBytes]::Missed([byte[]]$b.Map, $size, [byte[]]$b.Presence, $cells)
        if ($missed -gt 0) { $shapesOk = $false }
        $worst = [double]$tallest2.Invoke($null, [object[]]@([single]13500, $shape))
        if ($b.Clouds -gt 0 -and ($b.TallestTop -le 0 -or $b.TallestTop -gt $worst + 1e-3)) { $tallOk = $false } }
    $line += (" [{0}]: tallest {1:F0} m of {2:F0}" -f ($sh -join ","), $b.TallestTop, $worst) }
"  at 65%:$line"
Check $shapesOk "with any shape (bigger, drawn out, five bubbles) every point that can read a cloud lies in a marked empty-sky cell"
Check $tallOk "the slab ends at the tallest cloud drawn, never above what the shape allows"
BakeShaped $b 0.65 (NewShape 1 1 1 0 0); $plainCovered = $b.Covered; $plainClouds = $b.Clouds
BakeShaped $b 0.65 (NewShape 1 1 1 1 0)
Check ($b.Clouds -eq $plainClouds -and $b.Covered -gt $plainCovered) "Big clouds makes some clouds bigger, never more of them"
BakeShaped $b 0.65 (NewShape 1 1 2 0 0)
Check ($b.Clouds -gt $plainClouds) "Cumulus amount 200%: more clouds than at 100%"

# The empty-sky map under Mixed: the field's weather bounds kept, the score's channels replaced by
# where the clouds reach, a coarse cell marked wherever one of its fine cells is.
$fieldEmpty = [byte[]]$emptyType.GetMethod("Build").Invoke($null, [object[]]@([byte[]](New-Object byte[] ($res * $res)), $scores, $res, $false))
$withP = [byte[]]$emptyType.GetMethod("WithPresence").Invoke($null, [object[]]@($fieldEmpty, [byte[]]$b.Presence, $cells))
$keptW = $true; $presOk = $true; $coarseOk = $true
for ($k = 0; $k -lt $cells * $cells; $k++) {
    if ($withP[$k * 4] -ne $fieldEmpty[$k * 4] -or $withP[$k * 4 + 1] -ne $fieldEmpty[$k * 4 + 1]) { $keptW = $false }
    if ($withP[$k * 4 + 2] -ne $b.Presence[$k]) { $presOk = $false }
    if ($withP[$k * 4 + 2] -gt $withP[$k * 4 + 3]) { $coarseOk = $false } }
Check ($keptW -and $presOk -and $coarseOk) "the style's empty-sky map: weather bounds kept, b = where the clouds reach, a >= b (its coarse cell)"

"=== the light's cheap looks (CloudMix.EnvelopeDensity: the carving's mean, no noise read) ==="
$twin = $true
foreach ($pn in @(@(0.3, 0.6), @(0.8, 0.75), @(1.0, 0.5), @(0.5, 0.95))) {
    $n01 = [Math]::Max([double]0, [Math]::Min([double]1, ($pn[1] - [double](Value "NoiseMiddle")) * [double](Value "NoiseContrast") + 0.5))
    $want = [Math]::Max([double]0, [Math]::Min([double]1, ($pn[0] * [double](Value "CoreCover") - [double](Value "ErosionDepth") * (1 - $n01)) / (1 - [double](Value "Hardness"))))
    if ([Math]::Abs([double](Call "CarvedDensity" @([single]$pn[0], [single]$pn[1])) - $want) -gt 1e-4) { $twin = $false } }
Check $twin "CloudMix.CarvedDensity is the shader's carving"
$gen = $asm.GetType("VolumetricClouds.Sky.CumulusNoise3D")
$levels = [byte[][]]$gen.GetMethod("Generate").Invoke($null, [object[]]@([int]$gen.GetField("Size").GetValue($null), [int]$gen.GetMethod("SeedFor").Invoke($null, [object[]]@(67890)), 0))
$worstFit = 0.0; $fitLine = ""
foreach ($lv in 0, 2) {
    for ($s = 0; $s -le 20; $s++) {
        $pr = $s / 20.0
        $mean = [MixBytes]::Mean($levels[$lv], $pr, [double](Value "CoreCover"), [double](Value "ErosionDepth"), [double](Value "Hardness"), [double](Value "NoiseMiddle"), [double](Value "NoiseContrast"))
        $fit = [double](Call "EnvelopeDensity" @([single]$pr))
        $worstFit = [Math]::Max($worstFit, [Math]::Abs($fit - $mean))
        if ($lv -eq 0 -and $s % 5 -eq 0) { $fitLine += (" {0:F2}: {1:F3}/{2:F3}" -f $pr, $mean, $fit) } } }
"  profile: mean/fit$fitLine"
Check ($worstFit -lt 0.02) ("the fit is within {0:F3} of the carving's mean on the Cumulus noise (levels 0 and 2)" -f $worstFit)

"=== the log ==="
$shape = [string](Call "DescribeShape" @([single]13500))
"  " + $shape
Check ($shape -match "14 x 14 cells") "the shape line names the cells"
"  " + [string](Call "DescribeMix" @([single]0.47))

""
if ($failed -eq 0) { "all mix checks pass" } else { "$failed mix check(s) FAILED"; exit 1 }
