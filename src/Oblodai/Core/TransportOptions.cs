using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai;

/// <summary>Per-call knobs the transport understands.</summary>
public sealed record CallOptions
{
    /// <summary>Request body object; serialized once and signed byte-exactly.</summary>
    public object? Body { get; init; }

    /// <summary>Query parameters, in order.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>>? Query { get; init; }

    /// <summary>Values for the <c>{name}</c> segments of the route path.</summary>
    public IReadOnlyDictionary<string, string>? PathParams { get; init; }

    /// <summary>Your own idempotency key; generated automatically on deduplicated routes when omitted.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Per-attempt timeout.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Retries after the first attempt; the client's policy when null.</summary>
    public int? MaxRetries { get; init; }

    /// <summary>Extra headers for this call only, merged over the transport's own.</summary>
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }

    /// <summary><c>X-Request-ID</c> of the call; generated when null.</summary>
    public string? RequestId { get; init; }

    /// <summary>The per-call fields of <paramref name="options"/> plus the request itself.</summary>
    /// <param name="options">Caller's options, or null.</param>
    /// <param name="body">Request body object.</param>
    /// <param name="pathParams">Values of the path placeholders.</param>
    /// <param name="query">Query parameters.</param>
    public static CallOptions From(
        RequestOptions? options,
        object? body = null,
        IReadOnlyDictionary<string, string>? pathParams = null,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null)
        => new()
        {
            Body = body,
            PathParams = pathParams,
            Query = query,
            IdempotencyKey = options?.IdempotencyKey,
            Timeout = options?.Timeout,
            MaxRetries = options?.MaxRetries,
            ExtraHeaders = options?.ExtraHeaders,
            RequestId = options?.RequestId,
        };
}

/// <summary>A successful answer as it came off the wire: status, headers and bytes.</summary>
/// <param name="Status">HTTP status.</param>
/// <param name="Body">Response bytes.</param>
/// <param name="ContentType">Value of the <c>Content-Type</c> header.</param>
/// <param name="ContentDisposition">Value of the <c>Content-Disposition</c> header.</param>
public sealed record RawResponse(int Status, byte[] Body, string? ContentType, string? ContentDisposition)
{
    /// <summary>Response headers (content headers included), case-insensitive.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The response's <c>X-Request-ID</c>, else the one the SDK sent with the call.</summary>
    public string RequestId { get; init; } = string.Empty;
}

/// <summary>How the transport is wired up.</summary>
public sealed record TransportOptions
{
    /// <summary>Gateway origin, optionally with a path prefix.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>The merchant's API key pair; it signs every signed route.</summary>
    public Credentials? Credentials { get; init; }

    /// <summary>Per-attempt timeout. Default 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Overall budget per call including retries and pauses. Default 90 seconds.</summary>
    public TimeSpan Deadline { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Retry policy.</summary>
    public RetryOptions Retry { get; init; } = RetryOptions.Default;

    /// <summary>Request and response hooks.</summary>
    public Hooks? Hooks { get; init; }

    /// <summary>Time source for retry pauses and the call deadline.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Signing clock.</summary>
    public SkewCorrectingClock? Clock { get; init; }

    /// <summary>Structured logger.</summary>
    public IOblodaiLogger? Logger { get; init; }

    /// <summary>Value of the <c>User-Agent</c> header.</summary>
    public required string UserAgent { get; init; }

    /// <summary>Extra headers on every request. Never signed material.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Sent as <c>X-Admin-Token</c> on merchant-provisioning routes only. Redacted and never serialized.</summary>
    [JsonIgnore]
    public string? AdminToken { get; init; }

    /// <summary>Prints the wiring with the admin token replaced by a placeholder.</summary>
    /// <param name="builder">Buffer the record's <c>ToString()</c> writes into.</param>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("BaseUrl = ").Append(BaseUrl)
            .Append(", Credentials = ").Append(Credentials)
            .Append(", Timeout = ").Append(Timeout)
            .Append(", Deadline = ").Append(Deadline)
            .Append(", Retry = ").Append(Retry)
            .Append(", Hooks = ").Append(Hooks)
            .Append(", Clock = ").Append(Clock)
            .Append(", Logger = ").Append(Logger)
            .Append(", UserAgent = ").Append(UserAgent)
            .Append(", Headers = ").Append(Headers)
            .Append(", ").AppendRedacted(nameof(AdminToken), AdminToken is not null);
        return true;
    }
}
