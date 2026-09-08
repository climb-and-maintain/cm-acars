using ClimbAndMaintain.Acars.SimConnect.Configuration;

namespace ClimbAndMaintain.Acars.SimConnect.Validation;

public static class SimConnectCompatibility
{
    public static uint ExpectedApplicationMajor(SimConnectTarget target) => target switch
    {
        SimConnectTarget.Msfs2020 => 11,
        SimConnectTarget.Msfs2024 => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported simulator target."),
    };

    public static bool IsExpectedSimulator(SimConnectTarget target, SimConnectOpenInfo information)
    {
        ArgumentNullException.ThrowIfNull(information);
        return information.ApplicationVersionMajor == ExpectedApplicationMajor(target);
    }
}
