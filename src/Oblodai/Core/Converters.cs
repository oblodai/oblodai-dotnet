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

/// <summary>
/// Reads a decimal exactly or not at all. <see cref="decimal"/> holds 28 significant digits; an amount
/// with more (or a finer scale) would otherwise be rounded silently, and a money SDK must refuse rather
/// than round. Accepts a JSON number or a decimal string, and writes the decimal string (the wire's money
/// form, as every generated money property declares with <c>WriteAsString</c>).
/// </summary>
public sealed class StrictDecimalJsonConverter : JsonConverter<decimal>
{
    /// <summary>Significant digits (and decimal places) a <see cref="decimal"/> always holds exactly.</summary>
    public const int MaxDigits = 28;

    /// <summary>The shared instance.</summary>
    public static readonly StrictDecimalJsonConverter Instance = new();

    /// <inheritdoc />
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string raw = reader.TokenType switch
        {
            JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(
                reader.HasValueSequence ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence) : reader.ValueSpan),
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            _ => throw new JsonException($"expected a decimal, got {reader.TokenType}"),
        };
        return Parse(raw);
    }

    /// <summary>Writes the decimal string the wire carries money as, keeping its scale (<c>25.10</c>).</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The amount.</param>
    /// <param name="options">Serializer options.</param>
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Parse a decimal, refusing one that <see cref="decimal"/> cannot hold exactly.</summary>
    /// <param name="raw">The number as written on the wire.</param>
    /// <exception cref="JsonException">Not a decimal, or more precision than <see cref="decimal"/> holds.</exception>
    public static decimal Parse(string raw)
    {
        var text = raw.Trim();
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            throw new JsonException($"\"{Shorten(text)}\" is not a decimal amount this SDK can hold");
        }

        var mantissa = text.TrimStart('-', '+');
        var exponent = 0;
        var e = mantissa.IndexOfAny(['e', 'E']);
        if (e >= 0)
        {
            if (!int.TryParse(mantissa[(e + 1)..], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out exponent))
            {
                throw new JsonException($"\"{Shorten(text)}\" is not a decimal amount this SDK can hold");
            }

            mantissa = mantissa[..e];
        }

        var dot = mantissa.IndexOf('.');
        var integer = dot < 0 ? mantissa : mantissa[..dot];
        var fraction = dot < 0 ? string.Empty : mantissa[(dot + 1)..].TrimEnd('0');
        var digits = (integer + fraction).TrimStart('0');
        var scale = fraction.Length - exponent;
        if (digits.Length > MaxDigits || scale > MaxDigits)
        {
            throw new JsonException(
                $"amount \"{Shorten(text)}\" has more precision than a .NET decimal holds ({MaxDigits} digits); "
                + "refused rather than rounded");
        }

        return value;
    }

    private static string Shorten(string text) => text.Length <= 64 ? text : text[..61] + "...";
}
