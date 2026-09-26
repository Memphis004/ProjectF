# run-two-nodes.ps1 - two-node peer sync test for planet-clicker
#
# Usage (PowerShell, from repo root):
#   powershell -ExecutionPolicy Bypass -File planet-clicker\run-two-nodes.ps1
#
# Optional parameters (see param block below) let you reuse existing keys or
# change ports/durations. The script:
#   1. Generates two distinct secp256k1 private keys (openssl or .NET fallback).
#   2. Launches Node A (seed + miner) from the existing build.
#   3. Waits for A's swarm address, then launches Node B with --peer pointing at A.
#      B's storage is seeded with a snapshot of A's storage to bypass Libplanet's
#      ChainIdNotFoundException on genesis-only preload forks (see SIGN-IN.md).
#   4. B auto-clicks (--auto-click) so it keeps sending AddCount transactions.
#   5. Polls A's log for B's transactions being executed and B's log for synced
#      blocks, then reports PASS/FAIL and leaves both logs for diagnosis.

param(
    [string]$BuildExe = "C:\UnityProjects\planet-clicker\planet-clicker\Build\Windows\PlanetClicker.exe",
    [string]$WorkDir  = "C:\UnityProjects\planet-clicker",
    [int]$PortA = 39710,
    [int]$PortB = 39711,
    [int]$SeedWaitSeconds = 25,
    [int]$SyncWaitSeconds = 90
)

$ErrorActionPreference = "Stop"

function New-PrivateKeyHex {
    # Try openssl first; fall back to .NET RNG (32 random bytes are a valid key).
    try {
        $pem = & openssl ecparam -name secp256k1 -genkey -noout 2>$null
        if ($LASTEXITCODE -eq 0 -and $pem) {
            $hex = (& openssl ec -in - -text -noout 2>$null <<< ($pem -join "`n") |
                Select-String -Pattern "priv:" -Context 0,4)
            if ($hex) {
                $bytes = ($hex -join "") -replace "[^0-9a-fA-F]", ""
                if ($bytes.Length -ge 64) { return $bytes.Substring($bytes.Length - 64, 64).ToLower() }
            }
        }
    } catch { }

    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $buf = New-Object byte[] 32
    $rng.GetBytes($buf)
    return (($buf | ForEach-Object { $_.ToString("x2") }) -join "")
}

Write-Host "=== planet-clicker two-node peer test ===" -ForegroundColor Cyan

# --- 0. Preconditions -------------------------------------------------------
if (-not (Test-Path $BuildExe)) {
    Write-Host "FAIL: build not found at $BuildExe (build the player first)." -ForegroundColor Red
    exit 1
}

$buildDir  = Split-Path $BuildExe -Parent
$logA      = Join-Path $WorkDir "pc_test_A.log"
$logB      = Join-Path $WorkDir "pc_test_B.log"
$storeA    = Join-Path $WorkDir "pc_test_A_storage"
$storeB    = Join-Path $WorkDir "pc_test_B_storage"

# Kill leftovers from previous runs.
Get-Process PlanetClicker -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

foreach ($f in @($logA, $logB)) { if (Test-Path $f) { Remove-Item $f -Force } }
foreach ($d in @($storeA, $storeB)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }

# --- 1. Distinct keys --------------------------------------------------------
$keyA = New-PrivateKeyHex
$keyB = New-PrivateKeyHex
if ($keyA -eq $keyB) { Write-Host "FAIL: key generation collided (improbably)." -ForegroundColor Red; exit 1 }
Write-Host ("Node A key: {0}" -f $keyA.Substring(0, 16) + "...")
Write-Host ("Node B key: {0}" -f $keyB.Substring(0, 16) + "...")

# --- 2. Node A: seed + miner --------------------------------------------------
Push-Location $buildDir
Start-Process -FilePath ".\PlanetClicker.exe" -ArgumentList @(
    "--private-key", $keyA, "--port", "$PortA",
    "--storage-path", $storeA, "-logFile", $logA, "-batchmode", "-nographics"
) -WindowStyle Hidden
Pop-Location
Write-Host "Node A launched (seed + miner). Waiting $SeedWaitSeconds s for swarm..."
Start-Sleep -Seconds $SeedWaitSeconds

$addrLine = Select-String -Path $logA -Pattern "The address of this node: (\S+),(\S+),(\d+)" |
    Select-Object -Last 1
if (-not $addrLine) {
    Write-Host "FAIL: Node A never printed its swarm address. See $logA" -ForegroundColor Red
    exit 1
}
$peerA = $addrLine.Matches[0].Groups[1].Value
Write-Host "Node A swarm address: $peerA"

# --- 3. Snapshot A's storage so B joins a real chain --------------------------
# PreloadAsync on a genesis-only store fails with ChainIdNotFoundException; giving
# B a copy of A's store makes it join A's actual chain (see SIGN-IN.md).
Copy-Item $storeA $storeB -Recurse
Write-Host "Seeded Node B storage from Node A snapshot."

# --- 4. Node B: no-miner, tx sender -------------------------------------------
Push-Location $buildDir
Start-Process -FilePath ".\PlanetClicker.exe" -ArgumentList @(
    "--private-key", $keyB, "--port", "$PortB", "--no-miner",
    "--peer", "`"$peerA`"",
    "--storage-path", $storeB, "-logFile", $logB,
    "--auto-click", "-batchmode", "-nographics"
) -WindowStyle Hidden
Pop-Location
Write-Host "Node B launched. Waiting up to $SyncWaitSeconds s for sync..."
Start-Sleep -Seconds $SyncWaitSeconds

# --- 5. Verify -----------------------------------------------------------------
function Get-Count([string]$file, [string]$pattern) {
    if (-not (Test-Path $file)) { return 0 }
    return (Select-String -Path $file -Pattern $pattern | Measure-Object).Count
}

$addrA = (Select-String -Path $logA -Pattern "Address: (0x[0-9a-fA-F]+)" | Select-Object -First 1).Matches[0].Groups[1].Value
$addrB = (Select-String -Path $logB -Pattern "Address: (0x[0-9a-fA-F]+)" | Select-Object -First 1).Matches[0].Groups[1].Value

$txsExecutedOnA = Get-Count $logA "add_count"          # B's txs included in A's blocks
$rendersOnB     = Get-Count $logB "add_count"          # B rendering synced blocks
$tipA           = (Select-String -Path $logA -Pattern "created block index: (\d+)" |
                    Select-Object -Last 1).Matches[0].Groups[1].Value
$tipB           = (Select-String -Path $logB -Pattern "created block index: (\d+)" |
                    Select-Object -Last 1)

Write-Host ""
Write-Host "=== RESULTS ===" -ForegroundColor Cyan
Write-Host ("A address: {0}" -f $addrA)
Write-Host ("B address: {0}" -f $addrB)
Write-Host ("Distinct identities : {0}" -f ($addrA -ne $addrB))
Write-Host ("A tip (blocks mined): {0}" -f $tipA)
Write-Host ("B txs executed on A : {0}" -f $txsExecutedOnA)
Write-Host ("B renders of sync   : {0}" -f $rendersOnB)

$pass = ($addrA -ne $addrB) -and ($txsExecutedOnA -gt 0) -and ($rendersOnB -gt 0)
if ($pass) {
    Write-Host "PASS: distinct identities, B's transactions mined by A, B syncing A's blocks." -ForegroundColor Green
    exit 0
} else {
    Write-Host "FAIL: see $logA and $logB for diagnosis." -ForegroundColor Red
    exit 1
}
