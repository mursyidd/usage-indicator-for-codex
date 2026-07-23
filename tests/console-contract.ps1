[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,

    [Parameter(Mandatory)]
    [string]$LauncherProbe,

    [string]$ArchivePath,

    [string]$InstalledLauncher
)

$ErrorActionPreference = 'Stop'
$publicExecutableName = 'UsageIndicatorForCodex.exe'
$guiExecutableName = 'UsageIndicatorForCodex.Gui.exe'
$windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
. (Join-Path $PSScriptRoot '..\scripts\product-metadata.ps1')
$metadata = Get-UsageIndicatorProductMetadata

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

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
        Assert-True (
            [Threading.Tasks.Task]::WaitAll($streamTasks, 2000)
        ) "Timed out collecting process output: $FilePath $Arguments"
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout = $stdout
            Stderr = $stderr
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

$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$publicExecutable = Join-Path $publishRoot $publicExecutableName
$guiExecutable = Join-Path $publishRoot $guiExecutableName
$probeExecutable = (Resolve-Path -LiteralPath $LauncherProbe).Path

Assert-True (Test-Path -LiteralPath $publicExecutable -PathType Leaf) "Missing public launcher: $publicExecutable"
Assert-True ((Get-PeSubsystem $publicExecutable) -eq 3) "$publicExecutableName is not IMAGE_SUBSYSTEM_WINDOWS_CUI."
Assert-True (
    (Get-Item -LiteralPath $publicExecutable).VersionInfo.ProductVersion -ceq $metadata.Version
) "$publicExecutableName product version does not match $($metadata.Version)."
Assert-True (Test-Path -LiteralPath $guiExecutable -PathType Leaf) "Missing GUI executable: $guiExecutable"
Assert-True ((Get-PeSubsystem $guiExecutable) -eq 2) "$guiExecutableName is not IMAGE_SUBSYSTEM_WINDOWS_GUI."

$helpMarker = "__NEXT_PROMPT_MARKER_$([Guid]::NewGuid().ToString('N'))__"
$quotedPublicExecutable = Quote-PowerShellLiteral $publicExecutable
$helpCommand = @"
& $quotedPublicExecutable --help
`$nativeExit = `$LASTEXITCODE
[Console]::Out.WriteLine('$helpMarker')
exit `$nativeExit
"@
$help = Invoke-CapturedProcess `
    -FilePath $windowsPowerShell `
    -Arguments "-NoLogo -NoProfile -EncodedCommand $(ConvertTo-EncodedCommand $helpCommand)"
Assert-True ($help.ExitCode -eq 0) "Windows PowerShell --help returned $($help.ExitCode), not 0."
Assert-True ([string]::IsNullOrEmpty($help.Stderr)) "Windows PowerShell --help wrote stderr: $($help.Stderr)"
$lastHelpLine = '--install registers automatic startup only; it does not launch the application.'
$helpLineIndex = $help.Stdout.IndexOf($lastHelpLine, [StringComparison]::Ordinal)
$helpMarkerIndex = $help.Stdout.IndexOf($helpMarker, [StringComparison]::Ordinal)
Assert-True ($helpLineIndex -ge 0) 'Windows PowerShell --help output was incomplete.'
Assert-True ($helpMarkerIndex -gt $helpLineIndex) 'Windows PowerShell advanced before --help output completed.'

$invalidMarker = "__NEXT_PROMPT_MARKER_$([Guid]::NewGuid().ToString('N'))__"
$invalidCommand = @"
& $quotedPublicExecutable '--definitely-invalid'
`$nativeExit = `$LASTEXITCODE
[Console]::Out.WriteLine('$invalidMarker')
exit `$nativeExit
"@
$invalid = Invoke-CapturedProcess `
    -FilePath $windowsPowerShell `
    -Arguments "-NoLogo -NoProfile -EncodedCommand $(ConvertTo-EncodedCommand $invalidCommand)"
Assert-True ($invalid.ExitCode -eq 2) "Invalid argument returned $($invalid.ExitCode), not 2."
Assert-True ($invalid.Stderr.Contains('Unknown argument: --definitely-invalid')) 'Invalid argument did not write its error to stderr.'
Assert-True ($invalid.Stderr.Contains($lastHelpLine)) 'Invalid argument stderr did not contain complete help.'
Assert-True ($invalid.Stdout.Contains($invalidMarker)) 'Windows PowerShell did not reach the post-command marker.'

$probe = Invoke-CapturedProcess `
    -FilePath $probeExecutable `
    -Arguments "--verify-launcher `"$publicExecutable`"" `
    -TimeoutMilliseconds 30000
Assert-True ($probe.ExitCode -eq 0) "Launcher probe failed: $($probe.Stdout)$($probe.Stderr)"

if (-not [string]::IsNullOrWhiteSpace($InstalledLauncher)) {
    $installedExecutable = (Resolve-Path -LiteralPath $InstalledLauncher).Path
    Assert-True ((Get-PeSubsystem $installedExecutable) -eq 3) 'usage-indicator.exe is not IMAGE_SUBSYSTEM_WINDOWS_CUI.'
    Assert-True (
        (Get-Item -LiteralPath $installedExecutable).VersionInfo.ProductVersion -ceq $metadata.Version
    ) "usage-indicator.exe product version does not match $($metadata.Version)."
    $installedProbe = Invoke-CapturedProcess `
        -FilePath $probeExecutable `
        -Arguments "--verify-launcher Installed `"$installedExecutable`"" `
        -TimeoutMilliseconds 30000
    Assert-True ($installedProbe.ExitCode -eq 0) "Installed launcher probe failed: $($installedProbe.Stdout)$($installedProbe.Stderr)"
}

if (-not [string]::IsNullOrWhiteSpace($ArchivePath)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
    $archive = [IO.Compression.ZipFile]::OpenRead($resolvedArchive)
    try {
        $entryNames = @($archive.Entries | ForEach-Object FullName)
        Assert-True ($publicExecutableName -cin $entryNames) "Archive is missing $publicExecutableName."
        Assert-True ($guiExecutableName -cin $entryNames) "Archive is missing $guiExecutableName."

        $prohibitedPathComponents = @('.git', 'src', 'tests', 'bin', 'obj')
        $prohibitedExtensions = @('.cs', '.csproj', '.sln', '.slnx', '.xaml', '.props', '.targets', '.pdb')
        foreach ($entry in $archive.Entries) {
            $components = $entry.FullName -split '[\\/]'
            Assert-True (-not ($components | Where-Object { $_ -in $prohibitedPathComponents })) `
                "Archive contains a prohibited path: $($entry.FullName)"
            Assert-True ([IO.Path]::GetExtension($entry.FullName) -notin $prohibitedExtensions) `
                "Archive contains a prohibited file: $($entry.FullName)"
            Assert-True ([IO.Path]::GetFileName($entry.FullName) -notmatch '\.Tests(\.|$)') `
                "Archive contains a test artifact: $($entry.FullName)"
        }
    }
    finally {
        $archive.Dispose()
    }
}

Write-Output 'PASS portable and installed console, forwarding, exit-code, asynchronous process, version, and archive contract'
