# Builds every shader we ship for AMD's graphics chips with AMD's own shader compiler, so a
# shader AMD's driver would choke on is caught before an upload, not by a player.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-amd.ps1
#   ... -Rga D:\Tools\RGA\rga.exe -Targets gfx1100      # another RGA folder, one chip
#
# Needs Radeon GPU Analyzer (free, MIT, github.com/GPUOpen-Tools/radeon_gpu_analyzer), which runs
# on any GPU: by default it is looked for in Documents\Projects\RGA, or set $env:VC_RGA.
#
# Two routes, because RGA has no single one that covers every chip:
#   -Targets (RDNA3 and newer): the EXACT bytes in the shipped Windows bundle (the Direct3D 11
#     programs, pulled out by shaderdump.ps1 -Raw), not a recompile. RGA's Direct3D 11 mode
#     refuses RDNA3 and only takes source, so the bytes go through its Direct3D 12 mode with the
#     AMD driver compiler it bundles (--offline, amdxc64.dll; RDNA3+ only) -- the same compiler,
#     another front door. Each pixel program is paired with a vertex program that links to it.
#   -SourceTargets (RDNA2 and older): RGA's Direct3D 11 mode -- the game's own path -- fed our
#     .shader sources the way Unity 5.6 compiles them (its defines and includes, the
#     backwards-compatibility + level-3 flags, ps_4_0/vs_4_0 like every shipped program).
#     2026-10-07 this rebuild matched the shipped cloud shader (same 109 texture reads, 12 loops).
#
# FAIL when a program does not compile for a chip, or when it spills registers to scratch
# memory (the classic way a big shader becomes far slower on one vendor than another). The
# table also prints registers and size, to compare with earlier releases: 1.4.0's cloud pixel
# shader was 13,446 instructions, 90 VGPRs, 102 SGPRs on gfx1100.
#
# Chips: gfx1030 = RX 6000 (RDNA2), gfx1100 = RX 7000 (RDNA3), gfx1201 = RX 9000 (RDNA4).
# `rga -s dx12 --offline -l` and `rga -s dx11 -l` list what each route can take.
# What it cannot see: a player's exact driver build, and Direct3D 11 on RDNA3 specifically.

param(
    [string]$Rga = $(if ($env:VC_RGA) { $env:VC_RGA } else { Join-Path $env:USERPROFILE "Documents\Projects\RGA\rga.exe" }),
    [string]$Bundle = (Join-Path (Split-Path -Parent $PSScriptRoot) "VolumetricClouds\Resources\volumetricclouds-win.bundle"),
    [string[]]$Targets = @("gfx1100", "gfx1201"),
    [string[]]$SourceTargets = @("gfx1030"),
    [string]$UnityIncludes = "C:\Program Files\Unity\Editor\Data\CGIncludes",
    [string]$OutDir = (Join-Path $env:TEMP "vc_amdcheck")
)

$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$fails = 0
function Fail([string]$text) { Write-Host "FAIL $text"; $script:fails++ }

if (Test-Path $Rga -PathType Container) { $Rga = Join-Path $Rga "rga.exe" }
if (-not (Test-Path $Rga)) {
    Write-Host "FAIL rga.exe not found at $Rga -- install Radeon GPU Analyzer there, or pass -Rga / set VC_RGA"
    exit 1
}
if (-not (Test-Path $Bundle)) { Write-Host "FAIL no bundle at $Bundle"; exit 1 }

# Every run in its own folder: nothing is ever deleted.
$run = Join-Path $OutDir (Get-Date -Format "yyyyMMdd-HHmmss")
$dxbc = Join-Path $run "dxbc"
$isa = Join-Path $run "isa"
New-Item -ItemType Directory -Force $dxbc, $isa | Out-Null

# The shaders: BundleBuilder.cs's one list (as build-bundle.ps1 reads it); each file's
# Shader "..." line gives the name stored in the bundle.
$builder = Join-Path $root "UnityProject\Assets\Editor\BundleBuilder.cs"
$files = @([regex]::Matches((Get-Content $builder -Raw), '"Assets/VolumetricClouds/(\w+)\.shader"') | ForEach-Object { $_.Groups[1].Value })
if ($files.Count -eq 0) { Write-Host "FAIL no shader names found in $builder"; exit 1 }

$shaders = @()
foreach ($f in $files) {
    $path = Join-Path $root "UnityProject\Assets\VolumetricClouds\$f.shader"
    $m = Select-String -Path $path -Pattern '^\s*Shader\s+"([^"]+)"' | Select-Object -First 1
    if (-not $m) { Fail "$f.shader has no Shader `"...`" line"; continue }
    $name = $m.Matches[0].Groups[1].Value
    # A child process each: shaderdump compiles a C# type, which can be added once per session.
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "shaderdump.ps1") -Name $name -AssetFile $Bundle -OutDir $dxbc -Raw
    $safe = $name.Replace('/', '_')
    $ps = @(Get-ChildItem $dxbc -Filter "$safe.*.ps.dxbc")
    $vs = @(Get-ChildItem $dxbc -Filter "$safe.*.vs.dxbc")
    if ($ps.Count -eq 0 -or $vs.Count -eq 0) { Fail "$name`: no Direct3D 11 programs found in the bundle ($(@($out)[-1]))"; continue }
    $shaders += [pscustomobject]@{ Name = $name; Short = $f; Ps = $ps; Vs = $vs }
}

# A pipeline description wide enough for every vertex program's inputs (extra elements are
# allowed), the HDR target the game renders to, and a root signature with room for all of
# our constant buffers, textures and samplers.
$gpso = Join-Path $run "state.gpso"
$elements = @(
    '{ "POSITION", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 0, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "NORMAL", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 16, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "TANGENT", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 32, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "COLOR", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 48, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "TEXCOORD", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 64, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "TEXCOORD", 1, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 80, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "TEXCOORD", 2, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 96, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "TEXCOORD", 3, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 112, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }'
)
@(
    "# schemaVersion", "1.0", "",
    "# InputLayoutNumElements", "$($elements.Count)", "",
    "# InputLayout", ($elements -join ",`r`n"), "",
    "# PrimitiveTopologyType", "D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE", "",
    "# NumRenderTargets", "1", "",
    "# RTVFormats", "{ DXGI_FORMAT_R16G16B16A16_FLOAT }"
) | Set-Content -Path $gpso -Encoding ascii

$rootSig = Join-Path $run "rootsig.hlsl"
'#define RS "RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT), DescriptorTable(CBV(b0, numDescriptors = 8), SRV(t0, numDescriptors = 16)), DescriptorTable(Sampler(s0, numDescriptors = 16))"' |
    Set-Content -Path $rootSig -Encoding ascii

function Measure-Isa([string]$file) {
    $lines = Get-Content $file
    return [pscustomobject]@{
        Spills = @($lines | Where-Object { $_ -match 'scratch_(load|store)' }).Count
        Size   = @($lines | Where-Object { $_ -match '^\s+(s_|v_|image_|buffer_|global_|scratch_|exp|ds_)' }).Count
    }
}

# One pipeline: true when both programs compiled; their numbers go into $rows.
$rows = New-Object System.Collections.Generic.List[object]
function Build([string]$target, $shader, $p, $v) {
    $tag = "{0}.{1}+{2}" -f $shader.Short, ($p.Name -split '\.')[-3], ($v.Name -split '\.')[-3]
    & $Rga -s dx12 --offline -c $target --vs-blob $v.FullName --ps-blob $p.FullName --gpso $gpso `
        --rs-hlsl $rootSig --rs-macro RS --isa (Join-Path $isa "$tag.isa") -a (Join-Path $isa "$tag.csv") 2>&1 | Out-Null
    $pixelIsa = Join-Path $isa "$($target)_$($tag)_pixel.isa"
    $vertIsa = Join-Path $isa "$($target)_$($tag)_vert.isa"
    if (-not (Test-Path $pixelIsa) -or -not (Test-Path $vertIsa)) { return $false }
    foreach ($stage in @(@("ps", $p, $pixelIsa, "pixel"), @("vs", $v, $vertIsa, "vert"))) {
        $m = Measure-Isa $stage[2]
        $csv = Join-Path $isa "$($target)_$($tag)_$($stage[3]).csv"
        $st = if (Test-Path $csv) { Import-Csv $csv | Select-Object -First 1 } else { $null }
        $rows.Add([pscustomobject]@{ Chip = $target; Program = ($stage[1].Name -replace '^VolumetricClouds_', '' -replace '\.dxbc$', '')
            Size = $m.Size; VGPRs = $st.USED_VGPRs; SGPRs = $st.USED_SGPRs; Spills = $m.Spills })
    }
    return $true
}

foreach ($target in $Targets) {
    foreach ($s in $shaders) {
        $doneVs = @{}
        foreach ($p in $s.Ps) {
            $ok = $false
            foreach ($v in $s.Vs) { if (Build $target $s $p $v) { $ok = $true; $doneVs[$v.Name] = $true; break } }
            if (-not $ok) { Fail "$target $($p.Name): did not compile with any vertex program of $($s.Name)" }
        }
        foreach ($v in $s.Vs) {
            if ($doneVs[$v.Name]) { continue }
            $ok = $false
            foreach ($p in $s.Ps) { if (Build $target $s $p $v) { $ok = $true; break } }
            if (-not $ok) { Fail "$target $($v.Name): did not compile with any pixel program of $($s.Name)" }
        }
    }
}

# The source route. Each CGPROGRAM block becomes a plain HLSL file with what Unity 5.6 adds
# for Direct3D 11; its #pragma vertex/fragment name the entry points.
$hlsl = Join-Path $run "hlsl"
New-Item -ItemType Directory -Force $hlsl | Out-Null
$srcDir = Join-Path $root "UnityProject\Assets\VolumetricClouds"
$head = @('#define SHADER_API_D3D11 1', '#define SHADER_TARGET 35', '#define UNITY_VERSION 566',
          '#define UNITY_COLORSPACE_GAMMA 0', '#include "HLSLSupport.cginc"', '#include "UnityShaderVariables.cginc"')
if ($SourceTargets.Count -gt 0 -and -not (Test-Path $UnityIncludes)) {
    Fail "Unity's CGIncludes not found at $UnityIncludes (pass -UnityIncludes, or -SourceTargets @())"
    $SourceTargets = @()
}
foreach ($s in $shaders) {
    $blocks = @(); $current = $null
    foreach ($l in (Get-Content (Join-Path $srcDir "$($s.Short).shader"))) {
        if ($l -match '^\s*CGPROGRAM') { $current = New-Object System.Collections.Generic.List[string]; continue }
        if ($l -match '^\s*ENDCG') { $blocks += ,$current; $current = $null; continue }
        if ($null -ne $current) { $current.Add($l) }
    }
    for ($b = 0; $b -lt $blocks.Count; $b++) {
        $text = $blocks[$b] -join "`n"
        $vert = if ($text -match '#pragma\s+vertex\s+(\w+)') { $Matches[1] } else { $null }
        $frag = if ($text -match '#pragma\s+fragment\s+(\w+)') { $Matches[1] } else { $null }
        $file = Join-Path $hlsl "$($s.Short).$b.hlsl"
        ($head + ($blocks[$b] | Where-Object { $_ -notmatch '^\s*#pragma\s+(vertex|fragment|target|multi_compile|shader_feature|only_renderers|exclude_renderers)' })) |
            Set-Content -Path $file -Encoding ascii
        foreach ($target in $SourceTargets) {
            foreach ($stage in @(@($frag, "ps_4_0", "ps"), @($vert, "vs_4_0", "vs"))) {
                if (-not $stage[0]) { continue }
                $tag = "$($s.Short).$b.$($stage[2])"
                # 0x9000 = D3DCOMPILE_ENABLE_BACKWARDS_COMPATIBILITY | D3DCOMPILE_OPTIMIZATION_LEVEL3
                $log = & $Rga -s dx11 -c $target -f $stage[0] -p $stage[1] --DXFlags 36864 -I $UnityIncludes -I $srcDir `
                    --isa (Join-Path $isa "$tag.isa") -a (Join-Path $isa "$tag.csv") $file 2>&1 | Out-String
                $isaFile = Get-ChildItem $isa -Filter "$($target)_$($stage[0])_*$tag*.isa" -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($log -notmatch "Building for $target\.\.\. succeeded" -or -not $isaFile) {
                    $why = ($log -split "`n" | Where-Object { $_ -match 'error' } | Select-Object -First 1)
                    Fail "$target $tag (source, $($stage[0])): did not compile $why"
                    continue
                }
                $m = Measure-Isa $isaFile.FullName
                $csv = Get-ChildItem $isa -Filter "*_$($stage[0])_*$tag*.csv" -ErrorAction SilentlyContinue | Select-Object -First 1
                $st = if ($csv) { Import-Csv $csv.FullName | Select-Object -First 1 } else { $null }
                $spills = $m.Spills
                if ($st -and $st.SCRATCH_MEM -match '^\d+$' -and [int]$st.SCRATCH_MEM -gt 0) { $spills = [math]::Max($spills, [int]$st.SCRATCH_MEM) }
                $rows.Add([pscustomobject]@{ Chip = "$target (src)"; Program = $tag; Size = $m.Size; VGPRs = $st.USED_VGPRs; SGPRs = $st.USED_SGPRs; Spills = $spills })
            }
        }
    }
}

# A program can appear twice (built with two partners); one line each is enough.
$unique = @($rows | Sort-Object Chip, Program -Unique)
$unique | Format-Table -AutoSize | Out-String -Width 160 | Write-Host
foreach ($r in $unique) { if ($r.Spills -gt 0) { Fail "$($r.Chip) $($r.Program): $($r.Spills) scratch (spill) instructions" } }

Write-Host "ISA listings: $isa"
if ($fails -gt 0) { Write-Host "$fails failure(s)"; exit 1 }
$chips = @($Targets) + @($SourceTargets | ForEach-Object { "$_ from source" })
Write-Host ("pass: {0} compiled programs on {1} ({2}), no spills" -f $unique.Count, ($chips -join ", "), "AMD's compiler")
