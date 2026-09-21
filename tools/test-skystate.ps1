# Offline checks of the sky a savegame keeps (Sky/SkyState.cs) and the drift maths that keep
# it exact for the life of a city (Sky/Drift.cs), run against the BUILT mod DLL, no game needed.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-skystate.ps1
#
# What is covered: a lookup shifted by Drift.Phase reads the same place as one shifted by the
# whole drift (they differ by whole repeats), for drifts far past where a float gives up; the
# fog's lead starts at the old 0.35 per metre and never passes 400 m; a record survives the trip
# to bytes and back exactly, is 61 bytes, and anything that is not a record is refused -- while
# a NEWER version's record is still read.
#
# Not covered, because it needs the game: SkySaveData (the save hooks, the threads) and the
# shaders. The log says what those did: "sky: savegame read", "restored", "written".

param(
    [string]$Dll = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds\VolumetricClouds.dll"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
public static class SkyTestResolver
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
public static class SkyTestHelp
{
    // What a shader does, in single precision: the old lookup (p - drift) / tile, and the new
    // one p / tile - phase. Returns how far apart they are, modulo whole repeats.
    public static double RepeatError(float p, double drift, float tile, float phase)
    {
        float oldUv = (p - (float)drift) / tile;
        float newUv = p / tile - phase;
        double d = (double)oldUv - newUv;
        return Math.Abs(d - Math.Round(d));
    }

    // The same, exactly: the drift in doubles, so only the phase's own rounding shows.
    public static double RepeatErrorExact(double p, double drift, double tile, float phase)
    {
        double d = (p - drift) / tile - (p / tile - phase);
        return Math.Abs(d - Math.Round(d));
    }
}
"@
[SkyTestResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[SkyTestResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$drift = $asm.GetType('VolumetricClouds.Sky.Drift', $true)
$stateType = $asm.GetType('VolumetricClouds.Sky.SkyState', $true)
$failed = 0

function Check([string]$name, $actual, $expected) {
    if ("$actual" -ceq "$expected") { Write-Host "ok    $name  ->  $actual" }
    else { Write-Host "FAIL  $name  ->  [$actual]  expected [$expected]"; $script:failed++ }
}

function Phase([double]$metres, [double]$period) { return $drift::Phase($metres, $period) }

# ---- Drift.Phase ------------------------------------------------------------------------------

Check "0 m is phase 0" (Phase 0 100) "0"
Check "150 m over 100 m is half a repeat" (Phase 150 100) "0.5"
Check "a negative drift wraps forwards (-30 over 100 -> 0.7)" ([math]::Round((Phase -30 100), 6)) "0.7"
Check "a whole number of repeats is phase 0, not 1" (Phase 300 100) "0"
Check "no period -> 0, not NaN" (Phase 5 0) "0"
Check "NaN drift -> 0" (Phase ([double]::NaN) 100) "0"
Check "infinite drift -> 0" (Phase ([double]::PositiveInfinity) 100) "0"

# Far past where a float gives up: 1e10 m is 10 million km; a float's step there is 1 km.
Check "1e10 + 37 m over 100 m is still exactly 0.37" ([math]::Round((Phase (1e10 + 37) 100), 6)) "0.37"

$rng = New-Object System.Random 20260921
$outOfRange = 0
for ($i = 0; $i -lt 20000; $i++) {
    $m = ($rng.NextDouble() - 0.5) * 2e9
    $t = 50 + $rng.NextDouble() * 20000
    $ph = Phase $m $t
    if ($ph -lt 0 -or $ph -ge 1) { $outOfRange++ }
}
Check "20000 random drifts: every phase in [0, 1)" $outOfRange "0"

# A lookup shifted by the phase reads the same place as one shifted by the whole drift. The
# tiles are the real ones: weather 13500, clouds' noise 2600 at 1.25x (and the detail at 6x
# that), fog 7000, billows 360, swirl 1300 at 0.55x, wisps 85 at 1.8x.
$lookups = @(
    @{ Name = 'weather map';  Tile = 13500; Rate = 1.0 },
    @{ Name = 'cloud noise';  Tile = 2600;  Rate = 1.25 },
    @{ Name = 'cloud detail'; Tile = 2600;  Rate = 7.5 },
    @{ Name = 'fog placement';Tile = 7000;  Rate = 1.0 },
    @{ Name = 'fog billows';  Tile = 360;   Rate = 1.0 },
    @{ Name = 'fog swirl';    Tile = 1300;  Rate = 0.55 },
    @{ Name = 'fog wisps';    Tile = 85;    Rate = 1.8 }
)
foreach ($l in $lookups) {
    $worst = 0.0
    for ($i = 0; $i -lt 2000; $i++) {
        $p = ($rng.NextDouble() - 0.5) * 40000
        $w = ($rng.NextDouble() - 0.5) * 200000        # small enough for the float version to be right
        $ph = Phase ($w * $l.Rate) $l.Tile
        $e = [SkyTestHelp]::RepeatError([float]$p, $w * $l.Rate, [float]$l.Tile, $ph)
        if ($e -gt $worst) { $worst = $e }
    }
    # 1e-3 of a repeat is 13 m of weather map, 0.09 m of wisp: float rounding, not a jump.
    Check "$($l.Name): phase and whole drift read the same place (within float rounding)" ($worst -lt 1e-3) "True"

    $worst = 0.0
    for ($i = 0; $i -lt 2000; $i++) {
        $p = ($rng.NextDouble() - 0.5) * 40000
        $w = $rng.NextDouble() * 1e9                   # 1 million km: centuries of play
        $ph = Phase ($w * $l.Rate) $l.Tile
        $e = [SkyTestHelp]::RepeatErrorExact($p, $w * $l.Rate, $l.Tile, $ph)
        if ($e -gt $worst) { $worst = $e }
    }
    Check "$($l.Name): still exact at 1e9 m of drift" ($worst -lt 1e-6) "True"
}

# ---- Drift.FogLead ----------------------------------------------------------------------------

function Lead([double]$x, [double]$z) {
    $args2 = [object[]]@($x, $z, [float]0, [float]0)
    [void]$drift.GetMethod('FogLead').Invoke($null, $args2)
    return @([double]$args2[2], [double]$args2[3])
}

$l = Lead 0 0
Check "no drift, no lead" "$($l[0]),$($l[1])" "0,0"

$l = Lead 10 0
Check "10 m of drift -> 3.5 m of lead (the old 0.35 per metre)" ([math]::Round($l[0], 3)) "3.5"

$l = Lead 0 -10
Check "...in the drift's direction (along -z)" "$([math]::Round($l[0], 3)),$([math]::Round($l[1], 3))" "0,-3.5"

$cycle = [double]$drift.GetField('FogLeadCycle').GetValue($null)
Check "the cycle is ~7.2 km of drift" ([math]::Round($cycle)) "7181"

$l = Lead ($cycle / 4) 0
Check "a quarter cycle in: 400 m ahead, the most it ever gets" ([math]::Round($l[0], 2)) "400"

$l = Lead ($cycle * 3 / 4) 0
Check "three quarters in: 400 m behind" ([math]::Round($l[0], 2)) "-400"

$worst = 0.0
for ($i = 0; $i -lt 20000; $i++) {
    $x = ($rng.NextDouble() - 0.5) * 2e8
    $z = ($rng.NextDouble() - 0.5) * 2e8
    $l = Lead $x $z
    $len = [math]::Sqrt($l[0] * $l[0] + $l[1] * $l[1])
    if ($len -gt $worst) { $worst = $len }
}
Check "20000 random drifts up to 1e8 m: the lead never passes 400 m" ($worst -le 400.001) "True"

# Continuous: a metre more drift moves the lead by at most 0.35 m (plus rounding).
$worst = 0.0
for ($i = 0; $i -lt 5000; $i++) {
    $x = $rng.NextDouble() * 1e7
    $a = Lead $x 0
    $b = Lead ($x + 1) 0
    $step = [math]::Abs($b[0] - $a[0])
    if ($step -gt $worst) { $worst = $step }
}
Check "a metre of drift moves the lead by no more than 0.35 m" ($worst -le 0.3501) "True"

# ---- SkyState: the record in the save ---------------------------------------------------------

function NewState {
    $s = [Activator]::CreateInstance($stateType)
    $s.FieldSeed = 12345
    $s.NoiseSeed = 99999
    $s.WindX = 123456789.123456789
    $s.WindZ = -987654.25
    $s.FogX = 5.5e9
    $s.FogZ = -0.000123
    $s.FogBoil = [float]0.4375
    $s.FogAmount = [float]0.62
    $s.RainFallX = [float]12.5
    $s.RainFallY = [float]-1234.25
    $s.RainFallZ = [float]0.75
    return $s
}

function Read([byte[]]$bytes) {
    $args2 = [object[]]@($bytes, $null, $null)
    $ok = $stateType.GetMethod('TryRead').Invoke($null, $args2)
    return @{ Ok = $ok; State = $args2[1]; Note = $args2[2] }
}

$s = NewState
$bytes = $s.ToBytes()
Check "a record is 61 bytes" $bytes.Length "61"
Check "...and starts with version 1" $bytes[0] "1"
Check "...then the field seed, little-endian" ([BitConverter]::ToInt32($bytes, 1)) "12345"

$r = Read $bytes
Check "it reads back" $r.Ok "True"
Check "...with no note" ($r.Note -eq $null) "True"
$t = $r.State
Check "seeds exact" "$($t.FieldSeed)/$($t.NoiseSeed)" "12345/99999"
Check "wind exact (doubles, all 17 digits)" "$($t.WindX.ToString('R'))|$($t.WindZ.ToString('R'))" "$($s.WindX.ToString('R'))|$($s.WindZ.ToString('R'))"
Check "fog exact" "$($t.FogX.ToString('R'))|$($t.FogZ.ToString('R'))|$($t.FogBoil)|$($t.FogAmount)" "$($s.FogX.ToString('R'))|$($s.FogZ.ToString('R'))|$($s.FogBoil)|$($s.FogAmount)"
Check "rain fall exact" "$($t.RainFallX)|$($t.RainFallY)|$($t.RainFallZ)" "12.5|-1234.25|0.75"

$r = Read $null
Check "no data is refused" "$($r.Ok) ($($r.Note))" "False (empty)"

$r = Read ([byte[]]@())
Check "empty data is refused" $r.Ok "False"

$zero = [byte[]]$bytes.Clone(); $zero[0] = 0
$r = Read $zero
Check "version 0 is refused" $r.Ok "False"

$short = [byte[]]$bytes[0..59]
$r = Read $short
Check "60 bytes (one short of version 1) is refused" $r.Ok "False"
Write-Host "      (the note: $($r.Note))"

# A NEWER mod's record: version 2 with more fields after ours. An older mod reads its sky.
$newer = New-Object byte[] 73
[Array]::Copy($bytes, $newer, 61)
$newer[0] = 2
for ($i = 61; $i -lt 73; $i++) { $newer[$i] = 0xAB }
$r = Read $newer
Check "a newer version's record is still read" $r.Ok "True"
Check "...with the same sky" "$($r.State.FieldSeed)/$($r.State.WindX.ToString('R'))" "12345/$($s.WindX.ToString('R'))"
Check "...and says so" ($r.Note -match 'newer') "True"

$nan = NewState
$nan.FogX = [double]::NaN
$r = Read ($nan.ToBytes())
Check "a drift that is not a number is refused" $r.Ok "False"

$inf = NewState
$inf.FogAmount = [float]::PositiveInfinity
$r = Read ($inf.ToBytes())
Check "an infinite fog amount is refused" $r.Ok "False"

$inf = NewState
$inf.RainFallY = [float]::NaN
$r = Read ($inf.ToBytes())
Check "a rain fall that is not a number is refused" $r.Ok "False"

Check "the log line" ((NewState).Describe()) "pattern seeds 12345/99999, wind drift (123456789, -987654) m, fog drift (5500000000, 0) m, swirl 0.438, fog amount 62%, rain fallen 1234 m"

Write-Host ""
if ($failed -eq 0) { Write-Host "all passed" } else { Write-Host "$failed FAILED"; exit 1 }
