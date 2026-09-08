using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Core.Flights;

public sealed class FlightPhaseEngine
{
    private readonly FlightPhaseEngineOptions options;
    private FlightPhase? candidatePhase;
    private DateTimeOffset candidateSinceUtc;
    private DateTimeOffset? lastObservationUtc;
    private VerticalSpeed? lastAirborneVerticalSpeed;

    public FlightPhaseEngine(
        FlightPhase initialPhase = FlightPhase.Ready,
        FlightPhaseEngineOptions? options = null)
    {
        if (!Enum.IsDefined(initialPhase))
        {
            throw new ArgumentOutOfRangeException(nameof(initialPhase));
        }

        this.options = options ?? new FlightPhaseEngineOptions();
        this.options.Validate();
        CurrentPhase = initialPhase;
    }

    public FlightPhase CurrentPhase { get; private set; }

    public FlightPhaseTransition? Observe(TelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureTimestampIsValid(snapshot.CollectedAtUtc);

        if (!snapshot.OnGround)
        {
            lastAirborneVerticalSpeed = snapshot.VerticalSpeed;
        }

        if (snapshot.IsPaused || snapshot.IsSlewActive || CurrentPhase == FlightPhase.Completed)
        {
            ResetCandidate();
            return null;
        }

        TransitionRule rule = DetermineRule(snapshot);
        if (rule.Phase == CurrentPhase)
        {
            ResetCandidate();
            return null;
        }

        if (candidatePhase != rule.Phase)
        {
            candidatePhase = rule.Phase;
            candidateSinceUtc = snapshot.CollectedAtUtc;
        }

        if (snapshot.CollectedAtUtc - candidateSinceUtc < rule.Confirmation)
        {
            return null;
        }

        FlightPhase previous = CurrentPhase;
        CurrentPhase = rule.Phase;
        ResetCandidate();

        VerticalSpeed? landingRate = CurrentPhase == FlightPhase.Landing
            ? lastAirborneVerticalSpeed
            : null;

        return new FlightPhaseTransition(previous, CurrentPhase, snapshot.CollectedAtUtc, landingRate);
    }

    public FlightPhaseTransition Complete(DateTimeOffset occurredAtUtc)
    {
        EnsureTimestampIsValid(occurredAtUtc);
        if (CurrentPhase != FlightPhase.OnBlock)
        {
            throw new InvalidOperationException("A flight can only be completed after reaching on-block.");
        }

        FlightPhase previous = CurrentPhase;
        CurrentPhase = FlightPhase.Completed;
        ResetCandidate();
        return new FlightPhaseTransition(previous, CurrentPhase, occurredAtUtc, null);
    }

    private TransitionRule DetermineRule(TelemetrySnapshot snapshot)
    {
        if (!snapshot.OnGround)
        {
            return DetermineAirborneRule(snapshot);
        }

        return DetermineGroundRule(snapshot);
    }

    private TransitionRule DetermineAirborneRule(TelemetrySnapshot snapshot)
    {
        if (CurrentPhase is FlightPhase.Ready
            or FlightPhase.Boarding
            or FlightPhase.Pushback
            or FlightPhase.TaxiOut
            or FlightPhase.Takeoff
            or FlightPhase.Landing
            or FlightPhase.TaxiIn
            or FlightPhase.OnBlock)
        {
            return new(FlightPhase.Climb, options.AirborneConfirmation);
        }

        double verticalSpeed = snapshot.VerticalSpeed.FeetPerMinute;
        double? agl = snapshot.AltitudeAgl?.Feet;

        if (agl is { } approachAgl
            && approachAgl <= options.ApproachMaximumAgl.Feet
            && verticalSpeed <= options.DescentRateThreshold.FeetPerMinute)
        {
            return new(FlightPhase.Approach, options.FlightPathConfirmation);
        }

        if (verticalSpeed <= options.DescentRateThreshold.FeetPerMinute)
        {
            return new(FlightPhase.Descent, options.FlightPathConfirmation);
        }

        if (verticalSpeed >= options.ClimbRateThreshold.FeetPerMinute)
        {
            return new(FlightPhase.Climb, options.FlightPathConfirmation);
        }

        if (agl is { } cruiseAgl
            && cruiseAgl >= options.CruiseMinimumAgl.Feet
            && Math.Abs(verticalSpeed) <= options.CruiseVerticalSpeedTolerance.FeetPerMinute)
        {
            return new(FlightPhase.Cruise, options.CruiseConfirmation);
        }

        return new(CurrentPhase, TimeSpan.Zero);
    }

    private TransitionRule DetermineGroundRule(TelemetrySnapshot snapshot)
    {
        if (CurrentPhase is FlightPhase.Climb
            or FlightPhase.Cruise
            or FlightPhase.Descent
            or FlightPhase.Approach)
        {
            return new(FlightPhase.Landing, options.TouchdownConfirmation);
        }

        bool moving = snapshot.GroundSpeed.Knots >= options.TaxiSpeedThreshold.Knots;
        bool takeoffRoll = snapshot.GroundSpeed.Knots >= options.TakeoffRollSpeedThreshold.Knots;
        bool enginesRunning = snapshot.RunningEngineCount > 0;
        bool secured = !moving
            && (snapshot.Systems.ParkingBrakeSet == true || !enginesRunning);

        return CurrentPhase switch
        {
            FlightPhase.Ready when secured => new(FlightPhase.Boarding, options.GroundStateConfirmation),
            FlightPhase.Ready when moving => new(FlightPhase.TaxiOut, options.TaxiConfirmation),
            FlightPhase.Ready when enginesRunning && snapshot.Systems.ParkingBrakeSet != true =>
                new(FlightPhase.Pushback, options.GroundStateConfirmation),
            FlightPhase.Boarding when moving => new(FlightPhase.TaxiOut, options.TaxiConfirmation),
            FlightPhase.Boarding when enginesRunning && snapshot.Systems.ParkingBrakeSet != true =>
                new(FlightPhase.Pushback, options.GroundStateConfirmation),
            FlightPhase.Pushback when moving => new(FlightPhase.TaxiOut, options.TaxiConfirmation),
            FlightPhase.TaxiOut when takeoffRoll => new(FlightPhase.Takeoff, options.TakeoffRollConfirmation),
            FlightPhase.Takeoff when !takeoffRoll => new(FlightPhase.TaxiOut, options.TakeoffRollConfirmation),
            FlightPhase.Landing when moving => new(FlightPhase.TaxiIn, options.TaxiConfirmation),
            FlightPhase.Landing when secured => new(FlightPhase.OnBlock, options.OnBlockConfirmation),
            FlightPhase.TaxiIn when secured => new(FlightPhase.OnBlock, options.OnBlockConfirmation),
            _ => new(CurrentPhase, TimeSpan.Zero),
        };
    }

    private void EnsureTimestampIsValid(DateTimeOffset timestamp)
    {
        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Telemetry timestamps must use UTC.", nameof(timestamp));
        }

        if (lastObservationUtc is { } lastTimestamp && lastTimestamp > timestamp)
        {
            throw new ArgumentException("Telemetry timestamps cannot move backwards.", nameof(timestamp));
        }

        lastObservationUtc = timestamp;
    }

    private void ResetCandidate()
    {
        candidatePhase = null;
        candidateSinceUtc = default;
    }

    private readonly record struct TransitionRule(FlightPhase Phase, TimeSpan Confirmation);
}
