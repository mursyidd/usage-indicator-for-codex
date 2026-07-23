using System.Text.Json;
using System.IO;
using System.Text;

namespace UsageIndicatorForCodex.Services;

public sealed record UserSettings(bool Enabled, double HorizontalOffset, double VerticalOffset)
{
    public static UserSettings Default { get; } = new(true, 0, 6);
}

public sealed class UserSettingsStore
{
    internal const double MaximumOffset = 500;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly string? _legacyPath;
    private readonly Action? _beforeMigrationCommit;

    public UserSettingsStore() : this(GetDefaultPath(), GetLegacyPath())
    {
    }

    internal UserSettingsStore(string path) : this(path, null)
    {
    }

    internal UserSettingsStore(string path, string? legacyPath, Action? beforeMigrationCommit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (legacyPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);
        }

        _path = path;
        _legacyPath = legacyPath;
        _beforeMigrationCommit = beforeMigrationCommit;
    }

    public UserSettings Load()
    {
        if (File.Exists(_path))
        {
            return LoadPath(_path);
        }

        if (_legacyPath is null || !File.Exists(_legacyPath))
        {
            return UserSettings.Default;
        }

        var legacySettings = LoadValid(_legacyPath);
        if (legacySettings is null)
        {
            return UserSettings.Default;
        }

        return Migrate(legacySettings);
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsValid(settings))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), $"Offsets must be finite values from {-MaximumOffset} through {MaximumOffset}.");
        }

        var temporaryPath = WriteTemporaryFile(_path, settings);
        try
        {
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    internal static string GetDefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageIndicatorForCodex", "settings.json");

    internal static string GetLegacyPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageIndicator", "settings.json");

    private UserSettings Migrate(UserSettings settings)
    {
        string? temporaryPath = null;
        try
        {
            temporaryPath = WriteTemporaryFile(_path, settings);
            _beforeMigrationCommit?.Invoke();
            File.Move(temporaryPath, _path, overwrite: false);
            temporaryPath = null;
            return settings;
        }
        catch (IOException)
        {
            return File.Exists(_path) ? LoadPath(_path) : UserSettings.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return UserSettings.Default;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                DeleteTemporaryFile(temporaryPath);
            }
        }
    }

    private static UserSettings LoadPath(string path) => LoadValid(path) ?? UserSettings.Default;

    private static UserSettings? LoadValid(string path)
    {
        try
        {
            var settings = Deserialize(File.ReadAllText(path));
            return settings is not null && IsValid(settings) ? settings : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string WriteTemporaryFile(string destinationPath, UserSettings settings)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, JsonOptions));
            using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return temporaryPath;
        }
        catch
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

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
