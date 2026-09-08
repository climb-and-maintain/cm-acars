using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClimbAndMaintain.Acars.PhpVms;

public static class PhpVmsClientId
{
    private const int WireByteCount = 12;

    public static string Encode(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A phpVMS client ID cannot be derived from an empty GUID.", nameof(value));
        }

        Span<byte> valueBytes = stackalloc byte[16];
        _ = value.TryWriteBytes(valueBytes);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        _ = SHA256.HashData(valueBytes, hash);
        return Convert
            .ToBase64String(hash[..WireByteCount])
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

internal sealed class PhpVmsClientIdJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException("Compact phpVMS client IDs cannot be expanded into GUIDs.");

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(PhpVmsClientId.Encode(value));
}
