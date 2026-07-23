using System.Security.Principal;
using System.Runtime.InteropServices;

namespace CodexUsageIndicator.Services;

public static class StartupTaskManager
{
    private const string TaskName = "CodexUsageIndicator";

    internal static bool IsInstallationEnabled => true;

    public static void Install(string executablePath)
    {
        var configuration = CreateConfiguration(WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The installing Windows user could not be identified."));
        var schedulerType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(schedulerType)
            ?? throw new InvalidOperationException("Windows Task Scheduler could not be started.");
        service.Connect();
        dynamic root = service.GetFolder("\\");
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "Shows the Codex usage indicator companion.";
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

        root.RegisterTaskDefinition(TaskName, definition, 6, configuration.UserId, null, 3, null); // create-or-update, interactive token
    }

    internal static StartupTaskConfiguration CreateConfiguration(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new StartupTaskConfiguration(userId, "--background", 3, "PT1M", "PT0S");
    }

    public static void Uninstall()
    {
        var schedulerType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(schedulerType)
            ?? throw new InvalidOperationException("Windows Task Scheduler could not be started.");
        service.Connect();
        dynamic root = service.GetFolder("\\");
        try
        {
            root.DeleteTask(TaskName, 0);
        }
        catch (COMException exception) when (IsMissingTaskError(exception.HResult))
        {
            // Absence is already the desired uninstalled state.
        }
    }

    internal static bool IsMissingTaskError(int hresult) => unchecked((uint)hresult) == 0x80070002;
}

internal sealed record StartupTaskConfiguration(
    string UserId,
    string Arguments,
    int RestartCount,
    string RestartInterval,
    string ExecutionTimeLimit);
