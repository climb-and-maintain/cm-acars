namespace ClimbAndMaintain.Acars.SimConnect;

public static class SimConnectConstants
{
    public const double PoundsToKilograms = 0.45359237;
    public const uint ObjectIdUser = 0;
    public const uint Unused = uint.MaxValue;
    public const uint OpenConfigIndexLocal = uint.MaxValue;

    public const uint DataRequestFlagDefault = 0;
    public const uint DataRequestFlagChanged = 1;
    public const uint DataRequestFlagTagged = 2;

    public const int StatusRemoteDisconnect = unchecked((int)0xC000013C);

    public static bool Succeeded(int hResult) => hResult >= 0;

    public static bool Failed(int hResult) => hResult < 0;
}

public enum SimConnectTarget
{
    Msfs2020 = 2020,
    Msfs2024 = 2024,
}

public enum SimConnectReceiveId : uint
{
    Null = 0,
    Exception = 1,
    Open = 2,
    Quit = 3,
    Event = 4,
    EventObjectAddRemove = 5,
    EventFilename = 6,
    EventFrame = 7,
    SimObjectData = 8,
    SimObjectDataByType = 9,
    WeatherObservation = 10,
    CloudState = 11,
    AssignedObjectId = 12,
    ReservedKey = 13,
    CustomAction = 14,
    SystemState = 15,
}

#pragma warning disable CA1720 // These names are part of the documented native SimConnect ABI.
public enum SimConnectDataType : uint
{
    Invalid = 0,
    Int32 = 1,
    Int64 = 2,
    Float32 = 3,
    Float64 = 4,
    String8 = 5,
    String32 = 6,
    String64 = 7,
    String128 = 8,
    String256 = 9,
    String260 = 10,
    StringVariable = 11,
    InitPosition = 12,
    MarkerState = 13,
    Waypoint = 14,
    LatitudeLongitudeAltitude = 15,
    Cartesian = 16,
}
#pragma warning restore CA1720

public enum SimConnectPeriod : uint
{
    Never = 0,
    Once = 1,
    VisualFrame = 2,
    SimulationFrame = 3,
    Second = 4,
}

public enum SimConnectExceptionCode : uint
{
    None = 0,
    Error = 1,
    SizeMismatch = 2,
    UnrecognizedId = 3,
    Unopened = 4,
    VersionMismatch = 5,
}

[Flags]
public enum SimConnectCapabilities
{
    None = 0,
    NativeCoreApi = 1 << 0,
    CoreTelemetry = 1 << 1,
    SystemEvents = 1 << 2,
    LocalVariables = 1 << 3,
    Msfs2024ExtendedVariables = 1 << 4,
}
