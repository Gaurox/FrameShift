param(
    [string]$DotnetPath = 'dotnet',
    [string]$PackageCache = (Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages'),
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
$prototypeRoot = $PSScriptRoot
$shellProject = Join-Path $prototypeRoot 'Shell/FrameShift.MenuPrototype.Shell.csproj'
$probeProject = Join-Path $prototypeRoot 'Probe/FrameShift.MenuPrototype.Probe.csproj'
$nativeOutput = Join-Path $prototypeRoot 'bin/native'
if (!$ReportPath) {
    $ReportPath = Join-Path $prototypeRoot ('bin/evidence/menu-probe-' + [Guid]::NewGuid().ToString('N') + '.json')
}
if (Test-Path -LiteralPath $ReportPath) { throw 'Choose a new report path; existing files are never overwritten.' }
if (!(Test-Path -LiteralPath $PackageCache -PathType Container)) { throw 'This offline prototype requires an existing package cache.' }

# Set process-local CLI options only; do not create certificates or a user-wide SDK profile.
$cliSettings = @{
    DOTNET_CLI_HOME = (Join-Path $prototypeRoot 'bin/cli-home')
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    DOTNET_NOLOGO = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
}
$previousSettings = @{}

function Invoke-PrototypeDotnet([string[]]$Arguments) {
    & $DotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Prototype dotnet step failed ($LASTEXITCODE)." }
}

Push-Location -LiteralPath $prototypeRoot
try {
    foreach ($name in $cliSettings.Keys) {
        $previousSettings[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $cliSettings[$name], 'Process')
    }
    Invoke-PrototypeDotnet -Arguments @('--version')
    Invoke-PrototypeDotnet -Arguments @('restore', $shellProject, '--configfile', (Join-Path $prototypeRoot 'NuGet.Config'), '--packages', $PackageCache, '--locked-mode')
    Invoke-PrototypeDotnet -Arguments @('restore', $probeProject, '--configfile', (Join-Path $prototypeRoot 'NuGet.Config'), '--packages', $PackageCache, '--locked-mode')
    Invoke-PrototypeDotnet -Arguments @('publish', $shellProject, '-c', 'Release', '--no-restore', '-o', $nativeOutput)
    Invoke-PrototypeDotnet -Arguments @('build', $probeProject, '-c', 'Release', '--no-restore')
    $nativeDll = Join-Path $nativeOutput 'FrameShift.MenuPrototype.Shell.dll'
    $probeDll = Join-Path $prototypeRoot 'Probe/bin/Release/net8.0-windows/FrameShift.MenuPrototype.Probe.dll'
    Invoke-PrototypeDotnet -Arguments @($probeDll, '--native', $nativeDll, '--report', $ReportPath)
}
finally {
    foreach ($name in $previousSettings.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousSettings[$name], 'Process')
    }
    Pop-Location
}
