using Microsoft.Extensions.DependencyInjection;
using Oblodai.Resources;

namespace Oblodai.Tests.Readme;

/// <summary>
/// The C# the two READMEs print, written once here — compiled with the suite and executed by
/// <see cref="ReadmeTests"/> against a scripted gateway. The regions between a <c>// snippet:</c> and an
/// <c>// endsnippet</c> comment are the source of truth: the README blocks must match them character
/// for character (the README may add <c>using</c> lines on top).
/// </summary>
internal static class ReadmeSnippets
{
    /// <summary>README: "Where to get keys".</summary>
    internal static OblodaiClient Client(string publicId, string secret)
    {
        // snippet:client
        var oblodai = new OblodaiClient(new OblodaiOptions { PublicId = publicId, Secret = secret });
        // endsnippet
        return oblodai;
    }

    /// <summary>README: "Quick start" — an invoice.</summary>
    internal static async Task<PaymentView> QuickStartPayment(OblodaiClient oblodai)
    {
        // snippet:quickstart-payment
        var invoice = await oblodai.Payments.CreateAsync(
            amount: 25m,                // decimal, never double; sent as the string "25"
            currency: "USDT",           // what you price in: a fiat (USD, EUR, …) or a crypto asset
            network: "tron",            // omit to let the payer choose the network on the pay page
            orderId: "order-1001",      // your reference; the invoice is idempotent per order_id
            urlCallback: "https://shop.example/oblodai/webhook");

        Console.WriteLine($"{invoice.Url} {invoice.Address} {invoice.Status}"); // status: created
        // endsnippet
        return invoice;
    }

    /// <summary>README: "Quick start" — a payout with a caller-supplied idempotency key.</summary>
    internal static async Task<PayoutItem> QuickStartPayout(OblodaiClient oblodai)
    {
        // snippet:quickstart-payout
        var payout = await oblodai.Payouts.CreateAsync(
            address: "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx",
            amount: 10m,
            currency: "USDT",
            orderId: "payout-1",        // your reference; the payout is idempotent per order_id
            network: "tron",
            options: new RequestOptions { IdempotencyKey = "payout-1" });

        Console.WriteLine($"{payout.Uuid} {payout.Status}"); // pending → … → confirmed
        // endsnippet
        return payout;
    }

    /// <summary>README: "Quick start" — the request as a model.</summary>
    internal static async Task<PaymentView> RequestModel(OblodaiClient oblodai)
    {
        // snippet:request-model
        var request = new PaymentRequest { Amount = 25m, Currency = "USD", ToCurrency = "USDT" };
        var priced = await oblodai.Payments.CreateAsync(request);

        // Or from wire names, e.g. configuration; a double amount is refused (sdk.float_amount).
        var fromConfig = Model.From<PaymentRequest>(new Dictionary<string, object?>
        {
            ["amount"] = "25.00",
            ["currency"] = "EUR",
        });
        // endsnippet
        await oblodai.Payments.CreateAsync(fromConfig);
        return priced;
    }

    /// <summary>README: "Sandbox / testing".</summary>
    internal static async Task Sandbox(OblodaiClient sandbox)
    {
        // snippet:sandbox
        await sandbox.Sandbox.FaucetAsync(amount: 1000m, asset: "USDT");

        var invoice = await sandbox.Payments.CreateAsync(25m, "USDT", network: "tron", orderId: "sandbox-1");

        // No amount pays exactly what is due; repeating a txid adds confirmations instead of paying twice.
        var deposit = await sandbox.Sandbox.SimulateDepositAsync(invoiceId: invoice.Uuid);

        Console.WriteLine($"{deposit.Txid} {deposit.Confirmations}");
        // endsnippet
    }

    /// <summary>README: "Lists".</summary>
    internal static async Task<int> Lists(OblodaiClient oblodai)
    {
        // snippet:lists
        var firstPage = await oblodai.Payments.ListHistoryAsync(limit: 50);  // one request: Items + Paginate

        await foreach (var payment in oblodai.Payments.ListHistoryAsync(status: "paid"))
        {
            Console.WriteLine($"{payment.OrderId} {payment.Amount}");       // every page, fetched lazily
        }

        await foreach (var page in oblodai.Payouts.ListHistoryAsync(limit: 100).ByPageAsync())
        {
            Console.WriteLine($"{page.Items.Count} of {page.Paginate.Total}");
        }
        // endsnippet
        return firstPage.Items.Count;
    }

    /// <summary>README: "Long-running operations".</summary>
    internal static async Task<BatchInfoResponse> LongRunning(OblodaiClient oblodai, IReadOnlyList<PayoutRequest> payouts)
    {
        // snippet:long-running
        var submitted = await oblodai.Batches.CreatePayoutAsync(payouts, onError: BatchOnError.Continue);
        var batch = await oblodai.Batches.WaitAsync(submitted);            // polls until completed/stopped
        Console.WriteLine($"{batch.Status}: {batch.Succeeded} ok, {batch.Failed} failed");

        var job = await oblodai.Documents.CreateJobAsync(DocumentJobKind.Statement, from: "2026-01-01", to: "2026-06-30");
        var ready = await oblodai.Documents.WaitAsync(job);                // done, failed or expired
        if (ready.Status == DocumentJobStatus.Done)
        {
            var file = await oblodai.Documents.DownloadAsync(ready);
            await file.WriteToAsync(Path.Combine(Path.GetTempPath(), file.Filename ?? "statement.pdf"));
        }
        // endsnippet
        return batch;
    }

    /// <summary>README: "Webhooks".</summary>
    internal static bool Webhook(byte[] rawBody, IReadOnlyDictionary<string, string> headers, string endpointSecret)
    {
        // snippet:webhook
        var delivery = WebhookVerifier.VerifyDelivery(
            rawBody,                                        // the raw request bytes, not a re-serialized parse
            headers,
            new WebhookVerifyOptions { Secret = endpointSecret });

        if (delivery.IsTest)
        {
            return true;                                    // a rehearsal: signed, but no money moved
        }

        switch (delivery.Event)
        {
            case PaymentWebhook payment when Statuses.IsPaymentPaid(payment.Status):
                Console.WriteLine($"paid: {payment.OrderId} {payment.PaymentAmount} {payment.PayerCurrency}");
                break;
            case PayoutWebhook payout:
                Console.WriteLine($"payout {payout.Uuid}: {payout.Status}");
                break;
        }
        // endsnippet
        return false;
    }

    /// <summary>README: "Errors".</summary>
    internal static async Task<string?> Errors(OblodaiClient oblodai)
    {
        // snippet:errors
        try
        {
            await oblodai.Payouts.CreateAsync("TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx", 999999m, "USDT", "payout-2");
        }
        catch (ConflictException error) when (error.Code == ErrorCode.PayoutInsufficientFunds.Value)
        {
            Console.WriteLine(error.Message);   // [payout.insufficient_funds] … (request_id=…)
            return error.RequestId;             // quote it to support
        }
        // endsnippet
        return null;
    }

    /// <summary>README: "Per-call options, raw responses and hooks".</summary>
    internal static async Task<string> Options(OblodaiClient oblodai)
    {
        // snippet:options
        var info = await oblodai.Payments.GetInfoAsync(
            orderId: "order-1001",
            options: new RequestOptions
            {
                Timeout = TimeSpan.FromSeconds(5),          // per attempt
                MaxRetries = 0,                             // this call only
                RequestId = "checkout-7f3a",                // X-Request-ID, to find it in our logs
                ExtraHeaders = new Dictionary<string, string> { ["X-Shop"] = "eu-1" },
            });

        var raw = await oblodai.WithRawResponseAsync(c => c.Account.GetBalanceAsync());
        Console.WriteLine($"{raw.Status} {raw.RequestId} {raw.Value.Balance}");

        using var patient = oblodai.WithOptions(o => o with { Deadline = TimeSpan.FromMinutes(5) });
        // endsnippet
        return raw.RequestId + info.Uuid;
    }

    /// <summary>README: "Per-call options, raw responses and hooks" — hooks.</summary>
    internal static OblodaiClient Hooks(string publicId, string secret, HttpClient http)
    {
        // snippet:hooks
        var observed = new OblodaiClient(new OblodaiOptions
        {
            PublicId = publicId,
            Secret = secret,
            Hooks = new Hooks
            {
                OnRequest = r => Console.WriteLine($"→ {r.OperationId} #{r.Attempt} {r.RequestId}"),
                OnResponse = r => Console.WriteLine($"← {r.Status} in {r.Elapsed.TotalMilliseconds} ms"),
            },
        }, http);
        // endsnippet
        return observed;
    }

    /// <summary>README: "Configuration" — the IHttpClientFactory wiring.</summary>
    internal static void Container(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        // snippet:di
        services.AddHttpClient("oblodai")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton(provider => new OblodaiClient(
            new OblodaiOptions(), // OBLODAI_PUBLIC_ID / OBLODAI_SECRET from the environment
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("oblodai")));
        // endsnippet
    }
}
