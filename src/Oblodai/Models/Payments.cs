using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

/// <summary>One confirmed on-chain transfer attributed to an invoice.</summary>
public sealed record PaymentTx
{
    /// <summary>Transaction hash.</summary>
    [JsonPropertyName("txid")]
    public string Txid { get; init; } = string.Empty;

    /// <summary>Transfer amount in the payment currency. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>
    /// Network the transfer arrived on. On EVM it may differ from the invoice network: a deposit is
    /// credited on another chain with the same address too.
    /// </summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Height of the block the transfer was confirmed in.</summary>
    [JsonPropertyName("height")]
    public long Height { get; init; }

    /// <summary>When the transfer was credited (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;
}

/// <summary>
/// A refund issued against an invoice — a payout in disguise; the full detail is on
/// <c>payouts.info</c> by <see cref="Uuid"/>.
/// </summary>
public sealed record PaymentRefund
{
    /// <summary>Refund id (it is a payout).</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>Address the funds were returned to.</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    /// <summary>Refund amount in the payment coin. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Status of the refund payout.</summary>
    [JsonPropertyName("status")]
    public PayoutStatus Status { get; init; }

    /// <summary>True — the refund is in a terminal status.</summary>
    [JsonPropertyName("is_final")]
    public bool IsFinal { get; init; }

    /// <summary>On-chain transaction hash (appears after sending).</summary>
    [JsonPropertyName("txid")]
    public string Txid { get; init; } = string.Empty;

    /// <summary>Creation time (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;
}

/// <summary>
/// An invoice, as <c>/v1/payment</c>, <c>/v1/payment/info</c>, <c>/v1/payment/history</c> and
/// <c>/v1/payment/cancel</c> render it. <see cref="Refunds"/> and <see cref="RefundStatus"/> are
/// present on <c>info</c> only.
/// </summary>
public sealed record Payment
{
    /// <summary>Our payment id (use it in <c>info</c> / <c>refund</c>).</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>Your order number, passed at creation.</summary>
    [JsonPropertyName("order_id")]
    public string OrderId { get; init; } = string.Empty;

    /// <summary>
    /// Lifecycle: <c>select</c> (the customer is choosing a currency), <c>created</c> (waiting for
    /// payment), <c>confirm_check</c> (payment seen, waiting for confirmations; with
    /// <see cref="AmountRemaining"/> &gt; 0 it is partial), <c>paid</c>, <c>paid_over</c>
    /// (overpayment), <c>wrong_amount</c> (underpayment, deadline passed), <c>expired</c>,
    /// <c>cancelled</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public PaymentStatus Status { get; init; }

    /// <summary>True — the status is final and will not change again.</summary>
    [JsonPropertyName("is_final")]
    public bool IsFinal { get; init; }

    /// <summary>Amount due in the price currency (for example, in USD). Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>
    /// Price currency: fiat (USD, EUR, RUB, JPY… — see <c>pricing_currencies</c>) or a coin. Tells
    /// what the invoice COSTS, not what it is paid with (that is <see cref="PayerCurrency"/>).
    /// </summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Settlement network; empty until the payer picks one on a multi-network invoice.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>How much must be sent in the payment crypto. Decimal string.</summary>
    [JsonPropertyName("payer_amount")]
    public string PayerAmount { get; init; } = string.Empty;

    /// <summary>
    /// Currency the customer pays in (for example, USDT). Empty for a currency-agnostic invoice
    /// (<see cref="IsMulti"/>) until the customer picks a coin.
    /// </summary>
    [JsonPropertyName("payer_currency")]
    public string PayerCurrency { get; init; } = string.Empty;

    /// <summary>
    /// How much has already been confirmed as paid, in the payment crypto (0 if nothing arrived).
    /// Decimal string.
    /// </summary>
    [JsonPropertyName("amount_paid")]
    public string AmountPaid { get; init; } = string.Empty;

    /// <summary>How much is still left to pay (due − paid); 0 if enough has arrived. Decimal string.</summary>
    [JsonPropertyName("amount_remaining")]
    public string AmountRemaining { get; init; } = string.Empty;

    /// <summary>
    /// Address the customer sends the funds to. On XRP this is the classic r-address of the SHARED
    /// wallet — the payment must carry <see cref="DestinationTag"/>, otherwise the network rejects it.
    /// </summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// XRP only: numeric destination tag the customer MUST include in the transfer (the "tag/memo of
    /// the recipient" field on an exchange or in a wallet). Empty on other networks.
    /// </summary>
    [JsonPropertyName("destination_tag")]
    public string DestinationTag { get; init; } = string.Empty;

    /// <summary>
    /// XLM (Stellar) and TON only: numeric memo the customer MUST include in the transfer. Empty on
    /// other networks.
    /// </summary>
    [JsonPropertyName("memo")]
    public string Memo { get; init; } = string.Empty;

    /// <summary>
    /// XRP only: address and tag together in X-address format (XLS-5); the QR encodes it as well.
    /// Empty on other networks.
    /// </summary>
    [JsonPropertyName("address_xaddress")]
    public string AddressXaddress { get; init; } = string.Empty;

    /// <summary>
    /// XLM only: address and memo together as a muxed address (M…, SEP-23); the QR encodes it as
    /// well. Empty on other networks.
    /// </summary>
    [JsonPropertyName("address_muxed")]
    public string AddressMuxed { get; init; } = string.Empty;

    /// <summary>
    /// Address QR code as a PNG <c>data:</c> URI — usable directly in <c>&lt;img src&gt;</c>. On XRP
    /// it encodes the X-address (address+tag in one string).
    /// </summary>
    [JsonPropertyName("address_qr_code")]
    public string AddressQrCode { get; init; } = string.Empty;

    /// <summary>True — a currency-agnostic link; the customer has not chosen a currency/network yet.</summary>
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

    /// <summary>When the rate will be refreshed (RFC 3339; the rate holds for ~5 min).</summary>
    [JsonPropertyName("rate_expires_at")]
    public string RateExpiresAt { get; init; } = string.Empty;

    /// <summary>
    /// Rate locked by this invoice (payment currency per 1 unit of the price currency) —
    /// <see cref="PayerAmount"/> is calculated from it. Empty until the currency is chosen.
    /// Decimal string.
    /// </summary>
    [JsonPropertyName("exchange_rate")]
    public string ExchangeRate { get; init; } = string.Empty;

    /// <summary>Current number of confirmations of the incoming payment.</summary>
    [JsonPropertyName("confirmations")]
    public int Confirmations { get; init; }

    /// <summary>
    /// How many confirmations are needed for crediting (depends on the amount and the network).
    /// </summary>
    [JsonPropertyName("required_confirmations")]
    public int RequiredConfirmations { get; init; }

    /// <summary>Hash of the incoming transaction (once it is seen).</summary>
    [JsonPropertyName("txid")]
    public string Txid { get; init; } = string.Empty;

    /// <summary>
    /// All confirmed transfers that paid the invoice. Partial payment in several transfers is the
    /// regular <c>wrong_amount</c> scenario; the top-level <see cref="Txid"/> is only the last seen.
    /// </summary>
    [JsonPropertyName("tx_list")]
    public IReadOnlyList<PaymentTx> TxList { get; init; } = [];

    /// <summary>
    /// Moment of the actual payment — crediting of the last confirmed transfer (RFC 3339); null
    /// until the payment arrives. Distinguish it from <see cref="UpdatedAt"/>, which shifts on any
    /// invoice change.
    /// </summary>
    [JsonPropertyName("paid_at")]
    public string? PaidAt { get; init; }

    /// <summary>
    /// Address the first confirmed deposit came FROM, on account-based networks; empty on UTXO.
    /// This is NOT necessarily a refund address — check <see cref="PayerAddressIsRefundable"/>.
    /// </summary>
    [JsonPropertyName("payer_address")]
    public string PayerAddress { get; init; } = string.Empty;

    /// <summary>
    /// True — <see cref="PayerAddress"/> belongs to the payer and <c>address</c> may be omitted in
    /// <c>/v1/payment/refund</c>. False — the refund address is unknown: pass <c>address</c>
    /// explicitly, otherwise the request is rejected with <c>refund.no_address</c>.
    /// </summary>
    [JsonPropertyName("payer_address_is_refundable")]
    public bool PayerAddressIsRefundable { get; init; }

    /// <summary>Payer e-mail, if you passed one.</summary>
    [JsonPropertyName("payer_email")]
    public string PayerEmail { get; init; } = string.Empty;

    /// <summary>Your private data, returned in the response and in the webhook.</summary>
    [JsonPropertyName("additional_data")]
    public string AdditionalData { get; init; } = string.Empty;

    /// <summary>
    /// Our commission on this payment, in the payment currency. The invoice rate already includes
    /// the amortized fixed fee — it is not charged a second time. Decimal string.
    /// </summary>
    [JsonPropertyName("commission")]
    public string Commission { get; init; } = string.Empty;

    /// <summary>
    /// How much is (or will be) credited to you: <c>amount_paid − commission</c>. Network costs of
    /// collecting the deposit are borne by the gateway. Decimal string.
    /// </summary>
    [JsonPropertyName("merchant_amount")]
    public string MerchantAmount { get; init; } = string.Empty;

    /// <summary>
    /// Signed link to the PDF cheque — opens without an API key. Empty if document generation is
    /// not enabled.
    /// </summary>
    [JsonPropertyName("document_url")]
    public string DocumentUrl { get; init; } = string.Empty;

    /// <summary>
    /// True — a sandbox invoice (dev shop): the money is not real, keep it out of live reconciliation.
    /// </summary>
    [JsonPropertyName("is_test")]
    public bool IsTest { get; init; }

    /// <summary>Creation time (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>Time of the last change (RFC 3339).</summary>
    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;

    /// <summary>Refunds issued against this invoice. <c>/v1/payment/info</c> only.</summary>
    [JsonPropertyName("refunds")]
    public IReadOnlyList<PaymentRefund>? Refunds { get; init; }

    /// <summary>Aggregate refund state of the invoice. <c>/v1/payment/info</c> only.</summary>
    [JsonPropertyName("refund_status")]
    public string? RefundStatus { get; init; }
}
