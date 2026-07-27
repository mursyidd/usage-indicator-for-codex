using Microsoft.Win32;
using UsageIndicatorForCodex.Update;

namespace UsageIndicatorForCodex.UpdateHost;

internal static class UpdateHostLayout
{
    internal static void Validate(
        UpdateHostArguments arguments,
        string installerStateSubKey)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(installerStateSubKey);
        var expectedFiles = new[]
        {
            Path.Combine(arguments.InstallRoot, ProductConstants.LauncherRelativePath),
            Path.Combine(arguments.InstallRoot, ProductConstants.UpdateHostRelativePath),
            Path.Combine(arguments.InstallRoot, ProductConstants.GuiRelativePath)
        };
        foreach (var expectedFile in expectedFiles)
        {
            if (!File.Exists(expectedFile))
            {
                throw new InvalidOperationException(
                    $"The existing bootstrap-v1 installation is incomplete: {expectedFile}");
            }
        }

        using var stateKey = Registry.CurrentUser.OpenSubKey(installerStateSubKey);
        if (stateKey is null)
        {
            throw new InvalidOperationException(
                "The existing installation does not contain bootstrap-v1 installer state.");
        }

        var installedBootstrap = stateKey.GetValue(ProductConstants.BootstrapVersionValue);
        if (installedBootstrap is not int bootstrapVersion
            || bootstrapVersion != arguments.BootstrapVersion)
        {
            throw new InvalidOperationException(
                "The installed bootstrap protocol version is missing or unsupported.");
        }

        var installedPath = stateKey.GetValue(ProductConstants.InstallPathValue) as string;
        if (string.IsNullOrWhiteSpace(installedPath)
            || !string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(installedPath)),
                arguments.InstallRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The installed application path does not match installer-owned state.");
        }

        var installedVersion = stateKey.GetValue(ProductConstants.InstalledVersionValue) as string;
        InstalledVersionValidator.RequireExactVersion(
            installedVersion,
            UpdateHostProductInfo.Version,
            "existing installer state");
        InstalledVersionValidator.RequireExactVersion(
            InstalledVersionValidator.ReadProductVersion(
                Path.Combine(arguments.InstallRoot, ProductConstants.UpdateHostRelativePath)),
            UpdateHostProductInfo.Version,
            "existing installed update host");
    }
}
