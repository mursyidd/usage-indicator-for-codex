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
    Toggle,
    RevalidateCli,
    Help,
    Invalid
}

internal sealed record CommandLineOptions(CommandLineAction Action, int ExitCode, string Message)
{
    internal const string Usage = """
        Usage Indicator for Codex

        Installed command:
          usage-indicator start
          usage-indicator stop
          usage-indicator status
          usage-indicator version
          usage-indicator check-update
          usage-indicator update
          usage-indicator enable-startup
          usage-indicator disable-startup
          usage-indicator help

        Portable compatibility:
          UsageIndicatorForCodex.exe
          UsageIndicatorForCodex.exe --background
          UsageIndicatorForCodex.exe --install
          UsageIndicatorForCodex.exe --uninstall
          UsageIndicatorForCodex.exe --toggle
          UsageIndicatorForCodex.exe --revalidate-cli
          UsageIndicatorForCodex.exe --exit
          UsageIndicatorForCodex.exe --help
          UsageIndicatorForCodex.exe -h

        Running usage-indicator without arguments shows this help.
        Running UsageIndicatorForCodex.exe without arguments starts the application.
        Portable updates are not supported; download and run the installer, or replace the complete portable directory manually.
        --install registers automatic startup only; it does not launch the application.
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
            "stop" or "--exit" => new CommandLineOptions(CommandLineAction.Stop, 0, string.Empty),
            "status" => new CommandLineOptions(CommandLineAction.Status, 0, string.Empty),
            "version" => new CommandLineOptions(CommandLineAction.Version, 0, string.Empty),
            "check-update" => new CommandLineOptions(CommandLineAction.CheckUpdate, 0, string.Empty),
            "update" => new CommandLineOptions(CommandLineAction.Update, 0, string.Empty),
            "enable-startup" or "--install" => new CommandLineOptions(CommandLineAction.EnableStartup, 0, string.Empty),
            "disable-startup" or "--uninstall" => new CommandLineOptions(CommandLineAction.DisableStartup, 0, string.Empty),
            "--toggle" => new CommandLineOptions(CommandLineAction.Toggle, 0, string.Empty),
            "--revalidate-cli" => new CommandLineOptions(CommandLineAction.RevalidateCli, 0, string.Empty),
            "help" or "--help" or "-h" => new CommandLineOptions(CommandLineAction.Help, 0, Usage),
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
