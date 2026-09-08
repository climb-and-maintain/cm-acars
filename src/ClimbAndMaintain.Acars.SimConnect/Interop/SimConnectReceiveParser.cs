using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ClimbAndMaintain.Acars.SimConnect.Configuration;

namespace ClimbAndMaintain.Acars.SimConnect.Interop;

public static class SimConnectReceiveParser
{
    public const int HeaderSize = 12;
    public const int OpenMessageSize = 308;
    public const int ExceptionMessageSize = 24;
    public const int EventMessageSize = 24;
    public const int EventFilenameMessageSize = 288;
    public const int ObjectDataPayloadOffset = 40;
    public const int CoreTelemetryFieldCount = 10;
    public const int CoreTelemetryPayloadSize = CoreTelemetryFieldCount * sizeof(double);
    public const int StandardSystemsFieldCount = 33;
    public const int FlightControlsFieldCount = 3;
    public const int AircraftIdentityFieldCount = 2;
    public const int AircraftIdentityPayloadSize = 256 + 32;
    public const int SimulatorTimeFieldCount = 4;

    public static bool TryParseHeader(
        ReadOnlySpan<byte> packet,
        out SimConnectReceiveHeader? header,
        out string? error)
    {
        header = null;
        error = null;

        if (packet.Length < HeaderSize)
        {
            error = $"A SimConnect message must contain at least {HeaderSize} bytes.";
            return false;
        }

        uint size = BinaryPrimitives.ReadUInt32LittleEndian(packet);
        if (size < HeaderSize || size > packet.Length)
        {
            error = $"The SimConnect message size {size} is outside the received buffer length {packet.Length}.";
            return false;
        }

        header = new(
            size,
            BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]),
            (SimConnectReceiveId)BinaryPrimitives.ReadUInt32LittleEndian(packet[8..]));
        return true;
    }

    public static bool TryParseOpen(
        ReadOnlySpan<byte> packet,
        out SimConnectOpenMessage? message,
        out string? error)
    {
        message = null;
        if (!TryParseExpectedHeader(packet, SimConnectReceiveId.Open, OpenMessageSize, out SimConnectReceiveHeader? header, out error))
        {
            return false;
        }

        ReadOnlySpan<byte> applicationNameBytes = packet.Slice(HeaderSize, 256);
        int terminator = applicationNameBytes.IndexOf((byte)0);
        if (terminator < 0)
        {
            error = "The SimConnect application name is not null terminated.";
            return false;
        }

        string applicationName = Encoding.ASCII.GetString(applicationNameBytes[..terminator]);
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            error = "The SimConnect server returned an empty application name.";
            return false;
        }

        SimConnectOpenInfo information = new(
            applicationName,
            ReadUInt32(packet, 268),
            ReadUInt32(packet, 272),
            ReadUInt32(packet, 276),
            ReadUInt32(packet, 280),
            ReadUInt32(packet, 284),
            ReadUInt32(packet, 288),
            ReadUInt32(packet, 292),
            ReadUInt32(packet, 296));

        message = new(header!, information);
        return true;
    }

    public static bool TryParseException(
        ReadOnlySpan<byte> packet,
        out SimConnectExceptionMessage? message,
        out string? error)
    {
        message = null;
        if (!TryParseExpectedHeader(
                packet,
                SimConnectReceiveId.Exception,
                ExceptionMessageSize,
                out SimConnectReceiveHeader? header,
                out error))
        {
            return false;
        }

        message = new(
            header!,
            (SimConnectExceptionCode)ReadUInt32(packet, 12),
            ReadUInt32(packet, 16),
            ReadUInt32(packet, 20));
        return true;
    }

    public static bool TryParseEvent(
        ReadOnlySpan<byte> packet,
        out SimConnectEventMessage? message,
        out string? error)
    {
        message = null;
        if (!TryParseExpectedHeader(
                packet,
                SimConnectReceiveId.Event,
                EventMessageSize,
                out SimConnectReceiveHeader? header,
                out error))
        {
            return false;
        }

        message = new(
            header!,
            ReadUInt32(packet, 12),
            ReadUInt32(packet, 16),
            ReadUInt32(packet, 20));
        return true;
    }

    public static bool TryParseEventFilename(
        ReadOnlySpan<byte> packet,
        out SimConnectEventFilenameMessage? message,
        out string? error)
    {
        message = null;
        if (!TryParseExpectedHeader(
                packet,
                SimConnectReceiveId.EventFilename,
                EventFilenameMessageSize,
                out SimConnectReceiveHeader? header,
                out error))
        {
            return false;
        }

        string fileName = DecodeFixedAscii(packet.Slice(24, 260));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            error = "The SimConnect filename event did not include a file name.";
            return false;
        }

        message = new(
            header!,
            ReadUInt32(packet, 12),
            ReadUInt32(packet, 16),
            ReadUInt32(packet, 20),
            fileName,
            ReadUInt32(packet, 284));
        return true;
    }

    public static bool TryParseObjectData(
        ReadOnlySpan<byte> packet,
        out SimConnectObjectDataMessage? message,
        out string? error)
    {
        message = null;
        if (!TryParseHeader(packet, out SimConnectReceiveHeader? header, out error))
        {
            return false;
        }

        if (header!.Id is not SimConnectReceiveId.SimObjectData and not SimConnectReceiveId.SimObjectDataByType)
        {
            error = $"Expected object data but received message ID {(uint)header.Id}.";
            return false;
        }

        if (header.Size < ObjectDataPayloadOffset)
        {
            error = $"Object data is shorter than its {ObjectDataPayloadOffset}-byte fixed header.";
            return false;
        }

        int payloadLength = checked((int)header.Size - ObjectDataPayloadOffset);
        message = new(
            header,
            ReadUInt32(packet, 12),
            ReadUInt32(packet, 16),
            ReadUInt32(packet, 20),
            ReadUInt32(packet, 24),
            ReadUInt32(packet, 28),
            ReadUInt32(packet, 32),
            ReadUInt32(packet, 36),
            packet.Slice(ObjectDataPayloadOffset, payloadLength).ToArray());
        return true;
    }

    public static bool TryParseCoreTelemetry(
        SimConnectObjectDataMessage objectData,
        out SimConnectCoreTelemetry? telemetry,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(objectData);
        telemetry = null;
        error = null;

        if (objectData.DefinitionCount != CoreTelemetryFieldCount)
        {
            error = $"Expected {CoreTelemetryFieldCount} telemetry fields but received {objectData.DefinitionCount}.";
            return false;
        }

        if (objectData.Payload.Length != CoreTelemetryPayloadSize)
        {
            error = $"Expected {CoreTelemetryPayloadSize} telemetry bytes but received {objectData.Payload.Length}.";
            return false;
        }

        ReadOnlySpan<byte> payload = objectData.Payload.Span;
        double latitude = ReadDouble(payload, 0);
        double longitude = ReadDouble(payload, 8);
        double altitudeMsl = ReadDouble(payload, 16);
        double altitudeAgl = ReadDouble(payload, 24);
        double indicatedAirspeed = ReadDouble(payload, 32);
        double groundSpeed = ReadDouble(payload, 40);
        double verticalSpeed = ReadDouble(payload, 48);
        double trueHeading = ReadDouble(payload, 56);
        double magneticHeading = ReadDouble(payload, 64);
        double onGround = ReadDouble(payload, 72);

        double[] values =
        [
            latitude,
            longitude,
            altitudeMsl,
            altitudeAgl,
            indicatedAirspeed,
            groundSpeed,
            verticalSpeed,
            trueHeading,
            magneticHeading,
            onGround,
        ];

        if (values.Any(static value => !double.IsFinite(value)))
        {
            error = "Core telemetry contains a non-finite value.";
            return false;
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            error = "Core telemetry contains an invalid geographic position.";
            return false;
        }

        if (indicatedAirspeed < 0 || groundSpeed < 0)
        {
            error = "Core telemetry contains a negative airspeed or ground speed.";
            return false;
        }

        telemetry = new(
            latitude,
            longitude,
            altitudeMsl,
            altitudeAgl,
            indicatedAirspeed,
            groundSpeed,
            verticalSpeed,
            trueHeading,
            magneticHeading,
            onGround >= 0.5);
        return true;
    }

    public static bool TryParseStandardSystemsTelemetry(
        SimConnectObjectDataMessage objectData,
        out SimConnectStandardSystemsTelemetry? telemetry,
        out string? error)
    {
        telemetry = null;
        if (!TryParseFloat64Values(objectData, StandardSystemsFieldCount, out ImmutableArray<double> values, out error))
        {
            return false;
        }

        if (!TryReadWholeNumber(values[1], 0, 4, out int engineCount))
        {
            error = "The simulator returned an invalid engine count.";
            return false;
        }

        ImmutableArray<SimConnectEngineTelemetry>.Builder engines = ImmutableArray.CreateBuilder<SimConnectEngineTelemetry>(4);
        int offset = 2;
        for (int engine = 0; engine < 4; engine++)
        {
            engines.Add(new(
                IsTrue(values[offset]),
                IsTrue(values[offset + 1]),
                values[offset + 2],
                values[offset + 3]));
            offset += 4;
        }

        if (!TryReadWholeNumber(values[27], 0, int.MaxValue, out int transponderState))
        {
            error = "The simulator returned an invalid transponder state.";
            return false;
        }

        telemetry = new(
            values[0],
            engineCount,
            engines.MoveToImmutable(),
            IsTrue(values[18]),
            IsTrue(values[19]),
            IsTrue(values[20]),
            IsTrue(values[21]),
            IsTrue(values[22]),
            IsTrue(values[23]),
            IsTrue(values[24]),
            IsTrue(values[25]),
            IsTrue(values[26]),
            transponderState,
            IsTrue(values[28]),
            IsTrue(values[29]),
            values[30],
            IsTrue(values[31]),
            IsTrue(values[32]));
        return true;
    }

    public static bool TryParseFlightControlsTelemetry(
        SimConnectObjectDataMessage objectData,
        out SimConnectFlightControlsTelemetry? telemetry,
        out string? error)
    {
        telemetry = null;
        if (!TryParseFloat64Values(objectData, FlightControlsFieldCount, out ImmutableArray<double> values, out error))
        {
            return false;
        }

        if (!TryReadWholeNumber(values[1], 0, int.MaxValue, out int flapHandleIndex))
        {
            error = "The simulator returned an invalid flap handle index.";
            return false;
        }

        if (values[0] is < 0 or > 1 || values[2] is < 0 or > 1)
        {
            error = "The simulator returned a gear or flap extension ratio outside zero through one.";
            return false;
        }

        telemetry = new(values[0], flapHandleIndex, values[2]);
        return true;
    }

    public static bool TryParseAircraftIdentity(
        SimConnectObjectDataMessage objectData,
        out SimConnectAircraftIdentityTelemetry? identity,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(objectData);
        identity = null;
        error = null;

        if (objectData.DefinitionCount != AircraftIdentityFieldCount
            || objectData.Payload.Length != AircraftIdentityPayloadSize)
        {
            error = $"Expected {AircraftIdentityFieldCount} identity fields and {AircraftIdentityPayloadSize} bytes.";
            return false;
        }

        ReadOnlySpan<byte> payload = objectData.Payload.Span;
        string title = DecodeFixedAscii(payload[..256]);
        if (string.IsNullOrWhiteSpace(title))
        {
            error = "The simulator returned an empty aircraft title.";
            return false;
        }

        string icaoType = DecodeFixedAscii(payload.Slice(256, 32));
        identity = new(title, string.IsNullOrWhiteSpace(icaoType) ? null : icaoType);
        return true;
    }

    public static bool TryParseSimulatorTime(
        SimConnectObjectDataMessage objectData,
        out DateTimeOffset? simulatorTime,
        out string? error)
    {
        simulatorTime = null;
        if (!TryParseFloat64Values(objectData, SimulatorTimeFieldCount, out ImmutableArray<double> values, out error))
        {
            return false;
        }

        if (!TryReadWholeNumber(values[0], 1, 9999, out int year)
            || !TryReadWholeNumber(values[1], 1, 12, out int month)
            || !TryReadWholeNumber(values[2], 1, 31, out int day)
            || day > DateTime.DaysInMonth(year, month)
            || values[3] is < 0 or >= 86_400)
        {
            error = "The simulator returned an invalid Zulu date or time.";
            return false;
        }

        simulatorTime = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero)
            .AddSeconds(values[3]);
        return true;
    }

    public static bool TryParseBooleanValue(
        SimConnectObjectDataMessage objectData,
        out bool value,
        out string? error)
    {
        value = false;
        if (!TryParseFloat64Values(objectData, 1, out ImmutableArray<double> values, out error))
        {
            return false;
        }

        if (values[0] is < 0 or > 1)
        {
            error = "The simulator returned a Boolean value outside zero through one.";
            return false;
        }

        value = IsTrue(values[0]);
        return true;
    }

    public static bool TryParseTransponderCode(
        SimConnectObjectDataMessage objectData,
        out string? code,
        out string? error)
    {
        code = null;
        if (!TryParseFloat64Values(objectData, 1, out ImmutableArray<double> values, out error))
        {
            return false;
        }

        if (!TryReadWholeNumber(values[0], 0, 0x7777, out int encoded)
            || (encoded & 0x8888) != 0)
        {
            error = "The simulator returned an invalid BCO16 transponder code.";
            return false;
        }

        code = encoded.ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    public static bool TryParseFloat64Values(
        SimConnectObjectDataMessage objectData,
        int expectedCount,
        out ImmutableArray<double> values,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(objectData);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedCount);
        values = [];
        error = null;

        int expectedBytes = checked(expectedCount * sizeof(double));
        if (objectData.DefinitionCount != expectedCount || objectData.Payload.Length != expectedBytes)
        {
            error = $"Expected {expectedCount} Float64 fields and {expectedBytes} bytes.";
            return false;
        }

        ImmutableArray<double>.Builder builder = ImmutableArray.CreateBuilder<double>(expectedCount);
        ReadOnlySpan<byte> payload = objectData.Payload.Span;
        for (int index = 0; index < expectedCount; index++)
        {
            double value = ReadDouble(payload, index * sizeof(double));
            if (!double.IsFinite(value))
            {
                error = $"Float64 field {index} is not finite.";
                return false;
            }

            builder.Add(value);
        }

        values = builder.MoveToImmutable();
        return true;
    }

    private static bool TryParseExpectedHeader(
        ReadOnlySpan<byte> packet,
        SimConnectReceiveId expectedId,
        int minimumSize,
        out SimConnectReceiveHeader? header,
        out string? error)
    {
        if (!TryParseHeader(packet, out header, out error))
        {
            return false;
        }

        if (header!.Id != expectedId)
        {
            error = $"Expected message ID {(uint)expectedId} but received {(uint)header.Id}.";
            return false;
        }

        if (header.Size < minimumSize)
        {
            error = $"Message ID {(uint)expectedId} is shorter than its {minimumSize}-byte fixed layout.";
            return false;
        }

        return true;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));

    private static double ReadDouble(ReadOnlySpan<byte> bytes, int offset)
    {
        long bits = BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(offset, sizeof(double)));
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static string DecodeFixedAscii(ReadOnlySpan<byte> bytes)
    {
        int terminator = bytes.IndexOf((byte)0);
        ReadOnlySpan<byte> value = terminator < 0 ? bytes : bytes[..terminator];
        return Encoding.ASCII.GetString(value).Trim();
    }

    private static bool TryReadWholeNumber(double value, int minimum, int maximum, out int result)
    {
        double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (value < minimum || value > maximum || Math.Abs(value - rounded) > 0.001)
        {
            result = default;
            return false;
        }

        result = checked((int)rounded);
        return true;
    }

    private static bool IsTrue(double value) => value >= 0.5;
}
