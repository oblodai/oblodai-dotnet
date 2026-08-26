using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oblodai;

/// <summary>Offset pagination block the gateway attaches to every paged list.</summary>
public sealed record Paginate
{
    /// <summary>Total number of rows matching the query.</summary>
    [JsonPropertyName("total")]
    public int Total { get; init; }

    /// <summary>Rows per page as the gateway applied it.</summary>
    [JsonPropertyName("per_page")]
    public int PerPage { get; init; }

    /// <summary>Offset of this page.</summary>
    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    /// <summary>The gateway's own "there is more" flag — iteration stops on it.</summary>
    [JsonPropertyName("has_pages")]
    public bool HasPages { get; init; }
}

/// <summary>One page of a paged list route: <c>{ items, paginate }</c>.</summary>
/// <typeparam name="T">Item model.</typeparam>
public sealed record Page<T>
{
    /// <summary>The rows of this page.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Pagination block.</summary>
    [JsonPropertyName("paginate")]
    public Paginate Paginate { get; init; } = new();
}

/// <summary>A list the gateway caps by catalogue size rather than paginating: <c>{ items }</c>.</summary>
/// <typeparam name="T">Item model.</typeparam>
public sealed record PlainList<T>
{
    /// <summary>The rows.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; init; } = [];
}

/// <summary>Outcome of reading a response body: either a <c>result</c> payload or a classified error.</summary>
public readonly struct DecodedEnvelope
{
    private DecodedEnvelope(bool ok, JsonElement result, ApiException? error)
    {
        Ok = ok;
        Result = result;
        Error = error;
    }

    /// <summary>True when the body carried a success envelope.</summary>
    public bool Ok { get; }

    /// <summary>The <c>result</c> payload; only meaningful when <see cref="Ok"/>.</summary>
    public JsonElement Result { get; }

    /// <summary>The classified failure; only set when <see cref="Ok"/> is false.</summary>
    public ApiException? Error { get; }

    /// <summary>Wrap a successful payload.</summary>
    /// <param name="result">The <c>result</c> element.</param>
    public static DecodedEnvelope Success(JsonElement result) => new(true, result, null);

    /// <summary>Wrap a failure.</summary>
    /// <param name="error">The classified error.</param>
    public static DecodedEnvelope Failure(ApiException error) => new(false, default, error);
}

/// <summary>
/// Response envelopes, as the gateway writes them:
/// <code>
/// success : { "state": 0, "result": &lt;payload&gt; }
/// list    : result = { "items": [...], "paginate": { total, per_page, offset, has_pages } }
/// error   : { "error": { code, message, field?, retryable, retry_after?, request_id? } }
/// </code>
/// Every non-bare route uses these; bare routes (PDF/CSV documents) bypass this decoder.
/// </summary>
public static class EnvelopeDecoder
{
    /// <summary>
    /// Ceiling on any server-provided pause, in seconds (one day). Bounds the value before it becomes an
    /// <see cref="int"/> of milliseconds anywhere, so no arithmetic downstream can overflow; the retry
    /// policy applies its own, much smaller, <see cref="RetryOptions.MaxRetryAfterMs"/> on top.
    /// </summary>
    public const int MaxRetryAfterSeconds = 86_400;

    /// <summary>Interpret a response body.</summary>
    /// <param name="httpStatus">HTTP status of the response.</param>
    /// <param name="text">The raw body, so non-JSON failures keep their evidence.</param>
    /// <param name="retryAfterHeader">Value of the <c>Retry-After</c> header, if any.</param>
    /// <param name="locationHeader">Value of the <c>Location</c> header, for redirects.</param>
    public static DecodedEnvelope Decode(
        int httpStatus,
        string text,
        string? retryAfterHeader = null,
        string? locationHeader = null)
    {
        var retryAfter = ParseRetryAfter(retryAfterHeader);

        if (httpStatus is >= 300 and < 400)
        {
            var where = string.IsNullOrEmpty(locationHeader) ? string.Empty : $" to {locationHeader}";
            return DecodedEnvelope.Failure(ApiExceptionFactory.Create(
                httpStatus,
                new ErrorDetail
                {
                    Code = "internal",
                    Message = $"unexpected redirect (HTTP {httpStatus}){where}; check BaseUrl",
                },
                text,
                synthetic: true));
        }

        JsonElement body;
        try
        {
            if (text.Length == 0)
            {
                return httpStatus >= 400
                    ? DecodedEnvelope.Failure(NoEnvelope(httpStatus, text, retryAfter))
                    : throw new ContractException($"expected a JSON envelope, got {Describe(text)}", httpStatus, text);
            }

            using var doc = JsonDocument.Parse(text);
            body = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            if (httpStatus >= 400)
            {
                return DecodedEnvelope.Failure(NoEnvelope(httpStatus, text, retryAfter));
            }

            throw new ContractException($"expected a JSON envelope, got {Describe(text)}", httpStatus, text);
        }

        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object)
        {
            var detail = ReadErrorDetail(error);
            if (detail is null)
            {
                // An "error" object without a usable code is not an envelope the gateway wrote; the answer
                // still has to classify as its HTTP status rather than as a JSON parse crash.
                var requestId = String(error, "request_id");
                return DecodedEnvelope.Failure(NoEnvelope(httpStatus, text, retryAfter, requestId));
            }

            return DecodedEnvelope.Failure(ApiExceptionFactory.Create(httpStatus, detail, text, retryAfterHeader: retryAfter));
        }

        if (httpStatus >= 400)
        {
            return DecodedEnvelope.Failure(NoEnvelope(httpStatus, text, retryAfter));
        }

        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("state", out var state)
            && state.ValueKind == JsonValueKind.Number
            && state.GetInt32() == 0
            && body.TryGetProperty("result", out var result))
        {
            return DecodedEnvelope.Success(result);
        }

        throw new ContractException(
            $"response is not a {{state:0,result}} envelope: {Describe(text)}", httpStatus, text);
    }

    /// <summary>
    /// The <c>error</c> object read one field at a time. Returns null when the object carries no usable
    /// <c>code</c> — the caller then synthesizes a no-envelope error from the HTTP status. A field of the
    /// wrong JSON type is treated as absent, never as a reason to throw: a gateway that starts sending
    /// <c>retryable: "yes"</c> must degrade to the status-derived default, not crash the call.
    /// </summary>
    /// <param name="error">The <c>error</c> object.</param>
    public static ErrorDetail? ReadErrorDetail(JsonElement error)
    {
        var code = String(error, "code");
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        return new ErrorDetail
        {
            Code = code!,
            Message = String(error, "message"),
            Field = String(error, "field"),
            RequestId = String(error, "request_id"),
            Retryable = error.TryGetProperty("retryable", out var retryable)
                ? retryable.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null,
                }
                : null,
            RetryAfter = ReadRetryAfter(error),
        };
    }

    /// <summary>
    /// <c>retry_after</c> as an integer, a float or a numeric string; anything else is absent. Read in
    /// <see cref="double"/> and clamped before it becomes an <see cref="int"/>, so a body claiming
    /// <c>1e30</c> seconds cannot wrap around into a negative pause.
    /// </summary>
    /// <param name="error">The <c>error</c> object.</param>
    public static int? ReadRetryAfter(JsonElement error)
    {
        if (!error.TryGetProperty("retry_after", out var value))
        {
            return null;
        }

        double seconds;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDouble(out var number):
                seconds = number;
                break;
            case JsonValueKind.String when double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                seconds = parsed;
                break;
            default:
                return null;
        }

        return ClampSeconds(seconds);
    }

    /// <summary><c>Retry-After</c> as delta-seconds or an HTTP-date; null when absent or unparsable.</summary>
    /// <param name="value">Header value.</param>
    /// <param name="now">Reference time for HTTP-date forms.</param>
    public static int? ParseRetryAfter(string? value, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var v = value.Trim();

        // Delta-seconds. Parsed as a double so "999999999999" is a pause to clamp, not an unparsable value.
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return ClampSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            // Subtracting two DateTimeOffsets cannot overflow, and the clamp catches the year-9999 case.
            return ClampSeconds(Math.Ceiling((at - (now ?? DateTimeOffset.UtcNow)).TotalSeconds));
        }

        return null;
    }

    /// <summary>Into <c>[0, <see cref="MaxRetryAfterSeconds"/>]</c>; NaN is not a pause at all.</summary>
    /// <param name="seconds">Seconds as the wire stated them.</param>
    private static int? ClampSeconds(double seconds)
    {
        if (double.IsNaN(seconds))
        {
            return null;
        }

        return seconds <= 0 ? 0 : (int)Math.Min(Math.Ceiling(seconds), MaxRetryAfterSeconds);
    }

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static ApiException NoEnvelope(int httpStatus, string text, int? retryAfter, string? requestId = null) =>
        ApiExceptionFactory.Create(
            httpStatus,
            new ErrorDetail
            {
                Code = "internal",
                Message = $"HTTP {httpStatus} without an Oblodai error envelope ({Describe(text)}) — the answer came "
                          + "from a proxy or load balancer, not the API",
                RequestId = requestId,
            },
            text,
            synthetic: true,
            retryAfterHeader: retryAfter);

    /// <summary>
    /// What the body was, never what it said. The message of an exception ends up in logs, crash
    /// reports and bug trackers; quoting the payload there would undo every other rule about keeping
    /// response data out of them.
    /// </summary>
    /// <param name="text">The body.</param>
    private static string Describe(string text)
    {
        if (text.Length == 0)
        {
            return "<empty body>";
        }

        var trimmed = text.TrimStart();
        var shape = trimmed.Length == 0 ? "blank"
            : trimmed[0] == '{' ? "JSON object"
            : trimmed[0] == '[' ? "JSON array"
            : trimmed[0] == '<' ? "markup"
            : "text";

        return $"<{shape}, {text.Length} chars>";
    }
}
