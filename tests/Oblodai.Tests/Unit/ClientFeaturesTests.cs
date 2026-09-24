using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// What a caller sees around a call: the error line, the raw answer, a client with other options,
/// hooks on every attempt, waiters for long-running operations.
/// </summary>
public class ClientFeaturesTests
{
    private static OblodaiClient Client(FakeHttpHandler handler, OblodaiOptions? options = null)
        => new(
            (options ?? new OblodaiOptions()) with
            {
                PublicId = "pk",
                Secret = "s",
                BaseUrl = "https://api.test",
                Retry = options?.Retry ?? new RetryOptions { BaseDelayMs = 0, MaxDelayMs = 0 },
            },
            handler.Client());

    [Fact]
    public async Task AnErrorReadsAsCodeTextAndRequestId()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Error(
            400, """{"code":"payment.bad_amount","message":"amount must be positive","request_id":"req_1"}"""));
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<ValidationException>(() => client.Payments.CreateAsync(1m, "USDT"));

        Assert.Equal("[payment.bad_amount] amount must be positive (request_id=req_1)", error.Message);
        Assert.Equal("amount must be positive", error.Description);
        Assert.StartsWith("Oblodai.ValidationException: [payment.bad_amount] amount must be positive (request_id=req_1)", error.ToString());

        var local = new ConfigException(SdkErrorCodes.BadConfig, "no key");
        Assert.Equal("[sdk.bad_config] no key", local.Message);
    }

    [Fact]
    public async Task WithRawResponseReturnsStatusHeadersAndRequestIdBesideTheValue()
    {
        var handler = new FakeHttpHandler(new ScriptedResponse
        {
            Body = """{"state":0,"result":{"uuid":"x","status":"created"}}""",
            Headers = new Dictionary<string, string> { ["X-Request-ID"] = "gw-req-9", ["X-Rate-Remaining"] = "41" },
        });
        using var client = Client(handler);

        var raw = await client.WithRawResponseAsync(c => c.Payments.CreateAsync(1m, "USDT"));

        Assert.Equal(200, raw.Status);
        Assert.Equal("gw-req-9", raw.RequestId);
        Assert.Equal("41", raw.Header("x-rate-remaining"));
        Assert.Equal("x", raw.Value.Uuid);
        Assert.Equal(PaymentStatus.Created, raw.Value.Status);
    }

    [Fact]
    public async Task WithoutAnEchoTheRawRequestIdIsTheOneTheSdkSent()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok());
        using var client = Client(handler);

        var raw = await client.WithRawResponseAsync(
            c => c.Account.GetBalanceAsync(options: new RequestOptions { RequestId = "mine-1" }));

        Assert.Equal("mine-1", raw.RequestId);
    }

    [Fact]
    public async Task WithRawResponseOnAListGivesTheFirstPageAndAnErrorStillThrows()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Page("""[{"uuid":"a"}]""", 0, 1, 1, false),
            ScriptedResponse.Error(404, """{"code":"payment.not_found","message":"no"}"""));
        using var client = Client(handler);

        var page = await client.WithRawResponseAsync(c => c.Payments.ListHistoryAsync(limit: 1));
        Assert.Equal("a", Assert.Single(page.Value.Items).Uuid);
        Assert.Equal(200, page.Status);

        await Assert.ThrowsAsync<NotFoundException>(
            () => client.WithRawResponseAsync(c => c.Payments.GetInfoAsync(uuid: "zz")));
    }

    [Fact]
    public async Task WithOptionsMakesAClientWithOtherSettingsAndLeavesTheOriginalAlone()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Error(503, """{"code":"db.unavailable","message":"later","retryable":true}"""),
            ScriptedResponse.Error(503, """{"code":"db.unavailable","message":"later","retryable":true}"""),
            ScriptedResponse.Ok());
        using var client = Client(handler);

        var impatient = client.WithOptions(o => o with
        {
            Retry = new RetryOptions { MaxRetries = 0 },
            Headers = new Dictionary<string, string> { ["X-Tenant"] = "shop-2" },
        });

        await Assert.ThrowsAsync<UnavailableException>(() => impatient.Account.GetBalanceAsync());
        Assert.Single(handler.Calls);
        Assert.Equal("shop-2", handler.Calls[0].Header("X-Tenant"));
        Assert.Equal("pk", handler.Calls[0].Header(RequestSigner.HeaderPublicId));
        Assert.Same(client.Transport.Clock, impatient.Transport.Clock);

        await client.Account.GetBalanceAsync(); // the original still retries
        Assert.Equal(3, handler.Calls.Count);
        Assert.False(handler.Calls[2].HasHeader("X-Tenant"));
    }

    [Fact]
    public async Task HooksSeeEveryAttemptWithTheSignatureRedacted()
    {
        var requests = new List<RequestInfo>();
        var responses = new List<ResponseInfo>();
        var handler = new FakeHttpHandler(
            new ScriptedResponse { Throws = new HttpRequestException("reset") },
            ScriptedResponse.Ok());
        using var client = Client(handler, new OblodaiOptions
        {
            Hooks = new Hooks { OnRequest = requests.Add, OnResponse = responses.Add },
        });

        await client.Account.GetBalanceAsync();

        Assert.Equal([1, 2], requests.Select(r => r.Attempt));
        Assert.All(requests, r => Assert.Equal("getBalance", r.OperationId));
        Assert.Equal(requests[0].RequestId, requests[1].RequestId);
        Assert.Equal(Redaction.Placeholder, requests[0].Headers[RequestSigner.HeaderSignature]);
        Assert.Equal("https://api.test/v1/balance", requests[0].Url);

        Assert.Equal(0, responses[0].Status);
        Assert.Equal(SdkErrorCodes.TransportNetwork, responses[0].Error!.Code);
        Assert.Equal(200, responses[1].Status);
        Assert.Null(responses[1].Error);
        Assert.Same(requests[1], responses[1].Request);
    }

    [Fact]
    public async Task ARetryPauseGoesThroughTheTimeProvider()
    {
        var time = new RecordingTimeProvider();
        var handler = new FakeHttpHandler(
            ScriptedResponse.Error(429, """{"code":"request.rate_limited","message":"slow","retryable":true,"retry_after":2}"""),
            ScriptedResponse.Ok());
        using var client = Client(handler, new OblodaiOptions { TimeProvider = time });

        await client.Account.GetBalanceAsync();

        Assert.Equal([2000d], time.DelaysMs);
    }

    [Fact]
    public async Task ABatchWaiterPollsUntilTheBatchIsFinished()
    {
        var time = new RecordingTimeProvider();
        var handler = new FakeHttpHandler(
            ScriptedResponse.Ok("""{"batch_id":"b1","status":"pending","kind":"payout","count":2}"""),
            ScriptedResponse.Ok("""{"batch_id":"b1","status":"processing","items":[]}"""),
            ScriptedResponse.Ok("""{"batch_id":"b1","status":"completed","succeeded":2,"items":[]}"""));
        using var client = Client(handler, new OblodaiOptions { TimeProvider = time });

        var submitted = await client.Batches.CreatePayoutAsync([]);
        var done = await client.Batches.WaitAsync(submitted, pollInterval: TimeSpan.FromSeconds(3));

        Assert.Equal(BatchStatus.Completed, done.Status);
        Assert.Equal(2, done.Succeeded);
        Assert.Equal([3000d], time.DelaysMs);
        Assert.All(handler.Calls.Skip(1), c => Assert.EndsWith("/v1/batch/info", c.Url));
        Assert.Contains("\"batch_id\":\"b1\"", handler.Calls[1].Body);
    }

    [Fact]
    public async Task ADocumentJobWaiterReturnsAFailureInsteadOfThrowingAndDownloadsOnlyWhenDone()
    {
        var time = new RecordingTimeProvider();
        var handler = new FakeHttpHandler(
            ScriptedResponse.Ok("""{"job_id":"j1","status":"queued"}"""),
            ScriptedResponse.Ok("""{"job_id":"j1","status":"failed"}"""),
            ScriptedResponse.Ok("""{"job_id":"j2","status":"done"}"""),
            new ScriptedResponse { Body = "%PDF", ContentType = "application/pdf" });
        using var client = Client(handler, new OblodaiOptions { TimeProvider = time });

        var failed = await client.Documents.WaitAsync("j1", pollInterval: TimeSpan.Zero);
        Assert.Equal(DocumentJobStatus.Failed, failed.Status);
        Assert.Equal(SdkErrorCodes.JobNotDone, (await Assert.ThrowsAsync<ConfigException>(() => client.Documents.DownloadAsync(failed))).Code);

        var done = await client.Documents.WaitAsync("j2");
        var file = await client.Documents.DownloadAsync(done);

        Assert.Equal("%PDF", System.Text.Encoding.UTF8.GetString(file.Bytes));
        Assert.Contains("job_id=j2", handler.Calls[^1].Url);
    }

    [Fact]
    public async Task AWaitThatRunsOutSaysSo()
    {
        var time = new RecordingTimeProvider();
        using var client = Client(
            new FakeHttpHandler(_ => ScriptedResponse.Ok("""{"batch_id":"b1","status":"processing"}""")),
            new OblodaiOptions { TimeProvider = time });

        var error = await Assert.ThrowsAsync<TransportException>(() => client.Batches.WaitAsync(
            "b1", pollInterval: TimeSpan.FromSeconds(10), timeout: TimeSpan.FromSeconds(25)));

        Assert.Equal(SdkErrorCodes.WaitTimeout, error.Code);
        Assert.Equal([10000d, 10000d], time.DelaysMs);
    }

    [Fact]
    public void TheLongRunningTableNamesRoutesThatExist()
    {
        foreach (var (create, poll) in LongRunning.Operations)
        {
            Assert.True(Oblodai.Resources.Routes.All.ContainsKey(create), create);
            Assert.True(Oblodai.Resources.Routes.All.ContainsKey(poll), poll);
        }
    }
}
