# Changelog

All notable changes to the Oblodai .NET SDK. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/).

## Unreleased

### Added

- `client.CliLogin` — `StartAsync`, `PollAsync`, `LogoutAsync`: the browser login of the
  `oblodai` CLI (OAuth 2.0 device authorization) and logout of its key.
- `OblodaiException.Details`: the machine-readable facts of an error envelope's new `details` object
  (for example `cli.permission_denied` carries `required_role` and `role`); only string values are
  kept.
- Every method's documentation names the minimum team role a CLI key needs to call it;
  money-out operations (payouts, refunds, transfers, auto-withdrawal, split rules) take only the
  store owner's own CLI key.
- `client.Refunds.CalculateAsync` (POST /v1/payment/refund/calculate): dry-run a refund and get
  back a `RefundCalculation` — `Amount`, `Currency`, `Network`, `Address`, `AmountPaid`,
  `Surcharge`, `Commission`/`CommissionBearer`, `Credited`, `Refundable`, `Refunded`, `Remaining`,
  and, with `FromCurrency` set, the estimated `FromAmount`. Runs the same checks as
  `Refunds.PaymentAsync` and reserves/sends nothing.

### Changed

- `PayoutValidateResult` (`Payouts.ValidateAsync`) gains `Address` (the destination), and, for a
  `FromCurrency` payout, `FromAmount` and `Rate` alongside the existing `FundedBy`.
- `PayoutRequest.Memo` / `PayoutValidateRequest.Memo` docs are now network-specific: the XRP
  destination tag, the Stellar memo id, a TON comment (at most 64 bytes), and at most 120 bytes on
  every other network.
- **Breaking:** `Payments.ListHistoryAsync` takes its own request model `PaymentHistoryRequest`
  (`limit`, `offset`, `status`) instead of the shared `HistoryRequest`; `HistoryRequest` now serves
  `Payouts.ListHistoryAsync` only. The payment feed never honoured `kind`/`includeRefunds`, so the
  new model drops them, and `status` filters by the payment status vocabulary. Migration: pass
  `PaymentHistoryRequest` (or the named `limit`, `offset`, `status` arguments) to payment history
  calls.
- Method docs: the payout calculation lists `payout.unsupported_network` for an unknown network;
  lookup, test-webhook (`ok` / `status_code`) and refund amount fields are described more precisely.
  The webhook signing constants already carry the event-id and delivery-id header names that the
  contract now names as `event_id_header` / `delivery_id_header`.

## [2.0.0] — 2026-09-25

Generated from the gateway's OpenAPI contract by the backend's `tools/sdkgen`. Breaking: see
[MIGRATION-2.0.md](MIGRATION-2.0.md).

### Added

- `X-Request-ID` on every call (generated, or `RequestOptions.RequestId`), the same on all attempts.
- `client.WithRawResponseAsync` (status, headers, request id), `client.WithOptions`, `Hooks`
  (`OnRequest`/`OnResponse` per attempt), `TimeProvider` for pauses and deadlines.
- `PagePromise<T>.ByPageAsync()`.
- `IWebhookEvent.ObjectId`: the id of the object an event is about, from the id field the contract
  declares for its kind (generated per model); null on an `UnknownWebhookEvent` — not guessed.
- Waiters for long-running operations, generated from the contract's `x-sdk-poll`:
  `Batches.WaitAsync`, `Documents.WaitAsync`, `Documents.DownloadAsync`; each waits for its own
  terminal statuses.
- `ApiFacts` (`Oblodai.Contract`), generated: the long-running table (`Polls`), webhook kind → model
  (`WebhookModels`), known kinds (`WebhookKinds`), event name → kind (`WebhookEvents`) and the
  request numbers that are not money (`NonMoneyNumbers`).
- `SigningProtocol` (`Oblodai.Contract`), generated from the contract's `x-oblodai-signing`: request
  and webhook header names by role (`Header*`, `HeaderWebhook*`, the rehearsal header
  `HeaderWebhookTest` from `webhook.test_header`), the parts and separators of both canonical strings
  (`RequestCanonicalOrder`, `WebhookCanonicalOrder`), `SignatureAlgorithm`, the clock skew and the
  limits. Signing, webhook verification and the idempotency key check use it;
  `RequestSigner.Header*`, `RequestSigner.SignatureSkewSeconds`, `WebhookVerifier.Header*`,
  `Idempotency.MaxKeyLength` and the default `WebhookVerifyOptions.ToleranceSeconds` are now aliases
  of its values. The conformance suite checks the request a signed call sends — method, path and
  query, body and the headers under the contract's names.
- Status classes on the classified vocabularies (`PaymentStatus`, `PayoutStatus`, `BatchStatus`,
  `DocumentJobStatus`): `Final`, `Success`, `IsFinal`, `IsSuccess`, from the contract's
  `x-status-classes`.
- Models keep unknown fields in `Extra`, keep unknown vocabulary values, tolerate a missing field and
  print short, with secrets redacted.
- The shared conformance suite of the backend (`tools/sdkgen/conformance`) runs in the tests; the
  README snippets and the examples are executed against a scripted gateway; `make ci` checks the
  generated code and the README method tables for drift.

### Changed

- The resources, the client's resource properties, request and response models, vocabularies and the
  route table are generated from `services/core/api/openapi.json` into `src/Oblodai/Generated/`;
  `contract/`, `tools/Codegen` and the hand-written models are gone. 120 operations in 16 resources,
  named `client.Resource.MethodAsync` by one rule and pinned in `names.lock` (the generator adds new
  names itself and refuses to drop or rename one); the README method table is generated too.
- Requests are named arguments (or the request record, or `Model.From<T>` with wire names); money is
  `decimal`, sent as a string with its scale; a `double` amount does not compile, and in a dictionary
  it is `sdk.float_amount` before sending — also under a name the model does not know, unless the
  contract types that name as a number.
- `RequestOptions` is `IdempotencyKey`, `Timeout` (`TimeSpan`), `MaxRetries`, `ExtraHeaders`,
  `RequestId`; client `Timeout`/`Deadline` are `TimeSpan`s. A faucet key given both in the request and
  in `RequestOptions` is a `ConfigException` (`sdk.bad_config`, field `idempotency_key`) before sending,
  as in every Oblodai SDK.
- `OblodaiException.Message` is `[code] text (request_id=…)`; the text alone is `Description`.
- Webhook events are the generated models of the contract's webhook bodies (`PaymentWebhook`,
  `PayoutWebhook`, `WalletWebhook`, `ConversionWebhook`) behind `IWebhookEvent`, parsed by the kind
  table of `ApiFacts`; `WebhookDeliveryInfo.EventId` is the state id to deduplicate on.
- `Statuses` helpers and `LongRunning.Operations` / `TerminalStatuses` read the generated facts; the
  SDK keeps no hand-written copy of them.
- Targets `net8.0` and `net10.0`.

### Removed

- `Merchants.CreateAsync`, `OblodaiJson.SerializeWithSecrets`, `ContractVersion`, the lookup and
  query helper records (`PaymentLookup`, `PageParams`, `FormatQuery`, …).

## [1.3.0] — 2026-08-26

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
