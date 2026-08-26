using System.Text;
using System.Text.Json.Serialization;

namespace Oblodai.Models;

/// <summary>The API key pair as minted by onboarding. The secret is shown once — store it at once.</summary>
public sealed record ApiKeyPair
{
    /// <summary>Public key id — the <c>public_id</c> half of the signature.</summary>
    [JsonPropertyName("public_id")]
    public string PublicId { get; init; } = string.Empty;

    /// <summary>
    /// The signing secret. Shown ONCE and never again. Printing or serializing this record writes
    /// <c>[redacted]</c>; read the property, or use <see cref="OblodaiJson.SerializeWithSecrets"/> to
    /// hand it to your key store.
    /// </summary>
    [JsonPropertyName("secret")]
    [JsonConverter(typeof(RedactedStringJsonConverter))]
    public string Secret { get; init; } = string.Empty;

    /// <summary>Prints the pair without its secret.</summary>
    /// <param name="builder">Buffer the record's <c>ToString()</c> writes into.</param>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("PublicId = ").Append(PublicId)
            .Append(", ").AppendRedacted(nameof(Secret));
        return true;
    }
}

/// <summary><c>POST /v1/merchants</c> — a freshly provisioned merchant and its API key.</summary>
public sealed record MerchantOnboarded
{
    /// <summary>The new merchant.</summary>
    [JsonPropertyName("merchant_id")]
    public string MerchantId { get; init; } = string.Empty;

    /// <summary>Its default project (store).</summary>
    [JsonPropertyName("project_id")]
    public string ProjectId { get; init; } = string.Empty;

    /// <summary>The merchant's API key: the one pair that signs every route.</summary>
    [JsonPropertyName("api_key")]
    public ApiKeyPair ApiKey { get; init; } = new();
}

/// <summary>
/// <c>POST /v1/merchants/{id}/sandbox</c> — the merchant's dev store and its <c>test_</c> key. The
/// same shape as <see cref="MerchantOnboarded"/> plus <see cref="Created"/>.
/// </summary>
public sealed record SandboxStore
{
    /// <summary>The merchant the dev store belongs to.</summary>
    [JsonPropertyName("merchant_id")]
    public string MerchantId { get; init; } = string.Empty;

    /// <summary>The dev store's project.</summary>
    [JsonPropertyName("project_id")]
    public string ProjectId { get; init; } = string.Empty;

    /// <summary>The dev store's <c>test_</c> API key.</summary>
    [JsonPropertyName("api_key")]
    public ApiKeyPair ApiKey { get; init; } = new();

    /// <summary>False when the dev store already existed — the call is idempotent.</summary>
    [JsonPropertyName("created")]
    public bool Created { get; init; }
}
