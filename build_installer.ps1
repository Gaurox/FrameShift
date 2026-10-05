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

$script:DistributionNoticeFiles = @(
    @{ Source = 'LICENSE'; Published = 'licenses\LICENSE' },
    @{ Source = 'THIRD_PARTY_NOTICES.md'; Published = 'licenses\THIRD_PARTY_NOTICES.md' },
    @{ Source = 'src\FrameShift.SubtitlesWorker\native-dml\THIRD_PARTY_NOTICES.txt'; Published = 'licenses\subtitles-worker-native\THIRD_PARTY_NOTICES.txt' },
    @{ Source = 'licenses\subtitles-worker-native\APACHE-2.0.txt'; Published = 'licenses\subtitles-worker-native\APACHE-2.0.txt' },
    @{ Source = 'licenses\subtitles-worker-native\DirectML-LICENSE.txt'; Published = 'licenses\subtitles-worker-native\DirectML-LICENSE.txt' },
    @{ Source = 'licenses\subtitles-worker-native\DirectML-THIRD_PARTY_NOTICES.txt'; Published = 'licenses\subtitles-worker-native\DirectML-THIRD_PARTY_NOTICES.txt' }
)

$ocrNoticeNames = @(
    'APACHE-2.0.txt', 'Clipper2-LICENSE.txt', 'GlyphLessFont-NOTICE.txt', 'PDFium-LICENSE.txt', 'YamlDotNet-LICENSE.txt',
    'pdfium-native\LICENSE', 'pdfium-native\pdfium.txt', 'pdfium-native\abseil.txt', 'pdfium-native\agg23.txt',
    'pdfium-native\fast_float.txt', 'pdfium-native\freetype.txt', 'pdfium-native\icu.txt', 'pdfium-native\lcms.txt',
    'pdfium-native\libjpeg_turbo.ijg', 'pdfium-native\libjpeg_turbo.md', 'pdfium-native\libopenjpeg.txt',
    'pdfium-native\libpng.txt', 'pdfium-native\llvm-libc.txt', 'pdfium-native\simdutf.txt', 'pdfium-native\zlib.txt'
)
foreach ($noticeName in $ocrNoticeNames) {
    $relativePath = Join-Path 'licenses\ocr' $noticeName
    $script:DistributionNoticeFiles += @{ Source = $relativePath; Published = $relativePath }
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

function Assert-DistributionNoticeSources {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    foreach ($noticeFile in $script:DistributionNoticeFiles) {
        $sourcePath = Join-Path $RepositoryRoot $noticeFile.Source
        Assert-RequiredFile -Path $sourcePath -Label "Distribution notice source $($noticeFile.Source)"
    }
}

function Assert-DistributionNoticePayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    foreach ($noticeFile in $script:DistributionNoticeFiles) {
        $publishedPath = Join-Path $PublishDirectory $noticeFile.Published
        Assert-RequiredFile -Path $publishedPath -Label "Publish distribution notice $($noticeFile.Published)"
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
        'PDFiumCore.dll',
        'pdfium.dll',
        'YamlDotNet.dll',
        'Clipper2Lib.dll',
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
    Assert-DistributionNoticePayload -PublishDirectory $PublishDirectory
    Assert-ManagedPayload -Directory $PublishDirectory -Name 'FrameShift' -Desktop
    Assert-ManagedPayload -Directory (Join-Path $PublishDirectory 'Workers\CreateSubtitlesWorker') -Name 'FrameShift.SubtitlesWorker'
}

function Assert-ManagedPayload {
    param([string]$Directory, [string]$Name, [switch]$Desktop)

    $runtimeVersion = '8.0.31'
    $configPath = Join-Path $Directory "$Name.runtimeconfig.json"
    $depsPath = Join-Path $Directory "$Name.deps.json"
    Assert-RequiredFile -Path $configPath -Label 'Runtime manifest'
    Assert-RequiredFile -Path $depsPath -Label 'Dependency manifest'
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
    $expected = @('Microsoft.NETCore.App')
    if ($Desktop) { $expected += 'Microsoft.WindowsDesktop.App' }
    $frameworks = @($config.runtimeOptions.includedFrameworks)
    if ($frameworks.Count -ne $expected.Count) { throw "Unexpected runtime frameworks in $configPath" }
    foreach ($framework in $expected) {
        $match = @($frameworks | Where-Object { $_.name -eq $framework -and $_.version -eq $runtimeVersion })
        if ($match.Count -ne 1) { throw "Runtime $framework must be $runtimeVersion in $configPath" }
    }
    if ($deps.runtimeTarget.name -notlike '*/win-x64') { throw "Payload must target win-x64: $depsPath" }
    $runtimeLibraries = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*' })
    if ($runtimeLibraries.Count -ne 1 -or $runtimeLibraries[0] -ne "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/$runtimeVersion") {
        throw "Runtime dependency manifest mismatch: $depsPath"
    }
    $binaryVersions = @{
        'coreclr.dll' = '8.0.3126.42015'
        'System.Private.CoreLib.dll' = '8.0.3126.42015'
    }
    if ($Desktop) {
        $desktopLibraries = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/*' })
        if ($desktopLibraries.Count -ne 1 -or $desktopLibraries[0] -ne "runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/$runtimeVersion") {
            throw "Desktop dependency manifest mismatch: $depsPath"
        }
        $binaryVersions['System.Windows.Forms.dll'] = '8.0.3126.42106'
        $imageLibraries = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'SixLabors.ImageSharp/*' })
        if ($imageLibraries.Count -ne 1 -or $imageLibraries[0] -ne 'SixLabors.ImageSharp/4.1.2') {
            throw "ImageSharp 4.1.2 is required before distribution: $depsPath"
        }
        Assert-RequiredFile -Path (Join-Path $Directory 'SixLabors.ImageSharp.dll') -Label 'ImageSharp binary'
        $binaryVersions['SixLabors.ImageSharp.dll'] = '4.1.2.0'
    }
    foreach ($binary in $binaryVersions.Keys) {
        $path = Join-Path $Directory $binary
        Assert-RequiredFile -Path $path -Label 'Runtime binary'
        $actual = (([string][Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion -split ' ')[0]).Replace(',', '.')
        if ($actual -ne $binaryVersions[$binary]) { throw "Runtime binary version mismatch for $path : $actual" }
    }
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
    Assert-DistributionNoticeSources -RepositoryRoot $repoRoot

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
    # A locally prepared official SDK is convenient without changing the machine installation.
    $localSdk = Join-Path $repoRoot '.buildcheck\sdk\8.0.425'
    if (Test-Path -LiteralPath (Join-Path $localSdk 'dotnet.exe') -PathType Leaf) {
        $env:PATH = "$localSdk;$env:PATH"
        $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.buildcheck\dotnet-home'
    }
    $sdkVersion = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -ne '8.0.425') { throw 'The release requires .NET SDK 8.0.425.' }
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @('restore', $projectFile, '-p:Configuration=Release', '--locked-mode', '--verbosity', 'minimal') -Step 'FrameShift restore'
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @('restore', $workerProject, '-p:Configuration=Release', '--locked-mode', '--verbosity', 'minimal') -Step 'Create Subtitles worker restore'
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @('restore', $testProject, '-p:Configuration=Release', '--locked-mode', '--verbosity', 'minimal') -Step 'Test restore'

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
