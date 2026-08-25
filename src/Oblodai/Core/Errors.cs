using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oblodai;

/// <summary>
/// The gateway's error envelope: <c>{ "error": { code, message, field?, retryable, retry_after?, request_id? } }</c>.
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

    /// <summary>The caller cancelled the call.</summary>
    public const string TransportAborted = "transport.aborted";

    /// <summary>A retry would have exceeded the call deadline.</summary>
    public const string TransportDeadline = "transport.deadline";

    /// <summary>A webhook signature did not match the body.</summary>
    public const string WebhookBadSignature = "webhook.bad_signature";

    /// <summary>A webhook timestamp is outside the tolerance window.</summary>
    public const string WebhookStaleTimestamp = "webhook.stale_timestamp";

    /// <summary>A webhook delivery lacks the timestamp or signature header.</summary>
    public const string WebhookMissingHeader = "webhook.missing_header";
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
        : base(message, innerException)
    {
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
        ["message"] = Message,
        ["httpStatus"] = HttpStatus,
        ["retryable"] = Retryable,
        ["retryAfter"] = RetryAfter,
        ["requestId"] = RequestId,
        ["field"] = Field,
    };

    /// <summary>JSON view of <see cref="ToLogRecord"/>.</summary>
    public string ToJson() => JsonSerializer.Serialize(ToLogRecord());

    /// <inheritdoc />
    public override string ToString()
        => $"{GetType().Name}: {Code} (HTTP {HttpStatus}{(RequestId is null ? string.Empty : $", request {RequestId}")}) {Message}";
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

/// <summary>The gateway (or something in front of it) answered with an error status.</summary>
public class ApiException : OblodaiException
{
    /// <summary>Build an API error from a (possibly synthesized) envelope.</summary>
    /// <param name="init">Envelope fields.</param>
    public ApiException(ApiErrorInit init)
        : base(init.Code, init.Message, init.HttpStatus, init.Retryable, init.RetryAfter, init.RequestId, init.Field,
            init.Synthetic, init.Raw)
    {
    }
}

/// <summary>400 — the request is malformed or violates a business rule; see <see cref="OblodaiException.Field"/>.</summary>
public sealed class ValidationException : ApiException
{
    /// <summary>Build the error from an envelope.</summary>
    /// <param name="init">Envelope fields.</param>
    public ValidationException(ApiErrorInit init)
        : base(init)
    {
    }
}

/// <summary>401 — bad signature, unknown key, clock skew, IP not in the allow-list.</summary>
public sealed class AuthenticationException : ApiException
{
    /// <summary>Build the error from an envelope.</summary>
    /// <param name="init">Envelope fields.</param>
    public AuthenticationException(ApiErrorInit init)
        : base(init)
    {
    }
}

/// <summary>403 — the key is valid but not allowed to do this (wrong key kind, feature disabled).</summary>
public sealed class PermissionException : ApiException
{
    /// <summary>Build the error from an envelope.</summary>
    /// <param name="init">Envelope fields.</param>
    public PermissionException(ApiErrorInit init)
        : base(init)
    {
    }
}

/// <summary>404 — the referenced object does not exist for this merchant.</summary>
public sealed class NotFoundException : ApiException
{
    /// <summary>Build the error from an envelope.</summary>
    /// <param name="init">Envelope fields.</param>
    public NotFoundException(ApiErrorInit init)
        : base(init)
    {
    }
}

/// <summary>409 — state or idempotency conflict.</summary>
public class ConflictException : ApiException
{
    /// <summary>Build the error from an envelope.</summary>
    /// <param name="init">Envelope fields.</param>
    public ConflictException(ApiErrorInit init)
        : base(init)
    {
    }
}

/// <summary>409 <c>idempotency.key_reused</c> — the same key was used with a different request body.</summary>
public sealed class IdempotencyConflictException : ConflictException
{
    /// <summary>Build the error from an envelope.</summary>
    /// <param name="init">Envelope fields.</param>
    public IdempotencyConflictException(ApiErrorInit init)
        : base(init)
    {
    }
}

/// <summary>429 — rate limited; <see cref="OblodaiException.RetryAfter"/> is set.</summary>
public sealed class RateLimitException : ApiException
{
    /// <summary>Build the error from an envelope.</summary>
    /// <param name="init">Envelope fields.</param>
    public RateLimitException(ApiErrorInit init)
        : base(init)
    {
    }
}

/// <summary>503 — an upstream dependency is down; safe to retry after a pause.</summary>
public sealed class UnavailableException : ApiException
{
    /// <summary>Build the error from an envelope.</summary>
    /// <param name="init">Envelope fields.</param>
    public UnavailableException(ApiErrorInit init)
        : base(init)
    {
    }
}

/// <summary>5xx other than 503.</summary>
public sealed class InternalException : ApiException
{
    /// <summary>Build the error from an envelope.</summary>
    /// <param name="init">Envelope fields.</param>
    public InternalException(ApiErrorInit init)
        : base(init)
    {
    }
}

/// <summary>The request never produced an HTTP response: DNS, TCP, TLS, timeout, cancellation, deadline.</summary>
public sealed class TransportException : OblodaiException
{
    /// <summary>Build a transport failure.</summary>
    /// <param name="code">One of the <c>transport.*</c> codes.</param>
    /// <param name="message">What happened.</param>
    /// <param name="innerException">Underlying exception.</param>
    public TransportException(string code, string message, Exception? innerException = null)
        : base(
            code,
            message,
            httpStatus: 0,
            retryable: code is SdkErrorCodes.TransportTimeout or SdkErrorCodes.TransportNetwork,
            innerException: innerException)
    {
    }
}

/// <summary>Raised before any request is sent: bad options, missing credentials, unusable arguments.</summary>
public sealed class ConfigException : OblodaiException
{
    /// <summary>Build a configuration failure.</summary>
    /// <param name="code">One of the <c>sdk.*</c> codes.</param>
    /// <param name="message">What is wrong.</param>
    /// <param name="field">The option or argument at fault.</param>
    public ConfigException(string code, string message, string? field = null)
        : base(code, message, httpStatus: 0, retryable: false, field: field)
    {
    }
}

/// <summary>The response could not be interpreted as the documented envelope.</summary>
public sealed class ContractException : OblodaiException
{
    /// <summary>Build an envelope failure.</summary>
    /// <param name="message">What the body looked like.</param>
    /// <param name="httpStatus">HTTP status of the response.</param>
    /// <param name="raw">Raw body (never logged).</param>
    public ContractException(string message, int httpStatus, object? raw = null)
        : base(SdkErrorCodes.BadEnvelope, message, httpStatus, retryable: false, raw: raw)
    {
    }
}

/// <summary>Webhook verification failed (bad signature, stale timestamp, missing headers).</summary>
public sealed class SignatureException : OblodaiException
{
    /// <summary>Build a verification failure.</summary>
    /// <param name="code">One of the <c>webhook.*</c> codes.</param>
    /// <param name="message">Why the delivery was rejected.</param>
    public SignatureException(string code, string message)
        : base(code, message, httpStatus: 0, retryable: false)
    {
    }
}

/// <summary>Builds the right subclass from an error envelope (or a synthesized one) and the HTTP status.</summary>
public static class ApiExceptionFactory
{
    /// <summary>Statuses a response without an envelope may carry transiently (LB/proxy/timeouts).</summary>
    private static readonly HashSet<int> TransientStatuses = [408, 425, 429, 500, 502, 503, 504];

    /// <summary>Create the error that matches <paramref name="httpStatus"/> and <paramref name="detail"/>.</summary>
    /// <param name="httpStatus">HTTP status of the answer.</param>
    /// <param name="detail">The envelope's <c>error</c> object, or a synthesized stand-in.</param>
    /// <param name="raw">Raw body for debugging; never serialized.</param>
    /// <param name="synthetic">True when the answer carried no gateway envelope.</param>
    /// <param name="retryAfterHeader">Parsed <c>Retry-After</c> header, seconds.</param>
    public static ApiException Create(
        int httpStatus,
        ErrorDetail detail,
        object? raw = null,
        bool synthetic = false,
        int? retryAfterHeader = null)
    {
        var code = string.IsNullOrEmpty(detail.Code) ? "internal" : detail.Code;
        var message = string.IsNullOrEmpty(detail.Message)
            ? $"request failed with HTTP {httpStatus} ({(string.IsNullOrEmpty(detail.Code) ? "no envelope" : detail.Code)})"
            : detail.Message!;
        var retryable = synthetic
            ? TransientStatuses.Contains(httpStatus)
            : detail.Retryable ?? (httpStatus is 429 or 503);
        var retryAfter = detail.RetryAfter ?? retryAfterHeader;

        var init = new ApiErrorInit(code, message, httpStatus, retryable, retryAfter, detail.RequestId, detail.Field,
            synthetic, raw);

        if (code == "idempotency.key_reused")
        {
            return new IdempotencyConflictException(init);
        }

        return httpStatus switch
        {
            400 => new ValidationException(init),
            401 => new AuthenticationException(init),
            403 => new PermissionException(init),
            404 => new NotFoundException(init),
            409 => new ConflictException(init),
            429 => new RateLimitException(init),
            503 => new UnavailableException(init),
            >= 500 => new InternalException(init),
            _ => new ApiException(init),
        };
    }
}
