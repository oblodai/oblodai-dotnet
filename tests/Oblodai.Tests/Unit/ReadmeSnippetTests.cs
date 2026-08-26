using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Oblodai.Contract;
using Oblodai.Models;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// Every C# snippet in README.md, compiled. A snippet that does not compile is a bug report from the
/// first person who tries the SDK, and the usual cause is invisible in review: a type that lives in a
/// namespace the snippet's <c>using</c> lines do not import. Keep this file and the README in step —
/// if a snippet changes there, change it here and let the compiler check it.
/// </summary>
public class ReadmeSnippetTests
{
    [Fact]
    public async Task QuickstartAndLookupsCompileAndRun()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payment")),
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payment/info")));

        // --- README: "Start in the sandbox" ---
        using var oblodai = new OblodaiClient(
            new OblodaiOptions { PublicId = "pk_test_1", Secret = "s", BaseUrl = "https://api.test" },
            handler.Client());

        var invoice = await oblodai.Payments.CreateAsync(new PaymentRequest
        {
            Amount = "25",              // amounts are decimal strings, never floats
            Currency = "USDT",          // what you price in — a fiat (USD, EUR, …) or a crypto asset
            Network = Network.Tron,     // omit to let the payer choose the network on the pay page
            OrderId = "order-1001",     // your reference; idempotent per order_id
            UrlCallback = "https://shop.example/oblodai/webhook",
        });

        Assert.NotEmpty($"{invoice.Url} {invoice.Address} {invoice.Status}");

        // --- README: lookups take a bare id, a lookup object, or the object you already have ---
        var byOrderId = await oblodai.Payments.InfoAsync(new PaymentLookup { OrderId = "order-1001" });
        Assert.NotEmpty(byOrderId.Uuid);
    }

    [Fact]
    public void DependencyInjectionSnippetCompiles()
    {
        // --- README: "Dependency injection" ---
        var services = new ServiceCollection();

        services.AddHttpClient("oblodai").ConfigurePrimaryHttpMessageHandler(
            () => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.AddSingleton(sp => new OblodaiClient(
            new OblodaiOptions { PublicId = "pk_test_1", Secret = "s", BaseUrl = "https://api.test" },
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("oblodai")));

        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<OblodaiClient>();
        Assert.NotNull(client.Payments);
    }

    [Fact]
    public async Task ListSnippetsCompileAndRun()
    {
        var page = Fixtures.ResultJson("POST /v1/payment/history");
        var payouts = Fixtures.ResultJson("POST /v1/payout/history");
        var handler = new FakeHttpHandler(
            ScriptedResponse.Ok(page),
            ScriptedResponse.Ok(payouts),
            ScriptedResponse.Ok(payouts));
        using var oblodai = new OblodaiClient(
            new OblodaiOptions { PublicId = "pk_test_1", Secret = "s", BaseUrl = "https://api.test" },
            handler.Client());

        // --- README: "Lists" ---
        Page<Payment> first = await oblodai.Payments.HistoryAsync(new PaymentHistoryRequest { Limit = 50 });
        Assert.NotEmpty($"{first.Items.Count} of {first.Paginate.Total}, more: {first.Paginate.HasPages}");

        await foreach (var payout in oblodai.Payouts.HistoryAsync(new PayoutHistoryRequest { Status = PayoutStatus.Confirmed }))
        {
            Assert.NotEmpty(payout.Uuid);
        }

        List<Payout> refunds = await oblodai.Payouts.HistoryAsync(new PayoutHistoryRequest { Kind = "refund" }).AllAsync(1000);
        Assert.NotNull(refunds);
    }

    [Fact]
    public async Task TheErrorSnippetCompilesAndCatchesWhatItSaysItCatches()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Error(
            409,
            """{"code":"payout.insufficient_funds","message":"top up","retryable":true,"retry_after":60}"""));
        using var oblodai = new OblodaiClient(
            new OblodaiOptions
            {
                PublicId = "pk_test_1",
                Secret = "s",
                BaseUrl = "https://api.test",
                Retry = new RetryOptions { MaxRetries = 0 },
            },
            handler.Client());

        var scheduled = 0;

        // --- README: "Errors" ---
        try
        {
            await oblodai.Payouts.CreateAsync(new PayoutRequest
            {
                Amount = "1",
                Currency = "USDT",
                Address = "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx",
                OrderId = "o",
            });
        }
        catch (OblodaiException error)
            when (error.Code is ErrorCodes.PayoutInsufficientFunds or ErrorCodes.PayoutFundsMaturing)
        {
            // retryable — the balance may still arrive
            scheduled = error.RetryAfter ?? 60;
        }

        Assert.Equal(60, scheduled);
    }

    [Fact]
    public void TheWebhookSnippetCompilesAndRunsOnARealSignedDelivery()
    {
        var sample = Fixtures.WebhookSamples.EnumerateArray().First();
        var rawBodyBytes = System.Text.Encoding.UTF8.GetBytes(sample.GetProperty("raw").GetString()!);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in sample.GetProperty("headers").EnumerateObject())
        {
            headers[header.Name] = header.Value.GetString()!;
        }

        var secret = Fixtures.For("POST /v1/webhooks/rotate-secret").Result.GetProperty("secret").GetString()!;
        var seen = 0;

        // --- README: "Webhooks" ---
        var info = WebhookVerifier.VerifyDelivery(rawBodyBytes, name => headers.GetValueOrDefault(name), new WebhookVerifyOptions
        {
            Secret = secret,
            ToleranceSeconds = 0,
        });

        switch (info.Event)
        {
            case PaymentEvent { Status.Value: "paid" } paid: seen = paid.OrderId is null ? 1 : 2; break;
            case PayoutEvent payout: seen = payout.Uuid.Length; break;
            case WalletEvent deposit: seen = deposit.Address.Length; break;
            default: seen = -1; break; // an event family this snapshot does not know
        }

        Assert.NotEqual(0, seen);
    }

    [Fact]
    public void TheMoneySnippetCompiles()
    {
        // --- README: "Money helpers" ---
        Assert.Equal("0.3", Money.Add("0.1", "0.2"));
        Assert.Equal("0.1", Money.Subtract("0.3", "0.2"));
        Assert.Equal(-1, Money.Compare("0.1", "0.2"));
        Assert.True(Money.AreEqual("25", "25.000000"));
        Assert.True(Money.IsZero("0.000000"));
    }
}
