#Requires -Version 7
<#
.SYNOPSIS
  Stage 2.5 vertical spike in ONE command: SeedNode + Unity PingAction probe + cleanup.

.DESCRIPTION
  Automates the pipeline proven in Stage 2.5:
    1. dotnet build ProjectF.sln
    2. Start ProjectF.SeedNode against -StorePath, reusing the ORIGINAL validator
       key from <StorePath>/privkey.txt when present (restart determinism: the
       stored chain validates its genesis against the key-derived genesis; a
       fresh ephemeral key throws InvalidGenesisBlockException).
    3. Fire the network probe inside the Unity editor via the MCP CLI
       (script-execute launcher -> background thread -> result JSON file).
    4. Poll the result file; PASS = counter strictly increased over baseline.
    5. Cleanup: stop the seed node (unless -KeepSeed), print the verdict.

  Prerequisites: the Unity editor must be OPEN on this project with the MCP
  plugin listening (port 20817 by default). Anything needing a fresh compile
  should be done in the editor beforehand.

.PARAMETER StorePath
  Seed store directory (holds genesis.dat / peer.txt / privkey.txt).
  Default: $env:TEMP/pf-seed

.PARAMETER SeedPort
  Swarm port the seed listens on (also used to locate/kill its PID).

.PARAMETER McpProjectArg
  Path argument passed to unity-mcp-cli (repo-root relative Unity project).

.EXAMPLE
  pwsh scripts/run-netprobe.ps1                 # full cycle
  pwsh scripts/run-netprobe.ps1 -SkipBuild      # seed + probe only
  pwsh scripts/run-netprobe.ps1 -KeepSeed       # leave the seed running
#>
param(
    [string]$StorePath = (Join-Path $env:TEMP 'pf-seed'),
    [int]$SeedPort = 31236,
    [string]$McpProjectArg = 'UnityProject',
    [int]$ProbeTimeoutSeconds = 2500,  # matches the probe's 2400s session budget + slack
    [switch]$SkipBuild,
    [switch]$KeepSeed,
    [switch]$FreshSeed   # wipe seed + probe stores first (fast clean spike; new genesis)
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repoRoot 'artifacts'
$seedLog = Join-Path $artifacts 'seed-netprobe.log'
$seedErr = Join-Path $artifacts 'seed-netprobe.err'
$resultFile = Join-Path $artifacts 'net-probe-result.json'
$launcherArgs = Join-Path $artifacts 'net-probe-args.json'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

function Get-ListeningPid([int]$Port) {
    # Netstat parse: find the PID bound to $Port (bash-less, Windows-native).
    $line = netstat -ano | Select-String ":$Port\s.*LISTENING" | Select-Object -First 1
    if ($line) { return [int]($line.ToString().Trim() -split '\s+')[-1] }
    return $null
}

function Stop-SeedNode {
    # NB: do NOT name a variable $pid — that is PowerShell's read-only $PID.
    $procId = Get-ListeningPid $SeedPort
    if ($procId) {
        Write-Host "==> Stopping seed node (PID $procId, port $SeedPort)" -ForegroundColor DarkGray
        Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    else {
        Write-Host "==> No seed node listening on $SeedPort" -ForegroundColor DarkGray
    }
}

$overallStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
# On Windows, `npx` resolves to npm's sh wrapper (not executable by
# Start-Process: "%1 is not a valid Win32 application") — use npx.cmd.
$npxCmd = (Get-Command 'npx.cmd' -ErrorAction SilentlyContinue) ?? (Get-Command 'npx' -ErrorAction Stop)
try {
    # ---- 1. Build -----------------------------------------------------------------
    if (-not $SkipBuild) {
        Write-Host '==> [1/5] dotnet build ProjectF.sln' -ForegroundColor Cyan
        dotnet build (Join-Path $repoRoot 'ProjectF.sln') --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
    }
    else {
        Write-Host '==> [1/5] build skipped (-SkipBuild)' -ForegroundColor DarkGray
    }

    # ---- 2. Seed node -------------------------------------------------------------
    Write-Host "==> [2/5] starting SeedNode (store: $StorePath)" -ForegroundColor Cyan
    Stop-SeedNode   # a stale seed with an in-memory store would poison privkey.txt

    if ($FreshSeed) {
        # New chain: wipe seed store AND every per-genesis probe store
        # (<store>-probe-<hash8>) so nothing realigns against the old chain.
        Write-Host '    -FreshSeed: wiping seed + probe stores' -ForegroundColor Yellow
        Remove-Item $StorePath -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item "$StorePath-probe*" -Recurse -Force -ErrorAction SilentlyContinue
    }

    $env:PF_SeedNode__StorePath = $StorePath
    $privKeyFile = Join-Path $StorePath 'privkey.txt'
    if (Test-Path $privKeyFile) {
        # Restart determinism (Stage 2.5 lesson #1): reuse the original key or the
        # ctor rejects the store with InvalidGenesisBlockException.
        $env:PF_SeedNode__PrivateKeyHex = (Get-Content $privKeyFile -Raw).Trim()
        Write-Host '    validator key: reused from privkey.txt' -ForegroundColor DarkGray
    }
    else {
        Write-Host '    validator key: fresh (first run - bootstrap files will be written)' -ForegroundColor DarkGray
    }

    # Launch the seed DETACHED (Win32_Process.Create) so it survives the caller
    # being killed (a CI/tool timeout tears down the whole job tree; a seed
    # dying mid-probe looks exactly like "tx not mined"). Env vars are baked
    # into a generated cmd file because WMI-spawned processes do not inherit
    # this shell's modified environment block.
    $seedCmd = Join-Path $artifacts 'seed-run.cmd'
    @"
@echo off
set PF_SeedNode__StorePath=$($StorePath -replace '/', '\\')
set PF_SeedNode__PrivateKeyHex=$env:PF_SeedNode__PrivateKeyHex
cd /d "$repoRoot"
dotnet run --project src\ProjectF.SeedNode --no-build >"$($seedLog -replace '/', '\\')" 2>"$($seedErr -replace '/', '\\')"
"@ | Set-Content -Path $seedCmd -Encoding ASCII
    $null = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "cmd.exe /c `"$seedCmd`"" }

    # Wait for the swarm port (chain load can take a while on a long chain).
    $deadline = (Get-Date).AddSeconds(120)
    do {
        Start-Sleep -Seconds 3
        $seedPid = Get-ListeningPid $SeedPort
    } while (-not $seedPid -and (Get-Date) -lt $deadline)

    if (-not $seedPid) {
        Write-Host '    seed log tail:' -ForegroundColor Yellow
        Get-Content $seedLog -Tail 5 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "    $_" }
        Get-Content $seedErr -Tail 10 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw "seed node did not open port $SeedPort within 120s"
    }
    Write-Host "    seed up (PID $seedPid)" -ForegroundColor Green
    Get-Content $seedLog | Select-String 'Genesis block|Peer string' | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

    # ---- 3. Refresh assets + compile gate -------------------------------------------
    # Refresh FIRST (so Probe.cs edits compile and the domain settles) — launching
    # before a pending reload just burns attempt #1 on a 503.
    Write-Host '==> [3/5] refreshing assets (picks up Probe.cs edits)…' -ForegroundColor Cyan
    $null = Start-Process -FilePath $npxCmd.Source `
        -ArgumentList 'unity-mcp-cli', 'run-tool', 'assets-refresh', $McpProjectArg `
        -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput (Join-Path $artifacts 'mcp-refresh.out') `
        -RedirectStandardError (Join-Path $artifacts 'mcp-refresh.err')
    Start-Sleep -Seconds 30   # domain reload + compile settle

    # Fail fast if Probe.cs broke compilation: the editor keeps running the
    # LAST GOOD assembly, so a probe launched now would silently exercise
    # stale code (this exact trap cost a full probe cycle once).
    $refreshOut = Get-Content (Join-Path $artifacts 'mcp-refresh.out') -Raw -ErrorAction SilentlyContinue
    if ($refreshOut -match 'compilation errors exist') {
        $msg = ($refreshOut -split "`n" | Select-String 'error CS' | Select-Object -First 3) -join "`n"
        throw "Probe compile failed - fix before probing:`n$msg"
    }

    # ---- 4. Fire the probe via MCP ---------------------------------------------------
    Write-Host '==> [4/5] firing Unity network probe (MCP script-execute)' -ForegroundColor Cyan
    if (-not (Test-Path $launcherArgs)) {
        throw "missing $launcherArgs - the MCP launcher input (Stage 2.5 artifact)"
    }

    # The launcher embeds the probe's store dir; parameterize it so -StorePath
    # controls BOTH the seed and the probe (default template path otherwise).
    # Each run gets its own result file: a stale probe left over from a timed-out
    # CLI call can otherwise write the shared file and mask the live run.
    $storeNorm = ($StorePath -replace '\\', '/').TrimEnd('/')
    $runId = Get-Date -Format 'HHmmss'
    $perRunArgs = Join-Path $artifacts "net-probe-args-$runId.json"
    $resultFile = Join-Path $artifacts "net-probe-result-$runId.json"
    $launcherJson = (Get-Content $launcherArgs -Raw) `
        -replace [regex]::Escape('C:/Users/memph/AppData/Local/Temp/pf-seed'), $storeNorm `
        -replace [regex]::Escape('artifacts/net-probe-result.json'), ("artifacts/net-probe-result-$runId.json")
    if ($launcherJson -notmatch [regex]::Escape($storeNorm)) {
        Write-Host '    WARNING: store path placeholder not found in launcher - probe may read the wrong store' -ForegroundColor Yellow
    }
    Set-Content -Path $perRunArgs -Value $launcherJson -Encoding UTF8
    Remove-Item $resultFile -ErrorAction SilentlyContinue

    $launched = $false
    foreach ($attempt in 1..3) {
        $cli = Start-Process -FilePath $npxCmd.Source `
            -ArgumentList 'unity-mcp-cli', 'run-tool', 'script-execute', $McpProjectArg, '--input-file', $perRunArgs `
            -NoNewWindow -Wait -PassThru `
            -RedirectStandardOutput (Join-Path $artifacts 'mcp-cli.out') `
            -RedirectStandardError (Join-Path $artifacts 'mcp-cli.err')
        if ($cli.ExitCode -eq 0) { $launched = $true; break }
        Write-Host "    attempt $attempt failed (exit $($cli.ExitCode)) - retrying in 45s (editor may be reloading)" -ForegroundColor Yellow
        Start-Sleep -Seconds 45
    }
    if (-not $launched) { throw 'MCP CLI failed 3 times - is the Unity editor open with the MCP plugin running?' }

    # ---- 5. Poll the result file ----------------------------------------------------
    Write-Host '==> [5/5] waiting for the probe to finish (preload of a long chain is SLOW)' -ForegroundColor Cyan
    $resultDeadline = (Get-Date).AddSeconds($ProbeTimeoutSeconds)
    $lastProgress = ''
    while ((Get-Date) -lt $resultDeadline) {
        Start-Sleep -Seconds 15
        if (Test-Path $resultFile) {
            try { $result = Get-Content $resultFile -Raw | ConvertFrom-Json } catch { continue }
            if ($result.status -ne 'started') { break }
        }
        # Show progress lines from the editor log so the wait is not blind.
        $probe = Select-String -Path "$env:LOCALAPPDATA/Unity/Editor/Editor.log" -Pattern 'preload .*BlockCount|baseline counter|final counter' -ErrorAction SilentlyContinue |
            Select-Object -Last 1
        if ($probe -and $probe.Line -ne $lastProgress) {
            $lastProgress = $probe.Line
            Write-Host "    $($probe.Line)" -ForegroundColor DarkGray
        }
    }

    if (-not (Test-Path $resultFile)) { throw 'probe produced no result file within the deadline' }
    $result = Get-Content $resultFile -Raw | ConvertFrom-Json
    if ($result.status -eq 'error') { throw "probe failed: $($result.error)" }
    if ($result.status -ne 'done') { throw 'probe timed out (result file still says started)' }

    $increased = $result.counter -gt $result.baseline
    Write-Host ''
    if ($increased) {
        Write-Host ('  STAGE 2.5 SPIKE: PASS   counter {0} -> {1} (delta +{2})' -f `
            $result.baseline, $result.counter, ($result.counter - $result.baseline)) -ForegroundColor Green
    }
    else {
        Write-Host ('  STAGE 2.5 SPIKE: FAIL   counter {0} -> {1} (no increase - tx not mined/synced)' -f `
            $result.baseline, $result.counter) -ForegroundColor Red
        Write-Host '  hints:' -ForegroundColor Yellow
        Write-Host '   - grep "txs: 1" in the seed log to see whether the seed mined the tx'
        Write-Host '   - check ~/AppData/Local/Unity/Editor/Editor.log for [net-probe] lines'
    }
    Write-Host ''
    $exitCode = if ($increased) { 0 } else { 2 }
}
catch {
    Write-Host ''
    Write-Host "  NETPROBE ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ''
    $exitCode = 1
}
finally {
    if (-not $KeepSeed) { Stop-SeedNode }
    Write-Host ("total wall clock: {0:mm\:ss}" -f $overallStopwatch.Elapsed) -ForegroundColor DarkGray
}
exit $exitCode
