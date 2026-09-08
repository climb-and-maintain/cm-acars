using System.Buffers.Binary;
using System.Text;
using ClimbAndMaintain.Acars.SimConnect.Interop;

namespace ClimbAndMaintain.Acars.SimConnect.Tests.Fixtures;

internal static class PacketFixtures
{
    public static byte[] Open(uint applicationMajor, uint simConnectMajor)
    {
        byte[] packet = new byte[SimConnectReceiveParser.OpenMessageSize];
        WriteHeader(packet, SimConnectReceiveId.Open);
        Encoding.ASCII.GetBytes(applicationMajor == 11 ? "KittyHawk" : "SunRise").CopyTo(packet, 12);
        WriteUInt32(packet, 268, applicationMajor);
        WriteUInt32(packet, 272, 0);
        WriteUInt32(packet, 276, 282174);
        WriteUInt32(packet, 280, 999);
        WriteUInt32(packet, 284, simConnectMajor);
        WriteUInt32(packet, 288, 0);
        WriteUInt32(packet, 292, 62651);
        WriteUInt32(packet, 296, 3);
        return packet;
    }

    public static byte[] Exception(SimConnectExceptionCode code, uint sendId = 7, uint parameterIndex = uint.MaxValue)
    {
        byte[] packet = new byte[SimConnectReceiveParser.ExceptionMessageSize];
        WriteHeader(packet, SimConnectReceiveId.Exception);
        WriteUInt32(packet, 12, (uint)code);
        WriteUInt32(packet, 16, sendId);
        WriteUInt32(packet, 20, parameterIndex);
        return packet;
    }

    public static byte[] CoreTelemetry(
        uint requestId = 0x434D0002,
        uint definitionId = 0x434D0001,
        double latitude = 47.4502,
        double longitude = -122.3088)
    {
        byte[] packet = new byte[
            SimConnectReceiveParser.ObjectDataPayloadOffset + SimConnectReceiveParser.CoreTelemetryPayloadSize];
        WriteHeader(packet, SimConnectReceiveId.SimObjectData);
        WriteUInt32(packet, 12, requestId);
        WriteUInt32(packet, 16, SimConnectConstants.ObjectIdUser);
        WriteUInt32(packet, 20, definitionId);
        WriteUInt32(packet, 24, SimConnectConstants.DataRequestFlagDefault);
        WriteUInt32(packet, 28, 1);
        WriteUInt32(packet, 32, 1);
        WriteUInt32(packet, 36, SimConnectReceiveParser.CoreTelemetryFieldCount);

        double[] values =
        [
            latitude,
            longitude,
            1_250,
            900,
            145,
            151,
            450,
            175,
            160,
            0,
        ];

        for (int index = 0; index < values.Length; index++)
        {
            WriteDouble(packet, SimConnectReceiveParser.ObjectDataPayloadOffset + (index * sizeof(double)), values[index]);
        }

        return packet;
    }

    public static byte[] StandardSystems(
        uint requestId = 0x434D1004,
        uint definitionId = 0x434D1003)
    {
        double[] values =
        [
            10_000,
            2,
            1, 0, 0.75, 500,
            1, 0, 0.72, 480,
            0, 0, 0, 0,
            0, 0, 0, 0,
            1, 1, 1, 1, 0, 0, 1,
            1,
            0,
            4,
            1,
            0,
            0.9,
            1,
            1,
        ];
        return Float64ObjectData(requestId, definitionId, values);
    }

    public static byte[] FlightControls(
        uint requestId = 0x434D100A,
        uint definitionId = 0x434D1009) => Float64ObjectData(
            requestId,
            definitionId,
            [1, 2, 0.25]);

    public static byte[] SimulatorTime(
        uint requestId,
        uint definitionId,
        DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        return Float64ObjectData(
            requestId,
            definitionId,
            [utc.Year, utc.Month, utc.Day, utc.TimeOfDay.TotalSeconds]);
    }

    public static byte[] AircraftIdentity(
        uint requestId = 0x434D1006,
        uint definitionId = 0x434D1005,
        string title = "Cessna Skyhawk G1000",
        string icaoType = "C172")
    {
        byte[] payload = new byte[SimConnectReceiveParser.AircraftIdentityPayloadSize];
        Encoding.ASCII.GetBytes(title).CopyTo(payload, 0);
        Encoding.ASCII.GetBytes(icaoType).CopyTo(payload, 256);
        return ObjectData(requestId, definitionId, SimConnectReceiveParser.AircraftIdentityFieldCount, payload);
    }

    public static byte[] DynamicValues(
        uint requestId,
        uint definitionId,
        params double[] values) => Float64ObjectData(requestId, definitionId, values);

    public static byte[] EventFilename(uint eventId, string fileName)
    {
        byte[] packet = new byte[SimConnectReceiveParser.EventFilenameMessageSize];
        WriteHeader(packet, SimConnectReceiveId.EventFilename);
        WriteUInt32(packet, 12, 0);
        WriteUInt32(packet, 16, eventId);
        WriteUInt32(packet, 20, 0);
        Encoding.ASCII.GetBytes(fileName).CopyTo(packet, 24);
        WriteUInt32(packet, 284, 0);
        return packet;
    }

    public static byte[] Event(uint eventId, uint data, uint groupId = 0)
    {
        byte[] packet = new byte[SimConnectReceiveParser.EventMessageSize];
        WriteHeader(packet, SimConnectReceiveId.Event);
        WriteUInt32(packet, 12, groupId);
        WriteUInt32(packet, 16, eventId);
        WriteUInt32(packet, 20, data);
        return packet;
    }

    public static byte[] Quit()
    {
        byte[] packet = new byte[SimConnectReceiveParser.HeaderSize];
        WriteHeader(packet, SimConnectReceiveId.Quit);
        return packet;
    }

    public static byte[] RemoteDisconnect() => [];

    public static byte[] Null()
    {
        byte[] packet = new byte[SimConnectReceiveParser.HeaderSize];
        WriteHeader(packet, SimConnectReceiveId.Null);
        return packet;
    }

    private static byte[] Float64ObjectData(uint requestId, uint definitionId, double[] values)
    {
        byte[] payload = new byte[checked(values.Length * sizeof(double))];
        for (int index = 0; index < values.Length; index++)
        {
            WriteDouble(payload, index * sizeof(double), values[index]);
        }

        return ObjectData(requestId, definitionId, values.Length, payload);
    }

    private static byte[] ObjectData(uint requestId, uint definitionId, int fieldCount, byte[] payload)
    {
        byte[] packet = new byte[SimConnectReceiveParser.ObjectDataPayloadOffset + payload.Length];
        WriteHeader(packet, SimConnectReceiveId.SimObjectData);
        WriteUInt32(packet, 12, requestId);
        WriteUInt32(packet, 16, SimConnectConstants.ObjectIdUser);
        WriteUInt32(packet, 20, definitionId);
        WriteUInt32(packet, 24, SimConnectConstants.DataRequestFlagDefault);
        WriteUInt32(packet, 28, 1);
        WriteUInt32(packet, 32, 1);
        WriteUInt32(packet, 36, checked((uint)fieldCount));
        payload.CopyTo(packet, SimConnectReceiveParser.ObjectDataPayloadOffset);
        return packet;
    }

    private static void WriteHeader(byte[] packet, SimConnectReceiveId id)
    {
        WriteUInt32(packet, 0, checked((uint)packet.Length));
        WriteUInt32(packet, 4, 6);
        WriteUInt32(packet, 8, (uint)id);
    }

    private static void WriteUInt32(byte[] destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset, sizeof(uint)), value);

    private static void WriteDouble(byte[] destination, int offset, double value) =>
        BinaryPrimitives.WriteInt64LittleEndian(
            destination.AsSpan(offset, sizeof(double)),
            BitConverter.DoubleToInt64Bits(value));
}
