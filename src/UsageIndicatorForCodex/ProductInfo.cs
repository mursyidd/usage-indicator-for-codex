using System.Reflection;

namespace UsageIndicatorForCodex;

internal static class ProductInfo
{
    internal static string Version { get; } = ResolveVersion();
    internal static string? RepositoryUrl { get; } = ResolveRepositoryUrl();

    private static string ResolveVersion()
    {
        var version = typeof(ProductInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(version)
            ? throw new InvalidOperationException("Product version metadata is unavailable.")
            : version;
    }

    private static string? ResolveRepositoryUrl() =>
        typeof(ProductInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute =>
                string.Equals(
                    attribute.Key,
                    "UsageIndicatorRepositoryUrl",
                    StringComparison.Ordinal))?
            .Value;
}
