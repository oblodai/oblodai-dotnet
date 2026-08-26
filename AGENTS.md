# Oblodai .NET SDK — guide for coding agents

Package `Oblodai` (1.3). Everything below is verified against the gateway's contract snapshot shipped in
`contract/contract.json`.

## Non-negotiables

- Amounts are decimal **strings**: `Amount = "25"`, never `25m`. Do not parse them into `double`; use
  `Money.Add` / `Money.Compare` from the package.
- Every method is `…Async` and its last two parameters are
  `RequestOptions? options = null, CancellationToken cancellationToken = default`.
  `RequestOptions` = `{ IdempotencyKey, TimeoutMs, DeadlineMs, PreferPayoutKey }`.
- Two key kinds. The **payout key** is required for: `Payouts.*`, `Refunds.*`, `PayoutLinks.*`,
  `Transfers.*`, `Splits.*`, `Wallets.RefundBlockedDepositAsync`, `Settings.*AutoWithdraw*`,
  `Settings.*ApiAllowlist*`, `Webhooks.RotateSecretAsync`, `Webhooks.TestAsync(WebhookKind.Payout, …)`,
  `Sandbox.FaucetAsync`, `Sandbox.ResetAsync`. Configure it with `PayoutPublicId`/`PayoutSecret` (or
  `OBLODAI_PAYOUT_*`); a wrong kind is a 403 `merchant.wrong_key_kind`.
- List methods return `PagePromise<T>`: `await` = one page (`Page<T>` with `Items` and `Paginate`),
  `await foreach` = every item, `AllAsync(max)` = a list. Nothing is requested until it is consumed.
- Idempotency keys are generated automatically on create routes and reused across retries. Passing
  `IdempotencyKey` to a route the gateway does not deduplicate throws `sdk.idempotency_unsupported`.
- Vocabularies (`PaymentStatus`, `Network`, …) are string-backed `readonly record struct`s, not C#
  enums: compare with the constants (`PaymentStatus.Paid`), and expect values this snapshot does not
  know (`status.IsKnown == false`) rather than exceptions.

## Naming

| intent            | call                                                                                                                                |
| ----------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| fetch one         | `.InfoAsync(uuid)` / `.InfoAsync(new PaymentLookup { OrderId = … })` (alias `.GetAsync`)                                            |
| fetch many        | `.HistoryAsync(filter)` on payments/payouts (alias `.ListAsync`), `.ListAsync(filter)` elsewhere                                    |
| create            | `.CreateAsync(request)`; webhooks: `.RegisterAsync(url)`                                                                            |
| many, synchronous | `Payouts.MassAsync`, `PayoutLinks.BatchAsync` — ≤100, per-element `BatchElement<T> { Idx, Ok, Result, Message }`                    |
| many, async       | `Payments.BatchAsync`, `Payouts.BatchAsync`, `Refunds.BatchAsync`, `Transfers.BatchAsync` — ≤5000, poll `Batches.InfoAsync`         |
| documents         | `Documents.*` → `FileResult { Bytes, ContentType, Filename }`                                                                        |
| provisioning      | `Merchants.CreateAsync`, `Merchants.CreateSandboxAsync(merchantId)` — unsigned; `AdminToken` on a self-hosted gateway               |
| payer-facing      | `Payments.PublicViewAsync/SelectAsync/PublicQrAsync`, `PaymentLinks.PublicViewAsync/CheckoutAsync`, `PayoutLinks.ClaimPreviewAsync/ClaimAsync` — no credentials |

Request bodies are generated records named after the route: `POST /v1/payment` → `PaymentRequest`,
`POST /v1/payout/link/batch` → `PayoutLinkBatchRequest`. Required fields use the `required` modifier, so
the compiler tells you what the gateway insists on.

## Errors

`catch (OblodaiException error)` → `Code` (`family.reason`, constants in `ErrorCodes`), `HttpStatus`,
`Retryable` (authoritative — the SDK already retried what it should), `RetryAfter`, `RequestId` (quote it
to support), `Field` (400s), `Synthetic` (the answer came from a proxy, not the API). Subclasses per
status; `TransportException` when no response arrived; `ConfigException` before sending;
`SignatureException` for webhooks. `error.ToJson()` keeps the message and drops the raw body.

Codes worth handling: `payout.insufficient_funds` (retryable), `payout.funds_maturing` (retryable),
`idempotency.key_reused`, `invoice.not_payable`, `payment.not_found`, `merchant.wrong_key_kind`,
`merchant.bad_signature`, `request.rate_limited`.

## Statuses

- Payment: `select → created → confirm_check → paid | paid_over | wrong_amount | expired | cancelled`.
  `Statuses.IsPaymentPaid` = paid/paid_over. `wrong_amount` needs `Refunds.ResolveAsync`.
- Payout: `pending → approved → awaiting_cosign → broadcasting → sent → confirmed | failed | cancelled`.
- Webhook event types: `invoice.<status>`, `payout.<status>`, `wallet.paid`; the body's `type` is
  `payment | payout | wallet` and `WebhookVerifier.Parse` returns the matching
  `PaymentEvent` / `PayoutEvent` / `WalletEvent`.

## Webhooks

```csharp
var info = WebhookVerifier.VerifyDelivery(rawBody, headers, new WebhookVerifyOptions { Secret = secret });
```

Verify over the **raw** bytes. `info.IsTest` is true for rehearsal deliveries (`test: true` in the signed
body, `X-Webhook-Test: true`) — never treat them as money. Deduplicate on `info.Id` (`X-Webhook-Id`); drop
out-of-order events with `WebhookVerifier.IsStale(info.Event, lastSequence)`. During a rotation pass
`PreviousSecret` for ≥26 h.

## Machine-readable surface

`Routes.All` (107 routes: method, path, auth, idempotent, safe, bare, list), the generated request
records, `ErrorCodes.All`, `Network.Known`, `PaymentStatus.Known`, `PayoutStatus.Known`,
`EventType.Known`, and `contract/` itself (schemas, golden response bodies per route, error samples,
signed webhook samples). `ContractVersion` stamps which gateway commit the surface was generated from.
