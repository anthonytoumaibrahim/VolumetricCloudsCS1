# Offline checks of the cloud colours, run against the BUILT mod DLL, no game needed:
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-color.ps1
#
# ColorText: every spelling the file, the colour field and Paste accept, and the ones refused.
# CloudTint: what a colour does to the light -- above all that WHITE (the default) is exactly
# (1, 1, 1), so a player who never touches the colours gets bit for bit the clouds of before;
# that any grey is no tint; that a tint keeps the brightness (luminance 1) up to the 3x cap.

param(
    [string]$Dll = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds\VolumetricClouds.dll"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
public static class ColorTestResolver
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
[ColorTestResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[ColorTestResolver]::Install()

$unity = [System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$text = $asm.GetType('VolumetricClouds.ColorText', $true)
$tint = $asm.GetType('VolumetricClouds.Sky.CloudTint', $true)
$color32 = $unity.GetType('UnityEngine.Color32', $true)
$failed = 0

function Check([string]$name, $actual, $expected) {
    if ("$actual" -ceq "$expected") { Write-Host "ok    $name  ->  $actual" }
    else { Write-Host "FAIL  $name  ->  [$actual]  expected [$expected]"; $script:failed++ }
}

function Read([string]$s) {
    $args = [object[]]@($s, [Activator]::CreateInstance($color32))
    $ok = $text.GetMethod('TryParse').Invoke($null, $args)
    if ($ok) { return $text::Format($args[1]) } else { return 'refused' }
}

function C32([int]$r, [int]$g, [int]$b) { return [Activator]::CreateInstance($color32, [byte]$r, [byte]$g, [byte]$b, [byte]255) }
function Exact($c) { return "{0:R} {1:R} {2:R}" -f $c.r, $c.g, $c.b }
function Lum($c) { return [double](0.2126 * $c.r + 0.7152 * $c.g + 0.0722 * $c.b) }

# ---- ColorText ------------------------------------------------------------------------------

Check "format"                ($text::Format((C32 1 2 255)))  "#0102FF"
Check "#RRGGBB"               (Read "#FFC0CB")                "#FFC0CB"
Check "lower case, no #"      (Read "ffc0cb")                 "#FFC0CB"
Check "0x prefix"             (Read "0xFFC0CB")               "#FFC0CB"
Check "#RGB"                  (Read "#fc0")                   "#FFCC00"
Check "bare RGB refused"      (Read "fc0")                    "refused"
Check "#RRGGBBAA, alpha gone" (Read "#FFC0CB80")              "#FFC0CB"
Check "spaces and quotes"     (Read "  '#ffc0cb'  ")          "#FFC0CB"
Check "rgb()"                 (Read "rgb(255, 192, 203)")     "#FFC0CB"
Check "rgba(), alpha gone"    (Read "rgba(255,192,203,0.5)")  "#FFC0CB"
Check "Unity's log format"    (Read "RGBA(1.000, 0.753, 0.796, 0.000)") "#FFC0CB"
Check "comma list"            (Read "255, 192, 203")          "#FFC0CB"
Check "space list"            (Read "255 192 203")            "#FFC0CB"
Check "semicolon list"        (Read "255;192;203")            "#FFC0CB"
Check "0..1 list"             (Read "1, 0.5, 0")              "#FF8000"
Check "percentages"           (Read "100%, 50%, 0%")          "#FF8000"
Check "whole numbers are 0..255" (Read "1, 1, 1")             "#010101"
Check "pulled into 0..255"    (Read "300, -5, 128")           "#FF0080"
Check "a name"                (Read "Pink")                   "#FFC0CB"
Check "white by name"         (Read "white")                  "#FFFFFF"
Check "empty refused"         (Read "")                       "refused"
Check "a word refused"        (Read "hello")                  "refused"
Check "two numbers refused"   (Read "rgb(1, 2)")              "refused"
Check "NaN refused"           (Read "NaN, 1, 2")              "refused"
Check "not hex refused"       (Read "#GGHHII")                "refused"
Check "five digits refused"   (Read "12345")                  "refused"
Check "hsl() refused"         (Read "hsl(0, 100%, 50%)")      "refused"

# ---- CloudTint ------------------------------------------------------------------------------

Check "white is EXACTLY no tint"  (Exact ($tint::Of((C32 255 255 255)))) "1 1 1"
Check "grey is no tint"           (Exact ($tint::Of((C32 128 128 128)))) "1 1 1"
Check "black is no tint"          (Exact ($tint::Of((C32 0 0 0))))       "1 1 1"
Check "sRGB 255 decodes to 1"     ("{0:R}" -f $tint::ToLinear([byte]255)) "1"
Check "sRGB 128 decodes to 0.2159" ("{0:F4}" -f $tint::ToLinear([byte]128)) "0.2159"

$pink = $tint::Of((C32 255 192 203))
Check "pink keeps the brightness" ("{0:F5}" -f (Lum $pink)) "1.00000"
Check "pink stays pink (r > b > g)" ($pink.r -gt $pink.b -and $pink.b -gt $pink.g) "True"

$orange = $tint::Of((C32 255 140 0))
Check "orange keeps the brightness" ("{0:F5}" -f (Lum $orange)) "1.00000"

$red = $tint::Of((C32 255 0 0))
Check "pure red: capped at 3x"    (Exact $red) "3 0 0"
Check "dark red = red (only hue counts)" (Exact ($tint::Of((C32 64 0 0)))) (Exact $red)
Check "pure blue: capped at 3x"   (Exact ($tint::Of((C32 0 0 255)))) "0 0 3"
Check "pure red reads at 64%"     ("{0:F2}" -f (Lum $red)) "0.64"

Write-Host ""
if ($failed -eq 0) { Write-Host "all passed" } else { Write-Host "$failed FAILED"; exit 1 }
