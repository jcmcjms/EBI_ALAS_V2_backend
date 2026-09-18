#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs RabbitMQ on Windows Server 2019 for EBI.ALAS.V2 production use.

.DESCRIPTION
    RabbitMQ is required for MassTransit async message processing (audit logs,
    notifications). This script installs Erlang + RabbitMQ and configures it
    as a Windows Service.

.NOTES
    Run this script ONCE on the production server. After installation, configure
    the RabbitMQ connection string in your environment variables or Key Vault.
#>

$ErrorActionPreference = "Stop"

Write-Host "=== RabbitMQ Installation for Windows Server 2019 ===" -ForegroundColor Cyan
Write-Host ""

# ─── Step 1: Check if already installed ─────────────────────────────
$rabbitService = Get-Service -Name "RabbitMQ" -ErrorAction SilentlyContinue
if ($rabbitService) {
    Write-Host "RabbitMQ is already installed." -ForegroundColor Green
    Write-Host "Status: $($rabbitService.Status)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Connection string for appsettings:" -ForegroundColor Cyan
    Write-Host '  "RabbitMQ": "amqp://guest:guest@localhost:5672"' -ForegroundColor White
    exit 0
}

# ─── Step 2: Install Erlang (RabbitMQ dependency) ───────────────────
Write-Host "[1/4] Installing Erlang OTP..." -ForegroundColor Yellow

$erlangUrl = "https://github.com/erlang/otp/releases/download/OTP-27.2/otp_win64_27.2.exe"
$erlangInstaller = "$env:TEMP\otp_win64_27.2.exe"

if (-not (Test-Path $erlangInstaller)) {
    Write-Host "  Downloading Erlang OTP 27.2..." -ForegroundColor Gray
    Invoke-WebRequest -Uri $erlangUrl -OutFile $erlangInstaller -UseBasicParsing
}

Write-Host "  Installing Erlang (silent)..." -ForegroundColor Gray
Start-Process -FilePath $erlangInstaller -ArgumentList "/S" -Wait -NoNewWindow

# Add Erlang to PATH for this session
$erlangPath = "C:\Program Files\Erlang OTP\bin"
if (Test-Path $erlangPath) {
    $env:PATH = "$erlangPath;$env:PATH"
}

Write-Host "  Erlang installed." -ForegroundColor Green

# ─── Step 3: Install RabbitMQ ───────────────────────────────────────
Write-Host "[2/4] Installing RabbitMQ Server..." -ForegroundColor Yellow

$rabbitUrl = "https://github.com/rabbitmq/rabbitmq-server/releases/download/v4.0.5/rabbitmq-server-4.0.5.exe"
$rabbitInstaller = "$env:TEMP\rabbitmq-server-4.0.5.exe"

if (-not (Test-Path $rabbitInstaller)) {
    Write-Host "  Downloading RabbitMQ 4.0.5..." -ForegroundColor Gray
    Invoke-WebRequest -Uri $rabbitUrl -OutFile $rabbitInstaller -UseBasicParsing
}

Write-Host "  Installing RabbitMQ (silent)..." -ForegroundColor Gray
Start-Process -FilePath $rabbitInstaller -ArgumentList "/S" -Wait -NoNewWindow

Write-Host "  RabbitMQ installed." -ForegroundColor Green

# ─── Step 4: Enable Management Plugin ───────────────────────────────
Write-Host "[3/4] Enabling Management Plugin..." -ForegroundColor Yellow

$rabbitPath = "C:\Program Files\RabbitMQ Server\rabbitmq_server-4.0.5\sbin"
if (Test-Path $rabbitPath) {
    & "$rabbitPath\rabbitmq-plugins.bat" enable rabbitmq_management
    Write-Host "  Management plugin enabled." -ForegroundColor Green
    Write-Host "  Management UI: http://localhost:15672" -ForegroundColor Cyan
    Write-Host "  Default credentials: guest / guest" -ForegroundColor Yellow
}

# ─── Step 5: Configure Firewall ─────────────────────────────────────
Write-Host "[4/4] Configuring Windows Firewall..." -ForegroundColor Yellow

New-NetFirewallRule -DisplayName "RabbitMQ AMQP" -Direction Inbound -Protocol TCP -LocalPort 5672 -Action Allow -ErrorAction SilentlyContinue | Out-Null
New-NetFirewallRule -DisplayName "RabbitMQ Management" -Direction Inbound -Protocol TCP -LocalPort 15672 -Action Allow -ErrorAction SilentlyContinue | Out-Null

Write-Host "  Firewall rules added." -ForegroundColor Green

# ─── Summary ────────────────────────────────────────────────────────
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " RabbitMQ installed successfully!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "AMQP Endpoint:     localhost:5672" -ForegroundColor Cyan
Write-Host "Management UI:     http://localhost:15672" -ForegroundColor Cyan
Write-Host "Default user:      guest / guest" -ForegroundColor Yellow
Write-Host ""
Write-Host "IMPORTANT: Change the default guest password!" -ForegroundColor Red
Write-Host ""
Write-Host "Connection string for EBI.ALAS.V2:" -ForegroundColor Cyan
Write-Host '  "RabbitMQ": "amqp://YOUR_USER:YOUR_PASSWORD@localhost:5672"' -ForegroundColor White
Write-Host ""
Write-Host "For production, create a dedicated user:" -ForegroundColor Yellow
Write-Host '  rabbitmqctl add_user alas_api YOUR_SECURE_PASSWORD' -ForegroundColor White
Write-Host '  rabbitmqctl set_user_tags alas_api administrator' -ForegroundColor White
Write-Host '  rabbitmqctl set_permissions -p / alas_api ".*" ".*" ".*"' -ForegroundColor White
Write-Host ""
