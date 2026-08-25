using System.Text.Json.Serialization;

namespace Oblodai.Models;

/// <summary>An API key pair as minted by onboarding. The secret is shown once — store it at once.</summary>
public sealed record ApiKeyPair
{
    /// <summary>Public key id — the <c>public_id</c> half of the signature.</summary>
    [JsonPropertyName("public_id")]
    public string PublicId { get; init; } = string.Empty;

    /// <summary>The signing secret. Shown ONCE and never again.</summary>
    [JsonPropertyName("secret")]
    public string Secret { get; init; } = string.Empty;

    /// <summary><c>api</c> — the unified key kind current merchants receive.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;
}

/// <summary><c>POST /v1/merchants</c> — a freshly provisioned merchant and its keys.</summary>
public sealed record MerchantOnboarded
{
    /// <summary>The new merchant.</summary>
    [JsonPropertyName("merchant_id")]
    public string MerchantId { get; init; } = string.Empty;

    /// <summary>Its default project (store).</summary>
    [JsonPropertyName("project_id")]
    public string ProjectId { get; init; } = string.Empty;

    /// <summary>
    /// The unified key — the same pair as <see cref="PaymentKey"/> and <see cref="PayoutKey"/> for
    /// merchants created now.
    /// </summary>
    [JsonPropertyName("api_key")]
    public ApiKeyPair ApiKey { get; init; } = new();

    /// <summary>Key scoped to payment routes (historically separate).</summary>
    [JsonPropertyName("payment_key")]
    public ApiKeyPair PaymentKey { get; init; } = new();

    /// <summary>Key scoped to payout routes (historically separate).</summary>
    [JsonPropertyName("payout_key")]
    public ApiKeyPair PayoutKey { get; init; } = new();
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

    /// <summary>The unified <c>test_</c> key.</summary>
    [JsonPropertyName("api_key")]
    public ApiKeyPair ApiKey { get; init; } = new();

    /// <summary>Key scoped to payment routes.</summary>
    [JsonPropertyName("payment_key")]
    public ApiKeyPair PaymentKey { get; init; } = new();

    /// <summary>Key scoped to payout routes.</summary>
    [JsonPropertyName("payout_key")]
    public ApiKeyPair PayoutKey { get; init; } = new();

    /// <summary>False when the dev store already existed — the call is idempotent.</summary>
    [JsonPropertyName("created")]
    public bool Created { get; init; }
}
