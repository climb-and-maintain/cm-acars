using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ClimbAndMaintain.Acars.SimConnect.Provider;
using ClimbAndMaintain.Acars.SimConnect.Tests.Fixtures;
using ClimbAndMaintain.Acars.SimConnect.Validation;
using ClimbAndMaintain.Acars.SimConnect.Variables;

namespace ClimbAndMaintain.Acars.SimConnect.Tests;

public sealed class SimConnectProviderTests
{
    [Fact]
    public async Task ProviderConnectsAndPublishesNormalizedTelemetry()
    {
        using TemporaryFile library = new(PortableExecutableFixture.Create());
        FakeSimConnectNativeApiFactory factory = new(PacketFixtures.Open(12, 12))
        {
            SimulatorTime = new(2026, 9, 8, 12, 34, 56, TimeSpan.Zero),
            IsSlewActive = true,
            PauseState = 4,
            TransponderCodeBco16 = 0x0453,
        };
        await using MsfsSimConnectProvider provider = new(
            new()
            {
                Target = SimConnectTarget.Msfs2024,
                LibraryPath = library.Path,
                ConnectionTimeout = TimeSpan.FromSeconds(2),
            },
            new SimConnectLibraryValidator(),
            factory,
            platformSupported: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));

        SimulatorConnectionResult result = await provider.ConnectAsync(timeout.Token);
        await using IAsyncEnumerator<TelemetrySnapshot> reader = provider
            .ReadTelemetryAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(47.4502, reader.Current.Position.LatitudeDegrees, 4);
        Assert.Equal(SimulatorKind.Msfs2024, reader.Current.Simulator.Kind);
        Assert.Equal("Cessna Skyhawk G1000", reader.Current.Aircraft.Title);
        Assert.Equal("C172", reader.Current.Aircraft.IcaoType);
        Assert.Equal(2, reader.Current.EngineCount);
        Assert.Equal(EngineOperatingState.Running, reader.Current.Engines[0].State);
        Assert.True(reader.Current.Systems.AutopilotEngaged);
        Assert.Equal(GearPosition.Down, reader.Current.Systems.Gear);
        Assert.True(reader.Current.Systems.Lights.Beacon);
        Assert.True(reader.Current.Systems.BatteryOn);
        Assert.True(reader.Current.Systems.ApuRunning);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 12, 34, 56, TimeSpan.Zero), reader.Current.SimulatorTime);
        Assert.True(reader.Current.IsPaused);
        Assert.True(reader.Current.IsSlewActive);
        Assert.Equal("0453", reader.Current.Systems.Transponder!.Code);
        Assert.NotNull(reader.Current.FuelRemaining);
        Assert.NotNull(reader.Current.TotalFuelFlow);
        await Task.Delay(25, timeout.Token);
        Assert.NotEqual(SimulatorConnectionState.Faulted, provider.ConnectionState);
        Assert.Contains(provider.ConnectionState, new[]
        {
            SimulatorConnectionState.Connected,
            SimulatorConnectionState.Ready,
        });

        await provider.DisconnectAsync(CancellationToken.None);
        Assert.Equal(SimulatorConnectionState.Disconnected, provider.ConnectionState);
        Assert.NotNull(factory.Instance);
        Assert.Single(factory.Instance.CallingThreadIds.Distinct());
        Assert.Contains(factory.Instance.AddedDefinitions, item =>
            item.Name == "TRANSPONDER CODE:1" && item.Unit == "BCO16");
    }

    [Fact]
    public async Task RejectedOptionalClockDoesNotStopOtherSupplementalOrCoreTelemetry()
    {
        using TemporaryFile library = new(PortableExecutableFixture.Create());
        FakeSimConnectNativeApiFactory factory = new(PacketFixtures.Open(12, 12))
        {
            AsynchronouslyRejectedDatumName = "ZULU MONTH OF YEAR",
            IsSlewActive = true,
            PauseState = 1,
            TransponderCodeBco16 = 0x1200,
        };
        await using MsfsSimConnectProvider provider = new(
            new()
            {
                Target = SimConnectTarget.Msfs2024,
                LibraryPath = library.Path,
                ConnectionTimeout = TimeSpan.FromSeconds(2),
            },
            new SimConnectLibraryValidator(),
            factory,
            platformSupported: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));

        SimulatorConnectionResult result = await provider.ConnectAsync(timeout.Token);
        await using IAsyncEnumerator<TelemetrySnapshot> reader = provider
            .ReadTelemetryAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(await reader.MoveNextAsync());
        Assert.Null(reader.Current.SimulatorTime);
        Assert.True(reader.Current.IsPaused);
        Assert.True(reader.Current.IsSlewActive);
        Assert.Equal("1200", reader.Current.Systems.Transponder!.Code);
        Assert.NotEqual(SimulatorConnectionState.Faulted, provider.ConnectionState);
    }

    [Fact]
    public async Task MissingLibraryDoesNotPreventUnavailableProviderUse()
    {
        await using MsfsSimConnectProvider provider = new(new()
        {
            Target = SimConnectTarget.Msfs2020,
            LibraryPath = null,
        });

        SimulatorConnectionResult result = await provider.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(SimulatorConnectionFailureKind.IncompatibleLibrary, result.FailureKind);
        Assert.Equal(SimulatorConnectionState.Unavailable, provider.ConnectionState);
    }

    [Fact]
    public async Task ExplicitUnavailableProviderFailsGracefullyAndHasEmptyStream()
    {
        await using SimConnectUnavailableProvider provider = new("No native library has been configured.");

        SimulatorConnectionResult result = await provider.ConnectAsync(TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<TelemetrySnapshot> reader = provider
            .ReadTelemetryAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(SimulatorConnectionFailureKind.PrerequisiteMissing, result.FailureKind);
        Assert.False(await reader.MoveNextAsync());
    }

    [Fact]
    public async Task PublishesValidatedReadOnlySimVarAndLVarSamples()
    {
        using TemporaryFile library = new(PortableExecutableFixture.Create());
        FakeSimConnectNativeApiFactory factory = new(PacketFixtures.Open(12, 12));
        SimConnectReadOnlyVariableDefinition beacon = new(
            "beacon-override",
            SimConnectVariableKind.SimVar,
            "LIGHT BEACON",
            "Bool",
            SimConnectVariableValueKind.Logical);
        SimConnectReadOnlyVariableDefinition customApu = new(
            "custom-apu",
            SimConnectVariableKind.LocalVariable,
            "L:CUSTOM_APU_RUNNING",
            "number");
        await using MsfsSimConnectProvider provider = new(
            new()
            {
                Target = SimConnectTarget.Msfs2024,
                LibraryPath = library.Path,
                ConnectionTimeout = TimeSpan.FromSeconds(2),
                ReadOnlyVariables = [beacon, customApu],
            },
            new SimConnectLibraryValidator(),
            factory,
            platformSupported: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));

        SimulatorConnectionResult result = await provider.ConnectAsync(timeout.Token);
        await using IAsyncEnumerator<SimConnectReadOnlyVariableSample> reader = provider
            .ReadVariableSamplesAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(beacon, reader.Current.Definition);
        Assert.True(reader.Current.BooleanValue);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(customApu, reader.Current.Definition);
        Assert.Equal(2, reader.Current.NumericValue);
        Assert.Contains(factory.Instance!.AddedDefinitions, item =>
            item.Name == "L:CUSTOM_APU_RUNNING");
    }

    [Fact]
    public async Task RejectedOptionalVariableDoesNotStopCoreTelemetryOrOtherProfileVariables()
    {
        using TemporaryFile library = new(PortableExecutableFixture.Create());
        FakeSimConnectNativeApiFactory factory = new(PacketFixtures.Open(12, 12))
        {
            AsynchronouslyRejectedDatumName = "L:UNSUPPORTED_AIRCRAFT_VARIABLE",
        };
        SimConnectReadOnlyVariableDefinition rejected = new(
            "unsupported",
            SimConnectVariableKind.LocalVariable,
            "L:UNSUPPORTED_AIRCRAFT_VARIABLE",
            "number");
        SimConnectReadOnlyVariableDefinition supported = new(
            "supported",
            SimConnectVariableKind.SimVar,
            "LIGHT BEACON",
            "Bool",
            SimConnectVariableValueKind.Logical);
        await using MsfsSimConnectProvider provider = new(
            new()
            {
                Target = SimConnectTarget.Msfs2024,
                LibraryPath = library.Path,
                ConnectionTimeout = TimeSpan.FromSeconds(2),
                ReadOnlyVariables = [rejected, supported],
            },
            new SimConnectLibraryValidator(),
            factory,
            platformSupported: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));

        SimulatorConnectionResult result = await provider.ConnectAsync(timeout.Token);
        await using IAsyncEnumerator<TelemetrySnapshot> telemetryReader = provider
            .ReadTelemetryAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        await using IAsyncEnumerator<SimConnectReadOnlyVariableSample> variableReader = provider
            .ReadVariableSamplesAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(await telemetryReader.MoveNextAsync());
        Assert.True(await variableReader.MoveNextAsync());
        Assert.NotEqual(SimulatorConnectionState.Faulted, provider.ConnectionState);
        Assert.Contains(factory.Instance!.Requests, item => item.DefinitionId == 0x434D2001);
    }

    [Fact]
    public async Task SimulatorQuitRecoversWithANewNativeSession()
    {
        using TemporaryFile library = new(PortableExecutableFixture.Create());
        FakeSimConnectNativeApiFactory factory = FakeSimConnectNativeApiFactory.ForSessions(
            [PacketFixtures.Open(11, 11), PacketFixtures.Quit()],
            [PacketFixtures.Open(11, 11)]);
        await using MsfsSimConnectProvider provider = new(
            new()
            {
                Target = SimConnectTarget.Msfs2020,
                LibraryPath = library.Path,
                ConnectionTimeout = TimeSpan.FromSeconds(2),
                RecoveryInitialDelay = TimeSpan.FromMilliseconds(10),
                RecoveryMaximumDelay = TimeSpan.FromMilliseconds(20),
            },
            new SimConnectLibraryValidator(),
            factory,
            platformSupported: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));

        SimulatorConnectionResult result = await provider.ConnectAsync(timeout.Token);
        await using IAsyncEnumerator<TelemetrySnapshot> reader = provider
            .ReadTelemetryAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(await reader.MoveNextAsync());
        Assert.True(factory.Instances.Count >= 2);
        Assert.Equal(SimulatorKind.Msfs2020, reader.Current.Simulator.Kind);
        Assert.Equal(SimulatorConnectionState.Ready, provider.ConnectionState);
    }

    [Fact]
    public async Task SimulatorRecoveryRevalidatesLibraryBeforeLoadingANewNativeSession()
    {
        using TemporaryFile library = new(PortableExecutableFixture.Create());
        FakeSimConnectNativeApiFactory factory = FakeSimConnectNativeApiFactory.ForSessions(
            [PacketFixtures.Open(11, 11), PacketFixtures.Quit()],
            [PacketFixtures.Open(11, 11)]);
        await using MsfsSimConnectProvider provider = new(
            new()
            {
                Target = SimConnectTarget.Msfs2020,
                LibraryPath = library.Path,
                ConnectionTimeout = TimeSpan.FromSeconds(2),
                RecoveryInitialDelay = TimeSpan.FromMilliseconds(250),
                RecoveryMaximumDelay = TimeSpan.FromMilliseconds(250),
            },
            new SimConnectLibraryValidator(),
            factory,
            platformSupported: true);
        TaskCompletionSource recovering = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.ConnectionStateChanged += (_, eventArgs) =>
        {
            if (eventArgs.Current == SimulatorConnectionState.Recovering)
            {
                recovering.TrySetResult();
            }
            else if (eventArgs.Current == SimulatorConnectionState.Faulted)
            {
                faulted.TrySetResult();
            }
        };
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));

        SimulatorConnectionResult result = await provider.ConnectAsync(timeout.Token);
        await recovering.Task.WaitAsync(timeout.Token);
        File.WriteAllText(library.Path, "not a portable executable");
        await faulted.Task.WaitAsync(timeout.Token);

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(factory.Instances);
        Assert.Equal(SimulatorConnectionState.Faulted, provider.ConnectionState);
    }

    [Fact]
    public async Task RemoteDisconnectHResultRecoversWithANewNativeSession()
    {
        using TemporaryFile library = new(PortableExecutableFixture.Create());
        FakeSimConnectNativeApiFactory factory = FakeSimConnectNativeApiFactory.ForSessions(
            [PacketFixtures.Open(12, 12), PacketFixtures.RemoteDisconnect()],
            [PacketFixtures.Open(12, 12)]);
        await using MsfsSimConnectProvider provider = new(
            new()
            {
                Target = SimConnectTarget.Msfs2024,
                LibraryPath = library.Path,
                ConnectionTimeout = TimeSpan.FromSeconds(2),
                RecoveryInitialDelay = TimeSpan.FromMilliseconds(10),
                RecoveryMaximumDelay = TimeSpan.FromMilliseconds(20),
            },
            new SimConnectLibraryValidator(),
            factory,
            platformSupported: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));

        SimulatorConnectionResult result = await provider.ConnectAsync(timeout.Token);
        await using IAsyncEnumerator<TelemetrySnapshot> reader = provider
            .ReadTelemetryAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(await reader.MoveNextAsync());
        Assert.True(factory.Instances.Count >= 2);
        Assert.Equal(SimulatorConnectionState.Ready, provider.ConnectionState);
    }

    [Fact]
    public async Task FastTelemetryUsesSerializedTwicePerSecondRequests()
    {
        using TemporaryFile library = new(PortableExecutableFixture.Create());
        FakeSimConnectNativeApiFactory factory = new(PacketFixtures.Open(12, 12));
        await using MsfsSimConnectProvider provider = new(
            new()
            {
                Target = SimConnectTarget.Msfs2024,
                LibraryPath = library.Path,
                ConnectionTimeout = TimeSpan.FromSeconds(2),
            },
            new SimConnectLibraryValidator(),
            factory,
            platformSupported: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));

        SimulatorConnectionResult result = await provider.ConnectAsync(timeout.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(1_100), timeout.Token);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(factory.Instance);
        Assert.True(factory.Instance.Requests.Count(item => item.DefinitionId == 0x434D1001) >= 3);
        Assert.True(factory.Instance.Requests.Count(item => item.DefinitionId == 0x434D1009) >= 3);
        Assert.All(
            factory.Instance.Requests.Where(item => item.DefinitionId == 0x434D1001),
            item => Assert.Equal(SimConnectPeriod.Once, item.Period));
        Assert.All(
            factory.Instance.Requests.Where(item => item.DefinitionId == 0x434D1009),
            item => Assert.Equal(SimConnectPeriod.Once, item.Period));
        Assert.Contains(
            factory.Instance.Requests,
            item => item.DefinitionId == 0x434D1003 && item.Period == SimConnectPeriod.Second);
        Assert.Single(factory.Instance.CallingThreadIds.Distinct());
        Assert.Equal(SimulatorConnectionState.Tracking, provider.ConnectionState);
    }
}
