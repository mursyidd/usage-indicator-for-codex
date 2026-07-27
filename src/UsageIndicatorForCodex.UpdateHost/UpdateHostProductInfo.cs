using System.Reflection;

namespace UsageIndicatorForCodex.UpdateHost;

internal sealed record UpdateHostIntegrationConfiguration(
    Uri ReleaseApiUri,
    string InstallerStateSubKey,
    string LocalStateRoot,
    string InstanceIdentity);

internal static class UpdateHostProductInfo
{
    internal static string Version { get; } = ResolveVersion();
    internal static string? RepositoryUrl { get; } = ResolveRepositoryUrl();
    internal static UpdateHostIntegrationConfiguration? IntegrationConfiguration { get; } =
        ResolveIntegrationConfiguration();

    private static string ResolveVersion()
    {
        var version = typeof(UpdateHostProductInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(version)
            ? throw new InvalidOperationException("Product version metadata is unavailable.")
            : version;
    }

    private static string? ResolveRepositoryUrl() =>
        ResolveMetadata("UsageIndicatorRepositoryUrl");

    private static UpdateHostIntegrationConfiguration? ResolveIntegrationConfiguration()
    {
        var releaseApiUrl = ResolveMetadata("UsageIndicatorIntegrationReleaseApiUrl");
        var installerStateSubKey = ResolveMetadata(
            "UsageIndicatorIntegrationInstallerStateSubKey");
        var localStateRoot = ResolveMetadata("UsageIndicatorIntegrationLocalStateRoot");
        var instanceIdentity = ResolveMetadata("UsageIndicatorIntegrationInstanceIdentity");
        if (releaseApiUrl is null
            && installerStateSubKey is null
            && localStateRoot is null
            && instanceIdentity is null)
        {
            return null;
        }

        if (!Uri.TryCreate(releaseApiUrl, UriKind.Absolute, out var releaseApiUri)
            || releaseApiUri.Scheme != Uri.UriSchemeHttp
            || !releaseApiUri.IsLoopback
            || string.IsNullOrWhiteSpace(installerStateSubKey)
            || !installerStateSubKey.StartsWith(
                @"Software\UsageIndicatorForCodex\IntegrationTests\",
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(localStateRoot)
            || !Path.IsPathFullyQualified(localStateRoot)
            || string.IsNullOrWhiteSpace(instanceIdentity)
            || !instanceIdentity.StartsWith(
                "integration-",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "UpdateHost integration metadata is incomplete or outside its isolated test boundary.");
        }

        return new UpdateHostIntegrationConfiguration(
            releaseApiUri,
            installerStateSubKey,
            Path.GetFullPath(localStateRoot),
            instanceIdentity);
    }

    private static string? ResolveMetadata(string key) =>
        typeof(UpdateHostProductInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute =>
                string.Equals(
                    attribute.Key,
                    key,
                    StringComparison.Ordinal))?
            .Value;
}
