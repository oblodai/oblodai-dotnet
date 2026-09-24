# Oblodai .NET SDK — guide for coding agents

Package `Oblodai` (2.0, `net8.0` + `net10.0`). The resources, models, vocabularies and route table in
`src/Oblodai/Generated/` are generated from the gateway's OpenAPI contract (`openapi.json`) — never
edit them; the runtime around them is hand-written.

## Non-negotiables

- Every call is `client.Resource.MethodAsync(…)`: the request as named arguments (or the request
  record, or `Model.From<TRequest>(dictionary with wire names)`), then
  `RequestOptions? options = null, CancellationToken cancellationToken = default`.
  `RequestOptions` = `{ IdempotencyKey, Timeout (TimeSpan), MaxRetries, ExtraHeaders, RequestId }`.
  Cancelling the token throws `OperationCanceledException`, not an SDK error.
- Money is `decimal` (`amount: 25m`), sent as a string with its scale. Never a `double`: it does not
  compile, and in a dictionary body it is `sdk.float_amount` before sending. A few amounts the
  contract keeps as text are `string`. `Money.Of/Parse/Format` convert.
- **One API key.** `PublicId` + `Secret` (or `OBLODAI_PUBLIC_ID` / `OBLODAI_SECRET`) sign every signed
  route. Route auth is `Public` (unsigned, `client.Checkout`), `Key` (signed) or `Onboard`
  (`AdminToken` → `X-Admin-Token`, `Sandbox.OnboardStoreAsync` only).
- List methods return `PagePromise<T>`: `await` = one page (`Page<T>` with `Items` and `Paginate`),
  `await foreach` = every item, `ByPageAsync()` = page by page, `AllAsync(max)` = a list. Nothing is
  requested until it is consumed.
- Idempotency keys are generated automatically on routes the gateway deduplicates and reused across
  retries. Passing `IdempotencyKey` where the gateway does not deduplicate (list methods included)
  throws `sdk.idempotency_unsupported`; on the faucet the option fills the body's `idempotency_key`.
- Retry safety is the contract's own `x-retry-safe` flag (`RouteSpec.Safe`), never derived from a path.
- Vocabularies (`PaymentStatus`, `ErrorCode`, …) are string-backed `readonly record struct`s: compare
  with the constants; unknown values parse (`IsKnown == false`). Unknown fields land in `Model.Extra`.
- Every call carries `X-Request-ID` (generated, or `RequestOptions.RequestId`), the same on retries.

## Naming

`names.lock` pins every public method (`resource.method` → `client.Resource.MethodAsync`). The method
is the `operationId` without the resource: `createPayment` → `Payments.CreateAsync`,
`getPaymentInfo` → `Payments.GetInfoAsync`, `listPaymentHistory` → `Payments.ListHistoryAsync`,
`createPayoutBatch` → `Batches.CreatePayoutAsync`, `getBatchDocument` → `Documents.GetBatchAsync`.
Request records are named after the contract's schemas (`PaymentRequest`, `PayoutRequest`, …),
responses too (`PaymentView`, `PayoutItem`, `BatchSubmitResponse`, …).

## Errors

`catch (OblodaiException error)` → `Code` (`family.reason`; constants `ErrorCode.*.Value`),
`Message` = `[code] text (request_id=…)`, `Description` (text only), `HttpStatus`, `Retryable`
(authoritative — the SDK already retried what it should), `RetryAfter`, `RequestId`, `Field` (400s),
`Synthetic` (the answer came from a proxy). Subclasses per status; `TransportException` when no
response arrived (or a wait ran out, `sdk.wait_timeout`); `ConfigException` before sending;
`SignatureException` for a webhook signature or timestamp; `WebhookPayloadException`
(`webhook.bad_payload`) for a delivery that verified but could not be read.

## Around a call

- `client.WithRawResponseAsync(c => c.Payments.CreateAsync(…))` → `Value`, `Status`, `Headers`,
  `RequestId`.
- `client.WithOptions(o => o with { Timeout = … })` → a client with other options, same pool.
- `OblodaiOptions.Hooks = new Hooks { OnRequest = …, OnResponse = … }` — every attempt.
- `Batches.WaitAsync(submitted)`, `Documents.WaitAsync(job)`, `Documents.DownloadAsync(done)` — poll a
  long-running operation to its terminal status (`LongRunning.Operations`).

## Webhooks

```csharp
var info = WebhookVerifier.VerifyDelivery(rawBody, headers, new WebhookVerifyOptions { Secret = secret });
switch (info.Event) { case PaymentWebhook p: …; case PayoutWebhook p: …; case WalletWebhook w: …; }
```

Verify over the **raw** bytes. `info.IsTest` is true for rehearsal deliveries — never treat them as
money. Deduplicate on `info.EventId` (`X-Webhook-Event-Id`); drop out-of-order events with
`WebhookVerifier.IsStale(info.Event, lastSequence)`. During a rotation pass `PreviousSecret` for ≥26 h.

## Machine-readable surface

`Oblodai.Resources.Routes.All` (120 routes by `operationId`: method, path, auth, idempotent, safe,
bare, list), `names.lock`, the generated records, `ErrorCode.Known`, `PaymentStatus.Known`, … Checks:
`make ci` (drift against the backend's generator, build, format, tests, the shared conformance
suite, packaging).
