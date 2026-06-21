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

# --- Gate 1: working tree must be clean ---
Write-Host "Checking git working tree..." -ForegroundColor Cyan
$gitDirty = git -C $repoRoot status --porcelain 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host 'git status failed — skipping cleanliness check.' -ForegroundColor Yellow
} elseif (![string]::IsNullOrWhiteSpace($gitDirty)) {
    Write-Host ''
    Write-Host 'Uncommitted changes detected:' -ForegroundColor Yellow
    Write-Host $gitDirty -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'The installer will package the published output regardless, but the working tree is not clean.' -ForegroundColor Yellow
    $confirm = Read-Host 'Continue anyway? [y/N]'
    if ($confirm -notmatch '^[yY]$') {
        throw 'Build aborted: uncommitted changes present.'
    }
} else {
    Write-Host "Working tree clean." -ForegroundColor Green
}

# --- Gate 2: CHANGELOG.md must have a section for this version ---
Write-Host "Checking CHANGELOG.md for version $appVersion..." -ForegroundColor Cyan
$changelogPath = Join-Path $repoRoot 'docs\CHANGELOG.md'
if (!(Test-Path $changelogPath)) {
    throw "CHANGELOG.md not found at: $changelogPath"
}
$changelogContent = Get-Content -LiteralPath $changelogPath -Raw
if ($changelogContent -notmatch "(?m)^## $([regex]::Escape($appVersion))\s*$") {
    throw "CHANGELOG.md has no '## $appVersion' section. Add a release entry before building."
}
Write-Host "CHANGELOG.md ## $appVersion section found — OK" -ForegroundColor Green

# --- Gate 3: tests must pass ---
Write-Host "Running tests..." -ForegroundColor Cyan
$testProject = Join-Path $repoRoot 'tests\FrameShift.Tests\FrameShift.Tests.csproj'
dotnet test $testProject -c Release --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw 'Tests failed. Fix all test failures before building the installer.'
}
Write-Host "All tests passed." -ForegroundColor Green

$publishDir = Join-Path $repoRoot 'publish\FrameShift-win-x64'

Write-Host "Publishing FrameShift release payload v$appVersion..." -ForegroundColor Cyan
dotnet publish $projectFile -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=false -o $publishDir
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
