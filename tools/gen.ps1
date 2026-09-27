#Requires -Version 7
<#
.SYNOPSIS
  Luban CSV -> C# + binary tables (one run, both outputs).

.DESCRIPTION
  Reads data/luban.conf + data/*.csv and emits:
    - C#      -> src/ProjectF.Tables/Gen                     (compiled into ProjectF.Tables)
    - binary  -> UnityProject/Assets/StreamingAssets/Tables  (loaded by the Unity client)

  Luban.dll is expected at tools/luban/Luban.dll (gitignored — see data/README.md).
#>

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$lubanDll = Join-Path $repoRoot 'tools/luban/Luban.dll'
$dataDir = Join-Path $repoRoot 'data'
$confFile = Join-Path $dataDir 'luban.conf'
$outCodeDir = Join-Path $repoRoot 'src/ProjectF.Tables/Gen'
$outDataDir = Join-Path $repoRoot 'UnityProject/Assets/StreamingAssets/Tables'

# ---- 1. Locate Luban.dll (fail loudly with a download pointer) ---------------
if (-not (Test-Path $lubanDll)) {
    Write-Host ''
    Write-Host 'ERROR: Luban.dll not found.' -ForegroundColor Red
    Write-Host ''
    Write-Host "  Expected at: $lubanDll" -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  Download the Luban release (net8/net9 build, Luban.dll + its deps) from:' -ForegroundColor White
    Write-Host '    https://github.com/focus-creative-games/luban/releases' -ForegroundColor Cyan
    Write-Host ''
    Write-Host '  Then place the files so that this path exists:' -ForegroundColor White
    Write-Host "    tools/luban/Luban.dll" -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  Quick start (latest release, zip -> extract):' -ForegroundColor White
    Write-Host '    https://github.com/focus-creative-games/luban/releases/latest' -ForegroundColor Cyan
    Write-Host ''
    Write-Host '  The folder tools/luban/ is gitignored — the DLL is never committed.' -ForegroundColor DarkGray
    exit 1
}

# ---- 2. Prepare output dirs ---------------------------------------------------
New-Item -ItemType Directory -Force -Path $outCodeDir | Out-Null
New-Item -ItemType Directory -Force -Path $outDataDir | Out-Null

Write-Host '==> Running Luban (cs-bin: C# + binary in one pass)...' -ForegroundColor Cyan
& dotnet $lubanDll `
    --conf $confFile `
    -t all `
    -c cs-bin `
    -d bin `
    -x outputCodeDir=$outCodeDir `
    -x outputDataDir=$outDataDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Luban failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

# ---- 3. Report ----------------------------------------------------------------
Write-Host ''
Write-Host '==> OK' -ForegroundColor Green
Write-Host ("    C#     -> {0} ({1} files)" -f $outCodeDir, (Get-ChildItem $outCodeDir -Recurse -File -Filter '*.cs').Count)
Write-Host ("    binary -> {0}" -f $outDataDir)
Write-Host ''
Write-Host 'Next: dotnet build ProjectF.sln  (ProjectF.Tables compiles the new Gen/ output)' -ForegroundColor DarkGray
