# Changelog

All notable changes to the Oblodai .NET SDK. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/).

## 1.3.0 — 2026-08-26

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
  not deduplicate is never re-sent after a transport error or an envelope-less proxy answer. Whether a
  route is read-only is the core's own `safe` flag from `contract/contract.json`, never derived from the
  path; codegen fails if a route arrives without it.
- Automatic `Idempotency-Key` on create routes (one key per logical call, reused across retries) and a
  refusal (`sdk.idempotency_unsupported`) when a key is passed to a route the gateway ignores it on.
- Clock-skew correction: on a 401 signature failure the SDK re-signs once with the server's `Date` and
  keeps the offset only if that attempt got past authentication.
- One API key: `PublicId` + `Secret` sign every signed route, money-in and money-out alike. The payout
  credential pair and the payout-key option are gone — there is nothing to choose per call. Route auth
  is `public`, `key` or `onboard`; `merchant.wrong_key_kind` left the gateway's catalogue and survives
  only as a legacy string for merchants still on an old `oblodai_pk_`/`oblodai_wk_` pair.
- `PagePromise<T>`: `await` gives one page, `await foreach` walks them all lazily, `AllAsync(max)`
  collects. Nothing is requested until it is consumed.
- Secrets never print: `OblodaiOptions`, `ResolvedOptions`, `TransportOptions`, `WebhookVerifyOptions`,
  `Credentials`, `WebhookEndpoint.Secret`, `WebhookSecretRotated.Secret`, `ApiKeyPair.Secret` and
  `PayoutLink.ClaimToken`/`ClaimUrl`/`Passcode` are `[redacted]` in `ToString()` and in the default
  `System.Text.Json` path. `OblodaiJson.SerializeWithSecrets` is the one explicit way to serialize the
  real values.
- The error envelope is decoded field by field: a non-boolean `retryable` falls back to the status, a
  `retry_after` given as a float, a numeric string or an absurd number is clamped into `[0, 86400]`
  seconds, and a body with no usable `code` becomes a synthetic error with the HTTP status. A malformed
  envelope can no longer turn a retryable 503 into a JSON parse crash, and no arithmetic on a pause can
  overflow into a negative delay.
- Response bodies are read with a size cap (8 MiB JSON, 64 MiB documents → `sdk.response_too_large`),
  and the deadline covers the whole read.
- Caller cancellation propagates as `OperationCanceledException` instead of being wrapped, so ASP.NET
  request aborts and every `catch (OperationCanceledException)` behave as expected.
- A redirect an injected `HttpClient` followed is detected (the answer came back from a URL the SDK did
  not send to) and refused, so a signed request cannot reach another host. A finite `HttpClient.Timeout`
  on an injected client is warned about, quoting both values.
- Caller headers are validated before signing: a CR, LF or non-ASCII value is `sdk.bad_header`, and the
  names the SDK owns — including `X-Admin-Token`, which only rides on the onboarding routes — always
  win, compared case-insensitively. `RequestOptions.Headers` adds headers for one call.
- `WebhookVerifier`: rotation-aware signature verification over the raw bytes, freshness window,
  `Parse` into the discriminated `WebhookEvent` union, `IsStale` for out-of-order deliveries and
  `IsTest` / `IsTestEvent` for rehearsal deliveries (`Webhooks.TestAsync`, sandbox), which are signed
  like live ones but must never be acted on as if money moved. Needs no client and no API key. The MAC
  is checked before the timestamp, so the tolerance window is not a pre-auth oracle; an empty secret, an
  empty `PreviousSecret` and a negative tolerance are `ConfigException` before any crypto; an event
  family this snapshot does not know arrives as `UnknownWebhookEvent` rather than throwing; and a
  delivery that verified but could not be read is `WebhookPayloadException` (`webhook.bad_payload`, the
  CONTRACT family) so a receiver answering 401 to signature failures does not reject an authentic event.
- `OblodaiException` family: `Code`, `HttpStatus`, `Retryable`, `RetryAfter`, `RequestId`, `Field`,
  `Synthetic`, with a subclass per status (`ValidationException`, `AuthenticationException`,
  `PermissionException`, `NotFoundException`, `ConflictException`, `IdempotencyConflictException`,
  `RateLimitException`, `UnavailableException`, `InternalException`, `TransportException`,
  `ConfigException`, `ContractException`, `SignatureException`). The raw body is never serialized.
- Generated contract surface: `Routes`, request records, the open vocabularies (`PaymentStatus`,
  `PayoutStatus`, `Network`, …) as string-backed wrappers that carry unknown values through, and all
  469 `ErrorCodes`. The `string` → vocabulary conversion is `explicit`, so a typo cannot compile as a
  status.
- Money helpers (`Money.Add`, `Subtract`, `Compare`, `AreEqual`, `IsZero`) that work on the decimal
  strings the API uses, and status helpers (`Statuses.IsPaymentPaid`, …). A malformed amount is
  `ConfigException` / `sdk.bad_amount`, never a `FormatException`.
- Lookups and ids take either form: a bare id, a lookup record, or the object you already hold
  (`Payouts.CancelAsync(payout)`, `PayoutLinks.CancelAsync(link)`, `PaymentLinks.ToggleAsync(link, …)`).
- Six environment fallbacks — `OBLODAI_PUBLIC_ID`, `OBLODAI_SECRET`, `OBLODAI_ADMIN_TOKEN`,
  `OBLODAI_BASE_URL`, `OBLODAI_LOG`, `OBLODAI_ALLOW_INSECURE` — and an `IHttpClientFactory`-friendly
  constructor.
- Tests in three tiers: unit (signing and webhook vectors, retry, idempotency, skew, URL and header
  rules against a fake `HttpMessageHandler`), contract (every route hits the right method, path, auth
  and idempotency header; every golden body decodes into its model with the same key set) and live
  (the sandbox journey and a sweep of every namespace against a running gateway).
- `dotnet run --project tools/Codegen -- generate` regenerates the contract surface;
  `-- check` fails the build when the committed files have drifted from `contract/contract.json`.
- A GitHub Actions workflow (`.github/workflows/ci.yml`) running the drift check, a warnings-as-errors
  build, the suite and `dotnet pack`.
- Source Link and a normalized (`ContinuousIntegrationBuild`) package, so a stack trace from the
  published package steps into these sources.

### Migration

See [MIGRATION-1.3.md](MIGRATION-1.3.md) — this is the first .NET release, but the notes cover the
behaviour that differs from the pre-release drafts and from the other 1.3 SDKs.

[1.3.0]: https://github.com/oblodai/oblodai-dotnet/releases/tag/v1.3.0
