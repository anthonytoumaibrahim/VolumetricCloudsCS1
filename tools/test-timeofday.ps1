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
$high = Shares 30; $bright = Shares 3.25; $low = Shares 0; $dark = Shares -10
Check "high sun: all Day" (($high[0] -eq 1) -and ($high[1] -eq 0) -and ($high[2] -eq 0))
Check "3.25 deg, the game's sun still full: all Day" (($bright[0] -eq 1) -and ($bright[1] -eq 0) -and ($bright[2] -eq 0))
Check "the horizon: no Day, the rest Sunset and the 1.3.0 night ($($low[1].ToString('P0')) sunset)" (($low[0] -eq 0) -and ($low[1] -gt 0.85) -and ($low[1] -eq [single](1 - $low[2])))
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
$mid = Shares 1.5
$expected = [single](1.5 * $mid[0] + 0.25 * $mid[1] + 1.5 * $mid[2])
Check "density 150/25/150 at 1.5 deg: $((Blend 1.5 0.25 1.5 $mid).ToString('F3'))" ([math]::Abs((Blend 1.5 0.25 1.5 $mid) - $expected) -lt 1e-6)
Check "density 150/25/150 at 6 deg: still the Day value" ((Blend 1.5 0.25 1.5 (Shares 6)) -eq [single]1.5)
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

"=== the weather (Sky.Overcast) ==="
$overcast = $asm.GetType('VolumetricClouds.Sky.Overcast', $true)
$wblend = $overcast.GetMethods() | Where-Object { $_.Name -eq 'Blend' -and $_.GetParameters().Count -eq 3 }
function WBlend([single]$c, [single]$o, [single]$s) { return [single]$wblend.Invoke($null, [object[]]@($c, $o, $s)) }
$lerp = $mathf.GetMethod('Lerp', [type[]]@([single], [single], [single]))
function Lerp([single]$a, [single]$b, [single]$t) { return [single]$lerp.Invoke($null, [object[]]@($a, $b, $t)) }

$lerpOk = $true; $worst = ''
foreach ($pair in @(@(1.85, 0.15), @(3, 3), @(0.5, 1.5), @(2.4, 0.8), @(0.25, 0.1125))) {
    for ($s = -0.25; $s -le 1.25; $s += 0.01) {
        $a = WBlend $pair[0] $pair[1] $s; $b = Lerp $pair[0] $pair[1] $s
        if ($a -ne $b) { $lerpOk = $false; $worst = "$($pair[0])/$($pair[1]) at $s : $a vs $b" }
    }
}
Check "clear to overcast is Mathf.Lerp bit for bit (1.4.0's brightness curve)" $lerpOk $worst
Check "the two equal: the Clear sky value exactly, any cover" (((WBlend 0.37 0.37 0.61) -eq [single]0.37) -and ((WBlend 0.37 0.37 1) -eq [single]0.37))

# Cloud brightness = per time the clear/overcast blend, then the times: what 1.4.0 drew with Auto on.
$sameOk = $true
for ($e = -20.0; $e -le 30.0; $e += 0.5) {
    $w = Shares $e
    foreach ($cov in 0, 0.3, 0.93, 1) {
        $old = Blend (Lerp 1.85 0.15 $cov) (Lerp 3 3 $cov) (Lerp 1.2 0.55 $cov) $w
        $new = Blend (WBlend 1.85 0.15 $cov) (WBlend 3 3 $cov) (WBlend 1.2 0.55 $cov) $w
        if ($old -ne $new) { $sameOk = $false }
    }
}
Check "Cloud brightness draws 1.4.0's automatic brightness exactly (every hour, every cover)" $sameOk

$blend6c = $tint.GetMethod('Blend6')
$grey = C32 170 178 190
$sixOk = $true
for ($e = -20.0; $e -le 30.0; $e += 0.5) {
    $w = Shares $e
    $six = $blend6c.Invoke($null, [object[]]@($white, $white, $amber, $white, $white, $amber, [single]0.7, $w[0], $w[1], $w[2]))
    $three = $blend3.Invoke($null, [object[]]@($white, $white, $amber, $w[0], $w[1], $w[2]))
    if ((Exact $six) -ne (Exact $three)) { $sixOk = $false }
}
Check "colours, overcast the same as clear: exactly Blend3" $sixOk
$fullGrey = $blend6c.Invoke($null, [object[]]@($white, $white, $white, $grey, $grey, $grey, [single]1, [single]1, [single]0, [single]0))
$greyTint = $tint.GetMethod('Of').Invoke($null, [object[]]@($grey))
Check "colours, full overcast by day: the overcast colour's tint" ((Exact $fullGrey) -eq (Exact $greyTint))

"=== an older file's brightness (SettingsCatalog.OlderLight) ==="
$catalog = $asm.GetType('VolumetricClouds.UI.SettingsCatalog', $true)
$olderLight = $catalog.GetMethod('OlderLight', [System.Reflection.BindingFlags]'Static,NonPublic,Public')
function OlderLight($values, [bool]$overcast, [int]$time) {
    return $olderLight.Invoke($null, [object[]]@([System.Collections.Generic.IDictionary[string, string]]$values, $overcast, $time))
}
# His 1.4.0-era file: Auto on, clear 185/300/120, overcast 15/300/55.
$his = Values @{ CloudBrightnessAuto = 'true'; CloudBrightnessClear = '185'; CloudBrightnessClearSunset = '300'; CloudBrightnessClearNight = '120';
                 CloudBrightnessOvercast = '15'; CloudBrightnessOvercastSunset = '300'; CloudBrightnessOvercastNight = '55';
                 CloudBrightness = '45'; CloudBrightnessSunset = '45'; CloudBrightnessNight = '5'; MinIllumination = '100' }
Check "Auto on: clear sky 185/300/120" (((OlderLight $his $false 0) -eq '185') -and ((OlderLight $his $false 1) -eq '300') -and ((OlderLight $his $false 2) -eq '120'))
Check "Auto on: overcast 15/300/55" (((OlderLight $his $true 0) -eq '15') -and ((OlderLight $his $true 1) -eq '300') -and ((OlderLight $his $true 2) -eq '55'))
$fixed = Values @{ CloudBrightnessAuto = 'false'; CloudBrightness = '45'; CloudBrightnessNight = '20'; MinIllumination = '50'; CloudBrightnessClear = '185' }
Check "Auto off: the fixed value under a clear sky (45, sunset = day, night 20)" (((OlderLight $fixed $false 0) -eq '45') -and ((OlderLight $fixed $false 1) -eq '45') -and ((OlderLight $fixed $false 2) -eq '20'))
Check "Auto off: overcast = fixed x the floor (22.5, 10)" (((OlderLight $fixed $true 0) -eq '22.5') -and ((OlderLight $fixed $true 2) -eq '10'))
$v13 = Values @{ CloudBrightnessAuto = 'true'; CloudBrightnessClear = '150'; CloudBrightnessOvercast = '50'; CloudNightBrightness = 'true'; CloudNightBrightnessClear = '90' }
Check "a 1.3.0 file: Sunset = Day, Night from the night switch (overcast night: its old default 50)" (((OlderLight $v13 $false 1) -eq '150') -and ((OlderLight $v13 $false 2) -eq '90') -and ((OlderLight $v13 $true 2) -eq '50'))
$bare = Values @{ }
Check "nothing written: 1.4.0's defaults, 150 and 50" (((OlderLight $bare $false 0) -eq '150') -and ((OlderLight $bare $true 0) -eq '50'))

Write-Host ""
if ($failed -eq 0) { Write-Host "all passed" } else { Write-Host "$failed FAILED"; exit 1 }
