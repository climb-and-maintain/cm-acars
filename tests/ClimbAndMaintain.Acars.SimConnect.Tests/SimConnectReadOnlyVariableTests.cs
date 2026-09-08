using ClimbAndMaintain.Acars.SimConnect.Variables;

namespace ClimbAndMaintain.Acars.SimConnect.Tests;

public sealed class SimConnectReadOnlyVariableTests
{
    [Fact]
    public void AcceptsPlainReadOnlySimVarAndLVarNames()
    {
        SimConnectReadOnlyVariableDefinition simVar = new(
            "battery",
            SimConnectVariableKind.SimVar,
            "ELECTRICAL MASTER BATTERY:1",
            "Bool",
            SimConnectVariableValueKind.Logical);
        SimConnectReadOnlyVariableDefinition localVariable = new(
            "custom-apu",
            SimConnectVariableKind.LocalVariable,
            "L:CUSTOM_APU_RUNNING",
            "number");

        Assert.Equal("ELECTRICAL MASTER BATTERY:1", simVar.Name);
        Assert.Equal(SimConnectVariableValueKind.Logical, simVar.ValueKind);
        Assert.Equal("L:CUSTOM_APU_RUNNING", localVariable.Name);
    }

    [Theory]
    [InlineData("(A:LIGHT BEACON, Bool)")]
    [InlineData("LIGHT BEACON (>K:TOGGLE_BEACON_LIGHTS)")]
    [InlineData("L:VALUE; 1 +")]
    public void RejectsExpressionOrWriteSyntax(string name)
    {
        Assert.Throws<ArgumentException>(() => new SimConnectReadOnlyVariableDefinition(
            "unsafe",
            name.StartsWith("L:", StringComparison.Ordinal)
                ? SimConnectVariableKind.LocalVariable
                : SimConnectVariableKind.SimVar,
            name,
            "number"));
    }

    [Fact]
    public void DoesNotTreatSimVarsAndLVarsAsInterchangeable()
    {
        Assert.Throws<ArgumentException>(() => new SimConnectReadOnlyVariableDefinition(
            "wrong-kind",
            SimConnectVariableKind.SimVar,
            "L:CUSTOM_VALUE",
            "number"));
        Assert.Throws<ArgumentException>(() => new SimConnectReadOnlyVariableDefinition(
            "wrong-kind",
            SimConnectVariableKind.LocalVariable,
            "LIGHT BEACON",
            "Bool"));
    }
}
