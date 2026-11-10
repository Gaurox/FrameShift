# Native menu build/verification only. Never registers COM or launches an installer.
function Get-ShellFileHash {
    param([string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-', '') }
    finally { $hasher.Dispose(); $stream.Dispose() }
}

function Restore-ShellMenuProjects {
    param([string]$RepositoryRoot)
    foreach ($relative in @('src/FrameShift.ShellMenu/FrameShift.ShellMenu.csproj',
        'tests/FrameShift.ShellMenu.Fixture/FrameShift.ShellMenu.Fixture.csproj',
        'tests/FrameShift.ShellMenu.Probe/FrameShift.ShellMenu.Probe.csproj')) {
        $project = Join-Path $RepositoryRoot $relative
        Assert-RequiredFile -Path $project -Label 'Shell menu project'
        Invoke-ExternalTool -FilePath 'dotnet' -Arguments @('restore', $project, '-p:Configuration=Release', '--locked-mode', '--verbosity', 'minimal') -Step 'Shell menu restore'
    }
}

function Publish-ShellMenuPayload {
    param([string]$RepositoryRoot, [string]$PublishDirectory, [string]$Version)
    $work = Join-Path $RepositoryRoot ('scratch/shell-menu-build/' + [Guid]::NewGuid().ToString('N'))
    $native = Join-Path $work 'native'
    $fixture = Join-Path $work 'fixture'
    $project = Join-Path $RepositoryRoot 'src/FrameShift.ShellMenu/FrameShift.ShellMenu.csproj'
    $fixtureProject = Join-Path $RepositoryRoot 'tests/FrameShift.ShellMenu.Fixture/FrameShift.ShellMenu.Fixture.csproj'
    $probeProject = Join-Path $RepositoryRoot 'tests/FrameShift.ShellMenu.Probe/FrameShift.ShellMenu.Probe.csproj'
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @('publish', $project, '-c', 'Release', '--no-restore', "-p:Version=$Version", '-o', $native) -Step 'Native Shell publish'
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @('publish', $fixtureProject, '-c', 'Release', '--no-restore', "-p:Version=$Version", '-o', $fixture) -Step 'Native Shell fixture publish'
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @('build', $probeProject, '-c', 'Release', '--no-restore') -Step 'Native Shell probe build'
    $dll = Join-Path $native 'FrameShift.ShellMenu.dll'
    $probeDll = Join-Path $RepositoryRoot 'tests/FrameShift.ShellMenu.Probe/bin/Release/net8.0-windows/FrameShift.ShellMenu.Probe.dll'
    $reportPath = Join-Path $work 'native-probe.json'
    Invoke-ExternalTool -FilePath 'dotnet' -Arguments @($probeDll, '--native', $dll, '--fixture', (Join-Path $fixture 'FrameShift.ShellMenu.Fixture.dll'), '--report', $reportPath, '--source-root', $RepositoryRoot) -Step 'Unregistered native menu validation'
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $hash = (Get-ShellFileHash -Path $dll).ToLowerInvariant()
    if ($report.Failed -ne 0 -or $report.Passed -lt 23 -or $report.ProductionSha256 -ne $hash) { throw 'Native menu validation is incomplete or mismatched.' }
    $assets = Get-Content -LiteralPath (Join-Path (Split-Path $project) 'obj/project.assets.json') -Raw | ConvertFrom-Json
    $packages = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
    $compilerKey = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -eq 'runtime.win-x64.Microsoft.DotNet.ILCompiler/8.0.33' })
    if ($compilerKey.Count -ne 1) { throw 'Native Shell compiler must be 8.0.33.' }
    $compilerPackage = Join-Path $packages 'runtime.win-x64.microsoft.dotnet.ilcompiler/8.0.33'
    foreach ($notice in @(@('LICENSE.TXT','LICENSE.txt'), @('THIRD-PARTY-NOTICES.TXT','THIRD-PARTY-NOTICES.txt'))) {
        $bundled = Join-Path $RepositoryRoot ('licenses/shell-native/' + $notice[1])
        if ((Get-ShellFileHash -Path $bundled) -ne (Get-ShellFileHash -Path (Join-Path $compilerPackage $notice[0]))) { throw 'Native Shell distribution notices do not match the compiler package.' }
    }
    $destination = Join-Path $PublishDirectory ('Shell/' + $hash)
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $dll -Destination $destination
    $sourceFiles = @(Get-ChildItem -LiteralPath (Split-Path $project) -File | Where-Object { $_.Extension -in @('.cs','.csproj','.targets','.json') } | ForEach-Object {
        @{ Name = $_.Name; Sha256 = (Get-ShellFileHash -Path $_.FullName) }
    })
    $manifest = [ordered]@{
        SchemaVersion = 1; Version = $Version; BuildId = $hash; DllSha256 = $hash
        MenuSchemaVersion = 2; Clsid = '{F6740A5A-42BC-4716-9512-531F0B254862}'
        SdkVersion = '8.0.427'; AotCompilerVersion = '8.0.33'
        CompilerPackageSha512 = $assets.libraries.'runtime.win-x64.Microsoft.DotNet.ILCompiler/8.0.33'.sha512
        NativeProbePassed = $report.Passed; NativeProbeSha256 = (Get-ShellFileHash -Path $reportPath)
        SourceFiles = $sourceFiles; Scope = 'Adjacent classic/AI roots for video, audio, image and PDF OCR; registered Explorer and installer acceptance still required'
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'component.json') -Encoding UTF8
    Assert-ShellMenuPayload -PublishDirectory $PublishDirectory -Version $Version
}

function Get-ShellMenuManifest {
    param([string]$PublishDirectory)
    $root = Join-Path $PublishDirectory 'Shell'
    if (!(Test-Path -LiteralPath $root -PathType Container)) { throw 'Native Shell payload is missing.' }
    $manifests = @(Get-ChildItem -LiteralPath $root -Filter component.json -File -Recurse)
    if ($manifests.Count -ne 1) { throw 'Exactly one native Shell component is required.' }
    return $manifests[0]
}

function Assert-ShellMenuPayload {
    param([string]$PublishDirectory, [string]$Version)
    $file = Get-ShellMenuManifest -PublishDirectory $PublishDirectory
    $manifest = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($manifest.SchemaVersion -ne 1 -or $manifest.Version -ne $Version -or $manifest.SdkVersion -ne '8.0.427' -or
        $manifest.AotCompilerVersion -ne '8.0.33' -or $manifest.NativeProbePassed -lt 23 -or
        $manifest.MenuSchemaVersion -ne 2 -or $manifest.Clsid -ne '{F6740A5A-42BC-4716-9512-531F0B254862}' -or
        $manifest.BuildId -cnotmatch '^[a-f0-9]{64}$' -or $file.Directory.Name -cne $manifest.BuildId) {
        throw 'Native Shell manifest/version mismatch.'
    }
    $dll = Join-Path $file.Directory.FullName 'FrameShift.ShellMenu.dll'
    if ((Get-ShellFileHash -Path $dll) -ne $manifest.DllSha256 -or $manifest.DllSha256 -ne $manifest.BuildId) { throw 'Native Shell hash mismatch.' }
    $files = @(Get-ChildItem -LiteralPath (Join-Path $PublishDirectory 'Shell') -File -Recurse)
    if ($files.Count -ne 2) { throw 'Unexpected files in native Shell payload.' }
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($dll))
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'Invalid native Shell PE header.' }
        $reader.BaseStream.Position = 0x3C
        $pe = $reader.ReadInt32()
        if ($pe -lt 64 -or $pe + 264 -gt $reader.BaseStream.Length) { throw 'Invalid native Shell PE offset.' }
        $reader.BaseStream.Position = $pe
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Native Shell must be x64.' }
        $reader.BaseStream.Position = $pe + 22
        if (($reader.ReadUInt16() -band 0x2000) -eq 0) { throw 'Native Shell is not a DLL.' }
        $reader.BaseStream.Position = $pe + 24
        if ($reader.ReadUInt16() -ne 0x20B) { throw 'Native Shell must be PE32+.' }
        $reader.BaseStream.Position = $pe + 24 + 112 + 14 * 8
        if ($reader.ReadUInt64() -ne 0) { throw 'Native Shell must not contain a managed CLR header.' }
    }
    finally { $reader.Dispose() }
}
