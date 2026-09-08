namespace ClimbAndMaintain.Acars.Core.Telemetry;

internal static class MeasurementGuard
{
    public static double Finite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite.");
        }

        return value;
    }

    public static double NonNegative(double value, string parameterName)
    {
        Finite(value, parameterName);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value cannot be negative.");
        }

        return value;
    }
}
