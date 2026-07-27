[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$UpdateHostPath,
    [Parameter(Mandatory)][string]$InstallerPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repositoryRoot 'scripts\product-metadata.ps1')
$metadata = Get-UsageIndicatorProductMetadata -RepositoryRoot $repositoryRoot

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [int]$TimeoutMilliseconds = 30000
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) {
        if ($argument.Contains('"') -or $argument.EndsWith('\', [StringComparison]::Ordinal)) {
            throw "The integration process helper received an unsupported test argument: $argument"
        }
    }
    $startInfo.Arguments = (
        $ArgumentList |
            ForEach-Object { '"' + $_ + '"' }
    ) -join ' '

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        Assert-True $process.Start() "Process could not be started: $FilePath"
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            $process.Kill($true)
            [void]$process.WaitForExit(2000)
            throw "Process timed out: $FilePath"
        }
        $tasks = [Threading.Tasks.Task[]]@($stdoutTask, $stderrTask)
        Assert-True ([Threading.Tasks.Task]::WaitAll($tasks, 2000)) "Output capture timed out: $FilePath"
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

$hostFile = Get-Item -LiteralPath (Resolve-Path -LiteralPath $UpdateHostPath).Path
$installer = Get-Item -LiteralPath (Resolve-Path -LiteralPath $InstallerPath).Path
Assert-True (
    $hostFile.Name -ceq 'UsageIndicatorForCodex.UpdateHost.exe'
) 'Published update host has the wrong filename.'
Assert-True (
    $hostFile.VersionInfo.ProductVersion.Trim() -ceq $metadata.Version
) 'Published update host has the wrong product version.'
Assert-True (
    $installer.Name -ceq $metadata.InstallerAssetName
) 'Compiled installer has the wrong filename.'

$hostDirectory = Split-Path -Parent $hostFile.FullName
$runtimeSidecars = @(
    Get-ChildItem -LiteralPath $hostDirectory -File |
        Where-Object {
            $_.Extension -ceq '.dll' -or
            $_.Name.EndsWith('.deps.json', [StringComparison]::Ordinal) -or
            $_.Name.EndsWith('.runtimeconfig.json', [StringComparison]::Ordinal)
        }
)
Assert-True ($runtimeSidecars.Count -eq 0) 'Published update host is not a standalone single file.'

$invalidInvocation = Invoke-CapturedProcess -FilePath $hostFile.FullName
Assert-True ($invalidInvocation.ExitCode -eq 2) 'Direct UpdateHost invocation did not fail with exit code 2.'
Assert-True (
    $invalidInvocation.Stderr.IndexOf(
        'Update host invocation failed.',
        [StringComparison]::Ordinal) -ge 0
) 'Direct UpdateHost invocation did not report its private contract.'

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "Usage Indicator UpdateHost Integration $([Guid]::NewGuid().ToString('N'))")
$incompleteRoot = Join-Path $fixtureRoot 'Incomplete Install'
$silentFirstRoot = Join-Path $fixtureRoot 'Silent First Install'
New-Item -ItemType Directory -Path $incompleteRoot, $silentFirstRoot -Force | Out-Null
try {
    $layoutFailure = Invoke-CapturedProcess `
        -FilePath $hostFile.FullName `
        -ArgumentList @(
            '--command', 'check-update',
            '--install-root', $incompleteRoot,
            '--bootstrap-version', '1')
    Assert-True ($layoutFailure.ExitCode -ne 0) 'Incomplete bootstrap layout was accepted.'
    Assert-True (
        $layoutFailure.Stderr.IndexOf(
            'installation is incomplete',
            [StringComparison]::Ordinal) -ge 0
    ) 'Incomplete bootstrap layout did not fail before network work.'

    $firstInstallLog = Join-Path $fixtureRoot 'silent-first-install.log'
    $silentFirst = Invoke-CapturedProcess `
        -FilePath $installer.FullName `
        -ArgumentList @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/SP-',
            '/NORESTART',
            "/DIR=$silentFirstRoot",
            "/LOG=$firstInstallLog")
    Assert-True ($silentFirst.ExitCode -ne 0) 'Silent first installation was accepted.'
    Assert-True (
        -not (Test-Path -LiteralPath (Join-Path $silentFirstRoot 'bin\usage-indicator.exe'))
    ) 'Rejected silent first installation wrote the stable launcher.'

    $cliUpdateLog = Join-Path $fixtureRoot 'invalid-cli-update.log'
    $invalidCliUpdate = Invoke-CapturedProcess `
        -FilePath $installer.FullName `
        -ArgumentList @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/SP-',
            '/NORESTART',
            '/CLIUPDATE',
            '/BOOTSTRAPVERSION=1',
            "/DIR=$incompleteRoot",
            "/LOG=$cliUpdateLog")
    Assert-True ($invalidCliUpdate.ExitCode -ne 0) 'CLI update accepted an incomplete installation.'
    Assert-True (
        -not (Test-Path -LiteralPath (Join-Path $incompleteRoot 'app\UsageIndicatorForCodex.Gui.exe'))
    ) 'Rejected CLI update replaced payload files.'

    $testResult = & dotnet run `
        --project (Join-Path $repositoryRoot 'tests\UsageIndicatorForCodex.Tests\UsageIndicatorForCodex.Tests.csproj') `
        --configuration Release `
        --no-build 2>&1
    Assert-True ($LASTEXITCODE -eq 0) "Managed update integration checks failed: $($testResult -join [Environment]::NewLine)"
    $testOutput = $testResult -join [Environment]::NewLine
    foreach ($requiredPass in @(
        'PASS downloads only checksum-verified installers',
        'PASS rejects unverified installers before process mutation',
        'PASS orchestrates a successful silent update through validation and restart',
        'PASS keeps a previously stopped indicator stopped after update',
        'PASS propagates validated installer restart requirements without restarting',
        'PASS fails closed for installer, validation, and restart failures',
        'PASS cleans every prepared update and restores only eligible failed updates',
        'PASS uses the exact private Inno Setup argument contract'
    )) {
        Assert-True (
            $testOutput.IndexOf($requiredPass, [StringComparison]::Ordinal) -ge 0
        ) "Managed integration output is missing: $requiredPass"
    }
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'PASS isolated UpdateHost, guarded installer, validation, restart, and 3010 integration contract'
