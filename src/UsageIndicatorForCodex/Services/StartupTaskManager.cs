using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace UsageIndicatorForCodex.Services;

internal enum StartupTaskState
{
    Enabled,
    Disabled,
    Unrecognized
}

public static class StartupTaskManager
{
    internal const string TaskName = "UsageIndicatorForCodex";
    internal const string LegacyTaskName = "CodexUsageIndicator";
    internal const string LegacyExecutableName = "CodexUsageIndicator.exe";
    private const string BackgroundArgument = "--background";

    internal static bool IsInstallationEnabled => true;

    public static void Install(string executablePath) =>
        Install(executablePath, new ComStartupTaskScheduler());

    internal static void Install(string executablePath, IStartupTaskScheduler scheduler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(scheduler);
        scheduler.Register(TaskName, executablePath, CreateConfigurationForCurrentUser());
        DeleteLegacyTaskIfRecognized(scheduler);
    }

    internal static bool TryMigrateLegacyTask(string executablePath)
    {
        try
        {
            return MigrateLegacyTask(executablePath, new ComStartupTaskScheduler());
        }
        catch
        {
            return false;
        }
    }

    internal static bool MigrateLegacyTask(string executablePath, IStartupTaskScheduler scheduler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(scheduler);
        if (!IsRecognizedLegacyTask(TryGetLegacyTask(scheduler)))
        {
            return false;
        }

        scheduler.Register(TaskName, executablePath, CreateConfigurationForCurrentUser());
        DeleteIgnoringMissing(scheduler, LegacyTaskName);
        return true;
    }

    internal static StartupTaskConfiguration CreateConfiguration(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new StartupTaskConfiguration(userId, "--background", 3, "PT1M", "PT0S");
    }

    internal static StartupTaskState Inspect(string executablePath) =>
        Inspect(executablePath, new ComStartupTaskScheduler());

    internal static StartupTaskState Inspect(
        string executablePath,
        IStartupTaskScheduler scheduler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(scheduler);
        var expectedPath = NormalizeExecutablePath(executablePath)
            ?? throw new ArgumentException(
                "The startup executable path must be fully qualified.",
                nameof(executablePath));
        var canonical = scheduler.Get(TaskName);
        var legacy = scheduler.Get(LegacyTaskName);
        var canonicalRecognized = canonical is null
            || IsRecognizedCanonicalTask(canonical, expectedPath);
        var legacyRecognized = legacy is null || IsRecognizedLegacyTask(legacy);
        if (!canonicalRecognized || !legacyRecognized)
        {
            return StartupTaskState.Unrecognized;
        }

        if (canonical?.IsEnabled == true || legacy?.IsEnabled == true)
        {
            return StartupTaskState.Enabled;
        }

        return StartupTaskState.Disabled;
    }

    public static void Uninstall() => Uninstall(new ComStartupTaskScheduler());

    internal static void Uninstall(IStartupTaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        var failures = new List<Exception>();
        try
        {
            DeleteIgnoringMissing(scheduler, TaskName);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            DeleteLegacyTaskIfRecognized(scheduler);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count == 1)
        {
            throw failures[0];
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("Automatic startup tasks could not be removed.", failures);
        }
    }

    internal static bool IsMissingTaskError(int hresult) => unchecked((uint)hresult) == 0x80070002;

    internal static bool IsRecognizedLegacyTask(StartupTaskInfo? task)
    {
        if (task is null
            || !string.Equals(task.Arguments.Trim(), BackgroundArgument, StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedPath = NormalizeExecutablePath(task.ExecutablePath);
        return normalizedPath is not null
            && string.Equals(
                Path.GetFileName(normalizedPath),
                LegacyExecutableName,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsRecognizedCanonicalTask(
        StartupTaskInfo? task,
        string expectedExecutablePath)
    {
        if (task is null
            || !string.Equals(
                task.Arguments.Trim(),
                BackgroundArgument,
                StringComparison.Ordinal))
        {
            return false;
        }

        var actualPath = NormalizeExecutablePath(task.ExecutablePath);
        var expectedPath = NormalizeExecutablePath(expectedExecutablePath);
        return actualPath is not null
            && expectedPath is not null
            && string.Equals(
                actualPath,
                expectedPath,
                StringComparison.OrdinalIgnoreCase);
    }

    private static StartupTaskConfiguration CreateConfigurationForCurrentUser() =>
        CreateConfiguration(WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The Windows user could not be identified."));

    private static string? NormalizeExecutablePath(string path)
    {
        var candidate = path.Trim();
        if (candidate.Length >= 2 && candidate[0] == '"' && candidate[^1] == '"')
        {
            candidate = candidate[1..^1].Trim();
        }
        else if (candidate.Contains('"'))
        {
            return null;
        }

        try
        {
            return Path.IsPathFullyQualified(candidate)
                && !Path.EndsInDirectorySeparator(candidate)
                ? Path.GetFullPath(candidate)
                : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static void DeleteLegacyTaskIfRecognized(IStartupTaskScheduler scheduler)
    {
        if (IsRecognizedLegacyTask(TryGetLegacyTask(scheduler)))
        {
            DeleteIgnoringMissing(scheduler, LegacyTaskName);
        }
    }

    private static StartupTaskInfo? TryGetLegacyTask(IStartupTaskScheduler scheduler)
    {
        try
        {
            return scheduler.Get(LegacyTaskName);
        }
        catch
        {
            // If ownership cannot be confirmed, preserve the task and continue.
            return null;
        }
    }

    private static void DeleteIgnoringMissing(IStartupTaskScheduler scheduler, string taskName)
    {
        try
        {
            scheduler.Delete(taskName);
        }
        catch (Exception exception) when (IsMissingTaskError(exception.HResult))
        {
            // Absence is already the desired state.
        }
    }
}

internal interface IStartupTaskScheduler
{
    StartupTaskInfo? Get(string taskName);
    void Register(string taskName, string executablePath, StartupTaskConfiguration configuration);
    void Delete(string taskName);
}

internal sealed class ComStartupTaskScheduler : IStartupTaskScheduler
{
    private readonly dynamic _service;
    private readonly dynamic _root;

    internal ComStartupTaskScheduler()
    {
        var schedulerType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        _service = Activator.CreateInstance(schedulerType)
            ?? throw new InvalidOperationException("Windows Task Scheduler could not be started.");
        _service.Connect();
        _root = _service.GetFolder("\\");
    }

    public StartupTaskInfo? Get(string taskName)
    {
        dynamic task;
        try
        {
            task = _root.GetTask(taskName);
        }
        catch (Exception exception) when (StartupTaskManager.IsMissingTaskError(exception.HResult))
        {
            return null;
        }

        dynamic definition = task.Definition;
        dynamic actions = definition.Actions;
        var description = (string?)definition.RegistrationInfo.Description ?? string.Empty;
        if ((int)actions.Count != 1)
        {
            return new StartupTaskInfo(string.Empty, string.Empty, description, (bool)task.Enabled);
        }

        dynamic action = actions.Item(1);
        if ((int)action.Type != 0) // TASK_ACTION_EXEC
        {
            return new StartupTaskInfo(string.Empty, string.Empty, description, (bool)task.Enabled);
        }

        return new StartupTaskInfo(
            (string?)action.Path ?? string.Empty,
            (string?)action.Arguments ?? string.Empty,
            description,
            (bool)task.Enabled);
    }

    public void Register(
        string taskName,
        string executablePath,
        StartupTaskConfiguration configuration)
    {
        dynamic definition = _service.NewTask(0);
        definition.RegistrationInfo.Description = "Shows the Usage Indicator for Codex companion.";
        definition.Settings.Enabled = true;
        definition.Settings.Hidden = true;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = configuration.ExecutionTimeLimit;
        definition.Settings.RestartCount = configuration.RestartCount;
        definition.Settings.RestartInterval = configuration.RestartInterval;
        definition.Principal.UserId = configuration.UserId;
        definition.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN

        dynamic trigger = definition.Triggers.Create(9); // TASK_TRIGGER_LOGON
        trigger.Enabled = true;
        trigger.UserId = configuration.UserId;

        dynamic action = definition.Actions.Create(0); // TASK_ACTION_EXEC
        action.Path = executablePath;
        action.Arguments = configuration.Arguments;

        _root.RegisterTaskDefinition(
            taskName,
            definition,
            6,
            configuration.UserId,
            null,
            3,
            null); // create-or-update, interactive token
    }

    public void Delete(string taskName) => _root.DeleteTask(taskName, 0);
}

internal sealed record StartupTaskConfiguration(
    string UserId,
    string Arguments,
    int RestartCount,
    string RestartInterval,
    string ExecutionTimeLimit);

internal sealed record StartupTaskInfo(
    string ExecutablePath,
    string Arguments,
    string Description,
    bool IsEnabled = true);
