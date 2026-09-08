using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Core.Flights;

public sealed record FlightPhaseEngineOptions
{
    public Speed TaxiSpeedThreshold { get; init; } = new(4);

    public Speed TakeoffRollSpeedThreshold { get; init; } = new(40);

    public VerticalSpeed ClimbRateThreshold { get; init; } = new(300);

    public VerticalSpeed DescentRateThreshold { get; init; } = new(-300);

    public VerticalSpeed CruiseVerticalSpeedTolerance { get; init; } = new(250);

    public Altitude CruiseMinimumAgl { get; init; } = new(8_000);

    public Altitude ApproachMaximumAgl { get; init; } = new(3_000);

    public TimeSpan GroundStateConfirmation { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan TaxiConfirmation { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan TakeoffRollConfirmation { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan AirborneConfirmation { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan CruiseConfirmation { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan FlightPathConfirmation { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan TouchdownConfirmation { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan OnBlockConfirmation { get; init; } = TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        if (DescentRateThreshold.FeetPerMinute >= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DescentRateThreshold),
                "The descent threshold must be negative.");
        }

        if (ClimbRateThreshold.FeetPerMinute <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ClimbRateThreshold),
                "The climb threshold must be positive.");
        }

        if (CruiseVerticalSpeedTolerance.FeetPerMinute < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CruiseVerticalSpeedTolerance),
                "The cruise tolerance cannot be negative.");
        }

        ValidateDuration(GroundStateConfirmation, nameof(GroundStateConfirmation));
        ValidateDuration(TaxiConfirmation, nameof(TaxiConfirmation));
        ValidateDuration(TakeoffRollConfirmation, nameof(TakeoffRollConfirmation));
        ValidateDuration(AirborneConfirmation, nameof(AirborneConfirmation));
        ValidateDuration(CruiseConfirmation, nameof(CruiseConfirmation));
        ValidateDuration(FlightPathConfirmation, nameof(FlightPathConfirmation));
        ValidateDuration(TouchdownConfirmation, nameof(TouchdownConfirmation));
        ValidateDuration(OnBlockConfirmation, nameof(OnBlockConfirmation));
    }

    private static void ValidateDuration(TimeSpan duration, string parameterName)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, duration, "Confirmation times cannot be negative.");
        }
    }
}
