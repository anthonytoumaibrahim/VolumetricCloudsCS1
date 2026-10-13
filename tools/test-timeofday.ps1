# Offline checks of the time of day (1.5.0: the rows' clocks), run against the BUILT mod DLL, no game:
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-timeofday.ps1
#
# Sky.TimeOfDay: the Day / Sunset / Night shares from the sun's height add up to 1, and Night is the
# 1.3.0 night factor (Mathf.SmoothStep of Mathf.InverseLerp, 2 deg to -8 deg) BIT FOR BIT; the blend
# of a row's three values is its Day value exactly when the three are equal (every existing player),
# and exactly the old day/night blend when Sunset equals Day (a night carried over from a 1.3.0
# switch). Sky.CloudTint.Blend3: the same for colours against CloudTint.Blend. TimeOfDay.OlderNight:
# how an older settings file's night is read.

param(
    [string]$Dll = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds\VolumetricClouds.dll"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
public static class TimeTestResolver
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
[TimeTestResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[TimeTestResolver]::Install()

$unity = [System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$tod = $asm.GetType('VolumetricClouds.Sky.TimeOfDay', $true)
$tint = $asm.GetType('VolumetricClouds.Sky.CloudTint', $true)
$settings = $asm.GetType('VolumetricClouds.Settings', $true)
$mathf = $unity.GetType('UnityEngine.Mathf', $true)
$color32 = $unity.GetType('UnityEngine.Color32', $true)
$failed = 0

function Check([string]$name, $ok, $detail = '') {
    if ($ok) { Write-Host "ok    $name" }
    else { Write-Host "FAIL  $name  $detail"; $script:failed++ }
}

function Shares([single]$elevation) {
    $args = [object[]]@($elevation, [single]0, [single]0, [single]0)
    [void]$tod.GetMethod('Weights').Invoke($null, $args)
    return @([single]$args[1], [single]$args[2], [single]$args[3])
}

# The 1.3.0 night factor, as CloudVolume.NightFactor computed it with Unity's own Mathf.
function OldNight([single]$elevation) {
    $t = $mathf.GetMethod('InverseLerp').Invoke($null, [object[]]@([single]2, [single]-8, $elevation))
    return [single]$mathf.GetMethod('SmoothStep').Invoke($null, [object[]]@([single]0, [single]1, [single]$t))
}

$blend6 = $tod.GetMethods() | Where-Object { $_.Name -eq 'Blend' -and $_.GetParameters().Count -eq 6 }
function Blend([single]$d, [single]$s, [single]$n, $w) {
    return [single]$blend6.Invoke($null, [object[]]@($d, $s, $n, $w[0], $w[1], $w[2]))
}
$dayAndNight = $settings.GetMethod('DayAndNight')
function OldBlend([single]$d, [single]$n, [single]$share) {
    return [single]$dayAndNight.Invoke($null, [object[]]@($d, $n, $share))
}

"=== the shares through a day ==="
$elevations = @(60, 20, 15, 10, 5, 2, 1, 0, -2, -4, -6, -8, -12, -60)
foreach ($e in $elevations) {
    $w = Shares $e
    "    sun {0,4} deg: day {1,5:P0}  sunset {2,5:P0}  night {3,5:P0}" -f $e, $w[0], $w[1], $w[2]
}

$sumOk = $true; $nightOk = $true; $worst = ''
for ($e = -90.0; $e -le 90.0; $e += 0.25) {
    $w = Shares $e
    $sum = [double]$w[0] + $w[1] + $w[2]
    if ([math]::Abs($sum - 1) -gt 1e-6) { $sumOk = $false; $worst = "at $e deg: $sum" }
    if ($w[2] -ne (OldNight $e)) { $nightOk = $false; $worst = "at $e deg: $($w[2]) vs $(OldNight $e)" }
}
Check "the three add up to 1 at every height" $sumOk $worst
Check "Night is the 1.3.0 night factor bit for bit (every 0.25 deg)" $nightOk $worst
$high = Shares 30; $low = Shares 2; $dark = Shares -10
Check "high sun: all Day" (($high[0] -eq 1) -and ($high[1] -eq 0) -and ($high[2] -eq 0))
Check "2 deg: all Sunset" (($low[1] -eq 1) -and ($low[0] -eq 0) -and ($low[2] -eq 0))
Check "8 deg under: all Night" (($dark[2] -eq 1) -and ($dark[0] -eq 0) -and ($dark[1] -eq 0))

"=== a row's three values ==="
$odd = [single]0.37
$sameOk = $true; $pairOk = $true; $nightPairOk = $true
for ($e = -20.0; $e -le 30.0; $e += 0.5) {
    $w = Shares $e
    if ((Blend $odd $odd $odd $w) -ne $odd) { $sameOk = $false }
    # Sunset = Day: exactly the 1.3.0 day/night blend.
    if ((Blend 0.9 0.9 1.55 $w) -ne (OldBlend 0.9 1.55 $w[2])) { $pairOk = $false }
    # Night = Day: the same with Sunset.
    if ((Blend 0.9 0.2 0.9 $w) -ne (OldBlend 0.9 0.2 $w[1])) { $nightPairOk = $false }
}
Check "three equal: the Day value exactly, all day" $sameOk
Check "Sunset = Day: exactly the old day/night blend" $pairOk
Check "Night = Day: the Day value blended with Sunset's" $nightPairOk
$mid = Shares 6
$expected = [single](1.5 * $mid[0] + 0.25 * $mid[1] + 1.5 * $mid[2])
Check "density 150/25/150 at 6 deg: $((Blend 1.5 0.25 1.5 $mid).ToString('F3'))" ([math]::Abs((Blend 1.5 0.25 1.5 $mid) - $expected) -lt 1e-6)
$evening = Shares 2
Check "density 150/25/150 at 2 deg: the Sunset value" ((Blend 1.5 0.25 1.5 $evening) -eq [single]0.25)
Check "three different, then midnight: the Night value" ((Blend 1 2 3 (Shares -30)) -eq [single]3)

"=== colours ==="
function C32([int]$r, [int]$g, [int]$b) { return [Activator]::CreateInstance($color32, [byte]$r, [byte]$g, [byte]$b, [byte]255) }
function Exact($c) { return "{0:R} {1:R} {2:R}" -f $c.r, $c.g, $c.b }
$blend3 = $tint.GetMethod('Blend3')
$blend2 = $tint.GetMethod('Blend')
$white = C32 255 255 255
$amber = C32 216 190 152
$pink = C32 255 160 180
$whiteOk = $true; $pairColourOk = $true
for ($e = -20.0; $e -le 30.0; $e += 0.5) {
    $w = Shares $e
    $c = $blend3.Invoke($null, [object[]]@($white, $white, $white, $w[0], $w[1], $w[2]))
    if ((Exact $c) -ne '1 1 1') { $whiteOk = $false }
    $new = $blend3.Invoke($null, [object[]]@($white, $white, $amber, $w[0], $w[1], $w[2]))
    $old = $blend2.Invoke($null, [object[]]@($white, $amber, $w[2]))
    if ((Exact $new) -ne (Exact $old)) { $pairColourOk = $false }
}
Check "white all day: exactly (1, 1, 1) at every hour" $whiteOk
Check "a 1.3.0 night colour (Sunset = Day): exactly CloudTint.Blend" $pairColourOk
$sunsetTint = $blend3.Invoke($null, [object[]]@($white, $pink, $white, [single]0, [single]1, [single]0))
$pinkTint = $tint.GetMethod('Of').Invoke($null, [object[]]@($pink))
Check "full sunset: the sunset colour's tint" ((Exact $sunsetTint) -eq (Exact $pinkTint))

"=== an older settings file's night ==="
function Values([hashtable]$h) {
    $d = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($k in $h.Keys) { $d[$k] = $h[$k] }
    return ,$d   # the comma: PowerShell would otherwise unroll the dictionary into its entries
}
$older = $tod.GetMethod('OlderNight')
function OlderNight($values) {
    $dict = [System.Collections.Generic.IDictionary[string, string]]$values
    return $older.Invoke($null, [object[]]@($dict, 'CloudNightBrightness', 'CloudNightBrightnessClear', '150'))
}
Check "switch on: its night value"             ((OlderNight (Values @{ CloudNightBrightness = 'true'; CloudNightBrightnessClear = '120' })) -eq '120')
Check "switch on, any case: its night value"   ((OlderNight (Values @{ cloudnightbrightness = 'True'; CloudNightBrightnessClear = '120' })) -eq '120')
Check "switch off: the Day value (null)"       ($null -eq (OlderNight (Values @{ CloudNightBrightness = 'false'; CloudNightBrightnessClear = '120' })))
Check "no switch (before 1.3.0): the Day value" ($null -eq (OlderNight (Values @{ CloudBrightnessClear = '90' })))
Check "switch on, value missing: its old default" ((OlderNight (Values @{ CloudNightBrightness = 'true' })) -eq '150')
Check "switch unreadable: the Day value"       ($null -eq (OlderNight (Values @{ CloudNightBrightness = 'maybe'; CloudNightBrightnessClear = '120' })))

Write-Host ""
if ($failed -eq 0) { Write-Host "all passed" } else { Write-Host "$failed FAILED"; exit 1 }
