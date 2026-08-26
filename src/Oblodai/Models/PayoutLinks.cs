using System.Text;
using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

/// <summary>
/// A payout link (cheque), as <c>/v1/payout/link</c>, <c>/info</c>, <c>/list</c>, <c>/cancel</c> and
/// batch elements render it.
/// </summary>
public sealed record PayoutLink
{
    /// <summary>Link id.</summary>
    [JsonPropertyName("link_id")]
    public string LinkId { get; init; } = string.Empty;

    /// <summary>
    /// Lifecycle: <c>funded</c> | <c>claiming</c> | <c>claimed</c> | <c>expired</c> | <c>cancelled</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public PayoutLinkStatus Status { get; init; }

    /// <summary>Amount the recipient claims. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Asset code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Network the claim will be paid on.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Network fee. Decimal string; null while the asset cannot be priced.</summary>
    [JsonPropertyName("commission")]
    public string? Commission { get; init; }

    /// <summary>
    /// What the claim actually pays out. Decimal string; null while the asset cannot be priced.
    /// </summary>
    [JsonPropertyName("payer_amount")]
    public string? PayerAmount { get; init; }

    /// <summary>Who bears the network fee: <c>recipient</c> or <c>merchant</c>.</summary>
    [JsonPropertyName("fee_bearer")]
    public FeeBearer FeeBearer { get; init; }

    /// <summary>Pricing mode the fee was computed with.</summary>
    [JsonPropertyName("fee_type")]
    public string FeeType { get; init; } = string.Empty;

    /// <summary>Your reference for the link.</summary>
    [JsonPropertyName("reference")]
    public string Reference { get; init; } = string.Empty;

    /// <summary>Title shown to the recipient.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Note shown to the recipient.</summary>
    [JsonPropertyName("note")]
    public string Note { get; init; } = string.Empty;

    /// <summary>True — claiming requires the passcode.</summary>
    [JsonPropertyName("passcode_protected")]
    public bool PasscodeProtected { get; init; }

    /// <summary>When the link stops being claimable (RFC 3339).</summary>
    [JsonPropertyName("expires_at")]
    public string ExpiresAt { get; init; } = string.Empty;

    /// <summary>Creation time (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>
    /// The secret the recipient claims with — create and batch-create only, shown once. Whoever holds it
    /// can take the money, so it is treated as a bearer token: printing or serializing this record writes
    /// <c>[redacted]</c>. Read the property, or use <see cref="OblodaiJson.SerializeWithSecrets"/>.
    /// </summary>
    [JsonPropertyName("claim_token")]
    [JsonConverter(typeof(RedactedStringJsonConverter))]
    public string? ClaimToken { get; init; }

    /// <summary>
    /// Ready-made claim URL carrying the token. Create and batch-create only. Redacted like
    /// <see cref="ClaimToken"/>, which it embeds.
    /// </summary>
    [JsonPropertyName("claim_url")]
    [JsonConverter(typeof(RedactedStringJsonConverter))]
    public string? ClaimUrl { get; init; }

    /// <summary>The batch this link was created in. Batch-create only.</summary>
    [JsonPropertyName("batch_id")]
    public string? BatchId { get; init; }

    /// <summary>Set once claimed: the payout that paid the recipient.</summary>
    [JsonPropertyName("payout_id")]
    public string? PayoutId { get; init; }

    /// <summary>Set once claimed: the address the recipient claimed to.</summary>
    [JsonPropertyName("claim_address")]
    public string? ClaimAddress { get; init; }

    /// <summary>Recipient e-mail, when the link was sent by mail.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// The generated passcode, shown once on create when <c>passcode: "auto"</c> was requested. Redacted
    /// in <c>ToString()</c> and in the default JSON path, like the claim token.
    /// </summary>
    [JsonPropertyName("passcode")]
    [JsonConverter(typeof(RedactedStringJsonConverter))]
    public string? Passcode { get; init; }

    /// <summary>Prints the link without the three values that let a stranger claim it.</summary>
    /// <param name="builder">Buffer the record's <c>ToString()</c> writes into.</param>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("LinkId = ").Append(LinkId)
            .Append(", Status = ").Append(Status)
            .Append(", Amount = ").Append(Amount)
            .Append(", Currency = ").Append(Currency)
            .Append(", Network = ").Append(Network)
            .Append(", Commission = ").Append(Commission)
            .Append(", PayerAmount = ").Append(PayerAmount)
            .Append(", FeeBearer = ").Append(FeeBearer)
            .Append(", FeeType = ").Append(FeeType)
            .Append(", Reference = ").Append(Reference)
            .Append(", Title = ").Append(Title)
            .Append(", Note = ").Append(Note)
            .Append(", PasscodeProtected = ").Append(PasscodeProtected)
            .Append(", ExpiresAt = ").Append(ExpiresAt)
            .Append(", CreatedAt = ").Append(CreatedAt)
            .Append(", ").AppendRedacted(nameof(ClaimToken), ClaimToken is not null)
            .Append(", ").AppendRedacted(nameof(ClaimUrl), ClaimUrl is not null)
            .Append(", BatchId = ").Append(BatchId)
            .Append(", PayoutId = ").Append(PayoutId)
            .Append(", ClaimAddress = ").Append(ClaimAddress)
            .Append(", Email = ").Append(Email)
            .Append(", ").AppendRedacted(nameof(Passcode), Passcode is not null);
        return true;
    }
}

/// <summary><c>GET /v1/claim/{token}</c> — what the recipient sees before claiming.</summary>
public sealed record ClaimPreview
{
    /// <summary>The link's status.</summary>
    [JsonPropertyName("status")]
    public PayoutLinkStatus Status { get; init; }

    /// <summary>True — the link can be claimed right now.</summary>
    [JsonPropertyName("claimable")]
    public bool Claimable { get; init; }

    /// <summary>Amount on offer. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Asset code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Network the claim will be paid on.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Network fee. Decimal string; null while the asset cannot be priced.</summary>
    [JsonPropertyName("commission")]
    public string? Commission { get; init; }

    /// <summary>What the claim actually pays out. Decimal string; null when unpriceable.</summary>
    [JsonPropertyName("payer_amount")]
    public string? PayerAmount { get; init; }

    /// <summary>Who bears the network fee.</summary>
    [JsonPropertyName("fee_bearer")]
    public FeeBearer FeeBearer { get; init; }

    /// <summary>Pricing mode the fee was computed with.</summary>
    [JsonPropertyName("fee_type")]
    public string FeeType { get; init; } = string.Empty;

    /// <summary>Title shown to the recipient.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Note shown to the recipient.</summary>
    [JsonPropertyName("note")]
    public string Note { get; init; } = string.Empty;

    /// <summary>When the link stops being claimable (RFC 3339).</summary>
    [JsonPropertyName("expires_at")]
    public string ExpiresAt { get; init; } = string.Empty;
}

/// <summary><c>POST /v1/claim/{token}</c> — the payout minted by a claim.</summary>
public sealed record ClaimResult
{
    /// <summary>
    /// The payout that pays the recipient — follow it with <c>Payouts.InfoAsync</c> by this id.
    /// </summary>
    [JsonPropertyName("payout_id")]
    public string PayoutId { get; init; } = string.Empty;

    /// <summary>The LINK's status after the claim (<c>claimed</c>), not the payout's.</summary>
    [JsonPropertyName("status")]
    public PayoutLinkStatus Status { get; init; }

    /// <summary>Address the recipient claimed to.</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    /// <summary>Amount claimed. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Asset code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Network the payout goes out on.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Network fee. Decimal string; null when unpriceable.</summary>
    [JsonPropertyName("commission")]
    public string? Commission { get; init; }

    /// <summary>What actually reaches the recipient. Decimal string; null when unpriceable.</summary>
    [JsonPropertyName("payer_amount")]
    public string? PayerAmount { get; init; }

    /// <summary>Who bore the network fee.</summary>
    [JsonPropertyName("fee_bearer")]
    public FeeBearer FeeBearer { get; init; }

    /// <summary>Pricing mode the fee was computed with.</summary>
    [JsonPropertyName("fee_type")]
    public string FeeType { get; init; } = string.Empty;
}
