using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Application.Contracts;

/// <summary>
/// Optional boundary for telemetry supplied by a licensed PMDG SDK integration.
/// The generic SimConnect provider remains authoritative when this extension is unavailable.
/// </summary>
public interface IPmdgEnhancedTelemetryProvider
{
    bool IsAvailable { get; }

    ValueTask<TelemetrySnapshot> EnrichAsync(
        TelemetrySnapshot standardTelemetry,
        CancellationToken cancellationToken);
}
