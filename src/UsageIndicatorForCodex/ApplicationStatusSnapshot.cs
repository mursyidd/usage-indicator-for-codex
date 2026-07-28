using UsageIndicatorForCodex.Services;

namespace UsageIndicatorForCodex;

internal sealed record ApplicationStatusSnapshot(
    bool IsRunning,
    bool IsIndicatorEnabled,
    bool IsCreditExpiryEnabled,
    StartupTaskState StartupState)
{
    internal int ExitCode => 0;

    internal string Format() => string.Join(
        Environment.NewLine,
        $"running: {FormatBoolean(IsRunning)}",
        $"indicator-enabled: {FormatBoolean(IsIndicatorEnabled)}",
        $"credit-expiry: {(IsCreditExpiryEnabled ? "enabled" : "disabled")}",
        $"startup: {StartupState.ToString().ToLowerInvariant()}");

    private static string FormatBoolean(bool value) => value ? "true" : "false";
}
