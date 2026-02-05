# scripts/ensure-docker.ps1
# Ensures Docker containers are running before starting the application
# This script is called from launchSettings.json's "Docker-Auto-Start" profile

Write-Host "Checking Docker containers..." -ForegroundColor Cyan

# Check if Docker is running
try {
    $dockerInfo = docker info 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Docker is not running. Please start Docker Desktop." -ForegroundColor Red
        Write-Host "Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 1
    }
} catch {
    Write-Host "ERROR: Docker is not installed or not in PATH." -ForegroundColor Red
    exit 1
}

# Check if RabbitMQ container is running
$rabbitmqRunning = docker ps --filter "name=ddd-rabbitmq" --filter "status=running" --format "{{.Names}}"

if ($rabbitmqRunning) {
    Write-Host "RabbitMQ container is already running." -ForegroundColor Green
    exit 0
}

# Start RabbitMQ container
Write-Host "Starting RabbitMQ container..." -ForegroundColor Yellow

# Navigate to solution root (where docker-compose.yml is)
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = Split-Path -Parent $scriptPath
Push-Location $solutionRoot

try {
    docker-compose up -d

    if ($LASTEXITCODE -eq 0) {
        Write-Host "RabbitMQ container started successfully." -ForegroundColor Green

        # Wait for RabbitMQ health check
        Write-Host "Waiting for RabbitMQ to be ready..." -ForegroundColor Cyan
        $maxAttempts = 30
        $attempt = 0

        while ($attempt -lt $maxAttempts) {
            $health = docker inspect --format='{{.State.Health.Status}}' ddd-rabbitmq 2>$null
            if ($health -eq "healthy") {
                Write-Host "RabbitMQ is ready!" -ForegroundColor Green
                break
            }
            $attempt++
            Start-Sleep -Seconds 1
        }

        if ($attempt -eq $maxAttempts) {
            Write-Host "WARNING: RabbitMQ health check timeout. It may still be starting..." -ForegroundColor Yellow
        }
    } else {
        Write-Host "ERROR: Failed to start RabbitMQ container." -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}

# Note: This project uses SQL Server LocalDB (configured in Phase 2), not Docker.
