using System.Text.Json;
using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

/// <summary><c>POST /v1/webhooks</c> — the registered (or updated) callback endpoint.</summary>
public sealed record WebhookEndpoint
{
    /// <summary>Endpoint id.</summary>
    [JsonPropertyName("endpoint_id")]
    public string EndpointId { get; init; } = string.Empty;

    /// <summary>Registered callback URL.</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// The signing secret — shown ONCE, at first registration and at rotation; save it so you can
    /// verify delivery signatures. Absent when only the URL was changed.
    /// </summary>
    [JsonPropertyName("secret")]
    public string? Secret { get; init; }
}

/// <summary><c>POST /v1/webhooks/rotate-secret</c> — the new secret and the overlap window.</summary>
public sealed record WebhookSecretRotated
{
    /// <summary>Endpoint id.</summary>
    [JsonPropertyName("endpoint_id")]
    public string EndpointId { get; init; } = string.Empty;

    /// <summary>Registered callback URL.</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    /// <summary>New signing secret — shown only here.</summary>
    [JsonPropertyName("secret")]
    public string Secret { get; init; } = string.Empty;

    /// <summary>
    /// Until this moment deliveries also carry <c>X-Webhook-Signature-Prev</c> signed with the old
    /// secret; keep the old one until then (RFC 3339).
    /// </summary>
    [JsonPropertyName("previous_secret_valid_until")]
    public string PreviousSecretValidUntil { get; init; } = string.Empty;
}

/// <summary>
/// One delivery attempt record. Covers both <c>/v1/webhooks/deliveries</c> (which carries
/// <see cref="Sequence"/>) and <c>GET /v1/sandbox/webhooks</c> (which carries <see cref="Payload"/>
/// instead).
/// </summary>
public sealed record WebhookDelivery
{
    /// <summary>Delivery id — stable across retries, the same value as <c>X-Webhook-Id</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Where it was sent.</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// The event: <c>invoice.&lt;status&gt;</c>, <c>payout.&lt;status&gt;</c> or <c>wallet.paid</c>.
    /// </summary>
    [JsonPropertyName("event_type")]
    public EventType EventType { get; init; }

    /// <summary>Delivery state: <c>pending</c> | <c>delivered</c> | <c>dead</c>.</summary>
    [JsonPropertyName("status")]
    public DeliveryStatus Status { get; init; }

    /// <summary>How many times it has been attempted.</summary>
    [JsonPropertyName("attempts")]
    public int Attempts { get; init; }

    /// <summary>What went wrong on the last attempt; empty when nothing did.</summary>
    [JsonPropertyName("last_error")]
    public string LastError { get; init; } = string.Empty;

    /// <summary>
    /// The event's global sequence. <c>/v1/webhooks/deliveries</c> only — the sandbox listing does
    /// not carry it.
    /// </summary>
    [JsonPropertyName("sequence")]
    public long? Sequence { get; init; }

    /// <summary>When the delivery was queued (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>Time of the last attempt (RFC 3339).</summary>
    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;

    /// <summary>
    /// The body that was (or will be) delivered, verbatim. <c>GET /v1/sandbox/webhooks</c> only.
    /// </summary>
    [JsonPropertyName("payload")]
    public IReadOnlyDictionary<string, JsonElement>? Payload { get; init; }
}

/// <summary><c>/v1/test-webhook/*</c> and <c>/v1/payment/testing-webhook</c>.</summary>
public sealed record WebhookTestResult
{
    /// <summary>True — the receiver answered 2xx.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>True — the test delivery carried a signature.</summary>
    [JsonPropertyName("signed")]
    public bool Signed { get; init; }

    /// <summary>What the receiver answered; absent when it could not be reached (see <see cref="Error"/>).</summary>
    [JsonPropertyName("status_code")]
    public int? StatusCode { get; init; }

    /// <summary>Why the receiver could not be reached.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>Where the test was sent. <c>/v1/payment/testing-webhook</c> only.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>How long the receiver took, milliseconds. <c>/v1/payment/testing-webhook</c> only.</summary>
    [JsonPropertyName("duration_ms")]
    public int? DurationMs { get; init; }
}

/// <summary>
/// What every delivered event carries. The concrete event is
/// <see cref="PaymentEvent"/>, <see cref="PayoutEvent"/> or <see cref="WalletEvent"/>, discriminated
/// by <see cref="Type"/>; <c>WebhookVerifier.Parse</c> returns the right one.
/// </summary>
public abstract record WebhookEvent
{
    /// <summary>Event family: <c>payment</c>, <c>payout</c> or <c>wallet</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>Id of the object that changed (invoice, payout or static wallet).</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>The object's order number; null on refund payouts.</summary>
    [JsonPropertyName("order_id")]
    public string? OrderId { get; init; }

    /// <summary>True — the status is final and no further event will follow.</summary>
    [JsonPropertyName("is_final")]
    public bool IsFinal { get; init; }

    /// <summary>
    /// When the state change was committed (RFC 3339) — order events by this, or by
    /// <see cref="Sequence"/>.
    /// </summary>
    [JsonPropertyName("event_at")]
    public string EventAt { get; init; } = string.Empty;

    /// <summary>
    /// Global, increasing counter (gaps are normal); a lower sequence arriving later is stale.
    /// </summary>
    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    /// <summary>On-chain transaction hash, when there is one.</summary>
    [JsonPropertyName("txid")]
    public string Txid { get; init; } = string.Empty;

    /// <summary>
    /// Present and true ONLY on rehearsal deliveries (<c>Webhooks.TestAsync</c>, sandbox). The body is
    /// signed like a live one, so a handler must check this flag (or <c>X-Webhook-Test</c>) and never
    /// act on a test event as if money moved.
    /// </summary>
    [JsonPropertyName("test")]
    public bool? Test { get; init; }
}

/// <summary><c>invoice.&lt;status&gt;</c> — an invoice changed state.</summary>
public sealed record PaymentEvent : WebhookEvent
{
    /// <summary>The invoice's new status.</summary>
    [JsonPropertyName("status")]
    public PaymentStatus Status { get; init; }

    /// <summary>Amount due in the price currency. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Price currency.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Settlement network.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>How much was due in the payer asset. Decimal string.</summary>
    [JsonPropertyName("payer_amount")]
    public string PayerAmount { get; init; } = string.Empty;

    /// <summary>The payer asset.</summary>
    [JsonPropertyName("payer_currency")]
    public string PayerCurrency { get; init; } = string.Empty;

    /// <summary>
    /// What actually landed on the address, in <see cref="PayerCurrency"/>. Decimal string.
    /// </summary>
    [JsonPropertyName("payment_amount")]
    public string PaymentAmount { get; init; } = string.Empty;

    /// <summary>Address the deposit came from; empty on UTXO networks.</summary>
    [JsonPropertyName("payer_address")]
    public string PayerAddress { get; init; } = string.Empty;

    /// <summary>
    /// True — <see cref="PayerAddress"/> may be refunded to without passing an address explicitly.
    /// </summary>
    [JsonPropertyName("payer_address_is_refundable")]
    public bool PayerAddressIsRefundable { get; init; }

    /// <summary>Your private data, as passed at invoice creation.</summary>
    [JsonPropertyName("additional_data")]
    public string AdditionalData { get; init; } = string.Empty;
}

/// <summary>
/// <c>payout.&lt;status&gt;</c> — a payout (or refund) changed state; the body is the payout itself.
/// </summary>
public sealed record PayoutEvent : WebhookEvent
{
    /// <summary>The payout's new status.</summary>
    [JsonPropertyName("status")]
    public PayoutStatus Status { get; init; }

    /// <summary>Payout amount in <see cref="Currency"/>. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Payout currency code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Blockchain network.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Recipient address.</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    /// <summary>Destination tag / memo passed at creation. Empty — no memo.</summary>
    [JsonPropertyName("memo")]
    public string Memo { get; init; } = string.Empty;

    /// <summary>Total debited from the balance. Decimal string.</summary>
    [JsonPropertyName("payer_amount")]
    public string PayerAmount { get; init; } = string.Empty;

    /// <summary>Network fee withheld. Decimal string.</summary>
    [JsonPropertyName("commission")]
    public string Commission { get; init; } = string.Empty;

    /// <summary>Who paid the network fee.</summary>
    [JsonPropertyName("fee_bearer")]
    public FeeBearerResult FeeBearer { get; init; }

    /// <summary>
    /// How the payout was initiated: <c>api</c> (this SDK / API key) or <c>manual</c> (cabinet).
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    /// <summary>True — the payout is awaiting approval.</summary>
    [JsonPropertyName("approval_required")]
    public bool ApprovalRequired { get; init; }

    /// <summary>True — this is a payment refund, not a regular payout.</summary>
    [JsonPropertyName("is_refund")]
    public bool IsRefund { get; init; }

    /// <summary>Id of the payment being refunded; null if this is not a refund.</summary>
    [JsonPropertyName("refund_for")]
    public string? RefundFor { get; init; }

    /// <summary>Your <c>order_id</c> of the payment the refund was made for; null otherwise.</summary>
    [JsonPropertyName("payment_order_id")]
    public string? PaymentOrderId { get; init; }

    /// <summary>Signed link to the PDF cheque for this payout.</summary>
    [JsonPropertyName("document_url")]
    public string DocumentUrl { get; init; } = string.Empty;

    /// <summary>Creation time (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>Time of the last change (RFC 3339).</summary>
    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;
}

/// <summary><c>wallet.paid</c> — a deposit landed on a static wallet.</summary>
public sealed record WalletEvent : WebhookEvent
{
    /// <summary>Always <c>paid</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>The wallet's address.</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    /// <summary>The wallet's asset.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Blockchain network.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Asset the deposit arrived in.</summary>
    [JsonPropertyName("payer_currency")]
    public string PayerCurrency { get; init; } = string.Empty;

    /// <summary>How much landed, in <see cref="PayerCurrency"/>. Decimal string.</summary>
    [JsonPropertyName("payment_amount")]
    public string PaymentAmount { get; init; } = string.Empty;
}
