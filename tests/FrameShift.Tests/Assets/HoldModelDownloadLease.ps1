param([string]$LockPath, [string]$ReadyPath, [string]$ReleasePath)
$ErrorActionPreference = 'Stop'
$lease = [System.IO.File]::Open($LockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
try {
    [System.IO.File]::WriteAllText($ReadyPath, 'ready')
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not [System.IO.File]::Exists($ReleasePath)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Lease test timed out.' }
        Start-Sleep -Milliseconds 25
    }
}
finally { $lease.Dispose() }
