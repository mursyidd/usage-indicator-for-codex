using CodexUsageIndicator.Core;
using CodexUsageIndicator.Services;
using CodexUsageIndicator.Views;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

if (args.Contains("--fake-app-server", StringComparer.OrdinalIgnoreCase))
{
    await RunFakeAppServerAsync(
        args.Contains("--rpc-error", StringComparer.OrdinalIgnoreCase),
        args.Contains("--hang-after-initialize", StringComparer.OrdinalIgnoreCase),
        args.Contains("--mark-started", StringComparer.OrdinalIgnoreCase));
    return 0;
}

if (args.Contains("--probe", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        var snapshot = await new CodexCliAppServerReader().ReadAsync(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(snapshot.AccountFingerprint) || snapshot.ResetsAt <= DateTimeOffset.UtcNow || snapshot.RemainingPercent is < 0 or > 100)
        {
            throw new InvalidOperationException("The app-server response did not satisfy the account-scoped usage contract.");
        }

        Console.WriteLine("PASS account-scoped usage provider verified without logging account or usage values");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL account-scoped usage provider: {exception.Message}");
        return 1;
    }
}

if (args.Contains("--probe-isolated", StringComparer.OrdinalIgnoreCase))
{
    var isolatedHome = Path.Combine(Path.GetTempPath(), $"CodexUsageIndicator-Isolated-{Guid.NewGuid():N}");
    Directory.CreateDirectory(isolatedHome);
    try
    {
        var reader = new CodexCliAppServerReader(CodexCliAppServerReader.ResolveCliPath(), isolatedHome, TimeSpan.FromSeconds(20));
        _ = await reader.ReadAsync(CancellationToken.None);
        Console.Error.WriteLine("FAIL isolated CODEX_HOME unexpectedly returned usable account usage");
        return 1;
    }
    catch (Exception)
    {
        Console.WriteLine("PASS isolated CODEX_HOME fails closed without using the active CLI account");
        return 0;
    }
    finally
    {
        Directory.Delete(isolatedHome, recursive: true);
    }
}

if (args.Contains("--probe-timeout", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        var reader = new CodexCliAppServerReader(CodexCliAppServerReader.ResolveCliPath(), null, TimeSpan.Zero);
        _ = await reader.ReadAsync(CancellationToken.None);
        Console.Error.WriteLine("FAIL zero-timeout CLI reader unexpectedly returned usage");
        return 1;
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("PASS CLI startup timeout fails closed and terminates the helper process tree");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL timeout probe returned an unexpected error: {exception.Message}");
        return 1;
    }
}

var checks = new (string Name, Action Run)[]
{
    ("selects the most restrictive active window", SelectsMostRestrictiveWindow),
    ("uses approved color thresholds", UsesApprovedTones),
    ("keeps the approved neutral Usage wording", KeepsApprovedUsageWording),
    ("formats MYT timestamps without a zone label", FormatsMalaysiaTime),
    ("selects every responsive layout", SelectsResponsiveLayouts),
    ("sizes the full overlay to its rendered content", SizesFullOverlayToContent),
    ("measures layouts against available title-bar space", MeasuresLayoutsAgainstAvailableWidth),
    ("coalesces overlapping refresh requests", CoalescesOverlappingRefreshRequests),
    ("cancels and replaces refresh requests", CancelsAndReplacesRefreshRequests),
    ("routes commands to a single primary instance", RoutesCommandsToPrimaryInstance),
    ("waits for a revalidation command response", WaitsForRevalidationCommandResponse),
    ("fails command delivery cleanly when no primary pipe exists", FailsCommandDeliveryWithoutPrimaryPipe),
    ("retains the attached Codex window across foreign focus", RetainsAttachedWindowAcrossForeignFocus),
    ("ignores overlay location events while tracking the attached Codex window", IgnoresOverlayLocationEvents),
    ("observes attached Codex windows being hidden", ObservesAttachedWindowHide),
    ("identifies the Codex Desktop window by package identity", IdentifiesCodexDesktopWindow),
    ("selects the most recently active Codex window on startup", SelectsInitialAttachedWindow),
    ("rejects expired-only usage windows", RejectsExpiredWindows),
    ("parses primary and secondary app-server limits", ParsesRateLimitResponse),
    ("accepts an absent optional app-server rate-limit window", AcceptsAbsentOptionalRateLimitWindow),
    ("rejects malformed and incompatible app-server responses", RejectsInvalidResponses),
    ("rejects out-of-range rate-limit percentages", RejectsOutOfRangePercentages),
    ("fails closed when the configured CLI is absent", FailsWhenCliIsMissing),
    ("accepts exe and cmd CLI overrides", AcceptsExeAndCmdCliOverrides),
    ("rejects relative and unsupported CLI overrides", RejectsInvalidCliOverrides),
    ("resolves native exe candidates before cmd candidates", ResolvesNativeExeBeforeCmdCandidates),
    ("falls through an unlaunchable automatic exe to cmd", FallsThroughUnlaunchableAutomaticExe),
    ("does not fall through after an explicit launcher fails", DoesNotFallThroughAfterExplicitLauncherFailure),
    ("does not fall through after a started app-server RPC failure", DoesNotFallThroughAfterStartedRpcFailure),
    ("cancels an app-server and cleans up its process", CancelsAndCleansUpAppServer),
    ("quotes a CLI path containing spaces", QuotesCliPath),
    ("runs a spaced cmd launcher through the app-server protocol", RunsSpacedCmdLauncher),
    ("loads valid per-user settings", LoadsValidUserSettings),
    ("falls back atomically for malformed settings", FallsBackForMalformedUserSettings),
    ("rejects invalid setting offsets", RejectsInvalidUserSettingOffsets),
    ("uses the ordinary per-user settings path", UsesPerUserSettingsPath),
    ("terminates a provider process tree", TerminatesProcessTree),
    ("enables production live usage for the configured CLI account", ProductionProviderIsEnabled),
    ("enables production startup installation", StartupInstallationIsEnabled),
    ("scopes production startup to the installing user", ScopesStartupToInstallingUser),
    ("does not mask startup-task removal failures", DoesNotMaskStartupTaskRemovalFailures)
};

var failures = new List<string>();
foreach (var check in checks)
{
    try
    {
        check.Run();
        Console.WriteLine($"PASS {check.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {check.Name}: {exception.Message}");
    }
}

foreach (var failure in failures)
{
    Console.Error.WriteLine(failure);
}

return failures.Count == 0 ? 0 : 1;

static void SelectsMostRestrictiveWindow()
{
    var now = DateTimeOffset.UtcNow;
    var snapshot = IndicatorPresentation.SelectMostRestrictive("account", new[]
    {
        new RateLimitWindow(35, now.AddHours(3)),
        new RateLimitWindow(82, now.AddHours(12))
    });

    AssertEqual(18, snapshot.RemainingPercent);
}

static void UsesApprovedTones()
{
    AssertEqual(IndicatorTone.Green, IndicatorPresentation.GetTone(50));
    AssertEqual(IndicatorTone.Amber, IndicatorPresentation.GetTone(49));
    AssertEqual(IndicatorTone.Red, IndicatorPresentation.GetTone(19));
}

static void KeepsApprovedUsageWording()
{
    var snapshot = new UsageSnapshot("cli-account", 18, DateTimeOffset.UtcNow.AddHours(1));

    AssertEqual("Usage 18% left", IndicatorPresentation.FormatUsageLabel(IndicatorState.Available, snapshot, OverlayLayout.Full));
    AssertEqual("Usage 18%", IndicatorPresentation.FormatUsageLabel(IndicatorState.Available, snapshot, OverlayLayout.Compact));
    AssertEqual("Usage —", IndicatorPresentation.FormatUsageLabel(IndicatorState.Loading, null, OverlayLayout.Full));
    AssertEqual("Usage unavailable", IndicatorPresentation.FormatUsageLabel(IndicatorState.Unavailable, null, OverlayLayout.Full));
}

static void FormatsMalaysiaTime()
{
    var timestamp = new DateTimeOffset(2026, 7, 24, 2, 30, 0, TimeSpan.Zero);
    AssertEqual("24 July 10:30 am", IndicatorPresentation.FormatResetTime(timestamp));
}

static void SelectsResponsiveLayouts()
{
    AssertEqual(OverlayLayout.Full, IndicatorPresentation.SelectLayout(920));
    AssertEqual(OverlayLayout.Narrow, IndicatorPresentation.SelectLayout(760));
    AssertEqual(OverlayLayout.Compact, IndicatorPresentation.SelectLayout(570));
    AssertEqual(OverlayLayout.Hidden, IndicatorPresentation.SelectLayout(560));
}

static void SizesFullOverlayToContent()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var overlay = new UsageOverlayWindow();
            AssertEqual(false, overlay.Topmost);
            var layout = overlay.Render(
                IndicatorState.Available,
                new UsageSnapshot("account", 53, new DateTimeOffset(2026, 7, 29, 0, 23, 0, TimeSpan.Zero)),
                double.PositiveInfinity);
            AssertEqual(OverlayLayout.Full, layout);

            if (overlay.Width >= 455 || overlay.Width < overlay.MinWidth)
            {
                throw new InvalidOperationException($"Expected content-sized width below 455; received {overlay.Width}.");
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
    {
        throw failure;
    }
}

static void MeasuresLayoutsAgainstAvailableWidth()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var overlay = new UsageOverlayWindow();
            var snapshot = new UsageSnapshot("account", 100, new DateTimeOffset(2026, 7, 29, 0, 23, 0, TimeSpan.Zero));
            AssertEqual(OverlayLayout.Full, overlay.Render(IndicatorState.Available, snapshot, double.PositiveInfinity));
            var fullWidth = overlay.Width;
            var narrow = overlay.Render(IndicatorState.Available, snapshot, fullWidth - 1);
            if (narrow != OverlayLayout.Narrow)
            {
                throw new InvalidOperationException($"Expected Narrow below full width {fullWidth}; received {narrow} at measured width {overlay.Width}.");
            }
            var narrowWidth = overlay.Width;
            AssertEqual(OverlayLayout.Compact, overlay.Render(IndicatorState.Available, snapshot, narrowWidth - 1));
            AssertEqual(true, overlay.Width <= narrowWidth - 1);
            AssertEqual(OverlayLayout.Hidden, overlay.Render(IndicatorState.Available, snapshot, overlay.Width - 1));
        }
        catch (Exception exception)
        {
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
    {
        throw failure;
    }
}

static void CoalescesOverlappingRefreshRequests()
{
    var runner = new CoalescingRefreshRunner();
    var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var executions = 0;

    async Task Refresh(CancellationToken _)
    {
        executions++;
        if (executions == 1)
        {
            firstStarted.SetResult();
            await releaseFirst.Task;
        }
    }

    var first = runner.RunAsync(Refresh);
    firstStarted.Task.GetAwaiter().GetResult();
    var second = runner.RunAsync(Refresh);
    var third = runner.RunAsync(Refresh);
    releaseFirst.SetResult();
    Task.WhenAll(first, second, third).GetAwaiter().GetResult();

    AssertEqual(2, executions);
}

static void CancelsAndReplacesRefreshRequests()
{
    var runner = new CoalescingRefreshRunner();
    var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var replacementRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    async Task First(CancellationToken cancellationToken)
    {
        firstStarted.SetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            firstCancelled.SetResult();
            throw;
        }
    }

    Task Replacement(CancellationToken _)
    {
        replacementRan.SetResult();
        return Task.CompletedTask;
    }

    var first = runner.RunAsync(First);
    firstStarted.Task.GetAwaiter().GetResult();
    var replacement = runner.ReplaceAsync(Replacement);
    Task.WhenAll(first, replacement).GetAwaiter().GetResult();

    AssertEqual(true, firstCancelled.Task.IsCompleted);
    AssertEqual(true, replacementRan.Task.IsCompleted);
}

static void RoutesCommandsToPrimaryInstance()
{
    var userIdentity = $"S-1-5-21-{Guid.NewGuid():N}";
    using var primary = new SingleInstanceService(userIdentity);
    SingleInstanceService? secondary = null;
    var secondaryThread = new Thread(() => secondary = new SingleInstanceService(userIdentity));
    secondaryThread.Start();
    secondaryThread.Join();
    using var secondaryInstance = secondary ?? throw new InvalidOperationException("The secondary instance was not created.");
    AssertEqual(true, primary.IsPrimary);
    AssertEqual(false, secondaryInstance.IsPrimary);

    InstanceCommand? received = null;
    primary.Start((command, _) =>
    {
        received = command;
        return Task.FromResult(command == InstanceCommand.Toggle);
    });

    var result = SingleInstanceService.TrySendAsync(primary.PipeName, InstanceCommand.Toggle).GetAwaiter().GetResult();
    AssertEqual(true, result ?? false);
    AssertEqual(InstanceCommand.Toggle, received ?? throw new InvalidOperationException("The primary did not receive a command."));
}

static void WaitsForRevalidationCommandResponse()
{
    var userIdentity = $"S-1-5-21-{Guid.NewGuid():N}";
    using var primary = new SingleInstanceService(userIdentity);
    primary.Start(async (command, cancellationToken) =>
    {
        AssertEqual(InstanceCommand.RevalidateCli, command);
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        return true;
    });

    var result = SingleInstanceService.TrySendAsync(primary.PipeName, InstanceCommand.RevalidateCli).GetAwaiter().GetResult();
    AssertEqual(true, result ?? false);
}

static void FailsCommandDeliveryWithoutPrimaryPipe()
{
    var result = SingleInstanceService.TrySendAsync($"CodexUsageIndicator-missing-{Guid.NewGuid():N}", InstanceCommand.Toggle)
        .GetAwaiter()
        .GetResult();
    if (result is not null)
    {
        throw new InvalidOperationException("A missing primary pipe must not report a command result.");
    }
}

static void RetainsAttachedWindowAcrossForeignFocus()
{
    static bool Eligible(nint window) => window is 101 or 102;

    var attached = CodexWindowTracker.SelectAttachedWindow(0, 101, Eligible);
    AssertEqual((nint)101, attached);

    attached = CodexWindowTracker.SelectAttachedWindow(attached, 201, Eligible);
    AssertEqual((nint)101, attached);

    attached = CodexWindowTracker.SelectAttachedWindow(attached, 102, Eligible);
    AssertEqual((nint)102, attached);

    attached = CodexWindowTracker.SelectAttachedWindow(attached, 201, _ => false);
    AssertEqual((nint)0, attached);
}

static void IgnoresOverlayLocationEvents()
{
    AssertEqual(true, CodexWindowTracker.ShouldPublishLocationChange(101, 101, CodexUsageIndicator.Interop.NativeMethods.ObjIdWindow));
    AssertEqual(false, CodexWindowTracker.ShouldPublishLocationChange(101, 202, CodexUsageIndicator.Interop.NativeMethods.ObjIdWindow));
    AssertEqual(false, CodexWindowTracker.ShouldPublishLocationChange(101, 101, 1));
}

static void ObservesAttachedWindowHide()
{
    AssertEqual(true, CodexWindowTracker.ObservesEvent(CodexUsageIndicator.Interop.NativeMethods.EventObjectHide));
}

static void IdentifiesCodexDesktopWindow()
{
    AssertEqual(true, CodexWindowTracker.IsCodexDesktopMainWindow("ChatGPT", CodexWindowTracker.CodexPackageFamilyName, "Codex"));
    AssertEqual(true, CodexWindowTracker.IsCodexDesktopMainWindow("ChatGPT", CodexWindowTracker.CodexPackageFamilyName, "ChatGPT"));
    AssertEqual(false, CodexWindowTracker.IsCodexDesktopMainWindow("ChatGPT", "OpenAI.ChatGPT_2p2nqsd0c76g0", "Codex"));
    AssertEqual(false, CodexWindowTracker.IsCodexDesktopMainWindow("ChatGPT", CodexWindowTracker.CodexPackageFamilyName, "Other window"));
    AssertEqual(false, CodexWindowTracker.IsCodexDesktopMainWindow("Codex", CodexWindowTracker.CodexPackageFamilyName, "Codex"));
}

static void SelectsInitialAttachedWindow()
{
    static bool Eligible(nint window) => window is 102 or 103;

    AssertEqual((nint)102, CodexWindowTracker.SelectInitialAttachedWindow(new nint[] { 101, 102, 103 }, Eligible));
    AssertEqual((nint)0, CodexWindowTracker.SelectInitialAttachedWindow(new nint[] { 101, 104 }, Eligible));
}

static void RejectsExpiredWindows()
{
    var threw = false;
    try
    {
        IndicatorPresentation.SelectMostRestrictive("account", new[]
        {
            new RateLimitWindow(80, DateTimeOffset.UtcNow.AddMinutes(-1))
        });
    }
    catch (InvalidOperationException)
    {
        threw = true;
    }

    AssertEqual(true, threw);
}

static void ParsesRateLimitResponse()
{
    var primaryReset = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
    var secondaryReset = DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds();
    using var document = JsonDocument.Parse($$"""
    {
      "rateLimits": {
        "primary": { "usedPercent": 35, "resetsAt": {{primaryReset}} },
        "secondary": { "usedPercent": 82, "resetsAt": {{secondaryReset}} }
      }
    }
    """);

    var windows = AppServerResponses.ExtractRateLimitWindows(document.RootElement);
    var snapshot = IndicatorPresentation.SelectMostRestrictive("account", windows);
    AssertEqual(18, snapshot.RemainingPercent);
    AssertEqual(DateTimeOffset.FromUnixTimeSeconds(secondaryReset), snapshot.ResetsAt);
}

static void AcceptsAbsentOptionalRateLimitWindow()
{
    using var document = JsonDocument.Parse("""
    {
      "rateLimits": {
        "primary": { "usedPercent": 35, "resetsAt": 2000000000 },
        "secondary": null
      }
    }
    """);

    var snapshot = IndicatorPresentation.SelectMostRestrictive("account", AppServerResponses.ExtractRateLimitWindows(document.RootElement));
    AssertEqual(65, snapshot.RemainingPercent);
}

static void RejectsInvalidResponses()
{
    using var malformedRateLimits = JsonDocument.Parse("""
    { "rateLimits": { "primary": { "usedPercent": "eighty" } } }
    """);
    using var apiKeyAccount = JsonDocument.Parse("""
    { "account": { "type": "apiKey" } }
    """);

    AssertThrows<InvalidOperationException>(() => AppServerResponses.ExtractRateLimitWindows(malformedRateLimits.RootElement));
    AssertThrows<InvalidOperationException>(() => AppServerResponses.CreateAccountFingerprint(apiKeyAccount.RootElement));
    AssertThrows<JsonException>(() => JsonDocument.Parse("not json"));
}

static void RejectsOutOfRangePercentages()
{
    foreach (var usedPercent in new[] { -20, -1, 101, 500 })
    {
        using var document = JsonDocument.Parse($$"""
        {
          "rateLimits": {
            "primary": { "usedPercent": {{usedPercent}}, "resetsAt": 2000000000 },
            "secondary": { "usedPercent": 20, "resetsAt": 2000000000 }
          }
        }
        """);

        AssertThrows<InvalidOperationException>(() => AppServerResponses.ExtractRateLimitWindows(document.RootElement));
    }

    AssertThrows<InvalidOperationException>(() => IndicatorPresentation.SelectMostRestrictive("account", new[]
    {
        new RateLimitWindow(500, DateTimeOffset.UtcNow.AddHours(1))
    }));

    var snapshot = IndicatorPresentation.SelectMostRestrictive("account", new[]
    {
        new RateLimitWindow(0, DateTimeOffset.UtcNow.AddHours(1)),
        new RateLimitWindow(100, DateTimeOffset.UtcNow.AddHours(2))
    });
    AssertEqual(0, snapshot.RemainingPercent);
}

static void FailsWhenCliIsMissing()
{
    var reader = new CodexCliAppServerReader(Path.Combine(Path.GetTempPath(), "missing-codex.cmd"), null, TimeSpan.FromMilliseconds(50));
    AssertThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(CancellationToken.None)).GetAwaiter().GetResult();
}

static void AcceptsExeAndCmdCliOverrides()
{
    foreach (var extension in new[] { ".exe", ".cmd" })
    {
        var configuredPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "tools", $"codex{extension}");
        var paths = CodexCliAppServerReader.ResolveCliPaths(configuredPath, null, Path.GetTempPath());
        AssertEqual(configuredPath, paths.Single());
    }
}

static void RejectsInvalidCliOverrides()
{
    foreach (var configuredPath in new[] { "codex.cmd", Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "tools", "codex.bat") })
    {
        AssertThrows<InvalidOperationException>(() => CodexCliAppServerReader.ResolveCliPaths(configuredPath, null, Path.GetTempPath()));
    }
}

static void QuotesCliPath()
{
    var cliPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Program Files", "Codex", "codex.cmd");
    AssertEqual($"\"{cliPath}\" app-server --stdio", CodexCliAppServerReader.CreateAppServerCommand(cliPath));
    AssertEqual($"/d /s /c \"\"{cliPath}\" app-server --stdio\"", CodexCliAppServerReader.CreateAppServerArguments(cliPath));
}

static void ResolvesNativeExeBeforeCmdCandidates()
{
    WithTemporaryDirectory("Codex Candidate Resolution", directory =>
    {
        var pathDirectory = Path.Combine(directory, "path");
        var appDataDirectory = Path.Combine(directory, "appdata");
        Directory.CreateDirectory(pathDirectory);
        Directory.CreateDirectory(Path.Combine(appDataDirectory, "npm"));
        var exePath = Path.Combine(pathDirectory, "codex.exe");
        var appDataCmdPath = Path.Combine(appDataDirectory, "npm", "codex.cmd");
        var pathCmdPath = Path.Combine(pathDirectory, "codex.cmd");
        File.WriteAllText(exePath, "not an executable");
        File.WriteAllText(appDataCmdPath, "@echo off");
        File.WriteAllText(pathCmdPath, "@echo off");

        var paths = CodexCliAppServerReader.ResolveCliPaths(null, pathDirectory, appDataDirectory);
        AssertEqual(3, paths.Count);
        AssertEqual(exePath, paths[0]);
        AssertEqual(appDataCmdPath, paths[1]);
        AssertEqual(pathCmdPath, paths[2]);
    });
}

static void FallsThroughUnlaunchableAutomaticExe()
{
    WithTemporaryDirectory("Codex Automatic Fallback", directory =>
    {
        var exePath = Path.Combine(directory, "codex.exe");
        var shimPath = CreateFakeServerShim(directory);
        File.WriteAllText(exePath, "not an executable");

        var snapshot = new CodexCliAppServerReader(new[] { exePath, shimPath }, false, null, TimeSpan.FromSeconds(5))
            .ReadAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        AssertEqual(18, snapshot.RemainingPercent);
    });
}

static void DoesNotFallThroughAfterStartedRpcFailure()
{
    WithTemporaryDirectory("Codex RPC Failure", directory =>
    {
        var failingShim = CreateFakeServerShim(directory, "--rpc-error");
        var fallbackShim = CreateFakeServerShim(directory, "--mark-started", "fallback.cmd");
        var markerPath = Path.Combine(directory, "fallback-started.txt");

        WithTemporaryEnvironment("CODEX_TEST_SERVER_MARKER", markerPath, () =>
        {
            var reader = new CodexCliAppServerReader(new[] { failingShim, fallbackShim }, false, null, TimeSpan.FromSeconds(5));
            AssertThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(CancellationToken.None)).GetAwaiter().GetResult();
            AssertEqual(false, File.Exists(markerPath));
        });
    });
}

static void DoesNotFallThroughAfterExplicitLauncherFailure()
{
    WithTemporaryDirectory("Codex Explicit Failure", directory =>
    {
        var failingExe = Path.Combine(directory, "codex.exe");
        var fallbackShim = CreateFakeServerShim(directory, "--mark-started", "fallback.cmd");
        var markerPath = Path.Combine(directory, "fallback-started.txt");
        File.WriteAllText(failingExe, "not an executable");

        WithTemporaryEnvironment("CODEX_TEST_SERVER_MARKER", markerPath, () =>
        {
            var reader = new CodexCliAppServerReader(new[] { failingExe, fallbackShim }, true, null, TimeSpan.FromSeconds(5));
            AssertThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(CancellationToken.None)).GetAwaiter().GetResult();
            AssertEqual(false, File.Exists(markerPath));
        });
    });
}

static void CancelsAndCleansUpAppServer()
{
    WithTemporaryDirectory("Codex Cancellation", directory =>
    {
        var shimPath = CreateFakeServerShim(directory, "--hang-after-initialize");
        var pidPath = Path.Combine(directory, "server.pid");

        WithTemporaryEnvironment("CODEX_TEST_SERVER_PID_FILE", pidPath, () =>
        {
            using var cancellation = new CancellationTokenSource();
            var task = new CodexCliAppServerReader(shimPath, null, TimeSpan.FromSeconds(5)).ReadAsync(cancellation.Token);
            WaitForFile(pidPath);
            cancellation.Cancel();
            AssertThrowsAsync<OperationCanceledException>(() => task).GetAwaiter().GetResult();
            AssertEqual(true, HasExited(int.Parse(File.ReadAllText(pidPath))));
        });
    });
}

static void RunsSpacedCmdLauncher()
{
    WithTemporaryDirectory("Codex Usage Indicator Cmd Shim", directory =>
    {
        var shimPath = CreateFakeServerShim(directory);

        var snapshot = new CodexCliAppServerReader(shimPath, null, TimeSpan.FromSeconds(5))
            .ReadAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        AssertEqual(18, snapshot.RemainingPercent);
        AssertEqual(DateTimeOffset.FromUnixTimeSeconds(2_000_000_000), snapshot.ResetsAt);
        AssertEqual(false, string.IsNullOrWhiteSpace(snapshot.AccountFingerprint));
    });
}

static string CreateFakeServerShim(string directory, string? argument = null, string fileName = "codex.cmd")
{
    var shimPath = Path.Combine(directory, fileName);
    File.WriteAllText(shimPath, $"@echo off{Environment.NewLine}{CreateFakeServerInvocation(argument)}{Environment.NewLine}");
    return shimPath;
}

static string CreateFakeServerInvocation(string? argument = null)
{
    var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The test host path is unavailable.");
    var suffix = string.IsNullOrWhiteSpace(argument) ? string.Empty : $" {argument}";
    if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
    {
        return $"\"{processPath}\" \"{typeof(Program).Assembly.Location}\" --fake-app-server{suffix}";
    }

    return $"\"{processPath}\" --fake-app-server{suffix}";
}

static void WithTemporaryDirectory(string name, Action<string> action)
{
    var directory = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        action(directory);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void WithTemporaryEnvironment(string name, string value, Action action)
{
    var previousValue = Environment.GetEnvironmentVariable(name);
    try
    {
        Environment.SetEnvironmentVariable(name, value);
        action();
    }
    finally
    {
        Environment.SetEnvironmentVariable(name, previousValue);
    }
}

static void WaitForFile(string path)
{
    var timeout = Stopwatch.StartNew();
    while (!File.Exists(path))
    {
        if (timeout.Elapsed > TimeSpan.FromSeconds(5))
        {
            throw new InvalidOperationException("The fake app-server did not start.");
        }

        Thread.Sleep(10);
    }
}

static bool HasExited(int processId)
{
    try
    {
        using var process = Process.GetProcessById(processId);
        return process.HasExited;
    }
    catch (ArgumentException)
    {
        return true;
    }
}

static async Task RunFakeAppServerAsync(bool rpcError, bool hangAfterInitialize, bool markStarted)
{
    var pidPath = Environment.GetEnvironmentVariable("CODEX_TEST_SERVER_PID_FILE");
    if (!string.IsNullOrWhiteSpace(pidPath))
    {
        await File.WriteAllTextAsync(pidPath, Environment.ProcessId.ToString());
    }

    var markerPath = Environment.GetEnvironmentVariable("CODEX_TEST_SERVER_MARKER");
    if (markStarted && !string.IsNullOrWhiteSpace(markerPath))
    {
        await File.WriteAllTextAsync(markerPath, "started");
    }

    while (await Console.In.ReadLineAsync() is { } line)
    {
        using var request = JsonDocument.Parse(line);
        var root = request.RootElement;
        var id = root.GetProperty("id").GetInt32();
        var method = root.GetProperty("method").GetString();
        object response = method switch
        {
            "initialize" => new { jsonrpc = "2.0", id, result = new { } },
            "account/read" when rpcError => new { jsonrpc = "2.0", id, error = new { code = -1, message = "synthetic failure" } },
            "account/read" => new { jsonrpc = "2.0", id, result = new { account = new { type = "chatgpt", email = "synthetic@example.invalid", planType = "plus" } } },
            "account/rateLimits/read" => new { jsonrpc = "2.0", id, result = new { rateLimits = new { primary = new { usedPercent = 35, resetsAt = 2_000_000_000 }, secondary = new { usedPercent = 82, resetsAt = 2_000_000_000 } } } },
            _ => new { jsonrpc = "2.0", id, error = new { code = -1, message = "unexpected method" } }
        };

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response));
        await Console.Out.FlushAsync();
        if (hangAfterInitialize && string.Equals(method, "initialize", StringComparison.Ordinal))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
    }
}

static void LoadsValidUserSettings()
{
    WithTemporarySettingsFile(path =>
    {
        File.WriteAllText(path, """{"Enabled":false,"HorizontalOffset":-500,"VerticalOffset":500}""");
        AssertEqual(new UserSettings(false, -500, 500), new UserSettingsStore(path).Load());
    });
}

static void FallsBackForMalformedUserSettings()
{
    WithTemporarySettingsFile(path =>
    {
        var store = new UserSettingsStore(path);
        foreach (var json in new[]
        {
            """{"Enabled":false,"HorizontalOffset":0,"VerticalOffset":""}""",
            """{"HorizontalOffset":12}""",
            """{"Enabled":null,"HorizontalOffset":0,"VerticalOffset":6}""",
            """{"Enabled":false,"HorizontalOffset":null,"VerticalOffset":6}"""
        })
        {
            File.WriteAllText(path, json);
            AssertEqual(UserSettings.Default, store.Load());
        }
    });
}

static void RejectsInvalidUserSettingOffsets()
{
    WithTemporarySettingsFile(path =>
    {
        var store = new UserSettingsStore(path);
        File.WriteAllText(path, """{"Enabled":false,"HorizontalOffset":501,"VerticalOffset":6}""");
        AssertEqual(UserSettings.Default, store.Load());
        AssertThrows<ArgumentOutOfRangeException>(() => store.Save(new UserSettings(false, double.NaN, 6)));
        AssertThrows<ArgumentOutOfRangeException>(() => store.Save(new UserSettings(false, 0, double.PositiveInfinity)));
        AssertThrows<ArgumentOutOfRangeException>(() => store.Save(new UserSettings(false, -501, 6)));
    });
}

static void UsesPerUserSettingsPath()
{
    var expected = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexUsageIndicator",
        "settings.json");
    AssertEqual(expected, UserSettingsStore.GetDefaultPath());
}

static void WithTemporarySettingsFile(Action<string> action)
{
    var directory = Path.Combine(Path.GetTempPath(), $"CodexUsageIndicator-Settings-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        action(Path.Combine(directory, "settings.json"));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TerminatesProcessTree()
{
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        Arguments = "/d /c ping 127.0.0.1 -n 30 > nul",
        UseShellExecute = false,
        CreateNoWindow = true
    }) ?? throw new InvalidOperationException("Could not start process-cleanup test.");

    CodexCliAppServerReader.StopProcessTreeAsync(process).GetAwaiter().GetResult();
    AssertEqual(true, process.HasExited);
}

static void ProductionProviderIsEnabled()
{
    AssertEqual(true, CodexAppServerUsageProvider.IsLiveUsageEnabled);
}

static void StartupInstallationIsEnabled()
{
    AssertEqual(true, StartupTaskManager.IsInstallationEnabled);
}

static void ScopesStartupToInstallingUser()
{
    var configuration = StartupTaskManager.CreateConfiguration("S-1-5-21-1234");

    AssertEqual("S-1-5-21-1234", configuration.UserId);
    AssertEqual("--background", configuration.Arguments);
    AssertEqual(3, configuration.RestartCount);
    AssertEqual("PT1M", configuration.RestartInterval);
    AssertEqual("PT0S", configuration.ExecutionTimeLimit);
    AssertThrows<ArgumentException>(() => StartupTaskManager.CreateConfiguration(string.Empty));
}

static void DoesNotMaskStartupTaskRemovalFailures()
{
    AssertEqual(true, StartupTaskManager.IsMissingTaskError(unchecked((int)0x80070002)));
    AssertEqual(false, StartupTaskManager.IsMissingTaskError(unchecked((int)0x80070005)));
    AssertEqual(false, StartupTaskManager.IsMissingTaskError(unchecked((int)0x8004130F)));
}

static void AssertEqual<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}; received {actual}.");
    }
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
