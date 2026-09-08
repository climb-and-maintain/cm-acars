namespace ClimbAndMaintain.Acars.SimConnect.Variables;

public enum SimConnectVariableKind
{
    SimVar,
    LocalVariable,
}

public enum SimConnectVariableValueKind
{
    Numeric,
    Logical,
}

public sealed record SimConnectReadOnlyVariableDefinition
{
    public SimConnectReadOnlyVariableDefinition(
        string key,
        SimConnectVariableKind kind,
        string name,
        string? unit = null,
        SimConnectVariableValueKind valueKind = SimConnectVariableValueKind.Numeric)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string normalizedKey = key.Trim();
        string normalizedName = name.Trim();
        string? normalizedUnit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        if (normalizedKey.Length > 128 || normalizedKey.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(nameof(key), "A variable key cannot exceed 128 characters or contain control characters.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The variable kind is unsupported.");
        }

        if (!Enum.IsDefined(valueKind))
        {
            throw new ArgumentOutOfRangeException(nameof(valueKind), valueKind, "The variable value kind is unsupported.");
        }

        if (normalizedName.Length > 256 || ContainsExpressionSyntax(normalizedName))
        {
            throw new ArgumentException("A variable name must be a plain read-only SimVar or LVar name, not an expression.", nameof(name));
        }

        bool isLocalName = normalizedName.StartsWith("L:", StringComparison.OrdinalIgnoreCase);
        if ((kind == SimConnectVariableKind.LocalVariable) != isLocalName)
        {
            throw new ArgumentException("Local-variable names must begin with 'L:' and SimVar names must not.", nameof(name));
        }

        if (normalizedUnit is { Length: > 64 } || (normalizedUnit is not null && ContainsExpressionSyntax(normalizedUnit)))
        {
            throw new ArgumentException("A unit must be a plain SimConnect unit name of at most 64 characters.", nameof(unit));
        }

        Key = normalizedKey;
        Kind = kind;
        Name = normalizedName;
        Unit = normalizedUnit;
        ValueKind = valueKind;
    }

    public string Key { get; }

    public SimConnectVariableKind Kind { get; }

    public string Name { get; }

    public string? Unit { get; }

    public SimConnectVariableValueKind ValueKind { get; }

    private static bool ContainsExpressionSyntax(string value) => value.Any(static character =>
        char.IsControl(character) || character is '(' or ')' or '<' or '>' or '@' or ';' or '=');
}

public sealed record SimConnectReadOnlyVariableSample(
    SimConnectReadOnlyVariableDefinition Definition,
    double NumericValue,
    DateTimeOffset CollectedAtUtc)
{
    public bool BooleanValue => NumericValue >= 0.5;
}

public interface ISimConnectReadOnlyVariableSource
{
    IAsyncEnumerable<SimConnectReadOnlyVariableSample> ReadVariableSamplesAsync(CancellationToken cancellationToken);
}
