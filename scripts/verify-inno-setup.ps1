[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CompilerPath,
    [Parameter(Mandatory)][string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
if ($ExpectedVersion -cnotmatch '^([0-9]+)\.([0-9]+)\.([0-9]+)$') {
    throw 'ExpectedVersion must contain exactly three numeric components.'
}

$resolvedCompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
if (-not (Test-Path -LiteralPath $resolvedCompilerPath -PathType Leaf) -or
    (Split-Path -Leaf $resolvedCompilerPath) -cne 'ISCC.exe') {
    throw 'CompilerPath must identify ISCC.exe.'
}

$major = [int]$Matches[1]
$minor = [int]$Matches[2]
$revision = [int]$Matches[3]
$probeRoot = Join-Path $env:TEMP "UsageIndicatorForCodex-InnoProbe-$([Guid]::NewGuid().ToString('N'))"
$probePath = Join-Path $probeRoot 'version-probe.iss'
try {
    New-Item -ItemType Directory -Path $probeRoot | Out-Null
    @"
#if Ver != EncodeVer($major,$minor,$revision)
  #error Unexpected Inno Setup compiler version
#endif

[Setup]
AppName=Usage Indicator for Codex compiler probe
AppVersion=1.0.0
DefaultDirName={tmp}\UsageIndicatorForCodexCompilerProbe
PrivilegesRequired=lowest
Output=no
Uninstallable=no
"@ | Set-Content -LiteralPath $probePath -Encoding utf8

    $savedErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $compilerOutput = @(& $resolvedCompilerPath '/Q' $probePath 2>&1)
        $compilerExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }
    if ($compilerExitCode -ne 0) {
        $detail = ($compilerOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        throw "ISCC.exe is not version $ExpectedVersion.$([Environment]::NewLine)$detail"
    }
}
finally {
    if (Test-Path -LiteralPath $probeRoot) {
        Remove-Item -LiteralPath $probeRoot -Recurse -Force
    }
}

Get-Item -LiteralPath $resolvedCompilerPath
