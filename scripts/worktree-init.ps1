# scripts/worktree-init.ps1
#
# Claims the lowest free worktree slot in {2,3,4,5} for the current worktree,
# writes it to Aspire.AppHost/.worktree-slot, and prints the claimed slot.
#
# If the current worktree already has a slot file, prints the existing slot and
# exits 0 (idempotent — no double-claim).
#
# If all four slots {2,3,4,5} are taken, exits non-zero with a clear message.
#
# Scan-and-claim is guarded by a lockfile in the git common dir so that two
# concurrent runs never assign the same slot.
#
# Usage: pwsh -File scripts/worktree-init.ps1
#        (run from any directory inside the worktree)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Git plumbing helpers
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

# Resolve the root of the *current* worktree (where this script is running).
$worktreeRoot = (Invoke-Git @('rev-parse', '--show-toplevel')).Trim()

# git rev-parse --git-common-dir may return a relative path for the main
# checkout (just ".git"), so resolve it to an absolute path.
$gitCommonDirRaw = (Invoke-Git @('rev-parse', '--git-common-dir')).Trim()
# When git returns multiple lines (e.g. warning + path), take only the last.
if ($gitCommonDirRaw -is [array]) { $gitCommonDirRaw = $gitCommonDirRaw[-1] }
if ([System.IO.Path]::IsPathRooted($gitCommonDirRaw)) {
    $gitCommonDir = $gitCommonDirRaw
} else {
    $gitCommonDir = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine($worktreeRoot, $gitCommonDirRaw))
}

# The slot file lives inside the AppHost subdir, matching WorktreeSlot.Resolve().
$slotFile = [System.IO.Path]::Combine($worktreeRoot, 'Aspire.AppHost', '.worktree-slot')

# ---------------------------------------------------------------------------
# Idempotency guard: if this worktree already has a slot, we are done.
# ---------------------------------------------------------------------------

if (Test-Path $slotFile) {
    $existing = (Get-Content -LiteralPath $slotFile -TotalCount 1).Trim()
    Write-Host "Worktree already has slot $existing (nothing to do)." -ForegroundColor Green
    Write-Output $existing
    exit 0
}

# ---------------------------------------------------------------------------
# Lockfile — atomic acquire using FileMode.CreateNew + DeleteOnClose
# The lockfile lives in the git common dir, the one filesystem location that
# is shared across all worktrees regardless of which path they are checked out
# at. DeleteOnClose ensures a crashed process never leaves a stale lock.
# ---------------------------------------------------------------------------

$lockPath = [System.IO.Path]::Combine($gitCommonDir, 'worktree-slot.lock')
$lockStream = $null

$maxAttempts = 20       # ~2 s total at 100 ms each
$attempt = 0
while ($true) {
    try {
        $lockStream = [System.IO.FileStream]::new(
            $lockPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None,
            4096,
            [System.IO.FileOptions]::DeleteOnClose)
        break   # acquired
    }
    catch [System.IO.IOException] {
        $attempt++
        if ($attempt -ge $maxAttempts) {
            Write-Error "Could not acquire worktree slot lock after $maxAttempts attempts. Lock file: $lockPath"
            exit 1
        }
        Start-Sleep -Milliseconds 100
    }
}

try {
    # -----------------------------------------------------------------------
    # Inside the lock: scan all worktrees for taken slots, then claim the
    # lowest free one in {2,3,4,5} and write the slot file before releasing.
    # -----------------------------------------------------------------------

    # Parse `git worktree list --porcelain` to get stable paths.
    # Each worktree block starts with "worktree <path>".
    $worktreeLines = Invoke-Git @('worktree', 'list', '--porcelain')
    $worktreePaths = $worktreeLines |
        Where-Object { $_ -match '^worktree ' } |
        ForEach-Object { $_.Substring('worktree '.Length).Trim() }

    # Collect taken slots from every known worktree (other than the current one).
    $takenSlots = [System.Collections.Generic.HashSet[int]]::new()

    foreach ($path in $worktreePaths) {
        $otherSlotFile = [System.IO.Path]::Combine($path, 'Aspire.AppHost', '.worktree-slot')

        # Main checkout has no file → implicitly slot 1 (never claimable here).
        if (-not (Test-Path $otherSlotFile)) {
            continue
        }

        $raw = (Get-Content -LiteralPath $otherSlotFile -TotalCount 1).Trim()
        $parsed = 0
        if ([int]::TryParse($raw, [ref]$parsed) -and $parsed -ge 2 -and $parsed -le 5) {
            [void]$takenSlots.Add($parsed)
        }
    }

    # Find the lowest free slot in {2,3,4,5}.
    $claimedSlot = $null
    foreach ($candidate in 2..5) {
        if (-not $takenSlots.Contains($candidate)) {
            $claimedSlot = $candidate
            break
        }
    }

    if ($null -eq $claimedSlot) {
        $takenList = ($takenSlots | Sort-Object) -join ', '
        Write-Error "All 4 worktree slots are in use (slots taken: $takenList). Free a slot by running worktree-destroy.ps1 in one of the other worktrees."
        exit 1
    }

    # Write the slot file *inside* the lock so the value is visible to any
    # concurrent init that acquires the lock next.
    $slotDir = [System.IO.Path]::GetDirectoryName($slotFile)
    if (-not (Test-Path $slotDir)) {
        New-Item -ItemType Directory -Path $slotDir -Force | Out-Null
    }
    Set-Content -LiteralPath $slotFile -Value $claimedSlot -NoNewline

    Write-Host "Claimed worktree slot $claimedSlot." -ForegroundColor Green
    Write-Host "Slot file written: $slotFile" -ForegroundColor Gray
    Write-Output $claimedSlot
}
finally {
    # Disposing the stream closes it; DeleteOnClose removes the file atomically.
    if ($null -ne $lockStream) {
        $lockStream.Dispose()
    }
}
