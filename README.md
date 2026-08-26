<div align="center">

<a href="https://oblodai.com">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/oblodai/.github/main/brand/logo-white.svg">
    <img src="https://raw.githubusercontent.com/oblodai/.github/main/brand/logo-black.svg" alt="oblodai" height="52">
  </picture>
</a>

<h3>Official .NET / C# SDK for the <a href="https://oblodai.com">oblodai</a> payment gateway</h3>

Payments, payouts, payment links, splits, static wallets, webhooks — one API key.

<img src="https://img.shields.io/badge/nuget-Oblodai%201.3.0-004880?style=flat-square" alt="nuget">
<a href="https://github.com/oblodai/oblodai-dotnet/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/oblodai/oblodai-dotnet/ci.yml?branch=main&style=flat-square&label=CI" alt="CI"></a>
<img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square" alt=".NET 8.0">
<a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-000000?style=flat-square" alt="License: MIT"></a>

[Documentation](https://docs.oblodai.com) · [Dashboard](https://my.oblodai.com) · [Читать по-русски →](README.ru.md)

</div>

---

The official .NET / C# SDK for the **Oblodai** payment gateway: accepting payments, payouts, bulk
operations (batches), payment links, payout links (crypto cheques), splits, static wallets,
transfers, webhooks. Request signing, response parsing, typed errors, idempotency and retries — out
of the box. .NET 8+ and the base class library only (`HttpClient` + `System.Text.Json`): **zero
third-party dependencies**, at runtime and in the tests alike; every route the gateway exposes has a
method here, generated from the gateway's own contract snapshot and verified against golden
responses recorded from a live gateway.

> **Base URL.** Defaults to `https://api.oblodai.com`. Override `BaseUrl` and supply your own keys
> at initialisation if needed. The scheme must be `https://`; plain `http://` is accepted only for
> loopback (`http://127.0.0.1:8095`) or with the explicit allow-insecure option
> (`AllowInsecureBaseUrl = true`, or `OBLODAI_ALLOW_INSECURE=1`).

## Installation

```bash
dotnet add package Oblodai --version 1.3.0
```

.NET 8 or newer. The package is `Oblodai`; the client lives in the `Oblodai` namespace, the
generated request records and vocabularies in `Oblodai.Contract`, the response models in
`Oblodai.Models` and the resource namespaces in `Oblodai.Resources`. Webhook verification
(`WebhookVerifier`) needs no client and no API key. Nothing else is pulled in: the SDK and its test
suite reference the base class library only.

## Where to get keys

A merchant has **one API key**, issued in the [dashboard](https://my.oblodai.com) → **API keys**: a
public id `oblodai_<hex>` and a secret `oblodai_live_<hex>`. It signs every route that needs a
signature — invoices, payouts, refunds, links, splits, wallets, settings, documents. There is
nothing to choose per call.

A sandbox pair (public id `test_oblodai_<hex>`, secret `oblodai_test_<hex>`) comes from sandbox
onboarding and drives a chainless copy of the gateway; it is the same one key, against test money.

```csharp
using Oblodai;

using var oblodai = new OblodaiClient(new OblodaiOptions { PublicId = publicId, Secret = secret });
```

The environment fallback is `OBLODAI_PUBLIC_ID` / `OBLODAI_SECRET`, so `new OblodaiClient()` is
enough in a configured container. Merchant provisioning (`Merchants.CreateAsync`,
`Merchants.CreateSandboxAsync`) is unsigned — a self-hosted gateway gates it with an **onboarding
admin token** (`AdminToken`, or `OBLODAI_ADMIN_TOKEN`), sent as `X-Admin-Token`, and that token is
for provisioning only.

> **Legacy split keys.** Merchants onboarded long ago may still hold an old `oblodai_pk_<hex>` /
> `oblodai_wk_<hex>` pair, where one key signed money-in and the other money-out. That is the only
> case in which a call can come back 403 `merchant.wrong_key_kind`; the fix is to issue a current
> API key in the dashboard and use it for everything.

## Quick start

Every method is `…Async` and takes `RequestOptions? options = null, CancellationToken
cancellationToken = default` last. Create an invoice:

```csharp
using Oblodai;
using Oblodai.Contract;

using var oblodai = new OblodaiClient(); // OBLODAI_PUBLIC_ID / OBLODAI_SECRET from the environment

var invoice = await oblodai.Payments.CreateAsync(new PaymentRequest
{
    Amount = "25",              // amounts are decimal strings, never floats
    Currency = "USDT",          // what you price in: a fiat (USD, EUR, …) or a crypto asset
    Network = Network.Tron,     // omit to let the payer choose the network on the pay page
    OrderId = "order-1001",     // your reference; the invoice is idempotent per order_id
    UrlCallback = "https://shop.example/oblodai/webhook",
});

Console.WriteLine($"{invoice.Url} {invoice.Address} {invoice.Status}"); // status: created
```

To price in fiat, set `Amount = "25", Currency = "USD", ToCurrency = "USDT"` — `Currency` is what
you charge, `ToCurrency` the asset the payer sends. Send money out with the same key:

```csharp
var payout = await oblodai.Payouts.CreateAsync(
    new PayoutRequest
    {
        Address = "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx",
        Amount = "10",
        Currency = "USDT",
        Network = Network.Tron,
        OrderId = "payout-1",   // your reference; the payout is idempotent per order_id
    },
    new RequestOptions { IdempotencyKey = "payout-1" });

Console.WriteLine($"{payout.Uuid} {payout.Status}"); // pending → … → confirmed
```

Runnable programs live in [`examples/`](examples): `AcceptPayment`, `Payout`, `Sandbox`,
`WebhookReceiver`.

## Sandbox / testing

A sandbox key drives a chainless copy of the gateway: fake balance from a faucet, simulated
deposits, real webhooks. The business endpoints behave exactly as they do live — only the key
changes, and a live key on a sandbox route is refused.

```csharp
using Oblodai;
using Oblodai.Contract;

using var sandbox = new OblodaiClient(new OblodaiOptions { PublicId = testPublicId, Secret = testSecret });

await sandbox.Sandbox.FaucetAsync(new SandboxFaucetRequest { Asset = "USDT", Amount = "1000" });

var invoice = await sandbox.Payments.CreateAsync(new PaymentRequest
{
    Amount = "25", Currency = "USDT", Network = Network.Tron, OrderId = "sandbox-1",
});

// No Amount pays exactly what is due; repeating a Txid adds confirmations instead of paying twice.
var deposit = await sandbox.Sandbox.DepositAsync(new SandboxDepositRequest { InvoiceId = invoice.Uuid });

Console.WriteLine($"{deposit.Txid} {deposit.Confirmations}");
```

- `Sandbox.FaucetAsync` credits test money. Give it an `IdempotencyKey` when a retry
  must not top up twice.
- `Sandbox.DepositAsync` pays an invoice: no `Amount` pays exactly what is due, anything else
  produces an under- or overpayment, and `Confirmations` fewer than required exercises the pending →
  confirmed transition. Repeating a `Txid` adds confirmations instead of paying twice.
- `Sandbox.WebhooksAsync` lists the deliveries with their payloads — what your receiver would have
  been sent — and `Sandbox.ReplayAsync(deliveryId)` re-sends a terminal one.
- `Webhooks.TestAsync(kind, request)` rehearses a delivery against any receiver, sandbox or live: it
  is signed exactly like a real event and carries `test: true` in the signed body (and
  `X-Webhook-Test: true`). Check `info.IsTest` and never act on one as if money moved.
- `Sandbox.ResetAsync` cancels the store's open invoices and zeroes its balances.

## Method overview

16 namespaces, 107 routes — the whole merchant surface.

| Namespace      | Methods                                                                                                                                                                                        | Routes |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| `Payments`     | Create · Info/Get · Cancel · History/List · Batch · Qr · Services · SendEmail · Resend · PublicView · Select · PublicQr                                                                         | 12     |
| `Refunds`      | Create · Resolve · Batch                                                                                                                                                                        | 3      |
| `Payouts`      | Create · Validate · Calculate · Info/Get · Cancel · Approve · History/List · Mass · Batch · Services · Get/SetFeeConfig · Get/SetRefundFeeConfig                                                | 14     |
| `PayoutLinks`  | Create · Info/Get · List · Cancel · Batch · Cheque · ClaimPreview · Claim                                                                                                                       | 8      |
| `PaymentLinks` | Create · Info/Get · List · Toggle · PublicView · Checkout                                                                                                                                       | 6      |
| `Transfers`    | ToPersonal · ToUser · Batch                                                                                                                                                                     | 3      |
| `Batches`      | Info (asynchronous batch progress)                                                                                                                                                              | 1      |
| `Wallets`      | Create · Qr · Block · RefundBlockedDeposit                                                                                                                                                      | 4      |
| `Webhooks`     | Register · RotateSecret · Deliveries · Test (payment/payout/wallet) · TestLegacy                                                                                                                | 7      |
| `Documents`    | Statement · Ledger · BalanceCertificate · FeeSchedule · SplitReport · BatchReport · LinkReport · WalletStatement · ReferralsReport · CreateJob · JobInfo · JobFile · Download                    | 13     |
| `Splits`       | CreateRule · ListRules · DeleteRule · Get/SetConfig · Get/SetOptIn                                                                                                                               | 7      |
| `Settings`     | SetDiscount · ListDiscounts · Get/SetAccuracy · Get/SetAutoRefund · ListAccepted · SetAccepted · Get/SetPaymentFeeConfig · List/Set/DeleteAutoWithdraw · List/Add/Remove/EnableApiAllowlist      | 17     |
| `Account`      | Balance · Referral · Vrcs (read with no argument, set with one)                                                                                                                                 | 3      |
| `Catalog`      | Currencies · ExchangeRates                                                                                                                                                                      | 2      |
| `Sandbox`      | Faucet · Deposit · Webhooks · Replay · Reset                                                                                                                                                    | 5      |
| `Merchants`    | Create · CreateSandbox (provisioning; `AdminToken` on a self-hosted gateway)                                                                                                                    | 2      |

Lookups take a bare id, a lookup object, or the object you already hold:
`Payments.InfoAsync("uuid")`, `Payments.InfoAsync(new PaymentLookup { OrderId = "order-1001" })`,
`Payouts.CancelAsync(payout)`, `PayoutLinks.CancelAsync(link)`. Synchronous bulk calls
(`Payouts.MassAsync` ≤ 100, `PayoutLinks.BatchAsync` ≤ 500) answer per element with
`BatchElement<T> { Idx, Ok, Result, Message }`; asynchronous ones (`Payments.BatchAsync`,
`Payouts.BatchAsync`, `Refunds.BatchAsync`, `Transfers.BatchAsync`, ≤ 5000) are polled through
`Batches.InfoAsync`. Document routes answer outside the JSON envelope and return
`FileResult { Bytes, ContentType, Filename }`. Cancellation is yours: cancelling the
`CancellationToken` throws `OperationCanceledException`, not an SDK error, so an ASP.NET request
abort behaves the way the rest of your code expects.

### Lists

A list method returns a `PagePromise<T>` that has requested nothing yet: `await` it for one page,
`await foreach` it to walk every item across pages, `AllAsync(max)` to collect them.

```csharp
using Oblodai.Contract;
using Oblodai.Models;

Page<Payment> page = await oblodai.Payments.HistoryAsync(new PaymentHistoryRequest { Limit = 50 });
Console.WriteLine($"{page.Items.Count} of {page.Paginate.Total}, more: {page.Paginate.HasPages}");

await foreach (var payout in oblodai.Payouts.HistoryAsync(new PayoutHistoryRequest { Status = PayoutStatus.Confirmed }))
{
    Console.WriteLine(payout.Uuid);
}

List<Payout> refunds = await oblodai.Payouts.HistoryAsync(new PayoutHistoryRequest { Kind = "refund" }).AllAsync(1000);
```

### Statuses

- Payment: `select → created → confirm_check → paid | paid_over | wrong_amount | expired | cancelled`.
  `Statuses.IsPaymentPaid(status)` is true for `paid`/`paid_over`; `wrong_amount` (underpaid) waits
  for `Refunds.ResolveAsync(new PaymentResolveRequest { Uuid = …, Action = "accept" })`;
  `Statuses.IsPaymentFinal` covers the rest.
- Payout: `pending → approved → awaiting_cosign → broadcasting → sent → confirmed | failed | cancelled`.

Statuses, networks and the other vocabularies are string-backed `readonly record struct`s, not C#
enums: compare with the constants (`PaymentStatus.Paid`), and a value newer than this snapshot still
round-trips — `status.IsKnown` tells you whether the SDK documents it. Prefer webhooks for state
changes; poll `InfoAsync` only as a fallback.

### Money helpers

`Money.Add`, `Money.Subtract`, `Money.Compare`, `Money.AreEqual`, `Money.IsZero` — exact decimal
arithmetic on the string amounts the API uses. Never parse a wire amount into a `double`: USDT has 6
decimals, BTC 8 and ETH 18, and binary floating point holds none of them exactly. Never compare two
amounts as strings either — `"9" > "10"` is true as text and false as money. Anything that is not
`[-]digits[.digits]`, of at most 64 characters, is refused with `ConfigException` /
`sdk.bad_amount` — the SDK's own error, never a `FormatException`.

## Webhooks

`Webhooks.RegisterAsync(url)` sets (or replaces) the endpoint and returns the signing secret — shown
once, so store it where the receiver can read it. Verification needs no client and no API key:

```csharp
using Oblodai;

var info = WebhookVerifier.VerifyDelivery(rawBody, header, new WebhookVerifyOptions
{
    Secret = Environment.GetEnvironmentVariable("OBLODAI_WEBHOOK_SECRET")!,
});

if (info.IsTest) // a rehearsal delivery: signed like a live one, but no money moved
{
    return;
}

switch (info.Event)
{
    case PaymentEvent { Status.Value: "paid" } paid: MarkOrderPaid(paid.OrderId, info.Id); break;
    case PayoutEvent payout: Track(payout.Uuid, payout.Status); break;
    case WalletEvent deposit: Credit(deposit.Address, deposit.PaymentAmount); break;
}
```

Verify over the **raw** bytes — a re-serialized parse will not match. `rawBody` is the request body
as received and `header` a case-insensitive header lookup (an
`IReadOnlyDictionary<string, string>` overload exists too). The MAC is checked **before** the
timestamp, so the tolerance window cannot be probed by an unauthenticated sender.
`WebhookVerifyOptions` refuses an empty `Secret`, an empty `PreviousSecret` and a negative
`ToleranceSeconds` with `ConfigException` before any crypto runs; the default tolerance is 300
seconds and `ToleranceSeconds = 0` disables the freshness check. The signature header is accepted
trimmed and in either case; a `0x` prefix is not the encoding the gateway sends and is refused.

The receiver's status-code rule: answer 401 (or any 4xx) **only** when verification failed — a
forged or stale delivery, which arrives as `SignatureException`. A delivery that verified but cannot
be read is `WebhookPayloadException` / `webhook.bad_payload`, a *contract* error rather than a
signature one: answer 5xx, because the event is real and the gateway will retry it. An event family
this snapshot does not know arrives as `UnknownWebhookEvent` carrying the raw `type` instead of an
exception — `WebhookVerifier.IsKnownEvent` tells them apart, and `IsTest`, `IsStale` and
`IsKnownEvent` all work on it.

Rehearsal deliveries (`Webhooks.TestAsync`, sandbox) are signed exactly like live ones and carry
`test: true` in the body (and `X-Webhook-Test: true`): check `info.IsTest` (or
`WebhookVerifier.IsTestEvent(info.Event)`) and never act on one as if money moved. `info.Id`
(`X-Webhook-Id`) is stable across retries — use it to deduplicate;
`WebhookVerifier.IsStale(info.Event, lastSequence)` drops an out-of-order delivery. After
`Webhooks.RotateSecretAsync` keep the old secret in `PreviousSecret` for at least 26 hours:
deliveries queued before the rotation stay signed with it for their whole retry life.

## Errors

Every failure is an `OblodaiException` carrying the API's error envelope. Branch on `Code` — a
stable `family.reason` string — never on the message.

| Exception                      | HTTP          | When                                                            |
| ------------------------------ | ------------- | ---------------------------------------------------------------- |
| `ValidationException`          | 400           | malformed request or a business rule; `Field` names the culprit   |
| `AuthenticationException`      | 401           | bad signature, unknown key, clock skew, IP not allow-listed       |
| `PermissionException`          | 403           | valid key, not allowed here (a feature that is off for you)       |
| `NotFoundException`            | 404           | no such object for this merchant                                  |
| `ConflictException`            | 409           | a state conflict                                                  |
| `IdempotencyConflictException` | 409           | `idempotency.key_reused`: same key, different body                |
| `RateLimitException`           | 429           | rate limited; `RetryAfter` is set                                 |
| `UnavailableException`         | 503           | an upstream dependency is down; safe to retry after a pause       |
| `InternalException`            | other 5xx     | the gateway failed                                                |
| `ApiException`                 | anything else | an error status carrying an envelope                              |
| `TransportException`           | —             | no response at all: DNS, TCP, TLS, timeout, cancellation          |
| `ConfigException`              | —             | refused before sending: bad options, missing credentials          |
| `ContractException`            | —             | the answer is not the documented envelope                         |
| `WebhookPayloadException`      | —             | a delivery that verified but could not be read                    |
| `SignatureException`           | —             | webhook verification failed                                       |

Fields: `Code`, `Message`, `HttpStatus`, `Retryable` (authoritative — the SDK has already retried
what it should), `RetryAfter` (seconds), `RequestId` (quote it to support), `Field` (on 400s),
`Synthetic` (the answer came from a proxy, not the API), `Family`.

```csharp
using Oblodai;
using Oblodai.Contract;

try
{
    var payout = await oblodai.Payouts.CreateAsync(request);
    Console.WriteLine($"{payout.Uuid} {payout.Status}");
}
catch (OblodaiException error)
    when (error.Code is ErrorCodes.PayoutInsufficientFunds or ErrorCodes.PayoutFundsMaturing)
{
    ScheduleRetry(error.RetryAfter ?? 60); // retryable — the balance may still arrive
}
catch (OblodaiException error)
{
    Log(error.Code, error.RequestId); // the SDK already retried whatever was safe to retry
}
```

The catalogue is `ErrorCodes` — all 469 codes the gateway can answer with, shipped in the contract
snapshot and exposed as constants (`ErrorCodes.PayoutInsufficientFunds`) plus `ErrorCodes.All`.
Codes worth handling first: `payout.insufficient_funds` and `payout.funds_maturing` (both
retryable), `idempotency.key_reused`, `invoice.not_payable`, `payment.not_found`,
`merchant.bad_signature`, `request.rate_limited`. The SDK raises its own
families on top: `sdk.missing_credentials`, `sdk.bad_config`, `sdk.bad_idempotency_key`,
`sdk.idempotency_unsupported`, `sdk.bad_envelope`, `sdk.bad_path_param`, `sdk.bad_amount`,
`sdk.bad_header`, `sdk.response_too_large`, `transport.timeout|network|deadline`,
`webhook.bad_signature|stale_timestamp|missing_header|bad_payload`.

An error envelope is read field by field: a `retryable` that is not a boolean falls back to the
status, a `retry_after` that is a float, a numeric string or an absurd number is clamped into
`[0, 86400]` seconds, and a body with no usable `code` becomes a synthetic error carrying the HTTP
status. A malformed envelope never turns a retryable 503 into a parse crash. `error.ToJson()` keeps
the identity (code, message, status, request id) and drops the raw body, so a structured log cannot
leak what the body carried; `ToString()` keeps the stack trace and inner exception.

## Retries, idempotency and timeouts

- **Safe to repeat** is not guessed: whether a route may be re-sent is the gateway's own `safe` flag,
  read from `contract/contract.json`, and codegen fails on a snapshot that omits it.
- An error is retried only when the API says `retryable: true`. Answers with no API envelope (a
  proxy 502/503) and transport failures are retried only on read routes and keyed writes.
  `Retry-After` is honoured over the computed backoff.
- **Idempotency keys** are attached automatically on create-type routes — one per logical call,
  reused on every retry — so a timeout can never produce a second payout. Pass your own
  (`new RequestOptions { IdempotencyKey = … }`) to make retries safe across process restarts; on
  routes the gateway does not deduplicate (list methods included) the SDK refuses a key with
  `sdk.idempotency_unsupported` rather than let you believe a re-send is safe. A key that could not
  be sent as a header is `ConfigException` / `sdk.bad_idempotency_key`.
- **Per call:** `RequestOptions { IdempotencyKey, TimeoutMs, DeadlineMs, Headers }`
  — `Headers` are merged over the client's for this call only. **Per client:** `TimeoutMs` (per
  attempt, 30 s), `DeadlineMs` (attempts plus pauses, 90 s), `Retry = new RetryOptions
  { MaxRetries = 2, BaseDelayMs = 250, MaxDelayMs = 4000, MaxRetryAfterMs = 30000 }`;
  `new RetryOptions { MaxRetries = 0 }` disables retries. A `Retry-After` the gateway reports is kept
  up to a day, but the pause the SDK actually takes never exceeds `MaxRetryAfterMs`.
- **Clock skew** is corrected from the API's `Date` header after a 401 that looks like skew, and the
  correction is reverted when it does not help; an offset beyond 24 hours is implausible drift and is
  ignored.
- **Redirects are never followed**: a signed request must not be replayed against another origin, so
  the SDK notices a followed redirect (the response came back from a URL it did not send to) and
  fails the call rather than let your signature and body reach another host.
- **Body size caps**: 8 MiB on JSON routes, 64 MiB on document routes — an answer larger than that is
  `sdk.response_too_large`, a contract error, rather than an out-of-memory. The deadline covers the
  whole read, not just the first byte.
- **Reserved headers** win over `Headers`, compared case-insensitively: `X-Public-Id`,
  `X-Signature`, `X-Timestamp`, `Idempotency-Key`, `X-Admin-Token`, `Accept`, `User-Agent`,
  `Content-Type`, `Content-Length`, `Host`. A header carrying a CR, LF or non-ASCII byte is refused
  with `sdk.bad_header` before anything is signed.

## Configuration

| Option                          | What it does                                                                  |
| ------------------------------- | ------------------------------------------------------------------------------ |
| `PublicId` / `Secret`           | the merchant's API key pair; it signs every signed route                       |
| `BaseUrl`                       | the API origin; a path prefix is kept                                          |
| `AllowInsecureBaseUrl`          | permit plain `http://` for a non-loopback host                                 |
| `AdminToken`                    | onboarding admin token of a self-hosted gateway (provisioning routes only)     |
| `TimeoutMs`                     | per-attempt timeout (default 30000)                                            |
| `DeadlineMs`                    | budget for one call including retries and pauses (default 90000)               |
| `Retry`                         | retry policy; `new RetryOptions { MaxRetries = 0 }` disables retries           |
| `Logger`                        | structured logger for the SDK's diagnostics                                    |
| `Headers`                       | headers on every request (reserved names are ignored)                          |
| `Clock`                         | signing clock; injectable for tests                                            |

| Environment variable       | Meaning                                                          |
| -------------------------- | ------------------------------------------------------------------ |
| `OBLODAI_PUBLIC_ID`        | API key public id                                                  |
| `OBLODAI_SECRET`           | API key secret                                                     |
| `OBLODAI_ADMIN_TOKEN`      | onboarding admin token of a self-hosted gateway                    |
| `OBLODAI_BASE_URL`         | API origin (default `https://api.oblodai.com`)                     |
| `OBLODAI_LOG`              | `debug` \| `info` \| `warn` \| `error` — enables the console logger |
| `OBLODAI_ALLOW_INSECURE`   | `1` permits a plain `http://` base URL                             |

Explicit options win over the environment. Half a key pair (an id without its secret, or the other
way round) is refused when the options are resolved, with `sdk.bad_config`; missing credentials
surface later, on the first call that needs them.

**Secrets never print.** An API key secret, a webhook secret, a cheque passcode and a claim token or
URL are `[redacted]` in `ToString()` and in the default `System.Text.Json` path — both default paths
are overridden, so neither a log line nor a serialized dump can leak them. Reading the property
still gives you the value, and `OblodaiJson.SerializeWithSecrets(model)` is the one explicit way to
serialize the real thing — for the code that stores a secret or mails a claim URL, never for a log.

**Dependency injection.** The client takes an externally managed `HttpClient`, so it fits
`IHttpClientFactory`:

```csharp
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Oblodai;

services.AddHttpClient("oblodai").ConfigurePrimaryHttpMessageHandler(
    () => new SocketsHttpHandler { AllowAutoRedirect = false });

services.AddSingleton(sp => new OblodaiClient(
    new OblodaiOptions { PublicId = publicId, Secret = secret },
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("oblodai")));
```

The SDK applies its own per-attempt timeout and per-call deadline, so set `HttpClient.Timeout` to
`Timeout.InfiniteTimeSpan`. A finite one silently overrides both, and the SDK logs a warning quoting
your value and its own when it sees one.

**Self-hosted or local gateway.** `BaseUrl = "http://127.0.0.1:8095"` works out of the box; any
other plain-http host needs `AllowInsecureBaseUrl = true` (or `OBLODAI_ALLOW_INSECURE=1`). A path
prefix in the base URL is kept, so `https://gw.corp/oblodai` reaches
`https://gw.corp/oblodai/v1/payment` — and the signature covers the prefixed path.

## The contract snapshot

`contract/` is exported by the gateway's own test suite: the route registry (107 merchant routes,
each with its auth kind, idempotency wrapper and the gateway's own `safe` flag), request DTO schemas
with English field docs, every vocabulary and all 469 error codes, signing vectors, golden response
bodies recorded from a live gateway and real signed webhook deliveries. `src/Oblodai/Contract/*.g.cs`
is generated from it and is never edited by hand; `ContractVersion.CoreCommit`,
`ContractVersion.ExportedAt` and `ContractVersion.Hash` identify the snapshot in use. Codegen fails
rather than guess if a route ever arrives without its `safe` flag.

```bash
dotnet run --project tools/Codegen -- generate   # regenerate after refreshing contract/
dotnet run --project tools/Codegen -- check      # drift gate: fails when the generated files are stale
```

The contract tier of the suite is a completeness gate, not a sample: every one of the 107 routes must
have a method wired to the right path, auth gate and idempotency behaviour, and every recorded
response body must decode into a model whose fields match the wire key for key.

## Development

```bash
git clone https://github.com/oblodai/oblodai-dotnet && cd oblodai-dotnet
dotnet run --project tools/Codegen -- check          # the committed *.g.cs still match contract/
dotnet build -c Release -warnaserror                 # library, tests, tools and examples
dotnet test -c Release                               # unit + contract tiers (hermetic)
OBLODAI_LIVE_URL=http://127.0.0.1:8095 dotnet test   # adds the live tier against a running gateway
dotnet pack src/Oblodai/Oblodai.csproj -c Release
```

Source files stay under ~400 lines, and tests live next to what they test. Read
[AGENTS.md](AGENTS.md) for the same surface in one page, written for coding agents;
[CHANGELOG.md](CHANGELOG.md) for what changed; [MIGRATION-1.3.md](MIGRATION-1.3.md) for the move
from 1.2.

## License

MIT — see [LICENSE](LICENSE).
