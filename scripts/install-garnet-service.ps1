#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs Garnet as a Windows Service for production use on Windows Server 2019.

.DESCRIPTION
    This script sets up Garnet (Microsoft's Redis-compatible cache) as a
    persistent Windows Service that starts automatically on boot.

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

$DataDir = "C:\GarnetData"
$LogDir = "C:\GarnetLogs"

# ─── Create directories ─────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

Write-Host "Garnet path: $GarnetPath" -ForegroundColor Cyan
Write-Host "Data directory: $DataDir" -ForegroundColor Cyan
Write-Host "Log directory: $LogDir" -ForegroundColor Cyan

# ─── Stop existing service if present ────────────────────────────────
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# ─── Create the service using NSSM (if available) or sc.exe ─────────
# Garnet runs as a console app, so we use sc.exe with binPath
# For better process management, consider using NSSM:
#   nssm install GarnetCache garnet-server --bind 0.0.0.0 --port 6379

$binPath = "`"$GarnetPath`" --bind 0.0.0.0 --port 6379 --recover --storage-tier"

Write-Host "Creating Windows Service..." -ForegroundColor Green
sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= $ServiceDisplayName
sc.exe description $ServiceName "Microsoft Garnet Redis-compatible cache server for EBI.ALAS.V2"

# ─── Configure service recovery ─────────────────────────────────────
# Restart on failure: 1st failure = 1min, 2nd = 5min, 3rd = 15min
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/300000/restart/900000

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
Write-Host ""
