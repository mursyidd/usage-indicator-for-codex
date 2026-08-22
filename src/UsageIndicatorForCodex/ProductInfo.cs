using System.Reflection;

namespace UsageIndicatorForCodex;

internal static class ProductInfo
{
    internal static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var version = typeof(ProductInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(version)
            ? throw new InvalidOperationException("Product version metadata is unavailable.")
            : version;
    }
}
