using System.Collections.Immutable;
using ClimbAndMaintain.Acars.Core.Simulation;

namespace ClimbAndMaintain.Acars.Core.Telemetry;

public sealed record TelemetrySnapshot
{
    public required DateTimeOffset CollectedAtUtc { get; init; }

    public DateTimeOffset? SimulatorTime { get; init; }

    public required GeoPosition Position { get; init; }

    public required Altitude AltitudeMsl { get; init; }

    public Altitude? AltitudeAgl { get; init; }

    public required Speed IndicatedAirspeed { get; init; }

    public required Speed GroundSpeed { get; init; }

    public required VerticalSpeed VerticalSpeed { get; init; }

    public Heading? TrueHeading { get; init; }

    public Heading? MagneticHeading { get; init; }

    public required bool OnGround { get; init; }

    public FuelMass? FuelRemaining { get; init; }

    public FuelFlow? TotalFuelFlow { get; init; }

    public ImmutableArray<EngineTelemetry> Engines { get; init; } = [];

    public AircraftSystemsTelemetry Systems { get; init; } = new();

    public ImmutableArray<DoorTelemetry> Doors { get; init; } = [];

    public AircraftIdentity Aircraft { get; init; } = AircraftIdentity.Unknown;

    public SimulatorIdentity Simulator { get; init; } = SimulatorIdentity.Unknown;

    public bool IsPaused { get; init; }

    public bool IsSlewActive { get; init; }

    public int EngineCount => Engines.IsDefault ? 0 : Engines.Length;

    public int RunningEngineCount => Engines.IsDefault
        ? 0
        : Engines.Count(engine => engine.State == EngineOperatingState.Running);
}
