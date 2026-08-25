using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

/// <summary>
/// The payer-facing view of an invoice (<c>GET /v1/pay/{id}</c>, <c>POST /v1/pay/{id}/select</c>,
/// <c>POST /v1/link/{id}/checkout</c>): the same invoice as <see cref="Payment"/> minus every
/// merchant-only field (commission, merchant amount, payer e-mail, additional data, documents).
/// </summary>
public sealed record PublicPayment
{
    /// <summary>Our payment id.</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>The merchant's order number.</summary>
    [JsonPropertyName("order_id")]
    public string OrderId { get; init; } = string.Empty;

    /// <summary>Invoice lifecycle status.</summary>
    [JsonPropertyName("status")]
    public PaymentStatus Status { get; init; }

    /// <summary>True — the status is final and will not change again.</summary>
    [JsonPropertyName("is_final")]
    public bool IsFinal { get; init; }

    /// <summary>Amount due in the price currency. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Price currency: fiat or a coin.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Settlement network; empty until the payer picks one on a multi-network invoice.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>How much must be sent in the payment crypto. Decimal string.</summary>
    [JsonPropertyName("payer_amount")]
    public string PayerAmount { get; init; } = string.Empty;

    /// <summary>Currency the customer pays in; empty until a coin is chosen.</summary>
    [JsonPropertyName("payer_currency")]
    public string PayerCurrency { get; init; } = string.Empty;

    /// <summary>How much has already been confirmed as paid, in the payment crypto. Decimal string.</summary>
    [JsonPropertyName("amount_paid")]
    public string AmountPaid { get; init; } = string.Empty;

    /// <summary>How much is still left to pay (due − paid). Decimal string.</summary>
    [JsonPropertyName("amount_remaining")]
    public string AmountRemaining { get; init; } = string.Empty;

    /// <summary>Address the customer sends the funds to.</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    /// <summary>XRP only: numeric destination tag the customer MUST include. Empty elsewhere.</summary>
    [JsonPropertyName("destination_tag")]
    public string DestinationTag { get; init; } = string.Empty;

    /// <summary>XLM/TON only: numeric memo the customer MUST include. Empty elsewhere.</summary>
    [JsonPropertyName("memo")]
    public string Memo { get; init; } = string.Empty;

    /// <summary>XRP only: address and tag in one X-address string (XLS-5). Empty elsewhere.</summary>
    [JsonPropertyName("address_xaddress")]
    public string AddressXaddress { get; init; } = string.Empty;

    /// <summary>XLM only: address and memo in one muxed address (M…, SEP-23). Empty elsewhere.</summary>
    [JsonPropertyName("address_muxed")]
    public string AddressMuxed { get; init; } = string.Empty;

    /// <summary>Address QR code as a PNG <c>data:</c> URI.</summary>
    [JsonPropertyName("address_qr_code")]
    public string AddressQrCode { get; init; } = string.Empty;

    /// <summary>True — a currency-agnostic link; no currency/network chosen yet.</summary>
    [JsonPropertyName("is_multi")]
    public bool IsMulti { get; init; }

    /// <summary>Link to the hosted payment page.</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    /// <summary>"Back to shop" link shown before payment.</summary>
    [JsonPropertyName("url_return")]
    public string UrlReturn { get; init; } = string.Empty;

    /// <summary>Where to redirect after successful payment.</summary>
    [JsonPropertyName("url_success")]
    public string UrlSuccess { get; init; } = string.Empty;

    /// <summary>When the invoice expires (RFC 3339).</summary>
    [JsonPropertyName("expired_at")]
    public string ExpiredAt { get; init; } = string.Empty;

    /// <summary>When the rate will be refreshed (RFC 3339).</summary>
    [JsonPropertyName("rate_expires_at")]
    public string RateExpiresAt { get; init; } = string.Empty;

    /// <summary>Current number of confirmations of the incoming payment.</summary>
    [JsonPropertyName("confirmations")]
    public int Confirmations { get; init; }

    /// <summary>How many confirmations are needed for crediting.</summary>
    [JsonPropertyName("required_confirmations")]
    public int RequiredConfirmations { get; init; }

    /// <summary>Hash of the incoming transaction (once it is seen).</summary>
    [JsonPropertyName("txid")]
    public string Txid { get; init; } = string.Empty;

    /// <summary>Creation time (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>Time of the last change (RFC 3339).</summary>
    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;
}

/// <summary>
/// <c>/v1/payment/qr</c> and <c>GET /v1/pay/{id}/qr</c>. Every field is empty while the invoice has
/// no real address: sandbox invoices (synthetic <c>sandbox:</c> address) and <c>select</c> invoices
/// still awaiting a network.
/// </summary>
public sealed record QrCode
{
    /// <summary>The QR itself as a PNG <c>data:image/png;base64,…</c> URI.</summary>
    [JsonPropertyName("image")]
    public string Image { get; init; } = string.Empty;

    /// <summary>
    /// What the QR encodes: a payment URI when <see cref="IsUri"/>, otherwise the bare address.
    /// </summary>
    [JsonPropertyName("payload")]
    public string Payload { get; init; } = string.Empty;

    /// <summary>True — <see cref="Payload"/> is a payment URI rather than a plain address.</summary>
    [JsonPropertyName("is_uri")]
    public bool IsUri { get; init; }

    /// <summary>The address the QR points at.</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;
}

/// <summary><c>/v1/payment/send-email</c> — the invoice was e-mailed to the payer.</summary>
public sealed record EmailSent
{
    /// <summary>True — the mail was accepted for delivery.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>Address it was sent to.</summary>
    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    /// <summary>The invoice it was sent for.</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;
}
