namespace ClimbAndMaintain.Acars.AircraftProfiles;

public static class ProfileEngine
{
    public static ProfileSelection Select(IEnumerable<LoadedAircraftProfile> profiles, AircraftIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(identity);

        var candidates = profiles.Where(x => SupportsSimulator(x.Profile, identity.Simulator)).ToArray();
        var universal = candidates
            .Where(x => x.Source == ProfileSource.BundledUniversal)
            .OrderByDescending(x => x.Profile.Meta.Priority)
            .ThenBy(x => x.Profile.Meta.Id, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("A bundled universal profile is required for every supported simulator.");

        var match = candidates
            .Where(x => x.Source != ProfileSource.BundledUniversal && Matches(x.Profile, identity))
            .OrderByDescending(x => x.Profile.Meta.Priority)
            .ThenByDescending(x => x.Source)
            .ThenBy(x => x.Profile.Meta.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        var mappings = new Dictionary<string, TelemetryRule>(universal.Profile.Mappings, StringComparer.OrdinalIgnoreCase);
        var compoundMappings = new Dictionary<string, CompositeTelemetryRule>(
            universal.Profile.CompoundMappings ?? new Dictionary<string, CompositeTelemetryRule>(),
            StringComparer.OrdinalIgnoreCase);
        var disabled = new Dictionary<string, bool>(universal.Profile.Disabled, StringComparer.OrdinalIgnoreCase);
        var features = new Dictionary<string, bool>(universal.Profile.Features, StringComparer.OrdinalIgnoreCase);

        if (match is not null)
        {
            foreach (var item in match.Profile.Mappings)
            {
                mappings[item.Key] = item.Value;
            }

            foreach (var item in match.Profile.CompoundMappings ?? new Dictionary<string, CompositeTelemetryRule>())
            {
                compoundMappings[item.Key] = item.Value;
            }

            foreach (var item in match.Profile.Disabled)
            {
                disabled[item.Key] = item.Value;
            }

            foreach (var item in match.Profile.Features)
            {
                features[item.Key] = item.Value;
            }
        }

        foreach (var (feature, isDisabled) in disabled)
        {
            if (isDisabled)
            {
                mappings.Remove(feature);
                compoundMappings.Remove(feature);
            }
        }

        return new(universal, match, mappings, compoundMappings, disabled, features);
    }

    public static IReadOnlyDictionary<string, InterpretedFeature> Interpret(
        ProfileSelection selection,
        IEnumerable<RawVariableValue> rawValues)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(rawValues);

        var values = rawValues.ToDictionary(
            x => Key(x.Kind, x.Name),
            x => x.Value,
            StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, InterpretedFeature>(StringComparer.OrdinalIgnoreCase);

        foreach (var (feature, rule) in selection.EffectiveMappings)
        {
            if (!values.TryGetValue(Key(rule.Kind, rule.Name), out var raw))
            {
                result[feature] = new(feature, false, null, null, null, rule.Name, "Variable was not supplied by the simulator.");
                continue;
            }

            var numeric = (raw * (rule.Scale ?? 1)) + (rule.Offset ?? 0);
            var boolean = Evaluate(numeric, rule.Operator, rule.CompareTo);
            result[feature] = new(feature, true, raw, numeric, boolean, rule.Name);
        }

        foreach (var (feature, rule) in selection.EffectiveCompoundMappings)
        {
            CompositeEvaluation evaluation = Evaluate(rule, values);
            string sourceVariables = string.Join(
                ", ",
                Conditions(rule)
                    .Select(static condition => condition.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            result[feature] = evaluation.IsAvailable
                ? new(feature, true, null, null, evaluation.Value, sourceVariables)
                : new(
                    feature,
                    false,
                    null,
                    null,
                    null,
                    sourceVariables,
                    evaluation.Error ?? "The compound rule requested the universal fallback.");
        }

        return result;
    }

    public static IEnumerable<TelemetryRule> EnumerateVariables(AircraftProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        foreach (TelemetryRule rule in profile.Mappings.Values)
        {
            yield return rule;
        }

        foreach (TelemetryCondition condition in (profile.CompoundMappings
                     ?? new Dictionary<string, CompositeTelemetryRule>())
                 .Values
                 .SelectMany(Conditions))
        {
            yield return new TelemetryRule(
                condition.Kind,
                condition.Name,
                condition.Unit,
                condition.Operator,
                condition.CompareTo);
        }
    }

    private static bool SupportsSimulator(AircraftProfile profile, SimulatorEdition simulator)
    {
        var exact = simulator == SimulatorEdition.Msfs2020 ? "msfs20" : "msfs24";
        return profile.Meta.Simulators.Any(x =>
            string.Equals(x, "msfs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x, exact, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Matches(AircraftProfile profile, AircraftIdentity identity)
    {
        IReadOnlyList<IReadOnlyList<ProfileMatchRule>> groups = profile.MatchGroups is { Count: > 0 }
            ? profile.MatchGroups
            : profile.Match.Count > 0
                ? [profile.Match]
                : [];
        return groups.Any(group => group.Count > 0 && group.All(rule => Matches(rule, identity)));
    }

    private static bool Matches(ProfileMatchRule rule, AircraftIdentity identity)
    {
        var actual = rule.Scope switch
        {
            MatchScope.Title => identity.Title,
            MatchScope.ConfigPath => identity.ConfigPath,
            MatchScope.Either => $"{identity.Title}\n{identity.ConfigPath}",
            _ => throw new ArgumentOutOfRangeException(nameof(identity), rule.Scope, "Unknown aircraft match scope."),
        };
        var comparison = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return rule.Operator switch
        {
            RuleOperator.Equals => string.Equals(actual, rule.Value, comparison),
            RuleOperator.NotEquals => !string.Equals(actual, rule.Value, comparison),
            RuleOperator.Contains => actual.Contains(rule.Value, comparison),
            _ => false,
        };
    }

    private static bool Evaluate(double actual, RuleOperator op, double expected) => op switch
    {
        RuleOperator.Equals => Math.Abs(actual - expected) < 0.000001,
        RuleOperator.NotEquals => Math.Abs(actual - expected) >= 0.000001,
        RuleOperator.GreaterThan => actual > expected,
        RuleOperator.GreaterThanOrEqual => actual >= expected,
        RuleOperator.LessThan => actual < expected,
        RuleOperator.LessThanOrEqual => actual <= expected,
        _ => throw new InvalidOperationException($"Operator '{op}' is not numeric."),
    };

    private static CompositeEvaluation Evaluate(
        CompositeTelemetryRule rule,
        IReadOnlyDictionary<string, double> values)
    {
        bool encounteredUnknown = false;
        foreach (CompositeTelemetryBranch branch in rule.Branches)
        {
            ConditionResult match = Evaluate(branch.AnyOf, values);
            if (match == ConditionResult.Unknown)
            {
                encounteredUnknown = true;
                continue;
            }

            if (match == ConditionResult.False)
            {
                continue;
            }

            if (branch.AnyOf is null && encounteredUnknown)
            {
                return new(false, null, "One or more custom variables were unavailable; retained the universal value.");
            }

            if (branch.UseFallback)
            {
                return new(false, null, "The matched branch requested the universal value.");
            }

            return new(true, branch.Value, null);
        }

        return new(
            false,
            null,
            encounteredUnknown
                ? "One or more custom variables were unavailable; retained the universal value."
                : "No compound-rule branch matched; retained the universal value.");
    }

    private static ConditionResult Evaluate(
        IReadOnlyList<TelemetryConditionSet>? anyOf,
        IReadOnlyDictionary<string, double> values)
    {
        if (anyOf is null)
        {
            return ConditionResult.True;
        }

        bool encounteredUnknown = false;
        foreach (TelemetryConditionSet conditionSet in anyOf)
        {
            ConditionResult result = Evaluate(conditionSet, values);
            if (result == ConditionResult.True)
            {
                return ConditionResult.True;
            }

            encounteredUnknown |= result == ConditionResult.Unknown;
        }

        return encounteredUnknown ? ConditionResult.Unknown : ConditionResult.False;
    }

    private static ConditionResult Evaluate(
        TelemetryConditionSet conditionSet,
        IReadOnlyDictionary<string, double> values)
    {
        bool encounteredUnknown = false;
        foreach (TelemetryCondition condition in conditionSet.All)
        {
            if (!values.TryGetValue(Key(condition.Kind, condition.Name), out double raw))
            {
                encounteredUnknown = true;
                continue;
            }

            if (!Evaluate(raw, condition.Operator, condition.CompareTo))
            {
                return ConditionResult.False;
            }
        }

        return encounteredUnknown ? ConditionResult.Unknown : ConditionResult.True;
    }

    private static IEnumerable<TelemetryCondition> Conditions(CompositeTelemetryRule rule) =>
        rule.Branches
            .Where(static branch => branch.AnyOf is not null)
            .SelectMany(static branch => branch.AnyOf!)
            .SelectMany(static conditionSet => conditionSet.All);

    private static string Key(VariableKind kind, string name) => $"{kind}:{name}";

    private enum ConditionResult
    {
        False,
        True,
        Unknown,
    }

    private readonly record struct CompositeEvaluation(
        bool IsAvailable,
        bool? Value,
        string? Error);
}
