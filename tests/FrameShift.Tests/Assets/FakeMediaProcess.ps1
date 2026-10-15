param(
    [Parameter(Mandatory = $true)]
    [string]$Mode,
    [Parameter(Mandatory = $true)]
    [string]$ReadyPath,
    [string]$ChildReadyPath,
    [string]$OutputPath,
    [int]$Bytes = 0
)

$ErrorActionPreference = 'Stop'

[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($ReadyPath)) | Out-Null
[System.IO.File]::WriteAllText($ReadyPath, [string]$PID)

function Wait-Indefinitely {
    while ($true) {
        Start-Sleep -Milliseconds 100
    }
}

function Quote-ProcessArgument([string]$Value) {
    return '"' + $Value.Replace('"', '\"') + '"'
}

switch ($Mode) {
    'normal' {
        [Console]::Out.Write('normal-output')
        [Console]::Error.Write('normal-error')
        exit 0
    }
    'large' {
        $payloadLength = [Math]::Max($Bytes, 1)
        [Console]::Out.Write((New-Object string('o', $payloadLength)))
        [Console]::Error.Write((New-Object string('e', $payloadLength)))
        exit 0
    }
    'long' {
        Wait-Indefinitely
    }
    'log-then-wait' {
        [Console]::Error.WriteLine('observer line')
        Wait-Indefinitely
    }
    'diagnostic-flood' {
        [Console]::Out.Write(('o' * [Math]::Max($Bytes, 1)))
        [Console]::Error.Write(('e' * [Math]::Max($Bytes, 1)))
        [Console]::Error.Write(' final diagnostic')
        exit 0
    }
    'showinfo-flood' {
        [Console]::Error.WriteLine('[Parsed_showinfo_0] config in time_base: 1/90000, frame_rate: 240/1')
        for ($i = 0; $i -lt 3601; $i++) {
            $ticks = 90000000L + 375L * $i
            $seconds = ($ticks / 90000.0).ToString('0.######', [Globalization.CultureInfo]::InvariantCulture)
            [Console]::Error.WriteLine("[Parsed_showinfo_0] n: $i pts: $ticks pts_time: $seconds duration: 375 duration_time: 0.00416667")
        }
        [Console]::Error.Write(('x' * 1048576))
        [Console]::Error.WriteLine(' final diagnostic')
        exit 0
    }
    'raw-output' {
        $stream = [Console]::OpenStandardOutput()
        $buffer = New-Object byte[] 65536
        while ($true) {
            $stream.Write($buffer, 0, $buffer.Length)
            $stream.Flush()
        }
    }
    'stdin-block' {
        Wait-Indefinitely
    }
    'fail' {
        [Console]::Error.Write('controlled failure')
        exit 1
    }
    'file-lock' {
        if ([string]::IsNullOrWhiteSpace($OutputPath)) {
            throw 'OutputPath is required for file-lock mode.'
        }

        $stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $stream.WriteByte(1)
        $stream.Flush()
        Wait-Indefinitely
    }
    'tree' {
        if ([string]::IsNullOrWhiteSpace($ChildReadyPath)) {
            throw 'ChildReadyPath is required for tree mode.'
        }

        $childArguments = '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File {0} -Mode child -ReadyPath {1}' -f (Quote-ProcessArgument $PSCommandPath), (Quote-ProcessArgument $ChildReadyPath)
        Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $childArguments -WindowStyle Hidden | Out-Null
        Wait-Indefinitely
    }
    'child' {
        Wait-Indefinitely
    }
    default {
        throw "Unsupported mode: $Mode"
    }
}
