# Offline checks of the rainbow's colours (Sky/RainbowTable), run against the BUILT mod DLL --
# no game needed.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-rainbow.ps1
#
# What it guards: the bow the author signed off on the preview ("B is perfect!",
# docs/previews/render-rainbow.ps1): the Airy function the table is made from, the angles of both
# bows against textbook values, the colours in the right order (red outside the primary, inside the
# secondary), the dark band, the brighter inside, the texture's bytes, the same on every run, and
# quick enough for a city load's worker thread.

param(
    [string]$Dll = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds\VolumetricClouds.dll"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
public static class BowTestResolver
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
[BowTestResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[BowTestResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$bow = $asm.GetType("VolumetricClouds.Sky.RainbowTable")
$failed = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { "  ok   $what" } else { "FAIL $what"; $script:failed++ }
}
function Near([double]$a, [double]$b, [double]$tol) { return [Math]::Abs($a - $b) -le $tol }
function Call([string]$name, [object[]]$arguments) { return $bow.GetMethod($name).Invoke($null, $arguments) }

"=== RainbowTable: Airy's function ==="
# Reference values: Ai(0), the first peak, Ai(+-1), Ai(+-5) (Abramowitz & Stegun 10.4), and Ai(-10)
# from the power series summed in double (0.04024123; it is still exact there), where the mod uses
# the asymptotic form.
foreach ($case in @(@(0.0, 0.3550280539), @(-1.0188, 0.5357), @(1.0, 0.1352924163), @(-1.0, 0.5355608833),
                    @(-5.0, 0.3507610090), @(5.0, 0.0001083444), @(-10.0, 0.04024123))) {
    $v = [double](Call "Airy" @([double]$case[0]))
    Check (Near $v $case[1] ([Math]::Max(1e-3, [Math]::Abs($case[1]) * 2e-3))) ("Ai({0}) = {1:F6} (reference {2:F6})" -f $case[0], $v, $case[1])
}
# Where the series hands over to the asymptotic forms, the two sides must meet.
$lo = [double](Call "Airy" @([double]-4.9999)); $hi = [double](Call "Airy" @([double]-5.0001))
Check (Near $lo $hi 1e-3) ("continuous at x = -5 ({0:F5} / {1:F5})" -f $lo, $hi)

"=== the bows' angles (Descartes' rays, degrees from the point opposite the sun) ==="
# Textbook values for water: the primary 40.6 (violet) to 42.3-42.5 (red), the secondary 50-53.5.
foreach ($case in @(@(400, 40.6, 53.5), @(589, 42.1, 50.8), @(700, 42.5, 50.2))) {
    $n = [double](Call "WaterIndex" @([double]$case[0]))
    $b = 0.0; $p1 = 0.0; $c1 = 0.0; $p2 = 0.0; $c2 = 0.0
    $args1 = [object[]]@($n, 1, $b, $p1, $c1); [void]$bow.GetMethod("BowRay").Invoke($null, $args1)
    $args2 = [object[]]@($n, 2, $b, $p2, $c2); [void]$bow.GetMethod("BowRay").Invoke($null, $args2)
    $d1 = [double]$args1[3] * 180 / [Math]::PI; $d2 = [double]$args2[3] * 180 / [Math]::PI
    Check ((Near $d1 $case[1] 0.15) -and (Near $d2 $case[2] 0.15)) ("{0} nm (n {1:F4}): primary {2:F2}, secondary {3:F2} (expected {4} / {5})" -f $case[0], $n, $d1, $d2, $case[1], $case[2])
    Check (([double]$args1[4] -lt 0) -and ([double]$args2[4] -gt 0)) ("{0} nm: the primary is the rays' outermost angle, the secondary their innermost" -f $case[0])
}

"=== the table the mod draws (8 mm/h, 512 entries over 64 degrees) ==="
$radii = $null; $weights = $null
$rd = [object[]]@([double]8.0, 24, $radii, $weights); [void]$bow.GetMethod("RainDrops").Invoke($null, $rd)
$sw = [Diagnostics.Stopwatch]::StartNew()
$table = [single[]](Call "Build" @($rd[2], $rd[3], 512, $true))
$ms = $sw.ElapsedMilliseconds
"  " + (Call "Describe" @(, $table))
Check ($table.Length -eq 512 * 3) "512 entries of RGB"

function PeakAt([int]$c, [double]$from, [double]$to) {
    $a = [object[]]@($table, $c, [single]$from, [single]$to, [single]0, [single]0)
    [void]$bow.GetMethod("Peak").Invoke($null, $a)
    return @([double]$a[4], [double]$a[5])
}
$pr = PeakAt 0 30 47; $pg = PeakAt 1 30 47; $pb = PeakAt 2 30 47
Check (($pr[0] -gt $pg[0]) -and ($pg[0] -gt $pb[0])) ("the primary: red outside ({0:F1}), green ({1:F1}), blue inside ({2:F1})" -f $pr[0], $pg[0], $pb[0])
Check (($pr[0] -ge 41.5) -and ($pr[0] -le 42.6) -and ($pb[0] -ge 40.3) -and ($pb[0] -le 41.3)) "the primary between 40.3 and 42.6 degrees"
Check (($pg[1] -gt 0.7) -and ($pg[1] -lt 1.4)) ("its peak about as bright as isotropic scattering ({0:F2}; Airy theory ~1)" -f $pg[1])
$sr = PeakAt 0 48 60; $sb = PeakAt 2 48 60
Check ($sr[0] -lt $sb[0]) ("the secondary reversed: red inside ({0:F1}), blue outside ({1:F1})" -f $sr[0], $sb[0])
Check (($sr[1] -gt 0.05) -and ($sr[1] -lt 0.4 * $pr[1])) ("the secondary fainter ({0:F2} against {1:F2})" -f $sr[1], $pr[1])
$band = [double](Call "Least" @($table, 1, [single]44, [single]50))
$inside = [double](Call "Mean" @($table, 1, [single]25, [single]35))
Check ($band -lt 0.02) ("Alexander's dark band between them ({0:F3})" -f $band)
Check (($inside -gt 0.05) -and ($inside -lt 0.25) -and ($inside -gt 5 * $band)) ("the sky inside the bow brighter than the band ({0:F3})" -f $inside)
$last = $table[3 * 511 + 1]
Check ($last -lt 1e-3) ("nothing at the table's far end ({0:E1}): no edge where it stops" -f $last)
$neg = $false; foreach ($v in $table) { if ($v -lt 0) { $neg = $true } }
Check (-not $neg) "no negative light (colours out of gamut pulled in)"

"=== the texture's bytes (RainbowTable.Bytes) ==="
$sw.Restart()
$bytes = [byte[]]$bow.GetProperty("Bytes").GetValue($null, $null)
$made = $sw.ElapsedMilliseconds
$scale = [single]$bow.GetProperty("Scale").GetValue($null, $null)
Check ($bytes.Length -eq 512 * 4) "512 x 4 bytes (rows R, G, B and an empty fourth)"
$max = 0; foreach ($b in $bytes) { if ($b -gt $max) { $max = $b } }
Check ($max -eq 255) "the brightest entry is 255"
$empty = $true; for ($i = 512 * 3; $i -lt 512 * 4; $i++) { if ($bytes[$i] -ne 0) { $empty = $false } }
Check $empty "the fourth row is empty"
$scaleOut = [single]0
$encoded = [byte[]]$bow.GetMethod("Encode").Invoke($null, [object[]]@($table, $scaleOut))
$same = $encoded.Length -eq $bytes.Length
for ($i = 0; $same -and $i -lt $bytes.Length; $i++) { if ($encoded[$i] -ne $bytes[$i]) { $same = $false } }
Check $same "the same bytes on every run (the property is Build at 8 mm/h, encoded)"
$g = [int]$bytes[512 + 334]
Check (Near ($g / 255.0 * $scale) $table[3 * 334 + 1] ($scale / 255.0)) ("a byte reads back as its value (entry 334, {0:F2} deg: {1:F3})" -f ((334 + 0.5) * 64 / 512), ($g / 255.0 * $scale))
Check ($made -lt 3000) "quick enough for a city load's worker thread here ($made ms, <3 s; Mono in the game is slower)"

"=== the arch (Sky/RainbowArch: the bow a spot sees, standing round it) ==="
$arch = $asm.GetType("VolumetricClouds.Sky.RainbowArch")
$v3 = [UnityEngine.Vector3]
$bowAngle = [double]$arch.GetField("BowAngle").GetValue($null)
$spot = New-Object UnityEngine.Vector3 1000, 30, -2000
# The sun 25 degrees up in the south-west (towards the sun, a unit vector).
$el = 25 * [Math]::PI / 180; $az = 225 * [Math]::PI / 180
$toSun = New-Object UnityEngine.Vector3 ([single]([Math]::Cos($el) * [Math]::Cos($az))), ([single][Math]::Sin($el)), ([single]([Math]::Cos($el) * [Math]::Sin($az)))
$R = [single]2500
$worstAngle = 0.0; $worstDist = 0.0; $topY = -1e9; $topPhi = 0
for ($k = 0; $k -lt 64; $k++) {
    $phi = [single]($k * 2 * [Math]::PI / 64)
    $p = $arch.GetMethod("Point").Invoke($null, @($spot, $R, $toSun, $phi))
    $d = $v3::op_Subtraction($p, $spot)
    $dist = [double]$d.magnitude
    $cosA = -([double]$v3::Dot($d, $toSun)) / $dist
    # [double] on both: with an integer literal PowerShell picks Min(int, int) and rounds the cosine.
    $angle = [Math]::Acos([Math]::Max([double]-1, [Math]::Min([double]1, $cosA))) * 180 / [Math]::PI
    $worstAngle = [Math]::Max($worstAngle, [Math]::Abs($angle - $bowAngle))
    $worstDist = [Math]::Max($worstDist, [Math]::Abs($dist - $R))
    if ($p.y -gt $topY) { $topY = [double]$p.y; $topPhi = $k }
}
Check ($worstAngle -lt 0.01) ("every point {0} deg round the spot's antisolar axis (worst off by {1:E1})" -f $bowAngle, $worstAngle)
Check ($worstDist -lt 0.5) ("every point {0} m from the spot (worst off by {1:F2} m)" -f $R, $worstDist)
$topElevation = [Math]::Asin(($topY - $spot.y) / $R) * 180 / [Math]::PI
Check (($topPhi -eq 0) -and (Near $topElevation ($bowAngle - 25) 0.1)) ("phi 0 is its top, {0:F2} deg over the spot's horizon (the bow's angle less the sun's)" -f $topElevation)

# Always a semicircle on the ground ("too low to the ground"): the spot lifted so the circle's centre
# is on the ground, the radius putting the top where asked.
$centre = New-Object UnityEngine.Vector3 -3000, 40, 5000
foreach ($sunDeg in 10, 33, 54) {
    $e2 = $sunDeg * [Math]::PI / 180
    $sunV = New-Object UnityEngine.Vector3 ([single]([Math]::Cos($e2) * [Math]::Cos($az))), ([single][Math]::Sin($e2)), ([single]([Math]::Cos($e2) * [Math]::Sin($az)))
    $rad = [single]$arch.GetMethod("Radius").Invoke($null, @([single]950, [single]$sunDeg))
    $spot2 = $arch.GetMethod("Spot").Invoke($null, @($centre, $rad, $sunV))
    $top2 = $arch.GetMethod("Point").Invoke($null, @($spot2, $rad, $sunV, [single]0))
    $footL = $arch.GetMethod("Point").Invoke($null, @($spot2, $rad, $sunV, [single](-[Math]::PI / 2)))
    $footR = $arch.GetMethod("Point").Invoke($null, @($spot2, $rad, $sunV, [single]([Math]::PI / 2)))
    $width = [double]($v3::op_Subtraction($footR, $footL)).magnitude
    Check ((Near $footL.y $centre.y 0.5) -and (Near $footR.y $centre.y 0.5) -and (Near ($top2.y - $centre.y) 950 0.5)) ("sun {0} deg: feet on the ground, top 950 m up ({1:F0}), {2:F1} km wide, the spot {3:F0} m up" -f $sunDeg, ($top2.y - $centre.y), ($width / 1000), ($spot2.y - $centre.y))
}
Check ([double]$arch.GetMethod("Radius").Invoke($null, @([single]950, [single]65)) -eq 0) "no arch with the sun 65 deg up (it would lie nearly flat)"
Check ([double]$arch.GetMethod("Radius").Invoke($null, @([single]950, [single]-2)) -eq 0) "no arch with the sun down"
$depths = [single[]]$arch.GetField("Depths").GetValue($null)
$shaderDepths = 0..3 | ForEach-Object { 0.6 + $_ * (0.8 / 3.0) }
$same = $depths.Length -eq 4; for ($i = 0; $same -and $i -lt 4; $i++) { if (-not (Near $depths[$i] $shaderDepths[$i] 1e-5)) { $same = $false } }
Check $same ("the line-of-sight depths are the shader's 0.6 + k x 0.8 / 3: {0}" -f (($depths | ForEach-Object { $_.ToString("F3") }) -join ", "))

# The shader's crossings: a ray from the spot meets the shell once, R away; one from outside, twice.
$up = New-Object UnityEngine.Vector3 0, 1, 0
$c1 = $arch.GetMethod("Crossings").Invoke($null, @($spot, $up, $spot, $R))
Check (($c1.x -lt 0) -and (Near $c1.y $R 0.5)) ("from the spot straight up: behind at {0:F0}, ahead at {1:F0} (R {2})" -f $c1.x, $c1.y, $R)
$far = New-Object UnityEngine.Vector3 ($spot.x - 5000), $spot.y, $spot.z
$east = New-Object UnityEngine.Vector3 1, 0, 0
$c2 = $arch.GetMethod("Crossings").Invoke($null, @($far, $east, $spot, $R))
Check ((Near $c2.x (5000 - $R) 0.5) -and (Near $c2.y (5000 + $R) 0.5)) ("from 5 km out through the spot: in at {0:F0}, out at {1:F0}" -f $c2.x, $c2.y)
$miss = $arch.GetMethod("Crossings").Invoke($null, @($far, $up, $spot, $R))
Check ($miss.x -lt 0 -and $miss.y -lt 0) "a ray that passes by misses"

""
if ($failed -eq 0) { "ALL PASSED" } else { "$failed FAILED"; exit 1 }
