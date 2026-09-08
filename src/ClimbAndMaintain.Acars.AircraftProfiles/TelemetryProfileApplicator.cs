using System.Collections.Immutable;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;
using ProfileAircraftIdentity = ClimbAndMaintain.Acars.AircraftProfiles.AircraftIdentity;
using TelemetryAircraftIdentity = ClimbAndMaintain.Acars.Core.Telemetry.AircraftIdentity;

namespace ClimbAndMaintain.Acars.AircraftProfiles;

public static class TelemetryProfileApplicator
{
    public static ProfileApplicationResult Apply(
        IEnumerable<LoadedAircraftProfile> profiles,
        TelemetrySnapshot snapshot,
        IEnumerable<RawVariableValue> rawValues)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(rawValues);

        SimulatorEdition simulator = snapshot.Simulator.Kind switch
        {
            SimulatorKind.Msfs2020 => SimulatorEdition.Msfs2020,
            SimulatorKind.Msfs2024 => SimulatorEdition.Msfs2024,
            _ => throw new ArgumentException("Aircraft profiles can only be applied to an MSFS telemetry snapshot.", nameof(snapshot)),
        };
        ProfileSelection selection = ProfileEngine.Select(
            profiles,
            new ProfileAircraftIdentity(
                snapshot.Aircraft.Title,
                snapshot.Aircraft.ConfigurationPath ?? string.Empty,
                simulator));
        IReadOnlyDictionary<string, InterpretedFeature> interpreted = ProfileEngine.Interpret(selection, rawValues);

        AircraftSystemsTelemetry systems = snapshot.Systems;
        AircraftLightsTelemetry lights = systems.Lights;
        bool? doorsClosed = BooleanOverride(interpreted, "doorsClosed", null);
        systems = systems with
        {
            ParkingBrakeSet = BooleanOverride(interpreted, "parkingBrake", systems.ParkingBrakeSet),
            AutopilotEngaged = BooleanOverride(interpreted, "autopilot", systems.AutopilotEngaged),
            BatteryOn = BooleanOverride(interpreted, "battery", systems.BatteryOn),
            ExternalPowerOn = BooleanOverride(interpreted, "externalPower", systems.ExternalPowerOn),
            ApuRunning = BooleanOverride(interpreted, "apu", systems.ApuRunning),
            AntiIceOn = BooleanOverride(interpreted, "antiIce", systems.AntiIceOn),
            SeatBeltSignOn = BooleanOverride(interpreted, "seatBelts", systems.SeatBeltSignOn),
            PacksOn = BooleanOverride(interpreted, "packs", systems.PacksOn),
            EmergencyLightsOn = BooleanOverride(
                interpreted,
                "emergencyLights",
                systems.EmergencyLightsOn),
            Flaps = ApplyFlapLabel(systems.Flaps, selection),
            Lights = lights with
            {
                Beacon = BooleanOverride(interpreted, "beacon", lights.Beacon),
                Navigation = BooleanOverride(interpreted, "navigationLights", lights.Navigation),
                Strobe = BooleanOverride(interpreted, "strobeLights", lights.Strobe),
                Landing = BooleanOverride(interpreted, "landingLights", lights.Landing),
                Taxi = BooleanOverride(interpreted, "taxiLights", lights.Taxi),
                Wing = BooleanOverride(interpreted, "wingLights", lights.Wing),
                Logo = BooleanOverride(interpreted, "logoLights", lights.Logo),
            },
        };

        string profileId = selection.Match?.Profile.Meta.Id ?? selection.Universal.Profile.Meta.Id;
        TelemetrySnapshot applied = snapshot with
        {
            Aircraft = new TelemetryAircraftIdentity(
                snapshot.Aircraft.Title,
                snapshot.Aircraft.IcaoType,
                snapshot.Aircraft.ConfigurationPath,
                profileId),
            Systems = systems,
            Doors = doorsClosed is { } closed
                ? ImmutableArray.Create(new DoorTelemetry(
                    "Cabin exits",
                    closed ? DoorPosition.Closed : DoorPosition.Open))
                : snapshot.Doors,
        };
        return new(applied, selection, interpreted);
    }

    private static bool? BooleanOverride(
        IReadOnlyDictionary<string, InterpretedFeature> interpreted,
        string key,
        bool? fallback) => interpreted.TryGetValue(key, out InterpretedFeature? value)
                                  && value.IsAvailable
                                  && value.BooleanValue is { } boolean
        ? boolean
        : fallback;

    private static FlapTelemetry? ApplyFlapLabel(
        FlapTelemetry? flaps,
        ProfileSelection selection)
    {
        if (flaps?.Index is not { } index)
        {
            return flaps;
        }

        IReadOnlyDictionary<int, string>? labels = selection.Match?.Profile.FlapLabels
            ?? selection.Universal.Profile.FlapLabels;
        return labels is not null && labels.TryGetValue(index, out string? label)
            ? new FlapTelemetry(flaps.Index, flaps.Extension, label)
            : flaps;
    }
}

public sealed record ProfileApplicationResult(
    TelemetrySnapshot Snapshot,
    ProfileSelection Selection,
    IReadOnlyDictionary<string, InterpretedFeature> InterpretedFeatures);
