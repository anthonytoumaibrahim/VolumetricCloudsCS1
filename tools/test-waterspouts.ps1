# Offline checks of the waterspouts' pure parts (Sky/FunnelShape, WaterspoutRules, GroundKind), run
# against the BUILT mod DLL -- no game needed.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-waterspouts.ps1
#
# What it guards (the author, 2026-10-18): waterspouts are "super small (they don't go over a certain
# intensity, or turn to a tornado)", rarely touch down, at real life's rarity at 100%; the funnel's
# shape is what FunnelShape.cginc draws (radius, tip, collar, the bound the march trusts, the density).

param(
    [string]$Dll = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds\VolumetricClouds.dll"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
public static class SpoutTestResolver
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
[SpoutTestResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[SpoutTestResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$shape = $asm.GetType("VolumetricClouds.Sky.FunnelShape")
$rules = $asm.GetType("VolumetricClouds.Sky.WaterspoutRules")
$ground = $asm.GetType("VolumetricClouds.Sky.GroundLook")
$funnelType = $asm.GetType("VolumetricClouds.Sky.Funnel")
$kindType = $asm.GetType("VolumetricClouds.Sky.GroundKind")

$failed = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { "  ok   $what" } else { "FAIL $what"; $script:failed++ }
}
function Near([double]$a, [double]$b, [double]$tol) { return [Math]::Abs($a - $b) -le $tol }
function Kind([string]$name) { return [Enum]::Parse($kindType, $name) }
function V3([double]$x, [double]$y, [double]$z) { return [UnityEngine.Vector3]::new([float]$x, [float]$y, [float]$z) }

$tip = [double]$shape.GetField("TipLength").GetValue($null)

"=== WaterspoutRules: how often ==="
$events = $rules.GetMethod("EventsPerSecond")
$perHour = [double]$events.Invoke($null, @([float]1, [float]1, [float]1)) * 3600
Check (Near $perHour 1.0 1e-4) "100% in full showery weather: one event an hour of simulation ($perHour)"
Check ([double]$events.Invoke($null, @([float]0, [float]1, [float]1)) -eq 0) "0%: none"
Check (Near ([double]$events.Invoke($null, @([float]4, [float]1, [float]1)) * 3600) 4.0 1e-3) "400%: four an hour"
Check ([double]$events.Invoke($null, @([float]1, [float]0.3, [float]1)) -eq 0) "a sky under 45% cover: none"
$dry = [double]$events.Invoke($null, @([float]1, [float]1, [float]0)) * 3600
Check (Near $dry 0.35 1e-3) "a full sky without rain: a third as often ($dry)"

"=== WaterspoutRules: small, rarely down, 1-5 minutes ==="
$make = $rules.GetMethod("Make")
$random = [System.Random]::new(7)
$maxAcross = 0.0; $minLife = 1e9; $maxLife = 0.0; $maxSkirt = 0.0; $rings = 0; $downs = 0; $threads = 0
for ($k = 0; $k -lt 4000; $k++) {
    $s = $make.Invoke($null, @($random, ($k % 2 -eq 0)))
    $maxAcross = [Math]::Max($maxAcross, 2 * $s.GroundRadius)
    $minLife = [Math]::Min($minLife, $s.Life); $maxLife = [Math]::Max($maxLife, $s.Life)
    $maxSkirt = [Math]::Max($maxSkirt, 2 * $s.SkirtRadius)
    if ($s.RingAloft -gt 0) { $rings++ }
    if ($s.TouchesDown) { $downs++ }
    if ($s.Thread -gt 0) { $threads++ }
}
Check ($maxAcross -le 30.0001) "never more than 30 m across at the bottom (most $([Math]::Round($maxAcross, 1)) m)"
Check ($minLife -ge 60 -and $maxLife -le 300.001) "1 to 5 minutes ($([Math]::Round($minLife)) .. $([Math]::Round($maxLife)) s)"
Check ($maxSkirt -le 140) "the spray ring stays small (widest $([Math]::Round($maxSkirt)) m)"
Check ($downs -gt 700 -and $downs -lt 900) "about one in five reaches the surface ($downs of 4000)"
Check ($threads -gt 1080 -and $threads -lt 1320) "about three in ten stop part way with a thread down ($threads of 4000)"
Check ($rings -gt $threads) "the threads have a spray ring, and now and then an aloft one ($rings of 4000)"

"=== WaterspoutRules: a life ==="
$stage = $rules.GetMethod("Stage")
function StageAt($s, [double]$age) {
    $s.Age = [float]$age
    $a = [object[]]@($s, [float]0, [float]0, [float]0, [float]0, [float]0, [float]0)
    [void]$stage.Invoke($null, $a)
    return @{ Opacity = [double]$a[1]; Bottom = [double]$a[2]; Thin = [double]$a[3]; Bow = [double]$a[4]; Skirt = [double]$a[5]; Thread = [double]$a[6] }
}
function Spout([int]$seed, [string]$end) {
    $s = $make.Invoke($null, @(([System.Random]::new($seed)), $true))
    $s.TouchesDown = ($end -eq "down"); $s.Thread = 0; $s.RingAloft = 0; $s.AloftBottom = 0.5
    if ($end -eq "thread") { $s.Thread = 0.8; $s.RingAloft = 0.6 }
    $s.Life = 200
    return $s
}
$down = Spout 3 "down"
$start = StageAt $down 0
Check ($start.Opacity -eq 0) "it fades in from nothing"
Check ($start.Bottom -gt 0.85) "its funnel starts just under the cloud base (bottom $([Math]::Round($start.Bottom, 2)))"
$mid = StageAt $down 100
Check (Near $mid.Bottom (-$tip) 1e-4) "one that touches down reaches the water in mid-life"
Check ($mid.Skirt -gt 0.9 -and $mid.Thread -eq 0) "with its spray ring, no thread ($([Math]::Round($mid.Skirt, 2)))"
Check ($mid.Thin -eq 1) "full width in mid-life"
$late = StageAt $down 195
Check ($late.Thin -lt 0.6 -and $late.Bow -gt 3) "it ropes out at the end (width x$([Math]::Round($late.Thin, 2)), bow x$([Math]::Round($late.Bow, 1)))"
$end = StageAt $down 200
Check ($end.Opacity -eq 0 -and $end.Skirt -eq 0) "and is gone at the end of its life"
$early = StageAt $down 25
Check ($early.Skirt -gt 0 -and $early.Bottom -gt 0) "the spray ring shows before the funnel reaches the water (bottom $([Math]::Round($early.Bottom, 2)), ring $([Math]::Round($early.Skirt, 2)))"

$aloft = Spout 4 "aloft"
$m = StageAt $aloft 100
Check ((Near $m.Bottom 0.5 1e-4) -and $m.Thread -eq 0 -and $m.Skirt -eq 0) "an aloft one ends where it was given, nothing under it ($([Math]::Round($m.Bottom, 2)))"

$thread = Spout 6 "thread"
$t = StageAt $thread 100
Check ((Near $t.Bottom 0.5 1e-4) -and (Near $t.Thread 0.8 1e-3) -and $t.Skirt -gt 0.5) "one that stops part way: a thread down to its spray ring ($([Math]::Round($t.Thread, 2)), ring $([Math]::Round($t.Skirt, 2)))"
$t0 = StageAt $thread 5
Check ($t0.Thread -lt 0.05) "its thread comes once it is well down"

$ashore = Spout 5 "down"
$ashore.Life = 300
$ashore.LandfallAt = 100
$a1 = StageAt $ashore 110
$a2 = StageAt $ashore 120
Check ($a1.Opacity -gt 0 -and $a1.Opacity -lt (StageAt $ashore 100).Opacity) "come ashore, it fades"
Check ($a2.Opacity -eq 0 -and $a2.Skirt -eq 0) "and is gone 20 s later"

"=== WaterspoutRules: its funnel ==="
$shapeOf = $rules.GetMethod("Shape")
$ashore.LandfallAt = -1
$ashore.Ground = V3 100 40 200
$call = [object[]]@($ashore, [float]750, ([UnityEngine.Vector3]::new(1, 0, 0)), $null)
$ashore.Age = 150
Check ([bool]$shapeOf.Invoke($null, $call)) "a funnel under a 750 m base"
$f = $call[3]
Check (Near $f.Top.y 750 1e-3) "its top at the cloud base"
Check ($f.Top.x -gt $f.Ground.x) "leaning downwind"
$call2 = [object[]]@($ashore, [float]80, ([UnityEngine.Vector3]::new(1, 0, 0)), $null)
Check (-not [bool]$shapeOf.Invoke($null, $call2)) "none with under 60 m to the base"

"=== FunnelShape ==="
function MakeFunnel() {
    $f = [Activator]::CreateInstance($funnelType)
    $f.Ground = V3 0 50 0; $f.Top = V3 60 750 0
    $f.GroundRadius = 10; $f.TopRadius = 30; $f.CollarRadius = 80; $f.Exponent = 2.4
    $f.Bottom = -$tip; $f.Hollow = 0; $f.Bend = New-Object UnityEngine.Vector2 20, 10
    $f.Opacity = 1; $f.Density = 0.12
    $f.SkirtRadius = 50; $f.SkirtHeight = 25; $f.SkirtRing = 0.85; $f.SkirtStrength = 1
    return $f
}
$f = MakeFunnel
$radius = $shape.GetMethod("Radius")
Check (Near ([double]($radius.Invoke($null, @($f, [float]0)))) 10 1e-3) "touching down: full width at the ground"
Check (Near ([double]($radius.Invoke($null, @($f, [float]1)))) 110 1e-3) "the collar under the base: top + collar"
$f.Bottom = 0.5
Check ([double]$radius.Invoke($null, @($f, [float]0.45)) -eq 0) "aloft: nothing under its tip"
Check ([double]$radius.Invoke($null, @($f, [float](0.5 + $tip + 0.01))) -gt 0) "and its full width a tip's length above"
$f.Bottom = -$tip

# The bound the shader's march trusts: every part inside it, its slope never exceeded.
$bound = $shape.GetMethod("Bound").Invoke($null, @($f))
$axis = $shape.GetMethod("Axis"); $body = $shape.GetMethod("BodyRadius")
$inside = $true; $slopeOk = $true; $prev = $null
for ($k = 0; $k -le 400; $k++) {
    $s = [float]($k / 400.0)
    $c = $axis.Invoke($null, @($f, $s)); $r = [double]$body.Invoke($null, @($f, $s))
    $d = [Math]::Sqrt(([double]$c.x - $bound.x) * ([double]$c.x - $bound.x) + ([double]$c.y - $bound.y) * ([double]$c.y - $bound.y))
    if ($d + $r -gt $bound.z + 0.01) { $inside = $false }
    if ($prev -ne $null) {
        $dy = ($f.Top.y - $f.Ground.y) / 400.0
        $move = [Math]::Sqrt(([double]$c.x - $prev[0]) * ([double]$c.x - $prev[0]) + ([double]$c.y - $prev[1]) * ([double]$c.y - $prev[1])) + [Math]::Abs($r - $prev[2])
        if ($move / $dy -gt $bound.w * 1.15 + 1e-4) { $slopeOk = $false }
    }
    $prev = @([double]$c.x, [double]$c.y, $r)
}
Check $inside "the bound holds the whole funnel (radius $([Math]::Round($bound.z, 1)) m)"
Check $slopeOk "its slope bound holds between samples ($([Math]::Round($bound.w, 3)) m/m)"
Check ($bound.z -ge $f.SkirtRadius) "and the skirt"

# The density with the noise at its brightest: the wall's own shape.
$full = [Func[UnityEngine.Vector3, UnityEngine.Vector2]] { param($q) New-Object UnityEngine.Vector2 1, 1 }
$stem = $shape.GetMethod("StemDensity")
$centre = $axis.Invoke($null, @($f, [float]0.5))
Check (Near ([double]($stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($centre.x, 400, $centre.y)), $full, [float]0)))) 1 1e-4) "solid on the axis"
Check ([double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($centre.x + 500, 400, $centre.y)), $full, [float]0)) -eq 0) "nothing outside it"
$thin = [double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($centre.x, 400, $centre.y)), $full, [float]100))
$rMid = [double]$radius.Invoke($null, @($f, [float]0.5))
Check (Near $thin ($rMid / 80.0) 1e-3) "far away, a thin funnel is drawn a pixel wide and as much fainter ($([Math]::Round($thin, 3)))"
$f.Hollow = 0.5
Check (Near ([double]($stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($centre.x, 400, $centre.y)), $full, [float]0)))) 0.5 1e-4) "hollow 50%: half as dense in its middle where the noise is brightest"
$half = [Func[UnityEngine.Vector3, UnityEngine.Vector2]] { param($q) New-Object UnityEngine.Vector2 0.36, 0.36 }
Check ([double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($centre.x, 400, $centre.y)), $half, [float]0)) -lt 0.25) "hollow: see-through in its middle where the noise is middling"
$f.Hollow = 0
$skirt = $shape.GetMethod("SkirtDensity")
Check ([double]$skirt.Invoke($null, @($f, ([UnityEngine.Vector3]::new(0, 60, 0)), $full)) -gt 0) "the spray ring round its foot"
Check ([double]$skirt.Invoke($null, @($f, ([UnityEngine.Vector3]::new(0, 80, 0)), $full)) -eq 0) "and nothing above it"
$f.SkirtStrength = 0
Check ([double]$skirt.Invoke($null, @($f, ([UnityEngine.Vector3]::new(0, 60, 0)), $full)) -eq 0) "no skirt at strength 0"

# The thread under the tip of one that stops part way: faint and thin, on its own wavering axis.
$f.Bottom = 0.5; $f.Thread = 1
$sLow = [float]0.2
$a = $axis.Invoke($null, @($f, $sLow)); $wave = $shape.GetMethod("ThreadWave").Invoke($null, @($f, $sLow))
$yLow = 50 + 700 * 0.2
$onThread = [double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($a.x + $wave.x, $yLow, $a.y + $wave.y)), $full, [float]0))
Check ($onThread -gt 0.2 -and $onThread -le 0.8001) "a thread under its tip, fainter than the funnel ($([Math]::Round($onThread, 2)))"
Check ([double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($a.x + $wave.x + 8, $yLow, $a.y + $wave.y)), $full, [float]0)) -eq 0) "and thin (nothing 8 m off it)"
$f.Thread = 0
Check ([double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($a.x + $wave.x, $yLow, $a.y + $wave.y)), $full, [float]0)) -eq 0) "none without one"
$f.Bottom = -$tip

# Up into the cloud it hangs from, fading out over the overlap.
$f.Overlap = 100
$a1 = $axis.Invoke($null, @($f, [float]1))
$atBase = [double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($a1.x, 749, $a1.y)), $full, [float]0))
$halfUp = [double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($a1.x, 800, $a1.y)), $full, [float]0))
$past = [double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($a1.x, 851, $a1.y)), $full, [float]0))
$capTop = 2.5 / (0.12 * [double]$radius.Invoke($null, @($f, [float](699.0 / 700.0))))
Check (Near $atBase $capTop 1e-3) "110 m wide where it meets the cloud: no thicker across than 2.5, as thin per metre as that takes ($([Math]::Round($atBase, 3)), was a white goblet)"
Check ($halfUp -gt 0.75 * $atBase -and $halfUp -lt $atBase -and $past -eq 0) "it reaches into the cloud, held through the first part, gone at the end ($([Math]::Round($atBase, 3)) / $([Math]::Round($halfUp, 3)) / $past)"
$f.Hollow = 0.6
$hollowMid = [double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($centre.x, 400, $centre.y)), $full, [float]0))
$hollowTop = [double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($a1.x, 749, $a1.y)), $full, [float]0))
Check ($hollowMid -lt 0.5 -and (Near $hollowTop $atBase 1e-4)) "hollow down its middle, full where it meets the cloud ($([Math]::Round($hollowMid, 2)) / $([Math]::Round($hollowTop, 3)))"
$f.Hollow = 0

# The thread: one tube with the funnel, its density easing down from the tip (no step, no rope beside it).
$f.Bottom = 0.5; $f.Thread = 1
$prevD = $null; $smooth = $true
for ($k = 0; $k -le 60; $k++) {
    $sk = 0.35 + 0.25 * $k / 60.0
    $ak = $axis.Invoke($null, @($f, [float]$sk))
    $tipK = [Math]::Sqrt([Math]::Max(0, [Math]::Min(1, ($sk - 0.5) / $tip)))
    $wk = $shape.GetMethod("ThreadWave").Invoke($null, @($f, [float]$sk))
    $xk = $ak.x + $wk.x * (1 - $tipK); $zk = $ak.y + $wk.y * (1 - $tipK)
    $dk = [double]$stem.Invoke($null, @($f, ([UnityEngine.Vector3]::new($xk, 50 + 700 * $sk, $zk)), $full, [float]0))
    if ($prevD -ne $null -and [Math]::Abs($dk - $prevD) -gt 0.12) { $smooth = $false }
    $prevD = $dk
}
Check $smooth "from the thread into the funnel without a step along its axis"
$f.Bottom = -$tip; $f.Thread = 0

"=== GroundLook ==="
$classify = $ground.GetMethod("Classify")
function Ground([double]$water, [bool]$winter, [int[]]$v) { return $classify.Invoke($null, @([float]$water, $winter, $v[0], $v[1], $v[2], $v[3], $v[4], $v[5], $v[6], $v[7])).ToString() }
Check ((Ground 3 $true @(0,0,0,0,0,0,0,0)) -eq "Water") "deep water is water, winter or not"
Check ((Ground 0 $true @(0,0,255,0,0,0,0,0)) -eq "Snow") "a winter map's land is snow"
Check ((Ground 0 $false @(0,0,0,0,0,200,0,0)) -eq "City") "pavement: grit"
Check ((Ground 0 $false @(200,0,0,0,0,0,0,0)) -eq "Sand") "sand"
Check ((Ground 0 $false @(0,0,200,0,0,0,0,0)) -eq "Forest") "forest: leaves"
Check ((Ground 0 $false @(0,0,0,0,200,0,0,0)) -eq "Field") "fertile land: dust and chaff"
Check ((Ground 0 $false @(0,0,0,0,0,0,0,0)) -eq "Dust") "anything else: dust"

""
if ($failed -eq 0) { "ALL PASSED" } else { "$failed FAILED"; exit 1 }
