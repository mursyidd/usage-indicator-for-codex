using System.Globalization;

namespace CodexUsageIndicator.Core;

public enum IndicatorState
{
    Loading,
    Available,
    Unavailable
}

public enum IndicatorTone
{
    Neutral,
    Green,
    Amber,
    Red
}

public enum OverlayLayout
{
    Hidden,
    Compact,
    Narrow,
    Full
}

public sealed record RateLimitWindow(int UsedPercent, DateTimeOffset ResetsAt);

public sealed record UsageSnapshot(string AccountFingerprint, int RemainingPercent, DateTimeOffset ResetsAt);

public static class IndicatorPresentation
{
    private const double ReservedTitleBarWidth = 480;

    public static UsageSnapshot SelectMostRestrictive(string accountFingerprint, IEnumerable<RateLimitWindow> windows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountFingerprint);

        var candidates = windows.ToList();
        if (candidates.Any(window => window.UsedPercent is < 0 or > 100))
        {
            throw new InvalidOperationException("A rate-limit percentage is outside the supported range.");
        }

        var selected = candidates
            .Where(window => window.ResetsAt > DateTimeOffset.UtcNow)
            .OrderByDescending(window => window.UsedPercent)
            .ThenBy(window => window.ResetsAt)
            .FirstOrDefault();

        if (selected is null)
        {
            throw new InvalidOperationException("No active rate-limit windows were available.");
        }

        return new UsageSnapshot(accountFingerprint, 100 - selected.UsedPercent, selected.ResetsAt);
    }

    public static IndicatorTone GetTone(int remainingPercent) => remainingPercent switch
    {
        >= 50 => IndicatorTone.Green,
        >= 20 => IndicatorTone.Amber,
        _ => IndicatorTone.Red
    };

    public static string FormatUsageLabel(IndicatorState state, UsageSnapshot? snapshot, OverlayLayout layout)
    {
        if (state == IndicatorState.Available && snapshot is not null)
        {
            return layout == OverlayLayout.Compact
                ? $"Usage {snapshot.RemainingPercent}%"
                : $"Usage {snapshot.RemainingPercent}% left";
        }

        return state == IndicatorState.Unavailable ? "Usage unavailable" : "Usage —";
    }

    public static OverlayLayout SelectLayout(double codexWindowWidth)
    {
        var availableWidth = GetAvailableOverlayWidth(codexWindowWidth);
        return availableWidth switch
        {
            >= 430 => OverlayLayout.Full,
            >= 250 => OverlayLayout.Narrow,
            >= 90 => OverlayLayout.Compact,
            _ => OverlayLayout.Hidden
        };
    }

    public static double GetAvailableOverlayWidth(double codexWindowWidth) => Math.Max(0, codexWindowWidth - ReservedTitleBarWidth);

    public static string FormatResetTime(DateTimeOffset resetAt)
    {
        var malaysia = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
        var local = TimeZoneInfo.ConvertTime(resetAt, malaysia);
        return local.ToString("d MMMM h:mm tt", CultureInfo.GetCultureInfo("en-MY"))
            .Replace("AM", "am", StringComparison.Ordinal)
            .Replace("PM", "pm", StringComparison.Ordinal);
    }
}
