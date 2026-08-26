using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Oblodai;

/// <summary>
/// The single JSON configuration the SDK uses on the wire. Wire names are spelled out on every model
/// with <see cref="JsonPropertyNameAttribute"/> — nothing is renamed by a policy — and members that
/// are null are omitted, so an unset optional never reaches the gateway as an explicit <c>null</c>.
/// </summary>
public static class OblodaiJson
{
    /// <summary>Serializer options for request bodies and response models.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { NonNullableStringsNeverDecodeToNull },
        },
    };

    /// <summary>
    /// A model property declared <c>string</c> (not <c>string?</c>) must never hold null. The serializer
    /// happily writes a wire <c>null</c> into one, and the null then surfaces far away — as a
    /// <c>NullReferenceException</c> in the caller's code, or as an amount helper complaining about an
    /// empty string it was never given. A gateway that sends null for a field it documents as always
    /// present is reporting "no value", which for these fields is the empty string.
    /// <para>
    /// Properties declared nullable keep their null: there the distinction is the point. Properties with
    /// their own converter (the redacted secrets) are left alone.
    /// </para>
    /// </summary>
    /// <param name="typeInfo">Contract being built for one model type.</param>
    private static void NonNullableStringsNeverDecodeToNull(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        var nullability = new NullabilityInfoContext();
        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType != typeof(string)
                || property.CustomConverter is not null
                || property.AttributeProvider is not PropertyInfo member
                || nullability.Create(member).ReadState == NullabilityState.Nullable)
            {
                continue;
            }

            property.CustomConverter = NonNullStringJsonConverter.Instance;
        }
    }

    /// <summary>
    /// Serialize a model with its secret-bearing properties written out in full. The default path
    /// (<see cref="JsonSerializer"/> with <see cref="Options"/>, or any structured logger) writes
    /// <c>[redacted]</c> for a webhook secret, an API key secret, a cheque passcode and a claim token or
    /// URL — this is the one, explicit way to get the real values, for the code that stores or mails
    /// them. Do not send its output to a log.
    /// </summary>
    /// <param name="value">The model.</param>
    public static string SerializeWithSecrets(object value)
        => Redaction.Reveal(() => JsonSerializer.Serialize(value, value.GetType(), Options));

    /// <summary>Serialize a request body exactly once, so the signed bytes and the sent bytes agree.</summary>
    /// <param name="body">Body object, or null.</param>
    /// <param name="method">HTTP method; GET bodies are always empty.</param>
    public static string SerializeBody(object? body, string method)
    {
        if (method == "GET")
        {
            return string.Empty;
        }

        return body is null ? "{}" : JsonSerializer.Serialize(body, body.GetType(), Options);
    }

    /// <summary>Deserialize a decoded <c>result</c> payload into a model.</summary>
    /// <typeparam name="T">Model type.</typeparam>
    /// <param name="element">The <c>result</c> element.</param>
    /// <param name="httpStatus">Status of the response the element came from (for error reporting).</param>
    public static T Deserialize<T>(JsonElement element, int httpStatus = 200)
    {
        try
        {
            var value = element.Deserialize<T>(Options);
            if (value is null)
            {
                throw new ContractException($"the result payload decoded to null as {typeof(T).Name}", httpStatus);
            }

            return value;
        }
        catch (JsonException ex)
        {
            throw new ContractException(
                $"the result payload does not decode as {typeof(T).Name}: {ex.Message}", httpStatus, element.ToString());
        }
    }
}
