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

internal enum StartupTaskRegistrationMode
{
    Create,
    Update
}

internal sealed class StartupTaskOwnershipException : InvalidOperationException
{
    internal StartupTaskOwnershipException(string message)
        : base(message)
    {
    }

    internal int ExitCode => StartupTaskManager.OwnershipCollisionExitCode;
}

public static class StartupTaskManager
{
    internal const string TaskName = "UsageIndicatorForCodex";
    internal const string LegacyTaskName = "CodexUsageIndicator";
    internal const string LauncherExecutableName = "UsageIndicatorForCodex.exe";
    internal const string LegacyExecutableName = "CodexUsageIndicator.exe";
    internal const int OwnershipCollisionExitCode = 2;
    private const string BackgroundArgument = "--background";

    internal static bool IsInstallationEnabled => true;

    public static void Install(string executablePath) =>
        Install(executablePath, new ComStartupTaskScheduler());

    internal static void Install(string executablePath, IStartupTaskScheduler scheduler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(scheduler);
        var inventory = ReadInventory(executablePath, scheduler);
        ThrowIfUnrecognizedForEnable(inventory);

        scheduler.Register(
            TaskName,
            executablePath,
            CreateConfigurationForCurrentUser(),
            inventory.CanonicalOwnership == StartupTaskOwnership.Absent
                ? StartupTaskRegistrationMode.Create
                : StartupTaskRegistrationMode.Update);
        if (inventory.LegacyOwnership == StartupTaskOwnership.Recognized)
        {
            DeleteIgnoringMissing(scheduler, LegacyTaskName);
        }
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
        var inventory = ReadInventory(executablePath, scheduler);
        if (inventory.UnrecognizedNames.Count > 0
            || inventory.LegacyOwnership != StartupTaskOwnership.Recognized)
        {
            return false;
        }

        scheduler.Register(
            TaskName,
            executablePath,
            CreateConfigurationForCurrentUser(),
            inventory.CanonicalOwnership == StartupTaskOwnership.Absent
                ? StartupTaskRegistrationMode.Create
                : StartupTaskRegistrationMode.Update);
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
        var inventory = ReadInventory(executablePath, scheduler);
        if (inventory.UnrecognizedNames.Count > 0)
        {
            return StartupTaskState.Unrecognized;
        }

        if (inventory.Canonical?.IsEnabled == true || inventory.Legacy?.IsEnabled == true)
        {
            return StartupTaskState.Enabled;
        }

        return StartupTaskState.Disabled;
    }

    public static void Uninstall(string executablePath) =>
        Uninstall(executablePath, new ComStartupTaskScheduler());

    internal static void Uninstall(
        string executablePath,
        IStartupTaskScheduler scheduler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(scheduler);
        var inventory = ReadInventory(executablePath, scheduler);
        var failures = new List<Exception>();
        if (inventory.CanonicalOwnership == StartupTaskOwnership.Recognized)
        {
            try
            {
                DeleteIgnoringMissing(scheduler, TaskName);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (inventory.LegacyOwnership == StartupTaskOwnership.Recognized)
        {
            try
            {
                DeleteIgnoringMissing(scheduler, LegacyTaskName);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count == 1)
        {
            throw failures[0];
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("Automatic startup tasks could not be removed.", failures);
        }

        if (inventory.UnrecognizedNames.Count > 0)
        {
            throw new StartupTaskOwnershipException(
                "Startup cleanup preserved unrecognized scheduled task names that must be inspected manually: "
                + string.Join(", ", inventory.UnrecognizedNames)
                + ".");
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
        if (actualPath is null || expectedPath is null)
        {
            return false;
        }

        if (string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var applicationDirectory = Path.GetDirectoryName(expectedPath);
        if (string.IsNullOrWhiteSpace(applicationDirectory))
        {
            return false;
        }

        var expectedLauncherPath = Path.Combine(
            applicationDirectory,
            LauncherExecutableName);
        return string.Equals(
            actualPath,
            expectedLauncherPath,
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

    private static StartupTaskInventory ReadInventory(
        string executablePath,
        IStartupTaskScheduler scheduler)
    {
        var expectedPath = NormalizeExecutablePath(executablePath)
            ?? throw new ArgumentException(
                "The startup executable path must be fully qualified.",
                nameof(executablePath));
        var canonical = scheduler.Get(TaskName);
        var legacy = scheduler.Get(LegacyTaskName);
        return new StartupTaskInventory(
            canonical,
            legacy,
            Classify(canonical, task => IsRecognizedCanonicalTask(task, expectedPath)),
            Classify(legacy, IsRecognizedLegacyTask));
    }

    private static StartupTaskOwnership Classify(
        StartupTaskInfo? task,
        Func<StartupTaskInfo?, bool> isRecognized)
    {
        if (task is null)
        {
            return StartupTaskOwnership.Absent;
        }

        return isRecognized(task)
            ? StartupTaskOwnership.Recognized
            : StartupTaskOwnership.Unrecognized;
    }

    private static void ThrowIfUnrecognizedForEnable(StartupTaskInventory inventory)
    {
        if (inventory.UnrecognizedNames.Count > 0)
        {
            throw new StartupTaskOwnershipException(
                "Startup was not enabled because unrecognized scheduled task names must be inspected manually: "
                + string.Join(", ", inventory.UnrecognizedNames)
                + ".");
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

internal enum StartupTaskOwnership
{
    Absent,
    Recognized,
    Unrecognized
}

internal sealed record StartupTaskInventory(
    StartupTaskInfo? Canonical,
    StartupTaskInfo? Legacy,
    StartupTaskOwnership CanonicalOwnership,
    StartupTaskOwnership LegacyOwnership)
{
    internal IReadOnlyList<string> UnrecognizedNames
    {
        get
        {
            var names = new List<string>(2);
            if (CanonicalOwnership == StartupTaskOwnership.Unrecognized)
            {
                names.Add(StartupTaskManager.TaskName);
            }
            if (LegacyOwnership == StartupTaskOwnership.Unrecognized)
            {
                names.Add(StartupTaskManager.LegacyTaskName);
            }
            return names;
        }
    }
}

internal interface IStartupTaskScheduler
{
    StartupTaskInfo? Get(string taskName);
    void Register(
        string taskName,
        string executablePath,
        StartupTaskConfiguration configuration,
        StartupTaskRegistrationMode mode);
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
        StartupTaskConfiguration configuration,
        StartupTaskRegistrationMode mode)
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
            mode == StartupTaskRegistrationMode.Create ? 2 : 4,
            configuration.UserId,
            null,
            3,
            null); // create or update only after ownership inspection
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
