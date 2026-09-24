using System.Text.Json;
using Oblodai.Contract;
using Oblodai.Tests.Support;
using Oblodai.Resources;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>Envelope decoding, error classification, retry rules and URL building — the plumbing rules.</summary>
public class EnvelopeTests
{
    [Fact]
    public void DecodesASuccessEnvelope()
    {
        var decoded = EnvelopeDecoder.Decode(200, """{"state":0,"result":{"uuid":"u1"}}""");
        Assert.True(decoded.Ok);
        Assert.Equal("u1", decoded.Result.GetProperty("uuid").GetString());
    }

    [Fact]
    public void RejectsABodyThatIsNotAnEnvelope()
    {
        Assert.Throws<ContractException>(() => EnvelopeDecoder.Decode(200, """{"uuid":"u1"}"""));
        Assert.Throws<ContractException>(() => EnvelopeDecoder.Decode(200, "not json"));
    }

    [Theory]
    [InlineData(400, typeof(ValidationException))]
    [InlineData(401, typeof(AuthenticationException))]
    [InlineData(403, typeof(PermissionException))]
    [InlineData(404, typeof(NotFoundException))]
    [InlineData(409, typeof(ConflictException))]
    [InlineData(429, typeof(RateLimitException))]
    [InlineData(503, typeof(UnavailableException))]
    [InlineData(500, typeof(InternalException))]
    public void ClassifiesTheEnvelopeByStatus(int status, Type expected)
    {
        var decoded = EnvelopeDecoder.Decode(status, """{"error":{"code":"x.y","message":"m","retryable":false,"request_id":"rq-1"}}""");
        Assert.False(decoded.Ok);
        Assert.IsType(expected, decoded.Error);
        Assert.Equal("rq-1", decoded.Error!.RequestId);
        Assert.Equal("x", decoded.Error.Family);
        Assert.False(decoded.Error.Synthetic);
    }

    [Fact]
    public void IdempotencyKeyReuseIsItsOwnConflict()
    {
        var decoded = EnvelopeDecoder.Decode(409, """{"error":{"code":"idempotency.key_reused","retryable":false}}""");
        Assert.IsType<IdempotencyConflictException>(decoded.Error);
    }

    [Fact]
    public void TheEnvelopeRetryableFlagIsAuthoritative()
    {
        var retryable = EnvelopeDecoder.Decode(400, """{"error":{"code":"x.y","retryable":true}}""");
        Assert.True(retryable.Error!.Retryable);

        var notRetryable = EnvelopeDecoder.Decode(503, """{"error":{"code":"x.y","retryable":false}}""");
        Assert.False(notRetryable.Error!.Retryable);
    }

    [Fact]
    public void AnAnswerWithoutAnEnvelopeIsSynthetic()
    {
        var decoded = EnvelopeDecoder.Decode(502, "<html>bad gateway</html>");
        Assert.False(decoded.Ok);
        Assert.True(decoded.Error!.Synthetic);
        Assert.True(decoded.Error.Retryable);
        Assert.Contains("proxy or load balancer", decoded.Error.Message);

        var notTransient = EnvelopeDecoder.Decode(418, "<html>teapot</html>");
        Assert.True(notTransient.Error!.Synthetic);
        Assert.False(notTransient.Error.Retryable);
    }

    [Fact]
    public void NamesTheRedirectTargetInsteadOfABareEnvelopeError()
    {
        var decoded = EnvelopeDecoder.Decode(301, string.Empty, locationHeader: "https://www.api.test/v1/balance");
        Assert.Contains("redirect", decoded.Error!.Message);
        Assert.Contains("www.api.test", decoded.Error.Message);
        Assert.Equal(301, decoded.Error.HttpStatus);
    }

    [Fact]
    public void ReadsRetryAfterAsSecondsOrAsAnHttpDate()
    {
        Assert.Equal(120, EnvelopeDecoder.ParseRetryAfter("120"));
        Assert.Null(EnvelopeDecoder.ParseRetryAfter(null));
        Assert.Null(EnvelopeDecoder.ParseRetryAfter("soon"));

        var now = DateTimeOffset.UtcNow;
        Assert.Equal(60, EnvelopeDecoder.ParseRetryAfter(now.AddSeconds(60).ToString("r"), now));
        Assert.Equal(0, EnvelopeDecoder.ParseRetryAfter(now.AddSeconds(-60).ToString("r"), now));
    }

    [Fact]
    public void RetryAfterFromTheHeaderIsUsedWhenTheBodyHasNone()
    {
        var decoded = EnvelopeDecoder.Decode(429, """{"error":{"code":"request.rate_limited","retryable":true}}""", "30");
        Assert.Equal(30, decoded.Error!.RetryAfter);
    }

    [Fact]
    public void RetriesOnlyWhatIsRetryableAndSafeToRepeat()
    {
        var options = RetryOptions.Default;
        var transport = new TransportException(SdkErrorCodes.TransportNetwork, "boom");
        var synthetic = EnvelopeDecoder.Decode(503, "<html/>").Error!;
        var enveloped = EnvelopeDecoder.Decode(503, """{"error":{"code":"db.unavailable","retryable":true}}""").Error!;
        var refused = EnvelopeDecoder.Decode(400, """{"error":{"code":"x.y","retryable":false}}""").Error!;

        Assert.True(RetryPolicy.ShouldRetry(transport, 0, safeToRepeat: true, options));
        Assert.False(RetryPolicy.ShouldRetry(transport, 0, safeToRepeat: false, options));
        Assert.True(RetryPolicy.ShouldRetry(synthetic, 0, safeToRepeat: true, options));
        Assert.False(RetryPolicy.ShouldRetry(synthetic, 0, safeToRepeat: false, options));

        // The gateway answered, so it did not perform the operation: retryable even on an unsafe write.
        Assert.True(RetryPolicy.ShouldRetry(enveloped, 0, safeToRepeat: false, options));
        Assert.False(RetryPolicy.ShouldRetry(refused, 0, safeToRepeat: true, options));
        Assert.False(RetryPolicy.ShouldRetry(enveloped, options.MaxRetries, safeToRepeat: true, options));
        Assert.False(RetryPolicy.ShouldRetry(new InvalidOperationException("not ours"), 0, true, options));
    }

    [Fact]
    public void RetryAfterWinsOverBackoffAndIsCapped()
    {
        var options = RetryOptions.Default;
        var rateLimited = EnvelopeDecoder.Decode(
            429, """{"error":{"code":"request.rate_limited","retryable":true,"retry_after":5}}""").Error!;
        Assert.Equal(5000, RetryPolicy.DelayMs(rateLimited, 0, options));

        var far = EnvelopeDecoder.Decode(
            429, """{"error":{"code":"request.rate_limited","retryable":true,"retry_after":600}}""").Error!;
        Assert.Equal(options.MaxRetryAfterMs, RetryPolicy.DelayMs(far, 0, options));

        var plain = EnvelopeDecoder.Decode(503, """{"error":{"code":"db.unavailable","retryable":true}}""").Error!;
        Assert.Equal(250, RetryPolicy.DelayMs(plain, 0, options, () => 1.0));
        Assert.Equal(62, RetryPolicy.DelayMs(plain, 0, options, () => 0.0)); // jitter floor: exp / 4
        Assert.Equal(500, RetryPolicy.DelayMs(plain, 1, options, () => 1.0));
        Assert.Equal(options.MaxDelayMs, RetryPolicy.DelayMs(plain, 10, options, () => 1.0));
    }

    [Fact]
    public void ErrorsSerializeWithoutTheirRawBody()
    {
        var error = EnvelopeDecoder.Decode(
            400, """{"error":{"code":"payment.below_minimum","message":"too small","field":"amount","retryable":false}}""").Error!;

        var json = JsonDocument.Parse(error.ToJson()).RootElement;
        Assert.Equal("payment.below_minimum", json.GetProperty("code").GetString());
        Assert.Equal("too small", json.GetProperty("message").GetString());
        Assert.Equal("amount", json.GetProperty("field").GetString());
        Assert.False(json.TryGetProperty("raw", out _));

        // The raw body is kept for debugging but never reaches a log line or a serializer.
        Assert.NotNull(error.Raw);
        Assert.DoesNotContain("\"retryable\"", error.ToString());
        Assert.DoesNotContain("raw", error.ToLogRecord().Keys);
    }

    [Fact]
    public void BuildsUrlsKeepingABasePathPrefixAndSigningOverTheWholePath()
    {
        var request = RequestBuilder.Build(new BuildInput
        {
            BaseUrl = "https://gw.corp/oblodai",
            Route = Routes.GetBalance,
            Credentials = new Credentials("pk", "s"),
            Ts = 1_755_600_000,
            UserAgent = "test",
            Body = "{}",
        });

        Assert.Equal("https://gw.corp/oblodai/v1/balance", request.Url);
        Assert.Equal("/oblodai/v1/balance", request.RequestUri);
        Assert.Equal(
            RequestSigner.Sign("s", 1_755_600_000, "POST", "/oblodai/v1/balance", null, "{}"),
            request.Headers[RequestSigner.HeaderSignature]);
    }

    [Fact]
    public void SignsPathPlusQueryAndSendsNoBodyOnGet()
    {
        var request = RequestBuilder.Build(new BuildInput
        {
            BaseUrl = "https://api.test",
            Route = Routes.SandboxListWebhooks,
            Credentials = new Credentials("pk", "s"),
            Query = [new KeyValuePair<string, string?>("limit", "10"), new KeyValuePair<string, string?>("offset", "0")],
            Ts = 1_755_600_000,
            UserAgent = "test",
        });

        Assert.Equal("https://api.test/v1/sandbox/webhooks?limit=10&offset=0", request.Url);
        Assert.Null(request.Body);
        Assert.Equal(
            RequestSigner.Sign("s", 1_755_600_000, "GET", "/v1/sandbox/webhooks?limit=10&offset=0", null, string.Empty),
            request.Headers[RequestSigner.HeaderSignature]);
    }

    [Fact]
    public void DropsCallerHeadersThatCollideWithSignedOnes()
    {
        var request = RequestBuilder.Build(new BuildInput
        {
            BaseUrl = "https://api.test",
            Route = Routes.GetBalance,
            Credentials = new Credentials("pk", "s"),
            Ts = 1,
            UserAgent = "test",
            Body = "{}",
            ExtraHeaders = new Dictionary<string, string> { ["X-Signature"] = "zz", ["X-Trace"] = "t1" },
        });

        Assert.Matches("^[0-9a-f]{64}$", request.Headers[RequestSigner.HeaderSignature]);
        Assert.Equal("t1", request.Headers["X-Trace"]);
    }

    [Fact]
    public void RefusesPathParametersThatWouldRewriteTheUrl()
    {
        foreach (var bad in new[] { "..", ".", "a/b", string.Empty })
        {
            var error = Assert.Throws<ConfigException>(() => RequestBuilder.FillPath(
                "/v1/pay/{id}", new Dictionary<string, string> { ["id"] = bad }));
            Assert.Equal(SdkErrorCodes.BadPathParam, error.Code);
        }

        Assert.Equal("/v1/pay/a%20b", RequestBuilder.FillPath("/v1/pay/{id}", new Dictionary<string, string> { ["id"] = "a b" }));
        Assert.Throws<ConfigException>(() => RequestBuilder.FillPath("/v1/pay/{id}", null));
    }

    [Fact]
    public void RefusesToSignWithoutCredentials()
    {
        var error = Assert.Throws<ConfigException>(() => RequestBuilder.Build(new BuildInput
        {
            BaseUrl = "https://api.test",
            Route = Routes.GetBalance,
            Ts = 1,
            UserAgent = "test",
            Body = "{}",
        }));

        Assert.Equal(SdkErrorCodes.MissingCredentials, error.Code);
    }

    [Fact]
    public void PublicAndOnboardRoutesAreNotSigned()
    {
        foreach (var route in new[] { Routes.ListCurrencies, Routes.OnboardSandboxStore })
        {
            var request = RequestBuilder.Build(new BuildInput
            {
                BaseUrl = "https://api.test",
                Route = route,
                PathParams = new Dictionary<string, string> { ["id"] = "m1" },
                Ts = 1,
                UserAgent = "test",
                Body = "{}",
            });
            Assert.False(request.Headers.ContainsKey(RequestSigner.HeaderSignature));
        }
    }

    [Fact]
    public void TheClockCorrectsAndRevertsSkew()
    {
        var fake = new FixedClock(1_000_000);
        var clock = new SkewCorrectingClock(fake);
        Assert.Equal(1_000_000, clock.NowUnixSeconds());

        var observed = clock.ObserveServerDate(DateTimeOffset.FromUnixTimeSeconds(1_003_600));
        Assert.Equal(3600, observed);
        clock.Correct(observed!.Value);
        Assert.Equal(1_003_600, clock.NowUnixSeconds());

        clock.Correct(0);
        Assert.Equal(1_000_000, clock.NowUnixSeconds());

        // A broken proxy Date is ignored rather than wedging the client.
        Assert.Null(clock.ObserveServerDate(DateTimeOffset.FromUnixTimeSeconds(1_000_000 + (48 * 3600))));
        Assert.Null(clock.ObserveServerDate("not a date"));
        Assert.Null(clock.ObserveServerDate((string?)null));
    }

    private sealed class FixedClock(long now) : IClock
    {
        public long NowUnixSeconds() => now;
    }
}
