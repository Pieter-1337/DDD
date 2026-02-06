# scripts/ensure-docker.ps1
# Ensures Docker containers are running before starting the application
# This script is called from MSBuild during build process

param(
    [int]$TimeoutSeconds = 60,
    [switch]$SkipHealthCheck = $false
)

Write-Host "Checking Docker containers..." -ForegroundColor Cyan

# Check if Docker is running
try {
    $dockerInfo = docker info 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Docker Desktop is not running." -ForegroundColor Red
        Write-Host "To fix: Start Docker Desktop and run the build again." -ForegroundColor Yellow
        Write-Host "Alternatively, you can temporarily disable auto-start by removing Directory.Build.targets" -ForegroundColor Gray
        exit 1
    }
} catch {
    Write-Host "ERROR: Docker is not installed or not in PATH." -ForegroundColor Red
    Write-Host "To fix: Install Docker Desktop from https://www.docker.com/products/docker-desktop" -ForegroundColor Yellow
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

        # Wait for RabbitMQ health check (if not skipped)
        if (-not $SkipHealthCheck) {
            Write-Host "Waiting for RabbitMQ to be ready (timeout: $TimeoutSeconds seconds)..." -ForegroundColor Cyan
            $attempt = 0
            $intervalSeconds = 2

            while ($attempt -lt $TimeoutSeconds) {
                $health = docker inspect --format='{{.State.Health.Status}}' ddd-rabbitmq 2>$null
                if ($health -eq "healthy") {
                    Write-Host "RabbitMQ is ready!" -ForegroundColor Green
                    break
                }
                $attempt += $intervalSeconds
                Start-Sleep -Seconds $intervalSeconds
            }

            if ($attempt -ge $TimeoutSeconds) {
                Write-Host "WARNING: RabbitMQ health check timeout after $TimeoutSeconds seconds." -ForegroundColor Yellow
                Write-Host "The container may still be starting. Check status with: docker logs ddd-rabbitmq" -ForegroundColor Gray
                # Don't exit with error - container is starting, just not healthy yet
            }
        } else {
            Write-Host "Skipping health check (--SkipHealthCheck flag set)" -ForegroundColor Gray
        }
    } else {
        Write-Host "ERROR: Failed to start RabbitMQ container." -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}

# Note: This project uses SQL Server LocalDB (configured in Phase 2), not Docker.
