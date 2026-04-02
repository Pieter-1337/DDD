# RabbitMQ Docker Setup

This document contains the shared Docker and RabbitMQ setup used by both the MassTransit ([02-rabbitmq-masstransit-setup.md](./02-rabbitmq-masstransit-setup.md)) and Wolverine ([03-rabbitmq-wolverine-setup.md](./03-rabbitmq-wolverine-setup.md)) messaging guides. The setup is framework-agnostic — identical regardless of which messaging framework you choose.

---

## 1. docker-compose.yml

Create `docker-compose.yml` in your solution root:

```yaml
services:
  rabbitmq:
    image: rabbitmq:3-management
    container_name: ddd-rabbitmq
    restart: unless-stopped
    ports:
      - "0.0.0.0:5672:5672"    # AMQP port (messaging)
      - "0.0.0.0:15672:15672"  # Management UI
    environment:
      - RABBITMQ_DEFAULT_USER=guest
      - RABBITMQ_DEFAULT_PASS=guest
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "check_running"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  rabbitmq_data:
```

**Note**: The `0.0.0.0:5672:5672` format explicitly binds container ports to all network interfaces, ensuring the ports are accessible from your .NET application on Windows.

**Data persistence**: The `volumes` section ensures RabbitMQ data (including messages and DLQ) persists across container restarts and PC reboots. Data is only lost if you run `docker-compose down -v` (the `-v` flag deletes volumes).

---

## 2. Start and Verify

Start RabbitMQ:
```bash
docker-compose up -d
```

Verify RabbitMQ is running:
```bash
docker-compose ps
# Expected: ddd-rabbitmq status "Up"
```

Access RabbitMQ Management UI:
- URL: http://localhost:15672
- Username: guest
- Password: guest

### Verification Checklist

- [ ] RabbitMQ running in Docker
- [ ] RabbitMQ Management UI accessible at http://localhost:15672
- [ ] Can log into Management UI with guest/guest credentials

---

## 3. Auto-Start Docker on F5 in Visual Studio (Optional)

To automatically start RabbitMQ when you press F5 in Visual Studio, create an MSBuild target that runs before build.

### Create PowerShell Script

Create `scripts/ensure-docker.ps1` in your solution root:

```powershell
param(
    [int]$TimeoutSeconds = 60
)

Write-Host "Ensuring Docker containers are running..." -ForegroundColor Cyan

# Check if Docker Desktop is running
$dockerProcess = Get-Process "Docker Desktop" -ErrorAction SilentlyContinue
if (-not $dockerProcess) {
    Write-Host "ERROR: Docker Desktop is not running." -ForegroundColor Red
    Write-Host "Please start Docker Desktop and try again." -ForegroundColor Yellow
    exit 1
}

# Check if RabbitMQ container is running
$rabbitmqRunning = docker ps --filter "name=ddd-rabbitmq" --filter "status=running" --format "{{.Names}}"

if ($rabbitmqRunning) {
    Write-Host "RabbitMQ container already running." -ForegroundColor Green
    exit 0
}

# Start RabbitMQ container
Write-Host "Starting RabbitMQ container..." -ForegroundColor Yellow
Push-Location $PSScriptRoot\..
docker-compose up -d
Pop-Location

# Wait for RabbitMQ health check
Write-Host "Waiting for RabbitMQ to be ready..." -ForegroundColor Yellow
$elapsed = 0
while ($elapsed -lt $TimeoutSeconds) {
    $health = docker inspect --format="{{.State.Health.Status}}" ddd-rabbitmq 2>$null
    if ($health -eq "healthy") {
        Write-Host "RabbitMQ is ready!" -ForegroundColor Green
        exit 0
    }
    Start-Sleep -Seconds 2
    $elapsed += 2
}

Write-Host "ERROR: RabbitMQ health check timed out after $TimeoutSeconds seconds." -ForegroundColor Red
exit 1
```

### Create MSBuild Target

Create `Directory.Build.targets` in your solution root (same directory as .sln file):

```xml
<Project>

  <!--
    Docker startup targets for WebApi projects.
    These targets ensure Docker services (RabbitMQ) are running before build/run.
    Automatically inherited by all projects in the solution.

    Strategy: Use a SINGLE target that runs on EVERY Build, even cached builds.
    - The script itself is idempotent (fast exit if containers already running)
    - This ensures F5 ALWAYS checks and starts containers if needed
  -->

  <Target Name="EnsureDockerServices" BeforeTargets="Build">
    <Exec Command="powershell -ExecutionPolicy Bypass -File &quot;$(MSBuildThisFileDirectory)scripts\ensure-docker.ps1&quot;"
          IgnoreExitCode="false" />
  </Target>

</Project>
```

This approach:
- Automatically applies to ALL projects in the solution
- Runs before every build
- The PowerShell script exits quickly if containers are already running
- Ensures Docker is always checked before F5, even on cached builds

---

## 4. Docker Commands Cheat Sheet

```bash
# Start containers in background
docker-compose up -d

# Check if containers are running
docker-compose ps

# View logs
docker-compose logs rabbitmq
docker-compose logs -f rabbitmq  # Follow/stream logs (real-time)

# Stop containers (keeps data)
docker-compose stop

# Start stopped containers
docker-compose start

# Stop and remove containers (keeps data in volumes)
docker-compose down

# Stop, remove containers AND delete data
docker-compose down -v

# Restart a specific service
docker-compose restart rabbitmq

# Execute commands inside running container
docker exec -it ddd-rabbitmq bash
```
