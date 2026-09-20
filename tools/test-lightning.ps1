# Offline checks of the lightning maths, run against the BUILT mod DLL -- no game needed.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-lightning.ps1
#
# Run it in a fresh process (as above): loaded assemblies stay locked in an existing session.
#
# What can be tested this way is any class with no Unity-NATIVE dependency: Vector3 and Mathf
# are plain managed code and work here, but anything touching Shader, Mesh, Material or a
# Singleton<> is an internal call into the engine and throws. That is why the flicker formula
# lives in LightningFlicker rather than in CloudLightning, whose static initialiser calls
# Shader.PropertyToID.
#
# Two PowerShell traps this script already stepped in:
#   - a script block used as the AssemblyResolve handler re-enters itself and overflows the
#     stack; the resolver below is compiled C#
#   - New-Object results are PSObject-wrapped and do not convert when passed through
#     MethodInfo.Invoke; unwrap them with .psobject.BaseObject

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
[void][System.Reflection.Assembly]::LoadFrom("$managed\ColossalManaged.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$flags = [System.Reflection.BindingFlags]"Static,NonPublic,Public"
$failed = 0

function Check([string]$what, [bool]$ok) {
    if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:failed++ }
}

"=== LightningFlicker.Intensity(frame, start, reduction) ==="
# Two overloads: (frame, start, reduction) is the game's formula, the 4-argument one takes a profile.
$intensity = $asm.GetType("VolumetricClouds.Sky.LightningFlicker").GetMethods($flags) |
    Where-Object { $_.Name -eq "Intensity" -and $_.GetParameters().Count -eq 3 }
$start = [uint32]5000

function Level([int]$age, [single]$reduction) {
    [object[]]$a = [uint32]($start + $age), $start, $reduction
    return [single]$intensity.Invoke($null, $a)
}

$envelope = 0..29 | ForEach-Object { Level $_ 1.0 }
$raw = 0..29 | ForEach-Object { Level $_ 0.0 }
"  envelope (reduction 1): " + (($envelope | ForEach-Object { $_.ToString("F2") }) -join " ")
"  raw      (reduction 0): " + (($raw | ForEach-Object { $_.ToString("F2") }) -join " ")

$peakAge = 0; for ($i = 1; $i -lt 30; $i++) { if ($envelope[$i] -gt $envelope[$peakAge]) { $peakAge = $i } }
Check "envelope starts at 0" ($envelope[0] -eq 0)
Check "envelope peaks at ~1.0 a third of the way to frame 20 (peak $($envelope[$peakAge].ToString('F3')) at age $peakAge)" (($peakAge -ge 6) -and ($peakAge -le 7) -and ($envelope[$peakAge] -gt 0.98) -and ($envelope[$peakAge] -le 1.0001))
Check "envelope is gone from frame 20" ((($envelope[20..29] | Measure-Object -Maximum).Maximum) -eq 0)
Check "raw flicker stays within 0..1" ((($raw | Measure-Object -Minimum).Minimum -ge 0) -and (($raw | Measure-Object -Maximum).Maximum -le 1))
Check "raw flicker is held at 0.75 or more for ages 2..4" ((($raw[2..4] | Measure-Object -Minimum).Minimum) -ge 0.75)
Check "raw flicker reseeds every 3 frames (5010, 5011, 5012 all divide to 1670 and share a level; 5013 does not have to)" (($raw[10] -eq $raw[11]) -and ($raw[11] -eq $raw[12]))
Check "half reduction is the midpoint of the two" ([math]::Abs((Level 10 0.5) - (($raw[10] + $envelope[10]) / 2)) -lt 1e-5)

""
"=== LightningFlicker.Profile.Random + Intensity(frame, start, reduction, profile): our own strikes ==="
$flickerType = $asm.GetType("VolumetricClouds.Sky.LightningFlicker")
$profileType = $flickerType.GetNestedType("Profile")
$makeRandom = $profileType.GetMethod("Random", $flags)
$makeGame = $profileType.GetMethod("ForGameStrike", $flags)
$varied = $flickerType.GetMethods($flags) | Where-Object { $_.Name -eq "Intensity" -and $_.GetParameters().Count -eq 4 }

function Field($profile, [string]$name) { return $profileType.GetField($name).GetValue($profile) }

$durations = @(); $strokeCounts = @(); $powers = @(); $reseeds = @(); $allInRange = $true; $allEnd = $true; $allPeak = $true; $allFadeOut = $true
foreach ($seed in 1..40) {
    [object[]]$a = (New-Object System.Random $seed).psobject.BaseObject, (($seed % 2) -eq 0)
    $profile = $makeRandom.Invoke($null, $a)
    $frames = [uint32](Field $profile "Frames"); $strokes = [int](Field $profile "Strokes")
    $durations += $frames; $strokeCounts += $strokes; $powers += [single](Field $profile "Power"); $reseeds += [uint32](Field $profile "Reseed")

    $s = [uint32](7000 + 97 * $seed)
    $values = 0..($frames + 3) | ForEach-Object { [object[]]$b = [uint32]($s + $_), $s, [single]0.0, $profile; [single]$varied.Invoke($null, $b) }
    $smooth = 0..($frames + 3) | ForEach-Object { [object[]]$b = [uint32]($s + $_), $s, [single]1.0, $profile; [single]$varied.Invoke($null, $b) }

    if ((($values | Measure-Object -Minimum).Minimum -lt 0) -or (($values | Measure-Object -Maximum).Maximum -gt 1)) { $allInRange = $false }
    if ((($values[$frames..($frames + 3)] | Measure-Object -Maximum).Maximum) -ne 0) { $allEnd = $false }
    if ((($smooth | Measure-Object -Maximum).Maximum) -lt 0.8) { $allPeak = $false }
    if ($smooth[$frames - 1] -gt 0.12) { $allFadeOut = $false }

    if ($seed -le 6) {
        $peaks = 0; for ($i = 1; $i -lt $smooth.Count - 1; $i++) { if (($smooth[$i] -gt $smooth[$i - 1]) -and ($smooth[$i] -ge $smooth[$i + 1]) -and ($smooth[$i] -gt 0.05)) { $peaks++ } }
        "  seed $seed : $(($frames / 60).ToString('F2')) s, $strokes stroke(s) -> $peaks peak(s) in the envelope, flicker every $(Field $profile 'Reseed') frames, power $(([single](Field $profile 'Power')).ToString('F2')), reach $(([single](Field $profile 'Reach')).ToString('F0')) m"
    }
}
"  over 40 seeds: duration $((($durations | Measure-Object -Minimum).Minimum / 60).ToString('F2'))..$((($durations | Measure-Object -Maximum).Maximum / 60).ToString('F2')) s; strokes $(($strokeCounts | Measure-Object -Minimum).Minimum)..$(($strokeCounts | Measure-Object -Maximum).Maximum); power $((($powers | Measure-Object -Minimum).Minimum).ToString('F2'))..$((($powers | Measure-Object -Maximum).Maximum).ToString('F2')); reseed $(($reseeds | Measure-Object -Minimum).Minimum)..$(($reseeds | Measure-Object -Maximum).Maximum)"
Check "durations actually vary, and stay within 0.23..1.6 s" (((($durations | Measure-Object -Minimum).Minimum) -ge 14) -and ((($durations | Measure-Object -Maximum).Maximum) -le 96) -and ((($durations | Sort-Object -Unique).Count) -ge 12))
Check "every intensity is within 0..1" $allInRange
Check "every strike is exactly 0 from its last frame on" $allEnd
Check "every strike's envelope reaches at least 0.8" $allPeak
Check "every strike has faded out by its last frame (no cut-off)" $allFadeOut

[object[]]$g = ,(New-Object System.Random 5).psobject.BaseObject
$gameProfile = $makeGame.Invoke($null, $g)
$sameAsGame = $true
foreach ($age in 0..29) { [object[]]$b = [uint32]($start + $age), $start, [single]0.3, $gameProfile; if ([single]$varied.Invoke($null, $b) -ne (Level $age 0.3)) { $sameAsGame = $false } }
Check "a GAME strike's profile still gives the game's formula, frame for frame (30 frames, exact)" ($sameAsGame -and ([uint32](Field $gameProfile "Frames") -eq 30) -and [bool](Field $gameProfile "Exact"))

""
"=== LightningBoltMesh.Channel: main stroke from the cloud (750 m) to the ground (60 m) ==="
$channel = $asm.GetType("VolumetricClouds.Sky.LightningBoltMesh").GetMethod("Channel", $flags)
$top = New-Object UnityEngine.Vector3 100, 750, -200
$ground = New-Object UnityEngine.Vector3 160, 60, -150
$length = ($ground - $top).magnitude
$axis = ($ground - $top).normalized

foreach ($seed in 1, 2, 3, 4) {
    [object[]]$a = $top.psobject.BaseObject, $ground.psobject.BaseObject, 6, [single]0.13, (New-Object System.Random $seed).psobject.BaseObject
    $pts = $channel.Invoke($null, $a)

    $path = 0.0; $sideways = 0.0; $up = 0; $shortest = 1e9; $longest = 0.0
    for ($i = 0; $i -lt $pts.Count; $i++) {
        $rel = $pts[$i] - $top
        $dev = ($rel - $axis * [UnityEngine.Vector3]::Dot($rel, $axis)).magnitude
        if ($dev -gt $sideways) { $sideways = $dev }
        if ($i -gt 0) {
            $seg = ($pts[$i] - $pts[$i - 1]).magnitude
            $path += $seg
            if ($seg -lt $shortest) { $shortest = $seg }
            if ($seg -gt $longest) { $longest = $seg }
            if ($pts[$i].y -gt $pts[$i - 1].y) { $up++ }
        }
    }

    "  seed $seed : $($pts.Count) points, straight $($length.ToString('F0')) m, path $($path.ToString('F0')) m (x$(($path / $length).ToString('F2'))), max sideways $($sideways.ToString('F0')) m, segments $($shortest.ToString('F1'))..$($longest.ToString('F1')) m, $up of 64 go upwards"
    Check "seed $seed has 2^6 + 1 points and exact ends" (($pts.Count -eq 65) -and (($pts[0] - $top).magnitude -lt 1e-3) -and (($pts[64] - $ground).magnitude -lt 1e-3))
    Check "seed $seed wanders, but like a bolt rather than a scribble (path 1.05x..1.8x, sideways under 35% of its length)" ((($path / $length) -gt 1.05) -and (($path / $length) -lt 1.8) -and ($sideways -lt 0.35 * $length))
    Check "seed $seed mostly heads down (under a quarter of its segments rise)" ($up -lt 16)
}

""
if ($failed -eq 0) { "ALL CHECKS PASSED" } else { "$failed CHECK(S) FAILED"; exit 1 }
