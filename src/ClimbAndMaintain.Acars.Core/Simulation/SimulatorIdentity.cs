namespace ClimbAndMaintain.Acars.Core.Simulation;

public enum SimulatorKind
{
    Unknown,
    Msfs2020,
    Msfs2024,
    RecordedTelemetry,
}

public enum SimulatorConnectionState
{
    Unavailable,
    Disconnected,
    Connecting,
    Connected,
    AircraftLoading,
    Ready,
    Tracking,
    Recovering,
    Faulted,
}

public sealed record SimulatorIdentity
{
    public static SimulatorIdentity Unknown { get; } = new(SimulatorKind.Unknown, "Unknown simulator", null);

    public SimulatorIdentity(SimulatorKind kind, string displayName, string? version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Kind = kind;
        DisplayName = displayName.Trim();
        Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
    }

    public SimulatorKind Kind { get; }

    public string DisplayName { get; }

    public string? Version { get; }
}
