using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oblodai;

/// <summary>
/// What every delivered event carries. <see cref="WebhookVerifier.Parse(ReadOnlySpan{byte})"/> returns
/// the generated model of the event's family — <see cref="PaymentWebhook"/> (<c>type: payment</c>),
/// <see cref="PayoutWebhook"/> (<c>payout</c>), <see cref="WalletWebhook"/> (<c>wallet</c>),
/// <see cref="ConversionWebhook"/> (<c>conversion</c>) — or <see cref="UnknownWebhookEvent"/> for a
/// family this SDK version does not know. Switch on the type:
/// <code>
/// switch (evt)
/// {
///     case PaymentWebhook payment when Statuses.IsPaymentPaid(payment.Status): …
///     case PayoutWebhook payout: …
/// }
/// </code>
/// </summary>
public interface IWebhookEvent
{
    /// <summary>Event family: <c>payment</c>, <c>payout</c>, <c>wallet</c>, <c>conversion</c>, or one added later.</summary>
    string Type { get; }

    /// <summary>When the state change committed (RFC 3339); order events by it or by <see cref="EventSequence"/>.</summary>
    string EventAt { get; }

    /// <summary>
    /// Global, increasing counter (gaps are normal); a lower sequence arriving later is stale. Null when
    /// the delivery carried none.
    /// </summary>
    long? EventSequence { get; }

    /// <summary>True on rehearsal deliveries (<c>webhooks.send_test_*</c>, sandbox) — no money moved.</summary>
    bool? Test { get; }
}

/// <summary>An invoice changed state (<c>invoice.&lt;status&gt;</c>).</summary>
public sealed partial record PaymentWebhook : IWebhookEvent
{
    /// <inheritdoc />
    long? IWebhookEvent.EventSequence => Sequence;
}

/// <summary>A payout (or refund) changed state (<c>payout.&lt;status&gt;</c>).</summary>
public sealed partial record PayoutWebhook : IWebhookEvent
{
    /// <inheritdoc />
    long? IWebhookEvent.EventSequence => Sequence;
}

/// <summary>A deposit landed on a static wallet (<c>wallet.paid</c>).</summary>
public sealed partial record WalletWebhook : IWebhookEvent
{
    /// <inheritdoc />
    long? IWebhookEvent.EventSequence => Sequence;
}

/// <summary>A conversion finished (<c>conversion.completed</c> / <c>conversion.refunded</c>).</summary>
public sealed partial record ConversionWebhook : IWebhookEvent
{
    /// <inheritdoc />
    long? IWebhookEvent.EventSequence => Sequence;
}

/// <summary>
/// An event whose <c>type</c> this SDK version does not know. The gateway may add event families at any
/// time, and a receiver that throws on one it has not been taught about would reject an authentic,
/// signed delivery. The raw type stays in <see cref="Type"/> and the whole body in
/// <see cref="Model.Extra"/>.
/// </summary>
public sealed record UnknownWebhookEvent : Model, IWebhookEvent
{
    /// <inheritdoc />
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <inheritdoc />
    [JsonPropertyName("event_at")]
    public string EventAt { get; init; } = string.Empty;

    /// <summary>The delivery's <c>sequence</c>, when it carried a number.</summary>
    [JsonPropertyName("sequence")]
    public JsonElement? Sequence { get; init; }

    /// <inheritdoc />
    [JsonPropertyName("test")]
    public bool? Test { get; init; }

    /// <inheritdoc />
    long? IWebhookEvent.EventSequence
        => Sequence is { ValueKind: JsonValueKind.Number } n && n.TryGetInt64(out var value) ? value : null;
}
