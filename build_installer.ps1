param(
    [switch]$RunInstaller
)

$ErrorActionPreference = 'Stop'

$repoRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { (Get-Location).Path } else { (Resolve-Path $PSScriptRoot).Path }
$projectDir = Join-Path $repoRoot 'src\FrameShift'
$installerDir = Join-Path $repoRoot 'installer'
$projectFile = Join-Path $projectDir 'FrameShift.csproj'
$issFile = Join-Path $installerDir 'FrameShift.iss'

function Get-ProjectVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectFilePath
    )

    [xml]$projectXml = Get-Content -LiteralPath $ProjectFilePath
    foreach ($propertyGroup in $projectXml.Project.PropertyGroup) {
        if (![string]::IsNullOrWhiteSpace($propertyGroup.Version)) {
            return $propertyGroup.Version.Trim()
        }
    }

    throw "No <Version> was found in $ProjectFilePath"
}

$appVersion = Get-ProjectVersion -ProjectFilePath $projectFile
$installerExe = Join-Path $installerDir ("FrameShift_{0}_Setup.exe" -f $appVersion)

Write-Host "Publishing FrameShift release payload v$appVersion..." -ForegroundColor Cyan
dotnet publish $projectFile -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

function Get-IsccPath {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 5\ISCC.exe',
        'C:\Program Files\Inno Setup 5\ISCC.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    return $null
}

$isccPath = Get-IsccPath
if ($null -eq $isccPath) {
    Write-Host ''
    Write-Host 'Inno Setup compiler not found.' -ForegroundColor Yellow
    Write-Host 'The release payload was published successfully, but the installer was not rebuilt.' -ForegroundColor Yellow
    Write-Host "Run ISCC.exe manually on: $issFile" -ForegroundColor Yellow
    exit 1
}

Write-Host "Compiling installer v$appVersion with $isccPath ..." -ForegroundColor Cyan
& $isccPath "/DMyAppVersion=$appVersion" $issFile
if ($LASTEXITCODE -ne 0) {
    throw 'Inno Setup compilation failed.'
}

if (!(Test-Path $installerExe)) {
    throw "Installer was not created: $installerExe"
}

Write-Host ''
Write-Host "Installer ready: $installerExe" -ForegroundColor Green

if ($RunInstaller) {
    Write-Host 'Launching installer...' -ForegroundColor Cyan
    Start-Process -FilePath $installerExe -WorkingDirectory $installerDir
}
