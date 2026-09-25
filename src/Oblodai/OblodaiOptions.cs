using System.Text;
using System.Text.Json.Serialization;

namespace Oblodai;

/// <summary>
/// How to reach the gateway and how to behave while doing it. Every field falls back to an
/// environment variable, so a container needs no code to be configured.
/// </summary>
public sealed record OblodaiOptions
{
    /// <summary>The gateway used when nothing else is configured.</summary>
    public const string DefaultBaseUrl = "https://api.oblodai.com";

    /// <summary>Public id of the API key (<see cref="Contract.SigningProtocol.HeaderPublicId"/>). Falls back to <c>OBLODAI_PUBLIC_ID</c>.</summary>
    public string? PublicId { get; init; }

    /// <summary>
    /// Secret of the API key. Falls back to <c>OBLODAI_SECRET</c>. Redacted by <c>ToString()</c> and
    /// never serialized — options objects are dumped into logs and configuration endpoints constantly.
    /// </summary>
    [JsonIgnore]
    public string? Secret { get; init; }

    /// <summary>API origin. Falls back to <c>OBLODAI_BASE_URL</c>, then <see cref="DefaultBaseUrl"/>.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Per-attempt timeout. Default 30 seconds.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Overall budget per call, retries and pauses included. Default 90 seconds.</summary>
    public TimeSpan? Deadline { get; init; }

    /// <summary>Retry policy; <c>new RetryOptions { MaxRetries = 0 }</c> disables retries.</summary>
    public RetryOptions? Retry { get; init; }

    /// <summary>Called once per attempt before and after it is sent: metrics, tracing, structured logs.</summary>
    public Hooks? Hooks { get; init; }

    /// <summary>
    /// Time source for retry pauses and the call deadline; <see cref="TimeProvider.System"/> by default.
    /// A test passes its own to observe pauses without waiting them out.
    /// </summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Structured logger; <c>OBLODAI_LOG=debug</c> selects a console logger when omitted.</summary>
    public IOblodaiLogger? Logger { get; init; }

    /// <summary>Extra headers on every request.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Admin token of a self-hosted gateway; only the merchant-provisioning routes use it. Falls back to
    /// <c>OBLODAI_ADMIN_TOKEN</c>. Redacted and never serialized.
    /// </summary>
    [JsonIgnore]
    public string? AdminToken { get; init; }

    /// <summary>Permit plain <c>http://</c> base URLs outside loopback (local gateway, CI). Default false.</summary>
    public bool? AllowInsecureBaseUrl { get; init; }

    /// <summary>Signing clock; injectable for tests.</summary>
    public SkewCorrectingClock? Clock { get; init; }

    /// <summary>Prints every option, with the two secret-bearing ones replaced by a placeholder.</summary>
    /// <param name="builder">Buffer the record's <c>ToString()</c> writes into.</param>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("PublicId = ").Append(PublicId)
            .Append(", ").AppendRedacted(nameof(Secret), Secret is not null)
            .Append(", BaseUrl = ").Append(BaseUrl)
            .Append(", Timeout = ").Append(Timeout)
            .Append(", Deadline = ").Append(Deadline)
            .Append(", Retry = ").Append(Retry)
            .Append(", Hooks = ").Append(Hooks)
            .Append(", Logger = ").Append(Logger)
            .Append(", Headers = ").Append(Headers)
            .Append(", ").AppendRedacted(nameof(AdminToken), AdminToken is not null)
            .Append(", AllowInsecureBaseUrl = ").Append(AllowInsecureBaseUrl)
            .Append(", Clock = ").Append(Clock);
        return true;
    }

    /// <summary>Merge these options with the environment and validate what can be validated up front.</summary>
    /// <param name="env">Environment lookup; the process environment by default.</param>
    /// <exception cref="ConfigException">Half a key pair, or a base URL that is not usable.</exception>
    public ResolvedOptions Resolve(Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;

        var baseUrl = (BaseUrl ?? env("OBLODAI_BASE_URL") ?? DefaultBaseUrl).TrimEnd('/');
        AssertBaseUrl(baseUrl, AllowInsecureBaseUrl ?? env("OBLODAI_ALLOW_INSECURE") == "1");

        var publicId = Empty(PublicId ?? env("OBLODAI_PUBLIC_ID"));
        var secret = Empty(Secret ?? env("OBLODAI_SECRET"));
        if (publicId is null != secret is null)
        {
            throw new ConfigException(
                SdkErrorCodes.BadConfig,
                "PublicId and Secret must be provided together (or set both OBLODAI_PUBLIC_ID and OBLODAI_SECRET)");
        }

        var logger = Logger;
        if (logger is null && env("OBLODAI_LOG") is { Length: > 0 } level)
        {
            logger = level.ToLowerInvariant() switch
            {
                "debug" => new ConsoleLogger(OblodaiLogLevel.Debug),
                "info" => new ConsoleLogger(OblodaiLogLevel.Info),
                "warn" => new ConsoleLogger(OblodaiLogLevel.Warn),
                "error" => new ConsoleLogger(OblodaiLogLevel.Error),
                _ => null,
            };
        }

        return new ResolvedOptions
        {
            BaseUrl = baseUrl,
            Credentials = publicId is null || secret is null ? null : new Credentials(publicId, secret),
            Timeout = Positive(Timeout, nameof(Timeout)),
            Deadline = Positive(Deadline, nameof(Deadline)),
            Retry = Retry,
            Hooks = Hooks,
            TimeProvider = TimeProvider,
            Logger = logger,
            Headers = Headers,
            AdminToken = Empty(AdminToken ?? env("OBLODAI_ADMIN_TOKEN")),
            Clock = Clock,
        };
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static TimeSpan? Positive(TimeSpan? value, string name)
    {
        if (value is { } span && span <= TimeSpan.Zero)
        {
            throw new ConfigException(SdkErrorCodes.BadConfig, $"{name} must be positive (got {span})", name);
        }

        return value;
    }

    private static void AssertBaseUrl(string baseUrl, bool allowInsecure)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed))
        {
            throw new ConfigException(SdkErrorCodes.BadConfig, $"BaseUrl is not a valid URL: {baseUrl}", "BaseUrl");
        }

        if (parsed.Scheme == Uri.UriSchemeHttps)
        {
            return;
        }

        var local = parsed.Host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
        if (parsed.Scheme == Uri.UriSchemeHttp && (allowInsecure || local))
        {
            return;
        }

        throw new ConfigException(
            SdkErrorCodes.BadConfig,
            $"BaseUrl must use https (got {parsed.Scheme}://{parsed.Authority}); set AllowInsecureBaseUrl for a local gateway",
            "BaseUrl");
    }
}

/// <summary>Options after the environment has been folded in and the obvious mistakes rejected.</summary>
public sealed record ResolvedOptions
{
    /// <summary>API origin without a trailing slash.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>The merchant's API key pair, when configured. Its secret is redacted and not serialized.</summary>
    public Credentials? Credentials { get; init; }

    /// <summary>Per-attempt timeout.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Overall budget per call.</summary>
    public TimeSpan? Deadline { get; init; }

    /// <summary>Retry policy.</summary>
    public RetryOptions? Retry { get; init; }

    /// <summary>Request and response hooks.</summary>
    public Hooks? Hooks { get; init; }

    /// <summary>Time source for pauses and deadlines.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Structured logger.</summary>
    public IOblodaiLogger? Logger { get; init; }

    /// <summary>Extra headers on every request.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Admin token for merchant provisioning on a self-hosted gateway. Redacted and never serialized.</summary>
    [JsonIgnore]
    public string? AdminToken { get; init; }

    /// <summary>Signing clock.</summary>
    public SkewCorrectingClock? Clock { get; init; }

    /// <summary>Prints the resolved wiring with the admin token replaced by a placeholder.</summary>
    /// <param name="builder">Buffer the record's <c>ToString()</c> writes into.</param>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("BaseUrl = ").Append(BaseUrl)
            .Append(", Credentials = ").Append(Credentials)
            .Append(", Timeout = ").Append(Timeout)
            .Append(", Deadline = ").Append(Deadline)
            .Append(", Retry = ").Append(Retry)
            .Append(", Hooks = ").Append(Hooks)
            .Append(", Logger = ").Append(Logger)
            .Append(", Headers = ").Append(Headers)
            .Append(", ").AppendRedacted(nameof(AdminToken), AdminToken is not null)
            .Append(", Clock = ").Append(Clock);
        return true;
    }
}

/// <summary>
/// Overrides for one call — the last-but-one argument of every resource method. Every field left
/// null falls back to the client's setting.
/// </summary>
public sealed record RequestOptions
{
    /// <summary>
    /// Your own idempotency key; generated automatically on routes the gateway deduplicates, and
    /// rejected (<c>sdk.idempotency_unsupported</c>) on routes it does not. On a route whose body has
    /// its own <c>idempotency_key</c> field this fills that field instead.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Per-attempt timeout for this call (capped by the client's <see cref="OblodaiOptions.Deadline"/>).</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Retries after the first attempt for this call; overrides <see cref="RetryOptions.MaxRetries"/>.</summary>
    public int? MaxRetries { get; init; }

    /// <summary>
    /// Extra headers for this call alone, merged over the client's own. Names the SDK owns (the
    /// signature headers, the idempotency key, <c>X-Request-ID</c>, <c>Accept</c>,
    /// <c>Content-Type</c>, <c>User-Agent</c>, <c>X-Admin-Token</c>) are ignored, and a value with a
    /// CR, LF or non-ASCII character is refused with <c>sdk.bad_header</c> before anything is signed.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }

    /// <summary>
    /// Sent as <c>X-Request-ID</c> on every attempt of this call, to tie your logs to the gateway's;
    /// a fresh id is generated when omitted.
    /// </summary>
    public string? RequestId { get; init; }
}
