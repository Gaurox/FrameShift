[CmdletBinding()]
param(
    [switch]$AllowDirty,
    [switch]$RunInstaller,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'

$canonicalScript = Join-Path $PSScriptRoot 'build_installer.ps1'
if (!(Test-Path -LiteralPath $canonicalScript -PathType Leaf)) {
    throw "Canonical release script not found: $canonicalScript"
}

& $canonicalScript @PSBoundParameters
exit $LASTEXITCODE
