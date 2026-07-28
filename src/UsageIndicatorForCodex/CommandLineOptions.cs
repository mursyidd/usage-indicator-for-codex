using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace UsageIndicatorForCodex;

internal enum CommandLineAction
{
    Run,
    Stop,
    Status,
    Version,
    CheckUpdate,
    Update,
    EnableStartup,
    DisableStartup,
    EnableCreditExpiry,
    DisableCreditExpiry,
    Help,
    Invalid
}

internal sealed record CommandLineOptions(CommandLineAction Action, int ExitCode, string Message)
{
    internal const string Usage = """
        Usage Indicator for Codex

        Commands:
          usage-indicator start
          usage-indicator stop
          usage-indicator status
          usage-indicator version
          usage-indicator check-update
          usage-indicator update
          usage-indicator enable-startup
          usage-indicator disable-startup
          usage-indicator enable-credit-expiry
          usage-indicator disable-credit-expiry
          usage-indicator help

        Keyboard shortcut:
          Ctrl+Alt+U    Turn the indicator display on or off while running

        Running usage-indicator without arguments shows this help.
        """;

    internal static CommandLineOptions Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return new CommandLineOptions(CommandLineAction.Run, 0, string.Empty);
        }

        if (arguments.Count != 1)
        {
            return Invalid("Exactly one command may be specified.");
        }

        return arguments[0] switch
        {
            "start" or "--background" => new CommandLineOptions(CommandLineAction.Run, 0, string.Empty),
            "stop" => new CommandLineOptions(CommandLineAction.Stop, 0, string.Empty),
            "status" => new CommandLineOptions(CommandLineAction.Status, 0, string.Empty),
            "version" => new CommandLineOptions(CommandLineAction.Version, 0, string.Empty),
            "check-update" => new CommandLineOptions(CommandLineAction.CheckUpdate, 0, string.Empty),
            "update" => new CommandLineOptions(CommandLineAction.Update, 0, string.Empty),
            "enable-startup" => new CommandLineOptions(CommandLineAction.EnableStartup, 0, string.Empty),
            "disable-startup" => new CommandLineOptions(CommandLineAction.DisableStartup, 0, string.Empty),
            "enable-credit-expiry" => new CommandLineOptions(CommandLineAction.EnableCreditExpiry, 0, string.Empty),
            "disable-credit-expiry" => new CommandLineOptions(CommandLineAction.DisableCreditExpiry, 0, string.Empty),
            "help" => new CommandLineOptions(CommandLineAction.Help, 0, Usage),
            _ => Invalid($"Unknown argument: {arguments[0]}")
        };
    }

    private static CommandLineOptions Invalid(string error) =>
        new(CommandLineAction.Invalid, 2, $"{error}{Environment.NewLine}{Environment.NewLine}{Usage}");
}

internal static class CommandLineOutput
{
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private static readonly nint InvalidHandleValue = new(-1);

    internal static void Show(string message, bool isError)
    {
        if (TryWriteToParentConsole(message, isError))
        {
            return;
        }

        MessageBox.Show(
            message,
            "Usage Indicator for Codex",
            MessageBoxButton.OK,
            isError ? MessageBoxImage.Error : MessageBoxImage.Information);
    }

    private static bool TryWriteToParentConsole(string message, bool isError)
    {
        _ = AttachConsole(AttachParentProcess);
        var handle = GetStdHandle(isError ? StdErrorHandle : StdOutputHandle);
        if (handle is 0 || handle == InvalidHandleValue)
        {
            return false;
        }

        try
        {
            var stream = isError ? Console.OpenStandardError() : Console.OpenStandardOutput();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            writer.WriteLine(message);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int standardHandle);
}
