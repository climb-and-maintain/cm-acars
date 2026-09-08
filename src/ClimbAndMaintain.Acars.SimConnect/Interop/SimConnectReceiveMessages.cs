using System.Collections.Immutable;
using ClimbAndMaintain.Acars.SimConnect.Configuration;

namespace ClimbAndMaintain.Acars.SimConnect.Interop;

public sealed record SimConnectReceiveHeader(
    uint Size,
    uint Version,
    SimConnectReceiveId Id);

public sealed record SimConnectOpenMessage(
    SimConnectReceiveHeader Header,
    SimConnectOpenInfo Information);

public sealed record SimConnectExceptionMessage(
    SimConnectReceiveHeader Header,
    SimConnectExceptionCode Exception,
    uint SendId,
    uint ParameterIndex);

public sealed record SimConnectEventMessage(
    SimConnectReceiveHeader Header,
    uint GroupId,
    uint EventId,
    uint Data);

public sealed record SimConnectEventFilenameMessage(
    SimConnectReceiveHeader Header,
    uint GroupId,
    uint EventId,
    uint Data,
    string FileName,
    uint Flags);

public sealed record SimConnectObjectDataMessage(
    SimConnectReceiveHeader Header,
    uint RequestId,
    uint ObjectId,
    uint DefinitionId,
    uint Flags,
    uint EntryNumber,
    uint EntriesTotal,
    uint DefinitionCount,
    ReadOnlyMemory<byte> Payload);

public sealed record SimConnectCoreTelemetry(
    double LatitudeDegrees,
    double LongitudeDegrees,
    double AltitudeMslFeet,
    double AltitudeAglFeet,
    double IndicatedAirspeedKnots,
    double GroundSpeedKnots,
    double VerticalSpeedFeetPerMinute,
    double TrueHeadingDegrees,
    double MagneticHeadingDegrees,
    bool OnGround);

public sealed record SimConnectEngineTelemetry(
    bool Combustion,
    bool StarterActive,
    double N1Ratio,
    double FuelFlowPoundsPerHour);

public sealed record SimConnectStandardSystemsTelemetry(
    double FuelWeightPounds,
    int EngineCount,
    ImmutableArray<SimConnectEngineTelemetry> Engines,
    bool BeaconLight,
    bool NavigationLight,
    bool StrobeLight,
    bool LandingLight,
    bool TaxiLight,
    bool WingLight,
    bool LogoLight,
    bool AutopilotEngaged,
    bool ParkingBrakeSet,
    int TransponderState,
    bool BatteryOn,
    bool ExternalPowerOn,
    double ApuRpmRatio,
    bool AntiIceOn,
    bool SeatBeltSignOn);

public sealed record SimConnectFlightControlsTelemetry(
    double GearExtensionRatio,
    int FlapHandleIndex,
    double FlapExtensionRatio)
{
    public static SimConnectFlightControlsTelemetry Unknown { get; } = new(0, 0, 0);
}

public sealed record SimConnectAircraftIdentityTelemetry(
    string Title,
    string? IcaoType);
