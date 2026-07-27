using System.Diagnostics;
using Microsoft.Win32;

namespace UsageIndicatorForCodex.Update;

internal interface IInstalledVersionValidator
{
    void Validate(string installRoot, Version targetVersion);
}

internal sealed class InstalledVersionValidator : IInstalledVersionValidator
{
    private readonly string _installerStateSubKey;

    internal InstalledVersionValidator()
        : this(ProductConstants.InstallerStateSubKey)
    {
    }

    internal InstalledVersionValidator(string installerStateSubKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerStateSubKey);
        _installerStateSubKey = installerStateSubKey;
    }

    public void Validate(string installRoot, Version targetVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(targetVersion);
        var expected = targetVersion.ToString(3);

        using var stateKey = Registry.CurrentUser.OpenSubKey(_installerStateSubKey);
        var registryVersion = stateKey?.GetValue(ProductConstants.InstalledVersionValue) as string;
        RequireExactVersion(registryVersion, expected, "installer state");

        RequireExactVersion(
            ReadProductVersion(Path.Combine(installRoot, ProductConstants.UpdateHostRelativePath)),
            expected,
            "installed update host");
        RequireExactVersion(
            ReadProductVersion(Path.Combine(installRoot, ProductConstants.GuiRelativePath)),
            expected,
            "installed application payload");
    }

    internal static string ReadProductVersion(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "An installed version source is missing.",
                executablePath);
        }

        var version = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion?.Trim();
        return string.IsNullOrWhiteSpace(version)
            ? throw new InvalidDataException(
                $"Installed product version metadata is unavailable: {executablePath}")
            : version;
    }

    internal static void RequireExactVersion(
        string? actual,
        string expected,
        string description)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The {description} version is {actual ?? "missing"}, not target {expected}.");
        }
    }
}
