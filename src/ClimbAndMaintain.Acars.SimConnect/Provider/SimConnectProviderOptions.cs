using ClimbAndMaintain.Acars.SimConnect.Variables;

namespace ClimbAndMaintain.Acars.SimConnect.Provider;

public sealed record SimConnectProviderOptions
{
    public required SimConnectTarget Target { get; init; }

    public string? LibraryPath { get; init; }

    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan RecoveryInitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan RecoveryMaximumDelay { get; init; } = TimeSpan.FromSeconds(30);

    public IReadOnlyList<SimConnectReadOnlyVariableDefinition> ReadOnlyVariables { get; init; } = [];
}
