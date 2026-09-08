using System.Text.Json;
using ClimbAndMaintain.Acars.Core.Simulation;
using ClimbAndMaintain.Acars.Core.Telemetry;
using Xunit;

namespace ClimbAndMaintain.Acars.AircraftProfiles.Tests;

public sealed class ProfileEngineTests
{
    [Fact]
    public void SelectAircraftOverrideRetainsUniversalFallback()
    {
        var universal = Loaded(
            ProfileSource.BundledUniversal,
            Profile("universal", 0, ["msfs"], [], new Dictionary<string, TelemetryRule>
            {
                ["beacon"] = new(VariableKind.SimVar, "LIGHT BEACON"),
                ["fuel"] = new(VariableKind.SimVar, "FUEL TOTAL QUANTITY", "gallons"),
            }));
        var fenix = Loaded(
            ProfileSource.ClimbAndMaintain,
            Profile("fenix", 100, ["msfs20", "msfs24"],
                [new(MatchScope.Title, RuleOperator.Contains, "Fenix")],
                new Dictionary<string, TelemetryRule>
                {
                    ["beacon"] = new(VariableKind.LVar, "L:S_OH_EXT_LT_BEACON", null, RuleOperator.Equals, 1),
                }));

        var result = ProfileEngine.Select([universal, fenix], new("Fenix A320", "fnx320/aircraft.cfg", SimulatorEdition.Msfs2020));

        Assert.Equal("fenix", result.Match?.Profile.Meta.Id);
        Assert.Equal(VariableKind.LVar, result.EffectiveMappings["beacon"].Kind);
        Assert.Equal("FUEL TOTAL QUANTITY", result.EffectiveMappings["fuel"].Name);
    }

    [Fact]
    public void SelectDoesNotUseProfileValidatedForOtherSimulator()
    {
        var universal = Loaded(ProfileSource.BundledUniversal, Profile("universal", 0, ["msfs"], [], EmptyMappings()));
        var only2020 = Loaded(ProfileSource.User, Profile("only20", 500, ["msfs20"],
            [new(MatchScope.Title, RuleOperator.Contains, "Example")], EmptyMappings()));

        var result = ProfileEngine.Select([universal, only2020], new("Example", "", SimulatorEdition.Msfs2024));

        Assert.Null(result.Match);
    }

    [Fact]
    public void InterpretFenixFixture()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fenix-a319-a320-a321.json");
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        JsonElement aircraft = fixture.RootElement.GetProperty("aircraft");
        string? simulatorName = aircraft.GetProperty("simulator").GetString();
        SimulatorEdition simulator = simulatorName switch
        {
            "msfs20" => SimulatorEdition.Msfs2020,
            "msfs24" => SimulatorEdition.Msfs2024,
            _ => throw new InvalidDataException($"Unsupported fixture simulator '{simulatorName}'."),
        };
        var catalog = new ProfileCatalog();
        catalog.LoadBundled();
        var selection = ProfileEngine.Select(
            catalog.Profiles,
            new(
                aircraft.GetProperty("title").GetString()!,
                aircraft.GetProperty("configurationPath").GetString()!,
                simulator));

        foreach (JsonElement testCase in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            RawVariableValue[] values = testCase.GetProperty("values")
                .EnumerateObject()
                .Select(property => new RawVariableValue(VariableKind.LVar, property.Name, property.Value.GetDouble()))
                .ToArray();
            IReadOnlyDictionary<string, InterpretedFeature> result = ProfileEngine.Interpret(selection, values);

            foreach (JsonProperty expected in testCase.GetProperty("expected").EnumerateObject())
            {
                Assert.True(result[expected.Name].IsAvailable);
                Assert.Equal(expected.Value.GetBoolean(), result[expected.Name].BooleanValue);
            }
        }
    }

    [Fact]
    public void InterpretPinnedPmdgLightFixtures()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "pmdg-lights.json");
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var catalog = new ProfileCatalog();
        catalog.LoadBundled();

        foreach (JsonElement testCase in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            SimulatorEdition simulator = testCase.GetProperty("simulator").GetString() switch
            {
                "msfs20" => SimulatorEdition.Msfs2020,
                "msfs24" => SimulatorEdition.Msfs2024,
                _ => throw new InvalidDataException("The fixture contains an unsupported simulator."),
            };
            ProfileSelection selection = ProfileEngine.Select(
                catalog.Profiles,
                new(
                    testCase.GetProperty("title").GetString()!,
                    testCase.GetProperty("configurationPath").GetString()!,
                    simulator));
            RawVariableValue[] values = testCase.GetProperty("values")
                .EnumerateObject()
                .Select(property => new RawVariableValue(
                    VariableKind.LVar,
                    property.Name,
                    property.Value.GetDouble()))
                .ToArray();
            IReadOnlyDictionary<string, InterpretedFeature> interpreted =
                ProfileEngine.Interpret(selection, values);

            Assert.Equal(testCase.GetProperty("profileId").GetString(), selection.Match?.Profile.Meta.Id);
            foreach (JsonProperty expected in testCase.GetProperty("expected").EnumerateObject())
            {
                Assert.True(interpreted[expected.Name].IsAvailable);
                Assert.Equal(expected.Value.GetBoolean(), interpreted[expected.Name].BooleanValue);
            }
        }
    }

    [Fact]
    public void BundledFenixProfileRetainsPinnedProvenanceAndSafeReadOnlyMappings()
    {
        var catalog = new ProfileCatalog();
        catalog.LoadBundled();

        LoadedAircraftProfile fenix = Assert.Single(
            catalog.Profiles,
            profile => profile.Profile.Meta.Id == "cm.fenix.a319-a320-a321");

        Assert.Equal("B.Fatih KOZ <https://github.com/FatihKoz>", fenix.Profile.Meta.Author);
        Assert.Equal("BSD-2-Clause", fenix.Profile.Meta.License);
        Assert.Equal(
            "https://github.com/phpvms/acars-config/blob/20a81525037fcf920f158b4c063b48f33734a502/src/aircraft/fenix-a320.json",
            fenix.Profile.Meta.SourceUrl);
        Assert.Equal("20a81525037fcf920f158b4c063b48f33734a502", fenix.Profile.Meta.SourceRevision);
        Assert.Equal(
            ["beacon", "logoLights", "navigationLights", "seatBelts", "taxiLights", "wingLights"],
            fenix.Profile.Mappings.Keys.Order(StringComparer.Ordinal));
        Assert.All(fenix.Profile.Mappings.Values, rule => Assert.Equal(VariableKind.LVar, rule.Kind));
        Assert.All(fenix.Profile.Mappings.Values, rule => Assert.StartsWith("L:", rule.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsExecutableLookingVariable()
    {
        var profile = Profile("unsafe", 0, ["msfs"], [], new Dictionary<string, TelemetryRule>
        {
            ["beacon"] = new(VariableKind.LVar, "powershell -Command bad"),
        });

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("prohibited", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsExecutableLookingVariableInsideCompoundRule()
    {
        AircraftProfile profile = Profile("unsafe-compound", 1, ["msfs"],
            [new(MatchScope.Title, RuleOperator.Contains, "Example")], EmptyMappings()) with
        {
            CompoundMappings = new Dictionary<string, CompositeTelemetryRule>
            {
                ["beacon"] = new(
                    [
                        new(
                            [new TelemetryConditionSet(
                                [new TelemetryCondition(VariableKind.LVar, "L:SAFE;cmd.exe")])],
                            true),
                    ]),
            },
        };

        ProfileValidationResult result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("unsafe variable", StringComparison.Ordinal));
    }

    [Fact]
    public void VariableEnumerationIncludesEveryCompoundConditionWithoutWrites()
    {
        var catalog = new ProfileCatalog();
        catalog.LoadBundled();
        AircraftProfile fenix = Assert.Single(
            catalog.Profiles,
            profile => profile.Profile.Meta.Id == "cm.fenix.a319-a320-a321").Profile;

        TelemetryRule[] variables = [.. ProfileEngine.EnumerateVariables(fenix)];

        Assert.Contains(variables, rule => rule.Name == "L:S_OH_EXT_LT_LANDING_L");
        Assert.Contains(variables, rule => rule.Name == "L:I_OH_ELEC_APU_START_U");
        Assert.All(variables, rule => Assert.Equal(VariableKind.LVar, rule.Kind));
    }

    [Fact]
    public void CatalogRejectsDuplicateIds()
    {
        var catalog = new ProfileCatalog();
        catalog.Add(Profile("same", 0, ["msfs"], [], EmptyMappings()), ProfileSource.User, "one.json");

        Assert.Throws<InvalidDataException>(() =>
            catalog.Add(Profile("same", 1, ["msfs"], [], EmptyMappings()), ProfileSource.User, "two.json"));
    }

    [Fact]
    public void ApplicatorUsesMatchedReadOnlyOverrideAndStampsProfileIdentity()
    {
        LoadedAircraftProfile universal = Loaded(
            ProfileSource.BundledUniversal,
            Profile("universal", 0, ["msfs"], [], new Dictionary<string, TelemetryRule>
            {
                ["beacon"] = new(VariableKind.SimVar, "LIGHT BEACON"),
            }));
        LoadedAircraftProfile fenix = Loaded(
            ProfileSource.ClimbAndMaintain,
            Profile("fenix", 100, ["msfs20"],
                [new(MatchScope.Title, RuleOperator.Contains, "Fenix")],
                new Dictionary<string, TelemetryRule>
                {
                    ["beacon"] = new(VariableKind.LVar, "L:S_OH_EXT_LT_BEACON", null, RuleOperator.Equals, 1),
                }));
        TelemetrySnapshot snapshot = Snapshot("Fenix A320", SimulatorKind.Msfs2020) with
        {
            Systems = new AircraftSystemsTelemetry
            {
                Lights = new AircraftLightsTelemetry { Beacon = false },
            },
        };

        ProfileApplicationResult result = TelemetryProfileApplicator.Apply(
            [universal, fenix],
            snapshot,
            [new RawVariableValue(VariableKind.LVar, "L:S_OH_EXT_LT_BEACON", 1)]);

        Assert.Equal("fenix", result.Snapshot.Aircraft.ProfileId);
        Assert.True(result.Snapshot.Systems.Lights.Beacon);
        Assert.Equal(ProfileSource.ClimbAndMaintain, result.Selection.Match?.Source);
    }

    [Fact]
    public void ApplicatorAppliesBundledFenixLightsAndSeatBeltMappings()
    {
        var catalog = new ProfileCatalog();
        catalog.LoadBundled();
        TelemetrySnapshot snapshot = Snapshot("Fenix A320", SimulatorKind.Msfs2020) with
        {
            Systems = new AircraftSystemsTelemetry
            {
                SeatBeltSignOn = false,
                Lights = new AircraftLightsTelemetry
                {
                    Beacon = false,
                    Logo = false,
                    Navigation = false,
                    Taxi = false,
                    Wing = false,
                },
            },
        };

        ProfileApplicationResult result = TelemetryProfileApplicator.Apply(
            catalog.Profiles,
            snapshot,
            [
                new(VariableKind.LVar, "L:S_OH_EXT_LT_BEACON", 1),
                new(VariableKind.LVar, "L:S_OH_EXT_LT_NAV_LOGO", 2),
                new(VariableKind.LVar, "L:S_OH_SIGNS", 1),
                new(VariableKind.LVar, "L:S_OH_EXT_LT_NOSE", 1),
                new(VariableKind.LVar, "L:S_OH_EXT_LT_WING", 1),
            ]);

        Assert.Equal("cm.fenix.a319-a320-a321", result.Snapshot.Aircraft.ProfileId);
        Assert.True(result.Snapshot.Systems.SeatBeltSignOn);
        Assert.True(result.Snapshot.Systems.Lights.Beacon);
        Assert.True(result.Snapshot.Systems.Lights.Logo);
        Assert.True(result.Snapshot.Systems.Lights.Navigation);
        Assert.True(result.Snapshot.Systems.Lights.Taxi);
        Assert.True(result.Snapshot.Systems.Lights.Wing);
    }

    [Fact]
    public void ApplicatorAppliesBundledFenixCompoundRulesAndFlapLabels()
    {
        var catalog = new ProfileCatalog();
        catalog.LoadBundled();
        TelemetrySnapshot snapshot = Snapshot("Fenix A320", SimulatorKind.Msfs2024) with
        {
            Systems = new AircraftSystemsTelemetry
            {
                Flaps = new FlapTelemetry(2, new Ratio(0.4)),
                Lights = new AircraftLightsTelemetry
                {
                    Landing = false,
                    Strobe = false,
                },
            },
        };

        ProfileApplicationResult result = TelemetryProfileApplicator.Apply(
            catalog.Profiles,
            snapshot,
            [
                new(VariableKind.LVar, "L:S_OH_EXT_LT_LANDING_L", 2),
                new(VariableKind.LVar, "L:S_OH_EXT_LT_LANDING_R", 2),
                new(VariableKind.LVar, "L:S_OH_EXT_LT_STROBE", 2),
                new(VariableKind.LVar, "L:S_OH_ELEC_BAT1", 1),
                new(VariableKind.LVar, "L:S_OH_ELEC_BAT2", 1),
                new(VariableKind.LVar, "L:I_OH_ELEC_EXT_PWR_L", 0),
                new(VariableKind.LVar, "L:S_OH_ELEC_APU_MASTER", 1),
                new(VariableKind.LVar, "L:I_OH_ELEC_APU_START_U", 1),
                new(VariableKind.LVar, "L:S_OH_PNEUMATIC_PACK_1", 1),
                new(VariableKind.LVar, "L:I_OH_PNEUMATIC_PACK_1_U", 0),
                new(VariableKind.LVar, "L:S_OH_PNEUMATIC_PACK_2", 0),
                new(VariableKind.LVar, "L:I_OH_PNEUMATIC_PACK_2_U", 1),
                new(VariableKind.LVar, "L:Any_Exit_open_L", 0),
                new(VariableKind.LVar, "L:Any_Exit_open_R", 0),
                new(VariableKind.LVar, "L:S_OH_PNEUMATIC_WING_ANTI_ICE", 1),
                new(VariableKind.LVar, "L:S_OH_PNEUMATIC_ENG1_ANTI_ICE", 0),
                new(VariableKind.LVar, "L:S_OH_PNEUMATIC_ENG2_ANTI_ICE", 0),
                new(VariableKind.LVar, "L:S_OH_INT_LT_EMER", 2),
            ]);

        Assert.True(result.Snapshot.Systems.Lights.Landing);
        Assert.True(result.Snapshot.Systems.Lights.Strobe);
        Assert.True(result.Snapshot.Systems.BatteryOn);
        Assert.False(result.Snapshot.Systems.ExternalPowerOn);
        Assert.True(result.Snapshot.Systems.ApuRunning);
        Assert.True(result.Snapshot.Systems.PacksOn);
        Assert.True(result.Snapshot.Systems.AntiIceOn);
        Assert.True(result.Snapshot.Systems.EmergencyLightsOn);
        Assert.Equal("CONF 1+F", result.Snapshot.Systems.Flaps?.Label);
        Assert.Equal(DoorPosition.Closed, Assert.Single(result.Snapshot.Doors).Position);
    }

    [Fact]
    public void FenixStrobeAutoAndUnavailableVariablesRetainUniversalValues()
    {
        var catalog = new ProfileCatalog();
        catalog.LoadBundled();
        TelemetrySnapshot snapshot = Snapshot("Fenix A320", SimulatorKind.Msfs2020) with
        {
            Systems = new AircraftSystemsTelemetry
            {
                Lights = new AircraftLightsTelemetry
                {
                    Landing = true,
                    Strobe = true,
                },
            },
        };

        ProfileApplicationResult result = TelemetryProfileApplicator.Apply(
            catalog.Profiles,
            snapshot,
            [new RawVariableValue(VariableKind.LVar, "L:S_OH_EXT_LT_STROBE", 1)]);

        Assert.True(result.Snapshot.Systems.Lights.Strobe);
        Assert.True(result.Snapshot.Systems.Lights.Landing);
        Assert.False(result.InterpretedFeatures["strobeLights"].IsAvailable);
        Assert.False(result.InterpretedFeatures["landingLights"].IsAvailable);
    }

    [Fact]
    public void BundledFenixMatchGroupsDoNotClaimAnUnrelatedFenixAircraft()
    {
        var catalog = new ProfileCatalog();
        catalog.LoadBundled();

        ProfileSelection selection = ProfileEngine.Select(
            catalog.Profiles,
            new("Fenix Experimental Helicopter", "rotorcraft/example", SimulatorEdition.Msfs2024));

        Assert.Null(selection.Match);
    }

    [Fact]
    public void ApplicatorRetainsStandardValueWhenCustomVariableIsUnavailable()
    {
        LoadedAircraftProfile universal = Loaded(
            ProfileSource.BundledUniversal,
            Profile("universal", 0, ["msfs"], [], new Dictionary<string, TelemetryRule>
            {
                ["battery"] = new(VariableKind.SimVar, "ELECTRICAL MASTER BATTERY"),
            }));
        TelemetrySnapshot snapshot = Snapshot("Generic", SimulatorKind.Msfs2024) with
        {
            Systems = new AircraftSystemsTelemetry { BatteryOn = true },
        };

        ProfileApplicationResult result = TelemetryProfileApplicator.Apply([universal], snapshot, []);

        Assert.Equal("universal", result.Snapshot.Aircraft.ProfileId);
        Assert.True(result.Snapshot.Systems.BatteryOn);
        Assert.False(result.InterpretedFeatures["battery"].IsAvailable);
    }

    private static LoadedAircraftProfile Loaded(ProfileSource source, AircraftProfile profile) =>
        new(profile, source, $"{profile.Meta.Id}.json");

    private static Dictionary<string, TelemetryRule> EmptyMappings() =>
        new Dictionary<string, TelemetryRule>();

    private static AircraftProfile Profile(
        string id,
        int priority,
        IReadOnlyList<string> simulators,
        IReadOnlyList<ProfileMatchRule> matches,
        IReadOnlyDictionary<string, TelemetryRule> mappings) =>
        new(
            new(id, id, "Test", "Apache-2.0", priority, simulators),
            matches,
            mappings,
            new Dictionary<string, bool>(),
            new Dictionary<string, bool>());

    private static TelemetrySnapshot Snapshot(string title, SimulatorKind simulatorKind) => new()
    {
        CollectedAtUtc = DateTimeOffset.Parse("2026-09-08T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        Position = new GeoPosition(47, -122),
        AltitudeMsl = new Altitude(1_000),
        IndicatedAirspeed = new Speed(120),
        GroundSpeed = new Speed(130),
        VerticalSpeed = new VerticalSpeed(500),
        OnGround = false,
        Aircraft = new ClimbAndMaintain.Acars.Core.Telemetry.AircraftIdentity(title, "A320", "aircraft.cfg", null),
        Simulator = new SimulatorIdentity(simulatorKind, simulatorKind.ToString(), "1.0"),
    };
}
