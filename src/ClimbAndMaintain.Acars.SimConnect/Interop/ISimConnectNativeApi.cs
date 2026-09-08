namespace ClimbAndMaintain.Acars.SimConnect.Interop;

internal interface ISimConnectNativeApi : IDisposable
{
    int Open(out nint connection, string clientName, EventWaitHandle dispatchEvent, uint configIndex);

    int Close(nint connection);

    int GetNextDispatch(nint connection, out nint data, out uint dataSize);

    int AddToDataDefinition(
        nint connection,
        uint definitionId,
        string datumName,
        string? unitsName,
        SimConnectDataType dataType,
        float epsilon,
        uint datumId);

    int ClearDataDefinition(nint connection, uint definitionId);

    int RequestDataOnSimObject(
        nint connection,
        uint requestId,
        uint definitionId,
        uint objectId,
        SimConnectPeriod period,
        uint flags,
        uint origin,
        uint interval,
        uint limit);

    int SubscribeToSystemEvent(nint connection, uint eventId, string eventName);

    int UnsubscribeFromSystemEvent(nint connection, uint eventId);

    int RequestSystemState(nint connection, uint requestId, string stateName);

    int GetLastSentPacketId(nint connection, out uint packetId);
}

internal interface ISimConnectNativeApiFactory
{
    ISimConnectNativeApi Load(string absolutePath);
}
