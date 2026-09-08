using System.Text.Json.Serialization;

namespace ClimbAndMaintain.Acars.AircraftProfiles;

public enum SimulatorEdition
{
    Msfs2020,
    Msfs2024,
}

public enum ProfileSource
{
    BundledUniversal = 0,
    UpstreamPhpVms = 1,
    ClimbAndMaintain = 2,
    User = 3,
}

public enum MatchScope
{
    Title,
    ConfigPath,
    Either,
}

public enum RuleOperator
{
    Equals,
    NotEquals,
    Contains,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
}

public enum VariableKind
{
    SimVar,
    LVar,
}

public sealed record ProfileMetadata(
    string Id,
    string Name,
    string Author,
    string License,
    int Priority,
    IReadOnlyList<string> Simulators,
    string? SourceUrl = null,
    string? SourceRevision = null);

public sealed record ProfileMatchRule(
    MatchScope Scope,
    RuleOperator Operator,
    string Value,
    bool IgnoreCase = true);

public sealed record TelemetryRule(
    VariableKind Kind,
    string Name,
    string? Unit = null,
    RuleOperator Operator = RuleOperator.GreaterThan,
    double CompareTo = 0,
    double? Scale = null,
    double? Offset = null);

public sealed record TelemetryCondition(
    VariableKind Kind,
    string Name,
    string? Unit = null,
    RuleOperator Operator = RuleOperator.GreaterThan,
    double CompareTo = 0);

public sealed record TelemetryConditionSet(
    IReadOnlyList<TelemetryCondition> All);

public sealed record CompositeTelemetryBranch(
    IReadOnlyList<TelemetryConditionSet>? AnyOf = null,
    bool? Value = null,
    bool UseFallback = false);

public sealed record CompositeTelemetryRule(
    IReadOnlyList<CompositeTelemetryBranch> Branches);

public sealed record AircraftProfile(
    ProfileMetadata Meta,
    IReadOnlyList<ProfileMatchRule> Match,
    IReadOnlyDictionary<string, TelemetryRule> Mappings,
    IReadOnlyDictionary<string, bool> Disabled,
    IReadOnlyDictionary<string, bool> Features,
    IReadOnlyDictionary<string, CompositeTelemetryRule>? CompoundMappings = null,
    IReadOnlyDictionary<int, string>? FlapLabels = null,
    IReadOnlyList<IReadOnlyList<ProfileMatchRule>>? MatchGroups = null);

public sealed record AircraftIdentity(
    string Title,
    string ConfigPath,
    SimulatorEdition Simulator);

public sealed record LoadedAircraftProfile(
    AircraftProfile Profile,
    ProfileSource Source,
    string Origin);

public sealed record ProfileSelection(
    LoadedAircraftProfile Universal,
    LoadedAircraftProfile? Match,
    IReadOnlyDictionary<string, TelemetryRule> EffectiveMappings,
    IReadOnlyDictionary<string, CompositeTelemetryRule> EffectiveCompoundMappings,
    IReadOnlyDictionary<string, bool> Disabled,
    IReadOnlyDictionary<string, bool> Features);

public sealed record RawVariableValue(VariableKind Kind, string Name, double Value);

public sealed record InterpretedFeature(
    string Feature,
    bool IsAvailable,
    double? RawValue,
    double? NumericValue,
    bool? BooleanValue,
    string SourceVariable,
    string? Error = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AircraftProfile))]
internal sealed partial class ProfileJsonContext : JsonSerializerContext;
