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
    private static PaymentView? _invoice;

    private readonly LiveEnvironment _live;

    public LiveSandboxTests(LiveEnvironment live) => _live = live;

    [LiveFact]
    [Step(1)]
    public async Task ReadsPublicCatalogDataWithoutCredentials()
    {
        var currencies = await _live.Anonymous.Checkout.ListCurrenciesAsync();

        Assert.NotEmpty(currencies.Currencies);
        Assert.NotEmpty(currencies.PricingCurrencies);
    }

    [LiveFact]
    [Step(2)]
    public async Task CreatesAnInvoiceAndReadsItBackByOrderIdAndUuid()
    {
        _invoice = await _live.Merchant.Payments.CreateAsync(
            amount: 25m, currency: "USDT", network: "tron", orderId: LiveEnvironment.Unique("sdk-live"));

        Assert.Equal(PaymentStatus.Created, _invoice.Status);
        Assert.NotEmpty(_invoice.Address);

        var byOrderId = await _live.Merchant.Payments.GetInfoAsync(orderId: _invoice.OrderId);
        Assert.Equal(_invoice.Uuid, byOrderId.Uuid);

        var page = await _live.Merchant.Payments.ListHistoryAsync(limit: 5);
        Assert.Contains(page.Items, p => p.Uuid == _invoice.Uuid);

        // A signed GET with a query string: the signature covers path + raw query.
        var deliveries = await _live.Merchant.Sandbox.ListWebhooksAsync(limit: 5, offset: 0);
        Assert.NotNull(deliveries.Items);
    }

    [LiveFact]
    [Step(3)]
    public async Task ReplaysAnIdempotentCreateAndRefusesAReusedKeyWithADifferentBody()
    {
        var key = LiveEnvironment.Unique("sdk-idem");
        var orderId = $"{key}-o";
        var options = new RequestOptions { IdempotencyKey = key };

        var first = await _live.Merchant.Payments.CreateAsync(5m, "USDT", network: "tron", orderId: orderId, options: options);
        var replay = await _live.Merchant.Payments.CreateAsync(5m, "USDT", network: "tron", orderId: orderId, options: options);
        Assert.Equal(first.Uuid, replay.Uuid);

        var error = await Assert.ThrowsAsync<IdempotencyConflictException>(() => _live.Merchant.Payments.CreateAsync(
            2m, "USDT", network: "tron", orderId: $"{key}-o2", options: options));

        Assert.Equal("idempotency.key_reused", error.Code);
        Assert.Equal(409, error.HttpStatus);
    }

    [LiveFact]
    [Step(4)]
    public async Task SimulatesADepositSeesTheInvoicePaidThenFundsAndCreatesAPayout()
    {
        Assert.NotNull(_invoice);

        await _live.Merchant.Sandbox.SimulateDepositAsync(
            _invoice!.Uuid, amount: "25", confirmations: 20, txid: LiveEnvironment.Unique("sdk-tx"));

        var paid = await _live.Merchant.Payments.GetInfoAsync(uuid: _invoice.Uuid);
        Assert.True(Statuses.IsPaymentPaid(paid.Status), $"invoice ended as {paid.Status}");

        await _live.Merchant.Sandbox.FaucetAsync(amount: 100m, asset: "USDT");
        var balance = await _live.Merchant.Account.GetBalanceAsync();
        Assert.NotNull(balance.Balance);

        var calculation = await _live.Merchant.Payouts.CalculateAsync(10m, "USDT", network: "tron");
        Assert.Equal("USDT", calculation.Currency);

        var validation = await _live.Merchant.Payouts.ValidateAsync(LiveEnvironment.Address, 10m, "USDT", network: "tron");
        Assert.True(validation.Valid);

        var payout = await _live.Merchant.Payouts.CreateAsync(
            LiveEnvironment.Address, 10m, "USDT", LiveEnvironment.Unique("sdk-po"), network: "tron");

        Assert.NotEmpty(payout.Uuid);
        Assert.Equal(payout.OrderId, (await _live.Merchant.Payouts.GetInfoAsync(uuid: payout.Uuid)).OrderId);
    }

    [LiveFact]
    [Step(5)]
    public async Task ClassifiesADomainRefusalWithTheGatewaysOwnRetryableFlag()
    {
        var error = await Assert.ThrowsAsync<ConflictException>(() => _live.Merchant.Payouts.CreateAsync(
            LiveEnvironment.Address, 999999m, "USDT", LiveEnvironment.Unique("sdk-big"), network: "tron"));

        Assert.Equal("payout.insufficient_funds", error.Code);
        Assert.Equal(409, error.HttpStatus);
        Assert.False(string.IsNullOrEmpty(error.RequestId));
        Assert.StartsWith("[payout.insufficient_funds] ", error.Message);
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

        var log = await _live.Merchant.Webhooks.ListDeliveriesAsync(limit: 5);
        Assert.NotNull(log.Items);
    }
}
