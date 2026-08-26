using Oblodai.Contract;
using Oblodai.Resources;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// The transport lifecycle end to end through the client: signing, idempotency, retries, clock skew,
/// timeouts and credentials. Every case here is a rule the reference SDK is verified against.
/// </summary>
public class TransportTests
{
    private const string Address = "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx";

    private static OblodaiClient Client(FakeHttpHandler handler, OblodaiOptions? options = null)
        => new(
            (options ?? new OblodaiOptions()) with
            {
                PublicId = options?.PublicId ?? "pk_test_1",
                Secret = options?.Secret ?? "secret-1",
                BaseUrl = options?.BaseUrl ?? "https://api.test",
                Retry = options?.Retry ?? new RetryOptions { BaseDelayMs = 1, MaxDelayMs = 2 },
            },
            handler.Client());

    private static ScriptedResponse Retryable(int status, string code, int? retryAfter = null)
        => ScriptedResponse.Error(
            status,
            $$"""{"code":"{{code}}","message":"try later","retryable":true{{(retryAfter is null ? "" : $",\"retry_after\":{retryAfter}")}}}""");

    [Fact]
    public async Task SignsPathPlusQueryOnGetAndSendsNoBody()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Page("[]", 0, 0, 10, false));
        using var client = Client(handler);

        await client.Sandbox.WebhooksAsync(new PageParams { Limit = 10, Offset = 0 });

        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://api.test/v1/sandbox/webhooks?limit=10&offset=0", call.Url);
        Assert.Null(call.Body);
        Assert.Equal("pk_test_1", call.Header(RequestSigner.HeaderPublicId));
        Assert.Matches("^[0-9a-f]{64}$", call.Header(RequestSigner.HeaderSignature));
        Assert.Matches("^[0-9]+$", call.Header(RequestSigner.HeaderTimestamp));
    }

    [Fact]
    public async Task GeneratesOneIdempotencyKeyPerCreateCallAndReusesItAcrossRetries()
    {
        var handler = new FakeHttpHandler(
            Retryable(503, "db.unavailable"),
            ScriptedResponse.Ok("""{"uuid":"u"}"""));
        using var client = Client(handler);

        await client.Payments.CreateAsync(new PaymentRequest { Amount = "1", Currency = "USDT" });

        Assert.Equal(2, handler.Calls.Count);
        var key = handler.Calls[0].Header(RequestSigner.HeaderIdempotencyKey);
        Assert.Matches("^[0-9a-f-]{36}$", key);
        Assert.Equal(key, handler.Calls[1].Header(RequestSigner.HeaderIdempotencyKey));
        Assert.Matches("^[0-9a-f]{64}$", handler.Calls[1].Header(RequestSigner.HeaderSignature));
    }

    [Fact]
    public async Task HonoursACallerKeyAndAddsNoneToReadRoutes()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok("""{"uuid":"u"}"""), ScriptedResponse.Ok("""{"uuid":"u"}"""));
        using var client = Client(handler);

        await client.Payouts.CreateAsync(
            new PayoutRequest { Amount = "1", Currency = "USDT", Address = Address, OrderId = "o" },
            new RequestOptions { IdempotencyKey = "my-key-1" });
        await client.Payments.InfoAsync("u");

        Assert.Equal("my-key-1", handler.Calls[0].Header(RequestSigner.HeaderIdempotencyKey));
        Assert.False(handler.Calls[1].HasHeader(RequestSigner.HeaderIdempotencyKey));
    }

    [Fact]
    public async Task RejectsACallerKeyOnARouteTheGatewayDoesNotDeduplicate()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok());
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<ConfigException>(
            () => client.Payouts.ApproveAsync("p1", new RequestOptions { IdempotencyKey = "k1" }));

        Assert.Equal(SdkErrorCodes.IdempotencyUnsupported, error.Code);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task DoesNotRetryANonRetryableErrorEvenOnA5xx()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Error(500, """{"code":"internal","message":"boom","retryable":false}"""));
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<InternalException>(() => client.Account.BalanceAsync());

        Assert.Equal("internal", error.Code);
        Assert.Equal(500, error.HttpStatus);
        Assert.False(error.Retryable);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task RetriesARetryableErrorUntilTheBudgetIsSpent()
    {
        var handler = new FakeHttpHandler(
            Retryable(429, "request.rate_limited", 0),
            Retryable(429, "request.rate_limited", 0),
            Retryable(429, "request.rate_limited", 0));
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<RateLimitException>(() => client.Account.BalanceAsync());

        Assert.Equal(0, error.RetryAfter);
        Assert.Equal(3, handler.Calls.Count); // the first attempt plus MaxRetries (2)
    }

    [Fact]
    public async Task RetriesATransportFailureOnlyWhenTheRequestIsSafeToRepeat()
    {
        var boom = new HttpRequestException("connection reset");

        var read = new FakeHttpHandler(
            new ScriptedResponse { Throws = boom },
            ScriptedResponse.Ok("""{"balance":{"merchant":[]}}"""));
        using (var client = Client(read))
        {
            await client.Account.BalanceAsync();
        }

        Assert.Equal(2, read.Calls.Count);

        var unsafeWrite = new FakeHttpHandler(new ScriptedResponse { Throws = boom }, ScriptedResponse.Ok());
        using (var client = Client(unsafeWrite))
        {
            var error = await Assert.ThrowsAsync<TransportException>(
                () => client.Settings.SetAccuracyAsync(new PaymentAccuracySetRequest { Enabled = true }));
            Assert.Equal(SdkErrorCodes.TransportNetwork, error.Code);
        }

        Assert.Single(unsafeWrite.Calls);

        var keyedWrite = new FakeHttpHandler(
            new ScriptedResponse { Throws = boom },
            ScriptedResponse.Ok("""{"uuid":"u"}"""));
        using (var client = Client(keyedWrite))
        {
            await client.Payments.CreateAsync(new PaymentRequest { Amount = "1", Currency = "USDT" });
        }

        Assert.Equal(2, keyedWrite.Calls.Count);
    }

    [Fact]
    public async Task NeverReSendsAnUnsafeWriteAfterAProxyAnswerWithoutAnEnvelope()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Html(503), ScriptedResponse.Ok());
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<UnavailableException>(() => client.Payouts.ApproveAsync("p1"));

        Assert.True(error.Synthetic);
        Assert.True(error.Retryable);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task RetriesAReadRouteAfterAProxy502And504AndHonoursTheRetryAfterHeader()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Html(502),
            ScriptedResponse.Html(504, new Dictionary<string, string> { ["Retry-After"] = "0" }),
            ScriptedResponse.Ok("""{"balance":{"merchant":[]}}"""));
        using var client = Client(handler);

        await client.Account.BalanceAsync();
        Assert.Equal(3, handler.Calls.Count);

        var once = new FakeHttpHandler(ScriptedResponse.Html(429, new Dictionary<string, string> { ["Retry-After"] = "120" }));
        using var strict = Client(once, new OblodaiOptions { Retry = new RetryOptions { MaxRetries = 0 } });

        var error = await Assert.ThrowsAsync<RateLimitException>(() => strict.Account.BalanceAsync());
        Assert.Equal(120, error.RetryAfter);
    }

    [Fact]
    public async Task RetriesAnEnvelopedRetryableErrorOnAnUnsafeWrite()
    {
        // The gateway answered, so it did not perform the operation.
        var handler = new FakeHttpHandler(
            Retryable(409, "payout.funds_maturing", 0),
            ScriptedResponse.Ok("""{"uuid":"p"}"""));
        using var client = Client(handler);

        await client.Payouts.ApproveAsync("p1");

        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task ClassifiesTheEnvelopeAndKeepsRequestIdAndField()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Error(400, """{"code":"payment.below_minimum","message":"too small","field":"amount","retryable":false,"request_id":"rq-1"}"""),
            ScriptedResponse.Error(401, """{"code":"merchant.bad_signature","message":"bad","retryable":false}"""),
            ScriptedResponse.Error(409, """{"code":"idempotency.key_reused","message":"reused","retryable":false}"""));
        using var client = Client(handler, new OblodaiOptions { Retry = new RetryOptions { MaxRetries = 0 } });

        var validation = await Assert.ThrowsAsync<ValidationException>(
            () => client.Payments.CreateAsync(new PaymentRequest { Amount = "0", Currency = "USDT" }));
        Assert.Equal("payment.below_minimum", validation.Code);
        Assert.Equal("amount", validation.Field);
        Assert.Equal("rq-1", validation.RequestId);
        Assert.Equal("payment", validation.Family);

        await Assert.ThrowsAsync<AuthenticationException>(() => client.Account.BalanceAsync());
        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => client.Payments.CreateAsync(new PaymentRequest { Amount = "1", Currency = "USDT" }));
    }

    [Fact]
    public async Task ReSignsOnceWithTheServerClockWhenA401RevealsSkew()
    {
        var serverNow = DateTimeOffset.UtcNow.AddHours(1);
        var handler = new FakeHttpHandler(
            ScriptedResponse.Error(
                401,
                """{"code":"merchant.bad_signature","retryable":false}""",
                new Dictionary<string, string> { ["Date"] = serverNow.ToString("r") }),
            ScriptedResponse.Ok("""{"balance":{"merchant":[]}}"""));
        using var client = Client(handler, new OblodaiOptions { Retry = new RetryOptions { MaxRetries = 0 } });

        await client.Account.BalanceAsync();

        Assert.Equal(2, handler.Calls.Count);
        var stamped = long.Parse(handler.Calls[1].Header(RequestSigner.HeaderTimestamp));
        Assert.True(Math.Abs(stamped - serverNow.ToUnixTimeSeconds()) < 5);
    }

    [Fact]
    public async Task IgnoresTheDateHeaderOnA401ThatIsNotASignatureFailure()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Error(
            401,
            """{"code":"auth.ip_not_allowed","retryable":false}""",
            new Dictionary<string, string> { ["Date"] = DateTimeOffset.UtcNow.AddHours(1).ToString("r") }));
        using var client = Client(handler, new OblodaiOptions { Retry = new RetryOptions { MaxRetries = 0 } });

        var error = await Assert.ThrowsAsync<AuthenticationException>(() => client.Account.BalanceAsync());

        Assert.Equal("auth.ip_not_allowed", error.Code);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task RevertsTheSkewCorrectionWhenTheReSignedAttemptIsStillRejected()
    {
        var far = new Dictionary<string, string> { ["Date"] = DateTimeOffset.UtcNow.AddHours(1).ToString("r") };
        var handler = new FakeHttpHandler(
            ScriptedResponse.Error(401, """{"code":"merchant.bad_signature","retryable":false}""", far),
            ScriptedResponse.Error(401, """{"code":"merchant.bad_signature","retryable":false}""", far),
            ScriptedResponse.Ok("""{"balance":{"merchant":[]}}"""));
        using var client = Client(handler, new OblodaiOptions { Retry = new RetryOptions { MaxRetries = 0 } });

        await Assert.ThrowsAsync<AuthenticationException>(() => client.Account.BalanceAsync());
        await client.Account.BalanceAsync();

        // One bad Date cannot wedge the client: the third attempt is stamped with local time again.
        var stamped = long.Parse(handler.Calls[2].Header(RequestSigner.HeaderTimestamp));
        Assert.True(Math.Abs(stamped - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) < 5);
    }

    [Fact]
    public async Task TimesOutAndReportsTransportTimeout()
    {
        var handler = new FakeHttpHandler(new ScriptedResponse { DelayMs = 2000 });
        using var client = Client(handler, new OblodaiOptions
        {
            TimeoutMs = 20,
            Retry = new RetryOptions { MaxRetries = 0 },
        });

        var error = await Assert.ThrowsAsync<TransportException>(() => client.Account.BalanceAsync());

        Assert.Equal(SdkErrorCodes.TransportTimeout, error.Code);
        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task StopsRetryingWhenTheOverallDeadlineWouldBeExceeded()
    {
        var handler = new FakeHttpHandler(Retryable(503, "db.unavailable", 2), ScriptedResponse.Ok());
        using var client = Client(handler, new OblodaiOptions { DeadlineMs = 100 });

        var error = await Assert.ThrowsAsync<TransportException>(() => client.Account.BalanceAsync());

        Assert.Equal(SdkErrorCodes.TransportDeadline, error.Code);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task CancellingDuringARetryPauseThrowsTheCallersOwnCancellation()
    {
        var handler = new FakeHttpHandler(Retryable(503, "db.unavailable", 1), ScriptedResponse.Ok());
        using var client = Client(handler, new OblodaiOptions { Retry = new RetryOptions { MaxRetryAfterMs = 5000 } });
        using var cancellation = new CancellationTokenSource(20);

        // The caller's cancellation stays an OperationCanceledException: ASP.NET's request-abort handling
        // and every `catch (OperationCanceledException)` in the process depend on seeing it as itself.
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.Account.BalanceAsync(cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.IsNotType<TransportException>(error);
    }
}
