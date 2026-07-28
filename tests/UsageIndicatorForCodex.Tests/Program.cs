using UsageIndicatorForCodex;
using UsageIndicatorForCodex.Core;
using UsageIndicatorForCodex.Interop;
using UsageIndicatorForCodex.Services;
using UsageIndicatorForCodex.Update;
using UsageIndicatorForCodex.Views;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
    var isolatedHome = Path.Combine(Path.GetTempPath(), $"UsageIndicatorForCodex-Isolated-{Guid.NewGuid():N}");
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
    ("uses the canonical assembly identities", UsesCanonicalAssemblyIdentities),
    ("uses the authoritative product version", UsesAuthoritativeProductVersion),
    ("sends the authoritative app-server client version", SendsAuthoritativeAppServerVersion),
    ("formats reset timestamps in the local timezone without a zone label", FormatsLocalTime),
    ("selects every responsive layout", SelectsResponsiveLayouts),
    ("sizes the full overlay to its rendered content", SizesFullOverlayToContent),
    ("keeps overlay placement deterministic across monitor DPI changes", KeepsOverlayPlacementDeterministicAcrossDpiChanges),
    ("measures layouts against available title-bar space", MeasuresLayoutsAgainstAvailableWidth),
    ("coalesces overlapping refresh requests", CoalescesOverlappingRefreshRequests),
    ("cancels and replaces refresh requests", CancelsAndReplacesRefreshRequests),
    ("parses application commands strictly", ParsesApplicationCommandsStrictly),
    ("keeps update execution outside WPF and parses the private host contract strictly", DefinesStandaloneUpdateBoundary),
    ("selects stable updates and exact release assets", SelectsStableUpdatesAndExactAssets),
    ("checks for updates without downloading assets", ChecksForUpdatesWithoutDownloadingAssets),
    ("downloads only checksum-verified installers", DownloadsOnlyChecksumVerifiedInstallers),
    ("rejects invalid update checksums and repository URLs", RejectsInvalidUpdatesAndRepositoryUrls),
    ("rejects concurrent updates before update work begins", RejectsConcurrentUpdatesBeforeWork),
    ("keeps update checks lock-free and free of process mutation", KeepsUpdateChecksLockFree),
    ("recovers an abandoned per-user update mutex", RecoversAbandonedUpdateMutex),
    ("releases the update mutex after completion", ReleasesUpdateMutexAfterCompletion),
    ("releases the update mutex after update failure", ReleasesUpdateMutexAfterFailure),
    ("releases the update mutex after cancellation", ReleasesUpdateMutexAfterCancellation),
    ("orchestrates a successful silent update through validation and restart", OrchestratesSuccessfulSilentUpdate),
    ("rejects unverified installers before process mutation", RejectsUnverifiedInstallerBeforeProcessMutation),
    ("keeps a previously stopped indicator stopped after update", KeepsPreviouslyStoppedIndicatorStopped),
    ("propagates validated installer restart requirements without restarting", PropagatesValidatedRestartRequirement),
    ("fails closed for installer, validation, and restart failures", RejectsIncompleteUpdateOutcomes),
    ("cleans every prepared update and restores only eligible failed updates", CleansPreparedUpdatesAndRestoresEligibleFailures),
    ("uses the exact private Inno Setup argument contract", UsesExactSilentInstallerArguments),
    ("routes commands to a single primary instance", RoutesCommandsToPrimaryInstance),
    ("coordinates canonical and legacy instance identities", CoordinatesCanonicalAndLegacyInstances),
    ("serves the canonical stop command pipe", ServesCanonicalStopCommandPipe),
    ("fails command delivery cleanly when no primary pipe exists", FailsCommandDeliveryWithoutPrimaryPipe),
    ("retains the attached Codex window across foreign focus", RetainsAttachedWindowAcrossForeignFocus),
    ("retains the attached Codex window while minimized", RetainsMinimizedAttachedWindow),
    ("detaches only when the attached window object is destroyed", DetachesOnlyForAttachedWindowDestruction),
    ("ignores overlay location events while tracking the attached Codex window", IgnoresOverlayLocationEvents),
    ("observes attached Codex windows being hidden", ObservesAttachedWindowHide),
    ("identifies the Codex Desktop window by package identity", IdentifiesCodexDesktopWindow),
    ("rejects Codex companion tool windows", RejectsCodexCompanionToolWindows),
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
    ("formats complete application status with a successful exit code", FormatsCompleteApplicationStatus),
    ("loads valid per-user settings", LoadsValidUserSettings),
    ("inspects status settings without hiding malformed state", InspectsStatusSettingsStrictly),
    ("falls back atomically for malformed settings", FallsBackForMalformedUserSettings),
    ("rejects invalid setting offsets", RejectsInvalidUserSettingOffsets),
    ("uses the ordinary per-user settings path", UsesPerUserSettingsPath),
    ("migrates legacy settings without overwriting canonical settings", MigratesLegacySettingsSafely),
    ("preserves a canonical settings race winner", PreservesCanonicalSettingsRaceWinner),
    ("saves settings without abandoned temporary files", SavesSettingsAtomically),
    ("terminates a provider process tree", TerminatesProcessTree),
    ("enables production live usage for the configured CLI account", ProductionProviderIsEnabled),
    ("enables production startup installation", StartupInstallationIsEnabled),
    ("scopes production startup to the installing user", ScopesStartupToInstallingUser),
    ("recognizes canonical and legacy startup states", RecognizesStartupStates),
    ("recognizes launcher-backed canonical startup states", RecognizesLauncherBackedCanonicalStartupStates),
    ("reports foreign startup task collisions as unrecognized", ReportsStartupCollisionsAsUnrecognized),
    ("does not hide startup scheduler inspection failures", DoesNotHideStartupInspectionFailures),
    ("rejects startup enable when canonical ownership is unrecognized", RejectsEnableWithForeignCanonicalTask),
    ("rejects startup enable when legacy ownership is unrecognized", RejectsEnableWithForeignLegacyTask),
    ("updates only a recognized canonical startup task", UpdatesRecognizedCanonicalStartupTask),
    ("migrates a launcher-backed canonical startup task to the GUI", MigratesLauncherBackedCanonicalStartupTask),
    ("removes a launcher-backed canonical startup task during disable and uninstall", RemovesLauncherBackedCanonicalStartupTask),
    ("preserves foreign same-name launcher collisions without mutation", PreservesForeignLauncherCollisionsWithoutMutation),
    ("preserves foreign startup tasks during disable", PreservesForeignTasksDuringDisable),
    ("removes owned tasks but reports mixed foreign startup collisions", CleansOwnedTasksAndReportsMixedCollisions),
    ("removes recognized legacy startup after registering canonical startup", RemovesRecognizedLegacyStartupAfterRegisteringCanonical),
    ("preserves an unrelated legacy-named startup task", PreservesUnrelatedLegacyNamedStartupTask),
    ("leaves legacy startup when canonical registration fails", LeavesLegacyStartupWhenRegistrationFails),
    ("keeps normal launch fail-open when startup migration cannot resolve a path", KeepsNormalLaunchFailOpenForMigrationFailure),
    ("migrates only recognized legacy startup tasks", MigratesOnlyRecognizedLegacyStartupTasks),
    ("does not migrate legacy startup across a foreign canonical collision", RejectsMigrationAcrossForeignCanonicalTask),
    ("uninstalls canonical and recognized legacy startup tasks", UninstallsCanonicalAndRecognizedLegacyStartupTasks),
    ("preserves an unrecognized legacy startup task during uninstall", PreservesUnrecognizedLegacyStartupTaskDuringUninstall),
    ("treats missing startup tasks as non-fatal", TreatsMissingStartupTasksAsNonFatal),
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

static void UsesCanonicalAssemblyIdentities()
{
    AssertEqual("UsageIndicatorForCodex.Gui", typeof(UsageIndicatorForCodex.App).Assembly.GetName().Name!);
    AssertEqual("UsageIndicatorForCodex.Tests", System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!);
}

static void UsesAuthoritativeProductVersion()
{
    AssertEqual("0.2.1", ProductInfo.Version);
    AssertEqual(
        ProductInfo.Version,
        typeof(UsageIndicatorForCodex.App).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()!
            .InformationalVersion);
}

static void SendsAuthoritativeAppServerVersion()
{
    WithTemporaryDirectory("Codex Client Version", directory =>
    {
        var shimPath = CreateFakeServerShim(directory);
        var initializePath = Path.Combine(directory, "initialize.json");
        WithTemporaryEnvironment("CODEX_TEST_INITIALIZE_REQUEST", initializePath, () =>
        {
            _ = new CodexCliAppServerReader(shimPath, null, TimeSpan.FromSeconds(5))
                .ReadAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        });

        using var request = JsonDocument.Parse(File.ReadAllText(initializePath));
        AssertEqual(
            ProductInfo.Version,
            request.RootElement
                .GetProperty("params")
                .GetProperty("clientInfo")
                .GetProperty("version")
                .GetString()!);
    });
}

static void FormatsLocalTime()
{
    var timestamp = new DateTimeOffset(2026, 7, 24, 2, 30, 0, TimeSpan.Zero);
    var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

    AssertEqual("23 July 7:30 pm", IndicatorPresentation.FormatResetTime(timestamp, pacific));
    AssertEqual("24 July 2:30 am", IndicatorPresentation.FormatResetTime(timestamp, TimeZoneInfo.Utc));
    AssertEqual(
        IndicatorPresentation.FormatResetTime(timestamp, TimeZoneInfo.Local),
        IndicatorPresentation.FormatResetTime(timestamp));
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

static void KeepsOverlayPlacementDeterministicAcrossDpiChanges()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var overlay = new UsageOverlayWindow();
            AssertEqual(ResizeMode.NoResize, overlay.ResizeMode);
            AssertEqual(30d, overlay.Height);
            AssertEqual(30d, overlay.MinHeight);
            AssertEqual(30d, overlay.MaxHeight);

            var snapshot = new UsageSnapshot(
                "account",
                53,
                new DateTimeOffset(2026, 7, 29, 0, 23, 0, TimeSpan.Zero));
            AssertEqual(
                OverlayLayout.Full,
                overlay.Render(IndicatorState.Available, snapshot, double.PositiveInfinity));
            var renderedWidthDip = overlay.Width;

            var landscape = new NativeMethods.Rect
            {
                Left = 0,
                Top = 0,
                Right = 1920,
                Bottom = 1080
            };
            var settings = new UserSettings(true, 0, 6);
            var at96Dpi = UsageOverlayWindow.CalculatePlacement(
                landscape,
                settings,
                renderedWidthDip,
                96);
            var widthAt96Dpi = (int)Math.Ceiling(renderedWidthDip);
            AssertEqual(
                new OverlayPlacement(
                    (landscape.Width - widthAt96Dpi) / 2,
                    6,
                    widthAt96Dpi,
                    30),
                at96Dpi);
            AssertEqual(
                at96Dpi,
                UsageOverlayWindow.CalculatePlacement(landscape, settings, renderedWidthDip, 0));

            var portrait = new NativeMethods.Rect
            {
                Left = -1440,
                Top = -1840,
                Right = 0,
                Bottom = 720
            };
            var portraitSettings = new UserSettings(true, -12, 6);
            var at120Dpi = UsageOverlayWindow.CalculatePlacement(
                portrait,
                portraitSettings,
                renderedWidthDip,
                120);
            var widthAt120Dpi = (int)Math.Ceiling(renderedWidthDip * 1.25);
            AssertEqual(
                new OverlayPlacement(
                    portrait.Left + (portrait.Width - widthAt120Dpi) / 2 - 15,
                    portrait.Top + 8,
                    widthAt120Dpi,
                    38),
                at120Dpi);

            overlay.Width = 999;
            overlay.Height = 999;
            AssertEqual(
                at120Dpi,
                UsageOverlayWindow.CalculatePlacement(
                    portrait,
                    portraitSettings,
                    renderedWidthDip,
                    120));
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

static void ParsesApplicationCommandsStrictly()
{
    const string expectedUsage = """
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
          usage-indicator help

        Keyboard shortcut:
          Ctrl+Alt+U    Turn the indicator display on or off while running

        Running usage-indicator without arguments shows this help.
        """;

    AssertEqual(CommandLineAction.Run, CommandLineOptions.Parse([]).Action);
    AssertEqual(CommandLineAction.Run, CommandLineOptions.Parse(["start"]).Action);
    AssertEqual(CommandLineAction.Run, CommandLineOptions.Parse(["--background"]).Action);
    AssertEqual(CommandLineAction.Stop, CommandLineOptions.Parse(["stop"]).Action);
    AssertEqual(CommandLineAction.Status, CommandLineOptions.Parse(["status"]).Action);
    AssertEqual(CommandLineAction.Version, CommandLineOptions.Parse(["version"]).Action);
    AssertEqual(CommandLineAction.CheckUpdate, CommandLineOptions.Parse(["check-update"]).Action);
    AssertEqual(CommandLineAction.Update, CommandLineOptions.Parse(["update"]).Action);
    AssertEqual(CommandLineAction.EnableStartup, CommandLineOptions.Parse(["enable-startup"]).Action);
    AssertEqual(CommandLineAction.DisableStartup, CommandLineOptions.Parse(["disable-startup"]).Action);
    AssertEqual(CommandLineAction.Help, CommandLineOptions.Parse(["help"]).Action);
    AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--exit"]).Action);
    AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--install"]).Action);
    AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--uninstall"]).Action);
    AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--toggle"]).Action);
    AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--revalidate-cli"]).Action);
    AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--help"]).Action);
    AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["-h"]).Action);
    AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--unknown"]).Action);
    AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--toggle", "--exit"]).Action);
    AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--help", "--help"]).Action);
    AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--HELP"]).Action);
    AssertEqual(0, CommandLineOptions.Parse(["help"]).ExitCode);
    AssertEqual(2, CommandLineOptions.Parse(["--unknown"]).ExitCode);
    AssertEqual(true, CommandLineOptions.Parse(["--unknown"]).Message.Contains(CommandLineOptions.Usage, StringComparison.Ordinal));
    AssertEqual(true, CommandLineOptions.Usage.Contains("usage-indicator update", StringComparison.Ordinal));
    AssertEqual(expectedUsage, CommandLineOptions.Usage);
    AssertEqual(false, CommandLineOptions.Usage.Contains("UsageIndicatorForCodex.exe", StringComparison.Ordinal));
    AssertEqual(false, CommandLineOptions.Usage.Contains("Portable", StringComparison.Ordinal));
    AssertEqual(false, CommandLineOptions.Usage.Contains("--", StringComparison.Ordinal));
}

static void SelectsStableUpdatesAndExactAssets()
{
    var release = ReleaseUpdateClient.ParseLatestStableRelease(CreateReleaseJson("0.3.0"));
    AssertEqual(new Version(0, 3, 0), release.Version);
    AssertEqual(
        "UsageIndicatorForCodex-Setup-v0.3.0.exe",
        ReleaseUpdateClient.SelectExactAsset(
            release,
            "UsageIndicatorForCodex-Setup-v0.3.0.exe").Name);
    AssertThrows<InvalidDataException>(() =>
        ReleaseUpdateClient.SelectExactAsset(
            release,
            "usageindicatorforcodex-setup-v0.3.0.exe"));

    var prerelease = CreateReleaseJson("0.3.0").Replace(
        "\"prerelease\": false",
        "\"prerelease\": true",
        StringComparison.Ordinal);
    AssertThrows<InvalidDataException>(() =>
        ReleaseUpdateClient.ParseLatestStableRelease(prerelease));
    AssertThrows<InvalidDataException>(() =>
        ReleaseUpdateClient.ParseLatestStableRelease(CreateReleaseJson("0.3.0-beta")));
}

static void DefinesStandaloneUpdateBoundary()
{
    AssertEqual(
        false,
        typeof(UpdateOrchestrator).Assembly
            .GetReferencedAssemblies()
            .Any(reference => reference.Name is "PresentationFramework" or "PresentationCore"));
    var parsed = UpdateHostArguments.Parse(
    [
        "--command",
        "update",
        "--install-root",
        @"C:\Program Files\Usage Indicator",
        "--bootstrap-version",
        ProductConstants.BootstrapProtocolVersion.ToString()
    ]);
    AssertEqual(UpdateHostCommand.Update, parsed.Command);
    AssertEqual(
        Path.GetFullPath(@"C:\Program Files\Usage Indicator"),
        parsed.InstallRoot);
    AssertEqual(ProductConstants.BootstrapProtocolVersion, parsed.BootstrapVersion);

    AssertThrows<ArgumentException>(() => UpdateHostArguments.Parse(
    [
        "--command",
        "UPDATE",
        "--install-root",
        @"C:\Program Files\Usage Indicator",
        "--bootstrap-version",
        ProductConstants.BootstrapProtocolVersion.ToString()
    ]));
    AssertThrows<ArgumentException>(() => UpdateHostArguments.Parse(
    [
        "--command",
        "update",
        "--install-root",
        "relative",
        "--bootstrap-version",
        ProductConstants.BootstrapProtocolVersion.ToString()
    ]));
    AssertThrows<ArgumentException>(() => UpdateHostArguments.Parse(
    [
        "--command",
        "update",
        "--install-root",
        @"C:\Program Files\Usage Indicator",
        "--bootstrap-version",
        (ProductConstants.BootstrapProtocolVersion + 1).ToString()
    ]));
}

static void ChecksForUpdatesWithoutDownloadingAssets()
{
    var handler = new RecordingHttpMessageHandler(request =>
    {
        AssertEqual(
            "https://api.github.com/repos/example/project/releases/latest",
            request.RequestUri!.AbsoluteUri);
        return JsonResponse(CreateReleaseJson("0.3.0"));
    });
    using var client = new HttpClient(handler);
    var result = new ReleaseUpdateClient(
            client,
            "https://github.com/example/project",
            ProductInfo.Version)
        .CheckAsync(CancellationToken.None)
        .GetAwaiter()
        .GetResult();

    AssertEqual(true, result.IsAvailable);
    AssertEqual(1, handler.Requests.Count);
    AssertEqual("Update available: 0.3.0 (current 0.2.1).", result.Message);
}

static void DownloadsOnlyChecksumVerifiedInstallers()
{
    var installerName = "UsageIndicatorForCodex-Setup-v0.3.0.exe";
    var installerBytes = new byte[] { 1, 3, 3, 7 };
    var checksum = Convert.ToHexString(SHA256.HashData(installerBytes)).ToLowerInvariant();
    var handler = new RecordingHttpMessageHandler(request =>
    {
        var uri = request.RequestUri!.AbsoluteUri;
        if (uri.EndsWith("/releases/latest", StringComparison.Ordinal))
        {
            return JsonResponse(CreateReleaseJson("0.3.0"));
        }

        if (uri.EndsWith($"/{installerName}", StringComparison.Ordinal))
        {
            return ByteResponse(installerBytes);
        }

        if (uri.EndsWith($"/{installerName}.sha256", StringComparison.Ordinal))
        {
            return ByteResponse(Encoding.UTF8.GetBytes($"{checksum}  {installerName}\n"));
        }

        throw new InvalidOperationException($"Unexpected request: {uri}");
    });
    using var httpClient = new HttpClient(handler);
    WithTemporaryDirectory("Usage Indicator Update", directory =>
    {
        var releaseClient = new ReleaseUpdateClient(
                httpClient,
                "https://github.com/example/project",
                ProductInfo.Version);
        var check = releaseClient
            .CheckAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var verificationStarted = false;
        var prepared = releaseClient
            .PrepareAsync(
                check.LatestRelease,
                directory,
                () => verificationStarted = true,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        AssertEqual(true, verificationStarted);
        AssertEqual(installerName, Path.GetFileName(prepared.InstallerPath));
        AssertEqual(
            Path.GetFullPath(Path.GetDirectoryName(prepared.InstallerPath)!),
            prepared.WorkingDirectory);
        AssertEqual(true, installerBytes.SequenceEqual(File.ReadAllBytes(prepared.InstallerPath)));
    });
}

static void RejectsInvalidUpdatesAndRepositoryUrls()
{
    AssertEqual(
        "https://api.github.com/repos/example/project/releases/latest",
        ReleaseUpdateClient.CreateLatestReleaseApiUri(
            "https://github.com/example/project.git").AbsoluteUri);
    AssertThrows<InvalidOperationException>(() =>
        ReleaseUpdateClient.CreateLatestReleaseApiUri("https://example.invalid/owner/project"));
    AssertThrows<InvalidOperationException>(() =>
        ReleaseUpdateClient.CreateLatestReleaseApiUri("https://github.com/owner/project/extra"));

    var loopbackReleaseJson = CreateReleaseJson("0.3.0").Replace(
        "https://github.com/example/project",
        "http://127.0.0.1:43127",
        StringComparison.Ordinal);
    AssertThrows<InvalidDataException>(() =>
        ReleaseUpdateClient.ParseLatestStableRelease(loopbackReleaseJson));
    var loopbackRelease = ReleaseUpdateClient.ParseLatestStableRelease(
        loopbackReleaseJson,
        allowLoopbackHttp: true);
    AssertEqual(
        "http://127.0.0.1:43127/releases/download/v0.3.0/UsageIndicatorForCodex-Setup-v0.3.0.exe",
        loopbackRelease.Assets["UsageIndicatorForCodex-Setup-v0.3.0.exe"].DownloadUri.AbsoluteUri);
    using var loopbackHttpClient = new HttpClient(new RecordingHttpMessageHandler(
        _ => JsonResponse(loopbackReleaseJson)));
    _ = new ReleaseUpdateClient(
        loopbackHttpClient,
        new Uri("http://127.0.0.1:43127/releases/latest"),
        ProductInfo.Version,
        allowLoopbackHttp: true);
    AssertThrows<InvalidOperationException>(() =>
        _ = new ReleaseUpdateClient(
            loopbackHttpClient,
            new Uri("http://example.invalid/releases/latest"),
            ProductInfo.Version,
            allowLoopbackHttp: true));

    var bytes = Encoding.UTF8.GetBytes("verified content");
    var hash = SHA256.HashData(bytes);
    AssertEqual(true, ReleaseUpdateClient.ChecksumMatches(bytes, hash));
    hash[0] ^= 0xff;
    AssertEqual(false, ReleaseUpdateClient.ChecksumMatches(bytes, hash));
    AssertThrows<InvalidDataException>(() =>
        ReleaseUpdateClient.ParseChecksum(
            $"{new string('0', 64)}  Wrong.exe",
            "UsageIndicatorForCodex-Setup-v0.3.0.exe"));
    AssertThrows<InvalidDataException>(() =>
        ReleaseUpdateClient.ParseChecksum(
            $"{new string('0', 64)}  UsageIndicatorForCodex-Setup-v0.3.0.exe\n"
            + $"{new string('0', 64)}  extra.exe",
            "UsageIndicatorForCodex-Setup-v0.3.0.exe"));
}

static void RejectsConcurrentUpdatesBeforeWork()
{
    var userIdentity = $"contention-{Guid.NewGuid():N}";
    var mutexName = $"Local\\UsageIndicatorForCodex-Update-{userIdentity}";
    WithMutexOwnedOnAnotherThread(mutexName, () =>
    {
        using var blocked = new UpdateMutexLease(userIdentity);
        AssertEqual(mutexName, blocked.MutexName);
        AssertEqual(false, blocked.IsAcquired);
    });

    var scenario = new RecordingUpdateScenario
    {
        MutexAcquired = false
    };
    var result = scenario.CreateOrchestrator()
        .UpdateAsync(CancellationToken.None)
        .GetAwaiter()
        .GetResult();

    AssertEqual(1, result.ExitCode);
    AssertEqual(
        "lock,err:An update is already in progress.,release",
        string.Join(',', scenario.Operations));
    AssertEqual(true, scenario.MutexDisposed);
}

static void KeepsUpdateChecksLockFree()
{
    var available = new RecordingUpdateScenario();
    var result = available.CreateOrchestrator()
        .CheckAsync(CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    AssertEqual(0, result.ExitCode);
    AssertEqual(
        "out:Checking for updates...,check,out:Update available: 0.3.0 (current 0.2.0).",
        string.Join(',', available.Operations));
    AssertEqual(false, available.Operations.Contains("lock"));

    var current = new RecordingUpdateScenario
    {
        IsAvailable = false
    };
    var currentResult = current.CreateOrchestrator()
        .UpdateAsync(CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    AssertEqual(0, currentResult.ExitCode);
    AssertEqual(
        "lock,out:Checking for updates...,check,out:Up to date: 0.2.0.,release",
        string.Join(',', current.Operations));
}

static void RecoversAbandonedUpdateMutex()
{
    var userIdentity = $"abandoned-{Guid.NewGuid():N}";
    var mutexName = $"Local\\UsageIndicatorForCodex-Update-{userIdentity}";
    using var acquired = new ManualResetEventSlim(false);
    var abandoningThread = new Thread(() =>
    {
        var mutex = new Mutex(false, mutexName);
        AssertEqual(true, mutex.WaitOne(0));
        acquired.Set();
    });
    abandoningThread.Start();
    AssertEqual(true, acquired.Wait(TimeSpan.FromSeconds(5)));
    AssertEqual(true, abandoningThread.Join(TimeSpan.FromSeconds(5)));

    using var recovered = new UpdateMutexLease(userIdentity);
    AssertEqual(mutexName, recovered.MutexName);
    AssertEqual(true, recovered.IsAcquired);
}

static void ReleasesUpdateMutexAfterCompletion()
{
    var userIdentity = $"cleanup-{Guid.NewGuid():N}";
    using (var first = new UpdateMutexLease(userIdentity))
    {
        AssertEqual(true, first.IsAcquired);
    }

    using var second = new UpdateMutexLease(userIdentity);
    AssertEqual(true, second.IsAcquired);
}

static void ReleasesUpdateMutexAfterFailure()
{
    var scenario = new RecordingUpdateScenario
    {
        CheckFailure = new InvalidDataException("Release metadata mismatch.")
    };
    AssertThrows<InvalidDataException>(() =>
        scenario.CreateOrchestrator()
            .UpdateAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult());
    AssertEqual(true, scenario.MutexDisposed);
    AssertEqual("release", scenario.Operations[^1]);
}

static void ReleasesUpdateMutexAfterCancellation()
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var scenario = new RecordingUpdateScenario();
    AssertThrows<OperationCanceledException>(() =>
        scenario.CreateOrchestrator()
            .UpdateAsync(cancellation.Token)
            .GetAwaiter()
            .GetResult());
    AssertEqual(true, scenario.MutexDisposed);
    AssertEqual("release", scenario.Operations[^1]);
}

static void OrchestratesSuccessfulSilentUpdate()
{
    var scenario = new RecordingUpdateScenario
    {
        WasRunning = true
    };
    var result = scenario.CreateOrchestrator()
        .UpdateAsync(CancellationToken.None)
        .GetAwaiter()
        .GetResult();

    AssertEqual(0, result.ExitCode);
    AssertEqual(
        "lock,"
        + "out:Checking for updates...,"
        + "check,"
        + "out:Update available: 0.3.0 (current 0.2.0).,"
        + "out:Downloading installer...,"
        + "download,"
        + "out:Verifying SHA-256...,"
        + "verified,"
        + "inspect,"
        + "out:Stopping Usage Indicator for Codex...,"
        + "stop,"
        + "out:Installing 0.3.0...,"
        + "install,"
        + "out:Validating installed version...,"
        + "validate,"
        + "out:Restarting Usage Indicator for Codex...,"
        + "start,"
        + "out:Updated successfully: 0.2.0 -> 0.3.0.,"
        + "cleanup,"
        + "release",
        string.Join(',', scenario.Operations));
    AssertEqual(ProductConstants.BootstrapProtocolVersion, scenario.InstallerBootstrapVersion);
    AssertEqual(true, scenario.InstallerLogPath!.Contains("v0.3.0", StringComparison.Ordinal));
}

static void RejectsUnverifiedInstallerBeforeProcessMutation()
{
    var scenario = new RecordingUpdateScenario
    {
        WasRunning = true,
        PrepareFailure = new InvalidDataException("SHA-256 mismatch.")
    };
    AssertThrows<InvalidDataException>(() =>
        scenario.CreateOrchestrator()
            .UpdateAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult());
    foreach (var forbiddenOperation in new[] { "inspect", "stop", "install", "validate", "start" })
    {
        AssertEqual(false, scenario.Operations.Contains(forbiddenOperation));
    }
}

static void KeepsPreviouslyStoppedIndicatorStopped()
{
    var scenario = new RecordingUpdateScenario
    {
        WasRunning = false
    };
    var result = scenario.CreateOrchestrator()
        .UpdateAsync(CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    AssertEqual(0, result.ExitCode);
    AssertEqual(false, scenario.Operations.Contains("stop"));
    AssertEqual(false, scenario.Operations.Contains("start"));
    AssertEqual(true, scenario.Operations.Contains("validate"));
}

static void PropagatesValidatedRestartRequirement()
{
    var scenario = new RecordingUpdateScenario
    {
        WasRunning = true,
        InstallerExitCode = 3010
    };
    var result = scenario.CreateOrchestrator()
        .UpdateAsync(CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    AssertEqual(3010, result.ExitCode);
    AssertEqual(true, scenario.Operations.Contains("validate"));
    AssertEqual(false, scenario.Operations.Contains("start"));
    AssertEqual(
        false,
        scenario.Operations.Any(operation =>
            operation.Contains("Updated successfully:", StringComparison.Ordinal)));
    AssertEqual(
        true,
        scenario.Operations.Any(operation =>
            operation.Contains("Windows must be restarted", StringComparison.Ordinal)));
    AssertEqual(true, scenario.Operations.Contains("cleanup"));
}

static void RejectsIncompleteUpdateOutcomes()
{
    var installerFailure = new RecordingUpdateScenario
    {
        WasRunning = true,
        InstallerExitCode = 5
    };
    var installerException = AssertThrowsAndReturn<UpdateFailureException>(() =>
        installerFailure.CreateOrchestrator()
            .UpdateAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult());
    AssertEqual(true, installerException.Message.Contains("exited with code 5", StringComparison.Ordinal));
    AssertEqual(true, installerException.Message.Contains("Installer log:", StringComparison.Ordinal));
    AssertEqual(false, installerFailure.Operations.Contains("validate"));
    AssertEqual(true, installerFailure.Operations.Contains("start"));
    AssertEqual(true, installerFailure.Operations.Contains("cleanup"));

    var validationFailure = new RecordingUpdateScenario
    {
        WasRunning = true,
        ValidationFailure = new InvalidDataException("Installed host is old.")
    };
    var validationException = AssertThrowsAndReturn<UpdateFailureException>(() =>
        validationFailure.CreateOrchestrator()
            .UpdateAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult());
    AssertEqual(true, validationException.Message.Contains("validation failed", StringComparison.Ordinal));
    AssertEqual(true, validationFailure.Operations.Contains("start"));
    AssertEqual(true, validationFailure.Operations.Contains("cleanup"));
    AssertEqual(
        false,
        validationFailure.Operations.Any(operation =>
            operation.Contains("Updated successfully:", StringComparison.Ordinal)));

    var restartFailure = new RecordingUpdateScenario
    {
        WasRunning = true,
        StartFailure = new InvalidOperationException("No primary instance.")
    };
    var restartException = AssertThrowsAndReturn<UpdateFailureException>(() =>
        restartFailure.CreateOrchestrator()
            .UpdateAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult());
    AssertEqual(true, restartException.Message.Contains("could not be restarted", StringComparison.Ordinal));
    AssertEqual(true, restartFailure.Operations.Contains("validate"));
    AssertEqual(true, restartFailure.Operations.Contains("cleanup"));
    AssertEqual(
        false,
        restartFailure.Operations.Any(operation =>
            operation.Contains("Updated successfully:", StringComparison.Ordinal)));
}

static void CleansPreparedUpdatesAndRestoresEligibleFailures()
{
    var previouslyStoppedFailure = new RecordingUpdateScenario
    {
        WasRunning = false,
        InstallerExitCode = 5
    };
    _ = AssertThrowsAndReturn<UpdateFailureException>(() =>
        previouslyStoppedFailure.CreateOrchestrator()
            .UpdateAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult());
    AssertEqual(false, previouslyStoppedFailure.Operations.Contains("start"));
    AssertEqual(true, previouslyStoppedFailure.Operations.Contains("cleanup"));

    var failedStop = new RecordingUpdateScenario
    {
        WasRunning = true,
        StopFailure = new InvalidOperationException("Stop failed.")
    };
    _ = AssertThrowsAndReturn<InvalidOperationException>(() =>
        failedStop.CreateOrchestrator()
            .UpdateAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult());
    AssertEqual(false, failedStop.Operations.Contains("start"));
    AssertEqual(true, failedStop.Operations.Contains("cleanup"));

    var restorationFailure = new RecordingUpdateScenario
    {
        WasRunning = true,
        InstallerExitCode = 5,
        StartFailure = new InvalidOperationException("Restore failed.")
    };
    var primary = AssertThrowsAndReturn<UpdateFailureException>(() =>
        restorationFailure.CreateOrchestrator()
            .UpdateAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult());
    AssertEqual(true, primary.Message.Contains("exited with code 5", StringComparison.Ordinal));
    AssertEqual(false, primary.Message.Contains("Restore failed.", StringComparison.Ordinal));
    AssertEqual(
        true,
        restorationFailure.Operations.Any(operation =>
            operation.Contains("Restoration also failed", StringComparison.Ordinal)));
    AssertEqual(
        false,
        restorationFailure.Operations.Any(operation =>
            operation.Contains("Updated successfully:", StringComparison.Ordinal)));
    AssertEqual(true, restorationFailure.Operations.Contains("cleanup"));

    var cleanupFailureAfterSuccess = new RecordingUpdateScenario
    {
        CleanupFailure = new IOException("Directory is busy.")
    };
    var success = cleanupFailureAfterSuccess.CreateOrchestrator()
        .UpdateAsync(CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    AssertEqual(0, success.ExitCode);
    AssertEqual(
        true,
        cleanupFailureAfterSuccess.Operations.Any(operation =>
            operation.Contains("could not be deleted", StringComparison.Ordinal)));

    var cleanupFailureAfterPrimaryFailure = new RecordingUpdateScenario
    {
        InstallerExitCode = 5,
        CleanupFailure = new IOException("Directory is busy.")
    };
    var preservedPrimary = AssertThrowsAndReturn<UpdateFailureException>(() =>
        cleanupFailureAfterPrimaryFailure.CreateOrchestrator()
            .UpdateAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult());
    AssertEqual(true, preservedPrimary.Message.Contains("exited with code 5", StringComparison.Ordinal));
    AssertEqual(false, preservedPrimary.Message.Contains("Directory is busy.", StringComparison.Ordinal));
}

static void UsesExactSilentInstallerArguments()
{
    var logPath = Path.GetFullPath(@"C:\Update Logs\installer.log");
    AssertEqual(
        "/VERYSILENT|/SUPPRESSMSGBOXES|/SP-|/NORESTART|/RESTARTEXITCODE=3010"
        + "|/CLOSEAPPLICATIONS|/NOFORCECLOSEAPPLICATIONS|/NORESTARTAPPLICATIONS"
        + $"|/LOG={logPath}|/CLIUPDATE|/BOOTSTRAPVERSION=1",
        string.Join(
            '|',
            InstallerRunner.CreateArguments(
                logPath,
                ProductConstants.BootstrapProtocolVersion)));
    AssertThrows<ArgumentOutOfRangeException>(() =>
        InstallerRunner.CreateArguments(logPath, ProductConstants.BootstrapProtocolVersion + 1));
}

static string CreateReleaseJson(string version) => $$"""
    {
      "tag_name": "v{{version}}",
      "draft": false,
      "prerelease": false,
      "assets": [
        {
          "name": "UsageIndicatorForCodex-Setup-v{{version}}.exe",
          "browser_download_url": "https://github.com/example/project/releases/download/v{{version}}/UsageIndicatorForCodex-Setup-v{{version}}.exe"
        },
        {
          "name": "UsageIndicatorForCodex-Setup-v{{version}}.exe.sha256",
          "browser_download_url": "https://github.com/example/project/releases/download/v{{version}}/UsageIndicatorForCodex-Setup-v{{version}}.exe.sha256"
        }
      ]
    }
    """;

static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json")
};

static HttpResponseMessage ByteResponse(byte[] bytes) => new(HttpStatusCode.OK)
{
    Content = new ByteArrayContent(bytes)
};

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
        return Task.FromResult(command == InstanceCommand.Exit);
    });

    var result = SingleInstanceService.TrySendAsync(primary.PipeName, InstanceCommand.Exit).GetAwaiter().GetResult();
    AssertEqual(true, result ?? false);
    AssertEqual(InstanceCommand.Exit, received ?? throw new InvalidOperationException("The primary did not receive a command."));
}

static void CoordinatesCanonicalAndLegacyInstances()
{
    var userIdentity = $"S-1-5-21-{Guid.NewGuid():N}";
    using var primary = new SingleInstanceService(userIdentity);
    AssertEqual(true, primary.IsPrimary);
    AssertEqual($"Local\\UsageIndicatorForCodex-{userIdentity}", primary.MutexName);
    AssertEqual($"UsageIndicatorForCodex-{userIdentity}", primary.PipeName);
    AssertEqual($"Local\\CodexUsageIndicator-{userIdentity}", primary.LegacyMutexName);
    AssertEqual($"CodexUsageIndicator-{userIdentity}", primary.LegacyPipeName);
    AssertEqual(1, primary.GetPipeNamesForExit().Count);
    AssertEqual(primary.PipeName, primary.GetPipeNamesForExit()[0]);

    var blockedIdentity = $"S-1-5-21-{Guid.NewGuid():N}";
    var canonicalMutexName = $"Local\\UsageIndicatorForCodex-{blockedIdentity}";
    var legacyMutexName = $"Local\\CodexUsageIndicator-{blockedIdentity}";
    WithMutexOwnedOnAnotherThread(legacyMutexName, () =>
    {
        using var blocked = new SingleInstanceService(blockedIdentity);
        AssertEqual(false, blocked.IsPrimary);
    });

    using var releasedCanonicalMutex = new Mutex(false, canonicalMutexName);
    AssertEqual(true, releasedCanonicalMutex.WaitOne(0));
    releasedCanonicalMutex.ReleaseMutex();
}

static void ServesCanonicalStopCommandPipe()
{
    var userIdentity = $"S-1-5-21-{Guid.NewGuid():N}";
    using var primary = new SingleInstanceService(userIdentity);
    var received = new List<InstanceCommand>();
    var responsesSent = new List<InstanceCommand>();
    primary.Start(
        (command, _) =>
        {
            received.Add(command);
            return Task.FromResult(true);
        },
        command => responsesSent.Add(command));

    var orderedResult = SingleInstanceService.TrySendAsync(
            [$"UsageIndicatorForCodex-missing-{Guid.NewGuid():N}", primary.PipeName],
            InstanceCommand.Exit)
        .GetAwaiter()
        .GetResult();
    AssertEqual(true, orderedResult ?? false);
    AssertEqual(1, received.Count);
    AssertEqual(InstanceCommand.Exit, received[0]);
    AssertEqual(1, responsesSent.Count);
    AssertEqual(InstanceCommand.Exit, responsesSent[0]);
}

static void FailsCommandDeliveryWithoutPrimaryPipe()
{
    var result = SingleInstanceService.TrySendAsync($"UsageIndicatorForCodex-missing-{Guid.NewGuid():N}", InstanceCommand.Exit)
        .GetAwaiter()
        .GetResult();
    if (result is not null)
    {
        throw new InvalidOperationException("A missing primary pipe must not report a command result.");
    }
}

static void WithMutexOwnedOnAnotherThread(string mutexName, Action action)
{
    using var acquired = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    Exception? threadFailure = null;
    var owner = new Thread(() =>
    {
        try
        {
            using var mutex = new Mutex(false, mutexName);
            mutex.WaitOne();
            acquired.Set();
            release.Wait();
            mutex.ReleaseMutex();
        }
        catch (Exception exception)
        {
            threadFailure = exception;
            acquired.Set();
        }
    });
    owner.Start();
    acquired.Wait();
    if (threadFailure is not null)
    {
        throw new InvalidOperationException("The mutex owner thread failed.", threadFailure);
    }

    try
    {
        action();
    }
    finally
    {
        release.Set();
        owner.Join();
    }

    if (threadFailure is not null)
    {
        throw new InvalidOperationException("The mutex owner thread failed.", threadFailure);
    }
}

static void RetainsAttachedWindowAcrossForeignFocus()
{
    static bool Eligible(nint window) => window is 101 or 102;

    var attached = CodexWindowTracker.SelectAttachedWindow(0, 101, Eligible, _ => false);
    AssertEqual((nint)101, attached);

    attached = CodexWindowTracker.SelectAttachedWindow(attached, 201, Eligible, _ => false);
    AssertEqual((nint)101, attached);

    attached = CodexWindowTracker.SelectAttachedWindow(attached, 102, Eligible, _ => false);
    AssertEqual((nint)102, attached);

    attached = CodexWindowTracker.SelectAttachedWindow(attached, 201, _ => false, _ => false);
    AssertEqual((nint)0, attached);
}

static void RetainsMinimizedAttachedWindow()
{
    static bool Eligible(nint window) => window == 102;
    static bool Minimized(nint window) => window == 101;

    var attached = CodexWindowTracker.SelectAttachedWindow(101, 201, Eligible, Minimized);
    AssertEqual((nint)101, attached);

    attached = CodexWindowTracker.SelectAttachedWindow(attached, 102, Eligible, Minimized);
    AssertEqual((nint)102, attached);

    attached = CodexWindowTracker.SelectAttachedWindow(101, 201, _ => false, _ => false);
    AssertEqual((nint)0, attached);
}

static void DetachesOnlyForAttachedWindowDestruction()
{
    AssertEqual(true, CodexWindowTracker.ShouldDetachDestroyedWindow(
        101, 101, UsageIndicatorForCodex.Interop.NativeMethods.ObjIdWindow, 0));
    AssertEqual(false, CodexWindowTracker.ShouldDetachDestroyedWindow(
        101, 101, 1, 0));
    AssertEqual(false, CodexWindowTracker.ShouldDetachDestroyedWindow(
        101, 202, UsageIndicatorForCodex.Interop.NativeMethods.ObjIdWindow, 0));
}

static void IgnoresOverlayLocationEvents()
{
    AssertEqual(true, CodexWindowTracker.ShouldPublishLocationChange(101, 101, UsageIndicatorForCodex.Interop.NativeMethods.ObjIdWindow));
    AssertEqual(false, CodexWindowTracker.ShouldPublishLocationChange(101, 202, UsageIndicatorForCodex.Interop.NativeMethods.ObjIdWindow));
    AssertEqual(false, CodexWindowTracker.ShouldPublishLocationChange(101, 101, 1));
}

static void ObservesAttachedWindowHide()
{
    AssertEqual(true, CodexWindowTracker.ObservesEvent(UsageIndicatorForCodex.Interop.NativeMethods.EventObjectHide));
}

static void IdentifiesCodexDesktopWindow()
{
    AssertEqual(true, CodexWindowTracker.IsCodexDesktopMainWindow("ChatGPT", CodexWindowTracker.CodexPackageFamilyName, "Codex", 0));
    AssertEqual(true, CodexWindowTracker.IsCodexDesktopMainWindow("ChatGPT", CodexWindowTracker.CodexPackageFamilyName, "ChatGPT", 0));
    AssertEqual(false, CodexWindowTracker.IsCodexDesktopMainWindow("ChatGPT", "OpenAI.ChatGPT_2p2nqsd0c76g0", "Codex", 0));
    AssertEqual(false, CodexWindowTracker.IsCodexDesktopMainWindow("ChatGPT", CodexWindowTracker.CodexPackageFamilyName, "Other window", 0));
    AssertEqual(false, CodexWindowTracker.IsCodexDesktopMainWindow("Codex", CodexWindowTracker.CodexPackageFamilyName, "Codex", 0));
}

static void RejectsCodexCompanionToolWindows()
{
    AssertEqual(false, CodexWindowTracker.IsCodexDesktopMainWindow(
        "ChatGPT", CodexWindowTracker.CodexPackageFamilyName, "Codex", NativeMethods.WsExToolWindow));
    AssertEqual(false, CodexWindowTracker.IsCodexDesktopMainWindow(
        "ChatGPT", CodexWindowTracker.CodexPackageFamilyName, "ChatGPT", NativeMethods.WsExToolWindow));
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
    WithTemporaryDirectory("Usage Indicator for Codex Cmd Shim", directory =>
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
        var initializeRequestPath = Environment.GetEnvironmentVariable("CODEX_TEST_INITIALIZE_REQUEST");
        if (string.Equals(method, "initialize", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(initializeRequestPath))
        {
            await File.WriteAllTextAsync(initializeRequestPath, line);
        }

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

static void FormatsCompleteApplicationStatus()
{
    var running = new ApplicationStatusSnapshot(
        true,
        false,
        StartupTaskState.Enabled);
    AssertEqual(
        string.Join(
            Environment.NewLine,
            "running: true",
            "indicator-enabled: false",
            "startup: enabled"),
        running.Format());
    AssertEqual(0, running.ExitCode);

    var stopped = new ApplicationStatusSnapshot(
        false,
        true,
        StartupTaskState.Unrecognized);
    AssertEqual(
        string.Join(
            Environment.NewLine,
            "running: false",
            "indicator-enabled: true",
            "startup: unrecognized"),
        stopped.Format());
    AssertEqual(0, stopped.ExitCode);
}

static void InspectsStatusSettingsStrictly()
{
    WithTemporaryDirectory("UsageIndicatorForCodex-Status-Settings", directory =>
    {
        var canonicalPath = Path.Combine(directory, "canonical", "settings.json");
        var legacyPath = Path.Combine(directory, "legacy", "settings.json");
        var store = new UserSettingsStore(canonicalPath, legacyPath);

        AssertEqual(true, store.InspectEnabled());

        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(
            legacyPath,
            """{"Enabled":false,"HorizontalOffset":0,"VerticalOffset":6}""");
        AssertEqual(false, store.InspectEnabled());
        AssertEqual(false, File.Exists(canonicalPath));

        Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!);
        File.WriteAllText(canonicalPath, """{"Enabled":true,"HorizontalOffset":0,"VerticalOffset":6}""");
        AssertEqual(true, store.InspectEnabled());

        File.WriteAllText(canonicalPath, """{"Enabled":"invalid","HorizontalOffset":0,"VerticalOffset":6}""");
        AssertThrows<InvalidDataException>(() => store.InspectEnabled());

        File.Delete(canonicalPath);
        Directory.CreateDirectory(canonicalPath);
        AssertThrows<UnauthorizedAccessException>(() => store.InspectEnabled());
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
    var expectedCanonical = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UsageIndicatorForCodex",
        "settings.json");
    var expectedLegacy = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexUsageIndicator",
        "settings.json");
    AssertEqual(expectedCanonical, UserSettingsStore.GetDefaultPath());
    AssertEqual(expectedLegacy, UserSettingsStore.GetLegacyPath());
}

static void MigratesLegacySettingsSafely()
{
    WithTemporaryDirectory("UsageIndicatorForCodex-Settings-Migration", directory =>
    {
        var canonicalPath = Path.Combine(directory, "UsageIndicatorForCodex", "settings.json");
        var legacyPath = Path.Combine(directory, "CodexUsageIndicator", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, """{"Enabled":false,"HorizontalOffset":12,"VerticalOffset":-4}""");

        var migrated = new UserSettingsStore(canonicalPath, legacyPath).Load();
        AssertEqual(new UserSettings(false, 12, -4), migrated);
        AssertEqual(true, File.Exists(canonicalPath));
        AssertEqual(true, File.Exists(legacyPath));
        AssertEqual(migrated, new UserSettingsStore(canonicalPath).Load());

        File.WriteAllText(canonicalPath, """{"Enabled":true,"HorizontalOffset":21,"VerticalOffset":8}""");
        File.WriteAllText(legacyPath, """{"Enabled":false,"HorizontalOffset":99,"VerticalOffset":99}""");
        AssertEqual(
            new UserSettings(true, 21, 8),
            new UserSettingsStore(canonicalPath, legacyPath).Load());

        File.WriteAllText(canonicalPath, """{"Enabled":false,"HorizontalOffset":"invalid","VerticalOffset":6}""");
        AssertEqual(UserSettings.Default, new UserSettingsStore(canonicalPath, legacyPath).Load());

        File.Delete(canonicalPath);
        File.WriteAllText(legacyPath, """{"Enabled":false,"HorizontalOffset":501,"VerticalOffset":6}""");
        AssertEqual(UserSettings.Default, new UserSettingsStore(canonicalPath, legacyPath).Load());
        AssertEqual(false, File.Exists(canonicalPath));
    });
}

static void PreservesCanonicalSettingsRaceWinner()
{
    WithTemporaryDirectory("UsageIndicatorForCodex-Settings-Race", directory =>
    {
        var canonicalPath = Path.Combine(directory, "UsageIndicatorForCodex", "settings.json");
        var legacyPath = Path.Combine(directory, "CodexUsageIndicator", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, """{"Enabled":false,"HorizontalOffset":12,"VerticalOffset":-4}""");
        var winner = new UserSettings(true, 44, 5);

        var store = new UserSettingsStore(
            canonicalPath,
            legacyPath,
            () => new UserSettingsStore(canonicalPath).Save(winner));

        AssertEqual(winner, store.Load());
        AssertEqual(winner, new UserSettingsStore(canonicalPath).Load());
        AssertEqual(0, Directory.GetFiles(Path.GetDirectoryName(canonicalPath)!, "*.tmp").Length);
    });
}

static void SavesSettingsAtomically()
{
    WithTemporaryDirectory("UsageIndicatorForCodex-Settings-Save", directory =>
    {
        var path = Path.Combine(directory, "UsageIndicatorForCodex", "settings.json");
        var store = new UserSettingsStore(path);
        store.Save(new UserSettings(false, 1, 2));
        store.Save(new UserSettings(true, 3, 4));

        AssertEqual(new UserSettings(true, 3, 4), store.Load());
        AssertEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
    });
}

static void WithTemporarySettingsFile(Action<string> action)
{
    var directory = Path.Combine(Path.GetTempPath(), $"UsageIndicatorForCodex-Settings-{Guid.NewGuid():N}");
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

static void RecognizesStartupStates()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";

    var missing = new RecordingStartupTaskScheduler();
    AssertEqual(
        StartupTaskState.Disabled,
        StartupTaskManager.Inspect(executablePath, missing));

    var canonicalEnabled = new RecordingStartupTaskScheduler();
    canonicalEnabled.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        executablePath,
        "--background",
        "Shows the Usage Indicator for Codex companion.",
        true);
    AssertEqual(
        StartupTaskState.Enabled,
        StartupTaskManager.Inspect(executablePath, canonicalEnabled));

    var canonicalDisabled = new RecordingStartupTaskScheduler();
    canonicalDisabled.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        $""" "{executablePath}" """,
        "  --background  ",
        "Shows the Usage Indicator for Codex companion.",
        false);
    AssertEqual(
        StartupTaskState.Disabled,
        StartupTaskManager.Inspect(executablePath, canonicalDisabled));

    var legacyEnabled = new RecordingStartupTaskScheduler();
    legacyEnabled.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"C:\Legacy\CodexUsageIndicator.exe",
        "--background",
        "Shows the Codex usage indicator companion.",
        true);
    AssertEqual(
        StartupTaskState.Enabled,
        StartupTaskManager.Inspect(executablePath, legacyEnabled));
}

static void RecognizesLauncherBackedCanonicalStartupStates()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";
    var enabled = new RecordingStartupTaskScheduler();
    enabled.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        @"C:\Apps\UsageIndicatorForCodex.exe",
        "--background",
        "Shows the Usage Indicator for Codex companion.",
        true);
    AssertEqual(
        StartupTaskState.Enabled,
        StartupTaskManager.Inspect(executablePath, enabled));

    var disabled = new RecordingStartupTaskScheduler();
    disabled.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        @"  ""C:\Apps\.\UsageIndicatorForCodex.exe""  ",
        "  --background  ",
        "Shows the Usage Indicator for Codex companion.",
        false);
    AssertEqual(
        StartupTaskState.Disabled,
        StartupTaskManager.Inspect(executablePath, disabled));
}

static void ReportsStartupCollisionsAsUnrecognized()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";
    var collision = new RecordingStartupTaskScheduler();
    collision.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        executablePath,
        "--background",
        "Shows the Usage Indicator for Codex companion.",
        true);
    collision.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"C:\Tools\Foreign.exe",
        "--background",
        "Foreign task.",
        true);

    AssertEqual(
        StartupTaskState.Unrecognized,
        StartupTaskManager.Inspect(executablePath, collision));

    collision.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        @"C:\Tools\Foreign.exe",
        "--background",
        "Foreign task.",
        true);
    collision.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"C:\Legacy\CodexUsageIndicator.exe",
        "--background",
        "Shows the Codex usage indicator companion.",
        true);

    AssertEqual(
        StartupTaskState.Unrecognized,
        StartupTaskManager.Inspect(executablePath, collision));
}

static void DoesNotHideStartupInspectionFailures()
{
    var scheduler = new RecordingStartupTaskScheduler
    {
        GetFailure = new COMException(
            "Task metadata is unavailable.",
            unchecked((int)0x80070005))
    };

    AssertThrows<COMException>(() =>
        StartupTaskManager.Inspect(
            @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
            scheduler));
}

static void RejectsEnableWithForeignCanonicalTask()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";
    var scheduler = new RecordingStartupTaskScheduler();
    var foreign = new StartupTaskInfo(
        @"C:\Tools\Foreign.exe",
        "--background",
        "Foreign task.",
        true);
    scheduler.Tasks[StartupTaskManager.TaskName] = foreign;

    var exception = AssertThrowsAndReturn<StartupTaskOwnershipException>(() =>
        StartupTaskManager.Install(executablePath, scheduler));

    AssertEqual(StartupTaskManager.OwnershipCollisionExitCode, exception.ExitCode);
    AssertEqual(
        "Startup was not enabled because unrecognized scheduled task names must be inspected manually: UsageIndicatorForCodex.",
        exception.Message);
    AssertEqual(foreign, scheduler.Tasks[StartupTaskManager.TaskName]);
    AssertEqual(2, scheduler.Operations.Count);
    AssertEqual($"Get:{StartupTaskManager.TaskName}", scheduler.Operations[0]);
    AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", scheduler.Operations[1]);
}

static void RejectsEnableWithForeignLegacyTask()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";
    var scheduler = new RecordingStartupTaskScheduler();
    var recognizedCanonical = new StartupTaskInfo(
        executablePath,
        "--background",
        "Shows the Usage Indicator for Codex companion.",
        true);
    var foreignLegacy = new StartupTaskInfo(
        @"C:\Tools\Foreign.exe",
        "--background",
        "Foreign task.",
        true);
    scheduler.Tasks[StartupTaskManager.TaskName] = recognizedCanonical;
    scheduler.Tasks[StartupTaskManager.LegacyTaskName] = foreignLegacy;

    var exception = AssertThrowsAndReturn<StartupTaskOwnershipException>(() =>
        StartupTaskManager.Install(executablePath, scheduler));

    AssertEqual(StartupTaskManager.OwnershipCollisionExitCode, exception.ExitCode);
    AssertEqual(recognizedCanonical, scheduler.Tasks[StartupTaskManager.TaskName]);
    AssertEqual(foreignLegacy, scheduler.Tasks[StartupTaskManager.LegacyTaskName]);
    AssertEqual(2, scheduler.Operations.Count);
}

static void UpdatesRecognizedCanonicalStartupTask()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";
    var scheduler = new RecordingStartupTaskScheduler();
    scheduler.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        executablePath,
        "--background",
        "Shows the Usage Indicator for Codex companion.",
        false);

    StartupTaskManager.Install(executablePath, scheduler);

    AssertEqual(
        $"Register:{StartupTaskManager.TaskName}:{StartupTaskRegistrationMode.Update}",
        scheduler.Operations[2]);
    AssertEqual(true, scheduler.Tasks[StartupTaskManager.TaskName].IsEnabled);
}

static void MigratesLauncherBackedCanonicalStartupTask()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";
    var scheduler = new RecordingStartupTaskScheduler();
    scheduler.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        @"C:\Apps\UsageIndicatorForCodex.exe",
        "--background",
        "Shows the Usage Indicator for Codex companion.",
        false);

    StartupTaskManager.Install(executablePath, scheduler);

    AssertEqual(3, scheduler.Operations.Count);
    AssertEqual(
        $"Register:{StartupTaskManager.TaskName}:{StartupTaskRegistrationMode.Update}",
        scheduler.Operations[2]);
    AssertEqual(executablePath, scheduler.RegisteredExecutablePath!);
    AssertEqual(true, scheduler.Tasks[StartupTaskManager.TaskName].IsEnabled);
}

static void RemovesLauncherBackedCanonicalStartupTask()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";
    var scheduler = new RecordingStartupTaskScheduler();
    scheduler.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        @"C:\Apps\UsageIndicatorForCodex.exe",
        "--background",
        "Shows the Usage Indicator for Codex companion.");

    StartupTaskManager.Uninstall(executablePath, scheduler);

    AssertEqual(false, scheduler.Tasks.ContainsKey(StartupTaskManager.TaskName));
    AssertEqual(3, scheduler.Operations.Count);
    AssertEqual($"Delete:{StartupTaskManager.TaskName}", scheduler.Operations[2]);
}

static void PreservesForeignLauncherCollisionsWithoutMutation()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";
    foreach (var foreignPath in new[]
    {
        @"C:\OtherApps\UsageIndicatorForCodex.exe",
        @"C:\Apps\Nested\UsageIndicatorForCodex.exe",
        @"\\server\share\UsageIndicatorForCodex.exe"
    })
    {
        var scheduler = new RecordingStartupTaskScheduler();
        var foreign = new StartupTaskInfo(
            foreignPath,
            "--background",
            "Foreign task.");
        scheduler.Tasks[StartupTaskManager.TaskName] = foreign;

        AssertEqual(
            StartupTaskState.Unrecognized,
            StartupTaskManager.Inspect(executablePath, scheduler));
        AssertThrows<StartupTaskOwnershipException>(() =>
            StartupTaskManager.Install(executablePath, scheduler));
        AssertThrows<StartupTaskOwnershipException>(() =>
            StartupTaskManager.Uninstall(executablePath, scheduler));

        AssertEqual(foreign, scheduler.Tasks[StartupTaskManager.TaskName]);
        AssertEqual(
            false,
            scheduler.Operations.Any(operation =>
                operation.StartsWith("Register:", StringComparison.Ordinal)
                || operation.StartsWith("Delete:", StringComparison.Ordinal)));
    }
}

static void PreservesForeignTasksDuringDisable()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";
    foreach (var taskName in new[]
    {
        StartupTaskManager.TaskName,
        StartupTaskManager.LegacyTaskName
    })
    {
        var scheduler = new RecordingStartupTaskScheduler();
        var foreign = new StartupTaskInfo(
            @"C:\Tools\Foreign.exe",
            "--background",
            "Foreign task.",
            true);
        scheduler.Tasks[taskName] = foreign;

        var exception = AssertThrowsAndReturn<StartupTaskOwnershipException>(() =>
            StartupTaskManager.Uninstall(executablePath, scheduler));

        AssertEqual(StartupTaskManager.OwnershipCollisionExitCode, exception.ExitCode);
        AssertEqual(foreign, scheduler.Tasks[taskName]);
        AssertEqual(2, scheduler.Operations.Count);
        AssertEqual($"Get:{StartupTaskManager.TaskName}", scheduler.Operations[0]);
        AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", scheduler.Operations[1]);
    }
}

static void CleansOwnedTasksAndReportsMixedCollisions()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";
    var scheduler = new RecordingStartupTaskScheduler();
    scheduler.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        executablePath,
        "--background",
        "Shows the Usage Indicator for Codex companion.");
    var foreignLegacy = new StartupTaskInfo(
        @"C:\Tools\Foreign.exe",
        "--background",
        "Foreign task.");
    scheduler.Tasks[StartupTaskManager.LegacyTaskName] = foreignLegacy;

    var exception = AssertThrowsAndReturn<StartupTaskOwnershipException>(() =>
        StartupTaskManager.Uninstall(executablePath, scheduler));

    AssertEqual(
        "Startup cleanup preserved unrecognized scheduled task names that must be inspected manually: CodexUsageIndicator.",
        exception.Message);
    AssertEqual(false, scheduler.Tasks.ContainsKey(StartupTaskManager.TaskName));
    AssertEqual(foreignLegacy, scheduler.Tasks[StartupTaskManager.LegacyTaskName]);
    AssertEqual(true, scheduler.Operations.Contains($"Delete:{StartupTaskManager.TaskName}"));
    AssertEqual(false, scheduler.Operations.Contains($"Delete:{StartupTaskManager.LegacyTaskName}"));
}

static void RemovesRecognizedLegacyStartupAfterRegisteringCanonical()
{
    var scheduler = new RecordingStartupTaskScheduler();
    scheduler.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"  ""C:\Legacy\App\..\App\CODEXUSAGEINDICATOR.EXE""  ",
        "--background",
        "Shows the Codex usage indicator companion.");

    StartupTaskManager.Install(@"C:\Apps\UsageIndicatorForCodex.Gui.exe", scheduler);

    AssertEqual(4, scheduler.Operations.Count);
    AssertEqual($"Get:{StartupTaskManager.TaskName}", scheduler.Operations[0]);
    AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", scheduler.Operations[1]);
    AssertEqual(
        $"Register:{StartupTaskManager.TaskName}:{StartupTaskRegistrationMode.Create}",
        scheduler.Operations[2]);
    AssertEqual($"Delete:{StartupTaskManager.LegacyTaskName}", scheduler.Operations[3]);
    AssertEqual(true, scheduler.Tasks.ContainsKey(StartupTaskManager.TaskName));
    AssertEqual(false, scheduler.Tasks.ContainsKey(StartupTaskManager.LegacyTaskName));
    AssertEqual(@"C:\Apps\UsageIndicatorForCodex.Gui.exe", scheduler.RegisteredExecutablePath!);
    AssertEqual("--background", scheduler.RegisteredConfiguration!.Arguments);
}

static void PreservesUnrelatedLegacyNamedStartupTask()
{
    var scheduler = new RecordingStartupTaskScheduler();
    scheduler.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"C:\Tools\UnrelatedMaintenance.exe",
        "--background",
        "Unrelated maintenance task.");

    AssertThrows<StartupTaskOwnershipException>(() =>
        StartupTaskManager.Install(@"C:\Apps\UsageIndicatorForCodex.Gui.exe", scheduler));

    AssertEqual(2, scheduler.Operations.Count);
    AssertEqual($"Get:{StartupTaskManager.TaskName}", scheduler.Operations[0]);
    AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", scheduler.Operations[1]);
    AssertEqual(false, scheduler.Tasks.ContainsKey(StartupTaskManager.TaskName));
    AssertEqual(true, scheduler.Tasks.ContainsKey(StartupTaskManager.LegacyTaskName));
}

static void LeavesLegacyStartupWhenRegistrationFails()
{
    var scheduler = new RecordingStartupTaskScheduler
    {
        RegisterFailure = new COMException("Registration failed.", unchecked((int)0x80070005))
    };
    scheduler.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"C:\Legacy\CodexUsageIndicator.exe",
        "--background",
        "Shows the Codex usage indicator companion.");

    AssertThrows<COMException>(() =>
        StartupTaskManager.Install(@"C:\Apps\UsageIndicatorForCodex.Gui.exe", scheduler));
    AssertEqual(3, scheduler.Operations.Count);
    AssertEqual($"Get:{StartupTaskManager.TaskName}", scheduler.Operations[0]);
    AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", scheduler.Operations[1]);
    AssertEqual(
        $"Register:{StartupTaskManager.TaskName}:{StartupTaskRegistrationMode.Create}",
        scheduler.Operations[2]);
    AssertEqual(true, scheduler.Tasks.ContainsKey(StartupTaskManager.LegacyTaskName));
}

static void KeepsNormalLaunchFailOpenForMigrationFailure()
{
    var migrationCalled = false;
    AssertEqual(false, App.TryMigrateLegacyStartup(
        () => throw new InvalidOperationException("Path unavailable."),
        _ =>
        {
            migrationCalled = true;
            return true;
        }));
    AssertEqual(false, migrationCalled);

    AssertEqual(false, App.TryMigrateLegacyStartup(
        () => @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        _ => throw new COMException("Scheduler unavailable.")));
    AssertEqual(true, App.TryMigrateLegacyStartup(
        () => @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        _ => true));
}

static void MigratesOnlyRecognizedLegacyStartupTasks()
{
    var noLegacy = new RecordingStartupTaskScheduler();
    AssertEqual(false, StartupTaskManager.MigrateLegacyTask(
        @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        noLegacy));
    AssertEqual(2, noLegacy.Operations.Count);
    AssertEqual($"Get:{StartupTaskManager.TaskName}", noLegacy.Operations[0]);
    AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", noLegacy.Operations[1]);

    var unrelated = new RecordingStartupTaskScheduler();
    unrelated.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"C:\Tools\Other.exe",
        "--background",
        "Unrelated task.");
    AssertEqual(false, StartupTaskManager.MigrateLegacyTask(
        @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        unrelated));
    AssertEqual(2, unrelated.Operations.Count);
    AssertEqual($"Get:{StartupTaskManager.TaskName}", unrelated.Operations[0]);
    AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", unrelated.Operations[1]);
    AssertEqual(true, unrelated.Tasks.ContainsKey(StartupTaskManager.LegacyTaskName));

    var migration = new RecordingStartupTaskScheduler();
    migration.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"C:\Legacy\CodexUsageIndicator.exe",
        "--background",
        "Shows the Codex usage indicator companion.");
    AssertEqual(true, StartupTaskManager.MigrateLegacyTask(
        @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        migration));
    AssertEqual($"Get:{StartupTaskManager.TaskName}", migration.Operations[0]);
    AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", migration.Operations[1]);
    AssertEqual(
        $"Register:{StartupTaskManager.TaskName}:{StartupTaskRegistrationMode.Create}",
        migration.Operations[2]);
    AssertEqual($"Delete:{StartupTaskManager.LegacyTaskName}", migration.Operations[3]);
}

static void RejectsMigrationAcrossForeignCanonicalTask()
{
    const string executablePath = @"C:\Apps\UsageIndicatorForCodex.Gui.exe";
    var scheduler = new RecordingStartupTaskScheduler();
    var foreignCanonical = new StartupTaskInfo(
        @"C:\Tools\Foreign.exe",
        "--background",
        "Foreign task.");
    var recognizedLegacy = new StartupTaskInfo(
        @"C:\Legacy\CodexUsageIndicator.exe",
        "--background",
        "Shows the Codex usage indicator companion.");
    scheduler.Tasks[StartupTaskManager.TaskName] = foreignCanonical;
    scheduler.Tasks[StartupTaskManager.LegacyTaskName] = recognizedLegacy;

    AssertEqual(false, StartupTaskManager.MigrateLegacyTask(executablePath, scheduler));
    AssertEqual(2, scheduler.Operations.Count);
    AssertEqual(foreignCanonical, scheduler.Tasks[StartupTaskManager.TaskName]);
    AssertEqual(recognizedLegacy, scheduler.Tasks[StartupTaskManager.LegacyTaskName]);
}

static void UninstallsCanonicalAndRecognizedLegacyStartupTasks()
{
    var uninstall = new RecordingStartupTaskScheduler();
    uninstall.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        "--background",
        "Shows the Usage Indicator for Codex companion.");
    uninstall.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"C:\Legacy\CodexUsageIndicator.exe",
        "--background",
        "Shows the Codex usage indicator companion.");

    StartupTaskManager.Uninstall(
        @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        uninstall);
    AssertEqual($"Get:{StartupTaskManager.TaskName}", uninstall.Operations[0]);
    AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", uninstall.Operations[1]);
    AssertEqual($"Delete:{StartupTaskManager.TaskName}", uninstall.Operations[2]);
    AssertEqual($"Delete:{StartupTaskManager.LegacyTaskName}", uninstall.Operations[3]);
    AssertEqual(false, uninstall.Tasks.ContainsKey(StartupTaskManager.TaskName));
    AssertEqual(false, uninstall.Tasks.ContainsKey(StartupTaskManager.LegacyTaskName));
}

static void PreservesUnrecognizedLegacyStartupTaskDuringUninstall()
{
    var uninstall = new RecordingStartupTaskScheduler();
    uninstall.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        "--background",
        "Shows the Usage Indicator for Codex companion.");
    uninstall.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"C:\Tools\UnrelatedMaintenance.exe",
        "--background",
        "Unrelated maintenance task.");

    AssertThrows<StartupTaskOwnershipException>(() =>
        StartupTaskManager.Uninstall(
            @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
            uninstall));

    AssertEqual(3, uninstall.Operations.Count);
    AssertEqual($"Get:{StartupTaskManager.TaskName}", uninstall.Operations[0]);
    AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", uninstall.Operations[1]);
    AssertEqual($"Delete:{StartupTaskManager.TaskName}", uninstall.Operations[2]);
    AssertEqual(false, uninstall.Tasks.ContainsKey(StartupTaskManager.TaskName));
    AssertEqual(true, uninstall.Tasks.ContainsKey(StartupTaskManager.LegacyTaskName));

    var unreadable = new RecordingStartupTaskScheduler
    {
        GetFailure = new COMException("Task metadata is unavailable.", unchecked((int)0x80070005))
    };
    unreadable.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"C:\Legacy\CodexUsageIndicator.exe",
        "--background",
        "Shows the Codex usage indicator companion.");

    AssertThrows<COMException>(() =>
        StartupTaskManager.Uninstall(
            @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
            unreadable));

    AssertEqual(1, unreadable.Operations.Count);
    AssertEqual($"Get:{StartupTaskManager.TaskName}", unreadable.Operations[0]);
    AssertEqual(true, unreadable.Tasks.ContainsKey(StartupTaskManager.LegacyTaskName));
}

static void TreatsMissingStartupTasksAsNonFatal()
{
    var missing = new RecordingStartupTaskScheduler();
    StartupTaskManager.Uninstall(
        @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        missing);
    AssertEqual(2, missing.Operations.Count);
    AssertEqual($"Get:{StartupTaskManager.TaskName}", missing.Operations[0]);
    AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", missing.Operations[1]);

    AssertEqual(false, StartupTaskManager.MigrateLegacyTask(
        @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        missing));
}

static void DoesNotMaskStartupTaskRemovalFailures()
{
    var firstDeleteFailure = new RecordingStartupTaskScheduler();
    firstDeleteFailure.Tasks[StartupTaskManager.TaskName] = new StartupTaskInfo(
        @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        "--background",
        "Shows the Usage Indicator for Codex companion.");
    firstDeleteFailure.Tasks[StartupTaskManager.LegacyTaskName] = new StartupTaskInfo(
        @"C:\Legacy\CodexUsageIndicator.exe",
        "--background",
        "Shows the Codex usage indicator companion.");
    firstDeleteFailure.DeleteFailures[StartupTaskManager.TaskName] =
        new COMException("Access denied.", unchecked((int)0x80070005));
    AssertThrows<COMException>(() => StartupTaskManager.Uninstall(
        @"C:\Apps\UsageIndicatorForCodex.Gui.exe",
        firstDeleteFailure));
    AssertEqual(4, firstDeleteFailure.Operations.Count);
    AssertEqual($"Get:{StartupTaskManager.LegacyTaskName}", firstDeleteFailure.Operations[1]);
    AssertEqual($"Delete:{StartupTaskManager.LegacyTaskName}", firstDeleteFailure.Operations[3]);
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

static TException AssertThrowsAndReturn<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
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

internal sealed class RecordingStartupTaskScheduler : IStartupTaskScheduler
{
    internal Dictionary<string, StartupTaskInfo> Tasks { get; } = new(StringComparer.Ordinal);
    internal List<string> Operations { get; } = [];
    internal Dictionary<string, Exception> DeleteFailures { get; } = new(StringComparer.Ordinal);
    internal Exception? RegisterFailure { get; init; }
    internal Exception? GetFailure { get; init; }
    internal string? RegisteredExecutablePath { get; private set; }
    internal StartupTaskConfiguration? RegisteredConfiguration { get; private set; }

    public StartupTaskInfo? Get(string taskName)
    {
        Operations.Add($"Get:{taskName}");
        if (GetFailure is not null)
        {
            throw GetFailure;
        }

        return Tasks.GetValueOrDefault(taskName);
    }

    public void Register(
        string taskName,
        string executablePath,
        StartupTaskConfiguration configuration,
        StartupTaskRegistrationMode mode)
    {
        Operations.Add($"Register:{taskName}:{mode}");
        if (RegisterFailure is not null)
        {
            throw RegisterFailure;
        }

        RegisteredExecutablePath = executablePath;
        RegisteredConfiguration = configuration;
        Tasks[taskName] = new StartupTaskInfo(
            executablePath,
            configuration.Arguments,
            "Shows the Usage Indicator for Codex companion.",
            true);
    }

    public void Delete(string taskName)
    {
        Operations.Add($"Delete:{taskName}");
        if (DeleteFailures.TryGetValue(taskName, out var failure))
        {
            throw failure;
        }

        if (!Tasks.Remove(taskName))
        {
            throw new FileNotFoundException("Task not found.");
        }
    }
}

internal sealed class RecordingHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    internal List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(responseFactory(request));
    }
}

internal sealed class RecordingUpdateScenario :
    IReleaseUpdateClient,
    IIndicatorController,
    IInstallerRunner,
    IInstalledVersionValidator,
    IUpdateWorkingDirectoryCleaner,
    IUpdateOutput
{
    private readonly Version _currentVersion = new(0, 2, 0);
    private readonly StableRelease _latestRelease = new(
        new Version(0, 3, 0),
        "v0.3.0",
        new Dictionary<string, ReleaseAsset>(StringComparer.Ordinal));

    internal List<string> Operations { get; } = [];
    internal bool MutexAcquired { get; init; } = true;
    internal bool MutexDisposed { get; private set; }
    internal bool IsAvailable { get; init; } = true;
    internal bool WasRunning { get; init; }
    internal int InstallerExitCode { get; init; }
    internal int InstallerBootstrapVersion { get; private set; }
    internal string? InstallerLogPath { get; private set; }
    internal Exception? CheckFailure { get; init; }
    internal Exception? PrepareFailure { get; init; }
    internal Exception? StopFailure { get; init; }
    internal Exception? InstallerFailure { get; init; }
    internal Exception? ValidationFailure { get; init; }
    internal Exception? StartFailure { get; init; }
    internal Exception? CleanupFailure { get; init; }

    internal UpdateOrchestrator CreateOrchestrator() =>
        new(
            this,
            () =>
            {
                Operations.Add("lock");
                return new RecordingUpdateMutexLease(
                    MutexAcquired,
                    () =>
                    {
                        MutexDisposed = true;
                        Operations.Add("release");
                    });
            },
            this,
            this,
            this,
            this,
            this,
            new UpdatePaths(
                @"C:\Program Files\Usage Indicator",
                @"C:\Update Work",
                @"C:\Update Logs"));

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        Operations.Add("check");
        cancellationToken.ThrowIfCancellationRequested();
        if (CheckFailure is not null)
        {
            throw CheckFailure;
        }

        return Task.FromResult(new UpdateCheckResult(
            _currentVersion,
            _latestRelease,
            IsAvailable));
    }

    public Task<PreparedUpdate> PrepareAsync(
        StableRelease release,
        string destinationRoot,
        Action verificationStarted,
        CancellationToken cancellationToken)
    {
        Operations.Add("download");
        cancellationToken.ThrowIfCancellationRequested();
        if (PrepareFailure is not null)
        {
            throw PrepareFailure;
        }

        verificationStarted();
        Operations.Add("verified");
        return Task.FromResult(new PreparedUpdate(
            release.Version,
            @"C:\Update Work With Spaces\UsageIndicatorForCodex-Setup-v0.3.0.exe",
            @"C:\Update Work With Spaces"));
    }

    public bool IsRunning()
    {
        Operations.Add("inspect");
        return WasRunning;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Operations.Add("stop");
        cancellationToken.ThrowIfCancellationRequested();
        return StopFailure is null
            ? Task.CompletedTask
            : Task.FromException(StopFailure);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Operations.Add("start");
        cancellationToken.ThrowIfCancellationRequested();
        return StartFailure is null
            ? Task.CompletedTask
            : Task.FromException(StartFailure);
    }

    public Task<int> RunAsync(
        string installerPath,
        string logPath,
        int bootstrapVersion,
        CancellationToken cancellationToken)
    {
        Operations.Add("install");
        cancellationToken.ThrowIfCancellationRequested();
        InstallerLogPath = logPath;
        InstallerBootstrapVersion = bootstrapVersion;
        return InstallerFailure is null
            ? Task.FromResult(InstallerExitCode)
            : Task.FromException<int>(InstallerFailure);
    }

    public void Validate(string installRoot, Version targetVersion)
    {
        Operations.Add("validate");
        if (ValidationFailure is not null)
        {
            throw ValidationFailure;
        }
    }

    public void Delete(string workingDirectory)
    {
        Operations.Add("cleanup");
        if (CleanupFailure is not null)
        {
            throw CleanupFailure;
        }
    }

    public void WriteLine(string message) => Operations.Add($"out:{message}");

    public void WriteError(string message) => Operations.Add($"err:{message}");
}

internal sealed class RecordingUpdateMutexLease(
    bool isAcquired,
    Action onDispose) : IUpdateMutexLease
{
    private bool _disposed;

    public bool IsAcquired { get; } = isAcquired;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        onDispose();
    }
}
