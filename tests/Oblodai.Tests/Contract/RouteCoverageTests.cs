using System.Text.RegularExpressions;
using Oblodai.Contract;
using Oblodai.Models;
using Oblodai.Resources;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Contract;

/// <summary>
/// Every route the gateway declares has exactly one SDK method, wired to the right method, path, auth
/// gate and idempotency wrapper. The table below is the SDK's coverage ledger: a route the gateway adds
/// shows up in <see cref="Routes.All"/> after codegen and fails this test until a method is wired to it.
/// </summary>
public class RouteCoverageTests
{
    private const string Address = "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx";

    private static readonly Dictionary<string, Func<OblodaiClient, Task>> Coverage = new(StringComparer.Ordinal)
    {
        ["GET /v1/claim/{token}"] = c => c.PayoutLinks.ClaimPreviewAsync("tok"),
        ["GET /v1/currencies"] = c => c.Catalog.CurrenciesAsync(),
        ["GET /v1/documents/balance"] = c => c.Documents.BalanceCertificateAsync(),
        ["GET /v1/documents/batch"] = c => c.Documents.BatchReportAsync("b1"),
        ["GET /v1/documents/fees"] = c => c.Documents.FeeScheduleAsync(),
        ["GET /v1/documents/jobs/file"] = c => c.Documents.JobFileAsync("j1"),
        ["GET /v1/documents/ledger"] = c => c.Documents.LedgerAsync(),
        ["GET /v1/documents/link"] = c => c.Documents.LinkReportAsync("l1"),
        ["GET /v1/documents/referrals"] = c => c.Documents.ReferralsReportAsync(),
        ["GET /v1/documents/split"] = c => c.Documents.SplitReportAsync("i1"),
        ["GET /v1/documents/statement"] = c =>
            c.Documents.StatementAsync(new PeriodQuery { From = "2026-01-01", To = "2026-02-01" }),
        ["GET /v1/documents/wallet/statement"] = c => c.Documents.WalletStatementAsync("w1"),
        ["GET /v1/documents/{kind}/{id}"] = c =>
            c.Documents.DownloadAsync("invoice", "i1", new SignedDocumentQuery { Exp = 1, Sig = "s" }),
        ["GET /v1/link/{id}"] = c => c.PaymentLinks.PublicViewAsync("l1"),
        ["GET /v1/pay/{id}"] = c => c.Payments.PublicViewAsync("i1"),
        ["GET /v1/pay/{id}/qr"] = c => c.Payments.PublicQrAsync("i1"),
        ["GET /v1/sandbox/webhooks"] = c => c.Sandbox.WebhooksAsync().FirstPageAsync(),
        ["POST /v1/api-allowlist/add"] = c => c.Settings.AddApiAllowlistAsync("10.0.0.0/8"),
        ["POST /v1/api-allowlist/enable"] = c => c.Settings.EnableApiAllowlistAsync(true),
        ["POST /v1/api-allowlist/list"] = c => c.Settings.ListApiAllowlistAsync(),
        ["POST /v1/api-allowlist/remove"] = c => c.Settings.RemoveApiAllowlistAsync("10.0.0.0/8"),
        ["POST /v1/auto-withdraw/delete"] = c => c.Settings.DeleteAutoWithdrawAsync("USDT"),
        ["POST /v1/auto-withdraw/list"] = c => c.Settings.ListAutoWithdrawAsync(),
        ["POST /v1/auto-withdraw/set"] = c => c.Settings.SetAutoWithdrawAsync(new AutoWithdrawSetRequest
        {
            Currency = "USDT",
            Network = Network.Tron,
            Address = Address,
        }),
        ["POST /v1/balance"] = c => c.Account.BalanceAsync(),
        ["POST /v1/batch/info"] = c => c.Batches.InfoAsync(new BatchInfoRequest { BatchId = "b1" }),
        ["POST /v1/claim/{token}"] = c => c.PayoutLinks.ClaimAsync("tok", new ClaimRequest { Address = Address }),
        ["POST /v1/exchange-rate/list"] = c => c.Catalog.ExchangeRatesAsync().FirstPageAsync(),
        ["POST /v1/link/{id}/checkout"] = c => c.PaymentLinks.CheckoutAsync("l1"),
        ["POST /v1/pay/{id}/select"] = c =>
            c.Payments.SelectAsync("i1", new PaySelectRequest { Currency = "USDT", Network = Network.Tron }),
        ["POST /v1/payment"] = c => c.Payments.CreateAsync(new PaymentRequest { Amount = "1", Currency = "USDT" }),
        ["POST /v1/payment/accepted/list"] = c => c.Settings.ListAcceptedAsync().FirstPageAsync(),
        ["POST /v1/payment/accepted/set"] = c =>
            c.Settings.SetAcceptedAsync(new PaymentAcceptedSetRequest { Accepted = [] }),
        ["POST /v1/payment/accuracy/get"] = c => c.Settings.GetAccuracyAsync(),
        ["POST /v1/payment/accuracy/set"] = c =>
            c.Settings.SetAccuracyAsync(new PaymentAccuracySetRequest { Enabled = true }),
        ["POST /v1/payment/autorefund/get"] = c => c.Settings.GetAutoRefundAsync(),
        ["POST /v1/payment/autorefund/set"] = c =>
            c.Settings.SetAutoRefundAsync(new PaymentAutorefundSetRequest { Overpay = true, Underpay = false }),
        ["POST /v1/payment/batch"] = c => c.Payments.BatchAsync(new PaymentBatchRequest { Payments = [] }),
        ["POST /v1/payment/cancel"] = c => c.Payments.CancelAsync("i1"),
        ["POST /v1/payment/discount/list"] = c => c.Settings.ListDiscountsAsync().FirstPageAsync(),
        ["POST /v1/payment/discount/set"] = c =>
            c.Settings.SetDiscountAsync(new PaymentDiscountSetRequest { DiscountPercent = 1 }),
        ["POST /v1/payment/fee-config/get"] = c => c.Settings.GetPaymentFeeConfigAsync(),
        ["POST /v1/payment/fee-config/set"] = c =>
            c.Settings.SetPaymentFeeConfigAsync(new PaymentFeeConfigSetRequest { PayerPaysPercent = 50 }),
        ["POST /v1/payment/history"] = c => c.Payments.HistoryAsync().FirstPageAsync(),
        ["POST /v1/payment/info"] = c => c.Payments.InfoAsync("i1"),
        ["POST /v1/payment/link"] = c =>
            c.PaymentLinks.CreateAsync(new PaymentLinkRequest { AmountMode = AmountMode.Open, Currency = "USDT" }),
        ["POST /v1/payment/link/info"] = c => c.PaymentLinks.InfoAsync("l1"),
        ["POST /v1/payment/link/list"] = c => c.PaymentLinks.ListAsync().FirstPageAsync(),
        ["POST /v1/payment/link/toggle"] = c => c.PaymentLinks.ToggleAsync("l1", false),
        ["POST /v1/payment/qr"] = c => c.Payments.QrAsync("i1"),
        ["POST /v1/payment/refund"] = c => c.Refunds.CreateAsync(new PaymentRefundRequest { Uuid = "i1" }),
        ["POST /v1/payment/resend"] = c => c.Payments.ResendAsync("i1"),
        ["POST /v1/payment/resolve"] = c =>
            c.Refunds.ResolveAsync(new PaymentResolveRequest { Uuid = "i1", Action = "accept" }),
        ["POST /v1/payment/send-email"] = c => c.Payments.SendEmailAsync(new PaymentSendEmailRequest { Uuid = "i1" }),
        ["POST /v1/payment/services"] = c => c.Payments.ServicesAsync().FirstPageAsync(),
        ["POST /v1/payment/testing-webhook"] = c =>
            c.Webhooks.TestLegacyAsync(new PaymentTestingWebhookRequest { Url = "https://x.test/hook" }),
        ["POST /v1/payout"] = c => c.Payouts.CreateAsync(new PayoutRequest
        {
            Amount = "1",
            Currency = "USDT",
            Address = Address,
            OrderId = "o",
        }),
        ["POST /v1/payout/approve"] = c => c.Payouts.ApproveAsync("p1"),
        ["POST /v1/payout/batch"] = c => c.Payouts.BatchAsync(new PayoutBatchRequest { Payouts = [] }),
        ["POST /v1/payout/calculate"] = c =>
            c.Payouts.CalculateAsync(new PayoutCalculateRequest { Amount = "1", Currency = "USDT" }),
        ["POST /v1/payout/cancel"] = c => c.Payouts.CancelAsync("p1"),
        ["POST /v1/payout/fee-config/get"] = c => c.Payouts.GetFeeConfigAsync(),
        ["POST /v1/payout/fee-config/set"] = c =>
            c.Payouts.SetFeeConfigAsync(new PayoutFeeConfigSetRequest { FeeOnRecipient = true }),
        ["POST /v1/payout/history"] = c => c.Payouts.HistoryAsync().FirstPageAsync(),
        ["POST /v1/payout/info"] = c => c.Payouts.InfoAsync("p1"),
        ["POST /v1/payout/link"] = c => c.PayoutLinks.CreateAsync(new PayoutLinkRequest
        {
            Amount = "1",
            Currency = "USDT",
            Network = Network.Tron,
        }),
        ["POST /v1/payout/link/batch"] = c => c.PayoutLinks.BatchAsync(new PayoutLinkBatchRequest { Items = [] }),
        ["POST /v1/payout/link/cancel"] = c => c.PayoutLinks.CancelAsync("l1"),
        ["POST /v1/payout/link/cheque"] = c =>
            c.PayoutLinks.ChequeAsync(new PayoutLinkChequeRequest { ClaimToken = "t" }),
        ["POST /v1/payout/link/info"] = c => c.PayoutLinks.InfoAsync("l1"),
        ["POST /v1/payout/link/list"] = c => c.PayoutLinks.ListAsync().FirstPageAsync(),
        ["POST /v1/payout/mass"] = c => c.Payouts.MassAsync(new PayoutMassRequest { Payouts = [] }),
        ["POST /v1/payout/refund-fee-config/get"] = c => c.Payouts.GetRefundFeeConfigAsync(),
        ["POST /v1/payout/refund-fee-config/set"] = c =>
            c.Payouts.SetRefundFeeConfigAsync(new PayoutRefundFeeConfigSetRequest { FeeOnCustomer = true }),
        ["POST /v1/payout/services"] = c => c.Payouts.ServicesAsync().FirstPageAsync(),
        ["POST /v1/payout/validate"] = c => c.Payouts.ValidateAsync(new PayoutValidateRequest
        {
            Amount = "1",
            Currency = "USDT",
            Address = Address,
        }),
        ["POST /v1/referral/info"] = c => c.Account.ReferralAsync(),
        ["POST /v1/refund/batch"] = c => c.Refunds.BatchAsync(new RefundBatchRequest { Refunds = [] }),
        ["POST /v1/sandbox/deposit"] = c => c.Sandbox.DepositAsync(new SandboxDepositRequest { InvoiceId = "i1" }),
        ["POST /v1/sandbox/faucet"] = c =>
            c.Sandbox.FaucetAsync(new SandboxFaucetRequest { Asset = "USDT", Amount = "1" }),
        ["POST /v1/sandbox/reset"] = c => c.Sandbox.ResetAsync(),
        ["POST /v1/sandbox/webhooks/replay"] = c => c.Sandbox.ReplayAsync("d1"),
        ["POST /v1/split/config/get"] = c => c.Splits.GetConfigAsync(),
        ["POST /v1/split/config/set"] = c =>
            c.Splits.SetConfigAsync(new SplitConfigSetRequest { RefundHoldSeconds = 60 }),
        ["POST /v1/split/recipient/optin"] = c => c.Splits.SetOptInAsync(true),
        ["POST /v1/split/recipient/optin/get"] = c => c.Splits.GetOptInAsync(),
        ["POST /v1/split/rule"] = c => c.Splits.CreateRuleAsync(new SplitRuleRequest { Percent = "10" }),
        ["POST /v1/split/rule/delete"] = c => c.Splits.DeleteRuleAsync("r1"),
        ["POST /v1/split/rule/list"] = c => c.Splits.ListRulesAsync().FirstPageAsync(),
        ["POST /v1/test-webhook/payment"] = c => c.Webhooks.TestAsync(
            WebhookKind.Payment, new TestWebhookPaymentRequest { UrlCallback = "https://x.test/hook" }),
        ["POST /v1/test-webhook/payout"] = c => c.Webhooks.TestAsync(
            WebhookKind.Payout, new TestWebhookPaymentRequest { UrlCallback = "https://x.test/hook" }),
        ["POST /v1/test-webhook/wallet"] = c => c.Webhooks.TestAsync(
            WebhookKind.Wallet, new TestWebhookPaymentRequest { UrlCallback = "https://x.test/hook" }),
        ["POST /v1/transfer/batch"] = c => c.Transfers.BatchAsync(new TransferBatchRequest { Transfers = [] }),
        ["POST /v1/transfer/to-personal"] = c =>
            c.Transfers.ToPersonalAsync(new TransferToPersonalRequest { Amount = "1", Currency = "USDT" }),
        ["POST /v1/transfer/to-user"] = c => c.Transfers.ToUserAsync(new TransferToUserRequest
        {
            ToUserId = "u",
            Amount = "1",
            Currency = "USDT",
        }),
        ["POST /v1/vrcs"] = c => c.Account.VrcsAsync(),
        ["POST /v1/wallet"] = c => c.Wallets.CreateAsync(new WalletRequest { Currency = "USDT", Network = Network.Tron }),
        ["POST /v1/wallet/block"] = c => c.Wallets.BlockAsync(new WalletBlockRequest { Address = Address }),
        ["POST /v1/wallet/blocked-address-refund"] = c => c.Wallets.RefundBlockedDepositAsync(
            new WalletBlockedAddressRefundRequest { Uuid = "w1", Address = Address }),
        ["POST /v1/wallet/qr"] = c => c.Wallets.QrAsync(Address),
        ["POST /v1/webhooks"] = c => c.Webhooks.RegisterAsync("https://x.test/hook"),
        ["POST /v1/webhooks/deliveries"] = c => c.Webhooks.DeliveriesAsync().FirstPageAsync(),
        ["POST /v1/webhooks/rotate-secret"] = c => c.Webhooks.RotateSecretAsync(),
        ["POST /v1/documents/jobs"] = c =>
            c.Documents.CreateJobAsync(new DocumentsJobsRequest { Kind = "statement" }),
        ["POST /v1/documents/jobs/info"] = c => c.Documents.JobInfoAsync("j1"),
        ["POST /v1/merchants"] = c => c.Merchants.CreateAsync(new MerchantsRequest { Email = "a@b.c", Name = "A" }),
        ["POST /v1/merchants/{id}/sandbox"] = c => c.Merchants.CreateSandboxAsync("m1"),
    };

    /// <summary>Anything a route may answer: an empty list page, a flag, an id — enough for any model to decode.</summary>
    private const string AnyResult =
        """{"items":[],"paginate":{"total":0,"per_page":1,"offset":0,"has_pages":false},"enabled":true}""";

    public static TheoryData<string> RouteKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in Routes.All.Keys)
        {
            data.Add(key);
        }

        return data;
    }

    [Fact]
    public void TheRegistryIsTheGatewaysMerchantSurfaceNothingMoreAndNothingLess()
    {
        Assert.Equal(
            Fixtures.DeclaredRoutes().OrderBy(r => r, StringComparer.Ordinal).ToList(),
            Routes.All.Keys.OrderBy(r => r, StringComparer.Ordinal).ToList());
        Assert.Equal(107, Routes.All.Count);
        Assert.Equal(469, ErrorCodes.All.Count);
    }

    [Fact]
    public void EveryRouteHasExactlyOneSdkMethod()
    {
        var unwired = Routes.All.Keys.Where(k => !Coverage.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Empty(unwired);

        var unknown = Coverage.Keys.Where(k => !Routes.All.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Empty(unknown);
    }

    [Theory]
    [MemberData(nameof(RouteKeys))]
    public async Task TheMethodHitsTheRightRouteWithTheRightCredentials(string key)
    {
        var route = Routes.All[key];
        var handler = new FakeHttpHandler(new ScriptedResponse
        {
            Body = route.Bare ? "%PDF-1.4" : "{\"state\":0,\"result\":" + AnyResult + "}",
            ContentType = route.Bare ? "application/pdf" : "application/json",
        });

        using var client = new OblodaiClient(
            new OblodaiOptions
            {
                PublicId = "pk",
                Secret = "s",
                AdminToken = "adm",
                BaseUrl = "https://api.test",
            },
            handler.Client());

        await Coverage[key](client);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(route.Method, call.Method);
        Assert.Matches(
            new Regex("^" + Regex.Replace(route.Path, "\\{[a-z]+\\}", "[^/]+") + "$"),
            call.Uri.AbsolutePath);

        // One API key signs every signed route; the admin token rides on the onboarding routes only,
        // and a public route carries no signature at all.
        switch (route.Auth)
        {
            case RouteAuth.Public:
                Assert.False(call.HasHeader(RequestSigner.HeaderSignature));
                Assert.False(call.HasHeader(RequestSigner.HeaderAdminToken));
                break;
            case RouteAuth.Onboard:
                Assert.False(call.HasHeader(RequestSigner.HeaderSignature));
                Assert.Equal("adm", call.Header(RequestSigner.HeaderAdminToken));
                break;
            default:
                Assert.Equal(RouteAuth.Key, route.Auth);
                Assert.Equal("pk", call.Header(RequestSigner.HeaderPublicId));
                Assert.Matches("^[0-9a-f]{64}$", call.Header(RequestSigner.HeaderSignature));
                Assert.False(call.HasHeader(RequestSigner.HeaderAdminToken));
                break;
        }

        Assert.Equal(route.Idempotent, call.HasHeader(RequestSigner.HeaderIdempotencyKey));
        Assert.Equal(route.Method != "GET", call.Body is not null);
    }

    /// <summary>
    /// A batch lookup is one call with the one key — no second attempt under another credential. The
    /// old two-key SDK retried the lookup under the payout credential after a wrong-key-kind refusal;
    /// that fallback is gone, and a single response must be the whole story.
    /// </summary>
    [Fact]
    public async Task BatchInfoIsOneSignedCallWithNoSecondAttempt()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/batch/info")));

        using var client = new OblodaiClient(
            new OblodaiOptions { PublicId = "pk", Secret = "s", BaseUrl = "https://api.test" },
            handler.Client());

        var info = await client.Batches.InfoAsync(new BatchInfoRequest { BatchId = "b1" });

        var call = Assert.Single(handler.Calls);
        Assert.Equal("pk", call.Header(RequestSigner.HeaderPublicId));
        Assert.NotEmpty(info.BatchId);
    }

    [Fact]
    public async Task GoldenBodiesDecodeThroughTheMethodsThatFetchThem()
    {
        // A spot check that the resource layer and the models agree on the wire, end to end.
        var handler = new FakeHttpHandler(
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payment")),
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payout/link")),
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payment/history")),
            ScriptedResponse.Ok(Fixtures.ResultJson("GET /v1/currencies")));

        using var client = new OblodaiClient(
            new OblodaiOptions { PublicId = "pk", Secret = "s", BaseUrl = "https://api.test" },
            handler.Client());

        var invoice = await client.Payments.CreateAsync(new PaymentRequest { Amount = "25", Currency = "USDT" });
        Assert.NotEmpty(invoice.Uuid);
        Assert.True(invoice.Status.IsKnown);

        var link = await client.PayoutLinks.CreateAsync(new PayoutLinkRequest
        {
            Amount = "1",
            Currency = "USDT",
            Network = Network.Tron,
        });
        Assert.NotEmpty(link.LinkId);

        var page = await client.Payments.HistoryAsync();
        Assert.NotEmpty(page.Items);
        Assert.True(page.Paginate.Total > 0);

        var currencies = await client.Catalog.CurrenciesAsync();
        Assert.NotEmpty(currencies.Assets);
    }
}
