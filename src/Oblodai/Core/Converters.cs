using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oblodai;

/// <summary>
/// Reads an integer that the wire may not actually have sent as one. Anything that is not an integral
/// number (a float, a string, a null, an object) decodes to null instead of throwing, so one advisory
/// field the gateway changed the shape of cannot make an authentic, signed event unreadable.
/// </summary>
public sealed class LenientInt64JsonConverter : JsonConverter<long?>
{
    /// <inheritdoc />
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number when reader.TryGetInt64(out var number):
                return number;
            case JsonTokenType.String when long.TryParse(reader.GetString(), out var parsed):
                return parsed;
            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                reader.Skip();
                return null;
            default:
                return null;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value.Value);
    }
}

/// <summary>
/// Writes <see cref="Redaction.Placeholder"/> instead of the value, and reads the value normally. Put
/// it on a model property that carries a secret: the object still hands the secret to whoever asks for
/// the property, but serializing the object — the usual way a payload reaches a log line, a message
/// queue or an error report — cannot spread it.
/// </summary>
public sealed class RedactedStringJsonConverter : JsonConverter<string?>
{
    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : reader.GetString();

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(Redaction.IsRevealing ? value : Redaction.Placeholder);
    }
}

/// <summary>
/// Reads a JSON null as the empty string. Applied automatically to every model property declared
/// <c>string</c> rather than <c>string?</c>, so the type the compiler shows a caller is the truth.
/// </summary>
public sealed class NonNullStringJsonConverter : JsonConverter<string>
{
    /// <summary>The shared instance.</summary>
    public static readonly NonNullStringJsonConverter Instance = new();

    /// <inheritdoc />
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Null => string.Empty,
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            _ => throw new JsonException($"expected a string, got {reader.TokenType}"),
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
