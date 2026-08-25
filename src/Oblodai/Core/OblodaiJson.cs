using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    };

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
