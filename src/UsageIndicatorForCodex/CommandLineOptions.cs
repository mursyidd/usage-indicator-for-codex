using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace UsageIndicatorForCodex;

internal enum CommandLineAction
{
    Run,
    Install,
    Uninstall,
    Toggle,
    RevalidateCli,
    Exit,
    Help,
    Invalid
}

internal sealed record CommandLineOptions(CommandLineAction Action, int ExitCode, string Message)
{
    internal const string Usage = """
        Usage Indicator for Codex

        Usage:
          UsageIndicatorForCodex.exe
          UsageIndicatorForCodex.exe --background
          UsageIndicatorForCodex.exe --install
          UsageIndicatorForCodex.exe --uninstall
          UsageIndicatorForCodex.exe --toggle
          UsageIndicatorForCodex.exe --revalidate-cli
          UsageIndicatorForCodex.exe --exit
          UsageIndicatorForCodex.exe --help
          UsageIndicatorForCodex.exe -h

        Normal launch starts the application immediately.
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
            "--background" => new CommandLineOptions(CommandLineAction.Run, 0, string.Empty),
            "--install" => new CommandLineOptions(CommandLineAction.Install, 0, string.Empty),
            "--uninstall" => new CommandLineOptions(CommandLineAction.Uninstall, 0, string.Empty),
            "--toggle" => new CommandLineOptions(CommandLineAction.Toggle, 0, string.Empty),
            "--revalidate-cli" => new CommandLineOptions(CommandLineAction.RevalidateCli, 0, string.Empty),
            "--exit" => new CommandLineOptions(CommandLineAction.Exit, 0, string.Empty),
            "--help" or "-h" => new CommandLineOptions(CommandLineAction.Help, 0, Usage),
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
