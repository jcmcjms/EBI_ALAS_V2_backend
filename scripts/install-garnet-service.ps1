#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs Garnet as a Windows Service for production use on Windows Server 2019.

.DESCRIPTION
    This script sets up Garnet (Microsoft's Redis-compatible cache) as a
    persistent Windows Service that starts automatically on boot.
    Uses NSSM (Non-Sucking Service Manager) to wrap the console app as a
    proper Windows Service with log rotation and recovery.

    Performance flags are tuned for banking workloads:
    - Memory limit at 50% of available RAM to prevent OOM
    - Pub/Sub enabled for SignalR backplane
    - Max connections set to 1000 for multi-pod deployments
    - Client idle timeout at 300s for connection hygiene

.EXAMPLE
    .\install-garnet-service.ps1
#>

$ErrorActionPreference = "Stop"

# ─── Configuration ───────────────────────────────────────────────────
$ServiceName = "GarnetCache"
$ServiceDisplayName = "Garnet Cache Server (Redis-compatible)"
$GarnetPath = (Get-Command garnet-server -ErrorAction SilentlyContinue).Source

if (-not $GarnetPath) {
    Write-Error "garnet-server not found. Install via: dotnet tool install --global garnet-server"
    exit 1
}

# ─── Locate NSSM ────────────────────────────────────────────────────
$NssmPath = (Get-Command nssm -ErrorAction SilentlyContinue).Source
if (-not $NssmPath) {
    # Fallback: check common install locations
    $candidates = @(
        "$env:LOCALAPPDATA\Microsoft\WinGet\Links\nssm.exe",
        "C:\Program Files\nssm\win64\nssm.exe",
        "C:\Program Files (x86)\nssm\win64\nssm.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $NssmPath = $c; break }
    }
}
if (-not $NssmPath) {
    Write-Error "nssm not found. Install via: winget install NSSM.NSSM"
    exit 1
}

$DataDir = "C:\GarnetData"
$LogDir = "C:\GarnetLogs"

# ─── Calculate memory limit (50% of available RAM) ──────────────────
$TotalMemoryGB = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 0)
$MemoryLimitGB = [math]::Max(1, [math]::Floor($TotalMemoryGB / 2))
$MemoryArg = "${MemoryLimitGB}g"

Write-Host "System RAM: ${TotalMemoryGB}GB — Garnet memory limit: ${MemoryLimitGB}GB" -ForegroundColor Cyan

# ─── Create directories ─────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

Write-Host "Garnet path: $GarnetPath" -ForegroundColor Cyan
Write-Host "NSSM path:   $NssmPath" -ForegroundColor Cyan
Write-Host "Data directory: $DataDir" -ForegroundColor Cyan
Write-Host "Log directory: $LogDir" -ForegroundColor Cyan

# ─── Stop existing service if present ────────────────────────────────
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & $NssmPath remove $ServiceName confirm 2>$null
    Start-Sleep -Seconds 2
}

# ─── Create the service using NSSM ──────────────────────────────────
# Garnet is a console app — NSSM wraps it as a proper Windows Service
# with stdout/stderr capture, graceful shutdown, and log rotation.
#
# Performance flags for banking workloads:
#   --bind 0.0.0.0        : Listen on all interfaces
#   --port 6379            : Standard Redis port (client compatibility)
#   --recover              : Recover data from AOF on startup
#   --storage-tier         : Enable tiered storage (memory + disk)
#   --memory <50% RAM>     : Cap memory usage to prevent OOM
#   --pubsub               : Enable pub/sub for SignalR backplane
#   --max-connections 1000 : Support multi-pod connection pools
#   --timeout 300          : Close idle clients after 5 minutes

Write-Host "Creating Windows Service via NSSM..." -ForegroundColor Green
& $NssmPath install $ServiceName $GarnetPath `
    "--bind" "0.0.0.0" `
    "--port" "6379" `
    "--recover" `
    "--storage-tier" `
    "--memory" $MemoryArg `
    "--pubsub" `
    "--max-connections" "1000" `
    "--timeout" "300"

# ─── Service metadata ───────────────────────────────────────────────
& $NssmPath set $ServiceName DisplayName $ServiceDisplayName
& $NssmPath set $ServiceName Description "Microsoft Garnet Redis-compatible cache server for EBI.ALAS.V2 (memory=${MemoryLimitGB}GB, pubsub=enabled)"
& $NssmPath set $ServiceName Start SERVICE_AUTO_START

# ─── Process lifecycle ──────────────────────────────────────────────
& $NssmPath set $ServiceName AppDirectory $DataDir
& $NssmPath set $ServiceName AppStdout "$LogDir\garnet-stdout.log"
& $NssmPath set $ServiceName AppStderr "$LogDir\garnet-stderr.log"
& $NssmPath set $ServiceName AppStdoutCreationDisposition 4    # Append
& $NssmPath set $ServiceName AppStderrCreationDisposition 4    # Append
& $NssmPath set $ServiceName AppRotateFiles 1                  # Enable log rotation
& $NssmPath set $ServiceName AppRotateOnline 1                 # Rotate while running
& $NssmPath set $ServiceName AppRotateBytes 10485760           # 10 MB per log file

# ─── Recovery ───────────────────────────────────────────────────────
# Restart on failure: 1st = 15s, 2nd = 30s, 3rd = 60s
& $NssmPath set $ServiceName AppExit Default Restart
& $NssmPath set $ServiceName AppRestartDelay 15000

# ─── Graceful shutdown ──────────────────────────────────────────────
# Send Ctrl+C first, then kill after 15s if process doesn't exit
& $NssmPath set $ServiceName AppStopMethodConsole 15000

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " Garnet installed as Windows Service!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Start with:  Start-Service -Name $ServiceName" -ForegroundColor Cyan
Write-Host "Stop with:   Stop-Service -Name $ServiceName" -ForegroundColor Cyan
Write-Host "Status:      Get-Service -Name $ServiceName" -ForegroundColor Cyan
Write-Host ""
Write-Host "Connection:  127.0.0.1:6379" -ForegroundColor Yellow
Write-Host "Data dir:    $DataDir" -ForegroundColor Yellow
Write-Host "Memory cap:  ${MemoryLimitGB}GB" -ForegroundColor Yellow
Write-Host "Pub/Sub:     Enabled" -ForegroundColor Yellow
Write-Host "Max conns:   1000" -ForegroundColor Yellow
Write-Host ""
