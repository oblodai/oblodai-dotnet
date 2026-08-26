using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Oblodai.Models;

namespace Oblodai;

/// <summary>How to verify a delivery: the secret, the rotation overlap and the freshness window.</summary>
public sealed record WebhookVerifyOptions
{
    /// <summary>The endpoint secret from <c>Webhooks.RegisterAsync</c> / <c>RotateSecretAsync</c>.</summary>
    public required string Secret { get; init; }

    /// <summary>
    /// During a rotation keep the outgoing secret here. Deliveries queued before the rotation stay signed
    /// with it for their whole retry life (~26 h), so keep it at least that long after rotating.
    /// </summary>
    public string? PreviousSecret { get; init; }

    /// <summary>Reject deliveries whose timestamp is further away than this, seconds. Default 300; 0 disables.</summary>
    public int ToleranceSeconds { get; init; } = 300;

    /// <summary>Injectable clock (unix seconds) for tests.</summary>
    public Func<long>? Now { get; init; }
}

/// <summary>A verified delivery: the event plus the advisory headers worth keeping.</summary>
/// <param name="Event">The parsed event.</param>
/// <param name="Id"><c>X-Webhook-Id</c> — stable across retries; use it as your idempotency key.</param>
/// <param name="EventType"><c>X-Webhook-Event</c> — <c>invoice.&lt;status&gt;</c>, <c>payout.&lt;status&gt;</c>, <c>wallet.paid</c>.</param>
/// <param name="EventTime"><c>X-Webhook-Event-Time</c> — unix seconds when the state change committed.</param>
/// <param name="SentAt"><c>X-Webhook-Timestamp</c> — unix seconds when this attempt was sent.</param>
/// <param name="IsTest">
/// A rehearsal delivery (<c>X-Webhook-Test: true</c> / body <c>test: true</c>): signed like a live one,
/// but no money moved — never act on it as if it did.
/// </param>
public sealed record WebhookDeliveryInfo(
    WebhookEvent Event,
    string? Id,
    string? EventType,
    long? EventTime,
    long SentAt,
    bool IsTest);

/// <summary>
/// Webhook verification — usable on its own, with no client and no API key. Deliveries are signed as:
/// <code>
/// X-Webhook-Timestamp: &lt;unix seconds&gt;
/// X-Webhook-Signature: hex(HMAC-SHA256(secret, "&lt;ts&gt;." + rawBody))
/// X-Webhook-Signature-Prev: same, with the previous secret — only during a rotation overlap
/// X-Webhook-Event: invoice.&lt;status&gt; | payout.&lt;status&gt; | wallet.paid
/// X-Webhook-Id: stable per delivery (identical across retries) — use it as your idempotency key
/// X-Webhook-Event-Time: unix seconds when the state change committed (order events by it)
/// X-Webhook-Test: true — a rehearsal delivery, mirrored by "test": true in the signed body
/// </code>
/// Always verify over the raw request bytes; a re-serialized parse will not match.
/// </summary>
public static class WebhookVerifier
{
    /// <summary><c>X-Webhook-Timestamp</c>.</summary>
    public const string HeaderTimestamp = "X-Webhook-Timestamp";

    /// <summary><c>X-Webhook-Signature</c>.</summary>
    public const string HeaderSignature = "X-Webhook-Signature";

    /// <summary><c>X-Webhook-Signature-Prev</c>, sent during a rotation overlap.</summary>
    public const string HeaderSignaturePrev = "X-Webhook-Signature-Prev";

    /// <summary><c>X-Webhook-Event</c>.</summary>
    public const string HeaderEvent = "X-Webhook-Event";

    /// <summary><c>X-Webhook-Id</c>.</summary>
    public const string HeaderId = "X-Webhook-Id";

    /// <summary><c>X-Webhook-Event-Time</c>.</summary>
    public const string HeaderEventTime = "X-Webhook-Event-Time";

    /// <summary><c>X-Webhook-Test</c>, sent as <c>true</c> on rehearsal deliveries.</summary>
    public const string HeaderTest = "X-Webhook-Test";

    /// <summary>Verify the signature and freshness, then parse. Never returns an unverified body.</summary>
    /// <param name="rawBody">The raw request bytes, exactly as received.</param>
    /// <param name="header">Case-insensitive header lookup.</param>
    /// <param name="options">Secret, rotation overlap and tolerance.</param>
    /// <exception cref="SignatureException">Missing header, stale timestamp or bad signature.</exception>
    public static WebhookEvent Verify(ReadOnlySpan<byte> rawBody, Func<string, string?> header, WebhookVerifyOptions options)
        => VerifyDelivery(rawBody, header, options).Event;

    /// <summary>Verify a delivery whose headers come as a dictionary.</summary>
    /// <param name="rawBody">The raw request bytes.</param>
    /// <param name="headers">Delivery headers.</param>
    /// <param name="options">Secret, rotation overlap and tolerance.</param>
    public static WebhookEvent Verify(
        ReadOnlySpan<byte> rawBody,
        IReadOnlyDictionary<string, string> headers,
        WebhookVerifyOptions options)
        => VerifyDelivery(rawBody, Lookup(headers), options).Event;

    /// <summary>Like <see cref="Verify(ReadOnlySpan{byte},Func{string,string},WebhookVerifyOptions)"/>, and also returns the delivery headers.</summary>
    /// <param name="rawBody">The raw request bytes.</param>
    /// <param name="headers">Delivery headers.</param>
    /// <param name="options">Secret, rotation overlap and tolerance.</param>
    public static WebhookDeliveryInfo VerifyDelivery(
        ReadOnlySpan<byte> rawBody,
        IReadOnlyDictionary<string, string> headers,
        WebhookVerifyOptions options)
        => VerifyDelivery(rawBody, Lookup(headers), options);

    /// <summary>Verify and return the event together with the advisory headers.</summary>
    /// <param name="rawBody">The raw request bytes.</param>
    /// <param name="header">Case-insensitive header lookup.</param>
    /// <param name="options">Secret, rotation overlap and tolerance.</param>
    /// <exception cref="SignatureException">Missing header, stale timestamp or bad signature.</exception>
    public static WebhookDeliveryInfo VerifyDelivery(
        ReadOnlySpan<byte> rawBody,
        Func<string, string?> header,
        WebhookVerifyOptions options)
    {
        var timestampHeader = header(HeaderTimestamp);
        var signature = header(HeaderSignature);
        if (string.IsNullOrEmpty(timestampHeader) || string.IsNullOrEmpty(signature))
        {
            throw new SignatureException(
                SdkErrorCodes.WebhookMissingHeader, $"missing {HeaderTimestamp} or {HeaderSignature}");
        }

        if (!long.TryParse(timestampHeader, out var ts))
        {
            throw new SignatureException(SdkErrorCodes.WebhookBadSignature, "timestamp header is not an integer");
        }

        if (options.ToleranceSeconds > 0)
        {
            var now = (options.Now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds()))();
            if (Math.Abs(now - ts) > options.ToleranceSeconds)
            {
                throw new SignatureException(
                    SdkErrorCodes.WebhookStaleTimestamp,
                    $"delivery timestamp {ts} is outside the ±{options.ToleranceSeconds}s window");
            }
        }

        var previousSignature = header(HeaderSignaturePrev);
        var candidates = new List<(string Provided, string Secret)> { (signature!, options.Secret) };

        // A merchant who has not swapped the stored secret yet verifies the Prev header with it; one who
        // already swapped but kept the old copy verifies the main header with the new secret. Both hold.
        if (!string.IsNullOrEmpty(previousSignature))
        {
            candidates.Add((previousSignature!, options.Secret));
        }

        if (!string.IsNullOrEmpty(options.PreviousSecret))
        {
            candidates.Add((signature!, options.PreviousSecret!));
            if (!string.IsNullOrEmpty(previousSignature))
            {
                candidates.Add((previousSignature!, options.PreviousSecret!));
            }
        }

        var matched = false;
        foreach (var (provided, secret) in candidates)
        {
            var expected = RequestSigner.SignWebhook(secret, ts, rawBody);
            matched |= FixedTimeEquals(provided.ToLowerInvariant(), expected);
        }

        if (!matched)
        {
            throw new SignatureException(SdkErrorCodes.WebhookBadSignature, "signature does not match the body");
        }

        var eventTimeHeader = header(HeaderEventTime);
        long? eventTime = long.TryParse(eventTimeHeader, out var parsedEventTime) ? parsedEventTime : null;

        var parsed = Parse(rawBody);
        var isTest = string.Equals(header(HeaderTest), "true", StringComparison.Ordinal) || IsTestEvent(parsed);

        return new WebhookDeliveryInfo(parsed, header(HeaderId), header(HeaderEvent), eventTime, ts, isTest);
    }

    /// <summary>
    /// True for rehearsal deliveries (<c>Webhooks.TestAsync</c>, sandbox) — never act on them as if
    /// money moved.
    /// </summary>
    /// <param name="webhookEvent">The parsed event.</param>
    public static bool IsTestEvent(WebhookEvent webhookEvent) => webhookEvent.Test == true;

    /// <summary>Parse a (previously verified) delivery body into a typed event, discriminated by <c>type</c>.</summary>
    /// <param name="rawBody">The delivery body.</param>
    /// <exception cref="SignatureException">The body is not a delivery this SDK understands.</exception>
    public static WebhookEvent Parse(ReadOnlySpan<byte> rawBody)
    {
        JsonElement body;
        try
        {
            using var document = JsonDocument.Parse(rawBody.ToArray());
            body = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new SignatureException(SdkErrorCodes.WebhookBadSignature, "body is not JSON");
        }

        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
            || !body.TryGetProperty("uuid", out var uuid) || uuid.ValueKind != JsonValueKind.String)
        {
            throw new SignatureException(
                SdkErrorCodes.WebhookBadSignature, "body lacks the type/uuid fields every event carries");
        }

        return type.GetString() switch
        {
            "payment" => OblodaiJson.Deserialize<PaymentEvent>(body),
            "payout" => OblodaiJson.Deserialize<PayoutEvent>(body),
            "wallet" => OblodaiJson.Deserialize<WalletEvent>(body),
            var other => throw new SignatureException(
                SdkErrorCodes.WebhookBadSignature, $"unknown event type \"{other}\""),
        };
    }

    /// <summary>Parse a (previously verified) delivery body given as text.</summary>
    /// <param name="rawBody">The delivery body.</param>
    public static WebhookEvent Parse(string rawBody) => Parse(Encoding.UTF8.GetBytes(rawBody));

    /// <summary>
    /// Deliveries can arrive out of order (a retried <c>paid</c> after a refund). Keep the last
    /// <c>sequence</c> you processed per object and skip anything not newer.
    /// </summary>
    /// <param name="webhookEvent">The event just received.</param>
    /// <param name="lastProcessedSequence">The highest sequence already applied, if any.</param>
    public static bool IsStale(WebhookEvent webhookEvent, long? lastProcessedSequence)
        => lastProcessedSequence is { } last && webhookEvent.Sequence <= last;

    private static Func<string, string?> Lookup(IReadOnlyDictionary<string, string> headers)
        => name =>
        {
            if (headers.TryGetValue(name, out var direct))
            {
                return direct;
            }

            foreach (var (key, value) in headers)
            {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }

            return null;
        };

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
