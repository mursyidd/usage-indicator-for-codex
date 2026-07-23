using System.Text.Json;
using System.IO;

namespace CodexUsageIndicator.Services;

public sealed record UserSettings(bool Enabled, double HorizontalOffset, double VerticalOffset)
{
    public static UserSettings Default { get; } = new(true, 0, 6);
}

public sealed class UserSettingsStore
{
    internal const double MaximumOffset = 500;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public UserSettingsStore() : this(GetDefaultPath())
    {
    }

    internal UserSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return UserSettings.Default;
            }

            var settings = Deserialize(File.ReadAllText(_path));
            return settings is not null && IsValid(settings) ? settings : UserSettings.Default;
        }
        catch (JsonException)
        {
            return UserSettings.Default;
        }
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsValid(settings))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), $"Offsets must be finite values from {-MaximumOffset} through {MaximumOffset}.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }

    internal static string GetDefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageIndicator", "settings.json");

    private static UserSettings? Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(nameof(UserSettings.Enabled), out var enabled)
            || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !root.TryGetProperty(nameof(UserSettings.HorizontalOffset), out var horizontalOffset)
            || horizontalOffset.ValueKind != JsonValueKind.Number
            || !horizontalOffset.TryGetDouble(out var horizontal)
            || !root.TryGetProperty(nameof(UserSettings.VerticalOffset), out var verticalOffset)
            || verticalOffset.ValueKind != JsonValueKind.Number
            || !verticalOffset.TryGetDouble(out var vertical))
        {
            return null;
        }

        return new UserSettings(enabled.GetBoolean(), horizontal, vertical);
    }

    private static bool IsValid(UserSettings settings) =>
        double.IsFinite(settings.HorizontalOffset)
        && double.IsFinite(settings.VerticalOffset)
        && Math.Abs(settings.HorizontalOffset) <= MaximumOffset
        && Math.Abs(settings.VerticalOffset) <= MaximumOffset;
}
