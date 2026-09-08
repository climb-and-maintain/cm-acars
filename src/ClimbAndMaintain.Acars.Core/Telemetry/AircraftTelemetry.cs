namespace ClimbAndMaintain.Acars.Core.Telemetry;

public enum EngineOperatingState
{
    Unknown,
    Off,
    Starting,
    Running,
    ShuttingDown,
    Failed,
}

public enum GearPosition
{
    Unknown,
    Up,
    InTransit,
    Down,
}

public enum DoorPosition
{
    Unknown,
    Closed,
    Open,
    InTransit,
}

public enum TransponderMode
{
    Unknown,
    Off,
    Standby,
    On,
    Altitude,
}

public sealed record EngineTelemetry
{
    public EngineTelemetry(
        int number,
        EngineOperatingState state,
        Ratio? n1 = null,
        FuelFlow? fuelFlow = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        Number = number;
        State = state;
        N1 = n1;
        FuelFlow = fuelFlow;
    }

    public int Number { get; }

    public EngineOperatingState State { get; }

    public Ratio? N1 { get; }

    public FuelFlow? FuelFlow { get; }
}

public sealed record FlapTelemetry
{
    public FlapTelemetry(int? index, Ratio? extension, string? label = null)
    {
        if (index is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The flap index cannot be negative.");
        }

        if (index is null && extension is null)
        {
            throw new ArgumentException("A flap index, extension ratio, or both must be supplied.");
        }

        if (label is { Length: > 32 } || label?.Any(char.IsControl) == true)
        {
            throw new ArgumentException("A flap label must be at most 32 printable characters.", nameof(label));
        }

        Index = index;
        Extension = extension;
        Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
    }

    public int? Index { get; }

    public Ratio? Extension { get; }

    public string? Label { get; }
}

public sealed record DoorTelemetry
{
    public DoorTelemetry(string name, DoorPosition position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Position = position;
    }

    public string Name { get; }

    public DoorPosition Position { get; }
}

public sealed record TransponderTelemetry
{
    public TransponderTelemetry(string? code, TransponderMode mode)
    {
        if (code is not null && (code.Length != 4 || code.Any(character => character is < '0' or > '7')))
        {
            throw new ArgumentException("A transponder code must contain four octal digits.", nameof(code));
        }

        Code = code;
        Mode = mode;
    }

    public string? Code { get; }

    public TransponderMode Mode { get; }
}

public sealed record AircraftLightsTelemetry
{
    public bool? Beacon { get; init; }

    public bool? Navigation { get; init; }

    public bool? Strobe { get; init; }

    public bool? Landing { get; init; }

    public bool? Taxi { get; init; }

    public bool? Wing { get; init; }

    public bool? Logo { get; init; }
}

public sealed record AircraftSystemsTelemetry
{
    public bool? ParkingBrakeSet { get; init; }

    public GearPosition Gear { get; init; } = GearPosition.Unknown;

    public FlapTelemetry? Flaps { get; init; }

    public bool? AutopilotEngaged { get; init; }

    public TransponderTelemetry? Transponder { get; init; }

    public AircraftLightsTelemetry Lights { get; init; } = new();

    public bool? BatteryOn { get; init; }

    public bool? ExternalPowerOn { get; init; }

    public bool? ApuRunning { get; init; }

    public bool? AntiIceOn { get; init; }

    public bool? SeatBeltSignOn { get; init; }

    public bool? PacksOn { get; init; }

    public bool? EmergencyLightsOn { get; init; }
}
