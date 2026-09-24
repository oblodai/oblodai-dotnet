using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Oblodai.Contract;

namespace Oblodai;

/// <summary>
/// The HTTP engine every resource goes through. One method, <see cref="CallAsync{T}"/>, does the whole
/// lifecycle: serialize → sign → send (with timeout) → decode envelope → classify error → retry per
/// policy. <see cref="CallRawAsync"/> serves the few bare routes that return bytes instead of JSON.
/// </summary>
public sealed partial class OblodaiTransport : IDisposable
{
    /// <summary>
    /// How much of a JSON answer the SDK will buffer. Every enveloped route answers in kilobytes; a body
    /// past this is a proxy error page, a misrouted download or a compression bomb, and reading it to the
    /// end would trade a failed call for an exhausted process.
    /// </summary>
    public const long MaxJsonResponseBytes = 8L * 1024 * 1024;

    /// <summary>How much of a bare (PDF/CSV) answer the SDK will buffer.</summary>
    public const long MaxBareResponseBytes = 64L * 1024 * 1024;

    /// <summary>Error codes that mean the gateway rejected the signature because of the timestamp or MAC.</summary>
    private static readonly HashSet<string> SignatureFailureCodes = ["merchant.bad_signature", "auth.bad_timestamp"];

    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,

            // Without this a pooled connection outlives DNS: a gateway that fails over to a new address
            // keeps receiving requests on the old one until the socket happens to close.
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _clock = options.Clock ?? new SkewCorrectingClock();
        _logger = options.Logger ?? NoopLogger.Instance;

        // A finite HttpClient.Timeout silently caps every per-attempt timeout and every deadline this SDK
        // computes, and reports the cancellation as the SDK's own timeout with the SDK's own (larger)
        // number. Say so once, with both values, rather than let a call "time out after 30000 ms" in 5 s.
        if (!_ownsHttpClient && _http.Timeout != Timeout.InfiniteTimeSpan)
        {
            _logger.Log(
                OblodaiLogLevel.Warn,
                "the injected HttpClient has a finite Timeout; it overrides the SDK's own timeouts",
                new Dictionary<string, object?>
                {
                    ["httpClientTimeoutMs"] = (long)_http.Timeout.TotalMilliseconds,
                    ["sdkTimeoutMs"] = (long)options.Timeout.TotalMilliseconds,
                    ["sdkDeadlineMs"] = (long)options.Deadline.TotalMilliseconds,
                    ["fix"] = "set HttpClient.Timeout = Timeout.InfiniteTimeSpan and use Timeout/Deadline",
                });
        }
    }

    /// <summary>The signing clock, exposed for tests and diagnostics.</summary>
    public SkewCorrectingClock Clock => _clock;

    /// <summary>The wiring this transport was built with.</summary>
    public TransportOptions Options => _options;

    /// <summary>The HTTP client requests go through.</summary>
    internal HttpClient HttpClient => _http;

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

    /// <summary>Per-call headers over client headers; either side may be absent.</summary>
    /// <param name="client">Headers configured on the client.</param>
    /// <param name="call">Headers passed to this call.</param>
    private static IReadOnlyDictionary<string, string>? MergeHeaders(
        IReadOnlyDictionary<string, string>? client,
        IReadOnlyDictionary<string, string>? call)
    {
        if (call is null or { Count: 0 })
        {
            return client;
        }

        if (client is null or { Count: 0 })
        {
            return call;
        }

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in client)
        {
            merged[key] = value;
        }

        foreach (var (key, value) in call)
        {
            merged[key] = value;
        }

        return merged;
    }

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

        var requestId = options.RequestId ?? Guid.NewGuid().ToString();
        RequestBuilder.AssertHeaderIsSendable(RequestBuilder.HeaderRequestId, requestId);
        var retry = RetryFor(options);
        var safeToRepeat = route.Safe || (route.Idempotent && idempotencyKey is not null);
        var time = _options.TimeProvider;
        var deadline = time.GetUtcNow() + _options.Deadline;
        var label = $"{route.Method} {route.Path}";

        var attempt = 0;
        var skewTried = false;
        long offsetBeforeCorrection = 0;
        long offsetThisCallInstalled = 0;

        while (true)
        {
            // The offset this attempt is signed with, remembered per call. Comparing the server's time
            // against the SHARED offset instead would misread a correction another thread just installed
            // as this call's own, and re-sign against a clock nobody measured for this request.
            var signedWithOffset = _clock.Offset;
            var request = RequestBuilder.Build(new BuildInput
            {
                BaseUrl = _options.BaseUrl,
                Route = route,
                PathParams = options.PathParams,
                Query = options.Query,
                Body = body,
                Credentials = _options.Credentials,
                IdempotencyKey = idempotencyKey,
                RequestId = requestId,
                Ts = _clock.BaseNowUnixSeconds() + signedWithOffset,
                UserAgent = _options.UserAgent,
                ExtraHeaders = MergeHeaders(_options.Headers, options.ExtraHeaders),
                AdminToken = _options.AdminToken,
            });

            _logger.Log(OblodaiLogLevel.Debug, "request", new Dictionary<string, object?>
            {
                ["route"] = label,
                ["attempt"] = attempt,
                ["idempotencyKey"] = idempotencyKey,
                ["requestId"] = requestId,
            });

            var info = new RequestInfo(
                request.Method, request.Url, RedactHeaders(request.Headers), attempt + 1, requestId, route.OperationId);
            _options.Hooks?.OnRequest?.Invoke(info);
            var started = time.GetTimestamp();

            RawResponse raw;
            HttpResponseHeaders? responseHeaders;
            try
            {
                (raw, responseHeaders) = await SendAsync(route, request, options, deadline, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OblodaiException err)
            {
                _options.Hooks?.OnResponse?.Invoke(new ResponseInfo(
                    info, 0, EmptyHeaders, time.GetElapsedTime(started), err));
                if (RetryPolicy.ShouldRetry(err, attempt, safeToRepeat, retry))
                {
                    await PauseAsync(err, attempt, retry, deadline, cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw;
            }

            raw = raw with
            {
                RequestId = raw.Headers.TryGetValue(RequestBuilder.HeaderRequestId, out var echoed)
                            && !string.IsNullOrEmpty(echoed) ? echoed : requestId,
            };

            if (raw.Status is >= 200 and < 300)
            {
                _options.Hooks?.OnResponse?.Invoke(new ResponseInfo(
                    info, raw.Status, raw.Headers, time.GetElapsedTime(started), null));
                RawCapture.Record(raw);
                return raw;
            }

            var failure = Classify(route, raw, responseHeaders);
            _options.Hooks?.OnResponse?.Invoke(new ResponseInfo(
                info, raw.Status, raw.Headers, time.GetElapsedTime(started), failure));
            _logger.Log(OblodaiLogLevel.Debug, "response", LogRedaction.Redact(new Dictionary<string, object?>
            {
                ["route"] = label,
                ["status"] = raw.Status,
                ["code"] = failure.Code,
                ["requestId"] = failure.RequestId ?? raw.RequestId,
            }));

            // Clock skew: the gateway rejected the timestamp/MAC. Learn its time from the `Date` header,
            // re-sign once, and keep the offset only if that attempt got past authentication.
            if (raw.Status == 401 && SignatureFailureCodes.Contains(failure.Code))
            {
                if (!skewTried)
                {
                    var observed = _clock.ObserveServerDate(responseHeaders?.Date);
                    if (observed is { } offset
                        && Math.Abs(offset - signedWithOffset) > RequestSigner.SignatureSkewSeconds / 2)
                    {
                        _logger.Log(OblodaiLogLevel.Warn, "clock skew detected; re-signing with server time",
                            new Dictionary<string, object?> { ["route"] = label, ["offsetSec"] = offset });
                        skewTried = true;
                        offsetBeforeCorrection = signedWithOffset;
                        offsetThisCallInstalled = offset;
                        _clock.Correct(offset);
                        continue;
                    }
                }
                else
                {
                    // It was not skew. Undo the correction only while it is still the one this call made:
                    // between the two attempts another thread may have measured a real offset, and
                    // reverting that would put every concurrent call back on the wrong clock.
                    _clock.CorrectIfUnchanged(offsetThisCallInstalled, offsetBeforeCorrection);
                }
            }

            if (RetryPolicy.ShouldRetry(failure, attempt, safeToRepeat, retry))
            {
                await PauseAsync(failure, attempt, retry, deadline, cancellationToken).ConfigureAwait(false);
                attempt++;
                continue;
            }

            throw failure;
        }
    }

    private RetryOptions RetryFor(CallOptions options)
    {
        if (options.MaxRetries is not { } max)
        {
            return _options.Retry;
        }

        if (max < 0)
        {
            throw new ConfigException(SdkErrorCodes.BadConfig, $"MaxRetries must not be negative (got {max})", "MaxRetries");
        }

        return _options.Retry with { MaxRetries = max };
    }

    /// <summary>The headers of an attempt as hooks see them: the signature and the admin token redacted.</summary>
    /// <param name="headers">Headers as sent.</param>
    private static IReadOnlyDictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in headers)
        {
            output[key] = key.Equals(RequestSigner.HeaderSignature, StringComparison.OrdinalIgnoreCase)
                          || key.Equals(RequestSigner.HeaderAdminToken, StringComparison.OrdinalIgnoreCase)
                ? Redaction.Placeholder
                : value;
        }

        return output;
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
        catch (Exception err)
        {
            // The decoder is written not to throw anything else, but classifying a failure must never be
            // the thing that fails: a body nobody anticipated still has to come back as its HTTP status.
            return new ContractException(
                $"{route.Method} {route.Path}: HTTP {raw.Status} could not be read as an envelope ({err.GetType().Name})",
                raw.Status,
                text);
        }

        return new ContractException(
            $"{route.Method} {route.Path}: HTTP {raw.Status} with a success envelope", raw.Status, text);
    }
}
