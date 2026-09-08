namespace ClimbAndMaintain.Acars.Core.Telemetry;

public sealed record AircraftIdentity
{
    public static AircraftIdentity Unknown { get; } = new("Unknown aircraft", null, null, null);

    public AircraftIdentity(string title, string? icaoType, string? configurationPath, string? profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title.Trim();
        IcaoType = Normalize(icaoType);
        ConfigurationPath = Normalize(configurationPath);
        ProfileId = Normalize(profileId);
    }

    public string Title { get; }

    public string? IcaoType { get; }

    public string? ConfigurationPath { get; }

    public string? ProfileId { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
