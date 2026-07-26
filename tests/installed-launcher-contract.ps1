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
        [int]$TimeoutMilliseconds = 10000
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
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
New-Item -ItemType Directory -Path $layoutBin, $layoutApp -Force | Out-Null
$layoutLauncher = Join-Path $layoutBin $publicExecutableName
Copy-Item -LiteralPath $publicExecutable -Destination $layoutLauncher
$probeDirectory = Split-Path -Parent $probeExecutable
foreach ($sourcePath in Get-ChildItem -LiteralPath $probeDirectory -File) {
    Copy-Item -LiteralPath $sourcePath.FullName -Destination (Join-Path $layoutApp $sourcePath.Name)
}
Copy-Item -LiteralPath $probeExecutable -Destination (Join-Path $layoutApp 'UsageIndicatorForCodex.Gui.exe') -Force

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

    $probe = Invoke-CapturedProcess -FilePath $probeExecutable -Arguments "--verify-launcher `"$publicExecutable`"" -TimeoutMilliseconds 30000
    Assert-True ($probe.ExitCode -eq 0) "Launcher probe failed: $($probe.Stdout)$($probe.Stderr)"
}
finally {
    Remove-Item -LiteralPath $layoutRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'PASS installed launcher console, forwarding, exit-code, asynchronous process, and version contract'
