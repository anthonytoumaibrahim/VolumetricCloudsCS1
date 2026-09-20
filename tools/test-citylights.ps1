# Offline checks of the city-glow map, run against the BUILT mod DLL -- no game needed.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-citylights.ps1
#
# CityLightMap is pure arithmetic (buildings in, blurred 0..1 grid out), so synthetic cities
# can be fed to it and the answer inspected. See test-lightning.ps1 for why this needs a fresh
# process and a compiled assembly resolver.

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
"@
[TestResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[TestResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$type = $asm.GetType("VolumetricClouds.Sky.CityLightMap")
$res = [int]$type.GetField("Resolution").GetRawConstantValue()
$mapSize = [single]$type.GetField("MapSize").GetRawConstantValue()
$cell = $mapSize / $res
$failed = 0

function Check([string]$what, [bool]$ok) {
    if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:failed++ }
}

# A district: square of side `size` centred on (cx, cz), one building every `pitch` metres.
function District($map, [single]$cx, [single]$cz, [single]$size, [single]$pitch, [single]$side, [single]$height) {
    $n = 0
    for ($x = $cx - $size / 2; $x -lt $cx + $size / 2; $x += $pitch) {
        for ($z = $cz - $size / 2; $z -lt $cz + $size / 2; $z += $pitch) {
            [void]$map.Add([single]$x, [single]$z, [single]($side * $side), $height); $n++
        }
    }
    return $n
}

function At($light, [single]$x, [single]$z) {
    return $light[[int]$type.GetMethod("IndexOf").Invoke($null, [object[]]@([single]$x, [single]$z))]
}

"=== an empty map ==="
$map = [Activator]::CreateInstance($type)
$map.Clear()
$light = $map.Finish()
Check "no buildings, no glow anywhere" ((($light | Measure-Object -Maximum).Maximum) -eq 0)

"=== one city: towers downtown, mid-rise around it, suburbs beyond, a farm far away ==="
$map.Clear()
$towers = District $map 3000 -2000 1200 60 40 120          # downtown core
$midrise = District $map 3000 -200 1500 48 32 30            # north of it
$houses = District $map -2500 2500 2000 24 16 8             # a separate suburb
[void]$map.Add(-6500, -6500, 30 * 30, 10)                   # a lone farm
$outside = $map.Add(9000, 0, 1000, 50)                      # beyond the map edge
$light = $map.Finish()

$downtown = At $light 3000 -2000
$mid = At $light 3000 -200
$suburb = At $light -2500 2500
$farm = At $light -6500 -6500
$wild = At $light -7000 6000
"  $towers towers, $midrise mid-rise blocks, $houses houses, 1 farm"
"  glow: downtown $($downtown.ToString('F2'))  mid-rise $($mid.ToString('F2'))  suburb $($suburb.ToString('F2'))  farm $($farm.ToString('F3'))  wilderness $($wild.ToString('F3'))"

Check "downtown glows strongly (>= 0.7)" ($downtown -ge 0.7)
Check "the three districts are told apart: downtown > mid-rise > suburb, each by a clear margin" (($downtown -gt $mid * 1.2) -and ($mid -gt $suburb * 1.15))
Check "a suburb still glows visibly (0.2..0.5), it is not dark" (($suburb -ge 0.2) -and ($suburb -le 0.5))
Check "a lone farm barely registers (< 0.03)" ($farm -lt 0.03)
Check "wilderness is dark" ($wild -eq 0)
Check "a building beyond the map edge is rejected" (-not $outside)
Check "every value is within 0..1" ((($light | Measure-Object -Minimum).Minimum -ge 0) -and (($light | Measure-Object -Maximum).Maximum -le 1))

"=== orientation: the glow lands where the buildings are ==="
$peak = 0; for ($i = 1; $i -lt $light.Length; $i++) { if ($light[$i] -gt $light[$peak]) { $peak = $i } }
$px = (($peak % $res) + 0.5) / $res * $mapSize - $mapSize / 2
$pz = ([math]::Floor($peak / $res) + 0.5) / $res * $mapSize - $mapSize / 2
"  brightest cell is at world ($($px.ToString('F0')), $($pz.ToString('F0'))); downtown was built at (3000, -2000)"
Check "x maps to columns and z to rows, as uv = xz / MapSize + 0.5 in the shader expects" (([math]::Abs($px - 3000) -lt 3 * $cell) -and ([math]::Abs($pz + 2000) -lt 3 * $cell))

"=== spread: light reaches a few hundred metres past the last building, not kilometres ==="
$edge = At $light (3000 + 600 + 300) -2000       # 300 m outside the downtown block
$far = At $light (3000 + 600 + 1500) -2000       # 1.5 km outside
"  300 m outside downtown $($edge.ToString('F2')), 1.5 km outside $($far.ToString('F3'))"
Check "glow spills past the edge of a district" ($edge -gt 0.05)
Check "and is gone 1.5 km out" ($far -lt 0.01)

"=== the map's border is dark (the texture clamps there) ==="
$map.Clear()
[void](District $map 8400 8400 400 40 40 120)     # a block right in the corner
$light = $map.Finish()
$border = 0.0
for ($i = 0; $i -lt $res; $i++) { $border = [math]::Max($border, [math]::Max([math]::Max($light[$i], $light[($res - 1) * $res + $i]), [math]::Max($light[$i * $res], $light[$i * $res + $res - 1]))) }
Check "outermost ring of cells is exactly 0 even with a block in the corner" ($border -eq 0)

""
if ($failed -eq 0) { "ALL CHECKS PASSED" } else { "$failed CHECK(S) FAILED"; exit 1 }
