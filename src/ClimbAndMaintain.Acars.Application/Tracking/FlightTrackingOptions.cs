namespace ClimbAndMaintain.Acars.Application.Tracking;

public sealed record FlightTrackingOptions
{
    public TimeSpan PositionReportInterval { get; init; } = TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        if (PositionReportInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PositionReportInterval),
                PositionReportInterval,
                "The position-report interval must be positive.");
        }
    }
}
