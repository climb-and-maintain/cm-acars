namespace ClimbAndMaintain.Acars.Core.Telemetry;

public readonly record struct Altitude
{
    public Altitude(double feet)
    {
        Feet = MeasurementGuard.Finite(feet, nameof(feet));
    }

    public double Feet { get; }
}

public readonly record struct Distance
{
    public Distance(double nauticalMiles)
    {
        NauticalMiles = MeasurementGuard.NonNegative(nauticalMiles, nameof(nauticalMiles));
    }

    public double NauticalMiles { get; }
}

public readonly record struct Speed
{
    public Speed(double knots)
    {
        Knots = MeasurementGuard.NonNegative(knots, nameof(knots));
    }

    public double Knots { get; }
}

public readonly record struct VerticalSpeed
{
    public VerticalSpeed(double feetPerMinute)
    {
        FeetPerMinute = MeasurementGuard.Finite(feetPerMinute, nameof(feetPerMinute));
    }

    public double FeetPerMinute { get; }
}

public readonly record struct Heading
{
    public Heading(double degrees)
    {
        MeasurementGuard.Finite(degrees, nameof(degrees));
        Degrees = ((degrees % 360) + 360) % 360;
    }

    public double Degrees { get; }
}

public readonly record struct FuelMass
{
    public FuelMass(double kilograms)
    {
        Kilograms = MeasurementGuard.NonNegative(kilograms, nameof(kilograms));
    }

    public double Kilograms { get; }
}

public readonly record struct FuelFlow
{
    public FuelFlow(double kilogramsPerHour)
    {
        KilogramsPerHour = MeasurementGuard.NonNegative(kilogramsPerHour, nameof(kilogramsPerHour));
    }

    public double KilogramsPerHour { get; }
}

public readonly record struct Ratio
{
    public Ratio(double value)
    {
        MeasurementGuard.Finite(value, nameof(value));
        if (value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The ratio must be between zero and one.");
        }

        Value = value;
    }

    public double Value { get; }

    public double Percent => Value * 100;
}

public readonly record struct GeoPosition
{
    public GeoPosition(double latitudeDegrees, double longitudeDegrees)
    {
        MeasurementGuard.Finite(latitudeDegrees, nameof(latitudeDegrees));
        MeasurementGuard.Finite(longitudeDegrees, nameof(longitudeDegrees));

        if (latitudeDegrees is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(latitudeDegrees),
                latitudeDegrees,
                "Latitude must be between -90 and 90 degrees.");
        }

        if (longitudeDegrees is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longitudeDegrees),
                longitudeDegrees,
                "Longitude must be between -180 and 180 degrees.");
        }

        LatitudeDegrees = latitudeDegrees;
        LongitudeDegrees = longitudeDegrees;
    }

    public double LatitudeDegrees { get; }

    public double LongitudeDegrees { get; }
}
