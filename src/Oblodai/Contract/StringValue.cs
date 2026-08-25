using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oblodai.Contract;

/// <summary>
/// Contract of the open vocabularies (statuses, networks, fee bearers, …). They are wrappers around
/// the wire string rather than C# enums: the gateway may add a value at any time, and a member the
/// snapshot does not know must still round-trip instead of throwing or collapsing to a default.
/// </summary>
/// <typeparam name="TSelf">The wrapper type itself.</typeparam>
public interface IStringValue<TSelf>
    where TSelf : IStringValue<TSelf>
{
    /// <summary>Wrap a wire value (known or not).</summary>
    static abstract TSelf FromValue(string value);

    /// <summary>The wire value.</summary>
    string Value { get; }
}

/// <summary>Serializes an <see cref="IStringValue{TSelf}"/> wrapper as the bare wire string.</summary>
/// <typeparam name="T">The wrapper type.</typeparam>
public sealed class StringValueJsonConverter<T> : JsonConverter<T>
    where T : IStringValue<T>
{
    /// <inheritdoc />
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => T.FromValue(reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value ?? string.Empty);
}
