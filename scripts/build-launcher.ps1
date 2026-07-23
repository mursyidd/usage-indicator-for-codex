[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath,

    [string]$IntermediateDirectory
)

$ErrorActionPreference = 'Stop'
$sourceDirectory = Join-Path $PSScriptRoot '..\src\UsageIndicatorForCodex.Launcher'
$sourceDirectory = (Resolve-Path -LiteralPath $sourceDirectory).Path

if ([string]::IsNullOrWhiteSpace($IntermediateDirectory)) {
    $IntermediateDirectory = Join-Path ([IO.Path]::GetTempPath()) "UsageIndicatorForCodex-Launcher-$([Guid]::NewGuid().ToString('N'))"
}

$createdIntermediateDirectory = -not (Test-Path -LiteralPath $IntermediateDirectory)
New-Item -ItemType Directory -Path $IntermediateDirectory -Force | Out-Null

function Find-MsvcToolDirectory {
    $candidateRoots = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:USAGE_INDICATOR_MSVC_BIN)) {
        if (
            (Test-Path -LiteralPath (Join-Path $env:USAGE_INDICATOR_MSVC_BIN 'cl.exe') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $env:USAGE_INDICATOR_MSVC_BIN 'link.exe') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $env:USAGE_INDICATOR_MSVC_BIN 'lib.exe') -PathType Leaf)
        ) {
            return $env:USAGE_INDICATOR_MSVC_BIN
        }

        throw 'USAGE_INDICATOR_MSVC_BIN does not contain cl.exe, link.exe, and lib.exe.'
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $installations = @(& $vswhere -products * -property installationPath) |
            Sort-Object -Descending
        foreach ($installation in $installations) {
            if (-not [string]::IsNullOrWhiteSpace($installation)) {
                $toolRoot = Join-Path $installation 'VC\Tools\MSVC'
                if (Test-Path -LiteralPath $toolRoot -PathType Container) {
                    Get-ChildItem -LiteralPath $toolRoot -Directory |
                        Sort-Object Name -Descending |
                        ForEach-Object {
                            $candidateRoots.Add((Join-Path $_.FullName 'bin\Hostx64\x64'))
                        }
                }
            }
        }
    }

    foreach ($programFilesRoot in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ([string]::IsNullOrWhiteSpace($programFilesRoot)) {
            continue
        }

        $visualStudioRoot = Join-Path $programFilesRoot 'Microsoft Visual Studio'
        if (-not (Test-Path -LiteralPath $visualStudioRoot -PathType Container)) {
            continue
        }

        Get-ChildItem -LiteralPath $visualStudioRoot -Directory |
            Sort-Object FullName -Descending |
            ForEach-Object {
                Get-ChildItem -LiteralPath $_.FullName -Directory -ErrorAction SilentlyContinue |
                    Sort-Object FullName -Descending
            } |
            ForEach-Object {
                $toolRoot = Join-Path $_.FullName 'VC\Tools\MSVC'
                if (Test-Path -LiteralPath $toolRoot -PathType Container) {
                    Get-ChildItem -LiteralPath $toolRoot -Directory |
                        Sort-Object Name -Descending |
                        ForEach-Object {
                            $candidateRoots.Add((Join-Path $_.FullName 'bin\Hostx64\x64'))
                        }
                }
            }
    }

    foreach ($candidate in $candidateRoots | Sort-Object -Unique -Descending) {
        if (
            (Test-Path -LiteralPath (Join-Path $candidate 'cl.exe') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'link.exe') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'lib.exe') -PathType Leaf)
        ) {
            return $candidate
        }
    }

    throw 'An x64 MSVC compiler, linker, and library manager could not be found.'
}

function Invoke-NativeBuildTool {
    param(
        [Parameter(Mandatory)][string]$Tool,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    & $Tool @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path -Leaf $Tool) failed with exit code $LASTEXITCODE."
    }
}

try {
    $toolDirectory = Find-MsvcToolDirectory
    $compiler = Join-Path $toolDirectory 'cl.exe'
    $linker = Join-Path $toolDirectory 'link.exe'
    $libraryManager = Join-Path $toolDirectory 'lib.exe'
    $objectPath = Join-Path $IntermediateDirectory 'launcher.obj'
    $kernelLibrary = Join-Path $IntermediateDirectory 'kernel32.lib'
    $shellLibrary = Join-Path $IntermediateDirectory 'shell32.lib'
    $userLibrary = Join-Path $IntermediateDirectory 'user32.lib'
    $resolvedOutputParent = Split-Path -Parent $OutputPath
    if ([string]::IsNullOrWhiteSpace($resolvedOutputParent)) {
        $resolvedOutputParent = (Get-Location).Path
    }

    New-Item -ItemType Directory -Path $resolvedOutputParent -Force | Out-Null
    $resolvedOutputParent = (Resolve-Path -LiteralPath $resolvedOutputParent).Path
    $resolvedOutput = Join-Path $resolvedOutputParent (Split-Path -Leaf $OutputPath)

    Invoke-NativeBuildTool $libraryManager @(
        '/nologo',
        '/machine:x64',
        "/def:$(Join-Path $sourceDirectory 'kernel32.def')",
        "/out:$kernelLibrary"
    )
    Invoke-NativeBuildTool $libraryManager @(
        '/nologo',
        '/machine:x64',
        "/def:$(Join-Path $sourceDirectory 'shell32.def')",
        "/out:$shellLibrary"
    )
    Invoke-NativeBuildTool $libraryManager @(
        '/nologo',
        '/machine:x64',
        "/def:$(Join-Path $sourceDirectory 'user32.def')",
        "/out:$userLibrary"
    )
    Invoke-NativeBuildTool $compiler @(
        '/nologo',
        '/TC',
        '/c',
        '/GS-',
        '/Zl',
        '/W4',
        '/WX',
        '/O1',
        (Join-Path $sourceDirectory 'launcher.c'),
        "/Fo$objectPath"
    )
    Invoke-NativeBuildTool $linker @(
        '/nologo',
        '/machine:x64',
        '/subsystem:console',
        '/entry:LauncherEntry',
        '/nodefaultlib',
        '/incremental:no',
        '/opt:ref',
        '/opt:icf',
        '/dynamicbase',
        '/nxcompat',
        "/out:$resolvedOutput",
        $objectPath,
        $kernelLibrary,
        $shellLibrary,
        $userLibrary
    )

    Get-Item -LiteralPath $resolvedOutput
}
finally {
    if ($createdIntermediateDirectory -and (Test-Path -LiteralPath $IntermediateDirectory)) {
        Remove-Item -LiteralPath $IntermediateDirectory -Recurse -Force
    }
}
