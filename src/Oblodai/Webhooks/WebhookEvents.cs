using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oblodai;

/// <summary>
/// What every delivered event carries. <see cref="WebhookVerifier.Parse(ReadOnlySpan{byte})"/> returns
/// the generated model of the event's kind — the contract's webhook bodies, keyed by their
/// <c>type</c> in <see cref="Contract.ApiFacts.WebhookModels"/> (<see cref="PaymentWebhook"/> for
/// <c>payment</c>, …; each implements this interface in <c>Generated/Webhooks.g.cs</c>) — or <see cref="UnknownWebhookEvent"/> for a kind this SDK version does not know. Switch on the type:
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
    /// <summary>Event kind: one of <see cref="Contract.ApiFacts.WebhookKinds"/>, or one added later.</summary>
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

    /// <summary>
    /// The id of the object the event is about: the id field the contract declares for the event's kind
    /// (generated per model). Null for a kind without one and for an <see cref="UnknownWebhookEvent"/> —
    /// its id field is not guessed; read <see cref="Model.Extra"/> there.
    /// </summary>
    string? ObjectId { get; }
}

/// <summary>
/// An event whose <c>type</c> this SDK version does not know. The gateway may add event kinds at any
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

    /// <inheritdoc />
    string? IWebhookEvent.ObjectId => null;
}
