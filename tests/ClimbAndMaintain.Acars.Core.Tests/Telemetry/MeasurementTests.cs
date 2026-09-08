using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Core.Tests.Telemetry;

public sealed class MeasurementTests
{
    [Theory]
    [InlineData(370, 10)]
    [InlineData(-10, 350)]
    [InlineData(720, 0)]
    public void HeadingNormalizesDegrees(double input, double expected)
    {
        Heading heading = new(input);

        Assert.Equal(expected, heading.Degrees);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void MeasurementsRejectNonFiniteValues(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Altitude(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Speed(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalSpeed(value));
    }

    [Fact]
    public void GeoPositionRejectsCoordinatesOutsideTheGlobe()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPosition(90.1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPosition(0, -180.1));
    }

    [Theory]
    [InlineData("1200")]
    [InlineData("7700")]
    [InlineData(null)]
    public void TransponderTelemetryAcceptsValidCodes(string? code)
    {
        TransponderTelemetry transponder = new(code, TransponderMode.Altitude);

        Assert.Equal(code, transponder.Code);
    }

    [Theory]
    [InlineData("8000")]
    [InlineData("123")]
    [InlineData("12A0")]
    public void TransponderTelemetryRejectsInvalidCodes(string code)
    {
        Assert.Throws<ArgumentException>(() => new TransponderTelemetry(code, TransponderMode.On));
    }
}
