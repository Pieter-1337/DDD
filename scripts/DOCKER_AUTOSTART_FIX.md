# Docker Auto-Start Fix - Build Hang Issue Resolution

## Problem Summary

When running `dotnet build` with Docker Desktop not started, the build would **hang indefinitely** instead of failing gracefully.

### Root Cause

The `ensure-docker.ps1` script contained two blocking issues:

1. **Interactive prompt**: When Docker wasn't running, the script called `$Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")` which waits for keyboard input. In non-interactive build contexts (like `dotnet build` or CI/CD), this caused indefinite hanging.

2. **No timeout safety**: The MSBuild target had no timeout protection, allowing the script to run forever if it got stuck.

## Solution Implemented

### Changes Made

#### 1. `scripts/ensure-docker.ps1` - Removed Interactive Prompts

**Before:**
```powershell
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Docker is not running. Please start Docker Desktop." -ForegroundColor Red
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")  # ⚠️ BLOCKS
    exit 1
}
```

**After:**
```powershell
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Docker Desktop is not running." -ForegroundColor Red
    Write-Host "To fix: Start Docker Desktop and run the build again." -ForegroundColor Yellow
    Write-Host "Alternatively, you can temporarily disable auto-start by removing Directory.Build.targets" -ForegroundColor Gray
    exit 1  # Immediate fail, no blocking
}
```

#### 2. `scripts/ensure-docker.ps1` - Added Configurable Timeout

Added parameters for better control:

```powershell
param(
    [int]$TimeoutSeconds = 60,        # Health check timeout
    [switch]$SkipHealthCheck = $false # Skip waiting for health
)
```

Improved health check loop with clearer timeout handling:
- Changed from 30 attempts × 1s to configurable timeout with 2s intervals
- Better error messages with troubleshooting hints
- Non-fatal warning if timeout occurs (container may still be starting)

#### 3. `Directory.Build.targets` - Added MSBuild Safety Features

**Before:**
```xml
<Target Name="EnsureDockerServices" BeforeTargets="Build">
  <Exec Command="powershell ... ensure-docker.ps1"
        IgnoreExitCode="false" />
</Target>
```

**After:**
```xml
<Target Name="EnsureDockerServices" BeforeTargets="Build" Condition="'$(SKIP_DOCKER_CHECK)' != '1'">
  <Exec Command="powershell ... ensure-docker.ps1 -TimeoutSeconds 60"
        IgnoreExitCode="false"
        Timeout="90000" />
  <!-- Timeout is in milliseconds: 90000ms = 90 seconds -->
</Target>
```

New features:
- **MSBuild timeout**: Hard limit of 90 seconds (script timeout 60s + 30s buffer)
- **Conditional execution**: Can be disabled via environment variable
- **Explicit timeout parameter**: Passed to PowerShell script

## Usage

### Normal Build (Default Behavior)

```bash
dotnet build
```

- Checks Docker status
- Starts RabbitMQ if needed
- Waits up to 60 seconds for health check
- **If Docker is not running**: Fails immediately with clear error message

### Skip Docker Check Temporarily

```bash
# Windows (PowerShell)
$env:SKIP_DOCKER_CHECK = "1"
dotnet build

# Windows (CMD)
set SKIP_DOCKER_CHECK=1
dotnet build

# Linux/Mac
export SKIP_DOCKER_CHECK=1
dotnet build
```

### Manual Script Execution

```powershell
# Standard run with health check
.\scripts\ensure-docker.ps1

# Skip health check (faster, container starts in background)
.\scripts\ensure-docker.ps1 -SkipHealthCheck

# Custom timeout
.\scripts\ensure-docker.ps1 -TimeoutSeconds 120
```

## Testing the Fix

### Test 1: Docker Running (Happy Path)
```bash
# Start Docker Desktop first
dotnet build
# Expected: Quick exit if RabbitMQ already running, or starts and waits for health
```

### Test 2: Docker Not Running (Previously Hung)
```bash
# Stop Docker Desktop
dotnet build
# Expected: Immediate failure with message:
#   ERROR: Docker Desktop is not running.
#   To fix: Start Docker Desktop and run the build again.
```

### Test 3: Skip Docker Check
```bash
$env:SKIP_DOCKER_CHECK = "1"
dotnet build
# Expected: Build proceeds without Docker check
```

## Permanent Disable Options

If you want to completely disable Docker auto-start:

### Option 1: Delete the Target (Permanent)
```bash
rm Directory.Build.targets
```

### Option 2: Comment Out Target (Temporary)
Edit `Directory.Build.targets`:
```xml
<!--
<Target Name="EnsureDockerServices" BeforeTargets="Build" ...>
  ...
</Target>
-->
```

### Option 3: Set Environment Variable Permanently (Windows)
```powershell
[System.Environment]::SetEnvironmentVariable('SKIP_DOCKER_CHECK', '1', 'User')
```

## Error Messages Guide

### "ERROR: Docker Desktop is not running"
**Cause**: Docker Desktop process is not running
**Fix**: Start Docker Desktop and retry build

### "ERROR: Docker is not installed or not in PATH"
**Cause**: Docker CLI not found
**Fix**: Install Docker Desktop or add to PATH

### "WARNING: RabbitMQ health check timeout after X seconds"
**Cause**: Container started but not healthy yet
**Impact**: Non-fatal - container may still be starting
**Fix**: Check logs with `docker logs ddd-rabbitmq`

### "ERROR: Failed to start RabbitMQ container"
**Cause**: `docker-compose up -d` failed
**Fix**: Check Docker Desktop logs, verify docker-compose.yml syntax

## Architecture Notes

### Why This Approach?

This solution balances developer experience with robustness:

✅ **Fast feedback**: Fails immediately if Docker isn't running (no 30+ second hang)
✅ **Clear errors**: Actionable messages explain what to do
✅ **Escape hatches**: Multiple ways to disable if needed
✅ **Safe defaults**: MSBuild timeout prevents infinite hangs
✅ **Non-interactive**: Works in CI/CD, command-line, and Visual Studio

### MSBuild Target Strategy

The `Directory.Build.targets` approach ensures:
- **Automatic**: Inherited by all projects in solution
- **Every build**: Runs even on cached builds (idempotent script)
- **F5 experience**: Docker starts automatically in Visual Studio
- **Build-time**: Catches missing Docker before runtime failures

### Alternative Approaches Considered

❌ **Auto-start Docker Desktop**: Complex, platform-dependent, requires admin privileges
❌ **Longer timeout**: Doesn't solve the root cause (interactive prompt)
❌ **Silent failure**: Hides problems until runtime
✅ **Fail fast with clear message**: Best developer experience

## Troubleshooting

### Build still hangs

1. Check if you're on the latest version:
   ```bash
   git pull
   ```

2. Verify script has timeout parameter:
   ```powershell
   Get-Content scripts/ensure-docker.ps1 | Select-String "param"
   ```

3. Kill any stuck PowerShell processes:
   ```powershell
   Get-Process powershell | Where-Object {$_.Path -like "*ensure-docker*"} | Stop-Process
   ```

### Script fails but Docker is running

1. Test Docker CLI:
   ```bash
   docker info
   ```

2. Check RabbitMQ container:
   ```bash
   docker ps -a | grep rabbitmq
   ```

3. View script output:
   ```bash
   powershell -ExecutionPolicy Bypass -File scripts/ensure-docker.ps1
   ```

## Related Files

- `C:\projects\DDD\DDD\Directory.Build.targets` - MSBuild target that calls script
- `C:\projects\DDD\DDD\scripts\ensure-docker.ps1` - Docker startup script
- `C:\projects\DDD\DDD\docker-compose.yml` - RabbitMQ container definition

## Summary

The fix ensures builds **fail fast** with clear error messages instead of hanging indefinitely when Docker isn't running. The script no longer uses interactive prompts, and MSBuild has hard timeout protection. Developers can easily skip the check when needed via environment variable or by deleting the targets file.
