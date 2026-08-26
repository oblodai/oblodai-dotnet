# Changelog

All notable changes to the Oblodai .NET SDK. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/).

## 1.3.0 — 2026-08-25

First release of the .NET client, generated from the gateway's own contract snapshot and verified
against it. It matches the 1.3 line of the other Oblodai SDKs.

### Added

- `OblodaiClient` with every merchant route the gateway exposes (107), grouped as `Payments`, `Refunds`,
  `Payouts`, `PayoutLinks`, `PaymentLinks`, `Batches`, `Transfers`, `Wallets`, `Webhooks`, `Documents`,
  `Splits`, `Settings`, `Account`, `Catalog`, `Sandbox`, `Merchants`.
- Request signing with the gateway's five-field recipe
  (`ts \n METHOD \n path+query \n Idempotency-Key \n body`), checked against the signing vectors the
  gateway's test suite exports.
- Retries driven by the API's own `retryable` flag, with the safety rule that a write the gateway does
  not deduplicate is never re-sent after a transport error or an envelope-less proxy answer.
- Automatic `Idempotency-Key` on create routes (one key per logical call, reused across retries) and a
  refusal (`sdk.idempotency_unsupported`) when a key is passed to a route the gateway ignores it on.
- Clock-skew correction: on a 401 signature failure the SDK re-signs once with the server's `Date` and
  keeps the offset only if that attempt got past authentication.
- Two key pairs: the payment key and an optional payout key, picked per route; `Batches.InfoAsync`
  retries once with the payout key on `merchant.wrong_key_kind`.
- `PagePromise<T>`: `await` gives one page, `await foreach` walks them all lazily, `AllAsync(max)`
  collects. Nothing is requested until it is consumed.
- `WebhookVerifier`: rotation-aware signature verification over the raw bytes, freshness window,
  `Parse` into the discriminated `WebhookEvent` union, `IsStale` for out-of-order deliveries and
  `IsTest` / `IsTestEvent` for rehearsal deliveries (`webhooks.test`, sandbox), which are signed like
  live ones but must never be acted on as if money moved. Needs no client and no API key.
- `OblodaiException` family: `Code`, `HttpStatus`, `Retryable`, `RetryAfter`, `RequestId`, `Field`,
  `Synthetic`, with a subclass per status (`ValidationException`, `AuthenticationException`,
  `PermissionException`, `NotFoundException`, `ConflictException`, `IdempotencyConflictException`,
  `RateLimitException`, `UnavailableException`, `InternalException`, `TransportException`,
  `ConfigException`, `ContractException`, `SignatureException`). The raw body is never serialized.
- Generated contract surface: `Routes`, request records, the open vocabularies (`PaymentStatus`,
  `PayoutStatus`, `Network`, …) as string-backed wrappers that carry unknown values through, and all
  469 `ErrorCodes`.
- Money helpers (`Money.Add`, `Subtract`, `Compare`, `IsZero`) that work on the decimal strings the API
  uses, and status helpers (`Statuses.IsPaymentPaid`, …).
- `OBLODAI_PUBLIC_ID` / `OBLODAI_SECRET` / `OBLODAI_PAYOUT_*` / `OBLODAI_BASE_URL` /
  `OBLODAI_ADMIN_TOKEN` / `OBLODAI_ALLOW_INSECURE` / `OBLODAI_LOG` environment fallbacks, and an
  `IHttpClientFactory`-friendly constructor.
- Tests in three tiers: unit (signing and webhook vectors, retry, idempotency, skew, URL and header
  rules against a fake `HttpMessageHandler`), contract (every route hits the right method, path, auth
  and idempotency header; every golden body decodes into its model with the same key set) and live
  (the sandbox journey and a sweep of every namespace against a running gateway).
- `dotnet run --project tools/Codegen -- generate` regenerates the contract surface;
  `-- check` fails the build when the committed files have drifted from `contract/contract.json`.

[1.3.0]: https://github.com/oblodai/oblodai-dotnet/releases/tag/v1.3.0
