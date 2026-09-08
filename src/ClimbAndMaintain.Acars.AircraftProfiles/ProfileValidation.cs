using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace ClimbAndMaintain.Acars.AircraftProfiles;

public sealed record ProfileValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ProfileValidationResult Success { get; } = new(true, Array.Empty<string>());
}

public static partial class ProfileValidator
{
    private static readonly string[] EmptyProfileErrors = ["Profile content is empty."];

    private static readonly HashSet<string> SupportedSimulatorIds =
        new(StringComparer.OrdinalIgnoreCase) { "msfs", "msfs20", "msfs24" };

    public static ProfileValidationResult Validate(AircraftProfile? profile)
    {
        var errors = new List<string>();
        if (profile is null)
        {
            return new(false, EmptyProfileErrors);
        }

        if (profile.Meta is null)
        {
            return new(false, ["meta is required."]);
        }

        if (string.IsNullOrWhiteSpace(profile.Meta.Id) || !SafeId().IsMatch(profile.Meta.Id))
        {
            errors.Add("meta.id must contain only lowercase letters, digits, periods, underscores, or hyphens.");
        }

        if (string.IsNullOrWhiteSpace(profile.Meta.Name))
        {
            errors.Add("meta.name is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Meta.Author))
        {
            errors.Add("meta.author is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Meta.License))
        {
            errors.Add("meta.license is required.");
        }

        if (profile.Meta.Priority is < -10_000 or > 10_000)
        {
            errors.Add("meta.priority must be between -10000 and 10000.");
        }

        if (profile.Meta.Simulators is null || profile.Meta.Simulators.Count == 0)
        {
            errors.Add("meta.simulators must contain at least one simulator.");
        }
        else
        {
            foreach (string simulator in profile.Meta.Simulators)
            {
                if (string.IsNullOrWhiteSpace(simulator) || !SupportedSimulatorIds.Contains(simulator))
                {
                    errors.Add($"Unsupported simulator identifier '{simulator}'.");
                }
            }
        }

        if (profile.Match is null)
        {
            errors.Add("match must be an array.");
        }
        else if (profile.Match.Count > 32)
        {
            errors.Add("match cannot contain more than 32 rules.");
        }
        else
        {
            foreach (ProfileMatchRule? match in profile.Match)
            {
                if (match is null || string.IsNullOrWhiteSpace(match.Value))
                {
                    errors.Add("Match rule values cannot be empty.");
                    continue;
                }

                if (match.Value.Length > 512 || match.Value.Any(char.IsControl))
                {
                    errors.Add("Match rule values must be at most 512 printable characters.");
                }

                if (match.Operator is not (RuleOperator.Equals or RuleOperator.NotEquals or RuleOperator.Contains))
                {
                    errors.Add($"Match operator '{match.Operator}' is not valid for text aircraft identity fields.");
                }
            }
        }

        if (profile.MatchGroups is { Count: > 16 })
        {
            errors.Add("matchGroups cannot contain more than 16 alternatives.");
        }
        else if (profile.MatchGroups is not null)
        {
            foreach (IReadOnlyList<ProfileMatchRule>? group in profile.MatchGroups)
            {
                if (group is null || group.Count is 0 or > 16)
                {
                    errors.Add("Each matchGroups alternative must contain between 1 and 16 rules.");
                    continue;
                }

                foreach (ProfileMatchRule? match in group)
                {
                    if (match is null || string.IsNullOrWhiteSpace(match.Value))
                    {
                        errors.Add("Grouped match rule values cannot be empty.");
                        continue;
                    }

                    if (match.Value.Length > 512 || match.Value.Any(char.IsControl))
                    {
                        errors.Add("Grouped match rule values must be at most 512 printable characters.");
                    }

                    if (match.Operator is not (RuleOperator.Equals or RuleOperator.NotEquals or RuleOperator.Contains))
                    {
                        errors.Add($"Grouped match operator '{match.Operator}' is not valid for text aircraft identity fields.");
                    }
                }
            }
        }

        if ((profile.Match is null || profile.Match.Count == 0)
            && profile.MatchGroups is not { Count: > 0 }
            && profile.Meta.Priority > 0)
        {
            errors.Add("A non-universal profile must contain match rules or matchGroups.");
        }

        if (profile.Mappings is null)
        {
            errors.Add("mappings must be an object.");
        }
        else if (profile.Mappings.Count > 256)
        {
            errors.Add("mappings cannot contain more than 256 read-only variables.");
        }
        else
        {
            foreach ((string feature, TelemetryRule? rule) in profile.Mappings)
            {
                if (string.IsNullOrWhiteSpace(feature) || feature.Length > 128)
                {
                    errors.Add("Mapping feature names must contain between 1 and 128 characters.");
                }

                if (rule is null)
                {
                    errors.Add($"Mapping '{feature}' must contain a rule.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.Name))
                {
                    errors.Add($"Mapping '{feature}' has an empty variable name.");
                    continue;
                }

                if (LooksExecutable(rule.Name))
                {
                    errors.Add($"Mapping '{feature}' contains a prohibited executable expression.");
                }

                if (rule.Name.Length > 256 || ContainsDefinitionSyntax(rule.Name))
                {
                    errors.Add($"Mapping '{feature}' must use a plain variable name of at most 256 characters.");
                }

                bool isLocalVariable = rule.Name.StartsWith("L:", StringComparison.OrdinalIgnoreCase);
                if ((rule.Kind == VariableKind.LVar) != isLocalVariable)
                {
                    errors.Add($"Mapping '{feature}' must prefix LVar names with 'L:' and must not prefix SimVar names with 'L:'.");
                }

                if (rule.Unit is { } unit && (unit.Length > 64 || ContainsDefinitionSyntax(unit)))
                {
                    errors.Add($"Mapping '{feature}' must use a plain unit name of at most 64 characters.");
                }
            }
        }

        if (profile.Disabled is null)
        {
            errors.Add("disabled must be an object.");
        }

        if (profile.Features is null)
        {
            errors.Add("features must be an object.");
        }

        if (profile.CompoundMappings is { Count: > 128 })
        {
            errors.Add("compoundMappings cannot contain more than 128 read-only features.");
        }
        else if (profile.CompoundMappings is not null)
        {
            foreach ((string feature, CompositeTelemetryRule? rule) in profile.CompoundMappings)
            {
                if (string.IsNullOrWhiteSpace(feature) || feature.Length > 128)
                {
                    errors.Add("Compound mapping feature names must contain between 1 and 128 characters.");
                }

                if (rule is null || rule.Branches is null || rule.Branches.Count is 0 or > 32)
                {
                    errors.Add($"Compound mapping '{feature}' must contain between 1 and 32 branches.");
                    continue;
                }

                for (int branchIndex = 0; branchIndex < rule.Branches.Count; branchIndex++)
                {
                    CompositeTelemetryBranch? branch = rule.Branches[branchIndex];
                    if (branch is null)
                    {
                        errors.Add($"Compound mapping '{feature}' contains a null branch.");
                        continue;
                    }

                    if (branch.Value is null == !branch.UseFallback)
                    {
                        errors.Add($"Compound mapping '{feature}' branches must specify exactly one of value or useFallback.");
                    }

                    if (branch.AnyOf is null)
                    {
                        if (branchIndex != rule.Branches.Count - 1)
                        {
                            errors.Add($"Compound mapping '{feature}' may use an unconditional branch only at the end.");
                        }

                        continue;
                    }

                    if (branch.AnyOf.Count is 0 or > 16)
                    {
                        errors.Add($"Compound mapping '{feature}' branch groups must contain between 1 and 16 alternatives.");
                        continue;
                    }

                    foreach (TelemetryConditionSet? conditionSet in branch.AnyOf)
                    {
                        if (conditionSet is null || conditionSet.All is null || conditionSet.All.Count is 0 or > 16)
                        {
                            errors.Add($"Compound mapping '{feature}' alternatives must contain between 1 and 16 conditions.");
                            continue;
                        }

                        foreach (TelemetryCondition? condition in conditionSet.All)
                        {
                            ValidateCondition(feature, condition, errors);
                        }
                    }
                }
            }
        }

        if (profile.FlapLabels is { Count: > 64 })
        {
            errors.Add("flapLabels cannot contain more than 64 entries.");
        }
        else if (profile.FlapLabels is not null)
        {
            foreach ((int index, string? label) in profile.FlapLabels)
            {
                if (index < 0 || string.IsNullOrWhiteSpace(label) || label.Length > 32 || label.Any(char.IsControl))
                {
                    errors.Add("Flap labels require a non-negative index and a printable label of at most 32 characters.");
                }
            }
        }

        return new(errors.Count == 0, new ReadOnlyCollection<string>(errors));
    }

    private static void ValidateCondition(
        string feature,
        TelemetryCondition? condition,
        List<string> errors)
    {
        if (condition is null || string.IsNullOrWhiteSpace(condition.Name))
        {
            errors.Add($"Compound mapping '{feature}' contains an empty variable name.");
            return;
        }

        if (LooksExecutable(condition.Name)
            || condition.Name.Length > 256
            || ContainsDefinitionSyntax(condition.Name))
        {
            errors.Add($"Compound mapping '{feature}' contains an unsafe variable name.");
        }

        bool isLocalVariable = condition.Name.StartsWith("L:", StringComparison.OrdinalIgnoreCase);
        if ((condition.Kind == VariableKind.LVar) != isLocalVariable)
        {
            errors.Add(
                $"Compound mapping '{feature}' must prefix LVar names with 'L:' and must not prefix SimVar names with 'L:'.");
        }

        if (condition.Unit is { } unit && (unit.Length > 64 || ContainsDefinitionSyntax(unit)))
        {
            errors.Add($"Compound mapping '{feature}' must use a plain unit name of at most 64 characters.");
        }

        if (condition.Operator is not (RuleOperator.Equals
            or RuleOperator.NotEquals
            or RuleOperator.GreaterThan
            or RuleOperator.GreaterThanOrEqual
            or RuleOperator.LessThan
            or RuleOperator.LessThanOrEqual))
        {
            errors.Add($"Compound mapping '{feature}' uses a non-numeric condition operator.");
        }

        if (!double.IsFinite(condition.CompareTo))
        {
            errors.Add($"Compound mapping '{feature}' uses a non-finite comparison value.");
        }
    }

    private static bool LooksExecutable(string name) =>
        name.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("://", StringComparison.Ordinal) ||
        name.IndexOfAny(['\r', '\n', ';', '`']) >= 0;

    private static bool ContainsDefinitionSyntax(string value) => value.Any(static character =>
        char.IsControl(character) || character is '(' or ')' or '<' or '>' or '@' or ';' or '=');

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeId();
}
