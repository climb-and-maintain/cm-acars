namespace ClimbAndMaintain.Acars.SimConnect.Connection;

internal sealed class SimConnectNativeCallException(string operation, int hResult)
    : Exception($"{operation} failed with HRESULT 0x{hResult:X8}.")
{
    public string Operation { get; } = operation;

    public int HResultCode { get; } = hResult;
}
