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
    /// The secret the recipient claims with — create and batch-create only, shown once. Treat it
    /// like a bearer token.
    /// </summary>
    [JsonPropertyName("claim_token")]
    public string? ClaimToken { get; init; }

    /// <summary>Ready-made claim URL carrying the token. Create and batch-create only.</summary>
    [JsonPropertyName("claim_url")]
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
    /// The generated passcode, shown once on create when <c>passcode: "auto"</c> was requested.
    /// </summary>
    [JsonPropertyName("passcode")]
    public string? Passcode { get; init; }
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
    /// The payout that pays the recipient — follow it with <c>payouts.info</c> by this id.
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

/// <summary>An invoice spawned by a payment link — the short form <c>/info</c> lists.</summary>
public sealed record PaymentLinkPayment
{
    /// <summary>The invoice id.</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>The invoice's order number, when the payer supplied one.</summary>
    [JsonPropertyName("order_id")]
    public string? OrderId { get; init; }

    /// <summary>Amount due in the price currency. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Price currency.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Invoice lifecycle status.</summary>
    [JsonPropertyName("status")]
    public PaymentStatus Status { get; init; }

    /// <summary>Creation time (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;
}

/// <summary>
/// A payment link, as <c>/v1/payment/link/info</c> and <c>/list</c> render it. Which amount fields
/// are filled depends on <see cref="AmountMode"/>.
/// </summary>
public sealed record PaymentLink
{
    /// <summary>Link id.</summary>
    [JsonPropertyName("link_id")]
    public string LinkId { get; init; } = string.Empty;

    /// <summary>Public URL of the payment page — hand it to the buyer as a button, mail or QR.</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    /// <summary>False — the link is switched off and refuses new invoices.</summary>
    [JsonPropertyName("active")]
    public bool Active { get; init; }

    /// <summary>Title shown on the page.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Description shown on the page.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>How the amount is decided: <c>fixed</c>, <c>range</c> or <c>any</c>.</summary>
    [JsonPropertyName("amount_mode")]
    public AmountMode AmountMode { get; init; }

    /// <summary>Price currency.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary><c>fixed</c> links: the amount charged. Decimal string.</summary>
    [JsonPropertyName("amount_fixed")]
    public string? AmountFixed { get; init; }

    /// <summary><c>range</c> links: the lower bound. Decimal string.</summary>
    [JsonPropertyName("min_amount")]
    public string? MinAmount { get; init; }

    /// <summary><c>range</c> links: the upper bound. Decimal string.</summary>
    [JsonPropertyName("max_amount")]
    public string? MaxAmount { get; init; }

    /// <summary>Settlement asset the link is pinned to, when it is pinned.</summary>
    [JsonPropertyName("pinned_currency")]
    public string? PinnedCurrency { get; init; }

    /// <summary>Settlement network the link is pinned to, when it is pinned.</summary>
    [JsonPropertyName("pinned_network")]
    public Network? PinnedNetwork { get; init; }

    /// <summary>When the link stops accepting payers (RFC 3339); absent when it never expires.</summary>
    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; init; }

    /// <summary>
    /// Signed link to a PDF poster with the payment QR (for printing at the till). Empty if
    /// document generation is not enabled.
    /// </summary>
    [JsonPropertyName("document_url")]
    public string DocumentUrl { get; init; } = string.Empty;

    /// <summary>Creation time (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>Invoices spawned by this link. <c>/v1/payment/link/info</c> only.</summary>
    [JsonPropertyName("payments")]
    public IReadOnlyList<PaymentLinkPayment>? Payments { get; init; }
}

/// <summary><c>POST /v1/payment/link</c> acknowledgement.</summary>
public sealed record PaymentLinkCreated
{
    /// <summary>Link id.</summary>
    [JsonPropertyName("link_id")]
    public string LinkId { get; init; } = string.Empty;

    /// <summary>Public URL of the payment page.</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    /// <summary>Signed link to a PDF poster with the payment QR.</summary>
    [JsonPropertyName("document_url")]
    public string DocumentUrl { get; init; } = string.Empty;
}

/// <summary><c>POST /v1/payment/link/toggle</c> — the link's new on/off state.</summary>
public sealed record PaymentLinkToggled
{
    /// <summary>Link id.</summary>
    [JsonPropertyName("link_id")]
    public string LinkId { get; init; } = string.Empty;

    /// <summary>The state the link is in now.</summary>
    [JsonPropertyName("active")]
    public bool Active { get; init; }
}

/// <summary><c>GET /v1/link/{id}</c> — the payer-facing view of a payment link.</summary>
public sealed record PublicPaymentLink
{
    /// <summary>Link id.</summary>
    [JsonPropertyName("link_id")]
    public string LinkId { get; init; } = string.Empty;

    /// <summary>Title shown on the page.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Description shown on the page.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>How the amount is decided: <c>fixed</c>, <c>range</c> or <c>any</c>.</summary>
    [JsonPropertyName("amount_mode")]
    public AmountMode AmountMode { get; init; }

    /// <summary>Price currency.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary><c>fixed</c> links: the amount charged. Decimal string.</summary>
    [JsonPropertyName("amount_fixed")]
    public string? AmountFixed { get; init; }

    /// <summary><c>range</c> links: the lower bound. Decimal string.</summary>
    [JsonPropertyName("min_amount")]
    public string? MinAmount { get; init; }

    /// <summary><c>range</c> links: the upper bound. Decimal string.</summary>
    [JsonPropertyName("max_amount")]
    public string? MaxAmount { get; init; }

    /// <summary>Settlement asset the link is pinned to, when it is pinned.</summary>
    [JsonPropertyName("pinned_currency")]
    public string? PinnedCurrency { get; init; }

    /// <summary>Settlement network the link is pinned to, when it is pinned.</summary>
    [JsonPropertyName("pinned_network")]
    public Network? PinnedNetwork { get; init; }
}
