using UsageIndicatorForCodex.Update;
using UsageIndicatorForCodex.UpdateHost;

try
{
    var invocation = UpdateHostArguments.Parse(args);
    UpdateHostCacheCleaner.DeleteStaleSiblings();
    var integrationConfiguration = UpdateHostProductInfo.IntegrationConfiguration;
    var installerStateSubKey = integrationConfiguration?.InstallerStateSubKey
        ?? ProductConstants.InstallerStateSubKey;
    UpdateHostLayout.Validate(invocation, installerStateSubKey);

    if (integrationConfiguration is null
        && string.IsNullOrWhiteSpace(UpdateHostProductInfo.RepositoryUrl))
    {
        throw new InvalidOperationException(
            "This build does not contain an explicitly configured GitHub repository URL.");
    }

    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    var localStateRoot = integrationConfiguration?.LocalStateRoot
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsageIndicatorForCodex");
    IReleaseUpdateClient releaseClient = integrationConfiguration is null
        ? new ReleaseUpdateClient(
            httpClient,
            UpdateHostProductInfo.RepositoryUrl!,
            UpdateHostProductInfo.Version)
        : new ReleaseUpdateClient(
            httpClient,
            integrationConfiguration.ReleaseApiUri,
            UpdateHostProductInfo.Version,
            allowLoopbackHttp: true);
    Func<IUpdateMutexLease> mutexFactory = integrationConfiguration is null
        ? UpdateMutexLease.CreateForCurrentUser
        : () => new UpdateMutexLease(integrationConfiguration.InstanceIdentity);
    IIndicatorController indicatorController = integrationConfiguration is null
        ? SingleInstanceClient.CreateForCurrentUser(invocation.InstallRoot)
        : new SingleInstanceClient(
            integrationConfiguration.InstanceIdentity,
            Path.Combine(
                invocation.InstallRoot,
                ProductConstants.LauncherRelativePath));
    var orchestrator = new UpdateOrchestrator(
        releaseClient,
        mutexFactory,
        indicatorController,
        new InstallerRunner(),
        new InstalledVersionValidator(installerStateSubKey),
        new UpdateWorkingDirectoryCleaner(),
        new ConsoleUpdateOutput(),
        new UpdatePaths(
            invocation.InstallRoot,
            Path.Combine(localStateRoot, "updates"),
            Path.Combine(localStateRoot, "update-logs")));

    var outcome = invocation.Command == UpdateHostCommand.CheckUpdate
        ? await orchestrator.CheckAsync(CancellationToken.None)
        : await orchestrator.UpdateAsync(CancellationToken.None);
    return outcome.ExitCode;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"Update host invocation failed. {exception.Message}");
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Update failed. {exception.Message}");
    return 1;
}
