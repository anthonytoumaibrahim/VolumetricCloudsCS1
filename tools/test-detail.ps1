# Offline checks of cloud detail's texture generator (Sky/CloudDetail3D) and its light gain
# (Sky/CloudDetail), run against the BUILT mod DLL -- no game needed.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-detail.ps1
#
# What it guards: the texture is part of what a city's seed reproduces (invariant 14), so it must
# be the same bytes whatever the thread count; it must tile without a seam (it repeats every few
# hundred metres); its mip levels must keep its mean (the shader's distance fade relies on it);
# and it must be quick enough for the worker thread a city load waits on.

param(
    [string]$Dll = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds\VolumetricClouds.dll"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
public static class TestResolver
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

public static class DetailStats
{
    // Mean |v(x) - v(x+1)| over each y-z slice, x = 0..n-1; the last one is across the wrap
    // (n-1 -> 0). One slice's mean is ruled by a few big cells, so slices differ by +-20% by
    // themselves: a seam is a wrap slice that stands out from all of them, not from their mean.
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
[TestResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[TestResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$gen = $asm.GetType("VolumetricClouds.Sky.CloudDetail3D")
$detail = $asm.GetType("VolumetricClouds.Sky.CloudDetail")
$failed = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { "  ok   $what" } else { "FAIL $what"; $script:failed++ }
}

$size = [int]$gen.GetField("Size").GetValue($null)
$seedFor = $gen.GetMethod("SeedFor")
$generate = $gen.GetMethod("Generate")
$seed = [int]$seedFor.Invoke($null, [object[]]@(67890))

"=== CloudDetail3D.Generate($size, seed, threads) ==="
$sw = [Diagnostics.Stopwatch]::StartNew()
$one = [byte[][]]$generate.Invoke($null, [object[]]@($size, $seed, 1))
$tOne = $sw.Elapsed.TotalMilliseconds
$sw.Restart()
$many = [byte[][]]$generate.Invoke($null, [object[]]@($size, $seed, 0))
$tMany = $sw.Elapsed.TotalMilliseconds
"  one thread: $([int]$tOne) ms, all cores ($([Environment]::ProcessorCount), capped at 8): $([int]$tMany) ms"

Check ([DetailStats]::Same($one, $many)) "the same bytes on one thread and on every core (a seed reproduces it)"

# Generator 1's bytes for this seed, recorded 2026-09-26 before the generator moved into
# Sky/WorleyVolume (shared with the Cumulus noise): a refactor must never change a city's billows.
$md5 = [System.Security.Cryptography.MD5]::Create()
$all = New-Object System.IO.MemoryStream
foreach ($level in $one) { $all.Write($level, 0, $level.Length) }
$hash = [BitConverter]::ToString($md5.ComputeHash($all.ToArray())).Replace("-", "")
Check ($hash -eq "066224B6E7A017DC41947D75D3E2BE26") "the same bytes generator 1 has always made for this seed (md5 $hash)"
Check ($one.Length -eq 8) "8 mip levels for 128^3 (got $($one.Length))"
$sizesOk = $true
for ($l = 0; $l -lt $one.Length; $l++) { $e = [int][Math]::Pow($size -shr $l, 3); if ($one[$l].Length -ne $e) { $sizesOk = $false } }
Check $sizesOk "each level is half the edge of the one above, down to 1^3"

$s0 = [DetailStats]::Stats($one[0])
"  level 0: mean $($s0[0].ToString('F1')), min $($s0[1]), max $($s0[2])"
Check (($s0[1] -eq 0) -and ($s0[2] -eq 255)) "level 0 spans the whole 0..255"
Check (($s0[0] -gt 100) -and ($s0[0] -lt 230)) "level 0's mean is puffy, not empty or solid (got $($s0[0].ToString('F1')))"

$meansOk = $true
for ($l = 1; $l -lt $one.Length; $l++) { $m = ([DetailStats]::Stats($one[$l]))[0]; if ([Math]::Abs($m - $s0[0]) -gt 1.5) { $meansOk = $false; "  level $l mean $($m.ToString('F1'))" } }
Check $meansOk "every mip level keeps the mean (box filter): distant detail fades to its average"

$profile = [DetailStats]::Profile($one[0], $size)
$inner = $profile[0..($size - 2)]
$m = ($inner | Measure-Object -Average).Average
$sd = [Math]::Sqrt((($inner | ForEach-Object { ($_ - $m) * ($_ - $m) }) | Measure-Object -Sum).Sum / $inner.Count)
$wrap = $profile[$size - 1]
$z = ($wrap - $m) / $sd
"  mean step between neighbours per slice: $($m.ToString('F2')) +- $($sd.ToString('F2')) (min $((($inner | Measure-Object -Minimum).Minimum).ToString('F2')), max $((($inner | Measure-Object -Maximum).Maximum).ToString('F2'))); across the wrap $($wrap.ToString('F2')) (z = $($z.ToString('F1')))"
Check ([Math]::Abs($z) -lt 3.0) "it tiles without a seam: the slice across the wrap is like any other slice"

$other = [byte[][]]$generate.Invoke($null, [object[]]@($size, [int]$seedFor.Invoke($null, [object[]]@(12345)), 0))
Check (-not [DetailStats]::Same($one, $other)) "another city's seed gives another volume"

Check ($tMany -lt 8000) "quick enough for a city load's worker thread here (<8 s; Mono in the game is slower)"

""
"=== CloudDetail.LightGain: a side-lit sunny face as bright as the light before ==="
$gain = [single]$detail.GetField("LightGain").GetValue($null)
function HG([double]$c, [double]$g) { return (1 - $g * $g) / [Math]::Pow(1 + $g * $g - 2 * $g * $c, 1.5) }
function Lobe([double]$c, [double]$k) { return 0.75 * [Math]::Min((HG $c (0.8 * $k)), 10.0) + 0.25 * (HG $c (-0.3 * $k)) }
$old = 0.65 + 0.35 * [Math]::Min((HG 0 0.55), 5.0)
$sum = (Lobe 0 1.0) + 0.5 * (Lobe 0 0.5) + 0.25 * (Lobe 0 0.25)
"  old phase at 90 degrees $($old.ToString('F4')), octave sum $($sum.ToString('F4')), gain $($gain.ToString('F4'))"
Check ([Math]::Abs($gain * $sum - $old) -lt 1e-4) "gain x octaves = the old light on a sunlit face seen side-on"

""
if ($failed -eq 0) { "all detail checks pass" } else { "$failed detail check(s) FAILED"; exit 1 }
