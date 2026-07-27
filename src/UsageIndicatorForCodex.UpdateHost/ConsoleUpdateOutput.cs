using UsageIndicatorForCodex.Update;

namespace UsageIndicatorForCodex.UpdateHost;

internal sealed class ConsoleUpdateOutput : IUpdateOutput
{
    public void WriteLine(string message) => Console.Out.WriteLine(message);

    public void WriteError(string message) => Console.Error.WriteLine(message);
}
