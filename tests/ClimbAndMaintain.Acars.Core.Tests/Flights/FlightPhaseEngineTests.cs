using System.Collections.Immutable;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Core.Tests.Flights;

public sealed class FlightPhaseEngineTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ObserveRequiresSustainedEvidenceAndResetsAfterAFlicker()
    {
        FlightPhaseEngine engine = new(FlightPhase.Boarding, FastOptions());

        Assert.Null(engine.Observe(Snapshot(0, groundSpeedKnots: 6)));
        Assert.Null(engine.Observe(Snapshot(1, groundSpeedKnots: 6)));
        Assert.Null(engine.Observe(Snapshot(2, groundSpeedKnots: 0)));
        Assert.Null(engine.Observe(Snapshot(3, groundSpeedKnots: 6)));
        Assert.Null(engine.Observe(Snapshot(5, groundSpeedKnots: 6)));

        FlightPhaseTransition transition = Assert.IsType<FlightPhaseTransition>(
            engine.Observe(Snapshot(6, groundSpeedKnots: 6)));

        Assert.Equal(FlightPhase.Boarding, transition.From);
        Assert.Equal(FlightPhase.TaxiOut, transition.To);
    }

    [Fact]
    public void ObserveTracksACompleteFlightWithoutOscillating()
    {
        FlightPhaseEngine engine = new(FlightPhase.TaxiOut, FastOptions());

        ObserveUntil(engine, FlightPhase.Takeoff, Snapshot(0, groundSpeedKnots: 60), Snapshot(2, groundSpeedKnots: 65));
        ObserveUntil(engine, FlightPhase.Climb, Snapshot(3, false, 140, 1_200, 500), Snapshot(5, false, 150, 1_000, 2_500));
        ObserveUntil(engine, FlightPhase.Cruise, Snapshot(6, false, 430, 0, 10_000), Snapshot(9, false, 430, 50, 10_000));
        ObserveUntil(engine, FlightPhase.Descent, Snapshot(10, false, 300, -1_000, 5_000), Snapshot(12, false, 280, -900, 4_000));
        ObserveUntil(engine, FlightPhase.Approach, Snapshot(13, false, 200, -700, 2_500), Snapshot(15, false, 180, -600, 1_500));

        Assert.Null(engine.Observe(Snapshot(16, false, 145, -450, 100)));
        Assert.Null(engine.Observe(Snapshot(17, true, 80, 0, 0)));
        FlightPhaseTransition landing = Assert.IsType<FlightPhaseTransition>(
            engine.Observe(Snapshot(19, true, 65, 0, 0)));

        Assert.Equal(FlightPhase.Landing, landing.To);
        Assert.Equal(-450d, landing.LandingRate!.Value.FeetPerMinute);

        ObserveUntil(engine, FlightPhase.TaxiIn, Snapshot(20, groundSpeedKnots: 15), Snapshot(23, groundSpeedKnots: 12));
        ObserveUntil(
            engine,
            FlightPhase.OnBlock,
            Snapshot(24, groundSpeedKnots: 0, parkingBrakeSet: true, engineRunning: false),
            Snapshot(27, groundSpeedKnots: 0, parkingBrakeSet: true, engineRunning: false));

        FlightPhaseTransition completed = engine.Complete(Start.AddSeconds(28));

        Assert.Equal(FlightPhase.OnBlock, completed.From);
        Assert.Equal(FlightPhase.Completed, completed.To);
        Assert.Equal(FlightPhase.Completed, engine.CurrentPhase);
    }

    [Fact]
    public void ObservePausedTelemetryDoesNotAdvanceACandidate()
    {
        FlightPhaseEngine engine = new(FlightPhase.Boarding, FastOptions());

        Assert.Null(engine.Observe(Snapshot(0, groundSpeedKnots: 10)));
        Assert.Null(engine.Observe(Snapshot(4, groundSpeedKnots: 10, paused: true)));
        Assert.Null(engine.Observe(Snapshot(5, groundSpeedKnots: 10)));
        Assert.Null(engine.Observe(Snapshot(7, groundSpeedKnots: 10)));

        FlightPhaseTransition transition = Assert.IsType<FlightPhaseTransition>(
            engine.Observe(Snapshot(8, groundSpeedKnots: 10)));
        Assert.Equal(FlightPhase.TaxiOut, transition.To);
    }

    [Fact]
    public void ObserveSlewTelemetryDoesNotAdvanceACandidate()
    {
        FlightPhaseEngine engine = new(FlightPhase.Boarding, FastOptions());

        Assert.Null(engine.Observe(Snapshot(0, groundSpeedKnots: 10)));
        Assert.Null(engine.Observe(Snapshot(4, groundSpeedKnots: 100, slew: true)));
        Assert.Null(engine.Observe(Snapshot(5, groundSpeedKnots: 10)));
        Assert.Null(engine.Observe(Snapshot(7, groundSpeedKnots: 10)));

        FlightPhaseTransition transition = Assert.IsType<FlightPhaseTransition>(
            engine.Observe(Snapshot(8, groundSpeedKnots: 10)));
        Assert.Equal(FlightPhase.TaxiOut, transition.To);
    }

    [Fact]
    public void ObserveRejectsTelemetryThatMovesBackwardsInTime()
    {
        FlightPhaseEngine engine = new();
        engine.Observe(Snapshot(2));

        Assert.Throws<ArgumentException>(() => engine.Observe(Snapshot(1)));
    }

    [Fact]
    public void CompleteRequiresOnBlockPhase()
    {
        FlightPhaseEngine engine = new(FlightPhase.TaxiIn);

        Assert.Throws<InvalidOperationException>(() => engine.Complete(Start));
    }

    private static void ObserveUntil(
        FlightPhaseEngine engine,
        FlightPhase expected,
        TelemetrySnapshot first,
        TelemetrySnapshot confirmed)
    {
        Assert.Null(engine.Observe(first));
        FlightPhaseTransition transition = Assert.IsType<FlightPhaseTransition>(engine.Observe(confirmed));
        Assert.Equal(expected, transition.To);
    }

    private static FlightPhaseEngineOptions FastOptions() => new()
    {
        GroundStateConfirmation = TimeSpan.FromSeconds(1),
        TaxiConfirmation = TimeSpan.FromSeconds(3),
        TakeoffRollConfirmation = TimeSpan.FromSeconds(2),
        AirborneConfirmation = TimeSpan.FromSeconds(2),
        CruiseConfirmation = TimeSpan.FromSeconds(3),
        FlightPathConfirmation = TimeSpan.FromSeconds(2),
        TouchdownConfirmation = TimeSpan.FromSeconds(2),
        OnBlockConfirmation = TimeSpan.FromSeconds(3),
    };

    private static TelemetrySnapshot Snapshot(
        double seconds,
        bool onGround = true,
        double groundSpeedKnots = 0,
        double verticalSpeedFeetPerMinute = 0,
        double altitudeAglFeet = 0,
        bool parkingBrakeSet = false,
        bool engineRunning = true,
        bool paused = false,
        bool slew = false) => new()
        {
            CollectedAtUtc = Start.AddSeconds(seconds),
            Position = new GeoPosition(47.4502, -122.3088),
            AltitudeMsl = new Altitude(433 + altitudeAglFeet),
            AltitudeAgl = new Altitude(altitudeAglFeet),
            IndicatedAirspeed = new Speed(groundSpeedKnots),
            GroundSpeed = new Speed(groundSpeedKnots),
            VerticalSpeed = new VerticalSpeed(verticalSpeedFeetPerMinute),
            OnGround = onGround,
            Engines = ImmutableArray.Create(
                new EngineTelemetry(
                    1,
                    engineRunning ? EngineOperatingState.Running : EngineOperatingState.Off)),
            Systems = new AircraftSystemsTelemetry { ParkingBrakeSet = parkingBrakeSet },
            IsPaused = paused,
            IsSlewActive = slew,
        };
}
