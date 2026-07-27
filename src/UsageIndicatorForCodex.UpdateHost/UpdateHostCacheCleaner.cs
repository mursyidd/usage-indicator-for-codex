namespace UsageIndicatorForCodex.UpdateHost;

internal static class UpdateHostCacheCleaner
{
    internal static void DeleteStaleSiblings()
    {
        var currentPath = Environment.ProcessPath;
        var directory = currentPath is null ? null : Path.GetDirectoryName(currentPath);
        if (string.IsNullOrWhiteSpace(currentPath)
            || string.IsNullOrWhiteSpace(directory)
            || !Path.GetFileName(currentPath).StartsWith(
                "UsageIndicatorForCodex.UpdateHost.",
                StringComparison.Ordinal)
            || !Path.GetFileName(directory).StartsWith('v'))
        {
            return;
        }

        try
        {
            foreach (var candidate in Directory.EnumerateFiles(
                directory,
                "UsageIndicatorForCodex.UpdateHost.*.exe"))
            {
                if (string.Equals(candidate, currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Delete(candidate);
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DirectoryNotFoundException)
        {
        }
    }
}
