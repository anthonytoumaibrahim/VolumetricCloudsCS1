# Offline checks of the lightning PLACEMENT and the cloud BRIGHTNESS CURVE, run against the
# BUILT mod DLL -- no game needed. Same pattern and the same traps as test-lightning.ps1.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-placement.ps1
#
# Run it in a fresh process: loaded assemblies stay locked in an existing session.
#
# Both classes under test are pure managed maths (Vector3, Quaternion, Mathf), and neither
# has a static initialiser that calls into the engine -- which is exactly why the placement
# lives in its own class rather than inside CloudLightning.

param(
    [string]$Dll = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds\VolumetricClouds.dll"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
public static class PlacementResolver
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
[PlacementResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[PlacementResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed\ColossalManaged.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$flags = [System.Reflection.BindingFlags]"Static,NonPublic,Public"
$failed = 0

function Check([string]$what, [bool]$ok) {
    if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:failed++ }
}

$placement = $asm.GetType("VolumetricClouds.Sky.LightningPlacement")
$rate = $placement.GetMethod("StrikesPerSecond", $flags)
$gap = $placement.GetMethod("SecondsBetween", $flags)
$spot = $placement.GetMethod("Spot", $flags)

function Rate([double]$activity) { return [single]$rate.Invoke($null, [object[]]@([single]$activity)) }
function Gap([double]$activity) { return [single]$gap.Invoke($null, [object[]]@([single]$activity)) }

"=== LightningPlacement.StrikesPerSecond: the 0..400% slider ==="
foreach ($a in 0, 0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0) {
    $r = Rate $a
    $g = if ($r -gt 0) { (Gap $a).ToString("F2") + " s" } else { "never" }
    "  {0,4}% -> {1} per second, one every {2}" -f ($a * 100), $r.ToString("F3"), $g
}

Check "0% is silent" ((Rate 0) -eq 0)
# The old mapping was interval = Lerp(30, 3, activity); below 100% the meaning must not move,
# or every saved value would describe a different storm.
Check "0% .. 100% is still one every 30 s .. one every 3 s" (([math]::Abs((Gap 0.0001) - 30) -lt 0.01) -and ([math]::Abs((Gap 1) - 3) -lt 0.001))
Check "the two halves meet at 100% (no jump)" ([math]::Abs((Rate 0.9999) - (Rate 1.0)) -lt 0.001)
Check "200% is one every 1.5 s" ([math]::Abs((Gap 2) - 1.5) -lt 0.001)
Check "400% is one every 0.75 s" ([math]::Abs((Gap 4) - 0.75) -lt 0.001)
Check "it is clamped above 400%, so a hand-edited file cannot strobe" ((Rate 100) -eq (Rate 4))
Check "one clap per 1.2 s covers 400% (0.75 s apart, the nearest wins)" ((Gap 4) -lt 1.2)

""
"=== LightningPlacement.Spot: where the strikes go, from a city-builder camera ==="
# Quaternion.Euler is an engine ECall ("ECall methods must be packaged into a system module"
# out here), so the rotation is built from its components instead. Unity applies Euler angles
# in the order Z, X, Y, so with no roll q = qYaw * qPitch, which multiplies out to this.
# Quaternion's CONSTRUCTOR and its multiply operator are both plain managed code.
function Look([double]$pitch, [double]$yaw) {
    $sx = [math]::Sin($pitch * [math]::PI / 360); $cx = [math]::Cos($pitch * [math]::PI / 360)
    $sy = [math]::Sin($yaw * [math]::PI / 360);   $cy = [math]::Cos($yaw * [math]::PI / 360)
    return (New-Object UnityEngine.Quaternion ($cy * $sx), ($sy * $cx), (-$sy * $sx), ($cy * $cx)).psobject.BaseObject
}

function Ahead($rotation) {
    $f = $rotation * (New-Object UnityEngine.Vector3 0, 0, 1).psobject.BaseObject
    return (New-Object UnityEngine.Vector3 $f.x, 0, $f.z).psobject.BaseObject.normalized
}

# A camera 300 m up, looking 35 degrees down -- roughly how the game is played.
$camera = (New-Object UnityEngine.Vector3 0, 300, 0).psobject.BaseObject
$rotation = Look 35 20
$forward = ($rotation * (New-Object UnityEngine.Vector3 0, 0, 1).psobject.BaseObject)
$flatForward = (New-Object UnityEngine.Vector3 $forward.x, 0, $forward.z).psobject.BaseObject.normalized
$fov = 60.0
$aspect = 16.0 / 9.0

function Place([bool]$preferView, [double]$rx, [double]$ry, [double]$rd) {
    [object[]]$a = $camera, $rotation, [single]$fov, [single]$aspect, [single]0, $preferView,
                   [single]$rx, [single]$ry, [single]$rd, $false
    $result = $spot.Invoke($null, $a)
    return [pscustomobject]@{ Spot = $result; InView = [bool]$a[9] }
}

$random = New-Object System.Random 7
$inViewAngles = @(); $inViewDistances = @(); $allRoundAngles = @(); $inViewFlag = 0
foreach ($i in 1..4000) {
    $preferView = $random.NextDouble() -lt 0.7
    $p = Place $preferView $random.NextDouble() $random.NextDouble() $random.NextDouble()
    $to = ($p.Spot - $camera)
    $flat = (New-Object UnityEngine.Vector3 $to.x, 0, $to.z).psobject.BaseObject
    $angle = [UnityEngine.Vector3]::Angle($flatForward, $flat.normalized)
    if ($p.InView) { $inViewFlag++; $inViewAngles += $angle; $inViewDistances += $flat.magnitude }
    else { $allRoundAngles += $angle }
}

$share = 100.0 * $inViewFlag / 4000
# Horizontal half-angle of the frustum: atan(tan(fov/2) * aspect).
$halfWidth = [math]::Atan([math]::Tan($fov / 2 * [math]::PI / 180) * $aspect) * 180 / [math]::PI
"  aimed through the viewport: {0}% of 4000 (asked for 70%)" -f $share.ToString("F1")
"  horizontal half-angle of a {0} deg / {1} aspect frustum: {2} deg" -f $fov, $aspect.ToString("F2"), $halfWidth.ToString("F1")
"  in-view strikes: {0}..{1} deg off the heading, {2}..{3} m away" -f `
    (($inViewAngles | Measure-Object -Minimum).Minimum).ToString("F1"), `
    (($inViewAngles | Measure-Object -Maximum).Maximum).ToString("F1"), `
    (($inViewDistances | Measure-Object -Minimum).Minimum).ToString("F0"), `
    (($inViewDistances | Measure-Object -Maximum).Maximum).ToString("F0")
"  all-round strikes: {0}..{1} deg off the heading" -f `
    (($allRoundAngles | Measure-Object -Minimum).Minimum).ToString("F1"), `
    (($allRoundAngles | Measure-Object -Maximum).Maximum).ToString("F1")

$meanAngle = ($inViewAngles | Measure-Object -Average).Average
$withinFrustum = 100.0 * (($inViewAngles | Where-Object { $_ -le $halfWidth }).Count) / $inViewAngles.Count
"  in-view strikes average {0} deg off the heading; {1}% are inside the horizontal half-angle" -f `
    $meanAngle.ToString("F1"), $withinFrustum.ToString("F0")

Check "about seven in ten are aimed through the viewport" (($share -gt 66) -and ($share -lt 74))
# A ray through a screen corner, projected onto the ground, leans further off the heading
# than the frustum's own half-angle does -- the more so the further the camera looks down.
# What has to hold is that they land in FRONT of the camera and mostly in frame.
Check "no in-view strike lands behind the camera" ((($inViewAngles | Measure-Object -Maximum).Maximum) -le 90)
Check "most of them are inside the frustum's horizontal half-angle" ($withinFrustum -ge 80)
Check "the rest still go all round (more than 120 deg off the heading happens)" ((($allRoundAngles | Measure-Object -Maximum).Maximum) -gt 120)
Check "nothing lands closer than 800 m" ((($inViewDistances | Measure-Object -Minimum).Minimum) -ge 799.9)
Check "nothing lands further than 7 km" ((($inViewDistances | Measure-Object -Maximum).Maximum) -le 7000.1)

# Before this change the direction was uniform over the compass, so the share of strikes
# within the frustum was 2 * halfWidth / 360.
$old = 2 * $halfWidth / 360 * 100
"  for comparison: a uniform compass direction put {0}% of strikes inside the same frustum" -f $old.ToString("F1")
Check "that is a real improvement over the uniform placement (more than double)" ($share -gt 2 * $old)

""
"=== A camera looking at the horizon (no ground hit) still aims ahead ==="
$level = Look -5 90
$flatLevel = Ahead $level
$worstAngle = 0.0; $minD = 1e9; $maxD = 0.0
foreach ($i in 1..500) {
    [object[]]$a = $camera, $level, [single]$fov, [single]$aspect, [single]0, $true,
                   [single]$random.NextDouble(), [single]$random.NextDouble(), [single]$random.NextDouble(), $false
    $result = $spot.Invoke($null, $a)
    $to = ($result - $camera)
    $flat = (New-Object UnityEngine.Vector3 $to.x, 0, $to.z).psobject.BaseObject
    $angle = [UnityEngine.Vector3]::Angle($flatLevel, $flat.normalized)
    if ($angle -gt $worstAngle) { $worstAngle = $angle }
    $d = $flat.magnitude
    if ($d -lt $minD) { $minD = $d }
    if ($d -gt $maxD) { $maxD = $d }
}
"  worst heading error {0} deg, distances {1}..{2} m" -f $worstAngle.ToString("F1"), $minD.ToString("F0"), $maxD.ToString("F0")
Check "a level camera keeps the viewport direction, so nothing lands behind it" ($worstAngle -le 90)
Check "...and takes a random distance inside the band" (($minD -ge 799.9) -and ($maxD -le 7000.1))

""
"=== Settings.BrightnessCurve: cloud brightness against cloud intensity ==="
$curve = $asm.GetType("VolumetricClouds.Settings").GetMethod("BrightnessCurve", $flags)
function Brightness([double]$clear, [double]$overcast, [double]$coverage) {
    return [single]$curve.Invoke($null, [object[]]@([single]$clear, [single]$overcast, [single]$coverage))
}

"  shipping ends 200% clear / 50% overcast:"
foreach ($c in 0, 0.2, 0.39, 0.5, 0.8, 0.91, 0.98, 1.0) {
    "    intensity {0,5}% -> brightness {1}%" -f ($c * 100).ToString("F0"), ((Brightness 2.0 0.5 $c) * 100).ToString("F0")
}

Check "clear sky gives the clear end exactly" ([math]::Abs((Brightness 2.0 0.5 0) - 2.0) -lt 1e-5)
Check "full overcast gives the overcast end exactly" ([math]::Abs((Brightness 2.0 0.5 1) - 0.5) -lt 1e-5)
Check "it is clamped outside 0..1 rather than running away" (((Brightness 2.0 0.5 -3) -eq 2.0) -and ((Brightness 2.0 0.5 9) -eq 0.5))
Check "it is monotone from one end to the other" ((Brightness 2.0 0.5 0.3) -gt (Brightness 2.0 0.5 0.7))
Check "equal ends are a flat line (auto then does nothing, as it should)" ([math]::Abs((Brightness 0.25 0.25 0.6) - 0.25) -lt 1e-6)

""
"=== LightningPlacement.ThunderVolume: a clap fades with the strike's distance ==="
$thunder = $asm.GetType("VolumetricClouds.Sky.LightningPlacement").GetMethod("ThunderVolume", $flags)
function Clap([double]$metres) { return [single]$thunder.Invoke($null, [object[]]@([single]$metres)) }

foreach ($d in 0, 500, 1000, 2000, 4000, 7000, 12000) {
    "    strike {0,6} m away -> volume {1}" -f $d, (Clap $d).ToString("F3")
}

Check "a test strike (in front of the camera) is not faded" ((Clap 0) -eq 1.0)
Check "full volume out to 1 km" (((Clap 1000) -eq 1.0) -and ((Clap 999) -eq 1.0))
Check "half volume at 4 km (1 / sqrt)" ([math]::Abs((Clap 4000) - 0.5) -lt 1e-6)
Check "still heard at the far edge of the band" ((Clap 7000) -gt 0.35)
Check "never below the floor" ((Clap 12000) -eq [single]0.3 -and (Clap 1e9) -eq [single]0.3)
Check "it only ever gets quieter with distance" ((Clap 1500) -gt (Clap 3000) -and (Clap 3000) -gt (Clap 6000))

""
if ($failed -eq 0) { "All checks passed." } else { "$failed check(s) FAILED."; exit 1 }
