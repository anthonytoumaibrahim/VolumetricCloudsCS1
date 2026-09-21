# Offline checks of SettingsXmlFormat -- the text of VolumetricClouds.xml -- run against the
# BUILT mod DLL, no game needed.
#
#   dotnet build VolumetricClouds/VolumetricClouds.csproj
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-settings.ps1
#
# What is covered: numbers survive the trip slider -> file -> slider exactly, in any Windows
# culture; the spellings a hand edit may use are read (and the dangerous ones refused: "0,5",
# "NaN", "8" meaning Backspace); keys format and parse both ways; a written file parses back
# to the same values, comments included (an XML comment may not hold "--", and the catalog's
# labels do); and a broken or foreign file is reported, never half-read.
#
# Not covered, because it needs the game: SettingsXml itself (the catalog, the saver, the
# import from the .cgs). The log says what that did on every start.

param(
    [string]$Dll = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\VolumetricClouds\VolumetricClouds.dll"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

Add-Type -TypeDefinition @"
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
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
public static class TestHelp
{
    // Single-precision arithmetic, as the mod does it: PowerShell would promote to double.
    public static float Mul(float a, float b) { return a * b; }
    public static float Div(float a, float b) { return a / b; }

    // PowerShell 5.1 puts the thread's culture back after every statement, so the switch has
    // to happen in the same call as the method under test.
    public static object InCulture(string culture, MethodInfo method, object[] args)
    {
        CultureInfo saved = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
        try { return method.Invoke(null, args); }
        finally { Thread.CurrentThread.CurrentCulture = saved; }
    }

    public static string CultureOf(string culture, double value)
    {
        CultureInfo saved = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
        try { return value.ToString(); }
        finally { Thread.CurrentThread.CurrentCulture = saved; }
    }
}
"@
[TestResolver]::Dirs = @($managed, (Split-Path -Parent $Dll))
[TestResolver]::Install()

[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed\ColossalManaged.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$fmt = $asm.GetType('VolumetricClouds.SettingsXmlFormat', $true)
$entryType = $asm.GetType('VolumetricClouds.SettingsXmlFormat+Entry', $true)
$failed = 0

function Check([string]$name, $actual, $expected) {
    if ("$actual" -ceq "$expected") { Write-Host "ok    $name  ->  $actual" }
    else { Write-Host "FAIL  $name  ->  [$actual]  expected [$expected]"; $script:failed++ }
}

function TryNumber([string]$text) {
    $v = 0.0
    if ($fmt::TryParseNumber($text, [ref]$v)) { return $v } else { return 'refused' }
}

function TryBool([string]$text) {
    $v = $false
    if ($fmt::TryParseBool($text, [ref]$v)) { return $v } else { return 'refused' }
}

function TryKey([string]$text) {
    $v = 0
    if ($fmt::TryParseKey($text, [ref]$v)) { return $v } else { return 'refused' }
}

function Parse([string]$text) {
    $version = 0
    $dups = New-Object 'System.Collections.Generic.List[string]'
    try {
        $values = $fmt::Parse($text, [ref]$version, $dups)
        return @{ Values = $values; Version = $version; Duplicates = $dups; Error = $null }
    }
    catch {
        $inner = $_.Exception
        while ($inner.InnerException -ne $null -and -not ($inner -is [System.FormatException])) { $inner = $inner.InnerException }
        return @{ Values = $null; Version = 0; Duplicates = $dups; Error = $inner }
    }
}

# ---- numbers ----------------------------------------------------------------------------

Check "47%"            ($fmt::FormatNumber([double][TestHelp]::Mul(0.47, 100))) "47"
Check "3.3x"           ($fmt::FormatNumber([double][single]3.3)) "3.3"
Check "cool 40%"       ($fmt::FormatNumber([double][single]-0.4)) "-0.4"
Check "13.5 km"        ($fmt::FormatNumber(13500)) "13500"
Check "no -0"          ($fmt::FormatNumber(-0.00000001)) "0"
Check "four decimals"  ($fmt::FormatNumber(0.123456)) "0.1235"

# Every whole-percent slider position from 0 to 400%: stored -> written -> read -> stored must
# give back the very same float, and the number written must be the one the slider shows.
$bad = 0
foreach ($p in 0..400) {
    $stored = [TestHelp]::Div([single]$p, 100)
    $text = $fmt::FormatNumber([double][TestHelp]::Mul($stored, 100))
    $back = [TestHelp]::Div([single](TryNumber $text), 100)
    if ($back -ne $stored -or $text -ne "$p") { $bad++ ; Write-Host "   $p% -> '$text' -> $back" }
}
Check "every percent survives the file exactly" $bad 0

# A 0.05-step slider stores float noise (18 * 0.05f = 0.90000004), which four decimals drop on
# purpose. What must hold: the value comes back within that noise, and writing it again gives
# the same text -- or a saved file would keep changing under the player.
$bad = 0
foreach ($i in -20..20) {
    $stored = [TestHelp]::Mul([single]$i, [single]0.05)
    $text = $fmt::FormatNumber([double]$stored)
    $back = [single](TryNumber $text)
    $again = $fmt::FormatNumber([double]$back)
    if ([math]::Abs([double]$back - [double]$stored) -gt 1e-6 -or $again -ne $text) { $bad++ ; Write-Host "   $stored -> '$text' -> $back -> '$again'" }
}
Check "a 0.05-step slider: stable text, value within float noise" $bad 0

Check "dot"            (TryNumber "0.5") 0.5
Check "comma refused"  (TryNumber "0,5") "refused"
Check "no thousands"   (TryNumber "13,500") "refused"
Check "percent sign"   (TryNumber "47%") 47
Check "spaces"         (TryNumber " 12 ") 12
Check "exponent"       (TryNumber "1e3") 1000
Check "NaN refused"    (TryNumber "NaN") "refused"
Check "inf refused"    (TryNumber "Infinity") "refused"
Check "empty refused"  (TryNumber "") "refused"

# A German Windows writes 0,5 -- the file must not.
$format = $fmt.GetMethod('FormatNumber')
$parse = $fmt.GetMethod('TryParseNumber')
Check "de-DE really is a comma culture" ([TestHelp]::CultureOf('de-DE', 0.5)) "0,5"
Check "de-DE writes a dot" ([TestHelp]::InCulture('de-DE', $format, @([double]0.5))) "0.5"
$args2 = [object[]]@("0.5", [double]0)
[void][TestHelp]::InCulture('de-DE', $parse, $args2)
Check "de-DE reads a dot" $args2[1] 0.5
$args2 = [object[]]@("0,5", [double]0)
Check "de-DE still refuses a comma" ([TestHelp]::InCulture('de-DE', $parse, $args2)) "False"

# ---- switches ---------------------------------------------------------------------------

Check "true"   (TryBool "true") "True"
Check "TRUE"   (TryBool "TRUE") "True"
Check "on"     (TryBool " on ") "True"
Check "1"      (TryBool "1") "True"
Check "false"  (TryBool "false") "False"
Check "no"     (TryBool "no") "False"
Check "maybe"  (TryBool "maybe") "refused"

# ---- keys -------------------------------------------------------------------------------

$ctrl = 0x40000000; $shift = 0x20000000; $alt = 0x10000000
$F4 = 285; $C = 99; $Alpha8 = 56; $Backspace = 8

Check "F4 out"            ($fmt::FormatKey($F4)) "F4"
Check "Ctrl+Shift+C out"  ($fmt::FormatKey($C -bor $ctrl -bor $shift)) "Ctrl+Shift+C"
Check "Alt+F4 out"        ($fmt::FormatKey($F4 -bor $alt)) "Alt+F4"
Check "None out"          ($fmt::FormatKey(0)) "None"
Check "Ctrl+None out"     ($fmt::FormatKey($ctrl)) "None"
Check "F4 in"             (TryKey "F4") $F4
Check "any case, any order" (TryKey "shift+CONTROL+c") ($C -bor $ctrl -bor $shift)
Check "None in"           (TryKey "none") 0
Check "a digit is the top row, not a value" (TryKey "8") $Alpha8
Check "Backspace by name" (TryKey "Backspace") $Backspace
Check "a number refused"  (TryKey "285") "refused"
Check "a flags list refused" (TryKey "F4,F5") "refused"
Check "unknown modifier"  (TryKey "Hyper+F4") "refused"
Check "modifier alone"    (TryKey "Ctrl+") "refused"
Check "unknown key"       (TryKey "Foo") "refused"

$keyType = [System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll").GetType('UnityEngine.KeyCode')
$bad = 0
foreach ($code in [Enum]::GetValues($keyType)) {
    if ([int]$code -eq 0) { continue }
    foreach ($mods in @(0, $ctrl, ($ctrl -bor $shift -bor $alt))) {
        $encoded = [int]$code -bor $mods
        $text = $fmt::FormatKey($encoded)
        $back = TryKey $text
        # Aliased enum names print as one of the pair; the value is what has to come back.
        if ($back -ne $encoded) { $bad++; if ($bad -le 5) { Write-Host "   $encoded -> '$text' -> $back" } }
    }
}
Check "every Unity key, with modifiers, both ways" $bad 0

# ---- the file ---------------------------------------------------------------------------

function Entry($section, $group, $name, $value, $comment) {
    $e = [Activator]::CreateInstance($entryType)
    $e.Section = $section; $e.Group = $group; $e.Name = $name; $e.Value = $value; $e.Comment = $comment
    return $e
}

$list = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($entryType))
$list.Add((Entry "In-game panel: Now tab" $null "CoverageOverride" "false" "Ignore the game's weather. true or false, default false."))
$list.Add((Entry "In-game panel: Now tab" $null "Coverage" "91" "Fixed intensity, in %. 0 to 100, default 91."))
$list.Add((Entry "Options page: Weather tab" "Rain" "RainCurtains" "100" "A label -- with a dash -- and one at the end -"))
$list.Add((Entry "Options page: Weather tab" $null "ToggleKey" "Ctrl+Shift+C" "Open this panel. A key such as F4."))
$list.Add((Entry "Options page: Weather tab" $null "Odd" "<&>" $null))
$text = $fmt::Write($list, "Header line one`nHeader -- line two ends in a dash -")

$r = Parse $text
Check "written file parses" ($r.Error -eq $null) "True"
if ($r.Values -ne $null) {
    Check "version"          $r.Version 1
    Check "a switch"         $r.Values["CoverageOverride"] "false"
    Check "a percentage"     $r.Values["Coverage"] "91"
    Check "under a group"    $r.Values["RainCurtains"] "100"
    Check "a key"            $r.Values["ToggleKey"] "Ctrl+Shift+C"
    Check "escaped text"     $r.Values["Odd"] "<&>"
    Check "names ignore case" $r.Values["coverage"] "91"
    Check "five values"      $r.Values.Count 5
}
Check "CRLF line ends"      ($text.Contains("`r`n")) "True"
Check "one section heading each" ([regex]::Matches($text, "==========  ?Options page").Count) 1

$r = Parse "<VolumetricClouds version=`"1`"><Mine><A>1</A></Mine><B> 2 </B></VolumetricClouds>"
Check "wrapped lines are looked into" ("" + $r.Values["A"] + "," + $r.Values["B"]) "1,2"

$r = Parse "<VolumetricClouds><A>1</A><a>2</a></VolumetricClouds>"
Check "twice: the last one wins" $r.Values["A"] "2"
Check "twice: and it is reported" ($r.Duplicates -join ",") "a"
Check "no version attribute reads as 0" $r.Version 0

$r = Parse "<VolumetricClouds>`r`n  <A>1</A>`r`n  <B>2</B>`r`n"
Check "broken file is refused" ($r.Error -is [System.FormatException]) "True"
Check "...and says where" ($r.Error.Message -match "[Ll]ine") "True"
Write-Host "      (the parser says: $($r.Error.Message))"

$r = Parse "<ThemeMixer><A>1</A></ThemeMixer>"
Check "another mod's file is refused" ($r.Error -is [System.FormatException]) "True"

$r = Parse ""
Check "an empty file is refused" ($r.Error -is [System.FormatException]) "True"

$r = Parse "<VolumetricClouds><!-- a -- b --><A>1</A></VolumetricClouds>"
Check "a '--' in a hand-written comment is refused, not half-read" ($r.Error -is [System.FormatException]) "True"

Write-Host ""
if ($failed -eq 0) { Write-Host "all passed" } else { Write-Host "$failed FAILED"; exit 1 }
