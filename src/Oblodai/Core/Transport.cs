using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

    /// <summary>Sent as <c>X-Admin-Token</c> on merchant-provisioning routes only.</summary>
    public string? AdminToken { get; init; }
}

/// <summary>
/// The HTTP engine every resource goes through. One method, <see cref="CallAsync{T}"/>, does the whole
/// lifecycle: serialize → sign → send (with timeout) → decode envelope → classify error → retry per
/// policy. <see cref="CallRawAsync"/> serves the few bare routes that return bytes instead of JSON.
/// </summary>
public sealed class OblodaiTransport : IDisposable
{
    /// <summary>Error codes that mean the gateway rejected the signature because of the timestamp or MAC.</summary>
    private static readonly HashSet<string> SignatureFailureCodes = ["merchant.bad_signature", "auth.bad_timestamp"];

    private readonly TransportOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly SkewCorrectingClock _clock;
    private readonly IOblodaiLogger _logger;

    /// <summary>Create a transport.</summary>
    /// <param name="options">Wiring.</param>
    /// <param name="httpClient">
    /// An externally managed client (from <c>IHttpClientFactory</c>). When omitted the transport owns
    /// its own client, configured not to follow redirects and with per-attempt timeouts of its own.
    /// </param>
    public OblodaiTransport(TransportOptions options, HttpClient? httpClient = null)
    {
        _options = options;
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _clock = options.Clock ?? new SkewCorrectingClock();
        _logger = options.Logger ?? NoopLogger.Instance;
    }

    /// <summary>The signing clock, exposed for tests and diagnostics.</summary>
    public SkewCorrectingClock Clock => _clock;

    /// <summary>Call an envelope route and decode its <c>result</c>.</summary>
    /// <typeparam name="T">Model of the <c>result</c> payload.</typeparam>
    /// <param name="route">The route.</param>
    /// <param name="options">Per-call knobs.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<T> CallAsync<T>(RouteSpec route, CallOptions? options = null, CancellationToken cancellationToken = default)
    {
        var element = await CallElementAsync(route, options, cancellationToken).ConfigureAwait(false);
        return OblodaiJson.Deserialize<T>(element);
    }

    /// <summary>Call an envelope route and return its <c>result</c> as raw JSON.</summary>
    /// <param name="route">The route.</param>
    /// <param name="options">Per-call knobs.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<JsonElement> CallElementAsync(RouteSpec route, CallOptions? options = null, CancellationToken cancellationToken = default)
    {
        var raw = await ExecuteAsync(route, options ?? new CallOptions(), cancellationToken).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(raw.Body);
        var decoded = EnvelopeDecoder.Decode(raw.Status, text);
        if (!decoded.Ok)
        {
            throw decoded.Error!; // unreachable: ExecuteAsync already threw for error statuses
        }

        // The gateway replays a cached response by Idempotency-Key; when the original was too large to
        // cache it answers { ok, idempotent_replay: true, detail } instead of the object — surface that.
        if (decoded.Result.ValueKind == JsonValueKind.Object
            && decoded.Result.TryGetProperty("idempotent_replay", out var replay)
            && replay.ValueKind == JsonValueKind.True)
        {
            var detail = decoded.Result.TryGetProperty("detail", out var d) ? d.ToString() : string.Empty;
            throw new ContractException(
                $"{route.Method} {route.Path}: the request was already processed but its response was too large to "
                + $"replay — fetch the result by order_id/reference ({detail})",
                raw.Status,
                text);
        }

        return decoded.Result;
    }

    /// <summary>Call a bare route and return the response bytes (the status is already 2xx).</summary>
    /// <param name="route">The route.</param>
    /// <param name="options">Per-call knobs.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<RawResponse> CallRawAsync(RouteSpec route, CallOptions? options = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(route, options ?? new CallOptions(), cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>Which key pair signs a route. <c>any</c> routes take the payment key unless told otherwise.</summary>
    private Credentials? CredentialsFor(RouteSpec route, bool preferPayout)
        => route.Auth == RouteAuth.Payout || (route.Auth == RouteAuth.Any && preferPayout)
            ? _options.PayoutCredentials ?? _options.Credentials
            : _options.Credentials;

    private async Task<RawResponse> ExecuteAsync(RouteSpec route, CallOptions options, CancellationToken cancellationToken)
    {
        var body = OblodaiJson.SerializeBody(options.Body, route.Method);
        var idempotencyKey = options.IdempotencyKey;
        if (idempotencyKey is not null)
        {
            Idempotency.AssertValid(idempotencyKey);
            if (!route.Idempotent)
            {
                // The gateway ignores the header here, so a key would only make the SDK believe a re-send is
                // deduplicated when it is not — the one belief that turns a lost response into a double spend.
                throw new ConfigException(
                    SdkErrorCodes.IdempotencyUnsupported,
                    $"{route.Method} {route.Path} does not deduplicate by Idempotency-Key; drop IdempotencyKey from this call",
                    "IdempotencyKey");
            }
        }
        else if (route.Idempotent)
        {
            idempotencyKey = Idempotency.NewKey();
        }

        var safeToRepeat = route.Safe || (route.Idempotent && idempotencyKey is not null);
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(options.DeadlineMs ?? _options.DeadlineMs);
        var label = $"{route.Method} {route.Path}";

        var attempt = 0;
        var skewTried = false;
        long skewBefore = 0;

        while (true)
        {
            var extraHeaders = _options.Headers;
            if (route.Auth == RouteAuth.Onboard && !string.IsNullOrEmpty(_options.AdminToken))
            {
                var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in _options.Headers ?? new Dictionary<string, string>())
                {
                    merged[k] = v;
                }

                merged[RequestSigner.HeaderAdminToken] = _options.AdminToken!;
                extraHeaders = merged;
            }

            var request = RequestBuilder.Build(new BuildInput
            {
                BaseUrl = _options.BaseUrl,
                Route = route,
                PathParams = options.PathParams,
                Query = options.Query,
                Body = body,
                Credentials = CredentialsFor(route, options.PreferPayoutKey),
                IdempotencyKey = idempotencyKey,
                Ts = _clock.NowUnixSeconds(),
                UserAgent = _options.UserAgent,
                ExtraHeaders = extraHeaders,
            });

            _logger.Log(OblodaiLogLevel.Debug, "request", new Dictionary<string, object?>
            {
                ["route"] = label,
                ["attempt"] = attempt,
                ["idempotencyKey"] = idempotencyKey,
            });

            RawResponse raw;
            HttpResponseHeaders? responseHeaders;
            try
            {
                (raw, responseHeaders) = await SendAsync(request, options, deadline, cancellationToken).ConfigureAwait(false);
            }
            catch (OblodaiException err)
            {
                if (RetryPolicy.ShouldRetry(err, attempt, safeToRepeat, _options.Retry))
                {
                    await PauseAsync(err, attempt, deadline, cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw;
            }

            if (raw.Status is >= 200 and < 300)
            {
                return raw;
            }

            var failure = Classify(route, raw, responseHeaders);
            _logger.Log(OblodaiLogLevel.Debug, "response", LogRedaction.Redact(new Dictionary<string, object?>
            {
                ["route"] = label,
                ["status"] = raw.Status,
                ["code"] = failure.Code,
                ["requestId"] = failure.RequestId,
            }));

            // Clock skew: the gateway rejected the timestamp/MAC. Learn its time from the `Date` header,
            // re-sign once, and keep the offset only if that attempt got past authentication.
            if (raw.Status == 401 && SignatureFailureCodes.Contains(failure.Code))
            {
                if (!skewTried)
                {
                    var observed = _clock.ObserveServerDate(responseHeaders?.Date);
                    if (observed is { } offset && Math.Abs(offset - _clock.Offset) > RequestSigner.SignatureSkewSeconds / 2)
                    {
                        _logger.Log(OblodaiLogLevel.Warn, "clock skew detected; re-signing with server time",
                            new Dictionary<string, object?> { ["route"] = label, ["offsetSec"] = offset });
                        skewTried = true;
                        skewBefore = _clock.Offset;
                        _clock.Correct(offset);
                        continue;
                    }
                }
                else
                {
                    _clock.Correct(skewBefore); // the corrected timestamp did not help: it was not skew
                }
            }

            if (RetryPolicy.ShouldRetry(failure, attempt, safeToRepeat, _options.Retry))
            {
                await PauseAsync(failure, attempt, deadline, cancellationToken).ConfigureAwait(false);
                attempt++;
                continue;
            }

            throw failure;
        }
    }

    private static OblodaiException Classify(RouteSpec route, RawResponse raw, HttpResponseHeaders? headers)
    {
        var text = Encoding.UTF8.GetString(raw.Body);
        try
        {
            var decoded = EnvelopeDecoder.Decode(
                raw.Status,
                text,
                headers?.RetryAfter?.ToString(),
                headers?.Location?.ToString());
            if (!decoded.Ok)
            {
                return decoded.Error!;
            }
        }
        catch (OblodaiException err)
        {
            return err;
        }

        return new ContractException(
            $"{route.Method} {route.Path}: HTTP {raw.Status} with a success envelope", raw.Status, text);
    }

    private async Task PauseAsync(OblodaiException error, int attempt, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var ms = RetryPolicy.DelayMs(error, attempt, _options.Retry);
        if (DateTimeOffset.UtcNow.AddMilliseconds(ms) > deadline)
        {
            throw new TransportException(
                SdkErrorCodes.TransportDeadline,
                $"retry would exceed the call deadline; last error: {error.Message}",
                error);
        }

        if (ms <= 0)
        {
            return;
        }

        try
        {
            await Task.Delay(ms, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            throw new TransportException(
                SdkErrorCodes.TransportAborted, "request cancelled by the caller during a retry pause", ex);
        }
    }

    private async Task<(RawResponse Raw, HttpResponseHeaders Headers)> SendAsync(
        BuiltRequest request,
        CallOptions options,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var budget = (int)Math.Max(1, (deadline - DateTimeOffset.UtcNow).TotalMilliseconds);
        var timeoutMs = Math.Min(options.TimeoutMs ?? _options.TimeoutMs, budget);

        using var timeoutSource = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        if (request.Body is not null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }

        foreach (var (key, value) in request.Headers)
        {
            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue; // set on the content above
            }

            message.Headers.TryAddWithoutValidation(key, value);
        }

        try
        {
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, linked.Token)
                .ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
            var raw = new RawResponse(
                (int)response.StatusCode,
                bytes,
                response.Content.Headers.ContentType?.ToString(),
                response.Content.Headers.ContentDisposition?.ToString());
            return (raw, response.Headers);
        }
        catch (OperationCanceledException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new TransportException(SdkErrorCodes.TransportAborted, "request cancelled by the caller", ex);
            }

            throw new TransportException(SdkErrorCodes.TransportTimeout, $"request timed out after {timeoutMs} ms", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TransportException(SdkErrorCodes.TransportNetwork, $"network error: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new TransportException(SdkErrorCodes.TransportNetwork, $"network error: {ex.Message}", ex);
        }
    }
}
