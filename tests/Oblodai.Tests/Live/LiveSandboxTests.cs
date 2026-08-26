using Oblodai.Contract;
using Oblodai.Models;
using Xunit;

namespace Oblodai.Tests.Live;

/// <summary>
/// The money path against a REAL gateway (<c>OBLODAI_LIVE_URL</c>): onboard a merchant, take a sandbox
/// key and walk an invoice from creation to a payout — signature, envelope, idempotency and the error
/// classification all exercised for real. Skipped when no gateway is configured.
/// </summary>
[TestCaseOrderer(StepOrderer.TypeName, StepOrderer.AssemblyName)]
public class LiveSandboxTests : IClassFixture<LiveEnvironment>
{
    private static Payment? _invoice;

    private readonly LiveEnvironment _live;

    public LiveSandboxTests(LiveEnvironment live) => _live = live;

    [LiveFact]
    [Step(1)]
    public async Task ReadsPublicCatalogDataWithoutCredentials()
    {
        var currencies = await _live.Anonymous.Catalog.CurrenciesAsync();

        Assert.NotEmpty(currencies.Assets);
        Assert.NotEmpty(currencies.PricingCurrencies);
        Assert.All(currencies.Assets, asset => Assert.NotEmpty(asset.Currency));
    }

    [LiveFact]
    [Step(2)]
    public async Task CreatesAnInvoiceAndReadsItBackByOrderIdAndUuid()
    {
        _invoice = await _live.Merchant.Payments.CreateAsync(new PaymentRequest
        {
            Amount = "25",
            Currency = "USDT",
            Network = Network.Tron,
            OrderId = LiveEnvironment.Unique("sdk-live"),
        });

        Assert.Equal(PaymentStatus.Created, _invoice.Status);
        Assert.NotEmpty(_invoice.Address);

        var byOrderId = await _live.Merchant.Payments.InfoAsync(new PaymentLookup { OrderId = _invoice.OrderId });
        Assert.Equal(_invoice.Uuid, byOrderId.Uuid);

        var page = await _live.Merchant.Payments.HistoryAsync(new PaymentHistoryRequest { Limit = 5 });
        Assert.Contains(page.Items, p => p.Uuid == _invoice.Uuid);

        // A signed GET with a query string: the signature covers path + raw query.
        var deliveries = await _live.Merchant.Sandbox.WebhooksAsync(new PageParams { Limit = 5, Offset = 0 });
        Assert.NotNull(deliveries.Items);
    }

    [LiveFact]
    [Step(3)]
    public async Task ReplaysAnIdempotentCreateAndRefusesAReusedKeyWithADifferentBody()
    {
        var key = LiveEnvironment.Unique("sdk-idem");
        var orderId = $"{key}-o";

        var first = await _live.Merchant.Payments.CreateAsync(
            new PaymentRequest { Amount = "5", Currency = "USDT", Network = Network.Tron, OrderId = orderId },
            new RequestOptions { IdempotencyKey = key });
        var replay = await _live.Merchant.Payments.CreateAsync(
            new PaymentRequest { Amount = "5", Currency = "USDT", Network = Network.Tron, OrderId = orderId },
            new RequestOptions { IdempotencyKey = key });

        Assert.Equal(first.Uuid, replay.Uuid);

        var error = await Assert.ThrowsAsync<IdempotencyConflictException>(() => _live.Merchant.Payments.CreateAsync(
            new PaymentRequest { Amount = "2", Currency = "USDT", Network = Network.Tron, OrderId = $"{key}-o2" },
            new RequestOptions { IdempotencyKey = key }));

        Assert.Equal(ErrorCodes.IdempotencyKeyReused, error.Code);
        Assert.Equal(409, error.HttpStatus);
    }

    [LiveFact]
    [Step(4)]
    public async Task SimulatesADepositSeesTheInvoicePaidThenFundsAndCreatesAPayout()
    {
        Assert.NotNull(_invoice);

        await _live.Merchant.Sandbox.DepositAsync(new SandboxDepositRequest
        {
            InvoiceId = _invoice!.Uuid,
            Amount = "25",
            Confirmations = 20,
            Txid = LiveEnvironment.Unique("sdk-tx"),
        });

        var paid = await _live.Merchant.Payments.InfoAsync(_invoice.Uuid);
        Assert.True(Statuses.IsPaymentPaid(paid.Status), $"invoice ended as {paid.Status}");

        await _live.Merchant.Sandbox.FaucetAsync(new SandboxFaucetRequest { Asset = "USDT", Amount = "100" });
        var balance = await _live.Merchant.Account.BalanceAsync();
        Assert.Contains(balance.Balances.Merchant, entry => entry.Currency == "USDT");

        var calculation = await _live.Merchant.Payouts.CalculateAsync(new PayoutCalculateRequest
        {
            Amount = "10",
            Currency = "USDT",
            Network = Network.Tron,
        });
        Assert.Equal("USDT", calculation.Currency);
        Assert.NotEmpty(calculation.FeeBearer.Value);

        var validation = await _live.Merchant.Payouts.ValidateAsync(new PayoutValidateRequest
        {
            Amount = "10",
            Currency = "USDT",
            Network = Network.Tron,
            Address = LiveEnvironment.Address,
        });
        Assert.True(validation.Valid);

        var payout = await _live.Merchant.Payouts.CreateAsync(new PayoutRequest
        {
            Amount = "10",
            Currency = "USDT",
            Network = Network.Tron,
            Address = LiveEnvironment.Address,
            OrderId = LiveEnvironment.Unique("sdk-po"),
        });

        Assert.NotEmpty(payout.Uuid);
        Assert.Equal(payout.OrderId, (await _live.Merchant.Payouts.InfoAsync(payout.Uuid)).OrderId);
    }

    [LiveFact]
    [Step(5)]
    public async Task ClassifiesADomainRefusalWithTheGatewaysOwnRetryableFlag()
    {
        var error = await Assert.ThrowsAsync<ConflictException>(() => _live.Merchant.Payouts.CreateAsync(new PayoutRequest
        {
            Amount = "999999",
            Currency = "USDT",
            Network = Network.Tron,
            Address = LiveEnvironment.Address,
            OrderId = LiveEnvironment.Unique("sdk-big"),
        }));

        Assert.Equal(ErrorCodes.PayoutInsufficientFunds, error.Code);
        Assert.Equal(409, error.HttpStatus);
        Assert.False(string.IsNullOrEmpty(error.RequestId));
        Assert.False(error.Synthetic);
    }

    [LiveFact]
    [Step(6)]
    public async Task RegistersAnEndpointAndRehearsesAWebhookTheGatewaySigns()
    {
        var hook = LiveEnvironment.HookUrl ?? "http://127.0.0.1:8096/hook";

        var endpoint = await _live.Merchant.Webhooks.RegisterAsync(hook);
        Assert.NotEmpty(endpoint.EndpointId);
        Assert.False(string.IsNullOrEmpty(endpoint.Secret));

        // Delivery itself needs a reachable receiver; the signature contract is covered by the recorded
        // deliveries in the contract snapshot, which the unit tier verifies byte for byte.
        await LiveEnvironment.AcceptRefusalAsync(_live.Merchant.Webhooks.TestAsync(
            WebhookKind.Payment,
            new TestWebhookPaymentRequest
            {
                UrlCallback = hook,
                Currency = "USDT",
                Network = Network.Tron,
                Status = PaymentStatus.Paid,
            }));

        var log = await _live.Merchant.Webhooks.DeliveriesAsync(new WebhooksDeliveriesRequest { Limit = 5 });
        Assert.NotNull(log.Items);
    }
}
