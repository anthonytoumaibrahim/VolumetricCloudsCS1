# The sky layer's pictures (1.5.0): cuts the clouds out of photographed skies (Poly Haven's CC0 HDRIs,
# unclipped .hdr panoramas) with tools/SkyLayerMaker.cs, and writes one grey picture per sky -- how
# much cloud there is in each direction, the upper half only, the top row straight up -- plus a check
# picture of each (the photo, the cut, the cut over a plain sky).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-skylayer.ps1 -Source <folder of .hdr> [-Out VolumetricClouds\Resources\SkyLayer] [-Width 4096] [-Quality 85]
#
# The .hdr files are not in the repo (25 MB each at 4K): download <id>_4k.hdr from polyhaven.com into
# -Source. 4096 wide is all a 4K photo holds (its upper half, at full size; 2048 looked blurry in-game,
# 2026-10-13). -Out gets what ships: <name>.jpg at quality 85, the mod's size in mind (a PNG of the
# same picture is 3-4x larger; the author's cap 2026-10-12: the whole mod around 5 MB at most).
# Without -Out the pictures go to the check folder only.

param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$Check = (Join-Path $Source "..\check"),
    [string]$Out,
    [int]$Width = 4096,
    [int]$Quality = 85,
    [string[]]$Only,
    [switch]$Views
)

# The skies: what the mod calls each picture, the Poly Haven name, where the cloud starts fading in
# over the horizon (degrees; it takes 3 more; anything standing higher is found and painted over),
# how far round the photo's sun its glare is taken for sky (degrees; 0 = the disc only), and how far
# round it the cloud is filled in smoothly from round it (degrees; 0 = the disc and its glare's core),
# and whether its starburst's rays and veil are taken out (2026-10-13: they showed in-game).
$skies = @(
    @{ Name = "cirrus-1";  Id = "cayley_lookout";              Cut = 3.0; SunClear = 0;  SunFill = 6;  Rays = $false },
    @{ Name = "cirrus-2";  Id = "malagas_ferry_road";          Cut = 1.5; SunClear = 16; SunFill = 0;  Rays = $true },
    @{ Name = "cirrus-3";  Id = "limpopo_golf_course";         Cut = 1.5; SunClear = 0;  SunFill = 0;  Rays = $false },
    @{ Name = "sheep-1";   Id = "umhlanga_sunrise";            Cut = 1.5; SunClear = 8;  SunFill = 0;  Rays = $false },
    @{ Name = "sheep-2";   Id = "citrus_orchard_road_puresky"; Cut = 1.0; SunClear = 0;  SunFill = 0;  Rays = $false },
    @{ Name = "far-1";     Id = "rosendal_plains_2";           Cut = 1.5; SunClear = 25; SunFill = 0;  Rays = $true },
    @{ Name = "far-2";     Id = "quarry_cloudy";               Cut = 1.5; SunClear = 0;  SunFill = 0;  Rays = $false },
    @{ Name = "layers-1";  Id = "cloud_layers";                Cut = 1.5; SunClear = 0;  SunFill = 13; Rays = $false }
)

Add-Type -Path (Join-Path $PSScriptRoot "SkyLayerMaker.cs") -ReferencedAssemblies System.Drawing
New-Item -ItemType Directory -Force $Check | Out-Null
if ($Out) { New-Item -ItemType Directory -Force $Out | Out-Null }

$total = 0
foreach ($s in $skies) {
    if ($Only -and $Only -notcontains $s.Name) { continue }
    $hdr = Join-Path $Source ($s.Id + "_4k.hdr")
    if (-not (Test-Path $hdr)) { Write-Host "MISSING $hdr"; continue }

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $sky = [SkyLayerMaker]::ReadUpperHalf($hdr)
    $cut = [SkyLayerMaker]::Extract($sky, [float]$s.Cut, 3.0, [float]$s.SunClear, [float]$s.SunFill, [bool]$s.Rays)
    $h = $Width / 4
    $small = [SkyLayerMaker]::Downsample($cut.Cloud, $cut.Width, $cut.Height, $Width, $h)
    $png = Join-Path $Check ($s.Name + ".png")
    $bytes = [SkyLayerMaker]::SaveGray($small, $Width, $h, $png)
    $jpg = [SkyLayerMaker]::SaveGrayJpeg($small, $Width, $h, (Join-Path $Check ($s.Name + ".jpg")), $Quality)
    [SkyLayerMaker]::SaveCheck($sky, $cut, 1024, 256, (Join-Path $Check ($s.Name + "-check.png")))
    if ($Views) {
        # What a 2560 x 1440 screen shows of it, looking 10 degrees up (the game's fov, 45 degrees tall):
        # the stored size against the photo's own.
        [SkyLayerMaker]::SaveView($small, $Width, $h, 2560, 1440, 200, 10, 45, (Join-Path $Check ($s.Name + "-view-" + $Width + ".png")))
        [SkyLayerMaker]::SaveView($cut.Cloud, $cut.Width, $cut.Height, 2560, 1440, 200, 10, 45, (Join-Path $Check ($s.Name + "-view-full.png")))
    }
    if ($Out) { Copy-Item (Join-Path $Check ($s.Name + ".jpg")) (Join-Path $Out ($s.Name + ".jpg")) -Force }
    $total += $jpg

    $mean = 0.0; foreach ($v in $small) { $mean += $v }; $mean /= $small.Length
    "{0,-9} {1,-28} png {2,7:N0} B  jpg {3,7:N0} B  cloud {4,4:P0}  {5:N1} s  {6}" -f $s.Name, $s.Id, $bytes, $jpg, $mean, $watch.Elapsed.TotalSeconds, $cut.Notes
}
"total jpg {0:N0} bytes" -f $total
