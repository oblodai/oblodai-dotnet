using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oblodai;

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
