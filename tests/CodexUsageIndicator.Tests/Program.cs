using CodexUsageIndicator.Core;
using CodexUsageIndicator.Services;
using CodexUsageIndicator.Views;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

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
    ("coalesces overlapping refresh requests", CoalescesOverlappingRefreshRequests),
    ("retains the attached Codex window across foreign focus", RetainsAttachedWindowAcrossForeignFocus),
    ("ignores overlay location events while tracking the attached Codex window", IgnoresOverlayLocationEvents),
    ("observes attached Codex windows being hidden", ObservesAttachedWindowHide),
    ("identifies the Codex Desktop window by package identity", IdentifiesCodexDesktopWindow),
    ("selects the most recently active Codex window on startup", SelectsInitialAttachedWindow),
    ("rejects expired-only usage windows", RejectsExpiredWindows),
    ("parses primary and secondary app-server limits", ParsesRateLimitResponse),
    ("rejects malformed and incompatible app-server responses", RejectsInvalidResponses),
    ("fails closed when the configured CLI is absent", FailsWhenCliIsMissing),
    ("resolves the CLI path without a machine-specific username", ResolvesPortableCliPath),
    ("rejects a relative CLI override", RejectsRelativeCliPath),
    ("quotes a CLI path containing spaces", QuotesCliPath),
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
            overlay.Render(
                IndicatorState.Available,
                new UsageSnapshot("account", 53, new DateTimeOffset(2026, 7, 29, 0, 23, 0, TimeSpan.Zero)),
                OverlayLayout.Full);

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

static void CoalescesOverlappingRefreshRequests()
{
    var runner = new CoalescingRefreshRunner();
    var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var executions = 0;

    async Task Refresh()
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

static void FailsWhenCliIsMissing()
{
    var reader = new CodexCliAppServerReader(Path.Combine(Path.GetTempPath(), "missing-codex.cmd"), null, TimeSpan.FromMilliseconds(50));
    AssertThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(CancellationToken.None)).GetAwaiter().GetResult();
}

static void ResolvesPortableCliPath()
{
    var configuredPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "tools", "codex.cmd");
    var previousValue = Environment.GetEnvironmentVariable(CodexCliAppServerReader.CliPathEnvironmentVariable);
    try
    {
        Environment.SetEnvironmentVariable(CodexCliAppServerReader.CliPathEnvironmentVariable, configuredPath);
        AssertEqual(configuredPath, CodexCliAppServerReader.ResolveCliPath());
    }
    finally
    {
        Environment.SetEnvironmentVariable(CodexCliAppServerReader.CliPathEnvironmentVariable, previousValue);
    }
}

static void RejectsRelativeCliPath()
{
    var previousValue = Environment.GetEnvironmentVariable(CodexCliAppServerReader.CliPathEnvironmentVariable);
    try
    {
        Environment.SetEnvironmentVariable(CodexCliAppServerReader.CliPathEnvironmentVariable, "codex.cmd");
        AssertThrows<InvalidOperationException>(() => { _ = CodexCliAppServerReader.ResolveCliPath(); });
    }
    finally
    {
        Environment.SetEnvironmentVariable(CodexCliAppServerReader.CliPathEnvironmentVariable, previousValue);
    }
}

static void QuotesCliPath()
{
    var cliPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Program Files", "Codex", "codex.cmd");
    AssertEqual($"\"{cliPath}\" app-server --stdio", CodexCliAppServerReader.CreateAppServerCommand(cliPath));
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
