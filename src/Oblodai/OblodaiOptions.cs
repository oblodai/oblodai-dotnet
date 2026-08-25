namespace Oblodai;

/// <summary>
/// How to reach the gateway and how to behave while doing it. Every field falls back to an
/// environment variable, so a container needs no code to be configured.
/// </summary>
public sealed record OblodaiOptions
{
    /// <summary>The gateway used when nothing else is configured.</summary>
    public const string DefaultBaseUrl = "https://api.oblodai.com";

    /// <summary>Public id of the API key (<c>X-Public-Id</c>). Falls back to <c>OBLODAI_PUBLIC_ID</c>.</summary>
    public string? PublicId { get; init; }

    /// <summary>Secret of the API key. Falls back to <c>OBLODAI_SECRET</c>.</summary>
    public string? Secret { get; init; }

    /// <summary>
    /// Optional dedicated payout key; the gateway issues payment and payout keys separately. Falls back
    /// to <c>OBLODAI_PAYOUT_PUBLIC_ID</c>.
    /// </summary>
    public string? PayoutPublicId { get; init; }

    /// <summary>Secret of the payout key. Falls back to <c>OBLODAI_PAYOUT_SECRET</c>.</summary>
    public string? PayoutSecret { get; init; }

    /// <summary>API origin. Falls back to <c>OBLODAI_BASE_URL</c>, then <see cref="DefaultBaseUrl"/>.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Per-attempt timeout, ms. Default 30000.</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>Overall budget per call including retries, ms. Default 90000.</summary>
    public int? DeadlineMs { get; init; }

    /// <summary>Retry policy; <c>new RetryOptions { MaxRetries = 0 }</c> disables retries.</summary>
    public RetryOptions? Retry { get; init; }

    /// <summary>Structured logger; <c>OBLODAI_LOG=debug</c> selects a console logger when omitted.</summary>
    public IOblodaiLogger? Logger { get; init; }

    /// <summary>Extra headers on every request.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Admin token of a self-hosted gateway; only the merchant-provisioning routes use it. Falls back to
    /// <c>OBLODAI_ADMIN_TOKEN</c>.
    /// </summary>
    public string? AdminToken { get; init; }

    /// <summary>Permit plain <c>http://</c> base URLs outside loopback (local gateway, CI). Default false.</summary>
    public bool? AllowInsecureBaseUrl { get; init; }

    /// <summary>Signing clock; injectable for tests.</summary>
    public SkewCorrectingClock? Clock { get; init; }

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

        var payoutPublicId = Empty(PayoutPublicId ?? env("OBLODAI_PAYOUT_PUBLIC_ID"));
        var payoutSecret = Empty(PayoutSecret ?? env("OBLODAI_PAYOUT_SECRET"));
        if (payoutPublicId is null != payoutSecret is null)
        {
            throw new ConfigException(
                SdkErrorCodes.BadConfig, "PayoutPublicId and PayoutSecret must be provided together");
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
            PayoutCredentials = payoutPublicId is null || payoutSecret is null
                ? null
                : new Credentials(payoutPublicId, payoutSecret),
            TimeoutMs = TimeoutMs,
            DeadlineMs = DeadlineMs,
            Retry = Retry,
            Logger = logger,
            Headers = Headers,
            AdminToken = Empty(AdminToken ?? env("OBLODAI_ADMIN_TOKEN")),
            Clock = Clock,
        };
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;

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

    /// <summary>Payment (or unified) key pair, when configured.</summary>
    public Credentials? Credentials { get; init; }

    /// <summary>Payout key pair, when configured.</summary>
    public Credentials? PayoutCredentials { get; init; }

    /// <summary>Per-attempt timeout, ms.</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>Overall budget per call, ms.</summary>
    public int? DeadlineMs { get; init; }

    /// <summary>Retry policy.</summary>
    public RetryOptions? Retry { get; init; }

    /// <summary>Structured logger.</summary>
    public IOblodaiLogger? Logger { get; init; }

    /// <summary>Extra headers on every request.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Admin token for merchant provisioning on a self-hosted gateway.</summary>
    public string? AdminToken { get; init; }

    /// <summary>Signing clock.</summary>
    public SkewCorrectingClock? Clock { get; init; }
}

/// <summary>Per-call options every resource method accepts as its last argument.</summary>
public sealed record RequestOptions
{
    /// <summary>
    /// Your own idempotency key; generated automatically on create routes when omitted, and rejected on
    /// routes the gateway does not deduplicate.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Per-attempt timeout, ms.</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>Overall budget including retries, ms.</summary>
    public int? DeadlineMs { get; init; }

    /// <summary>Sign with the payout key on a route that accepts either key kind (e.g. batch lookups).</summary>
    public bool PreferPayoutKey { get; init; }
}
