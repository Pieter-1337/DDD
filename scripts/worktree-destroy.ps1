# scripts/worktree-destroy.ps1
#
# Tears down the worktree slot for the current worktree:
#   1. Reads Aspire.AppHost/.worktree-slot to determine the slot number.
#   2. Drops DDD_S{N} and IdentityDb_S{N} from (localdb)\MSSQLLocalDB
#      (DROP DATABASE IF EXISTS — safe to call when the DB does not exist yet).
#   3. Deletes the .worktree-slot file, releasing the slot.
#
# REFUSES to act on slot 1 (main checkout) or when no slot file is present.
# Slot 1 databases are DDD and IdentityDb — they must never be dropped here.
#
# Pass -WhatIf to preview the SQL DROP statements without executing them.
#
# Usage: pwsh -File scripts/worktree-destroy.ps1 [-WhatIf]

[CmdletBinding(SupportsShouldProcess)]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Locate the slot file
# ---------------------------------------------------------------------------

function Invoke-Git {
    param([string[]]$GitArgs)
    # Do NOT use 2>&1 on native executables in PS 5.1 — it wraps stderr as
    # ErrorRecord objects (NativeCommandError) and sets $? to $false even when
    # the process exits 0.  Capture stdout only; let stderr flow to the console.
    $out = & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArgs -join ' ') failed (exit $LASTEXITCODE)"
    }
    return $out
}

$rootRaw = Invoke-Git @('rev-parse', '--show-toplevel')
# When git returns multiple lines (e.g. warning + path), take only the last.
if ($rootRaw -is [array]) { $rootRaw = $rootRaw[-1] }
$worktreeRoot = $rootRaw.Trim()
$slotFile = [System.IO.Path]::Combine($worktreeRoot, 'Aspire.AppHost', '.worktree-slot')

# ---------------------------------------------------------------------------
# Refuse to act if no slot file is present (implicit main checkout = slot 1).
# ---------------------------------------------------------------------------

if (-not (Test-Path $slotFile)) {
    Write-Error "No .worktree-slot file found at '$slotFile'. This appears to be the main checkout (implicit slot 1). worktree-destroy.ps1 refuses to run on slot 1 to protect DDD and IdentityDb."
    exit 1
}

$raw = (Get-Content -LiteralPath $slotFile -TotalCount 1).Trim()
$slot = 0
if (-not [int]::TryParse($raw, [ref]$slot)) {
    Write-Error "Invalid slot value '$raw' in '$slotFile'. Expected an integer 2–5."
    exit 1
}

# ---------------------------------------------------------------------------
# Refuse slot 1 even if it somehow ended up in the file.
# ---------------------------------------------------------------------------

if ($slot -eq 1) {
    Write-Error "Slot file contains slot 1. worktree-destroy.ps1 refuses to drop DDD or IdentityDb. Remove the .worktree-slot file manually if you intended to reset the main checkout."
    exit 1
}

if ($slot -lt 2 -or $slot -gt 5) {
    Write-Error "Slot value $slot is out of the allowed range 2–5. Aborting."
    exit 1
}

$schedulingDb = "DDD_S$slot"
$identityDb   = "IdentityDb_S$slot"
$server       = '(localdb)\MSSQLLocalDB'

Write-Host "Worktree slot: $slot" -ForegroundColor Cyan
Write-Host "Server       : $server" -ForegroundColor Cyan
Write-Host "Databases    : $schedulingDb, $identityDb" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Drop the slot databases
# ---------------------------------------------------------------------------

$dropSql = @"
DROP DATABASE IF EXISTS [$schedulingDb];
DROP DATABASE IF EXISTS [$identityDb];
"@

if ($PSCmdlet.ShouldProcess($server, "DROP DATABASE IF EXISTS [$schedulingDb]; DROP DATABASE IF EXISTS [$identityDb]")) {
    Write-Host "Dropping databases on $server ..." -ForegroundColor Yellow

    # Use SqlCmd if available; fall back to System.Data.SqlClient via .NET.
    $sqlcmdAvailable = $null -ne (Get-Command sqlcmd -ErrorAction SilentlyContinue)

    if ($sqlcmdAvailable) {
        $tempSql = [System.IO.Path]::GetTempFileName() + '.sql'
        try {
            Set-Content -Path $tempSql -Value $dropSql -Encoding UTF8
            sqlcmd -S $server -i $tempSql -b
            if ($LASTEXITCODE -ne 0) {
                throw "sqlcmd exited with code $LASTEXITCODE"
            }
        }
        finally {
            if (Test-Path $tempSql) { Remove-Item $tempSql -Force }
        }
    }
    else {
        # Fallback: use System.Data.SqlClient bundled with .NET 9.
        Add-Type -AssemblyName 'System.Data'
        $connectionString = "Server=$server;Database=master;Integrated Security=True;TrustServerCertificate=True;"
        $connection = [System.Data.SqlClient.SqlConnection]::new($connectionString)
        try {
            $connection.Open()
            $cmd = $connection.CreateCommand()
            $cmd.CommandText = $dropSql
            [void]$cmd.ExecuteNonQuery()
        }
        finally {
            $connection.Close()
            $connection.Dispose()
        }
    }

    Write-Host "Databases dropped (or did not exist)." -ForegroundColor Green
}
else {
    Write-Host "[WhatIf] Would execute on $server :" -ForegroundColor Yellow
    Write-Host $dropSql -ForegroundColor Gray
}

# ---------------------------------------------------------------------------
# Release the slot by deleting the slot file
# ---------------------------------------------------------------------------

if ($PSCmdlet.ShouldProcess($slotFile, 'Delete slot file')) {
    Remove-Item -LiteralPath $slotFile -Force
    Write-Host "Slot $slot released (slot file deleted)." -ForegroundColor Green
}
else {
    Write-Host "[WhatIf] Would delete slot file: $slotFile" -ForegroundColor Yellow
}
