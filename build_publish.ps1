param(
    [switch]$RunTests,
    [switch]$NoInstaller
)

$ErrorActionPreference = 'Stop'

$repoRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    (Get-Location).Path
} else {
    (Resolve-Path $PSScriptRoot).Path
}

$projectDir = Join-Path $repoRoot 'src\FrameShift'
$projectFile = Join-Path $projectDir 'FrameShift.csproj'
$subtitlesWorkerProject = Join-Path $repoRoot 'src\FrameShift.SubtitlesWorker\FrameShift.SubtitlesWorker.csproj'
$testProject = Join-Path $repoRoot 'tests\FrameShift.Tests\FrameShift.Tests.csproj'
$installerDir = Join-Path $repoRoot 'installer'
$issFile = Join-Path $installerDir 'FrameShift.iss'
$publishDir = Join-Path $repoRoot 'publish\FrameShift-win-x64'

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

function Get-IsccPath {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 5\ISCC.exe',
        'C:\Program Files\Inno Setup 5\ISCC.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    return $null
}

function Restore-Project {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (!(Test-Path -LiteralPath $ProjectPath)) {
        return
    }

    Write-Host ''
    Write-Host "Restoring $Label..." -ForegroundColor Cyan
    dotnet restore $ProjectPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed for $ProjectPath"
    }
}

if (!(Test-Path -LiteralPath $projectFile)) {
    throw "Project file not found: $projectFile"
}

$appVersion = Get-ProjectVersion -ProjectFilePath $projectFile
$installerExe = Join-Path $installerDir ("FrameShift_{0}_Setup.exe" -f $appVersion)

Write-Host ''
Write-Host "FrameShift build/publish" -ForegroundColor Cyan
Write-Host "Version : $appVersion" -ForegroundColor Gray
Write-Host "Project : $projectFile" -ForegroundColor Gray
Write-Host "Publish : $publishDir" -ForegroundColor Gray

Restore-Project -ProjectPath $projectFile -Label 'app packages'
Restore-Project -ProjectPath $subtitlesWorkerProject -Label 'Create Subtitles worker packages'

if ($RunTests) {
    Restore-Project -ProjectPath $testProject -Label 'test packages'

    if (Test-Path -LiteralPath $testProject) {
        Write-Host ''
        Write-Host 'Running tests...' -ForegroundColor Cyan
        dotnet test $testProject -c Release --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw 'dotnet test failed.'
        }
    }
}

if (Test-Path -LiteralPath $publishDir) {
    Write-Host ''
    Write-Host 'Cleaning previous publish folder...' -ForegroundColor Cyan
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

Write-Host ''
Write-Host 'Publishing app...' -ForegroundColor Cyan
dotnet publish $projectFile `
    -c Release `
    -r win-x64 `
    -p:SelfContained=true `
    -p:PublishSingleFile=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

Write-Host ''
Write-Host "Publish ready: $publishDir" -ForegroundColor Green

if ($NoInstaller) {
    Write-Host 'Installer step skipped by request.' -ForegroundColor Yellow
    exit 0
}

$isccPath = Get-IsccPath
if ($null -eq $isccPath) {
    Write-Host ''
    Write-Host 'Inno Setup not found. Publish output is ready; installer was skipped.' -ForegroundColor Yellow
    exit 0
}

if (!(Test-Path -LiteralPath $issFile)) {
    Write-Host ''
    Write-Host "Inno Setup script not found: $issFile" -ForegroundColor Yellow
    Write-Host 'Publish output is ready; installer was skipped.' -ForegroundColor Yellow
    exit 0
}

Write-Host ''
Write-Host "Compiling installer with $isccPath ..." -ForegroundColor Cyan
& $isccPath "/DMyAppVersion=$appVersion" $issFile
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host 'Installer compilation failed, but publish output is still ready.' -ForegroundColor Yellow
    exit 0
}

if (Test-Path -LiteralPath $installerExe) {
    Write-Host "Installer ready: $installerExe" -ForegroundColor Green
} else {
    Write-Host 'Inno Setup completed, but the expected installer file was not found.' -ForegroundColor Yellow
}
