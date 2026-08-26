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

    /// <summary>Your own idempotency key; generated automatically on create routes when omitted.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Prefer the payout key pair on a route that accepts either kind.</summary>
    public bool PreferPayoutKey { get; init; }

    /// <summary>Per-attempt timeout, milliseconds.</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>Overall budget for this call including retries, milliseconds.</summary>
    public int? DeadlineMs { get; init; }

    /// <summary>Extra headers for this call only, merged over the transport's own.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>A raw (bare-route) response: the bytes plus the headers that describe them.</summary>
/// <param name="Status">HTTP status.</param>
/// <param name="Body">Response bytes.</param>
/// <param name="ContentType">Value of the <c>Content-Type</c> header.</param>
/// <param name="ContentDisposition">Value of the <c>Content-Disposition</c> header.</param>
public sealed record RawResponse(int Status, byte[] Body, string? ContentType, string? ContentDisposition);

/// <summary>How the transport is wired up.</summary>
public sealed record TransportOptions
{
    /// <summary>Gateway origin, optionally with a path prefix.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>Used for payment and <c>any</c> routes, and for payout routes when no payout pair exists.</summary>
    public Credentials? Credentials { get; init; }

    /// <summary>Optional second key pair for payout routes.</summary>
    public Credentials? PayoutCredentials { get; init; }

    /// <summary>Per-attempt timeout, ms. Default 30000.</summary>
    public int TimeoutMs { get; init; } = 30_000;

    /// <summary>Overall budget per call including retries and pauses, ms. Default 90000.</summary>
    public int DeadlineMs { get; init; } = 90_000;

    /// <summary>Retry policy.</summary>
    public RetryOptions Retry { get; init; } = RetryOptions.Default;

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
            .Append(", PayoutCredentials = ").Append(PayoutCredentials)
            .Append(", TimeoutMs = ").Append(TimeoutMs)
            .Append(", DeadlineMs = ").Append(DeadlineMs)
            .Append(", Retry = ").Append(Retry)
            .Append(", Clock = ").Append(Clock)
            .Append(", Logger = ").Append(Logger)
            .Append(", UserAgent = ").Append(UserAgent)
            .Append(", Headers = ").Append(Headers)
            .Append(", ").AppendRedacted(nameof(AdminToken), AdminToken is not null);
        return true;
    }
}
