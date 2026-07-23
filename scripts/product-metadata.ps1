Set-StrictMode -Version Latest

function ConvertTo-UsageIndicatorRepositoryUrl {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryUrl)

    $value = $RepositoryUrl.Trim()
    if ($value -cmatch '^git@github\.com:([^/]+)/(.+?)(?:\.git)?$') {
        return "https://github.com/$($Matches[1])/$($Matches[2])"
    }

    if ($value -cmatch '^ssh://git@github\.com/([^/]+)/(.+?)(?:\.git)?/?$') {
        return "https://github.com/$($Matches[1])/$($Matches[2])"
    }

    [Uri]$uri = $null
    if (
        [Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri) -and
        $uri.Scheme -ceq 'https' -and
        $uri.Host -ieq 'github.com' -and
        [string]::IsNullOrEmpty($uri.Query) -and
        [string]::IsNullOrEmpty($uri.Fragment)
    ) {
        $segments = @($uri.AbsolutePath.Trim('/') -split '/')
        if ($segments.Count -eq 2 -and $segments[0].Length -gt 0 -and $segments[1].Length -gt 0) {
            $repository = $segments[1] -replace '\.git$', ''
            return "https://github.com/$($segments[0])/$repository"
        }
    }

    throw "RepositoryUrl must identify one github.com owner/repository: $RepositoryUrl"
}

function Get-UsageIndicatorProductMetadata {
    [CmdletBinding()]
    param(
        [string]$RepositoryUrl,
        [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..')
    )

    $propsPath = Join-Path $RepositoryRoot 'Directory.Build.props'
    if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
        throw "Product metadata file does not exist: $propsPath"
    }

    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $versionNodes = @($props.Project.PropertyGroup.UsageIndicatorProductVersion)
    if ($versionNodes.Count -ne 1) {
        throw 'Directory.Build.props must contain exactly one UsageIndicatorProductVersion.'
    }

    $version = [string]$versionNodes[0]
    if ($version -cnotmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "UsageIndicatorProductVersion must be a stable semantic version: $version"
    }

    $resolvedRepositoryUrl = $RepositoryUrl
    if ([string]::IsNullOrWhiteSpace($resolvedRepositoryUrl)) {
        $remoteNames = @(& git -C $RepositoryRoot remote)
        if ($LASTEXITCODE -eq 0 -and $remoteNames -ccontains 'origin') {
            $remoteUrl = & git -C $RepositoryRoot remote get-url origin
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($remoteUrl)) {
                $resolvedRepositoryUrl = $remoteUrl.Trim()
            }
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($resolvedRepositoryUrl)) {
        $resolvedRepositoryUrl = ConvertTo-UsageIndicatorRepositoryUrl $resolvedRepositoryUrl
    }

    [pscustomobject]@{
        Version = $version
        FileVersion = "$version.0"
        Tag = "v$version"
        RepositoryUrl = $resolvedRepositoryUrl
        InstallerAssetName = "UsageIndicatorForCodex-Setup-v$version.exe"
        InstallerChecksumAssetName = "UsageIndicatorForCodex-Setup-v$version.exe.sha256"
        PortableAssetName = "usage-indicator-for-codex-v$version-win-x64.zip"
        PortableChecksumAssetName = "usage-indicator-for-codex-v$version-win-x64.zip.sha256"
    }
}
