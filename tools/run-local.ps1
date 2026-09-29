<#
  ProjectF - run-local.ps1
  รัน 2 process: SeedNode (chain, miner) + HubServer (presence)

  Stage 11.5 fixes:
  - Validator key PIN ไว้ที่ {store}/privkey.txt — key กับ store เป็นคู่เดียวกัน
    (ที่มาของ InvalidGenesisBlockException: รันใหม่ = key ใหม่ = genesis ใหม่ ทั้งที่ store เก่า)
  - Kill seed/hub ตัวเก่าก่อนรันเสมอ (port 31237/5170 ถูกยึด = ตายเงียบ ๆ)
  - รอ peer.txt จริง (แทน sleep ดื้อ ๆ) ก่อนสตาร์ท hub
  - Patch NetworkSettings.asset ของ Unity ให้ตรงกับ seed ปัจจุบันอัตโนมัติ
  - ใช้ -Reset ถ้าอยากล้าง store (genesis ใหม่จากศูนย์)
#>

param(
    [int]$SeedPort = 31237,
    [int]$HubPort = 5170,
    [string]$StorePath = "$PSScriptRoot/../.freebuff/chain",
    [switch]$Reset
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot/.."

$seedProject = "$root/src/ProjectF.SeedNode/ProjectF.SeedNode.csproj"
$hubProject = "$root/src/ProjectF.HubServer/ProjectF.HubServer.csproj"
$unitySettings = "$root/UnityProject/Assets/Main/Settings/NetworkSettings.asset"

foreach ($proj in @($seedProject, $hubProject)) {
    if (-not (Test-Path $proj)) {
        Write-Host "ไม่เจอ $proj" -ForegroundColor Red
        exit 1
    }
}

# ---------------------------------------------------------------
# 1. Kill ตัวเก่าที่อาจยึด port ค้างไว้
# ---------------------------------------------------------------
foreach ($port in @($SeedPort, $HubPort)) {
    $conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    foreach ($c in $conns) {
        Write-Host ("port {0} ถูก PID {1} ยึดอยู่ — kill ก่อน" -f $port, $c.OwningProcess) -ForegroundColor Yellow
        try { Stop-Process -Id $c.OwningProcess -Force -ErrorAction SilentlyContinue } catch {}
    }
}
# dotnet build-server ที่อาจล็อกไฟล์
try { dotnet build-server shutdown 2>$null | Out-Null } catch {}

# ---------------------------------------------------------------
# 2. Reset ถ้าสั่ง
# ---------------------------------------------------------------
if ($Reset -and (Test-Path $StorePath)) {
    Write-Host "ล้าง store: $StorePath" -ForegroundColor Yellow
    Remove-Item -Recurse -Force $StorePath
}

# ---------------------------------------------------------------
# 3. Validator key PIN กับ store — หัวใจของการกัน genesis mismatch
# ---------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $StorePath | Out-Null
$privKeyFile = Join-Path $StorePath "privkey.txt"
if (Test-Path $privKeyFile) {
    $nodeKey = (Get-Content $privKeyFile -Raw).Trim()
    Write-Host "ใช้ validator key เดิมจาก privkey.txt (genesis คงเดิม)" -ForegroundColor Gray
} else {
    # สร้าง key ใหม่ "ครั้งเดียว" ด้วย dotnet (Secp256k1) แล้วเก็บลงไฟล์
    Write-Host "สร้าง validator key ใหม่ (ครั้งแรกของ store นี้)..." -ForegroundColor Gray
    $keyGen = dotnet run --project "`"$seedProject`"" -- --SeedNode:GenerateKeyHexOnly 2>$null | Select-Object -Last 1
    $nodeKey = $keyGen.Trim()
    if ($nodeKey -notmatch '^[0-9a-fA-F]{64}$') {
        Write-Host "สร้าง key ไม่สำเร็จ: $keyGen" -ForegroundColor Red
        exit 1
    }
    Set-Content -Path $privKeyFile -Value $nodeKey -NoNewline
    Write-Host "เก็บ key ไว้ที่ $privKeyFile" -ForegroundColor Gray
}

# ---------------------------------------------------------------
# 4. Start SeedNode (รอ peer.txt จริง)
# ---------------------------------------------------------------
Write-Host "== [1/2] SeedNode (chain) ==" -ForegroundColor Cyan
$seed = Start-Process dotnet -ArgumentList @("run", "--project", "`"$seedProject`"", "--",
    "--SeedNode:Port=$SeedPort",
    "--SeedNode:StorePath=`"$StorePath`"",
    "--SeedNode:IsMiner=true",
    "--SeedNode:PrivateKeyHex=$nodeKey") `
    -WorkingDirectory "$root/src/ProjectF.SeedNode" -PassThru -NoNewWindow
$procs = @($seed)

$peerFile = Join-Path $StorePath "peer.txt"
$deadline = (Get-Date).AddSeconds(90)
$peerReady = $false
while ((Get-Date) -lt $deadline) {
    if ($seed.HasExited) {
        Write-Host "SeedNode ตายก่อนพร้อม (code $($seed.ExitCode)) — ดู log ด้านบน" -ForegroundColor Red
        exit 1
    }
    if ((Test-Path $peerFile) -and (Get-Item $peerFile).LastWriteTime -gt (Get-Date).AddMinutes(-2)) {
        $peerReady = $true
        break
    }
    Start-Sleep -Milliseconds 800
}
if (-not $peerReady) {
    Write-Host "รอ peer.txt นานเกิน 90s — ยกเลิก" -ForegroundColor Red
    exit 1
}
Write-Host ("SeedNode พร้อม: PID {0}  port {1}" -f $seed.Id, $SeedPort) -ForegroundColor Green

# ---------------------------------------------------------------
# 5. Patch NetworkSettings.asset (peer + APV) ให้ตรง seed ปัจจุบัน
# ---------------------------------------------------------------
try {
    $peer = (Get-Content $peerFile -Raw).Trim()
    $apv = (Get-Content (Join-Path $StorePath "apv.txt") -Raw).Trim()
    $settings = Get-Content $unitySettings -Raw
    $patched = $settings -replace '(?m)^  - [0-9a-f]{40,},[0-9.]+,\d+$', "  - $peer"
    $patched = $patched -replace '(?m)^  ApvToken: .*$', "  ApvToken: $apv"
    if ($patched -ne $settings) {
        # Unity ต้องการ BOM — เขียนกลับแบบเดิม
        [System.IO.File]::WriteAllText($unitySettings, $patched, (New-Object System.Text.UTF8Encoding($true)))
        Write-Host "patch NetworkSettings.asset (peer/APV) แล้ว — Unity จะ refresh เอง" -ForegroundColor Gray
    }
} catch {
    Write-Host "patch settings ไม่สำเร็จ (ข้าม): $($_.Exception.Message)" -ForegroundColor DarkYellow
}

# ---------------------------------------------------------------
# 6. Start HubServer
# ---------------------------------------------------------------
Write-Host "== [2/2] HubServer (presence) ==" -ForegroundColor Cyan
$hub = Start-Process dotnet -ArgumentList @("run", "--project", "`"$hubProject`"") `
    -WorkingDirectory "$root/src/ProjectF.HubServer" -PassThru -NoNewWindow
$procs += $hub

Write-Host ""
Write-Host ("SeedNode : PID {0}  (port {1})" -f $seed.Id, $SeedPort) -ForegroundColor Green
Write-Host ("HubServer: PID {0}  (127.0.0.1:{1})" -f $hub.Id, $HubPort) -ForegroundColor Green
Write-Host "peer.txt : $peerFile" -ForegroundColor Gray
Write-Host "กด Ctrl+C เพื่อปิดทั้งคู่" -ForegroundColor Magenta
Write-Host ""

while ($true) {
    Start-Sleep -Seconds 1
    foreach ($proc in $procs) {
        if ($proc.HasExited) {
            Write-Host ("PID {0} ออกเอง (code {1}) — ปิดที่เหลือให้" -f $proc.Id, $proc.ExitCode) -ForegroundColor Yellow
            exit 1
        }
    }
}
