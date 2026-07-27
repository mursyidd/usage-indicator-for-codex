[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$UpdateHostPath,
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$InstalledLauncher,
    [Parameter(Mandatory)][string]$RepositoryUrl,
    [Parameter(Mandatory)][string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repositoryRoot 'scripts\product-metadata.ps1')
$metadata = Get-UsageIndicatorProductMetadata `
    -RepositoryRoot $repositoryRoot `
    -RepositoryUrl $RepositoryUrl

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -cne $Actual) {
        throw "$Message Expected: $Expected Actual: $Actual"
    }
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [hashtable]$Environment = @{},
        [int]$TimeoutMilliseconds = 120000
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($entry in $Environment.GetEnumerator()) {
        $startInfo.EnvironmentVariables[$entry.Key] = [string]$entry.Value
    }
    foreach ($argument in $ArgumentList) {
        if ($argument.Contains('"') -or $argument.EndsWith('\', [StringComparison]::Ordinal)) {
            throw "The acceptance process helper received an unsupported test argument: $argument"
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
            try {
                $process.Kill($true)
            }
            catch [Management.Automation.MethodException] {
                $process.Kill()
            }
            [void]$process.WaitForExit(2000)
            throw "Process timed out: $FilePath"
        }
        $tasks = [Threading.Tasks.Task[]]@($stdoutTask, $stderrTask)
        Assert-True ([Threading.Tasks.Task]::WaitAll($tasks, 5000)) "Output capture timed out: $FilePath"
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

function Convert-RegistryValue {
    param($Value)
    if ($null -eq $Value) {
        return '<null>'
    }
    if ($Value -is [byte[]]) {
        return [Convert]::ToBase64String($Value)
    }
    if ($Value -is [string[]]) {
        return $Value -join [char]0x1f
    }
    return [string]$Value
}

function Get-RegistryKeySnapshot {
    param([Parameter(Mandatory)][string]$SubKey)
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($SubKey)
    if ($null -eq $key) {
        return '<missing>'
    }
    try {
        $records = foreach ($name in @($key.GetValueNames() | Sort-Object)) {
            $kind = $key.GetValueKind($name)
            $value = $key.GetValue(
                $name,
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            "$name|$kind|$(Convert-RegistryValue $value)"
        }
        return $records -join "`n"
    }
    finally {
        $key.Dispose()
    }
}

function Get-RegistryValueSnapshot {
    param(
        [Parameter(Mandatory)][string]$SubKey,
        [Parameter(Mandatory)][string]$Name
    )
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($SubKey)
    if ($null -eq $key) {
        return '<missing-key>'
    }
    try {
        if ($Name -cnotin $key.GetValueNames()) {
            return '<missing-value>'
        }
        $kind = $key.GetValueKind($Name)
        $value = $key.GetValue(
            $Name,
            $null,
            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        return "$kind|$(Convert-RegistryValue $value)"
    }
    finally {
        $key.Dispose()
    }
}

function Get-FileTreeSnapshot {
    param([Parameter(Mandatory)][string]$Root)
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return '<missing>'
    }
    $rootPrefixLength = $Root.TrimEnd('\').Length + 1
    return @(
        Get-ChildItem -LiteralPath $Root -Recurse -File |
            ForEach-Object {
                $relativePath = $_.FullName.Substring($rootPrefixLength)
                "$relativePath|$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
            } |
            Sort-Object
    ) -join "`n"
}

function Get-ProductPayloadSnapshot {
    param([string]$InstallRoot)
    if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
        return '<no-recorded-install>'
    }
    $records = foreach ($relativePath in @(
        'bin\usage-indicator.exe',
        'updater\UsageIndicatorForCodex.UpdateHost.exe',
        'app\UsageIndicatorForCodex.Gui.exe'
    )) {
        $path = Join-Path $InstallRoot $relativePath
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            "$relativePath|$((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)"
        } else {
            "$relativePath|<missing>"
        }
    }
    return $records -join "`n"
}

function Get-StartupSnapshot {
    $records = foreach ($taskName in @('UsageIndicatorForCodex', 'CodexUsageIndicator')) {
        $result = Invoke-CapturedProcess `
            -FilePath (Join-Path $env:SystemRoot 'System32\schtasks.exe') `
            -ArgumentList @('/Query', '/TN', $taskName, '/XML')
        "$taskName|$($result.ExitCode)|$($result.Stdout)|$($result.Stderr)"
    }
    return $records -join "`n"
}

function Wait-ForServerPort {
    param(
        [Parameter(Mandatory)][string]$ReadyPath,
        [Parameter(Mandatory)][Management.Automation.Job]$Job
    )
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $ReadyPath -PathType Leaf) {
            return [int]([IO.File]::ReadAllText($ReadyPath))
        }
        if ($Job.State -in @('Failed', 'Completed', 'Stopped')) {
            $jobOutput = Receive-Job -Job $Job -Keep 2>&1
            throw "Loopback release server stopped before readiness: $($jobOutput -join [Environment]::NewLine)"
        }
        Start-Sleep -Milliseconds 100
    }
    throw 'Loopback release server did not become ready.'
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "Usage Indicator Silent Upgrade $([Guid]::NewGuid().ToString('N'))")
$installRoot = Join-Path $fixtureRoot 'Temporary Install'
$cacheLocalAppData = Join-Path $fixtureRoot 'Isolated LocalAppData'
$hostStateRoot = Join-Path $fixtureRoot 'UpdateHost State'
$targetInstallerRoot = Join-Path $fixtureRoot 'Target Installer'
$oldHostRoot = Join-Path $fixtureRoot 'Old UpdateHost'
$readyPath = Join-Path $fixtureRoot 'release-server.port'
$requestLogPath = Join-Path $fixtureRoot 'release-server.requests'
$testId = [Guid]::NewGuid().ToString('N')
$installerStateSubKey = "Software\UsageIndicatorForCodex\IntegrationTests\$testId"
$installerStateRegistryPath = "HKCU:\$installerStateSubKey"
$testAppGuid = [Guid]::NewGuid().ToString().ToUpperInvariant()
$testAppId = "{{$testAppGuid}"
$testUninstallSubKey =
    "Software\Microsoft\Windows\CurrentVersion\Uninstall\{$testAppGuid}_is1"
$testUninstallRegistryPath = "HKCU:\$testUninstallSubKey"
$oldVersion = '0.1.999'
$serverJob = $null

New-Item -ItemType Directory -Path $fixtureRoot, $cacheLocalAppData -Force | Out-Null
try {
    Write-Verbose 'Building isolated target installer.'
    & (Join-Path $repositoryRoot 'scripts\build-installer.ps1') `
        -PublishDirectory $PublishDirectory `
        -InstalledLauncher $InstalledLauncher `
        -UpdateHostPath $UpdateHostPath `
        -OutputDirectory $targetInstallerRoot `
        -RepositoryUrl $metadata.RepositoryUrl `
        -IsccPath $IsccPath `
        -IntegrationTestInstallerStateSubKey $installerStateSubKey `
        -IntegrationTestAppId $testAppId | Out-Host
    $targetInstallerPath = Join-Path $targetInstallerRoot $metadata.InstallerAssetName
    Assert-True (
        (Test-Path -LiteralPath $targetInstallerPath -PathType Leaf)
    ) 'The isolated target installer was not built.'

    $installerHash = (
        Get-FileHash -LiteralPath $targetInstallerPath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    $serverJob = Start-Job -ScriptBlock {
        param(
            [string]$ReadyPath,
            [string]$RequestLogPath,
            [string]$InstallerPath,
            [string]$InstallerName,
            [string]$InstallerHash,
            [string]$TargetVersion
        )

        $listener = [Net.Sockets.TcpListener]::new(
            [Net.IPAddress]::Loopback,
            0)
        $listener.Start()
        try {
            $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
            [IO.File]::WriteAllText($ReadyPath, [string]$port)
            while ($true) {
                $client = $listener.AcceptTcpClient()
                try {
                    $stream = $client.GetStream()
                    $reader = [IO.StreamReader]::new(
                        $stream,
                        [Text.Encoding]::ASCII,
                        $false,
                        1024,
                        $true)
                    $requestLine = $reader.ReadLine()
                    if ([string]::IsNullOrWhiteSpace($requestLine)) {
                        continue
                    }
                    while (-not [string]::IsNullOrEmpty($reader.ReadLine())) {
                    }
                    $requestTarget = $requestLine.Split(' ')[1]
                    $requestPath = ([Uri]"http://127.0.0.1$requestTarget").AbsolutePath
                    [IO.File]::AppendAllText(
                        $RequestLogPath,
                        "$requestPath$([Environment]::NewLine)")

                    $status = '200 OK'
                    $contentType = 'application/octet-stream'
                    if ($requestPath -ceq '/releases/latest') {
                        $contentType = 'application/json'
                        $assetBase = "http://127.0.0.1:$port/assets"
                        $json = [ordered]@{
                            tag_name = "v$TargetVersion"
                            draft = $false
                            prerelease = $false
                            assets = @(
                                [ordered]@{
                                    name = $InstallerName
                                    browser_download_url = "$assetBase/$InstallerName"
                                },
                                [ordered]@{
                                    name = "$InstallerName.sha256"
                                    browser_download_url = "$assetBase/$InstallerName.sha256"
                                }
                            )
                        } | ConvertTo-Json -Depth 5 -Compress
                        $body = [Text.Encoding]::UTF8.GetBytes($json)
                    } elseif ($requestPath -ceq "/assets/$InstallerName") {
                        $body = [IO.File]::ReadAllBytes($InstallerPath)
                    } elseif ($requestPath -ceq "/assets/$InstallerName.sha256") {
                        $body = [Text.Encoding]::UTF8.GetBytes(
                            "$InstallerHash  $InstallerName`n")
                    } else {
                        $status = '404 Not Found'
                        $body = [Text.Encoding]::UTF8.GetBytes('not found')
                    }

                    $headers = [Text.Encoding]::ASCII.GetBytes(
                        "HTTP/1.1 $status`r`n" +
                        "Content-Type: $contentType`r`n" +
                        "Content-Length: $($body.Length)`r`n" +
                        "Connection: close`r`n`r`n")
                    $stream.Write($headers, 0, $headers.Length)
                    $stream.Write($body, 0, $body.Length)
                    $stream.Flush()
                }
                finally {
                    $client.Dispose()
                }
            }
        }
        finally {
            $listener.Stop()
        }
    } -ArgumentList @(
        $readyPath,
        $requestLogPath,
        $targetInstallerPath,
        $metadata.InstallerAssetName,
        $installerHash,
        $metadata.Version)
    $serverPort = Wait-ForServerPort -ReadyPath $readyPath -Job $serverJob

    Write-Verbose 'Building older integration UpdateHost.'
    $oldHostProject = Join-Path $repositoryRoot (
        'src\UsageIndicatorForCodex.UpdateHost\UsageIndicatorForCodex.UpdateHost.csproj')
    $oldHostArguments = @(
        'publish',
        $oldHostProject,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $oldHostRoot,
        '-p:PublishSingleFile=true',
        '-p:PublishTrimmed=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:UsageIndicatorProductVersion=$oldVersion",
        "-p:IntegrationTestReleaseApiUrl=http://127.0.0.1:$serverPort/releases/latest",
        "-p:IntegrationTestInstallerStateSubKey=$installerStateSubKey",
        "-p:IntegrationTestLocalStateRoot=$hostStateRoot",
        "-p:IntegrationTestInstanceIdentity=integration-$testId"
    )
    & dotnet @oldHostArguments
    Assert-Equal 0 $LASTEXITCODE 'The older isolated UpdateHost build failed.'
    $oldHostPath = Join-Path $oldHostRoot 'UsageIndicatorForCodex.UpdateHost.exe'
    Assert-Equal $oldVersion (
        (Get-Item -LiteralPath $oldHostPath).VersionInfo.ProductVersion.Trim()
    ) 'The older isolated UpdateHost has the wrong version.'

    Write-Verbose 'Building integration launcher and seeding temporary installation.'
    $launcherPath = Join-Path $installRoot 'bin\usage-indicator.exe'
    & (Join-Path $repositoryRoot 'scripts\build-launcher.ps1') `
        -OutputPath $launcherPath `
        -ProductVersion $metadata.Version `
        -IntegrationTestBuild | Out-Null
    $installedHostPath = Join-Path $installRoot (
        'updater\UsageIndicatorForCodex.UpdateHost.exe')
    $installedGuiPath = Join-Path $installRoot 'app\UsageIndicatorForCodex.Gui.exe'
    New-Item -ItemType Directory -Path (
        Split-Path -Parent $installedHostPath
    ), (
        Split-Path -Parent $installedGuiPath
    ) -Force | Out-Null
    Copy-Item -LiteralPath $oldHostPath -Destination $installedHostPath
    Copy-Item -LiteralPath $oldHostPath -Destination $installedGuiPath

    New-Item -Path $installerStateRegistryPath -Force | Out-Null
    New-ItemProperty `
        -Path $installerStateRegistryPath `
        -Name 'BootstrapVersion' `
        -PropertyType DWord `
        -Value 1 `
        -Force | Out-Null
    New-ItemProperty `
        -Path $installerStateRegistryPath `
        -Name 'InstallPath' `
        -PropertyType String `
        -Value ([IO.Path]::GetFullPath($installRoot).TrimEnd('\')) `
        -Force | Out-Null
    New-ItemProperty `
        -Path $installerStateRegistryPath `
        -Name 'InstalledVersion' `
        -PropertyType String `
        -Value $oldVersion `
        -Force | Out-Null
    New-ItemProperty `
        -Path $installerStateRegistryPath `
        -Name 'PathEntryOwned' `
        -PropertyType DWord `
        -Value 1 `
        -Force | Out-Null

    $canonicalStateSubKey = 'Software\UsageIndicatorForCodex\Installer'
    $canonicalUninstallSubKey =
        'Software\Microsoft\Windows\CurrentVersion\Uninstall\{3C77270D-28B4-45B7-BE77-B051195C969D}_is1'
    $canonicalStateBefore = Get-RegistryKeySnapshot $canonicalStateSubKey
    $canonicalUninstallBefore = Get-RegistryKeySnapshot $canonicalUninstallSubKey
    $pathBefore = Get-RegistryValueSnapshot 'Environment' 'Path'
    $startupBefore = Get-StartupSnapshot
    $realInstallPath = $null
    $canonicalStateKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        $canonicalStateSubKey)
    if ($null -ne $canonicalStateKey) {
        try {
            $realInstallPath = $canonicalStateKey.GetValue('InstallPath') -as [string]
        }
        finally {
            $canonicalStateKey.Dispose()
        }
    }
    $realPayloadBefore = Get-ProductPayloadSnapshot $realInstallPath

    $rejectionTreeBefore = Get-FileTreeSnapshot $installRoot
    $rejectionStateBefore = Get-RegistryKeySnapshot $installerStateSubKey
    Write-Verbose 'Running real non-silent CLI update rejection.'
    $nonSilentResult = Invoke-CapturedProcess `
        -FilePath $targetInstallerPath `
        -ArgumentList @(
            '/CLIUPDATE',
            '/BOOTSTRAPVERSION=1',
            '/SUPPRESSMSGBOXES',
            '/SP-',
            '/NORESTART',
            "/LOG=$(Join-Path $fixtureRoot 'non-silent-cli-update.log')") `
        -TimeoutMilliseconds 30000
    Assert-True (
        $nonSilentResult.ExitCode -ne 0
    ) 'A real non-silent /CLIUPDATE invocation was accepted.'
    Assert-Equal $rejectionTreeBefore (
        Get-FileTreeSnapshot $installRoot
    ) 'Rejected non-silent /CLIUPDATE modified the temporary installation.'
    Assert-Equal $rejectionStateBefore (
        Get-RegistryKeySnapshot $installerStateSubKey
    ) 'Rejected non-silent /CLIUPDATE modified installer state.'

    $launcherHashBefore = (
        Get-FileHash -LiteralPath $launcherPath -Algorithm SHA256
    ).Hash
    $oldHostHash = (
        Get-FileHash -LiteralPath $installedHostPath -Algorithm SHA256
    ).Hash
    $oldGuiHash = (
        Get-FileHash -LiteralPath $installedGuiPath -Algorithm SHA256
    ).Hash
    Write-Verbose 'Running launcher to cached UpdateHost to real Inno installer chain.'
    $upgradeResult = Invoke-CapturedProcess `
        -FilePath $launcherPath `
        -ArgumentList @('update') `
        -Environment @{
            USAGE_INDICATOR_E2E_LOCAL_APP_DATA = $cacheLocalAppData
        } `
        -TimeoutMilliseconds 300000
    Assert-Equal 0 $upgradeResult.ExitCode (
        "The launcher did not propagate successful UpdateHost exit code. stderr: " +
        $upgradeResult.Stderr)
    Assert-True (
        [string]::IsNullOrWhiteSpace($upgradeResult.Stderr)
    ) "The successful update wrote stderr: $($upgradeResult.Stderr)"
    foreach ($expectedOutput in @(
        'Checking for updates...',
        "Update available: $($metadata.Version) (current $oldVersion).",
        'Downloading installer...',
        'Verifying SHA-256...',
        "Installing $($metadata.Version)...",
        'Validating installed version...',
        "Updated successfully: $oldVersion -> $($metadata.Version)."
    )) {
        Assert-True (
            $upgradeResult.Stdout.IndexOf($expectedOutput, [StringComparison]::Ordinal) -ge 0
        ) "Original shell output is missing: $expectedOutput"
    }

    Assert-Equal $launcherHashBefore (
        (Get-FileHash -LiteralPath $launcherPath -Algorithm SHA256).Hash
    ) 'The silent CLI update replaced the stable launcher.'
    Assert-True (
        $oldHostHash -cne (
            Get-FileHash -LiteralPath $installedHostPath -Algorithm SHA256
        ).Hash
    ) 'The silent CLI update did not replace UpdateHost.'
    Assert-True (
        $oldGuiHash -cne (
            Get-FileHash -LiteralPath $installedGuiPath -Algorithm SHA256
        ).Hash
    ) 'The silent CLI update did not replace the GUI.'
    Assert-Equal $metadata.Version (
        (Get-Item -LiteralPath $installedHostPath).VersionInfo.ProductVersion.Trim()
    ) 'Installed UpdateHost version validation did not reach the target.'
    Assert-Equal $metadata.Version (
        (Get-Item -LiteralPath $installedGuiPath).VersionInfo.ProductVersion.Trim()
    ) 'Installed GUI version validation did not reach the target.'

    $cachedHostDirectory = Join-Path $cacheLocalAppData (
        "UsageIndicatorForCodex\update-host\v$oldVersion")
    $cachedHosts = @(
        Get-ChildItem `
            -LiteralPath $cachedHostDirectory `
            -Filter 'UsageIndicatorForCodex.UpdateHost.*.exe' `
            -File
    )
    Assert-Equal 1 $cachedHosts.Count 'The launcher did not execute one isolated cached UpdateHost.'
    Assert-Equal (
        (Get-FileHash -LiteralPath $oldHostPath -Algorithm SHA256).Hash
    ) (
        (Get-FileHash -LiteralPath $cachedHosts[0].FullName -Algorithm SHA256).Hash
    ) 'The launcher cache did not contain the older installed UpdateHost.'

    Assert-Equal 1 (
        Get-ItemPropertyValue `
            -Path $installerStateRegistryPath `
            -Name 'BootstrapVersion'
    ) 'The bootstrap protocol state changed.'
    Assert-Equal ([IO.Path]::GetFullPath($installRoot).TrimEnd('\')) (
        Get-ItemPropertyValue `
            -Path $installerStateRegistryPath `
            -Name 'InstallPath'
    ) 'The installer-owned install path changed.'
    Assert-Equal $metadata.Version (
        Get-ItemPropertyValue `
            -Path $installerStateRegistryPath `
            -Name 'InstalledVersion'
    ) 'The installer did not record the target version.'
    Assert-Equal 1 (
        Get-ItemPropertyValue `
            -Path $installerStateRegistryPath `
            -Name 'PathEntryOwned'
    ) 'The CLI update changed PATH ownership state.'
    Assert-Equal $pathBefore (
        Get-RegistryValueSnapshot 'Environment' 'Path'
    ) 'The CLI update changed the current-user PATH.'
    Assert-Equal $startupBefore (
        Get-StartupSnapshot
    ) 'The CLI update changed startup-task state.'
    Assert-Equal $canonicalStateBefore (
        Get-RegistryKeySnapshot $canonicalStateSubKey
    ) 'The isolated update modified canonical installer state.'
    Assert-Equal $canonicalUninstallBefore (
        Get-RegistryKeySnapshot $canonicalUninstallSubKey
    ) 'The isolated update modified the canonical uninstall registration.'
    Assert-Equal $realPayloadBefore (
        Get-ProductPayloadSnapshot $realInstallPath
    ) 'The isolated update modified the recorded real product installation.'
    Assert-True (
        Test-Path -LiteralPath $testUninstallRegistryPath
    ) 'The isolated real installer did not create its unique uninstall registration.'

    $workingDirectories = @()
    $workingRoot = Join-Path $hostStateRoot 'updates'
    if (Test-Path -LiteralPath $workingRoot -PathType Container) {
        $workingDirectories = @(Get-ChildItem -LiteralPath $workingRoot -Directory)
    }
    Assert-Equal 0 $workingDirectories.Count (
        'The successful update retained its downloaded installer working directory.')

    $requests = @(
        Get-Content -LiteralPath $requestLogPath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    Assert-Equal 3 $requests.Count 'The real update did not make the exact release/checksum requests.'
    Assert-True (
        $requests -ccontains '/releases/latest'
    ) 'The real update did not request release metadata.'
    Assert-True (
        $requests -ccontains "/assets/$($metadata.InstallerAssetName)"
    ) 'The real update did not request the exact installer asset.'
    Assert-True (
        $requests -ccontains "/assets/$($metadata.InstallerChecksumAssetName)"
    ) 'The real update did not request the exact checksum asset.'

    $uninstallerPath = Join-Path $installRoot 'unins000.exe'
    Assert-True (
        Test-Path -LiteralPath $uninstallerPath -PathType Leaf
    ) 'The real installer did not create an uninstaller.'
    Move-Item `
        -LiteralPath $launcherPath `
        -Destination (Join-Path $fixtureRoot 'preserved-launcher.exe')
    Write-Verbose 'Running isolated uninstall state cleanup.'
    $uninstallResult = Invoke-CapturedProcess `
        -FilePath $uninstallerPath `
        -ArgumentList @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART')
    Assert-Equal 0 $uninstallResult.ExitCode (
        "The isolated uninstall failed: $($uninstallResult.Stderr)")
    Assert-True (
        -not (Test-Path -LiteralPath $installerStateRegistryPath)
    ) 'Uninstall retained installer-owned state.'
    Assert-True (
        -not (Test-Path -LiteralPath $testUninstallRegistryPath)
    ) 'Uninstall retained the isolated uninstall registration.'
    Assert-Equal $pathBefore (
        Get-RegistryValueSnapshot 'Environment' 'Path'
    ) 'Uninstall changed PATH while removing isolated installer state.'
    Assert-Equal $startupBefore (
        Get-StartupSnapshot
    ) 'Uninstall changed startup state in the isolated acceptance test.'
    Assert-Equal $canonicalStateBefore (
        Get-RegistryKeySnapshot $canonicalStateSubKey
    ) 'Uninstall modified canonical installer state.'
    Assert-Equal $realPayloadBefore (
        Get-ProductPayloadSnapshot $realInstallPath
    ) 'Uninstall modified the recorded real product installation.'
}
finally {
    if ($null -ne $serverJob) {
        Stop-Job -Job $serverJob -ErrorAction SilentlyContinue
        Remove-Job -Job $serverJob -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $installerStateRegistryPath -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $testUninstallRegistryPath -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'PASS real launcher, cached UpdateHost, Inno installer, validation, rejection, preservation, cleanup, and uninstall chain'
