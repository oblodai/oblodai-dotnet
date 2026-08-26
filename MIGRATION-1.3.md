# Migrating to Oblodai .NET 1.3

`Oblodai` 1.3.0 is the first published .NET package, so there is no older release to upgrade from. What
follows is for two audiences: teams that were building against a pre-release drop of this repository,
and teams porting an integration from one of the other Oblodai SDKs, where the same 1.3 changes landed.

## What changed and what you have to do

### The `safe` flag comes from the gateway

Whether a route may be re-sent after a transport failure — without an idempotency key — is now the
core's own hand-classified `safe` flag in `contract/contract.json`. The SDK no longer derives it from
the path (an earlier draft matched suffixes like `/info` and `/list` and carried an exception list).
Codegen fails outright if a route ever arrives without the flag, rather than falling back to a guess.

**You do nothing.** The classification is identical for all 107 routes today; the difference is that it
is now stated by the gateway instead of inferred here.

### Secrets no longer print

`ToString()` and `System.Text.Json` both write `[redacted]` for every secret-bearing member:

| Type                                                          | Redacted member                     |
| ------------------------------------------------------------- | ------------------------------------ |
| `OblodaiOptions`, `ResolvedOptions`, `TransportOptions`        | `Secret`, `PayoutSecret`, `AdminToken` |
| `Credentials`                                                  | `Secret`                             |
| `WebhookVerifyOptions`                                         | `Secret`, `PreviousSecret`           |
| `WebhookEndpoint`, `WebhookSecretRotated`                      | `Secret`                             |
| `ApiKeyPair`                                                   | `Secret`                             |
| `PayoutLink`                                                   | `ClaimToken`, `ClaimUrl`, `Passcode` |

Reading the property still gives you the real value. **If you persisted one of these models by
serializing it** — storing a minted webhook secret, mailing a claim URL — switch that one call to
`OblodaiJson.SerializeWithSecrets(model)`, which is the explicit, deliberately awkward way to get the
real values out. Never send its output to a log.

```csharp
var endpoint = await oblodai.Webhooks.RegisterAsync("https://shop.example/hook");
await store.SaveAsync(endpoint.Secret);                       // fine — the property is the value
await store.SaveAsync(OblodaiJson.SerializeWithSecrets(endpoint)); // fine — explicit
logger.LogInformation("registered {Endpoint}", endpoint);     // safe now: prints [redacted]
```

### Webhook errors are split by cause

A delivery whose signature matched but whose body this SDK cannot read used to be a
`SignatureException`. It is now `WebhookPayloadException` — code `webhook.bad_payload`, in the CONTRACT
family (it derives from `ContractException`).

**If your receiver answers 401 on `SignatureException`, this matters.** Answering 401 to an authentic
delivery tells the gateway your endpoint rejected it, and a run of those retires the endpoint. Answer
500 (so the gateway retries) or 200-and-alert, but not 401:

```csharp
try { info = WebhookVerifier.VerifyDelivery(body, headers, options); }
catch (SignatureException)      { return Results.Unauthorized(); }   // not from us
catch (WebhookPayloadException) { return Results.StatusCode(500); }  // ours — authentic, unreadable
```

An event `type` this snapshot does not know is no longer an exception at all: it arrives as
`UnknownWebhookEvent` with the raw type string, and `IsTest`, `IsStale` and the new
`WebhookVerifier.IsKnownEvent` all work on it. A `switch` over the event union needs a `default` arm.

`WebhookVerifyOptions` also refuses, with `ConfigException`, an empty `Secret`, an empty
`PreviousSecret` and a negative `ToleranceSeconds` (use `0` to disable the freshness check), and the MAC
is now checked **before** the timestamp so the tolerance window cannot be probed by an unauthenticated
sender.

`WebhookVerifyOptions.Secret` is no longer `required` — a member the serializer is told to skip cannot
also be one it is told to demand. An object initialiser that omitted it used to fail to compile; it now
fails at verification time with `ConfigException` / `sdk.bad_config`.

### `Sequence` is nullable

`WebhookEvent.Sequence` is `long?`. A delivery that carries no usable sequence decodes to `null` rather
than making the whole authentic event unreadable, and `WebhookVerifier.IsStale` answers `false` for it.
Replace `lastSequence[e.Uuid] = e.Sequence;` with a null check.

### Vocabulary conversions are explicit

`PaymentStatus status = "padi";` no longer compiles. The `string` → vocabulary conversion is `explicit`,
because the implicit one turned a typo into a value that simply never matched anything, hours later, in
production. Use the constants (`PaymentStatus.Paid`), `PaymentStatus.FromValue(wireValue)` for a value
the gateway added after this snapshot, or a cast where you really mean one.

### Client-side refusals are all `ConfigException`

`Idempotency.AssertValid` used to throw `ValidationException` — an `ApiException` subclass, which claims
the gateway answered. Every refusal the SDK makes before a request leaves is now `ConfigException`:

| Code                          | When                                                        |
| ----------------------------- | ------------------------------------------------------------ |
| `sdk.bad_config`              | Unusable options: base URL, half a key pair, webhook options |
| `sdk.bad_idempotency_key`     | A key that could not be sent as a header value               |
| `sdk.idempotency_unsupported` | A key on a route the gateway does not deduplicate            |
| `sdk.bad_header`              | A caller header with CR, LF, a control or a non-ASCII byte   |
| `sdk.bad_amount`              | A string the money helpers cannot read as a decimal amount   |
| `sdk.missing_credentials`     | A signed route with no key pair configured                   |
| `sdk.bad_path_param`          | A path parameter that would rewrite the URL                  |

`Money.*` no longer throws `FormatException` — it throws `ConfigException` / `sdk.bad_amount`, so
`catch (OblodaiException)` around SDK calls now really does catch everything the SDK raises.

### Idempotency keys on list methods are refused, not dropped

Passing `IdempotencyKey` to a list method used to be silently ignored. It now throws
`sdk.idempotency_unsupported` at the call site, like every other route the gateway does not deduplicate
— silently dropping it left callers believing a lost page request was safe to repeat under that key.

### Cancellation is your exception

Cancelling the `CancellationToken` you passed now throws `OperationCanceledException` instead of a
`TransportException` with `transport.aborted` (a code the SDK no longer has). ASP.NET request aborts and every
`catch (OperationCanceledException)` in your process behave as they should. A timeout the SDK imposed is
still `TransportException` / `transport.timeout`.

### Caller-facing parameter types moved to the `Oblodai` namespace

`FileResult`, `PageParams`, `PaymentLookup`, `PayoutLookup`, `DocumentQuery`, `FormatQuery`,
`PeriodQuery`, `SignedDocumentQuery` and the new `PayoutRef` / `LinkRef` are in `Oblodai`, not
`Oblodai.Resources`. **Drop `using Oblodai.Resources;`** — with `using Oblodai;` (which you already
have) every snippet in the README compiles as written.

### Ids take either form

Methods that took a bare id string now take a reference that converts from a string *or* from the object
you already hold: `Payouts.CancelAsync(payout)`, `Payouts.ApproveAsync(payout)`,
`PayoutLinks.InfoAsync(link)` / `GetAsync` / `CancelAsync`, `PaymentLinks.InfoAsync(link)` / `GetAsync` /
`ToggleAsync(link, active)`. Existing string call sites are unchanged.

### `wallet.blocked` is not an error code

The wallet model's `blocked` **field** is real; there is no `wallet.blocked` error code in the
gateway's catalogue, and the SDK's documentation no longer names one. `Wallets.RefundBlockedDepositAsync`
answers `wallet.bad_uuid`, `refund.no_address`, `refund.nothing_to_refund`, `refund.dust`,
`refund.destination_internal` and `merchant.wrong_key_kind`.

### `Merchants` and the admin token

`client.Merchants.CreateAsync` / `CreateSandboxAsync` provision merchants on a self-hosted gateway.
They are unsigned and carry `AdminToken` (or `OBLODAI_ADMIN_TOKEN`) as `X-Admin-Token` — and only on
those routes. A caller-supplied `X-Admin-Token` header is now dropped, so it cannot be smuggled onto a
signed route.

### Model corrections

- `WebhookEvent.Sequence` is `long?` (above).
- A model property declared `string` (rather than `string?`) can no longer hold `null`: a wire `null`
  decodes to the empty string, so `payment.Uuid.Length` cannot throw a `NullReferenceException` and
  `Money.Compare` cannot be handed a null it then blames on an empty string.
- Nested models — `BalanceEntry`, `BalanceGroup`, `BatchInfoItem`, `CurrencyInfo`, `PricingCurrency`,
  `DocumentJobPeriod`, `ReferralWeek`, `ServiceMethodLimit`, `ServiceMethodCommission`, `PaymentRefund`
  — are now key-checked against the recorded bodies like the top-level ones.

## Per-call options

`RequestOptions` is `{ IdempotencyKey, TimeoutMs, DeadlineMs, Headers, PreferPayoutKey }`. `Headers` is
new: extra headers merged over the client's, for one call only, refused with `sdk.bad_header` if they
could not be sent verbatim.

## Limits worth knowing

| Thing                         | Limit                                                     |
| ----------------------------- | ---------------------------------------------------------- |
| `PayoutLinks.BatchAsync`      | 500 links per call, per-element outcomes                   |
| `Payouts.MassAsync`           | 100 payouts per call, per-element outcomes                 |
| `*.BatchAsync` (asynchronous) | 5000 items, poll `Batches.InfoAsync`                       |
| JSON response body            | 8 MiB, then `sdk.response_too_large`                       |
| Document response body        | 64 MiB, then `sdk.response_too_large`                      |
| `Retry-After` honoured        | reported up to 86 400 s, slept at most `MaxRetryAfterMs`   |
| Amount string                 | 64 characters, `[-]digits[.digits]`                        |
| Idempotency key               | 255 printable ASCII characters, no spaces                  |
