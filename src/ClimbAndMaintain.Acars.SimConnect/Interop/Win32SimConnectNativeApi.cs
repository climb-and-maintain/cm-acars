using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ClimbAndMaintain.Acars.SimConnect.Interop;

[SupportedOSPlatform("windows")]
internal sealed class Win32SimConnectNativeApi : ISimConnectNativeApi
{
    private readonly SafeNativeLibraryHandle library;
    private readonly OpenDelegate open;
    private readonly CloseDelegate close;
    private readonly GetNextDispatchDelegate getNextDispatch;
    private readonly AddToDataDefinitionDelegate addToDataDefinition;
    private readonly ClearDataDefinitionDelegate clearDataDefinition;
    private readonly RequestDataOnSimObjectDelegate requestDataOnSimObject;
    private readonly SubscribeToSystemEventDelegate subscribeToSystemEvent;
    private readonly UnsubscribeFromSystemEventDelegate unsubscribeFromSystemEvent;
    private readonly RequestSystemStateDelegate requestSystemState;
    private readonly GetLastSentPacketIdDelegate getLastSentPacketId;

    private Win32SimConnectNativeApi(SafeNativeLibraryHandle library)
    {
        this.library = library;
        open = Bind<OpenDelegate>(library, "SimConnect_Open");
        close = Bind<CloseDelegate>(library, "SimConnect_Close");
        getNextDispatch = Bind<GetNextDispatchDelegate>(library, "SimConnect_GetNextDispatch");
        addToDataDefinition = Bind<AddToDataDefinitionDelegate>(library, "SimConnect_AddToDataDefinition");
        clearDataDefinition = Bind<ClearDataDefinitionDelegate>(library, "SimConnect_ClearDataDefinition");
        requestDataOnSimObject = Bind<RequestDataOnSimObjectDelegate>(library, "SimConnect_RequestDataOnSimObject");
        subscribeToSystemEvent = Bind<SubscribeToSystemEventDelegate>(library, "SimConnect_SubscribeToSystemEvent");
        unsubscribeFromSystemEvent = Bind<UnsubscribeFromSystemEventDelegate>(library, "SimConnect_UnsubscribeFromSystemEvent");
        requestSystemState = Bind<RequestSystemStateDelegate>(library, "SimConnect_RequestSystemState");
        getLastSentPacketId = Bind<GetLastSentPacketIdDelegate>(library, "SimConnect_GetLastSentPacketID");
    }

    public static Win32SimConnectNativeApi Load(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath))
        {
            throw new ArgumentException("A native library must be loaded by absolute path.", nameof(absolutePath));
        }

        nint module = Win32NativeMethods.LoadLibraryEx(
            absolutePath,
            0,
            Win32NativeMethods.LoadLibrarySearchDllLoadDirectory | Win32NativeMethods.LoadLibrarySearchSystem32);
        if (module == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Unable to load native library '{absolutePath}'.");
        }

        SafeNativeLibraryHandle handle = new(module);
        try
        {
            return new(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public int Open(
        out nint connection,
        string clientName,
        EventWaitHandle dispatchEvent,
        uint configIndex)
    {
        ArgumentNullException.ThrowIfNull(dispatchEvent);
        return open(
            out connection,
            clientName,
            0,
            0,
            dispatchEvent.SafeWaitHandle.DangerousGetHandle(),
            configIndex);
    }

    public int Close(nint connection) => close(connection);

    public int GetNextDispatch(nint connection, out nint data, out uint dataSize) =>
        getNextDispatch(connection, out data, out dataSize);

    public int AddToDataDefinition(
        nint connection,
        uint definitionId,
        string datumName,
        string? unitsName,
        SimConnectDataType dataType,
        float epsilon,
        uint datumId) => addToDataDefinition(
            connection,
            definitionId,
            datumName,
            unitsName,
            dataType,
            epsilon,
            datumId);

    public int ClearDataDefinition(nint connection, uint definitionId) => clearDataDefinition(connection, definitionId);

    public int RequestDataOnSimObject(
        nint connection,
        uint requestId,
        uint definitionId,
        uint objectId,
        SimConnectPeriod period,
        uint flags,
        uint origin,
        uint interval,
        uint limit) => requestDataOnSimObject(
            connection,
            requestId,
            definitionId,
            objectId,
            period,
            flags,
            origin,
            interval,
            limit);

    public int SubscribeToSystemEvent(nint connection, uint eventId, string eventName) =>
        subscribeToSystemEvent(connection, eventId, eventName);

    public int UnsubscribeFromSystemEvent(nint connection, uint eventId) =>
        unsubscribeFromSystemEvent(connection, eventId);

    public int RequestSystemState(nint connection, uint requestId, string stateName) =>
        requestSystemState(connection, requestId, stateName);

    public int GetLastSentPacketId(nint connection, out uint packetId) => getLastSentPacketId(connection, out packetId);

    public void Dispose() => library.Dispose();

    private static TDelegate Bind<TDelegate>(SafeNativeLibraryHandle library, string exportName)
        where TDelegate : Delegate
    {
        nint address = Win32NativeMethods.GetProcAddress(library.DangerousGetHandle(), exportName);
        if (address == 0)
        {
            throw new EntryPointNotFoundException($"The native library does not export {exportName}.");
        }

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private delegate int OpenDelegate(
        out nint connection,
        [MarshalAs(UnmanagedType.LPStr)] string clientName,
        nint windowHandle,
        uint windowMessage,
        nint eventHandle,
        uint configIndex);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CloseDelegate(nint connection);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetNextDispatchDelegate(nint connection, out nint data, out uint dataSize);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private delegate int AddToDataDefinitionDelegate(
        nint connection,
        uint definitionId,
        [MarshalAs(UnmanagedType.LPStr)] string datumName,
        [MarshalAs(UnmanagedType.LPStr)] string? unitsName,
        SimConnectDataType dataType,
        float epsilon,
        uint datumId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ClearDataDefinitionDelegate(nint connection, uint definitionId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int RequestDataOnSimObjectDelegate(
        nint connection,
        uint requestId,
        uint definitionId,
        uint objectId,
        SimConnectPeriod period,
        uint flags,
        uint origin,
        uint interval,
        uint limit);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private delegate int SubscribeToSystemEventDelegate(
        nint connection,
        uint eventId,
        [MarshalAs(UnmanagedType.LPStr)] string eventName);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int UnsubscribeFromSystemEventDelegate(nint connection, uint eventId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private delegate int RequestSystemStateDelegate(
        nint connection,
        uint requestId,
        [MarshalAs(UnmanagedType.LPStr)] string stateName);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetLastSentPacketIdDelegate(nint connection, out uint packetId);
}

[SupportedOSPlatform("windows")]
internal sealed class Win32SimConnectNativeApiFactory : ISimConnectNativeApiFactory
{
    public ISimConnectNativeApi Load(string absolutePath) => Win32SimConnectNativeApi.Load(absolutePath);
}
