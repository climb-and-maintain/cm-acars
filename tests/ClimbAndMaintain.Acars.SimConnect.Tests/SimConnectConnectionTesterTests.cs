using ClimbAndMaintain.Acars.SimConnect.Connection;
using ClimbAndMaintain.Acars.SimConnect.Tests.Fixtures;
using ClimbAndMaintain.Acars.SimConnect.Validation;

namespace ClimbAndMaintain.Acars.SimConnect.Tests;

public sealed class SimConnectConnectionTesterTests
{
    [Fact]
    public async Task PerformsHandshakeAndTelemetryTestOnOneOwningThread()
    {
        using TemporaryFile library = new(PortableExecutableFixture.Create());
        FakeSimConnectNativeApiFactory factory = new(PacketFixtures.Open(11, 11));
        SimConnectConnectionTester tester = new(new SimConnectLibraryValidator(), factory, platformSupported: true);

        SimConnectConnectionTestResult result = await tester.TestAsync(
            SimConnectTarget.Msfs2020,
            library.Path,
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.TelemetrySample);
        Assert.NotNull(result.Attestation);
        Assert.Equal(SimConnectTarget.Msfs2020, result.Attestation.Target);
        Assert.Equal(11U, result.Server!.ApplicationVersionMajor);
        Assert.NotNull(factory.Instance);
        Assert.Single(factory.Instance.CallingThreadIds.Distinct());
    }

    [Fact]
    public async Task RejectsAHandshakeFromTheOtherSimulatorGeneration()
    {
        using TemporaryFile library = new(PortableExecutableFixture.Create());
        FakeSimConnectNativeApiFactory factory = new(PacketFixtures.Open(12, 12));
        SimConnectConnectionTester tester = new(new SimConnectLibraryValidator(), factory, platformSupported: true);

        SimConnectConnectionTestResult result = await tester.TestAsync(
            SimConnectTarget.Msfs2020,
            library.Path,
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(SimConnectConnectionTestOutcome.WrongSimulator, result.Outcome);
        Assert.Null(result.Attestation);
    }

    [Fact]
    public async Task ReportsVersionMismatchSeparately()
    {
        using TemporaryFile library = new(PortableExecutableFixture.Create());
        FakeSimConnectNativeApiFactory factory = new(
            PacketFixtures.Exception(SimConnectExceptionCode.VersionMismatch));
        SimConnectConnectionTester tester = new(new SimConnectLibraryValidator(), factory, platformSupported: true);

        SimConnectConnectionTestResult result = await tester.TestAsync(
            SimConnectTarget.Msfs2024,
            library.Path,
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(SimConnectConnectionTestOutcome.VersionMismatch, result.Outcome);
        Assert.Contains(result.Diagnostics, item => item.Code == SimConnectDiagnosticCodes.VersionMismatch);
    }

    [Fact]
    public async Task DoesNotAttemptNativeLoadWhenStaticValidationFails()
    {
        FakeSimConnectNativeApiFactory factory = new();
        SimConnectConnectionTester tester = new(new SimConnectLibraryValidator(), factory, platformSupported: true);

        SimConnectConnectionTestResult result = await tester.TestAsync(
            SimConnectTarget.Msfs2024,
            "/does/not/exist/SimConnect.dll",
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(SimConnectConnectionTestOutcome.StaticValidationFailed, result.Outcome);
        Assert.Null(factory.Instance);
    }

    [Fact]
    public async Task CrossPlatformDefaultReturnsAnActionableDiagnostic()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryFile library = new(PortableExecutableFixture.Create());
        SimConnectConnectionTester tester = new();

        SimConnectConnectionTestResult result = await tester.TestAsync(
            SimConnectTarget.Msfs2024,
            library.Path,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(SimConnectConnectionTestOutcome.PlatformUnsupported, result.Outcome);
        Assert.Contains(result.Diagnostics, item => item.Code == SimConnectDiagnosticCodes.PlatformUnsupported);
    }
}
