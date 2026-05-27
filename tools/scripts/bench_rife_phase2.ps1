param(
    [string]$ExePath = "E:\AI\FrameShift_V1\src\FrameShift\bin\Debug\net8.0-windows\FrameShift.exe",
    [string[]]$Inputs = @(
        "E:\AI\FrameShift_V1\tests\input\Video5s_16fps_no_sound-1280p.mp4",
        "E:\AI\FrameShift_V1\tests\input\video13s_25fps_sound_1080p.mp4",
        "E:\AI\FrameShift_V1\tests\input\video217s_25fps_sound_1080p.mp4"
    ),
    [string]$Pipeline = "bmp",
    [string]$OutputJson = "E:\AI\FrameShift_V1\tools\scripts\rife_phase2_bench_results.json"
)

$ErrorActionPreference = "Stop"

function Get-ReportValue {
    param(
        [string[]]$Lines,
        [string]$Prefix
    )

    $line = $Lines | Where-Object { $_.StartsWith($Prefix, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if (-not $line) {
        return $null
    }

    return $line.Substring($Prefix.Length).Trim()
}

function Get-Ms {
    param([string]$Text)
    if (-not $Text) {
        return $null
    }

    $match = [regex]::Match($Text, '([0-9]+(?:\.[0-9]+)?)\s*ms')
    if (-not $match.Success) {
        return $null
    }

    return [double]::Parse($match.Groups[1].Value, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-IntPrefix {
    param([string]$Text)
    if (-not $Text) {
        return $null
    }

    $match = [regex]::Match($Text, '^([0-9]+)')
    if (-not $match.Success) {
        return $null
    }

    return [int]$match.Groups[1].Value
}

function Invoke-RifeBench {
    param(
        [string]$InputPath,
        [string]$ExePath
    )

    $inputItem = Get-Item -LiteralPath $InputPath
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("FrameShift_RifeBench_" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot | Out-Null

    $tempInput = Join-Path $tempRoot $inputItem.Name
    Copy-Item -LiteralPath $inputItem.FullName -Destination $tempInput

    $reportFileName = if ($Pipeline -ieq "rawvideo") { "RifeRawVideoPerformanceReport_latest.txt" } else { "RifePerformanceReport_latest.txt" }
    $reportPath = Join-Path $env:LOCALAPPDATA "FrameShift\logs\$reportFileName"
    if (Test-Path -LiteralPath $reportPath) {
        Remove-Item -LiteralPath $reportPath -Force
    }

    $arguments = @(
        "--action", "interpolate-video-rife",
        "--model-id", "rife-v426-x2",
        "--multiplier", "2",
        "--playback-divisor", "1",
        "--interpolate-pipeline", $Pipeline,
        $tempInput
    )

    $process = Start-Process -FilePath $ExePath -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $peakWorkingSetBytes = 0L
    while (-not $process.HasExited) {
        try {
            $process.Refresh()
            if ($process.WorkingSet64 -gt $peakWorkingSetBytes) {
                $peakWorkingSetBytes = $process.WorkingSet64
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 200
    }
    $process.WaitForExit()

    if (-not (Test-Path -LiteralPath $reportPath)) {
        throw "RIFE report not found after benchmark run: $reportPath"
    }

    $lines = Get-Content -LiteralPath $reportPath
    if ($Pipeline -ieq "rawvideo") {
        $totalMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Total time:")
        $imageToTensorMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Image -> tensor:")
        $tensorToImageMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Tensor -> rawvideo:")
        $onnxMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "ONNX inference:")
        $decodeMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Decode / rawvideo input:")
        $encodeMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Encode / rawvideo output:")
        $pairCount = $null
        $encodedFrameCount = Get-IntPrefix (Get-ReportValue -Lines $lines -Prefix "Output frames:")
        $frameReadMs = $decodeMs
        $frameWriteMs = $encodeMs
        $extractMs = 0
        $reconstructMs = 0
        $audioExpected = (Get-ReportValue -Lines $lines -Prefix "Audio expected:") -eq "True"
        $audioPreservedReport = (Get-ReportValue -Lines $lines -Prefix "Audio preserved:") -eq "True"
        $sourceDurationSeconds = [double]::Parse(((Get-ReportValue -Lines $lines -Prefix "Source duration:") -replace '\s*s$',''), [System.Globalization.CultureInfo]::InvariantCulture)
        $outputDurationSeconds = [double]::Parse(((Get-ReportValue -Lines $lines -Prefix "Output duration:") -replace '\s*s$',''), [System.Globalization.CultureInfo]::InvariantCulture)
    } else {
        $totalMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Total time:")
        $imageToTensorMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Image -> tensor:")
        $tensorToImageMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Tensor -> image:")
        $onnxMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "ONNX inference:")
        $frameReadMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Frame read:")
        $frameWriteMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Temporary frame write:")
        $extractMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Extraction / FFmpeg decode:")
        $reconstructMs = Get-Ms (Get-ReportValue -Lines $lines -Prefix "Reconstruction / FFmpeg encode:")
        $pairCount = Get-IntPrefix (Get-ReportValue -Lines $lines -Prefix "Pairs processed:")
        $encodedFrameCount = Get-IntPrefix (Get-ReportValue -Lines $lines -Prefix "Encoded temporary frames:")
        $audioExpected = $inputItem.Name -notmatch 'no_sound'
        $audioPreservedReport = $null
        $sourceDurationSeconds = $null
        $outputDurationSeconds = $null
    }
    $provider = Get-ReportValue -Lines $lines -Prefix "Provider:"
    $vramApprox = Get-ReportValue -Lines $lines -Prefix "Approx VRAM lower bound:"
    $peakWorkingSetMb = if ($peakWorkingSetBytes -gt 0) { [math]::Round($peakWorkingSetBytes / 1MB, 2) } else { $null }
    $realFps = if ($totalMs -and $encodedFrameCount) { [math]::Round(($encodedFrameCount * 1000.0) / $totalMs, 2) } else { $null }
    $outputItem = Get-ChildItem -LiteralPath $tempRoot -File | Where-Object { $_.Name -like '*_rife_x2*' } | Select-Object -First 1

    $outputDurationProbeSeconds = $null
    $outputHasAudio = $null
    $syncDeltaMs = $null
    if ($outputItem) {
        $ffprobePath = Join-Path (Split-Path -Parent $ExePath) 'Tools\ffmpeg\ffprobe.exe'
        if (Test-Path -LiteralPath $ffprobePath) {
            $ffprobeJson = & $ffprobePath -v error -show_entries format=duration:stream=codec_type -of json $outputItem.FullName
            $probe = $ffprobeJson | ConvertFrom-Json
            if ($probe.format.duration) {
                $outputDurationProbeSeconds = [double]::Parse($probe.format.duration, [System.Globalization.CultureInfo]::InvariantCulture)
            }
            if ($probe.streams) {
                $outputHasAudio = @($probe.streams | Where-Object { $_.codec_type -eq 'audio' }).Count -gt 0
            }
        }
    }

    if ($outputDurationProbeSeconds -ne $null) {
        if ($sourceDurationSeconds -eq $null) {
            $ffprobePath = Join-Path (Split-Path -Parent $ExePath) 'Tools\ffmpeg\ffprobe.exe'
            $sourceJson = & $ffprobePath -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 $tempInput
            if ($sourceJson) {
                $sourceDurationSeconds = [double]::Parse($sourceJson.Trim(), [System.Globalization.CultureInfo]::InvariantCulture)
            }
        }
        if ($sourceDurationSeconds -ne $null) {
            $syncDeltaMs = [math]::Round(([math]::Abs($outputDurationProbeSeconds - $sourceDurationSeconds) * 1000.0), 2)
        }
    }

    $result = [ordered]@{
        input = $inputItem.FullName
        pipeline = $Pipeline
        sourceBytes = $inputItem.Length
        exitCode = $process.ExitCode
        provider = $provider
        totalMs = $totalMs
        realFps = $realFps
        pairCount = $pairCount
        encodedFrameCount = $encodedFrameCount
        imageToTensorMs = $imageToTensorMs
        tensorToImageMs = $tensorToImageMs
        onnxMs = $onnxMs
        ioMs = if ($extractMs -or $frameReadMs -or $frameWriteMs -or $reconstructMs) {
            ($extractMs + $frameReadMs + $frameWriteMs + $reconstructMs)
        } else {
            $null
        }
        extractMs = $extractMs
        frameReadMs = $frameReadMs
        frameWriteMs = $frameWriteMs
        reconstructMs = $reconstructMs
        peakWorkingSetMb = $peakWorkingSetMb
        vramApprox = $vramApprox
        audioExpected = $audioExpected
        audioPreservedReport = $audioPreservedReport
        outputHasAudio = $outputHasAudio
        sourceDurationSeconds = $sourceDurationSeconds
        outputDurationSeconds = $outputDurationProbeSeconds
        syncDeltaMs = $syncDeltaMs
        reportPath = $reportPath
    }

    Remove-Item -LiteralPath $tempRoot -Recurse -Force
    return [pscustomobject]$result
}

$results = foreach ($input in $Inputs) {
    Invoke-RifeBench -InputPath $input -ExePath $ExePath
}

$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputJson -Encoding UTF8
$results | Format-Table input,provider,totalMs,realFps,imageToTensorMs,tensorToImageMs,onnxMs,ioMs,peakWorkingSetMb -AutoSize
