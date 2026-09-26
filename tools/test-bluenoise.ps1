# Offline checks of the raymarches' jitter tile (Sky/BlueNoise), run against the BUILT mod DLL --
# no game needed.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-bluenoise.ps1
#
# What it guards: the tile is what turned the Cumulus heaps' "small dots everywhere" into an even
# grain, so it must really be BLUE -- every value equally often, and no clumps: any 4x4 block's
# mean close to the middle, far closer than white noise gets -- and it must tile (the shader
# repeats it across the screen), be the same on every run, and be quick for a city load's worker.

param(
    [string]$Dll = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds\VolumetricClouds.dll"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
public static class BlueTestResolver
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

public static class BlueStats
{
    // The variance of the means of every k x k block (wrapping round the tile): white noise's is
    // the values' variance / k^2; blue noise's is far lower, because no block gathers a clump.
    public static double BlockMeanVariance(byte[] v, int n, int k)
    {
        double sum = 0, sum2 = 0; int count = 0;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double s = 0;
                for (int dy = 0; dy < k; dy++)
                    for (int dx = 0; dx < k; dx++)
                        s += v[((y + dy) % n) * n + (x + dx) % n];
                s /= k * k;
                sum += s; sum2 += s * s; count++;
            }
        double mean = sum / count;
        return sum2 / count - mean * mean;
    }

    public static double Variance(byte[] v)
    {
        double sum = 0, sum2 = 0;
        foreach (byte b in v) { sum += b; sum2 += (double)b * b; }
        double mean = sum / v.Length;
        return sum2 / v.Length - mean * mean;
    }

    // Mean |v(x) - v(x+1)| down column x (x = n - 1 is across the wrap).
    public static double ColumnStep(byte[] v, int n, int x)
    {
        double s = 0; int x2 = (x + 1) % n;
        for (int y = 0; y < n; y++) s += Math.Abs(v[y * n + x] - v[y * n + x2]);
        return s / n;
    }

    public static byte[] White(int n, int seed)
    {
        Random r = new Random(seed); byte[] b = new byte[n * n];
        for (int i = 0; i < b.Length; i++) b[i] = (byte)r.Next(256);
        return b;
    }
}
"@
[BlueTestResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[BlueTestResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$blue = $asm.GetType("VolumetricClouds.Sky.BlueNoise")
$failed = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { "  ok   $what" } else { "FAIL $what"; $script:failed++ }
}

"=== BlueNoise: the raymarches' jitter tile ==="
$n = [int]$blue.GetField("Size").GetValue($null)
$sw = [Diagnostics.Stopwatch]::StartNew()
$tile = [byte[]]$blue.GetProperty("Bytes").GetValue($null, $null)
$ms = $sw.ElapsedMilliseconds
"  $n x $n in $ms ms"
Check ($tile.Length -eq $n * $n) "the tile is $n x $n bytes"

$again = [byte[]]$blue.GetMethod("Generate").Invoke($null, [object[]]@($n, 20260926))
$same = $true
for ($i = 0; $i -lt $tile.Length; $i++) { if ($tile[$i] -ne $again[$i]) { $same = $false; break } }
Check $same "the same bytes on every run (the property is Generate(Size, its seed))"

$hist = New-Object int[] 256
foreach ($b in $tile) { $hist[$b]++ }
$flat = $true
foreach ($h in $hist) { if ($h -ne ($n * $n / 256)) { $flat = $false } }
Check $flat "every value 0..255 exactly $($n * $n / 256) times (a rank each)"

# Blue noise gains more over white the larger the block (a void-and-cluster tile measured 0.30,
# 0.18 and 0.07 of white noise's variance at 2, 4 and 8); white noise itself sits at 1.
$white = [BlueStats]::White($n, 7)
foreach ($k in 2, 4, 8) {
    $vb = [BlueStats]::BlockMeanVariance($tile, $n, $k)
    $vw = [BlueStats]::BlockMeanVariance($white, $n, $k)
    $expect = [BlueStats]::Variance($tile) / ($k * $k)
    "  ${k}x$k block means: variance $($vb.ToString('F1')) = $(($vb / $expect).ToString('F2')) x white noise's expectation $($expect.ToString('F1')) (a white tile: $(($vw / $expect).ToString('F2')))"
    Check ($vb -lt $expect / $k) "no clumps at ${k}x${k}: under 1/$k of white noise's block variance"
}

$steps = @(0..($n - 1) | ForEach-Object { [BlueStats]::ColumnStep($tile, $n, $_) })
$inner = $steps[0..($n - 2)]
$mean = ($inner | Measure-Object -Average).Average
$min = ($inner | Measure-Object -Minimum).Minimum; $max = ($inner | Measure-Object -Maximum).Maximum
"  neighbours differ by $($mean.ToString('F1')) on average (columns $($min.ToString('F1'))..$($max.ToString('F1'))); across the wrap $($steps[$n - 1].ToString('F1')); white noise's expectation 85.3"
Check (($steps[$n - 1] -ge $min * 0.8) -and ($steps[$n - 1] -le $max * 1.2)) "it tiles: the wrap is like any other column"
Check ($mean -gt 85.3) "neighbours differ more than white noise's do (the error is pushed to fine grain)"

Check ($ms -lt 3000) "quick enough for a city load's worker thread here (<3 s; Mono in the game is slower)"

""
if ($failed -eq 0) { "all blue noise checks pass" } else { "$failed blue noise check(s) FAILED"; exit 1 }
