param(
    [string]$ExePath = "E:\AI\FrameShift_V1\src\FrameShift\bin\Debug\net8.0-windows\FrameShift.exe",
    [string]$OutputJson = "E:\AI\FrameShift_V1\tools\scripts\rife_rawvideo_validation.json"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-RifeTempDirs {
    $root = Join-Path $env:TEMP "FrameShift\RifeInterpolation"
    if (-not (Test-Path -LiteralPath $root)) {
        return @()
    }

    return @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
}

function Get-OutputCandidates {
    param([string]$InputPath)

    $inputItem = Get-Item -LiteralPath $InputPath
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($inputItem.Name)
    $ext = $inputItem.Extension
    return @(Get-ChildItem -LiteralPath $inputItem.DirectoryName -File -Filter "$baseName*_rife_x2*$ext" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
}

function Wait-MainWindow {
    param([System.Diagnostics.Process]$Process, [int]$TimeoutSeconds = 20)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $Process.Refresh()
        if ($Process.MainWindowHandle -ne 0) {
            return $true
        }

        Start-Sleep -Milliseconds 250
    }

    return $false
}

function Invoke-CancelButton {
    param([System.Diagnostics.Process]$Process)

    if (-not (Wait-MainWindow -Process $Process)) {
        throw "Main window not available for cancel button invocation."
    }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        "Cancel all")
    $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $button) {
        throw "Cancel button not found."
    }

    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Invoke-CloseWindow {
    param([System.Diagnostics.Process]$Process)

    if (-not (Wait-MainWindow -Process $Process)) {
        throw "Main window not available for close request."
    }

    [void]$Process.CloseMainWindow()
}

function Get-NewFfmpegProcesses {
    param([datetime]$Since)

    return @(Get-CimInstance Win32_Process -Filter "Name = 'ffmpeg.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CreationDate -and ([System.Management.ManagementDateTimeConverter]::ToDateTime($_.CreationDate)) -ge $Since } |
        Select-Object ProcessId, Name, CommandLine)
}

function Get-NewFrameShiftProcesses {
    param([datetime]$Since)

    return @(Get-CimInstance Win32_Process -Filter "Name = 'FrameShift.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CreationDate -and ([System.Management.ManagementDateTimeConverter]::ToDateTime($_.CreationDate)) -ge $Since } |
        Select-Object ProcessId, Name, CommandLine)
}

function Start-RifeProcess {
    param(
        [string]$ExePath,
        [string]$InputPath,
        [string]$Pipeline = "auto"
    )

    $arguments = @(
        "--action", "interpolate-video-rife",
        "--model-id", "rife-v426-x2",
        "--multiplier", "2",
        "--playback-divisor", "1",
        "--interpolate-pipeline", $Pipeline,
        $InputPath
    )

    return Start-Process -FilePath $ExePath -ArgumentList $arguments -PassThru -WindowStyle Normal
}

function Wait-AndCollect {
    param(
        [System.Diagnostics.Process]$Process,
        [datetime]$StartTime,
        [string[]]$TempBefore,
        [string[]]$OutputsBefore,
        [string]$InputPath
    )

    $Process.WaitForExit()
    Start-Sleep -Seconds 2
    $tempAfter = Get-RifeTempDirs
    $outputsAfter = Get-OutputCandidates -InputPath $InputPath
    $newTemps = @($tempAfter | Where-Object { $_ -notin $TempBefore })
    $newOutputs = @($outputsAfter | Where-Object { $_ -notin $OutputsBefore })
    $ffmpegAfter = Get-NewFfmpegProcesses -Since $StartTime
    $frameshiftAfter = Get-NewFrameShiftProcesses -Since $StartTime

    return [pscustomobject]@{
        exitCode = $Process.ExitCode
        newTempDirs = $newTemps
        newOutputs = $newOutputs
        orphanFfmpeg = $ffmpegAfter
        lingeringFrameShift = $frameshiftAfter
    }
}

function Run-CancelScenario {
    param(
        [string]$Name,
        [string]$ExePath,
        [string]$InputPath,
        [string]$Pipeline,
        [ValidateSet("cancel_button","close_window")] [string]$Action,
        [int]$DelaySeconds
    )

    $startTime = Get-Date
    $tempBefore = Get-RifeTempDirs
    $outputsBefore = Get-OutputCandidates -InputPath $InputPath
    $process = Start-RifeProcess -ExePath $ExePath -InputPath $InputPath -Pipeline $Pipeline

    Start-Sleep -Seconds $DelaySeconds
    if ($Action -eq "cancel_button") {
        Invoke-CancelButton -Process $process
    } else {
        Invoke-CloseWindow -Process $process
    }

    $result = Wait-AndCollect -Process $process -StartTime $startTime -TempBefore $tempBefore -OutputsBefore $outputsBefore -InputPath $InputPath

    return [pscustomobject]@{
        name = $Name
        pipeline = $Pipeline
        action = $Action
        delaySeconds = $DelaySeconds
        exitCode = $result.exitCode
        noFreeze = $true
        noNewOutput = ($result.newOutputs.Count -eq 0)
        noResidualTemp = ($result.newTempDirs.Count -eq 0)
        noOrphanFfmpeg = ($result.orphanFfmpeg.Count -eq 0)
        noLingeringFrameShift = ($result.lingeringFrameShift.Count -eq 0)
        residualTempDirs = $result.newTempDirs
        orphanFfmpeg = $result.orphanFfmpeg
        lingeringFrameShift = $result.lingeringFrameShift
    }
}

function Run-AutoFallbackScenario {
    param(
        [string]$ExePath,
        [string]$InputPath
    )

    $startTime = Get-Date
    $tempBefore = Get-RifeTempDirs
    $outputsBefore = Get-OutputCandidates -InputPath $InputPath
    $logPath = Join-Path $env:LOCALAPPDATA "FrameShift\logs\FrameShift_diagnostic.log"
    $logBeforeLength = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).Length } else { 0 }

    $process = Start-RifeProcess -ExePath $ExePath -InputPath $InputPath -Pipeline "auto"
    Start-Sleep -Seconds 2

    $encoder = Get-CimInstance Win32_Process -Filter "Name = 'ffmpeg.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ParentProcessId -eq $process.Id -and
            $_.CommandLine -match "-f rawvideo" -and
            $_.CommandLine -match "\s-i\s-"
        } |
        Select-Object -First 1

    if ($null -eq $encoder) {
        throw "Could not find rawvideo encoder ffmpeg process to force fallback."
    }

    Stop-Process -Id $encoder.ProcessId -Force
    $result = Wait-AndCollect -Process $process -StartTime $startTime -TempBefore $tempBefore -OutputsBefore $outputsBefore -InputPath $InputPath

    $logAfter = if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Raw } else { "" }
    $logTail = if ($logAfter.Length -gt $logBeforeLength) { $logAfter.Substring([int]$logBeforeLength) } else { "" }
    $fallbackLogged = $logTail -match "Falling back to BMP pipeline"

    foreach ($output in $result.newOutputs) {
        Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
    }

    return [pscustomobject]@{
        name = "auto_fallback_on_rawvideo_failure"
        exitCode = $result.exitCode
        producedOutput = ($result.newOutputs.Count -gt 0)
        fallbackLogged = $fallbackLogged
        noResidualTemp = ($result.newTempDirs.Count -eq 0)
        noOrphanFfmpeg = ($result.orphanFfmpeg.Count -eq 0)
        noLingeringFrameShift = ($result.lingeringFrameShift.Count -eq 0)
        residualTempDirs = $result.newTempDirs
        orphanFfmpeg = $result.orphanFfmpeg
    }
}

$results = @()
$results += Run-CancelScenario -Name "cancel_long_rawvideo_inference" -ExePath $ExePath -InputPath "E:\AI\FrameShift_V1\tests\input\video217s_25fps_sound_1080p.mp4" -Pipeline "rawvideo" -Action "cancel_button" -DelaySeconds 10
$results += Run-CancelScenario -Name "close_window_rawvideo_medium" -ExePath $ExePath -InputPath "E:\AI\FrameShift_V1\tests\input\video13s_25fps_sound_1080p.mp4" -Pipeline "rawvideo" -Action "close_window" -DelaySeconds 5
$results += Run-CancelScenario -Name "cancel_rawvideo_late_medium" -ExePath $ExePath -InputPath "E:\AI\FrameShift_V1\tests\input\video13s_25fps_sound_1080p.mp4" -Pipeline "rawvideo" -Action "cancel_button" -DelaySeconds 28
$results += Run-AutoFallbackScenario -ExePath $ExePath -InputPath "E:\AI\FrameShift_V1\tests\input\Video5s_16fps_no_sound-1280p.mp4"

$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputJson -Encoding UTF8
$results | Format-Table name,exitCode,noFreeze,noNewOutput,noResidualTemp,noOrphanFfmpeg,noLingeringFrameShift,producedOutput,fallbackLogged -AutoSize
