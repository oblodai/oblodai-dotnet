using System.Text.Json;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// The five per-call options — <c>IdempotencyKey</c>, <c>Timeout</c>, <c>MaxRetries</c>,
/// <c>ExtraHeaders</c>, <c>RequestId</c> — and the <c>X-Request-ID</c> every call carries.
/// </summary>
public class RequestOptionsTests
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

    private static ScriptedResponse Unavailable()
        => ScriptedResponse.Error(503, """{"code":"db.unavailable","message":"later","retryable":true}""");

    [Fact]
    public async Task EveryCallCarriesOneRequestIdOnAllItsAttempts()
    {
        var handler = new FakeHttpHandler(Unavailable(), ScriptedResponse.Ok(), ScriptedResponse.Ok());
        using var client = Client(handler);

        await client.Account.GetBalanceAsync();
        await client.Account.GetBalanceAsync();

        var ids = handler.Calls.Select(c => c.Header(RequestBuilder.HeaderRequestId)).ToList();
        Assert.All(ids, id => Assert.True(Guid.TryParse(id, out _), id));
        Assert.Equal(ids[0], ids[1]);   // the retry of the first call
        Assert.NotEqual(ids[1], ids[2]); // the second call
    }

    [Fact]
    public async Task TheCallersRequestIdIsSentAndACallerHeaderCannotReplaceIt()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok());
        using var client = Client(handler);

        await client.Account.GetBalanceAsync(options: new RequestOptions
        {
            RequestId = "order-42-balance",
            ExtraHeaders = new Dictionary<string, string> { ["X-Request-ID"] = "forged" },
        });

        Assert.Equal("order-42-balance", handler.Calls[0].Header("X-Request-ID"));
    }

    [Fact]
    public async Task MaxRetriesOverridesTheClientPolicyForOneCall()
    {
        var none = new FakeHttpHandler(Unavailable(), ScriptedResponse.Ok());
        using (var client = Client(none))
        {
            await Assert.ThrowsAsync<UnavailableException>(
                () => client.Account.GetBalanceAsync(options: new RequestOptions { MaxRetries = 0 }));
        }

        Assert.Single(none.Calls);

        var more = new FakeHttpHandler(Unavailable(), Unavailable(), Unavailable(), ScriptedResponse.Ok());
        using (var client = Client(more, new OblodaiOptions { Retry = new RetryOptions { MaxRetries = 1, BaseDelayMs = 0 } }))
        {
            await client.Account.GetBalanceAsync(options: new RequestOptions { MaxRetries = 3 });
        }

        Assert.Equal(4, more.Calls.Count);

        using var bad = Client(new FakeHttpHandler());
        var error = await Assert.ThrowsAsync<ConfigException>(
            () => bad.Account.GetBalanceAsync(options: new RequestOptions { MaxRetries = -1 }));
        Assert.Equal(SdkErrorCodes.BadConfig, error.Code);
    }

    [Fact]
    public async Task TimeoutIsATimeSpanPerAttempt()
    {
        var handler = new FakeHttpHandler(new ScriptedResponse { DelayMs = 2000 });
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<TransportException>(() => client.Account.GetBalanceAsync(
            options: new RequestOptions { Timeout = TimeSpan.FromMilliseconds(20), MaxRetries = 0 }));

        Assert.Equal(SdkErrorCodes.TransportTimeout, error.Code);
    }

    [Fact]
    public async Task AnIdempotencyKeyOnARouteThatDoesNotDeduplicateIsRefusedBeforeSending()
    {
        var handler = new FakeHttpHandler();
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<ConfigException>(() => client.Payments.CancelAsync(
            uuid: "u", options: new RequestOptions { IdempotencyKey = "k" }));

        Assert.Equal(SdkErrorCodes.IdempotencyUnsupported, error.Code);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task OnARouteWithItsOwnIdempotencyKeyFieldTheOptionFillsTheBody()
    {
        // Ruling 10: the faucet deduplicates by a body field, not by the header. The option is that
        // field; no Idempotency-Key header is sent.
        var handler = new FakeHttpHandler(ScriptedResponse.Ok());
        using var client = Client(handler);

        await client.Sandbox.FaucetAsync(100m, "USDT", options: new RequestOptions { IdempotencyKey = "faucet-1" });

        var body = JsonDocument.Parse(handler.Calls[0].Body!).RootElement;
        Assert.Equal("faucet-1", body.GetProperty("idempotency_key").GetString());
        Assert.False(handler.Calls[0].HasHeader(RequestSigner.HeaderIdempotencyKey));
    }

    [Fact]
    public async Task ExtraHeadersAreMergedOverTheClientsOwn()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok());
        using var client = Client(handler, new OblodaiOptions
        {
            Headers = new Dictionary<string, string> { ["X-Trace"] = "client", ["X-Tenant"] = "t" },
        });

        await client.Account.GetBalanceAsync(options: new RequestOptions
        {
            ExtraHeaders = new Dictionary<string, string> { ["X-Trace"] = "call" },
        });

        Assert.Equal("call", handler.Calls[0].Header("X-Trace"));
        Assert.Equal("t", handler.Calls[0].Header("X-Tenant"));
    }

    [Fact]
    public void ClientTimeoutsAreTimeSpansAndMustBePositive()
    {
        var error = Assert.Throws<ConfigException>(
            () => new OblodaiClient(new OblodaiOptions { BaseUrl = "https://api.test", Timeout = TimeSpan.Zero }));
        Assert.Equal(nameof(OblodaiOptions.Timeout), error.Field);

        error = Assert.Throws<ConfigException>(
            () => new OblodaiClient(new OblodaiOptions { BaseUrl = "https://api.test", Deadline = TimeSpan.FromSeconds(-1) }));
        Assert.Equal(nameof(OblodaiOptions.Deadline), error.Field);
    }
}
