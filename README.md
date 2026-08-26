# Oblodai .NET SDK

Official C# / .NET client for the [Oblodai](https://oblodai.com) crypto payment gateway: invoices,
payouts, refunds, payout links, static wallets, webhooks, documents — the whole merchant API, typed end
to end and verified against the gateway's own contract snapshot.

- .NET 8+, zero third-party dependencies (`HttpClient` + `System.Text.Json`).
- Every route the gateway exposes has a method here (107 of them); request records, vocabularies and
  all 471 error codes are generated from the gateway's own contract snapshot.
- Retries driven by the API's own `retryable` flag, automatic idempotency keys, clock-skew correction.
- `WebhookVerifier`: signature verification that needs no client and no API key.

```bash
dotnet add package Oblodai
```

## Start in the sandbox

Get your keys in the Oblodai dashboard. A **sandbox key** (`test_…`) drives a chainless copy of the
gateway — fake balance from a faucet, simulated deposits, real webhooks — so integrate against it first.

```csharp
using Oblodai;
using Oblodai.Contract;

using var oblodai = new OblodaiClient(new OblodaiOptions
{
    PublicId = Environment.GetEnvironmentVariable("OBLODAI_PUBLIC_ID"),
    Secret = Environment.GetEnvironmentVariable("OBLODAI_SECRET"),
});

var invoice = await oblodai.Payments.CreateAsync(new PaymentRequest
{
    Amount = "25",              // amounts are decimal strings, never floats
    Currency = "USDT",          // what you price in — a fiat (USD, EUR, …) or a crypto asset
    Network = Network.Tron,     // omit to let the payer choose the network on the pay page
    OrderId = "order-1001",     // your reference; idempotent per order_id
    UrlCallback = "https://shop.example/oblodai/webhook",
});

Console.WriteLine($"{invoice.Url} {invoice.Address} {invoice.Status}"); // status: created
```

Prices in fiat: `Amount = "25", Currency = "USD", ToCurrency = "USDT"` — `Currency` is what you charge,
`ToCurrency` the asset the payer sends. Runnable programs live in [`examples/`](examples).

Options fall back to the environment when left unset: `OBLODAI_PUBLIC_ID`, `OBLODAI_SECRET`,
`OBLODAI_PAYOUT_PUBLIC_ID`, `OBLODAI_PAYOUT_SECRET`, `OBLODAI_BASE_URL`, `OBLODAI_ADMIN_TOKEN`,
`OBLODAI_ALLOW_INSECURE`, `OBLODAI_LOG` — so `new OblodaiClient()` is enough in a configured container.

### Two keys

The gateway issues a **payment key** and a **payout key**. Sandbox keys are both at once; live keys are
separate, and money-out routes need the payout one: `Payouts.*`, `Refunds.*`, `PayoutLinks.*`,
`Transfers.*`, `Splits.*`, `Wallets.RefundBlockedDepositAsync`, auto-withdraw, the IP allow-list,
`Webhooks.RotateSecretAsync`, `Sandbox.FaucetAsync`/`ResetAsync`. Pass both pairs and the SDK picks the
right one per call:

```csharp
new OblodaiClient(new OblodaiOptions
{
    PublicId = publicId, Secret = secret,
    PayoutPublicId = payoutPublicId, PayoutSecret = payoutSecret,
});
```

A call with the wrong kind is a 403 `merchant.wrong_key_kind`.

### Dependency injection

The client takes an externally managed `HttpClient`, so it fits `IHttpClientFactory`:

```csharp
services.AddHttpClient("oblodai").ConfigurePrimaryHttpMessageHandler(
    () => new SocketsHttpHandler { AllowAutoRedirect = false });

services.AddSingleton(sp => new OblodaiClient(
    new OblodaiOptions { PublicId = "…", Secret = "…" },
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("oblodai")));
```

The SDK applies its own per-attempt timeout and per-call deadline, so set `HttpClient.Timeout` to
`Timeout.InfiniteTimeSpan`. A finite one silently overrides both, and the SDK logs a warning quoting
your value and its own when it sees one. The SDK never follows a redirect; if the client you inject
does, the SDK notices (the response came back from a URL it did not send to) and fails the call rather
than let your signature and body reach another host.

## Resources

| Namespace                 | Methods                                                                                                                                                                                                |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Payments`                | Create · Info/Get · Cancel · History/List · Batch · Qr · Services · SendEmail · Resend · PublicView · Select · PublicQr                                                                                 |
| `Refunds`                 | Create · Resolve · Batch                                                                                                                                                                               |
| `Payouts`                 | Create · Validate · Calculate · Info/Get · Cancel · Approve · History/List · Mass · Batch · Services · Get/SetFeeConfig · Get/SetRefundFeeConfig                                                        |
| `PayoutLinks`             | Create · Info/Get · List · Cancel · Batch · Cheque · ClaimPreview · Claim                                                                                                                              |
| `PaymentLinks`            | Create · Info/Get · List · Toggle · PublicView · Checkout                                                                                                                                              |
| `Batches` / `Transfers`   | Info · ToPersonal · ToUser · Batch                                                                                                                                                                     |
| `Wallets`                 | Create · Qr · Block · RefundBlockedDeposit                                                                                                                                                             |
| `Webhooks`                | Register · RotateSecret · Deliveries · Test                                                                                                                                                            |
| `Documents`               | Statement · Ledger · BalanceCertificate · FeeSchedule · SplitReport · BatchReport · LinkReport · WalletStatement · ReferralsReport · CreateJob · JobInfo · JobFile · Download                           |
| `Splits`                  | CreateRule · ListRules · DeleteRule · Get/SetConfig · Get/SetOptIn                                                                                                                                      |
| `Settings`                | SetDiscount · ListDiscounts · Get/SetAccuracy · Get/SetAutoRefund · ListAccepted · SetAccepted · Get/SetPaymentFeeConfig · List/Set/DeleteAutoWithdraw · List/Add/Remove/EnableApiAllowlist             |
| `Account` / `Catalog`     | Balance · Referral · Vrcs · Currencies · ExchangeRates                                                                                                                                                 |
| `Sandbox`                 | Faucet · Deposit · Webhooks · Replay · Reset                                                                                                                                                           |
| `Merchants`               | Create · CreateSandbox (provisioning; `AdminToken` on a self-hosted gateway)                                                                                                                           |

Every method is `…Async` and ends with `RequestOptions? options = null, CancellationToken cancellationToken = default`.
`RequestOptions` carries `IdempotencyKey`, `TimeoutMs`, `DeadlineMs`, `Headers` (merged over the
client's, for this call only) and `PreferPayoutKey`. Lookups take a bare id, a lookup object, or the
object you already hold: `Payments.InfoAsync("uuid")`,
`Payments.InfoAsync(new PaymentLookup { OrderId = "order-1001" })`, `Payouts.CancelAsync(payout)`,
`PayoutLinks.CancelAsync(link)`.

Cancellation is yours: cancelling the `CancellationToken` throws `OperationCanceledException`, not an
SDK error, so an ASP.NET request abort behaves the way the rest of your code expects.

### Lists

List methods return a `PagePromise<T>`: `await` it for one page, `await foreach` it to walk every item
across pages, `AllAsync(max)` to collect. Nothing is requested until it is consumed.

```csharp
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
  `Statuses.IsPaymentPaid(status)` is true for `paid`/`paid_over`; `wrong_amount` (underpaid) waits for
  `Refunds.ResolveAsync(new PaymentResolveRequest { Uuid = …, Action = "accept" })`; `Statuses.IsPaymentFinal` covers the rest.
- Payout: `pending → approved → awaiting_cosign → broadcasting → sent → confirmed | failed | cancelled`.

Statuses, networks and the other vocabularies are string-backed wrappers, not C# enums: a value newer
than this snapshot still round-trips (`status.IsKnown` tells you whether the SDK documents it).

Prefer webhooks for state changes; poll `InfoAsync` only as a fallback.

### Errors

Every failure is an `OblodaiException` carrying the API's error envelope: `Code`
(`payout.insufficient_funds`), `HttpStatus`, `Retryable`, `RetryAfter`, `RequestId`, `Field`,
`Synthetic`. Subclasses let you `catch` by kind: `ValidationException` (400), `AuthenticationException`
(401), `PermissionException` (403), `NotFoundException` (404), `ConflictException` /
`IdempotencyConflictException` (409), `RateLimitException` (429), `UnavailableException` (503),
`InternalException` (other 5xx), `TransportException` (no response), `ConfigException` (rejected before
sending: `sdk.bad_config`, `sdk.bad_header`, `sdk.bad_amount`, `sdk.bad_idempotency_key`,
`sdk.idempotency_unsupported`, `sdk.missing_credentials`, `sdk.bad_path_param`), `ContractException`
(the answer is not the documented envelope, `sdk.bad_envelope` / `sdk.response_too_large`),
`SignatureException` (a webhook signature or timestamp), `WebhookPayloadException` (a webhook whose
signature matched but whose body could not be read — a CONTRACT failure, so a receiver that answers 401
to signature failures does not answer 401 to an authentic event).

Quote `RequestId` to support. The raw body is never part of the message, `ToString()` or `ToJson()` —
only its shape and size — and `ToString()` keeps the stack trace and inner exception.

An error envelope is read field by field: a `retryable` that is not a boolean falls back to the status,
a `retry_after` that is a float, a numeric string or an absurd number is clamped into `[0, 86400]`
seconds, and a body with no usable `code` becomes a synthetic error carrying the HTTP status. A
malformed envelope never turns a retryable 503 into a parse crash.

```csharp
try
{
    await oblodai.Payouts.CreateAsync(request);
}
catch (OblodaiException error) when (error.Code is ErrorCodes.PayoutInsufficientFunds or ErrorCodes.PayoutFundsMaturing)
{
    // retryable — the balance may still arrive
    ScheduleRetry(error.RetryAfter ?? 60);
}
// anything else: the SDK already retried what was safe to retry
```

### Retries and idempotency

- Create-type routes get an `Idempotency-Key` automatically (one per logical call, reused on every
  retry), so a timeout can never produce a second payout. Pass your own key
  (`new RequestOptions { IdempotencyKey = … }`) to make retries safe across restarts; on routes the
  gateway does not deduplicate the SDK refuses a key (`sdk.idempotency_unsupported`).
- An error is retried only when the API says `retryable: true`. Answers without an API envelope (a proxy
  502/503) and transport failures are retried only on read routes or keyed writes. `Retry-After` is honoured.
- Whether a route is safe to re-send is the gateway's own statement, read from the `safe` flag in
  `contract/contract.json`. Nothing is guessed from the path.
- `OblodaiOptions.Retry` sets `MaxRetries`, `BaseDelayMs`, `MaxDelayMs`, `MaxRetryAfterMs`; `TimeoutMs`
  is per attempt and `DeadlineMs` is the budget for the whole call including retries. A `Retry-After`
  the gateway reports is kept up to a day, but the pause the SDK actually takes never exceeds
  `MaxRetryAfterMs` (30 s by default).
- Response bodies are read with a cap — 8 MiB on JSON routes, 64 MiB on document routes — and the
  deadline covers the whole read, not just the first byte.

### Webhooks

```csharp
using Oblodai;

var info = WebhookVerifier.VerifyDelivery(rawBodyBytes, name => request.Headers[name], new WebhookVerifyOptions
{
    Secret = Environment.GetEnvironmentVariable("OBLODAI_WEBHOOK_SECRET")!,
});

switch (info.Event)
{
    case PaymentEvent { Status.Value: "paid" } paid: MarkOrderPaid(paid.OrderId); break;
    case PayoutEvent payout: Track(payout.Uuid, payout.Status); break;
    case WalletEvent deposit: Credit(deposit.Address, deposit.PaymentAmount); break;
}
```

`WebhookVerifyOptions` refuses an empty `Secret`, an empty `PreviousSecret` and a negative
`ToleranceSeconds` with `ConfigException` before any crypto runs; `ToleranceSeconds = 0` disables the
freshness check. The MAC is checked **before** the timestamp, so the tolerance window cannot be probed
by an unauthenticated sender. The signature header is accepted trimmed and in either case; a `0x`
prefix is not the encoding the gateway sends and is refused.

An event family this snapshot does not know arrives as `UnknownWebhookEvent` with the raw `type` — it
is never an exception, and `IsTest`, `IsStale` and `IsKnownEvent` all work on it.

Verify over the **raw** bytes — a re-serialized parse will not match. Rehearsal deliveries
(`Webhooks.TestAsync`, sandbox) are signed like live ones and carry `test: true` in the body (and
`X-Webhook-Test: true`) — check `info.IsTest` (or `WebhookVerifier.IsTestEvent(info.Event)`) and never
act on one as if money moved. `info.Id` (`X-Webhook-Id`) is stable across retries, so use it to
deduplicate; `WebhookVerifier.IsStale(event, lastSequence)` drops out-of-order deliveries. After
`Webhooks.RotateSecretAsync` pass `PreviousSecret` for at least 26 hours.

### Money helpers

`Money.Add`, `Money.Subtract`, `Money.Compare`, `Money.AreEqual`, `Money.IsZero` — exact decimal
arithmetic on the string amounts the API uses (USDT has 6 decimals, BTC 8, ETH 18). Never parse a wire
amount into a `double`, and never compare two amounts as strings (`"9" > "10"` lexicographically).
Anything that is not `[-]digits[.digits]` of at most 64 characters is a `ConfigException` with code
`sdk.bad_amount` — the SDK's own error, never a `FormatException`.

### Secrets

An API key secret, a webhook secret, a cheque passcode and a claim token or URL never print. Both
default paths are overridden: `ToString()` writes `[redacted]`, and so does serializing the object with
`System.Text.Json`. Reading the property still gives you the value, and
`OblodaiJson.SerializeWithSecrets(model)` is the one explicit way to serialize the real thing — for the
code that stores a secret or mails a claim URL, never for a log.

### Self-hosted or local gateway

`BaseUrl = "http://localhost:8093"` works out of the box; other plain-http hosts need
`AllowInsecureBaseUrl = true` (or `OBLODAI_ALLOW_INSECURE=1`). A path prefix in `BaseUrl` is kept
(`https://gw.corp/oblodai` → `https://gw.corp/oblodai/v1/payment`). Merchant provisioning
(`Merchants.CreateAsync`) is unsigned and carries `AdminToken` as `X-Admin-Token` when configured.

## The contract snapshot

`contract/` is exported by the gateway's own test suite: the route registry (107 merchant routes, each
with its auth kind, idempotency wrapper and `safe` flag), request DTO schemas with English field docs,
enums, all 471 error codes, signing vectors, golden response bodies recorded from a live gateway and
real signed webhook deliveries. `src/Oblodai/Contract/*.g.cs` is generated from it, and codegen fails
rather than guess if a route ever arrives without its `safe` flag.

```bash
dotnet run --project tools/Codegen -- generate   # regenerate after refreshing contract/
dotnet run --project tools/Codegen -- check      # CI gate: fail when the committed files drifted
```

## Development

```bash
dotnet run --project tools/Codegen -- check       # the committed *.g.cs still match contract/
dotnet build  -warnaserror                       # library, tests, tools and examples
dotnet test                                      # unit + contract tiers
OBLODAI_LIVE_URL=http://127.0.0.1:8095 dotnet test   # adds the live tier against a running gateway
dotnet pack src/Oblodai/Oblodai.csproj -c Release
```

License: MIT.
