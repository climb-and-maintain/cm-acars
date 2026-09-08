using System.Buffers.Binary;
using ClimbAndMaintain.Acars.SimConnect.Interop;
using ClimbAndMaintain.Acars.SimConnect.Tests.Fixtures;
using ClimbAndMaintain.Acars.SimConnect.Validation;

namespace ClimbAndMaintain.Acars.SimConnect.Tests;

public sealed class SimConnectReceiveParserTests
{
    [Fact]
    public void ParsesMsfs2020OpenMessage()
    {
        bool parsed = SimConnectReceiveParser.TryParseOpen(
            PacketFixtures.Open(11, 11),
            out SimConnectOpenMessage? message,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(message);
        Assert.Equal("KittyHawk", message.Information.ApplicationName);
        Assert.Equal(11U, message.Information.ApplicationVersionMajor);
        Assert.Equal("11.0.282174.999", message.Information.ApplicationVersion);
        Assert.True(SimConnectCompatibility.IsExpectedSimulator(SimConnectTarget.Msfs2020, message.Information));
        Assert.False(SimConnectCompatibility.IsExpectedSimulator(SimConnectTarget.Msfs2024, message.Information));
    }

    [Fact]
    public void ParsesMsfs2024OpenMessage()
    {
        bool parsed = SimConnectReceiveParser.TryParseOpen(
            PacketFixtures.Open(12, 12),
            out SimConnectOpenMessage? message,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(message);
        Assert.Equal("SunRise", message.Information.ApplicationName);
        Assert.Equal(12U, message.Information.ApplicationVersionMajor);
        Assert.True(SimConnectCompatibility.IsExpectedSimulator(SimConnectTarget.Msfs2024, message.Information));
    }

    [Fact]
    public void RejectsTruncatedHeader()
    {
        bool parsed = SimConnectReceiveParser.TryParseHeader(new byte[11], out _, out string? error);

        Assert.False(parsed);
        Assert.Contains("at least 12", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsClaimedSizeBeyondBuffer()
    {
        byte[] packet = PacketFixtures.Null();
        BinaryPrimitives.WriteUInt32LittleEndian(packet, 100);

        bool parsed = SimConnectReceiveParser.TryParseHeader(packet, out _, out string? error);

        Assert.False(parsed);
        Assert.Contains("outside", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsOpenMessageWithoutTerminatedApplicationName()
    {
        byte[] packet = PacketFixtures.Open(11, 11);
        packet.AsSpan(12, 256).Fill((byte)'X');

        bool parsed = SimConnectReceiveParser.TryParseOpen(packet, out _, out string? error);

        Assert.False(parsed);
        Assert.Contains("null terminated", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesExceptionMessage()
    {
        bool parsed = SimConnectReceiveParser.TryParseException(
            PacketFixtures.Exception(SimConnectExceptionCode.VersionMismatch),
            out SimConnectExceptionMessage? message,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(message);
        Assert.Equal(SimConnectExceptionCode.VersionMismatch, message.Exception);
        Assert.Equal(7U, message.SendId);
        Assert.Equal(uint.MaxValue, message.ParameterIndex);
    }

    [Fact]
    public void ParsesPauseSystemEvent()
    {
        bool parsed = SimConnectReceiveParser.TryParseEvent(
            PacketFixtures.Event(0x434D1110, 4),
            out SimConnectEventMessage? message,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(message);
        Assert.Equal(0x434D1110U, message.EventId);
        Assert.Equal(4U, message.Data);
    }

    [Fact]
    public void ParsesPackedCoreTelemetry()
    {
        byte[] packet = PacketFixtures.CoreTelemetry();

        Assert.True(SimConnectReceiveParser.TryParseObjectData(packet, out SimConnectObjectDataMessage? data, out string? objectError), objectError);
        Assert.True(SimConnectReceiveParser.TryParseCoreTelemetry(data!, out SimConnectCoreTelemetry? telemetry, out string? telemetryError), telemetryError);

        Assert.NotNull(telemetry);
        Assert.Equal(47.4502, telemetry.LatitudeDegrees, 4);
        Assert.Equal(-122.3088, telemetry.LongitudeDegrees, 4);
        Assert.Equal(1_250, telemetry.AltitudeMslFeet);
        Assert.False(telemetry.OnGround);
    }

    [Fact]
    public void RejectsNonFiniteTelemetry()
    {
        byte[] packet = PacketFixtures.CoreTelemetry();
        BinaryPrimitives.WriteInt64LittleEndian(
            packet.AsSpan(SimConnectReceiveParser.ObjectDataPayloadOffset, sizeof(double)),
            BitConverter.DoubleToInt64Bits(double.NaN));

        Assert.True(SimConnectReceiveParser.TryParseObjectData(packet, out SimConnectObjectDataMessage? data, out _));
        bool parsed = SimConnectReceiveParser.TryParseCoreTelemetry(data!, out _, out string? error);

        Assert.False(parsed);
        Assert.Contains("non-finite", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnexpectedTelemetryPayloadLength()
    {
        byte[] packet = PacketFixtures.CoreTelemetry()[..^1];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, checked((uint)packet.Length));

        Assert.True(SimConnectReceiveParser.TryParseObjectData(packet, out SimConnectObjectDataMessage? data, out _));
        Assert.False(SimConnectReceiveParser.TryParseCoreTelemetry(data!, out _, out string? error));
        Assert.Contains("telemetry bytes", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesStandardSystemsTelemetry()
    {
        Assert.True(SimConnectReceiveParser.TryParseObjectData(
            PacketFixtures.StandardSystems(),
            out SimConnectObjectDataMessage? data,
            out string? objectError), objectError);

        bool parsed = SimConnectReceiveParser.TryParseStandardSystemsTelemetry(
            data!,
            out SimConnectStandardSystemsTelemetry? systems,
            out string? systemsError);

        Assert.True(parsed, systemsError);
        Assert.NotNull(systems);
        Assert.Equal(2, systems.EngineCount);
        Assert.True(systems.Engines[0].Combustion);
        Assert.Equal(500, systems.Engines[0].FuelFlowPoundsPerHour);
        Assert.True(systems.AutopilotEngaged);
        Assert.True(systems.BatteryOn);
        Assert.Equal(4, systems.TransponderState);
    }

    [Fact]
    public void ParsesFastGearAndFlapTelemetry()
    {
        Assert.True(SimConnectReceiveParser.TryParseObjectData(
            PacketFixtures.FlightControls(),
            out SimConnectObjectDataMessage? data,
            out string? objectError), objectError);

        bool parsed = SimConnectReceiveParser.TryParseFlightControlsTelemetry(
            data!,
            out SimConnectFlightControlsTelemetry? controls,
            out string? controlsError);

        Assert.True(parsed, controlsError);
        Assert.NotNull(controls);
        Assert.Equal(1, controls.GearExtensionRatio);
        Assert.Equal(2, controls.FlapHandleIndex);
        Assert.Equal(0.25, controls.FlapExtensionRatio);
    }

    [Fact]
    public void ParsesZuluSimulatorTime()
    {
        DateTimeOffset expected = new(2026, 9, 8, 12, 34, 56, 250, TimeSpan.Zero);
        Assert.True(SimConnectReceiveParser.TryParseObjectData(
            PacketFixtures.SimulatorTime(1, 2, expected),
            out SimConnectObjectDataMessage? data,
            out string? objectError), objectError);

        bool parsed = SimConnectReceiveParser.TryParseSimulatorTime(
            data!,
            out DateTimeOffset? simulatorTime,
            out string? error);

        Assert.True(parsed, error);
        Assert.Equal(expected, simulatorTime);
    }

    [Fact]
    public void ParsesBco16TransponderCodeWithoutDecimalConversion()
    {
        Assert.True(SimConnectReceiveParser.TryParseObjectData(
            PacketFixtures.DynamicValues(1, 2, 0x0453),
            out SimConnectObjectDataMessage? data,
            out string? objectError), objectError);

        bool parsed = SimConnectReceiveParser.TryParseTransponderCode(
            data!,
            out string? code,
            out string? error);

        Assert.True(parsed, error);
        Assert.Equal("0453", code);
    }

    [Fact]
    public void RejectsInvalidBco16TransponderDigit()
    {
        Assert.True(SimConnectReceiveParser.TryParseObjectData(
            PacketFixtures.DynamicValues(1, 2, 0x1288),
            out SimConnectObjectDataMessage? data,
            out _));

        Assert.False(SimConnectReceiveParser.TryParseTransponderCode(data!, out _, out string? error));
        Assert.Contains("BCO16", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesAircraftIdentity()
    {
        Assert.True(SimConnectReceiveParser.TryParseObjectData(
            PacketFixtures.AircraftIdentity(),
            out SimConnectObjectDataMessage? data,
            out string? objectError), objectError);

        bool parsed = SimConnectReceiveParser.TryParseAircraftIdentity(
            data!,
            out SimConnectAircraftIdentityTelemetry? identity,
            out string? identityError);

        Assert.True(parsed, identityError);
        Assert.NotNull(identity);
        Assert.Equal("Cessna Skyhawk G1000", identity.Title);
        Assert.Equal("C172", identity.IcaoType);
    }

    [Fact]
    public void ParsesAircraftFilenameEvent()
    {
        bool parsed = SimConnectReceiveParser.TryParseEventFilename(
            PacketFixtures.EventFilename(0x434D1010, @"SimObjects\Airplanes\C172\aircraft.cfg"),
            out SimConnectEventFilenameMessage? message,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(message);
        Assert.Equal(0x434D1010U, message.EventId);
        Assert.EndsWith("aircraft.cfg", message.FileName, StringComparison.Ordinal);
    }
}
