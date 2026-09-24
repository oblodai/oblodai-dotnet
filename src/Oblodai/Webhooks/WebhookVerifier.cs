using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oblodai;

/// <summary>How to verify a delivery: the secret, the rotation overlap and the freshness window.</summary>
public sealed record WebhookVerifyOptions
{
    /// <summary>
    /// The endpoint secret from <c>Webhooks.RegisterAsync</c> / <c>RotateSecretAsync</c>. Redacted by
    /// <c>ToString()</c> and never serialized — which is also why it cannot be <c>required</c>: a member
    /// the serializer is told to skip cannot also be one it is told to demand. An empty or missing
    /// secret is refused by <see cref="WebhookVerifier.AssertUsableOptions"/> before any crypto runs.
    /// </summary>
    [JsonIgnore]
    public string Secret { get; init; } = string.Empty;

    /// <summary>
    /// During a rotation keep the outgoing secret here. Deliveries queued before the rotation stay signed
    /// with it for their whole retry life (~26 h), so keep it at least that long after rotating.
    /// Redacted and never serialized.
    /// </summary>
    [JsonIgnore]
    public string? PreviousSecret { get; init; }

    /// <summary>Reject deliveries whose timestamp is further away than this, seconds. Default 300; 0 disables.</summary>
    public int ToleranceSeconds { get; init; } = 300;

    /// <summary>Injectable clock (unix seconds) for tests.</summary>
    public Func<long>? Now { get; init; }

    /// <summary>Prints the tolerance and whether each secret is set — never the secrets themselves.</summary>
    /// <param name="builder">Buffer the record's <c>ToString()</c> writes into.</param>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.AppendRedacted(nameof(Secret), !string.IsNullOrEmpty(Secret))
            .Append(", ").AppendRedacted(nameof(PreviousSecret), !string.IsNullOrEmpty(PreviousSecret))
            .Append(", ToleranceSeconds = ").Append(ToleranceSeconds)
            .Append(", Now = ").Append(Now);
        return true;
    }
}

/// <summary>A verified delivery: the event plus the advisory headers worth keeping.</summary>
/// <param name="Event">The parsed event.</param>
/// <param name="Id"><c>X-Webhook-Id</c> — stable across retries of this one delivery only.</param>
/// <param name="EventId">
/// <c>X-Webhook-Event-Id</c> — the id of the STATE this delivery carries: the same for a resend of a
/// state you already handled, different as soon as the state differs. Deduplicate on this one.
/// </param>
/// <param name="EventType"><c>X-Webhook-Event</c> — <c>invoice.&lt;status&gt;</c>, <c>payout.&lt;status&gt;</c>, <c>wallet.paid</c>.</param>
/// <param name="EventTime"><c>X-Webhook-Event-Time</c> — unix seconds when the state change committed.</param>
/// <param name="SentAt"><c>X-Webhook-Timestamp</c> — unix seconds when this attempt was sent.</param>
/// <param name="IsTest">
/// A rehearsal delivery (<c>X-Webhook-Test: true</c> / body <c>test: true</c>): signed like a live one,
/// but no money moved — never act on it as if it did.
/// </param>
public sealed record WebhookDeliveryInfo(
    IWebhookEvent Event,
    string? Id,
    string? EventId,
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
/// X-Webhook-Id: stable per delivery (identical across retries of THAT delivery)
/// X-Webhook-Event-Id: stable per STATE — deduplicate on it
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

    /// <summary><c>X-Webhook-Event-Id</c>.</summary>
    public const string HeaderEventId = "X-Webhook-Event-Id";

    /// <summary><c>X-Webhook-Event-Time</c>.</summary>
    public const string HeaderEventTime = "X-Webhook-Event-Time";

    /// <summary><c>X-Webhook-Test</c>, sent as <c>true</c> on rehearsal deliveries.</summary>
    public const string HeaderTest = "X-Webhook-Test";

    /// <summary>Verify the signature and freshness, then parse. Never returns an unverified body.</summary>
    /// <param name="rawBody">The raw request bytes, exactly as received.</param>
    /// <param name="header">Case-insensitive header lookup.</param>
    /// <param name="options">Secret, rotation overlap and tolerance.</param>
    /// <exception cref="SignatureException">Missing header, stale timestamp or bad signature.</exception>
    public static IWebhookEvent Verify(ReadOnlySpan<byte> rawBody, Func<string, string?> header, WebhookVerifyOptions options)
        => VerifyDelivery(rawBody, header, options).Event;

    /// <summary>Verify a delivery whose headers come as a dictionary.</summary>
    /// <param name="rawBody">The raw request bytes.</param>
    /// <param name="headers">Delivery headers.</param>
    /// <param name="options">Secret, rotation overlap and tolerance.</param>
    public static IWebhookEvent Verify(
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
        AssertUsableOptions(options);

        var timestampHeader = header(HeaderTimestamp);
        var signature = NormalizeSignature(header(HeaderSignature));
        if (string.IsNullOrEmpty(timestampHeader) || string.IsNullOrEmpty(signature))
        {
            throw new SignatureException(
                SdkErrorCodes.WebhookMissingHeader, $"missing {HeaderTimestamp} or {HeaderSignature}");
        }

        if (!long.TryParse(timestampHeader.Trim(), out var ts))
        {
            throw new SignatureException(SdkErrorCodes.WebhookBadSignature, "timestamp header is not an integer");
        }

        // The MAC is checked BEFORE the freshness window. The other order makes the tolerance a pre-auth
        // oracle: anyone could learn the receiver's clock offset by probing timestamps without a signature.
        var previousSignature = NormalizeSignature(header(HeaderSignaturePrev));
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
            matched |= FixedTimeEquals(provided, expected);
        }

        if (!matched)
        {
            throw new SignatureException(SdkErrorCodes.WebhookBadSignature, "signature does not match the body");
        }

        if (options.ToleranceSeconds > 0)
        {
            var now = (options.Now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds()))();
            if (Math.Abs(now - ts) > options.ToleranceSeconds)
            {
                throw new SignatureException(
                    SdkErrorCodes.WebhookStaleTimestamp,
                    $"delivery timestamp {ts} is outside the \u00b1{options.ToleranceSeconds}s window");
            }
        }

        var eventTimeHeader = header(HeaderEventTime);
        long? eventTime = long.TryParse(eventTimeHeader, out var parsedEventTime) ? parsedEventTime : null;

        var parsed = Parse(rawBody);
        var isTest = string.Equals(header(HeaderTest)?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                     || IsTestEvent(parsed);

        return new WebhookDeliveryInfo(
            parsed, header(HeaderId), header(HeaderEventId), header(HeaderEvent), eventTime, ts, isTest);
    }

    /// <summary>
    /// Verification cannot start without a real secret: an empty one would make the HMAC a function of
    /// the body alone, so any sender could produce a matching signature. A negative tolerance would read
    /// as "everything is stale" — both are configuration mistakes, raised before any crypto runs.
    /// </summary>
    /// <param name="options">Verification options.</param>
    /// <exception cref="ConfigException">The secret is empty or the tolerance is negative.</exception>
    public static void AssertUsableOptions(WebhookVerifyOptions options)
    {
        if (string.IsNullOrEmpty(options.Secret))
        {
            throw new ConfigException(
                SdkErrorCodes.BadConfig,
                "WebhookVerifyOptions.Secret is empty; verification with an empty key would accept any sender",
                nameof(WebhookVerifyOptions.Secret));
        }

        if (options.PreviousSecret is { Length: 0 })
        {
            throw new ConfigException(
                SdkErrorCodes.BadConfig,
                "WebhookVerifyOptions.PreviousSecret is an empty string; leave it null when no rotation is in flight",
                nameof(WebhookVerifyOptions.PreviousSecret));
        }

        if (options.ToleranceSeconds < 0)
        {
            throw new ConfigException(
                SdkErrorCodes.BadConfig,
                $"WebhookVerifyOptions.ToleranceSeconds is negative ({options.ToleranceSeconds}); "
                + "use 0 to disable the freshness check",
                nameof(WebhookVerifyOptions.ToleranceSeconds));
        }
    }

    /// <summary>
    /// A signature header as it may realistically arrive: with surrounding whitespace from a proxy, in
    /// upper case, or (wrongly) with an <c>0x</c> prefix. The first two are accepted, the third is not —
    /// it is not the encoding the gateway sends, and silently stripping it would hide a real mismatch.
    /// </summary>
    /// <param name="value">Raw header value.</param>
    private static string? NormalizeSignature(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// True for rehearsal deliveries (<c>Webhooks.TestAsync</c>, sandbox) — never act on them as if
    /// money moved.
    /// </summary>
    /// <param name="webhookEvent">The parsed event.</param>
    public static bool IsTestEvent(IWebhookEvent webhookEvent) => webhookEvent.Test == true;

    /// <summary>
    /// Parse a (previously verified) delivery body into a typed event, discriminated by <c>type</c>.
    /// A body the SDK cannot read raises <see cref="ContractException"/> with
    /// <see cref="SdkErrorCodes.WebhookBadPayload"/> — NOT a signature failure. The signature already
    /// proved the delivery is authentic, and a receiver that answers 401 to signature failures must not
    /// answer 401 here: the gateway would retire the endpoint over a bug in the receiver's own decoding.
    /// A <c>type</c> this snapshot does not know is not an error at all — it comes back as
    /// <see cref="UnknownWebhookEvent"/> carrying the raw type string.
    /// </summary>
    /// <param name="rawBody">The delivery body.</param>
    /// <exception cref="ContractException">The body is not JSON, or lacks the fields every event carries.</exception>
    public static IWebhookEvent Parse(ReadOnlySpan<byte> rawBody)
    {
        JsonElement body;
        try
        {
            using var document = JsonDocument.Parse(rawBody.ToArray());
            body = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new WebhookPayloadException("delivery body is not JSON");
        }

        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            throw new WebhookPayloadException("delivery body lacks the string type field every event carries");
        }

        try
        {
            return type.GetString() switch
            {
                "payment" => OblodaiJson.Deserialize<PaymentWebhook>(body),
                "payout" => OblodaiJson.Deserialize<PayoutWebhook>(body),
                "wallet" => OblodaiJson.Deserialize<WalletWebhook>(body),
                "conversion" => OblodaiJson.Deserialize<ConversionWebhook>(body),
                _ => OblodaiJson.Deserialize<UnknownWebhookEvent>(body),
            };
        }
        catch (ContractException error)
        {
            throw new WebhookPayloadException(error.Description);
        }
    }

    /// <summary>
    /// True when the event is one of the families this snapshot models
    /// (<see cref="PaymentWebhook"/>, <see cref="PayoutWebhook"/>, <see cref="WalletWebhook"/>,
    /// <see cref="ConversionWebhook"/>) rather than an
    /// <see cref="UnknownWebhookEvent"/> the gateway added later. Guard a <c>switch</c> with it before
    /// treating an event as money.
    /// </summary>
    /// <param name="webhookEvent">The parsed event.</param>
    public static bool IsKnownEvent(IWebhookEvent webhookEvent) => webhookEvent is not UnknownWebhookEvent;

    /// <summary>Parse a (previously verified) delivery body given as text.</summary>
    /// <param name="rawBody">The delivery body.</param>
    public static IWebhookEvent Parse(string rawBody) => Parse(Encoding.UTF8.GetBytes(rawBody));

    /// <summary>
    /// Deliveries can arrive out of order (a retried <c>paid</c> after a refund). Keep the last
    /// <c>sequence</c> you processed per object and skip anything not newer.
    /// </summary>
    /// <param name="webhookEvent">The event just received.</param>
    /// <param name="lastProcessedSequence">The highest sequence already applied, if any.</param>
    public static bool IsStale(IWebhookEvent webhookEvent, long? lastProcessedSequence)
        => lastProcessedSequence is { } last && webhookEvent.EventSequence is { } sequence && sequence <= last;

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
