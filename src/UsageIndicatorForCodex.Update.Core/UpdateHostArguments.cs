namespace UsageIndicatorForCodex.Update;

internal enum UpdateHostCommand
{
    CheckUpdate,
    Update
}

internal sealed record UpdateHostArguments(
    UpdateHostCommand Command,
    string InstallRoot,
    int BootstrapVersion)
{
    internal static UpdateHostArguments Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 6
            || arguments[0] != "--command"
            || arguments[2] != "--install-root"
            || arguments[4] != "--bootstrap-version")
        {
            throw new ArgumentException("The private update-host invocation is invalid.");
        }

        var command = arguments[1] switch
        {
            "check-update" => UpdateHostCommand.CheckUpdate,
            "update" => UpdateHostCommand.Update,
            _ => throw new ArgumentException("The private update-host command is invalid.")
        };

        if (!Path.IsPathFullyQualified(arguments[3]))
        {
            throw new ArgumentException("The private installation root must be absolute.");
        }

        var installRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(arguments[3]));
        if (!int.TryParse(arguments[5], out var bootstrapVersion)
            || bootstrapVersion != ProductConstants.BootstrapProtocolVersion)
        {
            throw new ArgumentException("The private bootstrap protocol version is unsupported.");
        }

        return new UpdateHostArguments(command, installRoot, bootstrapVersion);
    }
}
