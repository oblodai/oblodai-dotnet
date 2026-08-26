namespace Oblodai;

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
public class ContractException : OblodaiException
{
    /// <summary>Build an envelope failure.</summary>
    /// <param name="message">What the body looked like.</param>
    /// <param name="httpStatus">HTTP status of the response.</param>
    /// <param name="raw">Raw body (never logged).</param>
    public ContractException(string message, int httpStatus, object? raw = null)
        : this(SdkErrorCodes.BadEnvelope, message, httpStatus, raw)
    {
    }

    /// <summary>Build an envelope failure with a specific code (e.g. <c>webhook.bad_payload</c>).</summary>
    /// <param name="code">The <c>sdk.*</c> or <c>webhook.*</c> code that names the failure.</param>
    /// <param name="message">What the body looked like.</param>
    /// <param name="httpStatus">HTTP status of the response, or 0 when there was none.</param>
    /// <param name="raw">Raw body (never logged).</param>
    public ContractException(string code, string message, int httpStatus, object? raw = null)
        : base(code, message, httpStatus, retryable: false, raw: raw)
    {
    }
}

/// <summary>
/// A webhook delivery whose signature matched but whose body this SDK cannot read. It is in the
/// CONTRACT family, not the signature family: the delivery is authentic, and a receiver that answers
/// 401 to signature failures must not answer 401 to its own decoding bug — the gateway would read that
/// as a rejected endpoint and eventually retire it.
/// </summary>
public sealed class WebhookPayloadException : ContractException
{
    /// <summary>Build a payload failure.</summary>
    /// <param name="message">What the body looked like.</param>
    public WebhookPayloadException(string message)
        : base(SdkErrorCodes.WebhookBadPayload, message, httpStatus: 0)
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

        // Both sources are already clamped to [0, EnvelopeDecoder.MaxRetryAfterSeconds] by the decoder;
        // clamp again here so an ErrorDetail built by hand cannot smuggle a negative or overflowing pause in.
        var retryAfter = Clamp(detail.RetryAfter ?? retryAfterHeader);

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

    private static int? Clamp(int? seconds)
        => seconds is null ? null : Math.Clamp(seconds.Value, 0, EnvelopeDecoder.MaxRetryAfterSeconds);
}
