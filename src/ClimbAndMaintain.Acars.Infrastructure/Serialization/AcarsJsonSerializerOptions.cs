using System.Text.Json;
using System.Text.Json.Serialization;
using ClimbAndMaintain.Acars.Application.Contracts;
using ClimbAndMaintain.Acars.Core.Flights;
using ClimbAndMaintain.Acars.Core.Telemetry;

namespace ClimbAndMaintain.Acars.Infrastructure.Serialization;

internal static class AcarsJsonSerializerOptions
{
    public static JsonSerializerOptions Create()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new SingleValueConverter<Altitude>("feet", value => new Altitude(value), value => value.Feet));
        options.Converters.Add(new SingleValueConverter<Distance>(
            "nauticalMiles",
            value => new Distance(value),
            value => value.NauticalMiles));
        options.Converters.Add(new SingleValueConverter<Speed>("knots", value => new Speed(value), value => value.Knots));
        options.Converters.Add(new SingleValueConverter<VerticalSpeed>(
            "feetPerMinute",
            value => new VerticalSpeed(value),
            value => value.FeetPerMinute));
        options.Converters.Add(new SingleValueConverter<Heading>(
            "degrees",
            value => new Heading(value),
            value => value.Degrees));
        options.Converters.Add(new SingleValueConverter<FuelMass>(
            "kilograms",
            value => new FuelMass(value),
            value => value.Kilograms));
        options.Converters.Add(new SingleValueConverter<FuelFlow>(
            "kilogramsPerHour",
            value => new FuelFlow(value),
            value => value.KilogramsPerHour));
        options.Converters.Add(new SingleValueConverter<Ratio>("value", value => new Ratio(value), value => value.Value));
        options.Converters.Add(new GeoPositionConverter());
        options.Converters.Add(new GuidValueConverter<FlightSessionId>(
            value => new FlightSessionId(value),
            value => value.Value));
        options.Converters.Add(new GuidValueConverter<FlightEventId>(
            value => new FlightEventId(value),
            value => value.Value));
        options.Converters.Add(new GuidValueConverter<PositionReportId>(
            value => new PositionReportId(value),
            value => value.Value));
        options.Converters.Add(new GuidValueConverter<OutboxItemId>(
            value => new OutboxItemId(value),
            value => value.Value));
        return options;
    }

    private sealed class SingleValueConverter<T> : JsonConverter<T>
    {
        private readonly string propertyName;
        private readonly Func<double, T> create;
        private readonly Func<T, double> getValue;

        public SingleValueConverter(string propertyName, Func<double, T> create, Func<T, double> getValue)
        {
            this.propertyName = propertyName;
            this.create = create;
            this.getValue = getValue;
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return create(reader.GetDouble());
            }

            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (!document.RootElement.TryGetProperty(propertyName, out JsonElement value))
            {
                throw new JsonException(FormattableString.Invariant($"Missing '{propertyName}' measurement property."));
            }

            return create(value.GetDouble());
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber(propertyName, getValue(value));
            writer.WriteEndObject();
        }
    }

    private sealed class GeoPositionConverter : JsonConverter<GeoPosition>
    {
        public override GeoPosition Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            return new GeoPosition(
                root.GetProperty("latitudeDegrees").GetDouble(),
                root.GetProperty("longitudeDegrees").GetDouble());
        }

        public override void Write(Utf8JsonWriter writer, GeoPosition value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("latitudeDegrees", value.LatitudeDegrees);
            writer.WriteNumber("longitudeDegrees", value.LongitudeDegrees);
            writer.WriteEndObject();
        }
    }

    private sealed class GuidValueConverter<T> : JsonConverter<T>
    {
        private readonly Func<Guid, T> create;
        private readonly Func<T, Guid> getValue;

        public GuidValueConverter(Func<Guid, T> create, Func<T, Guid> getValue)
        {
            this.create = create;
            this.getValue = getValue;
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return create(reader.GetGuid());
            }

            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            return create(document.RootElement.GetProperty("value").GetGuid());
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(getValue(value));
    }
}
