# Offline check that "fog amount X%" really covers X% of the map, run against the BUILT mod DLL.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-fogcoverage.ps1
#
# Background (measured in-game): the GPU gamma-decodes the weather texture, so a
# shader testing "saturate((weather - threshold) / softness)" sees smaller numbers than the
# stored densities the clouds' threshold table was solved against. GetThresholdAsDrawn solves
# against the decoded values instead. This script generates a real field, and for several
# amounts measures the coverage each threshold actually gives under GPU rules.

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
public static class Coverage
{
    // What the fog shader computes per texel, averaged over the field.
    public static double Measure(float[] density, float threshold, float softness, bool decode)
    {
        double sum = 0;
        for (int i = 0; i < density.Length; i++)
        {
            double v = density[i];
            if (decode) v = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            double c = (v - threshold) / softness;
            sum += c < 0 ? 0 : c > 1 ? 1 : c;
        }
        return sum / density.Length;
    }
}
"@
[TestResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[TestResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$type = $asm.GetType("VolumetricClouds.Sky.CloudDensityField")
$any = [System.Reflection.BindingFlags]"Instance,Public,NonPublic"
$failed = 0

function Check([string]$what, [bool]$ok) {
    if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:failed++ }
}

$field = [Activator]::CreateInstance($type, [object[]]@(12345))
$density = [single[]]$type.GetField("_density", $any).GetValue($field)
$type.GetProperty("GpuReadsSrgb").GetSetMethod($true).Invoke($field, [object[]]@($true))   # as measured in-game
$softness = [single]0.1

"=== fog amount -> share of the map actually covered, under GPU (decoded) rules ==="
"  amount   clouds' table   as-drawn table"
$worstOld = 0.0; $worstNew = 0.0
foreach ($amount in 0.1, 0.2, 0.3, 0.5, 0.7, 0.9) {
    $old = [single]$type.GetMethod("GetThreshold").Invoke($field, [object[]]@([single]$amount))
    $new = [single]$type.GetMethod("GetThresholdAsDrawn").Invoke($field, [object[]]@([single]$amount, $softness))
    $coveredOld = [Coverage]::Measure($density, $old, $softness, $true)
    $coveredNew = [Coverage]::Measure($density, $new, $softness, $true)
    "  {0,5:P0}   {1,12:P1}   {2,13:P1}" -f $amount, $coveredOld, $coveredNew
    $worstOld = [math]::Max($worstOld, [math]::Abs($coveredOld - $amount))
    $worstNew = [math]::Max($worstNew, [math]::Abs($coveredNew - $amount))
}

Check "with the as-drawn table the covered share is within 2 points of the amount asked for (worst: $($worstNew.ToString('P1')))" ($worstNew -lt 0.02)
Check "with the clouds' table it was far off, which is what pushed the slider to 100% (worst: $($worstOld.ToString('P1')))" ($worstOld -gt 0.1)

$none = [single]$type.GetMethod("GetThresholdAsDrawn").Invoke($field, [object[]]@([single]0.0, $softness))
$all = [single]$type.GetMethod("GetThresholdAsDrawn").Invoke($field, [object[]]@([single]1.0, $softness))
Check "amount 0 covers nothing" ([Coverage]::Measure($density, $none, $softness, $true) -eq 0)
Check "amount 100% covers everything" ([Coverage]::Measure($density, $all, $softness, $true) -gt 0.999)

""
if ($failed -eq 0) { "ALL CHECKS PASSED" } else { "$failed CHECK(S) FAILED"; exit 1 }
