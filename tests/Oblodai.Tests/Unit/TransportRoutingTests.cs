using Oblodai.Contract;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// The rest of the transport lifecycle: which key pair signs a route, what rides on which route, and
/// what the SDK refuses to send at all. Same fixtures as <see cref="TransportTests"/>.
/// </summary>
public class TransportRoutingTests
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

    [Fact]
    public async Task UsesThePayoutCredentialsForPayoutRoutes()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok("""{"uuid":"p"}"""), ScriptedResponse.Ok("""{"uuid":"i"}"""));
        using var client = Client(handler, new OblodaiOptions { PayoutPublicId = "wk_test_1", PayoutSecret = "s2" });

        await client.Payouts.CreateAsync(new PayoutRequest
        {
            Amount = "1",
            Currency = "USDT",
            Address = Address,
            OrderId = "o",
        });
        await client.Payments.CreateAsync(new PaymentRequest { Amount = "1", Currency = "USDT" });

        Assert.Equal("wk_test_1", handler.Calls[0].Header(RequestSigner.HeaderPublicId));
        Assert.Equal("pk_test_1", handler.Calls[1].Header(RequestSigner.HeaderPublicId));
    }

    [Fact]
    public async Task RefusesACallWithoutCredentialsOnlyWhenTheRouteNeedsThem()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok("""{"currencies":[],"pricing_currencies":[]}"""));
        using var client = new OblodaiClient(new OblodaiOptions { BaseUrl = "https://api.test" }, handler.Client());

        var currencies = await client.Catalog.CurrenciesAsync();
        Assert.Empty(currencies.Assets);

        var error = await Assert.ThrowsAsync<ConfigException>(() => client.Account.BalanceAsync());
        Assert.Equal(SdkErrorCodes.MissingCredentials, error.Code);
    }

    [Fact]
    public async Task SendsTheAdminTokenOnProvisioningRoutesOnly()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/merchants")),
            ScriptedResponse.Ok("""{"balance":{"merchant":[]}}"""));
        using var client = Client(handler, new OblodaiOptions { AdminToken = "adm" });

        await client.Merchants.CreateAsync(new MerchantsRequest { Email = "a@b.c" });
        await client.Account.BalanceAsync();

        Assert.Equal("adm", handler.Calls[0].Header(RequestSigner.HeaderAdminToken));
        Assert.False(handler.Calls[0].HasHeader(RequestSigner.HeaderSignature));
        Assert.False(handler.Calls[1].HasHeader(RequestSigner.HeaderAdminToken));
    }

    [Fact]
    public async Task KeepsAPathPrefixOnTheBaseUrlAndDropsCollidingCallerHeaders()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok("""{"balance":{"merchant":[]}}"""));
        using var client = Client(handler, new OblodaiOptions
        {
            BaseUrl = "https://gw.corp/oblodai/",
            Headers = new Dictionary<string, string> { ["X-Signature"] = "zz", ["X-Trace"] = "t1" },
        });

        await client.Account.BalanceAsync();

        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://gw.corp/oblodai/v1/balance", call.Url);
        Assert.Matches("^[0-9a-f]{64}$", call.Header(RequestSigner.HeaderSignature));
        Assert.Equal("t1", call.Header("X-Trace"));
    }

    [Fact]
    public async Task RefusesPathParametersThatWouldRewriteTheUrl()
    {
        var handler = new FakeHttpHandler();
        using var client = Client(handler);

        foreach (var bad in new[] { "..", "a/b" })
        {
            var error = await Assert.ThrowsAsync<ConfigException>(() => client.Payments.PublicViewAsync(bad));
            Assert.Equal(SdkErrorCodes.BadPathParam, error.Code);
        }

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task SendsTheDocumentIdAsUuidAndReturnsTheBytes()
    {
        var handler = new FakeHttpHandler(new ScriptedResponse
        {
            Body = "%PDF-1.4 report",
            ContentType = "application/pdf",
            Headers = new Dictionary<string, string> { ["Content-Disposition"] = "inline; filename=\"batch.csv\"" },
        });
        using var client = Client(handler);

        var file = await client.Documents.BatchReportAsync("b-1", new FormatQuery { Format = "csv" });

        Assert.Contains("uuid=b-1", handler.Calls[0].Uri.Query);
        Assert.Contains("format=csv", handler.Calls[0].Uri.Query);
        Assert.StartsWith("application/pdf", file.ContentType);
        Assert.Equal("batch.csv", file.Filename);
        Assert.NotEmpty(file.Bytes);
    }

    [Fact]
    public async Task NamesTheRedirectTargetInsteadOfABareEnvelopeError()
    {
        var handler = new FakeHttpHandler(new ScriptedResponse
        {
            Status = 301,
            Body = string.Empty,
            Headers = new Dictionary<string, string> { ["Location"] = "https://www.api.test/v1/balance" },
        });
        using var client = Client(handler, new OblodaiOptions { Retry = new RetryOptions { MaxRetries = 0 } });

        var error = await Assert.ThrowsAsync<ApiException>(() => client.Account.BalanceAsync());

        Assert.Equal(301, error.HttpStatus);
        Assert.Contains("redirect", error.Message);
        Assert.Contains("www.api.test", error.Message);
    }

    [Fact]
    public async Task SurfacesAnIdempotentReplayThatCouldNotBeCached()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok(
            """{"ok":true,"idempotent_replay":true,"detail":"response too large to cache"}"""));
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<ContractException>(
            () => client.Payments.CreateAsync(new PaymentRequest { Amount = "1", Currency = "USDT" }));

        Assert.Equal(SdkErrorCodes.BadEnvelope, error.Code);
        Assert.Contains("already processed", error.Message);
    }
}
