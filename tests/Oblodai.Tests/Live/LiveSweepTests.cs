using Oblodai.Contract;
using Oblodai.Models;
using Oblodai.Resources;
using Xunit;

namespace Oblodai.Tests.Live;

/// <summary>
/// Every namespace against a REAL gateway (<c>OBLODAI_LIVE_URL</c>). The point is not the business
/// outcome but that the bodies the SDK sends are accepted (no 400 from our own shapes) and that the
/// bodies coming back decode (no <see cref="ContractException"/>). Routes that need a subsystem the
/// stand may lack (documents, email) are probed and skipped when the gateway reports them disabled.
/// </summary>
[TestCaseOrderer(StepOrderer.TypeName, StepOrderer.AssemblyName)]
public class LiveSweepTests : IClassFixture<LiveEnvironment>
{
    private static Payment? _invoice;
    private static PayoutLink? _link;
    private static bool _documentsEnabled = true;

    private readonly LiveEnvironment _live;

    public LiveSweepTests(LiveEnvironment live) => _live = live;

    private OblodaiClient Ob => _live.Merchant;

    private OblodaiClient Pub => _live.Anonymous;

    [LiveFact]
    [Step(1)]
    public async Task FundsTheStoreAndOpensAnInvoice()
    {
        await Ob.Sandbox.FaucetAsync(new SandboxFaucetRequest { Asset = "USDT", Amount = "1000" });

        // A per-invoice url_callback needs a registered endpoint: the gateway signs with its secret.
        await Ob.Webhooks.RegisterAsync(LiveEnvironment.HookUrl ?? "http://127.0.0.1:8096/hook");

        _invoice = await Ob.Payments.CreateAsync(new PaymentRequest
        {
            Amount = "25",
            Currency = "USDT",
            Network = Network.Tron,
            OrderId = LiveEnvironment.Unique("sw"),
            PayerEmail = "buyer@example.com",
            UrlCallback = LiveEnvironment.HookUrl ?? "http://127.0.0.1:8096/hook",
        });

        Assert.NotEmpty(_invoice.Uuid);

        try
        {
            await Ob.Documents.BalanceCertificateAsync();
        }
        catch (NotFoundException error) when (error.Code == "document.disabled")
        {
            _documentsEnabled = false;
        }
    }

    [LiveFact]
    [Step(2)]
    public async Task CatalogAndAccount()
    {
        Assert.NotEmpty((await Pub.Catalog.CurrenciesAsync()).Assets);
        Assert.NotNull((await Pub.Catalog.ExchangeRatesAsync(new ExchangeRateListRequest { CurrencyFrom = "BTC" })).Items);
        Assert.NotNull((await Ob.Account.BalanceAsync()).Balances.Merchant);
        Assert.NotEmpty((await Ob.Account.ReferralAsync()).Code);
        await Ob.Account.VrcsAsync();
        await Ob.Account.VrcsAsync(false);
    }

    [LiveFact]
    [Step(3)]
    public async Task PaymentsLookupsQrServicesPublicCheckoutAndBatch()
    {
        Assert.NotNull(_invoice);

        Assert.Equal(_invoice!.Uuid, (await Ob.Payments.GetAsync(_invoice.Uuid)).Uuid);

        // Sandbox invoices carry a synthetic `sandbox:` address, which the gateway deliberately does not
        // render into a QR — the fields come back empty. A real invoice returns a data URI.
        Assert.NotNull((await Ob.Payments.QrAsync(_invoice.Uuid)).Image);
        Assert.NotEmpty((await Ob.Payments.ServicesAsync(new PaymentServicesRequest { Limit = 5 })).Items);
        Assert.Equal(PaymentStatus.Created, (await Pub.Payments.PublicViewAsync(_invoice.Uuid)).Status);
        Assert.NotNull((await Pub.Payments.PublicQrAsync(_invoice.Uuid)).Image);

        var multi = await Ob.Payments.CreateAsync(new PaymentRequest
        {
            Amount = "10",
            Currency = "USDT",
            OrderId = LiveEnvironment.Unique("sw-multi"),
        });
        var selected = await Pub.Payments.SelectAsync(
            multi.Uuid, new PaySelectRequest { Currency = "USDT", Network = Network.Tron });
        Assert.Equal(Network.Tron, selected.Network);

        await LiveEnvironment.AcceptRefusalAsync(Ob.Payments.ResendAsync(_invoice.Uuid));
        await LiveEnvironment.AcceptRefusalAsync(
            Ob.Payments.SendEmailAsync(new PaymentSendEmailRequest { Uuid = _invoice.Uuid }));

        var batch = await Ob.Payments.BatchAsync(new PaymentBatchRequest
        {
            OnError = BatchOnError.Continue,
            Payments =
            [
                new PaymentBatchPaymentsItem
                {
                    Amount = "3",
                    Currency = "USDT",
                    Network = Network.Tron,
                    OrderId = LiveEnvironment.Unique("sw-b"),
                },
            ],
        });
        Assert.NotEmpty(batch.BatchId);
        Assert.Equal(batch.BatchId, (await Ob.Batches.InfoAsync(new BatchInfoRequest { BatchId = batch.BatchId })).BatchId);

        var toCancel = await Ob.Payments.CreateAsync(new PaymentRequest
        {
            Amount = "1",
            Currency = "USDT",
            Network = Network.Tron,
            OrderId = LiveEnvironment.Unique("sw-c"),
        });
        Assert.Equal(PaymentStatus.Cancelled, (await Ob.Payments.CancelAsync(toCancel.Uuid)).Status);

        await foreach (var payment in Ob.Payments.HistoryAsync(new PaymentHistoryRequest { Limit = 2 }))
        {
            Assert.NotEmpty(payment.Uuid);
        }
    }

    [LiveFact]
    [Step(4)]
    public async Task DepositThenRefundResolveAndRefundBatch()
    {
        Assert.NotNull(_invoice);

        await Ob.Sandbox.DepositAsync(new SandboxDepositRequest
        {
            InvoiceId = _invoice!.Uuid,
            Amount = "25",
            Confirmations = 20,
            Txid = LiveEnvironment.Unique("sw-tx"),
        });

        var paid = await Ob.Payments.GetAsync(_invoice.Uuid);
        Assert.Contains(paid.Status.Value, new[] { "paid", "confirm_check" });

        await LiveEnvironment.AcceptRefusalAsync(Ob.Refunds.CreateAsync(new PaymentRefundRequest
        {
            Uuid = _invoice.Uuid,
            Address = LiveEnvironment.Address,
            Amount = "5",
            Reference = LiveEnvironment.Unique("sw-r"),
        }));
        await LiveEnvironment.AcceptRefusalAsync(
            Ob.Refunds.ResolveAsync(new PaymentResolveRequest { Uuid = _invoice.Uuid, Action = "accept" }));
        await LiveEnvironment.AcceptRefusalAsync(Ob.Refunds.BatchAsync(new RefundBatchRequest
        {
            Refunds =
            [
                new RefundBatchRefundsItem
                {
                    Uuid = _invoice.Uuid,
                    Address = LiveEnvironment.Address,
                    Amount = "1",
                    Reference = LiveEnvironment.Unique("sw-rb"),
                },
            ],
        }));
    }

    [LiveFact]
    [Step(5)]
    public async Task PayoutsEveryShape()
    {
        Assert.Equal("USDT", (await Ob.Payouts.CalculateAsync(new PayoutCalculateRequest
        {
            Amount = "10",
            Currency = "USDT",
            Network = Network.Tron,
        })).Currency);

        Assert.True((await Ob.Payouts.ValidateAsync(new PayoutValidateRequest
        {
            Amount = "10",
            Currency = "USDT",
            Network = Network.Tron,
            Address = LiveEnvironment.Address,
        })).Valid);

        var payout = await Ob.Payouts.CreateAsync(new PayoutRequest
        {
            Amount = "10",
            Currency = "USDT",
            Network = Network.Tron,
            Address = LiveEnvironment.Address,
            OrderId = LiveEnvironment.Unique("sw-po"),
        });
        Assert.Equal(payout.Uuid, (await Ob.Payouts.GetAsync(new PayoutLookup { OrderId = payout.OrderId })).Uuid);

        await LiveEnvironment.AcceptRefusalAsync(Ob.Payouts.CancelAsync(payout.Uuid));
        await LiveEnvironment.AcceptRefusalAsync(Ob.Payouts.ApproveAsync(payout.Uuid));

        var mass = await Ob.Payouts.MassAsync(new PayoutMassRequest
        {
            Payouts =
            [
                new PayoutMassPayoutsItem
                {
                    Amount = "1",
                    Currency = "USDT",
                    Network = Network.Tron,
                    Address = LiveEnvironment.Address,
                    OrderId = LiveEnvironment.Unique("sw-m"),
                },
            ],
        });
        Assert.Equal(0, mass[0].Idx);

        var batch = await Ob.Payouts.BatchAsync(new PayoutBatchRequest
        {
            Payouts =
            [
                new PayoutBatchPayoutsItem
                {
                    Amount = "1",
                    Currency = "USDT",
                    Network = Network.Tron,
                    Address = LiveEnvironment.Address,
                    OrderId = LiveEnvironment.Unique("sw-pb"),
                },
            ],
        });
        Assert.NotEmpty(batch.BatchId);

        Assert.NotEmpty((await Ob.Payouts.ServicesAsync()).Items);
        Assert.True((await Ob.Payouts.SetFeeConfigAsync(new PayoutFeeConfigSetRequest { FeeOnRecipient = true })).FeeOnRecipient);
        Assert.True((await Ob.Payouts.GetFeeConfigAsync()).FeeOnRecipient);
        Assert.True((await Ob.Payouts.SetRefundFeeConfigAsync(new PayoutRefundFeeConfigSetRequest { FeeOnCustomer = true })).FeeOnCustomer);
        Assert.True((await Ob.Payouts.GetRefundFeeConfigAsync()).FeeOnCustomer);
        Assert.NotNull((await Ob.Payouts.HistoryAsync(new PayoutHistoryRequest { Kind = "refund", Limit = 5 })).Items);
    }

    [LiveFact]
    [Step(6)]
    public async Task PayoutLinksCreateClaimCancelAndBatch()
    {
        _link = await Ob.PayoutLinks.CreateAsync(new PayoutLinkRequest
        {
            Amount = "5",
            Currency = "USDT",
            Network = Network.Tron,
            Reference = LiveEnvironment.Unique("sw-pl"),
            Title = "Bonus",
            ExpiresInSeconds = 3600,
        });

        Assert.False(string.IsNullOrEmpty(_link.ClaimToken));
        Assert.Equal(PayoutLinkStatus.Funded, (await Ob.PayoutLinks.GetAsync(_link.LinkId)).Status);
        Assert.NotEmpty((await Ob.PayoutLinks.ListAsync(new PayoutLinkListRequest { Limit = 5 })).Items);

        Assert.True((await Pub.PayoutLinks.ClaimPreviewAsync(_link.ClaimToken!)).Claimable);
        var claimed = await Pub.PayoutLinks.ClaimAsync(
            _link.ClaimToken!, new ClaimRequest { Address = LiveEnvironment.Address });
        Assert.NotEmpty(claimed.PayoutId);

        var second = await Ob.PayoutLinks.CreateAsync(new PayoutLinkRequest
        {
            Amount = "1",
            Currency = "USDT",
            Network = Network.Tron,
            Reference = LiveEnvironment.Unique("sw-pl2"),
        });
        Assert.Equal(PayoutLinkStatus.Cancelled, (await Ob.PayoutLinks.CancelAsync(second.LinkId)).Status);

        var batch = await Ob.PayoutLinks.BatchAsync(new PayoutLinkBatchRequest
        {
            Items =
            [
                new PayoutLinkBatchItemsItem
                {
                    Amount = "1",
                    Currency = "USDT",
                    Network = Network.Tron,
                    Reference = LiveEnvironment.Unique("sw-plb"),
                },
            ],
        });
        Assert.True(batch[0].Ok);
    }

    [LiveFact]
    [Step(7)]
    public async Task PaymentLinksCreateListToggleAndCheckout()
    {
        var created = await Ob.PaymentLinks.CreateAsync(new PaymentLinkRequest
        {
            Title = "Tip",
            AmountMode = AmountMode.Fixed,
            Currency = "USDT",
            AmountFixed = "10",
            PinnedNetwork = Network.Tron,
        });

        Assert.NotEmpty(created.LinkId);
        Assert.True((await Ob.PaymentLinks.GetAsync(created.LinkId)).Active);
        Assert.NotEmpty((await Ob.PaymentLinks.ListAsync()).Items);
        Assert.Equal(AmountMode.Fixed, (await Pub.PaymentLinks.PublicViewAsync(created.LinkId)).AmountMode);

        var checkout = await Pub.PaymentLinks.CheckoutAsync(
            created.LinkId, new LinkCheckoutRequest { Currency = "USDT", Network = Network.Tron });
        Assert.NotEmpty(checkout.Uuid);

        Assert.False((await Ob.PaymentLinks.ToggleAsync(created.LinkId, false)).Active);
    }

    [LiveFact]
    [Step(8)]
    public async Task SplitsAndSettings()
    {
        var rule = await Ob.Splits.CreateRuleAsync(new SplitRuleRequest
        {
            Percent = "10",
            Address = LiveEnvironment.Address,
            Network = Network.Tron,
            Note = "partner",
        });
        Assert.Contains((await Ob.Splits.ListRulesAsync()).Items, r => r.RuleId == rule.RuleId);
        Assert.Equal(3600, (await Ob.Splits.SetConfigAsync(new SplitConfigSetRequest { RefundHoldSeconds = 3600 })).RefundHoldSeconds);
        Assert.Equal(3600, (await Ob.Splits.GetConfigAsync()).RefundHoldSeconds);
        Assert.True((await Ob.Splits.SetOptInAsync(true)).Enabled);
        Assert.True((await Ob.Splits.GetOptInAsync()).Enabled);
        Assert.True((await Ob.Splits.DeleteRuleAsync(rule.RuleId)).Ok);

        Assert.Equal(2, (await Ob.Settings.SetDiscountAsync(new PaymentDiscountSetRequest
        {
            Currency = "USDT",
            Network = Network.Tron,
            DiscountPercent = 2,
        })).DiscountPercent);
        Assert.NotEmpty((await Ob.Settings.ListDiscountsAsync()).Items);

        Assert.True((await Ob.Settings.SetAccuracyAsync(new PaymentAccuracySetRequest
        {
            Enabled = true,
            AccuracyPercent = 2,
        })).Enabled);
        Assert.True((await Ob.Settings.GetAccuracyAsync()).Enabled);

        Assert.True((await Ob.Settings.SetAutoRefundAsync(new PaymentAutorefundSetRequest
        {
            Overpay = true,
            Underpay = false,
        })).Overpay);
        Assert.NotNull((await Ob.Settings.GetAutoRefundAsync()).Configured);

        Assert.True((await Ob.Settings.SetAcceptedAsync(new PaymentAcceptedSetRequest
        {
            Accepted = [new PaymentAcceptedSetAcceptedItem { Currency = "USDT", Network = Network.Tron }],
        })).Ok);
        Assert.NotNull((await Ob.Settings.ListAcceptedAsync()).Items);

        Assert.Equal(50, (await Ob.Settings.SetPaymentFeeConfigAsync(new PaymentFeeConfigSetRequest
        {
            PayerPaysPercent = 50,
        })).PayerPaysPercent);
        Assert.Equal(50, (await Ob.Settings.GetPaymentFeeConfigAsync()).PayerPaysPercent);

        Assert.NotEmpty(await Ob.Settings.SetAutoWithdrawAsync(new AutoWithdrawSetRequest
        {
            Currency = "USDT",
            Network = Network.Tron,
            Address = LiveEnvironment.Address,
            MinAmount = "100",
        }));
        Assert.NotNull(await Ob.Settings.ListAutoWithdrawAsync());
        Assert.NotNull(await Ob.Settings.DeleteAutoWithdrawAsync("USDT"));

        Assert.Contains("203.0.113.0/24", (await Ob.Settings.AddApiAllowlistAsync("203.0.113.0/24")).Items);
        Assert.Contains("203.0.113.0/24", (await Ob.Settings.ListApiAllowlistAsync()).Items);
        Assert.False((await Ob.Settings.EnableApiAllowlistAsync(false)).Enabled);
        Assert.DoesNotContain("203.0.113.0/24", (await Ob.Settings.RemoveApiAllowlistAsync("203.0.113.0/24")).Items);
    }

    [LiveFact]
    [Step(9)]
    public async Task WebhooksAndTheSandboxInspector()
    {
        var hook = LiveEnvironment.HookUrl ?? "http://127.0.0.1:8096/hook";

        Assert.NotEmpty((await Ob.Webhooks.RegisterAsync(hook)).EndpointId);
        Assert.False(string.IsNullOrEmpty((await Ob.Webhooks.RotateSecretAsync()).Secret));
        Assert.NotNull((await Ob.Webhooks.DeliveriesAsync(new WebhooksDeliveriesRequest { Limit = 5 })).Items);

        await LiveEnvironment.AcceptRefusalAsync(Ob.Webhooks.TestAsync(WebhookKind.Payment, new TestWebhookPaymentRequest
        {
            UrlCallback = hook,
            Currency = "USDT",
            Network = Network.Tron,
            Status = PaymentStatus.Paid,
        }));
        await LiveEnvironment.AcceptRefusalAsync(Ob.Webhooks.TestLegacyAsync(new PaymentTestingWebhookRequest
        {
            Url = hook,
            Status = PaymentStatus.Paid,
        }));

        var inspector = await Ob.Sandbox.WebhooksAsync(new PageParams { Limit = 5 });
        Assert.NotNull(inspector.Items);

        var terminal = inspector.Items.FirstOrDefault(d => d.Status.Value is "delivered" or "dead");
        if (terminal is not null)
        {
            await LiveEnvironment.AcceptRefusalAsync(Ob.Sandbox.ReplayAsync(terminal.Id));
        }
    }

    [LiveFact]
    [Step(10)]
    public async Task WalletsAndTransfers()
    {
        // A dev store has no chain behind it, so the gateway refuses these by design; the shapes still travel.
        await LiveEnvironment.AcceptRefusalAsync(Ob.Wallets.CreateAsync(new WalletRequest
        {
            Currency = "USDT",
            Network = Network.Tron,
            OrderId = LiveEnvironment.Unique("sw-w"),
        }));
        await LiveEnvironment.AcceptRefusalAsync(Ob.Wallets.QrAsync(LiveEnvironment.Address));
        await LiveEnvironment.AcceptRefusalAsync(Ob.Wallets.BlockAsync(new WalletBlockRequest { Address = LiveEnvironment.Address }));
        await LiveEnvironment.AcceptRefusalAsync(Ob.Transfers.ToPersonalAsync(new TransferToPersonalRequest
        {
            Amount = "1",
            Currency = "USDT",
        }));
    }

    [LiveFact]
    [Step(11)]
    public async Task DocumentsWhenTheStandHasARenderer()
    {
        if (!_documentsEnabled)
        {
            return;
        }

        var statement = await Ob.Documents.StatementAsync(new PeriodQuery
        {
            From = "2026-01-01",
            To = "2026-12-31",
            Lang = "en",
        });
        Assert.Contains("pdf", statement.ContentType);
        Assert.NotEmpty(statement.Bytes);

        Assert.NotEmpty((await Ob.Documents.FeeScheduleAsync()).Bytes);
        Assert.Matches("csv|pdf", (await Ob.Documents.LedgerAsync(new PeriodQuery { Format = "csv" })).ContentType);

        if (_link?.ClaimToken is { } token)
        {
            var cheque = await Ob.PayoutLinks.ChequeAsync(new PayoutLinkChequeRequest { ClaimToken = token, Lang = "en" });
            Assert.Contains("pdf", cheque.ContentType);
        }

        var job = await Ob.Documents.CreateJobAsync(new DocumentsJobsRequest
        {
            Kind = "statement",
            Format = "csv",
            Lang = "en",
            From = "2026-01-01",
            To = "2026-08-25",
        });
        Assert.Equal(job.JobId, (await Ob.Documents.JobInfoAsync(job.JobId)).JobId);
        await LiveEnvironment.AcceptRefusalAsync(Ob.Documents.JobFileAsync(job.JobId));

        // A document_url is a signed public link: kind and id from the path, exp and sig from the query.
        var info = await Ob.Payments.GetAsync(_invoice!.Uuid);
        var url = new Uri(info.DocumentUrl);
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var query = url.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]), StringComparer.Ordinal);

        var document = await Pub.Documents.DownloadAsync(
            segments[^2],
            segments[^1],
            new SignedDocumentQuery { Exp = long.Parse(query["exp"]), Sig = query["sig"] });
        Assert.Contains("pdf", document.ContentType);
    }

    [LiveFact]
    [Step(12)]
    public async Task SandboxResetLast()
    {
        var reset = await Ob.Sandbox.ResetAsync();

        Assert.True(reset.InvoicesCancelled >= 0);
        Assert.True(reset.BalancesZeroed >= 0);
    }
}
