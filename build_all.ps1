param(
    [switch]$AllowDirty,
    [switch]$RunInstaller,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { (Get-Location).Path } else { (Resolve-Path $PSScriptRoot).Path }
$canonicalScript = Join-Path $repoRoot 'build_installer.ps1'

if (!(Test-Path -LiteralPath $canonicalScript)) {
    throw "Canonical build script not found: $canonicalScript"
}

& $canonicalScript @PSBoundParameters
exit $LASTEXITCODE
