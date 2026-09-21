param(
    # Defaults to the mod's own settings file.
    [string]$Path = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\VolumetricClouds.cgs",
    # Regex over the key names, e.g. '^Halo'.
    [string]$Filter = '.'
)

# Prints every value saved in a ColossalFramework settings file (.cgs), without the game.
#
# Since 2026-09-21 the mod keeps its settings in VolumetricClouds.xml, plain text in the same
# folder: open that instead. VolumetricClouds.cgs is only read ONCE, by the import on the first
# run without an XML file, which then sets MovedToVolumetricCloudsXml in it. This script stays
# for that old file and for the game's own (-Path ...\gameSettings.cgs).
#
#   .\read-settings.ps1
#   .\read-settings.ps1 -Filter '^Halo'
#
# Layout (version 4, read off a hex dump): 'CGSF', uint16 version, then one block per value
# type -- int32, bool, float, then three more -- each an int32 count followed by entries of
# [length-prefixed name][value]. Only the first three blocks are decoded; the script says so
# if any other block is not empty.
#
# Keys nothing reads any more stay in the file for ever (DebugSkipDrawLight, HaloFogValue,
# FogThickness...). Check a name against Settings.cs before treating its value as live.

if (-not (Test-Path $Path)) {
    Write-Error "No settings file at $Path"
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes($Path)
if ([System.Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'CGSF') {
    Write-Error "Not a CGSF settings file: $Path"
    exit 1
}

$script:pos = 6

function Read-Count {
    $value = [BitConverter]::ToInt32($bytes, $script:pos)
    $script:pos += 4
    return $value
}

# BinaryWriter string: 7-bit encoded length, then the characters.
function Read-Name {
    $length = 0
    $shift = 0
    do {
        $b = $bytes[$script:pos]
        $script:pos += 1
        $length = $length -bor (($b -band 0x7f) -shl $shift)
        $shift += 7
    } while ($b -band 0x80)

    $name = [System.Text.Encoding]::UTF8.GetString($bytes, $script:pos, $length)
    $script:pos += $length
    return $name
}

$rows = New-Object System.Collections.Generic.List[object]

$count = Read-Count
for ($i = 0; $i -lt $count; $i++) {
    $name = Read-Name
    $rows.Add([pscustomobject]@{ Type = 'int'; Name = $name; Value = [BitConverter]::ToInt32($bytes, $script:pos).ToString() })
    $script:pos += 4
}

$count = Read-Count
for ($i = 0; $i -lt $count; $i++) {
    $name = Read-Name
    $rows.Add([pscustomobject]@{ Type = 'bool'; Name = $name; Value = ([bool]$bytes[$script:pos]).ToString() })
    $script:pos += 1
}

$count = Read-Count
for ($i = 0; $i -lt $count; $i++) {
    $name = Read-Name
    $value = [BitConverter]::ToSingle($bytes, $script:pos)
    $rows.Add([pscustomobject]@{ Type = 'float'; Name = $name; Value = $value.ToString('G6', [cultureinfo]::InvariantCulture) })
    $script:pos += 4
}

"$Path  (version $([BitConverter]::ToUInt16($bytes, 4)), written $((Get-Item $Path).LastWriteTime.ToString('yyyy-MM-dd HH:mm')))"
$rows | Where-Object { $_.Name -match $Filter } | Sort-Object Type, Name |
    ForEach-Object { '  {0,-5} {1,-28} {2}' -f $_.Type, $_.Name, $_.Value }

# Whatever follows should be empty blocks: a count of zero each.
$undecoded = 0
while ($script:pos + 4 -le $bytes.Length) {
    $undecoded += Read-Count
    if ($undecoded -ne 0) { break }
}
if ($undecoded -ne 0) {
    "  ! a later block (strings / input keys) is not empty and was not decoded"
}
