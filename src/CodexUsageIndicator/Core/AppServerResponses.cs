using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexUsageIndicator.Core;

public static class AppServerResponses
{
    public static string CreateAccountFingerprint(JsonElement accountReadResult)
    {
        if (!accountReadResult.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object ||
            !account.TryGetProperty("type", out var type) || !string.Equals(type.GetString(), "chatgpt", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The active Codex account cannot be verified as a ChatGPT account.");
        }

        var email = account.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("The active Codex account has no verifiable account identity.");
        }

        var plan = account.TryGetProperty("planType", out var planElement) ? planElement.GetString() : null;
        var identity = $"chatgpt\n{email.Trim().ToLowerInvariant()}\n{plan ?? "unknown"}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
    }

    public static IReadOnlyList<RateLimitWindow> ExtractRateLimitWindows(JsonElement rateLimitReadResult)
    {
        var snapshots = new List<JsonElement>();
        if (rateLimitReadResult.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
        {
            snapshots.AddRange(buckets.EnumerateObject().Select(property => property.Value));
        }

        if (snapshots.Count == 0 && rateLimitReadResult.TryGetProperty("rateLimits", out var fallbackSnapshot) && fallbackSnapshot.ValueKind == JsonValueKind.Object)
        {
            snapshots.Add(fallbackSnapshot);
        }

        var windows = new List<RateLimitWindow>();
        foreach (var snapshot in snapshots)
        {
            AddWindow(snapshot, "primary", windows);
            AddWindow(snapshot, "secondary", windows);
        }

        if (windows.Count == 0)
        {
            throw new InvalidOperationException("The Codex app-server response contains no usable rate-limit windows.");
        }

        return windows;
    }

    private static void AddWindow(JsonElement snapshot, string propertyName, ICollection<RateLimitWindow> windows)
    {
        if (!snapshot.TryGetProperty(propertyName, out var window))
        {
            return;
        }

        if (window.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("usedPercent", out var usedPercent) || usedPercent.ValueKind != JsonValueKind.Number ||
            !window.TryGetProperty("resetsAt", out var resetsAt) || resetsAt.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException($"The Codex app-server {propertyName} rate-limit window is malformed.");
        }

        var percent = usedPercent.GetInt32();
        if (percent is < 0 or > 100)
        {
            throw new InvalidOperationException($"The Codex app-server {propertyName} rate-limit percentage is outside the supported range.");
        }

        windows.Add(new RateLimitWindow(percent, DateTimeOffset.FromUnixTimeSeconds(resetsAt.GetInt64())));
    }
}
