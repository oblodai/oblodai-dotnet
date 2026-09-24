using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oblodai;

/// <summary>
/// The gateway's error envelope: <c>{ "error": { code, message, field?, retryable, retry_after?, request_id? } }</c>.
/// <para>
/// Never deserialized as a unit: <see cref="EnvelopeDecoder"/> reads it field by field, because a body
/// whose <c>retryable</c> is a string or whose <c>retry_after</c> overflows must still classify as the
/// HTTP failure it is, not crash the call with a JSON exception.
/// </para>
/// </summary>
public sealed record ErrorDetail
{
    /// <summary>Stable machine code, <c>family.reason</c>.</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>Human-readable explanation.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>The request field the error refers to, for validation failures.</summary>
    [JsonPropertyName("field")]
    public string? Field { get; init; }

    /// <summary>The gateway's own verdict on whether repeating the identical request can succeed.</summary>
    [JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }

    /// <summary>Seconds to wait before retrying.</summary>
    [JsonPropertyName("retry_after")]
    public int? RetryAfter { get; init; }

    /// <summary>Server-side request id — quote it when contacting support.</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }
}

/// <summary>Codes the SDK itself raises before or instead of a request.</summary>
public static class SdkErrorCodes
{
    /// <summary>A signed route was called without credentials.</summary>
    public const string MissingCredentials = "sdk.missing_credentials";

    /// <summary>The options are unusable (bad base URL, half a key pair).</summary>
    public const string BadConfig = "sdk.bad_config";

    /// <summary>The caller's idempotency key is not a valid header value.</summary>
    public const string BadIdempotencyKey = "sdk.bad_idempotency_key";

    /// <summary>An idempotency key was passed to a route the gateway does not deduplicate.</summary>
    public const string IdempotencyUnsupported = "sdk.idempotency_unsupported";

    /// <summary>The response could not be read as the documented envelope.</summary>
    public const string BadEnvelope = "sdk.bad_envelope";

    /// <summary>A path parameter would rewrite the URL.</summary>
    public const string BadPathParam = "sdk.bad_path_param";

    /// <summary>The request timed out before a response arrived.</summary>
    public const string TransportTimeout = "transport.timeout";

    /// <summary>DNS, TCP, TLS or socket failure.</summary>
    public const string TransportNetwork = "transport.network";


    /// <summary>A retry would have exceeded the call deadline.</summary>
    public const string TransportDeadline = "transport.deadline";

    /// <summary>A webhook signature did not match the body.</summary>
    public const string WebhookBadSignature = "webhook.bad_signature";

    /// <summary>A webhook timestamp is outside the tolerance window.</summary>
    public const string WebhookStaleTimestamp = "webhook.stale_timestamp";

    /// <summary>A webhook delivery lacks the timestamp or signature header.</summary>
    public const string WebhookMissingHeader = "webhook.missing_header";

    /// <summary>
    /// The delivery's signature matched but its body is not an event this SDK can read. Deliberately
    /// NOT in the signature family: a receiver that answers 401 to signature failures must not answer
    /// 401 to an authentic event it merely failed to parse.
    /// </summary>
    public const string WebhookBadPayload = "webhook.bad_payload";

    /// <summary>A string that should have been a decimal amount was not one.</summary>
    public const string BadAmount = "sdk.bad_amount";

    /// <summary>An amount was given as binary floating point (<c>double</c>/<c>float</c>); it is refused before sending.</summary>
    public const string FloatAmount = "sdk.float_amount";

    /// <summary>A caller-supplied header cannot be sent verbatim (CR/LF, control or non-ASCII).</summary>
    public const string BadHeader = "sdk.bad_header";

    /// <summary>A long-running operation was still running when its wait ran out.</summary>
    public const string WaitTimeout = "sdk.wait_timeout";

    /// <summary>A document job's file was asked for before the job was done.</summary>
    public const string JobNotDone = "sdk.job_not_done";

    /// <summary>The response body exceeded the size the SDK is willing to buffer.</summary>
    public const string ResponseTooLarge = "sdk.response_too_large";
}

/// <summary>
/// Base of the error family. One shape carries the gateway's envelope; subclasses exist for
/// <c>catch</c> ergonomics, but the discriminator is always <see cref="Code"/>.
/// <para>
/// <see cref="Retryable"/> is authoritative when the gateway wrote the envelope: it is the gateway's
/// own classification of the failure. A response without an envelope (a proxy 502, an HTML 503) is
/// <see cref="Synthetic"/> — the gateway never saw or never answered the request — and is retried
/// only when repeating is safe.
/// </para>
/// </summary>
public class OblodaiException : Exception
{
    /// <summary>Build an error from its parts.</summary>
    /// <param name="code">Machine code (<c>family.reason</c>).</param>
    /// <param name="message">Human-readable message.</param>
    /// <param name="httpStatus">HTTP status, or 0 when no response was received.</param>
    /// <param name="retryable">Whether repeating the identical request can succeed later.</param>
    /// <param name="retryAfter">Seconds to wait before retrying, when known.</param>
    /// <param name="requestId">Server-side request id.</param>
    /// <param name="field">Offending request field, for validation failures.</param>
    /// <param name="synthetic">True when no gateway envelope was present.</param>
    /// <param name="raw">Decoded (or raw) response body; never serialized or logged.</param>
    /// <param name="innerException">Underlying exception, when there is one.</param>
    public OblodaiException(
        string code,
        string message,
        int httpStatus = 0,
        bool retryable = false,
        int? retryAfter = null,
        string? requestId = null,
        string? field = null,
        bool synthetic = false,
        object? raw = null,
        Exception? innerException = null)
        : base(Format(code, message, requestId), innerException)
    {
        Description = message;
        Code = code;
        HttpStatus = httpStatus;
        Retryable = retryable;
        RetryAfter = retryAfter;
        RequestId = requestId;
        Field = field;
        Synthetic = synthetic;
        Raw = raw;
    }

    /// <summary>Stable machine code (<c>family.reason</c>), e.g. <c>payout.insufficient_funds</c>.</summary>
    public string Code { get; }

    /// <summary>
    /// The explanation alone, without the code and request id that <see cref="Exception.Message"/>
    /// (<c>[code] text (request_id=…)</c>) wraps around it.
    /// </summary>
    public string Description { get; }

    /// <summary>HTTP status, or 0 when no response was received.</summary>
    public int HttpStatus { get; }

    /// <summary>Whether repeating the identical request can succeed later.</summary>
    public bool Retryable { get; }

    /// <summary>Seconds to wait before retrying, when the gateway (or a <c>Retry-After</c> header) said so.</summary>
    public int? RetryAfter { get; }

    /// <summary>Server-side request id — quote it when contacting support.</summary>
    public string? RequestId { get; }

    /// <summary>The request field the error refers to, for validation failures.</summary>
    public string? Field { get; }

    /// <summary>No gateway envelope: the answer came from something in front of the gateway.</summary>
    public bool Synthetic { get; }

    /// <summary>Raw body. Never serialized and never logged, so a dump cannot leak payload data.</summary>
    [JsonIgnore]
    public object? Raw { get; }

    /// <summary>Code family (<c>payout</c> in <c>payout.insufficient_funds</c>).</summary>
    public string Family
    {
        get
        {
            var dot = Code.IndexOf('.');
            return dot < 0 ? Code : Code[..dot];
        }
    }

    /// <summary>Structured-logger friendly view: keeps the message, drops the raw body.</summary>
    public IReadOnlyDictionary<string, object?> ToLogRecord() => new Dictionary<string, object?>
    {
        ["name"] = GetType().Name,
        ["code"] = Code,
        ["message"] = Description,
        ["httpStatus"] = HttpStatus,
        ["retryable"] = Retryable,
        ["retryAfter"] = RetryAfter,
        ["requestId"] = RequestId,
        ["field"] = Field,
    };

    /// <summary>JSON view of <see cref="ToLogRecord"/>.</summary>
    public string ToJson() => JsonSerializer.Serialize(ToLogRecord());

    /// <summary>What a log line shows: <c>[code] text (request_id=…)</c>, the request id only when known.</summary>
    /// <param name="code">Machine code.</param>
    /// <param name="message">Explanation.</param>
    /// <param name="requestId">Server-side request id.</param>
    public static string Format(string code, string message, string? requestId)
        => string.IsNullOrEmpty(requestId) ? $"[{code}] {message}" : $"[{code}] {message} (request_id={requestId})";
}

/// <summary>The fields of an error envelope, as the SDK reconstructs them from a response.</summary>
/// <param name="Code">Machine code (<c>family.reason</c>).</param>
/// <param name="Message">Human-readable message.</param>
/// <param name="HttpStatus">HTTP status of the answer.</param>
/// <param name="Retryable">Whether repeating the identical request can succeed later.</param>
/// <param name="RetryAfter">Seconds to wait before retrying, when known.</param>
/// <param name="RequestId">Server-side request id.</param>
/// <param name="Field">Offending request field, for validation failures.</param>
/// <param name="Synthetic">True when no gateway envelope was present.</param>
/// <param name="Raw">Decoded (or raw) response body; never serialized or logged.</param>
public sealed record ApiErrorInit(
    string Code,
    string Message,
    int HttpStatus,
    bool Retryable,
    int? RetryAfter = null,
    string? RequestId = null,
    string? Field = null,
    bool Synthetic = false,
    object? Raw = null);
