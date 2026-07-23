using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UsageIndicatorForCodex.Services;

internal sealed record ReleaseAsset(string Name, Uri DownloadUri);

internal sealed record StableRelease(
    Version Version,
    string Tag,
    IReadOnlyDictionary<string, ReleaseAsset> Assets);

internal sealed record UpdateCheckResult(
    Version CurrentVersion,
    StableRelease LatestRelease,
    bool IsAvailable)
{
    internal string Message => IsAvailable
        ? $"Update available: {LatestRelease.Version.ToString(3)} (current {CurrentVersion.ToString(3)})."
        : $"Up to date: {CurrentVersion.ToString(3)}.";
}

internal sealed class ReleaseUpdateService
{
    private const string InstallerPrefix = "UsageIndicatorForCodex-Setup-v";
    private readonly HttpClient _httpClient;
    private readonly Uri _latestReleaseUri;
    private readonly Version _currentVersion;

    internal ReleaseUpdateService(
        HttpClient httpClient,
        string repositoryUrl,
        string currentVersion)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _latestReleaseUri = CreateLatestReleaseApiUri(repositoryUrl);
        _currentVersion = ParseStableVersion(currentVersion, "current product version");
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"usage-indicator-for-codex/{_currentVersion.ToString(3)}");
        }
    }

    internal async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(_latestReleaseUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var release = ParseLatestStableRelease(json);
        return new UpdateCheckResult(
            _currentVersion,
            release,
            release.Version > _currentVersion);
    }

    internal async Task<string?> PrepareUpdateAsync(
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        var check = await CheckAsync(cancellationToken);
        if (!check.IsAvailable)
        {
            return null;
        }

        var version = check.LatestRelease.Version.ToString(3);
        var installerName = $"{InstallerPrefix}{version}.exe";
        var checksumName = $"{installerName}.sha256";
        var installerAsset = SelectExactAsset(check.LatestRelease, installerName);
        var checksumAsset = SelectExactAsset(check.LatestRelease, checksumName);

        var installerBytes = await _httpClient.GetByteArrayAsync(
            installerAsset.DownloadUri,
            cancellationToken);
        var checksumBytes = await _httpClient.GetByteArrayAsync(
            checksumAsset.DownloadUri,
            cancellationToken);
        var expectedHash = ParseChecksum(
            Encoding.UTF8.GetString(checksumBytes),
            installerName);
        if (!ChecksumMatches(installerBytes, expectedHash))
        {
            throw new InvalidDataException(
                $"SHA-256 verification failed for {installerName}.");
        }

        var destinationDirectory = Path.Combine(
            Path.GetFullPath(destinationRoot),
            $"v{version}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destinationDirectory);
        var installerPath = Path.Combine(destinationDirectory, installerName);
        try
        {
            await File.WriteAllBytesAsync(
                installerPath,
                installerBytes,
                cancellationToken);
            return installerPath;
        }
        catch
        {
            Directory.Delete(destinationDirectory, recursive: true);
            throw;
        }
    }

    internal static Uri CreateLatestReleaseApiUri(string repositoryUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var repositoryUri)
            || !string.Equals(repositoryUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.Equals(repositoryUri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(repositoryUri.Query)
            || !string.IsNullOrEmpty(repositoryUri.Fragment))
        {
            throw new InvalidOperationException(
                "The configured repository must be an HTTPS github.com owner/repository URL.");
        }

        var segments = repositoryUri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            throw new InvalidOperationException(
                "The configured repository must identify exactly one GitHub owner and repository.");
        }

        var repository = segments[1].EndsWith(".git", StringComparison.Ordinal)
            ? segments[1][..^4]
            : segments[1];
        if (string.IsNullOrWhiteSpace(segments[0]) || string.IsNullOrWhiteSpace(repository))
        {
            throw new InvalidOperationException("The configured GitHub repository is invalid.");
        }

        return new Uri(
            $"https://api.github.com/repos/{Uri.EscapeDataString(segments[0])}/{Uri.EscapeDataString(repository)}/releases/latest");
    }

    internal static StableRelease ParseLatestStableRelease(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean()
            || root.GetProperty("prerelease").GetBoolean())
        {
            throw new InvalidDataException("GitHub did not return a stable release.");
        }

        var tag = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidDataException("The release tag is missing.");
        if (!tag.StartsWith('v'))
        {
            throw new InvalidDataException($"Release tag is not versioned: {tag}");
        }

        var version = ParseStableVersion(tag[1..], "release tag");
        var assets = new Dictionary<string, ReleaseAsset>(StringComparer.Ordinal);
        foreach (var assetElement in root.GetProperty("assets").EnumerateArray())
        {
            var name = assetElement.GetProperty("name").GetString()
                ?? throw new InvalidDataException("A release asset name is missing.");
            var downloadUrl = assetElement.GetProperty("browser_download_url").GetString();
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri)
                || downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException($"Release asset URL is invalid: {name}");
            }

            if (!assets.TryAdd(name, new ReleaseAsset(name, downloadUri)))
            {
                throw new InvalidDataException($"Release contains a duplicate asset: {name}");
            }
        }

        return new StableRelease(version, tag, assets);
    }

    internal static ReleaseAsset SelectExactAsset(StableRelease release, string expectedName)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedName);
        return release.Assets.TryGetValue(expectedName, out var asset)
            ? asset
            : throw new InvalidDataException(
                $"Release {release.Tag} is missing exact asset {expectedName}.");
    }

    internal static byte[] ParseChecksum(string content, string expectedFileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFileName);
        var lines = content
            .TrimStart('\uFEFF')
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1)
        {
            throw new InvalidDataException("Checksum asset must contain exactly one record.");
        }

        var match = Regex.Match(
            lines[0],
            "^([0-9A-Fa-f]{64})[\\t ]+\\*?(.+)$",
            RegexOptions.CultureInvariant);
        if (!match.Success
            || !string.Equals(match.Groups[2].Value, expectedFileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Checksum record must name exact asset {expectedFileName}.");
        }

        return Convert.FromHexString(match.Groups[1].Value);
    }

    internal static bool ChecksumMatches(ReadOnlySpan<byte> content, ReadOnlySpan<byte> expectedHash)
    {
        Span<byte> actualHash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(content, actualHash);
        return expectedHash.Length == actualHash.Length
            && CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static Version ParseStableVersion(string value, string description)
    {
        if (!Regex.IsMatch(
                value,
                "^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)$",
                RegexOptions.CultureInvariant)
            || !System.Version.TryParse(value, out var version))
        {
            throw new InvalidDataException($"{description} is not a stable semantic version: {value}");
        }

        return version;
    }
}
