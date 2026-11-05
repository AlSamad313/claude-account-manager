using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfileCore;

/// Source-generated, so cpm can be compiled ahead of time.
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Registry))]
internal partial class CoreJson : JsonSerializerContext;

/// ISO 8601 in UTC with whole seconds, as Swift's .iso8601 strategy writes and reads.
internal sealed class IsoDate : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
        DateTimeOffset.Parse(r.GetString()!, CultureInfo.InvariantCulture).UtcDateTime;

    public override void Write(Utf8JsonWriter w, DateTime v, JsonSerializerOptions o) =>
        w.WriteStringValue(v.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}

internal sealed class IsoDateOrNull : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
        r.TokenType == JsonTokenType.Null ? null : DateTimeOffset.Parse(r.GetString()!, CultureInfo.InvariantCulture).UtcDateTime;

    public override void Write(Utf8JsonWriter w, DateTime? v, JsonSerializerOptions o)
    {
        if (v == null) w.WriteNullValue();
        else w.WriteStringValue(v.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }
}
