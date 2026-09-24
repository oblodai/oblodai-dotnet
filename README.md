<div align="center">

<a href="https://oblodai.com">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/oblodai/.github/main/brand/logo-white.svg">
    <img src="https://raw.githubusercontent.com/oblodai/.github/main/brand/logo-black.svg" alt="oblodai" height="52">
  </picture>
</a>

<h3>Official .NET / C# SDK for the <a href="https://oblodai.com">oblodai</a> payment gateway</h3>

Payments, payouts, payment links, splits, static wallets, webhooks — one API key.

<img src="https://img.shields.io/badge/nuget-Oblodai%202.0.0-004880?style=flat-square" alt="nuget">
<img src="https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4?style=flat-square" alt=".NET 8 | 10">
<a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-000000?style=flat-square" alt="License: MIT"></a>

[Documentation](https://docs.oblodai.com) · [Dashboard](https://my.oblodai.com) · [Читать по-русски →](README.ru.md)

</div>

---

The official .NET / C# SDK for the **Oblodai** payment gateway: accepting payments, payouts, bulk
operations (batches), payment links, payout links (crypto cheques), splits, static wallets,
transfers, documents, webhooks. Request signing, typed models, typed errors, idempotency and retries
— out of the box. .NET 8 or 10 and the base class library only (`HttpClient` + `System.Text.Json`):
**zero third-party dependencies**. Every operation of the gateway's OpenAPI contract has a method
here, generated from `openapi.json` by the gateway's own generator — nothing that describes the API
is written by hand.

> **Base URL.** Defaults to `https://api.oblodai.com`. Override `BaseUrl` and supply your own keys
> at initialisation if needed. The scheme must be `https://`; plain `http://` is accepted only for
> loopback (`http://127.0.0.1:8095`) or with the explicit allow-insecure option
> (`AllowInsecureBaseUrl = true`, or `OBLODAI_ALLOW_INSECURE=1`).

## Installation

```bash
dotnet add package Oblodai --version 2.0.0
```

.NET 8 or newer (the package carries `net8.0` and `net10.0` builds). Everything a caller needs is in
the `Oblodai` namespace — the client, the request and response models, the vocabularies
(`PaymentStatus`, `ErrorCode`, …) and the errors; the resource classes live in `Oblodai.Resources`.
Webhook verification (`WebhookVerifier`) needs no client and no API key. Coming from 1.3? Read
[MIGRATION-2.0.md](MIGRATION-2.0.md).

## Where to get keys

A merchant has **one API key**, issued in the [dashboard](https://my.oblodai.com) → **API keys**: a
public id `oblodai_<hex>` and a secret `oblodai_live_<hex>`. It signs every route that needs a
signature — invoices, payouts, refunds, links, splits, wallets, settings, documents. There is
nothing to choose per call. A sandbox pair (`test_oblodai_<hex>` / `oblodai_test_<hex>`) drives a
chainless copy of the gateway with test money.

```csharp
using Oblodai;

var oblodai = new OblodaiClient(new OblodaiOptions { PublicId = publicId, Secret = secret });
```

The environment fallback is `OBLODAI_PUBLIC_ID` / `OBLODAI_SECRET`, so `new OblodaiClient()` is
enough in a configured container. One client per key; it is thread-safe — share it.

## Quick start

Every method is `client.Resource.MethodAsync(…)`: the request as **named arguments** (or as the
request model), then `RequestOptions? options = null` and `CancellationToken cancellationToken =
default`. Create an invoice:

```csharp
var invoice = await oblodai.Payments.CreateAsync(
    amount: 25m,                // decimal, never double; sent as the string "25"
    currency: "USDT",           // what you price in: a fiat (USD, EUR, …) or a crypto asset
    network: "tron",            // omit to let the payer choose the network on the pay page
    orderId: "order-1001",      // your reference; the invoice is idempotent per order_id
    urlCallback: "https://shop.example/oblodai/webhook");

Console.WriteLine($"{invoice.Url} {invoice.Address} {invoice.Status}"); // status: created
```

Send money out with the same key:

```csharp
var payout = await oblodai.Payouts.CreateAsync(
    address: "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx",
    amount: 10m,
    currency: "USDT",
    orderId: "payout-1",        // your reference; the payout is idempotent per order_id
    network: "tron",
    options: new RequestOptions { IdempotencyKey = "payout-1" });

Console.WriteLine($"{payout.Uuid} {payout.Status}"); // pending → … → confirmed
```

The request can also be passed as its model — every request type is a record with the wire's field
names in `PascalCase`, `required` where the contract insists — or built from a dictionary with the
wire names:

```csharp
var request = new PaymentRequest { Amount = 25m, Currency = "USD", ToCurrency = "USDT" };
var priced = await oblodai.Payments.CreateAsync(request);

// Or from wire names, e.g. configuration; a double amount is refused (sdk.float_amount).
var fromConfig = Model.From<PaymentRequest>(new Dictionary<string, object?>
{
    ["amount"] = "25.00",
    ["currency"] = "EUR",
});
```

Runnable programs live in [`examples/`](examples): `AcceptPayment`, `Payout`, `Sandbox`,
`WebhookReceiver` — the suite executes each of them against a scripted gateway.

## Sandbox / testing

A sandbox key drives a chainless copy of the gateway: fake balance from a faucet, simulated
deposits, real webhooks. The business endpoints behave exactly as they do live — only the key
changes, and a live key on a sandbox route is refused.

```csharp
await sandbox.Sandbox.FaucetAsync(amount: 1000m, asset: "USDT");

var invoice = await sandbox.Payments.CreateAsync(25m, "USDT", network: "tron", orderId: "sandbox-1");

// No amount pays exactly what is due; repeating a txid adds confirmations instead of paying twice.
var deposit = await sandbox.Sandbox.SimulateDepositAsync(invoiceId: invoice.Uuid);

Console.WriteLine($"{deposit.Txid} {deposit.Confirmations}");
```

- `Sandbox.FaucetAsync` credits test money; `RequestOptions.IdempotencyKey` is its own
  `idempotency_key` field, so a retry never tops up twice.
- `Sandbox.SimulateDepositAsync` pays an invoice: no `amount` pays exactly what is due, anything else
  produces an under- or overpayment, and fewer `confirmations` than required exercises the pending →
  confirmed transition.
- `Sandbox.ListWebhooksAsync` lists the deliveries with their payloads and
  `Sandbox.ReplayWebhookAsync(deliveryId)` re-sends one.
- `Webhooks.SendTestPaymentAsync` (and `…Payout`, `…Wallet`, `…Conversion`) rehearses a delivery
  against any receiver: it is signed exactly like a real event and carries `test: true`.
- `Sandbox.ResetAsync` cancels the store's open invoices and zeroes its balances.

## Method overview

The whole merchant surface of the contract. The table below is written by the generator; the names are
pinned in [`names.lock`](names.lock): a regeneration that would rename or drop one fails as a breaking
change, a new one is added to the lock by the generator. Long-running operations also get `WaitAsync`
(and `DownloadAsync` when the job has a file) — see below.

<!-- sdkgen:methods -->
16 resources, 120 methods.

| Resource | Methods |
| --- | --- |
| `Payments` | `CreateAsync` · `GetInfoAsync` · `GetQrAsync` · `ListHistoryAsync` · `ListServicesAsync` · `CancelAsync` · `SendEmailAsync` · `SetCheckoutConfigAsync` · `GetCheckoutConfigAsync` · `GetAmlLinksAsync` · `ResolveAsync` |
| `PaymentLinks` | `CreateAsync` · `ListAsync` · `GetAsync` · `ToggleAsync` |
| `Refunds` | `PaymentAsync` · `BlockedWalletAsync` |
| `Payouts` | `CreateAsync` · `CreateMassAsync` · `GetInfoAsync` · `ListHistoryAsync` · `CalculateAsync` · `ValidateAsync` · `CancelAsync` · `ApproveAsync` · `ListServicesAsync` · `TransferToPersonalAsync` · `TransferToUserAsync` · `CreateTransferBatchAsync` |
| `PayoutLinks` | `CreateAsync` · `CreateBatchAsync` · `ListAsync` · `GetAsync` · `CancelAsync` · `GetPayoutClaimAsync` · `ClaimPayoutAsync` |
| `Batches` | `CreatePaymentAsync` · `CreateRefundAsync` · `CreatePayoutAsync` · `GetInfoAsync` |
| `Splits` | `CreateRuleAsync` · `ListRulesAsync` · `DeleteRuleAsync` · `SetConfigAsync` · `GetConfigAsync` · `SetRecipientOptInAsync` · `GetRecipientOptInAsync` |
| `Wallets` | `CreateAsync` · `BlockAsync` · `GetQrAsync` |
| `Account` | `GetBalanceAsync` · `GetSummaryAsync` · `ListExchangeRatesAsync` |
| `Webhooks` | `ResendPaymentAsync` · `RegisterAsync` · `ListDeliveriesAsync` · `RequeueDeliveryAsync` · `SendLegacyTestAsync` · `SendTestPaymentAsync` · `SendTestWalletAsync` · `SendTestPayoutAsync` · `SendTestConversionAsync` · `RotateSecretAsync` · `SetActiveAsync` |
| `Settings` | `SetAccuracyAsync` · `GetAccuracyAsync` · `SetAutoRefundAsync` · `GetAutoRefundAsync` · `SetDiscountAsync` · `ListDiscountsAsync` · `ListApiLogAsync` · `GetAutoConvertAsync` · `SetAutoConvertAsync` · `SetAcceptedCurrenciesAsync` · `ListAcceptedCurrenciesAsync` · `SetPayoutFeeConfigAsync` · `GetPayoutFeeConfigAsync` · `SetRefundFeeConfigAsync` · `GetRefundFeeConfigAsync` · `SetPaymentFeeConfigAsync` · `GetPaymentFeeConfigAsync` · `SetAutoWithdrawRuleAsync` · `ListAutoWithdrawRulesAsync` · `DeleteAutoWithdrawRuleAsync` · `ConfigureVrcsAsync` |
| `ApiAllowlist` | `ListAsync` · `AddEntryAsync` · `RemoveEntryAsync` · `SetEnabledAsync` |
| `Referrals` | `GetInfoAsync` |
| `Documents` | `GetSignedAsync` · `GetBalanceAsync` · `GetFeesAsync` · `GetLedgerAsync` · `GetSplitAsync` · `GetPayoutLinkChequeAsync` · `GetStatementAsync` · `GetBatchAsync` · `GetPaymentLinkAsync` · `GetWalletStatementAsync` · `GetReferralsAsync` · `CreateJobAsync` · `GetJobAsync` · `DownloadJobFileAsync` |
| `Checkout` | `GetSourceOfFundsFormAsync` · `SubmitSourceOfFundsAsync` · `GetPublicPaymentLinkAsync` · `PaymentLinkAsync` · `ListCurrenciesAsync` · `GetAsync` · `SelectMethodAsync` · `StartOnrampAsync` · `GetOnrampAsync` · `GetQrAsync` |
| `Sandbox` | `OnboardStoreAsync` · `FaucetAsync` · `SimulateDepositAsync` · `ResetAsync` · `ListWebhooksAsync` · `ReplayWebhookAsync` |
<!-- /sdkgen:methods -->

`Checkout` is the payer-facing side (no credentials). Document routes answer outside the JSON
envelope and return `FileResult { Bytes, ContentType, Filename }` with `WriteToAsync(path)`.
Cancelling the `CancellationToken` throws `OperationCanceledException`, not an SDK error.

### Lists

A list method returns a `PagePromise<T>` that has requested nothing yet: `await` it for one page,
`await foreach` it to walk every item across pages, `ByPageAsync()` to walk page by page,
`AllAsync(max)` to collect.

```csharp
var firstPage = await oblodai.Payments.ListHistoryAsync(limit: 50);  // one request: Items + Paginate

await foreach (var payment in oblodai.Payments.ListHistoryAsync(status: "paid"))
{
    Console.WriteLine($"{payment.OrderId} {payment.Amount}");       // every page, fetched lazily
}

await foreach (var page in oblodai.Payouts.ListHistoryAsync(limit: 100).ByPageAsync())
{
    Console.WriteLine($"{page.Items.Count} of {page.Paginate.Total}");
}
```

### Long-running operations

Batches and document jobs are accepted at once and finish later. `WaitAsync` polls the operation
until its status is terminal and returns the last answer — a `failed` job is returned, not thrown,
so it is inspected like a finished one; a wait that runs out (10 minutes by default) is
`sdk.wait_timeout`.

```csharp
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
```

### Statuses, vocabularies, money

- Payment: `select → created → confirm_check → paid | paid_over | wrong_amount | expired | cancelled`;
  `Statuses.IsPaymentPaid(status)` is true for `paid`/`paid_over`. Payout: `pending → approved →
  awaiting_cosign → broadcasting → sent → confirmed | failed | cancelled`.
- Vocabularies (`PaymentStatus`, `ErrorCode`, `BatchStatus`, …) are string-backed
  `readonly record struct`s: compare with the constants (`PaymentStatus.Paid`); a value newer than
  this SDK still parses — `status.IsKnown` is false and `status.Value` holds it. A field the SDK does
  not know lands in the model's `Extra`.
- Money is `decimal` in the models and a decimal string on the wire (`25.10m` ↔ `"25.10"`, scale
  kept). A `double` does not compile where money is expected; in a dictionary body it is refused
  with `sdk.float_amount` before anything is sent. `Money.Of`, `Money.Parse` and `Money.Format`
  convert; `Money.Add`, `Money.Compare`, … work on strings at arbitrary precision for amounts wider
  than `decimal`.

## Webhooks

`Webhooks.RegisterAsync(url)` sets (or replaces) the endpoint and returns the signing secret — shown
once, so store it where the receiver can read it. Verification needs no client and no API key:

```csharp
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
```

Verify over the **raw** bytes — a re-serialized parse will not match. The MAC is checked **before**
the timestamp (default window 300 s, `ToleranceSeconds = 0` disables it), so the window cannot be
probed by an unauthenticated sender. The event is the generated model of its family —
`PaymentWebhook`, `PayoutWebhook`, `WalletWebhook`, `ConversionWebhook` — or `UnknownWebhookEvent`
for a family added later (`WebhookVerifier.IsKnownEvent`). Answer 4xx **only** to a
`SignatureException`; a delivery that verified but cannot be read is `WebhookPayloadException`
(`webhook.bad_payload`) — answer 5xx, the gateway will retry it. Deduplicate on
`delivery.EventId` (`X-Webhook-Event-Id`, stable per state), drop out-of-order deliveries with
`WebhookVerifier.IsStale(delivery.Event, lastSequence)`, and after `Webhooks.RotateSecretAsync`
keep the old secret in `PreviousSecret` for at least 26 hours.

## Errors

Every failure is an `OblodaiException`. Its `Message` reads `[code] text (request_id=…)`, so a log
line alone is enough to find the call on our side; branch on `Code` — a stable `family.reason`
string, `ErrorCode.*.Value` in code — never on the text (`Description` holds the text alone).

```csharp
try
{
    await oblodai.Payouts.CreateAsync("TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx", 999999m, "USDT", "payout-2");
}
catch (ConflictException error) when (error.Code == ErrorCode.PayoutInsufficientFunds.Value)
{
    Console.WriteLine(error.Message);   // [payout.insufficient_funds] … (request_id=…)
    return error.RequestId;             // quote it to support
}
```

| Exception | HTTP | When |
| --- | --- | --- |
| `ValidationException` | 400 | malformed request or a business rule; `Field` names the culprit |
| `AuthenticationException` | 401 | bad signature, unknown key, clock skew, IP not allow-listed |
| `PermissionException` | 403 | valid key, not allowed here |
| `NotFoundException` | 404 | no such object for this merchant |
| `ConflictException` / `IdempotencyConflictException` | 409 | a state conflict / same key, different body |
| `RateLimitException` | 429 | rate limited; `RetryAfter` is set |
| `UnavailableException` / `InternalException` | 503 / 5xx | the gateway or a dependency failed |
| `TransportException` | — | no response: DNS, TCP, TLS, timeout (`transport.*`), or a wait ran out |
| `ConfigException` | — | refused before sending (`sdk.*`: options, credentials, `sdk.float_amount`) |
| `ContractException` / `WebhookPayloadException` | — | the answer (or a verified delivery) is not the documented shape |
| `SignatureException` | — | webhook verification failed |

Fields: `Code`, `Description`, `HttpStatus`, `Retryable` (authoritative — the SDK already retried
what it should), `RetryAfter`, `RequestId`, `Field`, `Synthetic` (the answer came from a proxy).

## Per-call options, raw responses and hooks

```csharp
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
```

- `RequestOptions`: `IdempotencyKey` (generated automatically on routes the gateway deduplicates,
  reused on every retry, refused with `sdk.idempotency_unsupported` where it would mean nothing),
  `Timeout` (per attempt), `MaxRetries`, `ExtraHeaders`, `RequestId` (`X-Request-ID`; a fresh one is
  generated for every call and kept on all its attempts).
- `WithRawResponseAsync` returns the value together with `Status`, `Headers` and `RequestId`
  (the gateway's, else the one sent); for a list, the first page. An error status still throws.
- `WithOptions` returns a client with some options changed; it shares the connection pool.

Hooks see every attempt — for metrics, tracing and logs; the signature is redacted in what they get:

```csharp
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
```

Retries: an error is retried only when the API says `retryable: true`; an answer with no envelope (a
proxy 502/503) and a transport failure are retried only on retry-safe routes (`x-retry-safe` in the
contract) and on writes with an idempotency key. `Retry-After` wins over the computed backoff; the
whole call is bounded by `Deadline`. Clock skew is corrected from the `Date` header after a 401 that
looks like one; redirects are never followed; bodies are capped (8 MiB JSON, 64 MiB documents).

## Configuration

| Option | What it does |
| --- | --- |
| `PublicId` / `Secret` | the merchant's API key pair; it signs every signed route |
| `BaseUrl` | the API origin; a path prefix is kept |
| `AllowInsecureBaseUrl` | permit plain `http://` for a non-loopback host |
| `AdminToken` | onboarding admin token of a self-hosted gateway (`Sandbox.OnboardStoreAsync` only) |
| `Timeout` | per-attempt timeout (default 30 s) |
| `Deadline` | budget for one call including retries and pauses (default 90 s) |
| `Retry` | retry policy; `new RetryOptions { MaxRetries = 0 }` disables retries |
| `Hooks` | `OnRequest` / `OnResponse` callbacks on every attempt |
| `Logger` | structured logger for the SDK's diagnostics |
| `Headers` | headers on every request (reserved names are ignored) |
| `Clock` | signing clock; injectable for tests |
| `TimeProvider` | time source for retry pauses, deadlines and waits; injectable for tests |

| Environment variable | Meaning |
| --- | --- |
| `OBLODAI_PUBLIC_ID` / `OBLODAI_SECRET` | the API key |
| `OBLODAI_ADMIN_TOKEN` | onboarding admin token of a self-hosted gateway |
| `OBLODAI_BASE_URL` | API origin (default `https://api.oblodai.com`) |
| `OBLODAI_LOG` | `debug` \| `info` \| `warn` \| `error` — enables the console logger |
| `OBLODAI_ALLOW_INSECURE` | `1` permits a plain `http://` base URL |

Explicit options win over the environment; half a key pair is refused with `sdk.bad_config`.
Secrets never print: options redact the key secret and the admin token, and a model prints the
fields whose names look secret (`secret`, `token`, `passcode`, `claim_url`, …) as `[redacted]` —
the property still holds the value. The client takes an externally managed `HttpClient`, so it fits
`IHttpClientFactory`:

```csharp
services.AddHttpClient("oblodai")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
    .ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
services.AddSingleton(provider => new OblodaiClient(
    new OblodaiOptions(), // OBLODAI_PUBLIC_ID / OBLODAI_SECRET from the environment
    provider.GetRequiredService<IHttpClientFactory>().CreateClient("oblodai")));
```

## Generated code

`src/Oblodai/Generated/*.g.cs` — routes, vocabularies, models and resource methods — is generated
from the gateway's `services/core/api/openapi.json` by `tools/sdkgen` in the backend repository
(`make sdk` there regenerates all eight SDKs) and is never edited by hand. The runtime around it
(transport, signing, retries, errors, pagination, webhooks, waiters) is hand-written and stable.
`make ci` fails when the generated files are not what the generator makes of the contract.

## Development

```bash
make ci      # drift check, build (-warnaserror), dotnet format, tests, conformance, package
make test    # just the tests
make live    # the live tier against a running gateway: OBLODAI_LIVE_URL=http://127.0.0.1:8095
```

The .NET toolchain runs in docker (`scripts/dotnet.sh`, SDK 10); the drift check needs Go and the
backend checkout (`OBLODAI_BACKEND`, default `../oblodai-backend`), which also supplies the shared
conformance suite (`tools/sdkgen/conformance`): signing vectors, retries, money, forward
compatibility. Read [AGENTS.md](AGENTS.md) for the same surface in one page, written for coding
agents; [CHANGELOG.md](CHANGELOG.md) for what changed; [MIGRATION-2.0.md](MIGRATION-2.0.md) for the
move from 1.3.

## License

MIT — see [LICENSE](LICENSE).
