#Requires -Version 7
<#
.SYNOPSIS
  Stage 5 + 6 proof in ONE command: start a seed node + a follower node and
  watch their tips converge, then boot the presence server and drive the
  two-client OnMove/OnLeave integration tests against it.

.DESCRIPTION
  Automates the two-node reproduction the Stage 5 spec asked for, plus the
  Stage 6 presence proof, in one run:
    1. dotnet build (ProjectF.SeedNode) unless -SkipBuild
    2. Start the SEED (validator) node with a script-managed key
       (artifacts/validator.key — the SAME key file run-netprobe.ps1 uses, so
       both scripts share one chain identity; -FreshSeed wipes the store).
    3. Wait for the seed's bootstrap files (genesis.dat / peer.txt / apv.txt).
    4. Start a FOLLOWER node: its OWN key, IsMiner=false, fed entirely from
       the seed's bootstrap files. It never needs the validator's key.
    5. Watch both nodes' [status] heartbeats until the follower has appended
       the same tip block hash as the seed (converged) or -DurationSeconds.
    6. STAGE 6: build the full solution (unless already built), start
       ProjectF.HubServer on 127.0.0.1:5170, and run the presence integration
       tests with PF_HUB_URL pointed at it (real gRPC, two fake clients).
    7. Combined verdict + cleanup (all three processes stopped unless
       -KeepNodes).

  PASS = both nodes report Peers: 1 and the follower's last appended block
  hash equals the seed's at the same index — i.e. "discover each other via
  the peer string and stay at the same block height" — AND the presence
  integration tests pass against the live hub.

.EXAMPLE
  pwsh scripts/run-two-nodes.ps1                 # build + fresh run + presence
  pwsh scripts/run-two-nodes.ps1 -SkipBuild      # nodes already built
  pwsh scripts/run-two-nodes.ps1 -FreshSeed      # wipe the seed store first
  pwsh scripts/run-two-nodes.ps1 -KeepNodes      # leave everything running
  pwsh scripts/run-two-nodes.ps1 -SkipPresence   # chain proof only (Stage 5)
#>
param(
    [int]$DurationSeconds = 60,
    [int]$SeedPort = 31236,
    [int]$FollowerPort = 31237,
    [int]$HubPort = 5170,
    [switch]$SkipBuild,
    [switch]$FreshSeed,
    [switch]$KeepNodes,
    [switch]$SkipPresence
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$seedLog = Join-Path $artifacts 'two-nodes-seed.log'
$followerLog = Join-Path $artifacts 'two-nodes-follower.log'
$hubLog = Join-Path $artifacts 'two-nodes-hub.log'
$seedStore = Join-Path $env:TEMP 'pf-seed-2nodes'

function Get-ListeningPid([int]$Port) {
    $line = netstat -ano | Select-String ":$Port\s.*LISTENING" | Select-Object -First 1
    if ($line) { return [int]($line.ToString().Trim() -split '\s+')[-1] }
    return $null
}

function Stop-PortOwner([int]$Port, [string]$Label) {
    $procId = Get-ListeningPid $Port
    if ($procId) {
        Write-Host "    stopping stale $Label on port $Port (PID $procId)" -ForegroundColor Yellow
        Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
}

function Get-LastStatus([string]$LogPath) {
    # Last "[status] Peers: X, Tip: #Y" line the node printed.
    $m = Select-String -Path $LogPath -Pattern '\[status\] Peers: (\d+), Tip: #(\d+)' -ErrorAction SilentlyContinue |
        Select-Object -Last 1
    if ($m) { return [pscustomobject]@{ Peers = [int]$m.Matches[0].Groups[1].Value; Tip = [int]$m.Matches[0].Groups[2].Value } }
    return $null
}

function Get-LastAppended([string]$LogPath) {
    # Last "Appended the block #N <hash>" line (Libplanet INFO log).
    $m = Select-String -Path $LogPath -Pattern 'Appended the block #(\d+) ([0-9a-f]{64})' -ErrorAction SilentlyContinue |
        Select-Object -Last 1
    if ($m) { return [pscustomobject]@{ Index = [int]$m.Matches[0].Groups[1].Value; Hash = $m.Matches[0].Groups[2].Value } }
    return $null
}

function Show-Tail([string]$LogPath, [int]$Lines = 6) {
    Get-Content $LogPath -Tail $Lines -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$exitCode = 1
$chainPass = $false
$presencePass = $false
try {
    # ---- 1. Build -------------------------------------------------------------
    if (-not $SkipBuild) {
        Write-Host '==> [1/6] dotnet build (ProjectF.SeedNode)' -ForegroundColor Cyan
        dotnet build (Join-Path $repoRoot 'src/ProjectF.SeedNode/ProjectF.SeedNode.csproj') --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
    }
    else {
        Write-Host '==> [1/6] build skipped (-SkipBuild)' -ForegroundColor DarkGray
    }

    # ---- 2. Seed node ---------------------------------------------------------
    Write-Host "==> [2/6] starting SEED (store: $seedStore, port: $SeedPort)" -ForegroundColor Cyan
    Stop-PortOwner $SeedPort 'seed node'
    if ($FreshSeed) {
        Write-Host '    -FreshSeed: wiping the seed store' -ForegroundColor Yellow
        Remove-Item $seedStore -Recurse -Force -ErrorAction SilentlyContinue
    }

    $keyFile = Join-Path $artifacts 'validator.key'
    $keyHex = if (Test-Path $keyFile) {
        (Get-Content $keyFile -Raw).Trim()
    }
    else {
        # First run: generate once and persist (the SeedNode no longer writes
        # privkey.txt; key stability is the scripts' job). Same file as
        # run-netprobe.ps1 — one validator identity across all scripts.
        $b = New-Object byte[] 32
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($b)
        ([System.BitConverter]::ToString($b) -replace '-', '').ToLowerInvariant() |
            Set-Content -Path $keyFile -NoNewline -Encoding ASCII
        Write-Host "    validator key generated -> $keyFile" -ForegroundColor DarkGray
        (Get-Content $keyFile -Raw).Trim()
    }

    $env:PF_SeedNode__StorePath = $seedStore
    $env:PF_SeedNode__Port = $SeedPort
    $env:PF_SeedNode__PrivateKeyHex = $keyHex
    $env:PF_SeedNode__IsMiner = 'true'
    $seedProc = Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run', '--project', (Join-Path $repoRoot 'src/ProjectF.SeedNode'), '--no-build' `
        -NoNewWindow -PassThru `
        -RedirectStandardOutput $seedLog -RedirectStandardError "$seedLog.err"

    # Readiness = the swarm PORT is bound. The bootstrap files are written
    # just before the transport binds, so on a warm store they already exist
    # from a previous run — file existence alone is not a readiness signal.
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline -and -not (Get-ListeningPid $SeedPort)) {
        Start-Sleep -Seconds 2
        if ($seedProc.HasExited) { throw "seed exited during startup (code $($seedProc.ExitCode))" }
    }
    if (-not (Get-ListeningPid $SeedPort)) {
        Write-Host '    seed log tail:' -ForegroundColor Yellow
        Show-Tail $seedLog 10
        throw "seed never opened port $SeedPort"
    }
    # Port is up; make sure the file set is complete too (same boot wrote them).
    $filesDeadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $filesDeadline -and -not (
        (Test-Path (Join-Path $seedStore 'peer.txt')) -and
        (Test-Path (Join-Path $seedStore 'apv.txt')) -and
        (Test-Path (Join-Path $seedStore 'genesis.dat')))) {
        Start-Sleep -Milliseconds 500
    }

    $peerString = (Get-Content (Join-Path $seedStore 'peer.txt') -Raw).Trim()
    $apvToken = (Get-Content (Join-Path $seedStore 'apv.txt') -Raw).Trim()
    Write-Host "    seed up (PID $(Get-ListeningPid $SeedPort))" -ForegroundColor Green
    Write-Host "    peer string: $peerString" -ForegroundColor DarkGray

    # ---- 3. Follower node -----------------------------------------------------
    Write-Host "==> [3/6] starting FOLLOWER (own key, IsMiner=false, port: $FollowerPort)" -ForegroundColor Cyan
    Stop-PortOwner $FollowerPort 'follower node'
    # Driven entirely by the seed's bootstrap files: genesis.dat (chain), apv.txt
    # (network identity), peer.txt (discovery). NO validator key required.
    $env:PF_SeedNode__StorePath = "$seedStore-follower"
    $env:PF_SeedNode__Port = $FollowerPort
    $env:PF_SeedNode__IsMiner = 'false'
    $env:PF_SeedNode__GenesisPath = "$seedStore/genesis.dat"
    $env:PF_SeedNode__ApvToken = $apvToken
    $env:PF_SeedNode__StaticPeers = $peerString
    $env:PF_SeedNode__PrivateKeyHex = ''   # follower must not inherit the validator key
    $followerProc = Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run', '--project', (Join-Path $repoRoot 'src/ProjectF.SeedNode'), '--no-build' `
        -NoNewWindow -PassThru `
        -RedirectStandardOutput $followerLog -RedirectStandardError "$followerLog.err"

    # ---- 4. Watch convergence ---------------------------------------------------
    Write-Host "==> [4/6] watching tips converge (up to ${DurationSeconds}s)…" -ForegroundColor Cyan
    Write-Host '    (seed mines; follower must discover, sync, and append the same hashes)' -ForegroundColor DarkGray
    $watchDeadline = (Get-Date).AddSeconds($DurationSeconds)
    $converged = $false
    $lastLine = ''
    while ((Get-Date) -lt $watchDeadline) {
        Start-Sleep -Seconds 4

        if ($followerProc.HasExited) {
            Write-Host '    follower log tail:' -ForegroundColor Yellow
            Show-Tail $followerLog 12
            throw "follower exited early (code $($followerProc.ExitCode))"
        }
        if ($seedProc.HasExited) {
            throw "seed exited early (code $($seedProc.ExitCode))"
        }

        $s = Get-LastStatus $seedLog
        $f = Get-LastStatus $followerLog
        $line = "seed: Peers $($s?.Peers ?? 0) tip #$($s?.Tip ?? -1)   |   follower: Peers $($f?.Peers ?? 0) tip #$($f?.Tip ?? -1)"
        if ($line -ne $lastLine) {
            $lastLine = $line
            Write-Host "    $line" -ForegroundColor DarkGray
        }

        # Converged = both connected AND the follower's last appended block is
        # the seed's last appended block (same index AND same hash).
        $sa = Get-LastAppended $seedLog
        $fa = Get-LastAppended $followerLog
        if ($s -and $f -and $s.Peers -ge 1 -and $f.Peers -ge 1 -and $sa -and $fa -and
            $sa.Index -eq $fa.Index -and $sa.Hash -eq $fa.Hash) {
            $converged = $true
            break
        }
    }

    # ---- 5. Verdict -----------------------------------------------------------
    Write-Host ''
    if ($converged) {
        $chainPass = $true
        Write-Host '  STAGE 5 TWO-NODES: PASS' -ForegroundColor Green
        Write-Host '  both nodes appended the same tip block:' -ForegroundColor Green
        $sa = Get-LastAppended $seedLog
        $fa = Get-LastAppended $followerLog
        Write-Host ('    seed:     #{0} {1}' -f $sa.Index, $sa.Hash) -ForegroundColor Green
        Write-Host ('    follower: #{0} {1}' -f $fa.Index, $fa.Hash) -ForegroundColor Green
    }
    else {
        Write-Host '  STAGE 5 TWO-NODES: FAIL (tips did not converge in time)' -ForegroundColor Red
        Write-Host '  hints:' -ForegroundColor Yellow
        Write-Host '   - follower log: artifacts/two-nodes-follower.log (look for "BootstrapAsync failed")'
        Write-Host '   - a follower joining a LONG chain needs catch-up time; try -FreshSeed for a short chain'
        Show-Tail $followerLog 8
    }
    Write-Host ''

    # ---- 6. Stage 6: presence proof -------------------------------------------
    if ($SkipPresence) {
        Write-Host '==> [6/6] presence proof skipped (-SkipPresence)' -ForegroundColor DarkGray
        $presencePass = $true   # do not poison the combined verdict
    }
    elseif (-not $chainPass) {
        Write-Host '==> [6/6] presence proof skipped (chain proof failed)' -ForegroundColor DarkGray
        $presencePass = $false
    }
    else {
        try {
            Write-Host '==> [6/6] building solution + presence proof (real gRPC against the live hub)' -ForegroundColor Cyan
            dotnet build (Join-Path $repoRoot 'ProjectF.sln') --nologo -v q
            if ($LASTEXITCODE -ne 0) { throw "dotnet build ProjectF.sln failed ($LASTEXITCODE)" }

            Stop-PortOwner $HubPort 'hub server'
            $env:PF_HUB_URL = "http://127.0.0.1:${HubPort}"
            $hubProc = Start-Process -FilePath 'dotnet' `
                -ArgumentList (Join-Path $repoRoot 'src/ProjectF.HubServer/bin/Debug/net8.0/ProjectF.HubServer.dll') `
                -NoNewWindow -PassThru `
                -RedirectStandardOutput $hubLog -RedirectStandardError "$hubLog.err"

            $hubDeadline = (Get-Date).AddSeconds(30)
            while ((Get-Date) -lt $hubDeadline -and -not (Get-ListeningPid $HubPort)) {
                Start-Sleep -Seconds 1
            }
            if (-not (Get-ListeningPid $HubPort)) {
                Write-Host '    hub log tail:' -ForegroundColor Yellow
                Show-Tail $hubLog 10
                throw "hub server never opened port $HubPort"
            }
            Write-Host "    hub up (PID $(Get-ListeningPid $HubPort))" -ForegroundColor Green

            # The presence tests connect as two fake MagicOnion clients and
            # assert OnMove / OnEmote / OnLeave flows over real gRPC.
            dotnet test (Join-Path $repoRoot 'src/ProjectF.Lib.Tests/ProjectF.Lib.Tests.csproj') `
                --filter 'FullyQualifiedName~PresenceHubIntegrationTests' --nologo -v q
            if ($LASTEXITCODE -eq 0) {
                $presencePass = $true
                Write-Host '  STAGE 6 PRESENCE: PASS (OnMove + OnLeave via live hub)' -ForegroundColor Green
            }
            else {
                Write-Host "  STAGE 6 PRESENCE: FAIL (dotnet test exit $LASTEXITCODE)" -ForegroundColor Red
                Show-Tail $hubLog 8
            }
        }
        catch {
            Write-Host "  STAGE 6 PRESENCE: FAIL - $($_.Exception.Message)" -ForegroundColor Red
        }
    }

    Write-Host ''
    if ($chainPass -and $presencePass) {
        Write-Host '  COMBINED: PASS  (Stage 5 chain + Stage 6 presence)' -ForegroundColor Green
        $exitCode = 0
    }
    else {
        Write-Host ('  COMBINED: FAIL  (chain: {0}, presence: {1})' -f `
            (@{$true='PASS';$false='FAIL'}[$chainPass]), (@{$true='PASS';$false='FAIL'}[$presencePass])) -ForegroundColor Red
        $exitCode = if ($chainPass) { 2 } else { 3 }
    }
    Write-Host ''
}
catch {
    Write-Host ''
    Write-Host "  TWO-NODES ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ''
    $exitCode = 1
}
finally {
    if (-not $KeepNodes) {
        foreach ($p in @($SeedPort, $FollowerPort, $HubPort)) {
            $procId = Get-ListeningPid $p
            if ($procId) {
                Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
                Write-Host "==> stopped process on port $p (PID $procId)" -ForegroundColor DarkGray
            }
        }
        Remove-Item Env:PF_HUB_URL -ErrorAction SilentlyContinue
    }
    Write-Host ("total wall clock: {0:mm\:ss}" -f $sw.Elapsed) -ForegroundColor DarkGray
}
exit $exitCode
