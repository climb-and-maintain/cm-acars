using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using FlaApplication = FlaUI.Core.Application;

namespace ClimbAndMaintain.Acars.UITests;

public sealed class FlaUiSmokeTests
{
    private static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);

    public static bool CanRunFlaUi =>
        OperatingSystem.IsWindows() &&
        string.Equals(
            Environment.GetEnvironmentVariable("CM_ACARS_RUN_UI_TESTS"),
            "1",
            StringComparison.Ordinal);

    [Fact(
        Skip = "FlaUI smoke coverage requires Windows and CM_ACARS_RUN_UI_TESTS=1.",
        SkipUnless = nameof(CanRunFlaUi),
        Timeout = 60_000)]
    public void AppExposesNamedSetupNavigationAndCorePages()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string appPath = ResolveAppPath();
        Assert.True(File.Exists(appPath), $"Built application was not found at {appPath}.");

        ProcessStartInfo startInfo = new(appPath)
        {
            WorkingDirectory = Path.GetDirectoryName(appPath)
                ?? throw new InvalidOperationException($"Application path has no parent directory: {appPath}"),
            UseShellExecute = false,
        };
        using FlaApplication application = FlaApplication.Launch(startInfo);
        using UIA3Automation automation = new();

        try
        {
            Window shell = WaitFor(
                () => FindWindow(application, automation, "ShellWindow"),
                "the ACARS main window",
                cancellationToken);
            Assert.Equal("ACARS main window", shell.Name);

            Window? firstRun = TryWaitFor(
                () => FindWindow(application, automation, "FirstRun.Window"),
                TimeSpan.FromSeconds(5),
                cancellationToken);
            if (firstRun is not null)
            {
                Assert.Equal("ACARS first-run setup", firstRun.Name);
                AutomationElement later = WaitFor(
                    () => firstRun.FindFirstDescendant(cf => cf.ByAutomationId("FirstRun.Later")),
                    "the accessible Set up later button",
                    cancellationToken);
                Assert.Equal("Close setup and configure later", later.Name);
                later.AsButton().Invoke();
                WaitFor(
                    () => FindWindow(application, automation, "FirstRun.Window") is null,
                    "the first-run window to close",
                    cancellationToken);
            }

            AutomationElement navigationElement = WaitFor(
                () => shell.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryNavigation")),
                "primary navigation",
                cancellationToken);
            Assert.Equal("Primary navigation", navigationElement.Name);
            ListBox navigation = navigationElement.AsListBox();
            string[] pageNames = navigation.Items.Select(static item => item.Name).ToArray();
            foreach (string pageName in new[] { "Dashboard", "Flights", "Flight Log", "Diagnostics", "Settings", "About" })
            {
                Assert.Contains(pageName, pageNames);
            }

            navigation.Select("Settings");
            AutomationElement appearance = WaitFor(
                () => shell.FindFirstDescendant(cf => cf.ByAutomationId("Appearance.System")),
                "Settings appearance controls",
                cancellationToken);
            Assert.Equal("Use system appearance", appearance.Name);

            navigation.Select("Diagnostics");
            AutomationElement test2020 = WaitFor(
                () => shell.FindFirstDescendant(cf => cf.ByAutomationId("SimConnect2020.Test")),
                "MSFS 2020 connection test",
                cancellationToken);
            AutomationElement test2024 = WaitFor(
                () => shell.FindFirstDescendant(cf => cf.ByAutomationId("SimConnect2024.Test")),
                "MSFS 2024 connection test",
                cancellationToken);
            Assert.Contains("2020", test2020.Name, StringComparison.Ordinal);
            Assert.Contains("2024", test2024.Name, StringComparison.Ordinal);
            Assert.NotEqual(test2020.Name, test2024.Name);

            navigation.Select("Flights");
            AutomationElement replay = WaitFor(
                () => shell.FindFirstDescendant(cf => cf.ByAutomationId("Replay.Start")),
                "recorded telemetry replay controls",
                cancellationToken);
            Assert.Equal("Start telemetry replay", replay.Name);

            AutomationElement availability = WaitFor(
                () => shell.FindFirstDescendant(cf => cf.ByName("Application availability status")),
                "missing-prerequisite availability status",
                cancellationToken);
            Assert.NotEmpty(availability.Name);
        }
        finally
        {
            application.Kill();
        }
    }

    private static string ResolveAppPath()
    {
        string? configured = Environment.GetEnvironmentVariable("CM_ACARS_APP_PATH");
        return string.IsNullOrWhiteSpace(configured)
            ? RepositoryLayout.FromRoot(
                "src",
                "ClimbAndMaintain.Acars.App",
                "bin",
                "Release",
                "net10.0-windows",
                "ClimbAndMaintain.Acars.App.exe")
            : Path.GetFullPath(configured);
    }

    private static Window? FindWindow(FlaApplication application, UIA3Automation automation, string automationId)
    {
        if (application.HasExited)
        {
            throw new InvalidOperationException($"The application exited before '{automationId}' appeared (exit code {application.ExitCode}).");
        }

        return application
            .GetAllTopLevelWindows(automation)
            .SingleOrDefault(window => string.Equals(window.AutomationId, automationId, StringComparison.Ordinal));
    }

    private static T WaitFor<T>(Func<T?> probe, string description, CancellationToken cancellationToken)
        where T : class
    {
        return TryWaitFor(probe, ElementTimeout, cancellationToken)
            ?? throw new TimeoutException($"Timed out waiting for {description}.");
    }

    private static T? TryWaitFor<T>(Func<T?> probe, TimeSpan timeout, CancellationToken cancellationToken)
        where T : class
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            T? result = probe();
            if (result is not null)
            {
                return result;
            }

            Thread.Sleep(RetryInterval);
        }
        while (stopwatch.Elapsed < timeout);

        return null;
    }

    private static void WaitFor(Func<bool> probe, string description, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (probe())
            {
                return;
            }

            Thread.Sleep(RetryInterval);
        }
        while (stopwatch.Elapsed < ElementTimeout);

        throw new TimeoutException($"Timed out waiting for {description}.");
    }
}
