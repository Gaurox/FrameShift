[CmdletBinding()]
param(
    [switch]$AllowDirty,
    [switch]$RunInstaller,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:BundledFfmpegHashes = @{
    'Tools\ffmpeg\ffmpeg.exe' = '227AF0691433B703FFC5725E47F7D06EEFC34B4A72E7870E73D30E2CDA483ECF'
    'Tools\ffmpeg\ffprobe.exe' = '901F0EFE4793CBB0F017101E3427F816E8FBF9A407BD585F49DF30F4325CFD88'
}

function Assert-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label not found: $Path"
    }
}

function Assert-ExpectedSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedHash
    )

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $hasher = [System.Security.Cryptography.SHA256]::Create()
        try {
            $actualHash = ([System.BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-', '')
        }
        finally {
            $hasher.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    if (![string]::Equals($actualHash, $ExpectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label SHA-256 mismatch. Expected $ExpectedHash, got ${actualHash}: $Path"
    }
}

function Assert-BundledFfmpegPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PayloadRoot,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    foreach ($relativePath in $script:BundledFfmpegHashes.Keys) {
        $toolPath = Join-Path $PayloadRoot $relativePath
        Assert-RequiredFile -Path $toolPath -Label "$Label $relativePath"
        Assert-ExpectedSha256 -Path $toolPath -Label "$Label $relativePath" -ExpectedHash $script:BundledFfmpegHashes[$relativePath]
    }
}

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

function Invoke-ExternalTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$Step
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

function Get-IsccPath {
    param(
        [string]$RequestedPath
    )

    if (![string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (!(Test-Path -LiteralPath $RequestedPath -PathType Leaf)) {
            throw "Requested Inno Setup compiler not found: $RequestedPath"
        }

        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $candidates = @()
    if (![string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    }

    $candidates += @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 5\ISCC.exe',
        'C:\Program Files\Inno Setup 5\ISCC.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw 'Inno Setup compiler not found. Install Inno Setup or pass -IsccPath.'
}

function Assert-PublishDirectoryIsSafe {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    $publishRoot = [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'publish'))
    $expectedPublishDirectory = [System.IO.Path]::GetFullPath((Join-Path $publishRoot 'FrameShift-win-x64'))
    $actualPublishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)

    if (![string]::Equals($actualPublishDirectory, $expectedPublishDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean an unexpected publish directory: $actualPublishDirectory"
    }

    foreach ($path in @($publishRoot, $expectedPublishDirectory)) {
        if (Test-Path -LiteralPath $path) {
            $attributes = (Get-Item -LiteralPath $path -Force).Attributes
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to clean publish path containing a reparse point: $path"
            }
        }
    }
}

function Clear-ExpectedPublishDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    Assert-PublishDirectoryIsSafe -RepositoryRoot $RepositoryRoot -PublishDirectory $PublishDirectory

    if (Test-Path -LiteralPath $PublishDirectory) {
        Write-Host "Cleaning publish directory: $PublishDirectory" -ForegroundColor Cyan
        Remove-Item -LiteralPath $PublishDirectory -Recurse -Force
    }

    [System.IO.Directory]::CreateDirectory($PublishDirectory) | Out-Null

    if ($null -ne (Get-ChildItem -LiteralPath $PublishDirectory -Force | Select-Object -First 1)) {
        throw "Publish directory was not empty after cleanup: $PublishDirectory"
    }
}

function Assert-PublishPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    $requiredPayloadFiles = @(
        'FrameShift.exe',
        'Tools\ffmpeg\ffmpeg.exe',
        'Tools\ffmpeg\ffprobe.exe',
        'Workers\CreateSubtitlesWorker\FrameShift.SubtitlesWorker.exe'
    )

    foreach ($relativePath in $requiredPayloadFiles) {
        $payloadFile = Join-Path $PublishDirectory $relativePath
        if (!(Test-Path -LiteralPath $payloadFile -PathType Leaf)) {
            throw "Publish payload is incomplete. Required file not found: $payloadFile"
        }
    }

    Assert-BundledFfmpegPayload -PayloadRoot $PublishDirectory -Label 'Published FFmpeg payload'
}

try {
    $repoRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
    $projectFile = Join-Path $repoRoot 'src\FrameShift\FrameShift.csproj'
    $workerProject = Join-Path $repoRoot 'src\FrameShift.SubtitlesWorker\FrameShift.SubtitlesWorker.csproj'
    $testProject = Join-Path $repoRoot 'tests\FrameShift.Tests\FrameShift.Tests.csproj'
    $changelogPath = Join-Path $repoRoot 'docs\CHANGELOG.md'
    $installerDir = Join-Path $repoRoot 'installer'
    $issFile = Join-Path $installerDir 'FrameShift.iss'
    $publishDir = Join-Path $repoRoot 'publish\FrameShift-win-x64'
    $appSourceDir = Join-Path $repoRoot 'src\FrameShift'

    Write-Host 'Validating release inputs...' -ForegroundColor Cyan
    Assert-RequiredFile -Path $projectFile -Label 'FrameShift project file'
    Assert-RequiredFile -Path $workerProject -Label 'Create Subtitles worker project file'
    Assert-RequiredFile -Path $testProject -Label 'Test project file'
    Assert-RequiredFile -Path $changelogPath -Label 'CHANGELOG.md'
    Assert-RequiredFile -Path $issFile -Label 'Inno Setup script'
    Assert-PublishDirectoryIsSafe -RepositoryRoot $repoRoot -PublishDirectory $publishDir
    Assert-BundledFfmpegPayload -PayloadRoot $appSourceDir -Label 'Bundled FFmpeg source payload'

    $appVersion = Get-ProjectVersion -ProjectFilePath $projectFile
    $installerExe = Join-Path $installerDir ("FrameShift_{0}_Setup.exe" -f $appVersion)
    $changelogContent = Get-Content -LiteralPath $changelogPath -Raw
    if ($changelogContent -notmatch "(?m)^## $([regex]::Escape($appVersion))\s*$") {
        throw "CHANGELOG.md has no '## $appVersion' section. Add a release entry before building."
    }

    $gitStatus = git -C $repoRoot status --porcelain 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git status failed with exit code $LASTEXITCODE."
    }

    if (![string]::IsNullOrWhiteSpace(($gitStatus -join [Environment]::NewLine))) {
        if (!$AllowDirty) {
            throw 'Working tree has uncommitted changes. Commit or stash them, or pass -AllowDirty for an intentional local build.'
        }

        Write-Host 'Proceeding with an explicitly allowed dirty working tree:' -ForegroundColor Yellow
        Write-Host $gitStatus -ForegroundColor Yellow
    }

    Write-Host "Release version: $appVersion" -ForegroundColor Green

    Write-Host 'Restoring locked dependencies...' -ForegroundColor Cyan
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @('restore', $projectFile, '--locked-mode', '--verbosity', 'minimal') -Step 'FrameShift restore'
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @('restore', $workerProject, '--locked-mode', '--verbosity', 'minimal') -Step 'Create Subtitles worker restore'
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @('restore', $testProject, '--locked-mode', '--verbosity', 'minimal') -Step 'Test restore'

    Write-Host 'Running mandatory Release tests...' -ForegroundColor Cyan
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @('test', $testProject, '-c', 'Release', '--no-restore', '--verbosity', 'minimal') -Step 'Release tests'

    Clear-ExpectedPublishDirectory -RepositoryRoot $repoRoot -PublishDirectory $publishDir

    Write-Host "Publishing FrameShift win-x64 self-contained payload v$appVersion..." -ForegroundColor Cyan
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @(
        'publish', $projectFile,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=false',
        '--no-restore',
        '--verbosity', 'minimal',
        '-o', $publishDir
    ) -Step 'FrameShift publish'

    Assert-PublishPayload -PublishDirectory $publishDir
    Write-Host 'Publish payload verified.' -ForegroundColor Green

    $compilerPath = Get-IsccPath -RequestedPath $IsccPath
    $installerBuildStartedAt = [DateTime]::UtcNow
    Write-Host "Compiling installer v$appVersion with $compilerPath ..." -ForegroundColor Cyan
    Invoke-ExternalTool -FilePath $compilerPath -Arguments @(
        "/DMyAppVersion=$appVersion",
        "/DPublishOutputDir=$publishDir",
        $issFile
    ) -Step 'Inno Setup compilation'

    if (!(Test-Path -LiteralPath $installerExe -PathType Leaf)) {
        throw "Installer was not created: $installerExe"
    }

    $installerInfo = Get-Item -LiteralPath $installerExe
    if ($installerInfo.Length -le 0) {
        throw "Installer is empty: $installerExe"
    }

    if ($installerInfo.LastWriteTimeUtc -lt $installerBuildStartedAt.AddSeconds(-2)) {
        throw "Installer was not refreshed by the current Inno Setup compilation: $installerExe"
    }

    Write-Host ''
    Write-Host "Installer ready: $installerExe" -ForegroundColor Green

    if ($RunInstaller) {
        Write-Host 'Launching installer...' -ForegroundColor Cyan
        Start-Process -FilePath $installerExe -WorkingDirectory $installerDir
    }
}
catch {
    Write-Error $_
    exit 1
}

exit 0
