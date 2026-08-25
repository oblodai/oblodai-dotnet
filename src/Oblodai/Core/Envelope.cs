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
            var detail = error.Deserialize<ErrorDetail>(OblodaiJson.Options) ?? new ErrorDetail();
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
        if (int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds;
        }

        if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            var delta = (at - (now ?? DateTimeOffset.UtcNow)).TotalSeconds;
            return delta <= 0 ? 0 : (int)Math.Ceiling(delta);
        }

        return null;
    }

    private static ApiException NoEnvelope(int httpStatus, string text, int? retryAfter) =>
        ApiExceptionFactory.Create(
            httpStatus,
            new ErrorDetail
            {
                Code = "internal",
                Message = $"HTTP {httpStatus} without an Oblodai error envelope ({Describe(text)}) — the answer came "
                          + "from a proxy or load balancer, not the API",
            },
            text,
            synthetic: true,
            retryAfterHeader: retryAfter);

    private static string Describe(string text)
    {
        if (text.Length == 0)
        {
            return "<empty body>";
        }

        var head = string.Join(' ', text[..Math.Min(120, text.Length)].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > 120 ? head + "…" : head;
    }
}
