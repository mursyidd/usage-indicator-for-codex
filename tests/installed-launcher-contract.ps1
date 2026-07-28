[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstalledLauncher,

    [Parameter(Mandatory)]
    [string]$LauncherProbe
)

$ErrorActionPreference = 'Stop'
$publicExecutableName = 'usage-indicator.exe'
$windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
. (Join-Path $PSScriptRoot '..\scripts\product-metadata.ps1')
$metadata = Get-UsageIndicatorProductMetadata

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Get-PeSubsystem {
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    Assert-True ($bytes.Length -ge 256) "PE file is unexpectedly short: $Path"
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    Assert-True ($peOffset -ge 0 -and $peOffset + 94 -lt $bytes.Length) "Invalid PE header offset: $Path"
    Assert-True (
        $bytes[$peOffset] -eq 0x50 -and
        $bytes[$peOffset + 1] -eq 0x45 -and
        $bytes[$peOffset + 2] -eq 0 -and
        $bytes[$peOffset + 3] -eq 0
    ) "Missing PE signature: $Path"

    $optionalHeader = $peOffset + 24
    $magic = [BitConverter]::ToUInt16($bytes, $optionalHeader)
    Assert-True ($magic -eq 0x10b -or $magic -eq 0x20b) "Unsupported PE optional-header magic: $Path"
    return [BitConverter]::ToUInt16($bytes, $optionalHeader + 68)
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$Arguments,
        [hashtable]$Environment = @{},
        [int]$TimeoutMilliseconds = 10000
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($name in $Environment.Keys) {
        $startInfo.Environment[$name] = [string]$Environment[$name]
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        Assert-True $process.Start() "Process could not be started: $FilePath"
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            $taskkill = Join-Path $env:WINDIR 'System32\taskkill.exe'
            & $taskkill /PID $process.Id /T /F 2>&1 | Out-Null
            [void]$process.WaitForExit(2000)
            throw "Process timed out after $TimeoutMilliseconds ms: $FilePath $Arguments"
        }

        $streamTasks = [Threading.Tasks.Task[]]@($stdoutTask, $stderrTask)
        Assert-True ([Threading.Tasks.Task]::WaitAll($streamTasks, 2000)) "Timed out collecting process output: $FilePath $Arguments"
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout = $stdoutTask.GetAwaiter().GetResult()
            Stderr = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function ConvertTo-EncodedCommand {
    param([Parameter(Mandatory)][string]$Command)
    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
}

function Quote-PowerShellLiteral {
    param([Parameter(Mandatory)][string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

$publicExecutable = (Resolve-Path -LiteralPath $InstalledLauncher).Path
$probeExecutable = (Resolve-Path -LiteralPath $LauncherProbe).Path
Assert-True (Test-Path -LiteralPath $publicExecutable -PathType Leaf) "Missing public launcher: $publicExecutable"
Assert-True ((Get-PeSubsystem $publicExecutable) -eq 3) "$publicExecutableName is not IMAGE_SUBSYSTEM_WINDOWS_CUI."
Assert-True (
    (Get-Item -LiteralPath $publicExecutable).VersionInfo.ProductVersion -ceq $metadata.Version
) "$publicExecutableName product version does not match $($metadata.Version)."

$layoutRoot = Join-Path ([IO.Path]::GetTempPath()) "UsageIndicatorForCodex-InstalledLauncherContract-$([Guid]::NewGuid().ToString('N'))"
$layoutBin = Join-Path $layoutRoot 'bin'
$layoutApp = Join-Path $layoutRoot 'app'
$layoutUpdater = Join-Path $layoutRoot 'updater'
New-Item -ItemType Directory -Path $layoutBin, $layoutApp, $layoutUpdater -Force | Out-Null
$layoutLauncher = Join-Path $layoutBin $publicExecutableName
Copy-Item -LiteralPath $publicExecutable -Destination $layoutLauncher
$probeDirectory = Split-Path -Parent $probeExecutable
foreach ($sourcePath in Get-ChildItem -LiteralPath $probeDirectory -File) {
    Copy-Item -LiteralPath $sourcePath.FullName -Destination (Join-Path $layoutApp $sourcePath.Name)
}
Copy-Item -LiteralPath $probeExecutable -Destination (Join-Path $layoutApp 'UsageIndicatorForCodex.Gui.exe') -Force
Copy-Item -LiteralPath $probeExecutable -Destination (Join-Path $layoutUpdater 'UsageIndicatorForCodex.UpdateHost.exe') -Force

try {
    $expectedHelpLines = @(
        'Usage Indicator for Codex',
        'usage-indicator start',
        'usage-indicator stop',
        'usage-indicator status',
        'usage-indicator version',
        'usage-indicator check-update',
        'usage-indicator update',
        'usage-indicator enable-startup',
        'usage-indicator disable-startup',
        'usage-indicator enable-credit-expiry',
        'usage-indicator disable-credit-expiry',
        'usage-indicator help',
        'Keyboard shortcut:',
        'Ctrl+Alt+U    Turn the indicator display on or off while running',
        'Running usage-indicator without arguments shows this help.'
    )
    $lastHelpLine = $expectedHelpLines[-1]
    $quotedLauncher = Quote-PowerShellLiteral $layoutLauncher
    $helpMarker = "__NEXT_PROMPT_MARKER_$([Guid]::NewGuid().ToString('N'))__"
    $helpCommand = @"
& $quotedLauncher
`$nativeExit = `$LASTEXITCODE
[Console]::Out.WriteLine('$helpMarker')
exit `$nativeExit
"@
    $help = Invoke-CapturedProcess -FilePath $windowsPowerShell -Arguments "-NoLogo -NoProfile -EncodedCommand $(ConvertTo-EncodedCommand $helpCommand)"
    Assert-True ($help.ExitCode -eq 0) "Windows PowerShell no-argument launcher returned $($help.ExitCode), not 0. Output: $($help.Stdout)$($help.Stderr)"
    Assert-True ([string]::IsNullOrEmpty($help.Stderr)) "Windows PowerShell no-argument launcher wrote stderr: $($help.Stderr)"
    $helpLineIndex = $help.Stdout.IndexOf($lastHelpLine, [StringComparison]::Ordinal)
    $helpMarkerIndex = $help.Stdout.IndexOf($helpMarker, [StringComparison]::Ordinal)
    foreach ($expectedHelpLine in $expectedHelpLines) {
        Assert-True ($help.Stdout.Contains($expectedHelpLine)) "Windows PowerShell no-argument help output is missing: $expectedHelpLine"
    }
    Assert-True ($helpLineIndex -ge 0) 'Windows PowerShell no-argument help output was incomplete.'
    Assert-True ($helpMarkerIndex -gt $helpLineIndex) 'Windows PowerShell advanced before no-argument help output completed.'

    $invalidMarker = "__NEXT_PROMPT_MARKER_$([Guid]::NewGuid().ToString('N'))__"
    $invalidCommand = @"
& $quotedLauncher '--definitely-invalid'
`$nativeExit = `$LASTEXITCODE
[Console]::Out.WriteLine('$invalidMarker')
exit `$nativeExit
"@
    $invalid = Invoke-CapturedProcess -FilePath $windowsPowerShell -Arguments "-NoLogo -NoProfile -EncodedCommand $(ConvertTo-EncodedCommand $invalidCommand)"
    Assert-True ($invalid.ExitCode -eq 2) "Invalid argument returned $($invalid.ExitCode), not 2."
    Assert-True ($invalid.Stderr.Contains('Unknown argument: --definitely-invalid')) 'Invalid argument did not write its error to stderr.'
    Assert-True ($invalid.Stderr.Contains($lastHelpLine)) 'Invalid argument stderr did not contain complete help.'
    Assert-True ($invalid.Stdout.Contains($invalidMarker)) 'Windows PowerShell did not reach the post-command marker.'

    $updateStdoutMarker = "__UPDATE_STDOUT_$([Guid]::NewGuid().ToString('N'))__"
    $updateStderrMarker = "__UPDATE_STDERR_$([Guid]::NewGuid().ToString('N'))__"
    $updateNextMarker = "__UPDATE_NEXT_$([Guid]::NewGuid().ToString('N'))__"
    $updateEnvironment = @{
        USAGE_INDICATOR_LAUNCHER_PROBE_OUTPUT = (Join-Path $layoutRoot 'powershell-update.json')
        USAGE_INDICATOR_LAUNCHER_PROBE_EXIT_CODE = '37'
        USAGE_INDICATOR_LAUNCHER_PROBE_STDOUT = $updateStdoutMarker
        USAGE_INDICATOR_LAUNCHER_PROBE_STDERR = $updateStderrMarker
    }
    $updateCommand = @"
& $quotedLauncher 'update'
`$nativeExit = `$LASTEXITCODE
[Console]::Out.WriteLine('$updateNextMarker')
exit `$nativeExit
"@
    $update = Invoke-CapturedProcess `
        -FilePath $windowsPowerShell `
        -Arguments "-NoLogo -NoProfile -EncodedCommand $(ConvertTo-EncodedCommand $updateCommand)" `
        -Environment $updateEnvironment
    Assert-True ($update.ExitCode -eq 37) "PowerShell update returned $($update.ExitCode), not 37."
    Assert-True ($update.Stderr.Contains($updateStderrMarker)) 'PowerShell update lost cached-host stderr.'
    $updateStdoutIndex = $update.Stdout.IndexOf($updateStdoutMarker, [StringComparison]::Ordinal)
    $updateNextIndex = $update.Stdout.IndexOf($updateNextMarker, [StringComparison]::Ordinal)
    Assert-True ($updateStdoutIndex -ge 0) 'PowerShell update lost cached-host stdout.'
    Assert-True ($updateNextIndex -gt $updateStdoutIndex) 'PowerShell advanced before the cached update host completed.'

    $cmdStdoutMarker = "__CMD_UPDATE_STDOUT_$([Guid]::NewGuid().ToString('N'))__"
    $cmdStderrMarker = "__CMD_UPDATE_STDERR_$([Guid]::NewGuid().ToString('N'))__"
    $cmdNextMarker = "__CMD_UPDATE_NEXT_$([Guid]::NewGuid().ToString('N'))__"
    $cmdEnvironment = @{
        USAGE_INDICATOR_LAUNCHER_PROBE_OUTPUT = (Join-Path $layoutRoot 'cmd-update.json')
        USAGE_INDICATOR_LAUNCHER_PROBE_EXIT_CODE = '41'
        USAGE_INDICATOR_LAUNCHER_PROBE_STDOUT = $cmdStdoutMarker
        USAGE_INDICATOR_LAUNCHER_PROBE_STDERR = $cmdStderrMarker
    }
    $cmdArguments = "/d /v:on /s /c `"`"$layoutLauncher`" check-update & set `"nativeExit=!ERRORLEVEL!`" & echo $cmdNextMarker & exit /b !nativeExit!`""
    $cmdUpdate = Invoke-CapturedProcess `
        -FilePath $env:ComSpec `
        -Arguments $cmdArguments `
        -Environment $cmdEnvironment
    Assert-True ($cmdUpdate.ExitCode -eq 41) "cmd.exe check-update returned $($cmdUpdate.ExitCode), not 41."
    Assert-True ($cmdUpdate.Stderr.Contains($cmdStderrMarker)) 'cmd.exe check-update lost cached-host stderr.'
    $cmdStdoutIndex = $cmdUpdate.Stdout.IndexOf($cmdStdoutMarker, [StringComparison]::Ordinal)
    $cmdNextIndex = $cmdUpdate.Stdout.IndexOf($cmdNextMarker, [StringComparison]::Ordinal)
    Assert-True ($cmdStdoutIndex -ge 0) 'cmd.exe check-update lost cached-host stdout.'
    Assert-True ($cmdNextIndex -gt $cmdStdoutIndex) 'cmd.exe advanced before the cached update host completed.'

    $probe = Invoke-CapturedProcess -FilePath $probeExecutable -Arguments "--verify-launcher `"$publicExecutable`"" -TimeoutMilliseconds 30000
    Assert-True ($probe.ExitCode -eq 0) "Launcher probe failed: $($probe.Stdout)$($probe.Stderr)"
}
finally {
    Remove-Item -LiteralPath $layoutRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'PASS installed launcher console, forwarding, exit-code, asynchronous process, and version contract'
