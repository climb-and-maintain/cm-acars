namespace ClimbAndMaintain.Acars.SimConnect.Validation;

public static class SimConnectExportSurface
{
    private static readonly IReadOnlyList<string> RequiredExportNames = Array.AsReadOnly(
    [
        "SimConnect_Open",
        "SimConnect_Close",
        "SimConnect_GetNextDispatch",
        "SimConnect_AddToDataDefinition",
        "SimConnect_ClearDataDefinition",
        "SimConnect_RequestDataOnSimObject",
        "SimConnect_SubscribeToSystemEvent",
        "SimConnect_UnsubscribeFromSystemEvent",
        "SimConnect_RequestSystemState",
        "SimConnect_GetLastSentPacketID",
    ]);

    public static IReadOnlyList<string> RequiredExports => RequiredExportNames;
}
