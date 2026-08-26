using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

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
