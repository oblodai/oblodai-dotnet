# Migrating from 1.3 to 2.0

2.0 is generated from the gateway's OpenAPI contract (`services/core/api/openapi.json`) instead of
the older contract snapshot, and every public method name follows one rule:
`client.Resource.MethodAsync`, where the resource comes from the operation's tag and the method is
its `operationId` without the resource name. `names.lock` pins the result; the table below is built
from it. The wire does not change — only the C# surface does.

## At a glance

- **.NET 8 or 10.** The package carries `net8.0` and `net10.0` builds.
- **One namespace.** Models, request records and vocabularies moved from `Oblodai.Models` /
  `Oblodai.Contract` to `Oblodai`. Most files need just `using Oblodai;`. The resource classes stay in
  `Oblodai.Resources`; `Oblodai.Resources.Routes.All` is keyed by `operationId`
  (`Routes.All["createPayout"]`), not by `"POST /v1/payout"`.
- **Models are generated.** Response types are named after the contract's schemas
  (`Payment` → `PaymentView`, `Payout` → `PayoutItem` / `PayoutView`, …); a field the SDK does not know
  lands in `Extra`, and `ToString()` is short and redacts secrets.
- **Money is `decimal`.** `Amount = "25"` becomes `amount: 25m` (sent as `"25"`, scale kept:
  `25.10m` → `"25.10"`). A `double` does not compile; in a dictionary body
  (`Model.From<T>(…)`) it is refused with `sdk.float_amount` before sending. `Money.Of`,
  `Money.Parse` and `Money.Format` convert. A few amounts the contract keeps as text stay `string`.
- **Named arguments.** `Payments.CreateAsync(new PaymentRequest { Amount = "25", Currency = "USDT" })`
  becomes `Payments.CreateAsync(amount: 25m, currency: "USDT")`; the request record is still accepted
  (`Payments.CreateAsync(new PaymentRequest { Amount = 25m, Currency = "USDT" })`). Lookups are named
  too: `Payments.InfoAsync("uuid")` → `Payments.GetInfoAsync(uuid: "uuid")`,
  `new PaymentLookup { OrderId = … }` → `orderId: …`. `PaymentLookup`, `PayoutLookup`, `PayoutRef`,
  `LinkRef`, `PageParams` and the `DocumentQuery` family are gone — pass the fields as arguments
  (`Documents.BatchReportAsync(id, new FormatQuery { Format = "csv" })` →
  `Documents.GetBatchAsync(uuid: id, format: "csv")`).
- **Vocabularies** are generated from the contract: `Network.Tron` is now the string `"tron"` where the
  contract types the field as a string; `ErrorCodes.PayoutInsufficientFunds` is
  `ErrorCode.PayoutInsufficientFunds.Value`.
- **Per-call options** (`RequestOptions`):

  | 1.3 | 2.0 |
  | --- | --- |
  | `TimeoutMs = 30000` (per attempt) | `Timeout = TimeSpan.FromSeconds(30)` (per attempt) |
  | `DeadlineMs` (per call) | the client's `Deadline` (`TimeSpan`, the whole call) |
  | `Headers` | `ExtraHeaders` |
  | `IdempotencyKey` | `IdempotencyKey` (unchanged; on the faucet it fills the body's own `idempotency_key`) |
  | — | `MaxRetries`, `RequestId` (sent as `X-Request-ID`; generated per call when omitted) |

- **Client options:** `OblodaiOptions.TimeoutMs` / `DeadlineMs` become `Timeout` / `Deadline`
  (`TimeSpan`). New: `Hooks` (`OnRequest` / `OnResponse` on every attempt), `TimeProvider`,
  `client.WithOptions(o => o with { … })`, `client.WithRawResponseAsync(c => c.Payments.CreateAsync(…))`.
- **Errors:** `error.Message` reads `[code] text (request_id=…)`; the bare text is `error.Description`.
- **Lists** still return `PagePromise<T>`; new: `ByPageAsync()`.
- **Long-running operations:** `Batches.WaitAsync(submitted)` and `Documents.WaitAsync(job)` /
  `Documents.DownloadAsync(job)` poll until the status is terminal.
- **Webhooks:** `WebhookVerifier.Parse`/`Verify` return `IWebhookEvent` — the generated
  `PaymentWebhook`, `PayoutWebhook`, `WalletWebhook`, `ConversionWebhook`, or `UnknownWebhookEvent`
  (`PaymentEvent` → `PaymentWebhook`, …; `Sequence` is `EventSequence` on the interface).
  `WebhookDeliveryInfo` gains `EventId` (`X-Webhook-Event-Id`) — deduplicate on it.
- **Removed:** `Merchants.CreateAsync` (`POST /v1/merchants` is not part of the merchant API
  contract); `Merchants.CreateSandboxAsync` is `Sandbox.OnboardStoreAsync`. `Webhooks.TestAsync(kind,
  …)` is one method per kind. `OblodaiJson.SerializeWithSecrets` and `ContractVersion` are gone;
  `contract/` and `tools/Codegen` left the repository.

## Method names (120 methods)

| 1.3 | 2.0 | operationId | route |
| --- | --- | --- | --- |
| `Account.BalanceAsync` | `Account.GetBalanceAsync` | `getBalance` | `POST /v1/balance` |
| — (new) | `Account.GetSummaryAsync` | `getSummary` | `POST /v1/summary` |
| `Catalog.ExchangeRatesAsync` | `Account.ListExchangeRatesAsync` | `listExchangeRates` | `POST /v1/exchange-rate/list` |
| `Settings.AddApiAllowlistAsync` | `ApiAllowlist.AddEntryAsync` | `addApiAllowlistEntry` | `POST /v1/api-allowlist/add` |
| `Settings.ListApiAllowlistAsync` | `ApiAllowlist.ListAsync` | `listApiAllowlist` | `POST /v1/api-allowlist/list` |
| `Settings.RemoveApiAllowlistAsync` | `ApiAllowlist.RemoveEntryAsync` | `removeApiAllowlistEntry` | `POST /v1/api-allowlist/remove` |
| `Settings.EnableApiAllowlistAsync` | `ApiAllowlist.SetEnabledAsync` | `setApiAllowlistEnabled` | `POST /v1/api-allowlist/enable` |
| `Payments.BatchAsync` | `Batches.CreatePaymentAsync` | `createPaymentBatch` | `POST /v1/payment/batch` |
| `Payouts.BatchAsync` | `Batches.CreatePayoutAsync` | `createPayoutBatch` | `POST /v1/payout/batch` |
| `Refunds.BatchAsync` | `Batches.CreateRefundAsync` | `createRefundBatch` | `POST /v1/refund/batch` |
| `Batches.InfoAsync` | `Batches.GetInfoAsync` | `getBatchInfo` | `POST /v1/batch/info` |
| `Payments.PublicViewAsync` | `Checkout.GetAsync` | `getCheckout` | `GET /v1/pay/{id}` |
| — (new) | `Checkout.GetOnrampAsync` | `getCheckoutOnramp` | `GET /v1/pay/{id}/onramp` |
| `PaymentLinks.PublicViewAsync` | `Checkout.GetPublicPaymentLinkAsync` | `getPublicPaymentLink` | `GET /v1/link/{id}` |
| `Payments.PublicQrAsync` | `Checkout.GetQrAsync` | `getCheckoutQr` | `GET /v1/pay/{id}/qr` |
| — (new) | `Checkout.GetSourceOfFundsFormAsync` | `getSourceOfFundsForm` | `GET /v1/aml/{token}` |
| `Catalog.CurrenciesAsync` | `Checkout.ListCurrenciesAsync` | `listCurrencies` | `GET /v1/currencies` |
| `PaymentLinks.CheckoutAsync` | `Checkout.PaymentLinkAsync` | `checkoutPaymentLink` | `POST /v1/link/{id}/checkout` |
| `Payments.SelectAsync` | `Checkout.SelectMethodAsync` | `selectCheckoutMethod` | `POST /v1/pay/{id}/select` |
| — (new) | `Checkout.StartOnrampAsync` | `startCheckoutOnramp` | `POST /v1/pay/{id}/onramp` |
| — (new) | `Checkout.SubmitSourceOfFundsAsync` | `submitSourceOfFunds` | `POST /v1/aml/{token}` |
| `Documents.CreateJobAsync` | `Documents.CreateJobAsync` | `createDocumentJob` | `POST /v1/documents/jobs` |
| `Documents.JobFileAsync` | `Documents.DownloadJobFileAsync` | `downloadDocumentJobFile` | `GET /v1/documents/jobs/file` |
| `Documents.BalanceCertificateAsync` | `Documents.GetBalanceAsync` | `getBalanceDocument` | `GET /v1/documents/balance` |
| `Documents.BatchReportAsync` | `Documents.GetBatchAsync` | `getBatchDocument` | `GET /v1/documents/batch` |
| `Documents.FeeScheduleAsync` | `Documents.GetFeesAsync` | `getFeesDocument` | `GET /v1/documents/fees` |
| `Documents.JobInfoAsync` | `Documents.GetJobAsync` | `getDocumentJob` | `POST /v1/documents/jobs/info` |
| `Documents.LedgerAsync` | `Documents.GetLedgerAsync` | `getLedgerDocument` | `GET /v1/documents/ledger` |
| `Documents.LinkReportAsync` | `Documents.GetPaymentLinkAsync` | `getPaymentLinkDocument` | `GET /v1/documents/link` |
| `PayoutLinks.ChequeAsync` | `Documents.GetPayoutLinkChequeAsync` | `getPayoutLinkCheque` | `POST /v1/payout/link/cheque` |
| `Documents.ReferralsReportAsync` | `Documents.GetReferralsAsync` | `getReferralsDocument` | `GET /v1/documents/referrals` |
| `Documents.DownloadAsync` | `Documents.GetSignedAsync` | `getSignedDocument` | `GET /v1/documents/{kind}/{id}` |
| `Documents.SplitReportAsync` | `Documents.GetSplitAsync` | `getSplitDocument` | `GET /v1/documents/split` |
| `Documents.StatementAsync` | `Documents.GetStatementAsync` | `getStatementDocument` | `GET /v1/documents/statement` |
| `Documents.WalletStatementAsync` | `Documents.GetWalletStatementAsync` | `getWalletStatementDocument` | `GET /v1/documents/wallet/statement` |
| `PaymentLinks.CreateAsync` | `PaymentLinks.CreateAsync` | `createPaymentLink` | `POST /v1/payment/link` |
| `PaymentLinks.InfoAsync`, `PaymentLinks.GetAsync` | `PaymentLinks.GetAsync` | `getPaymentLink` | `POST /v1/payment/link/info` |
| `PaymentLinks.ListAsync` | `PaymentLinks.ListAsync` | `listPaymentLinks` | `POST /v1/payment/link/list` |
| `PaymentLinks.ToggleAsync` | `PaymentLinks.ToggleAsync` | `togglePaymentLink` | `POST /v1/payment/link/toggle` |
| `Payments.CancelAsync` | `Payments.CancelAsync` | `cancelPayment` | `POST /v1/payment/cancel` |
| `Payments.CreateAsync` | `Payments.CreateAsync` | `createPayment` | `POST /v1/payment` |
| — (new) | `Payments.GetAmlLinksAsync` | `getPaymentAmlLinks` | `POST /v1/payment/aml-links` |
| — (new) | `Payments.GetCheckoutConfigAsync` | `getCheckoutConfig` | `POST /v1/checkout-config/get` |
| `Payments.InfoAsync`, `Payments.GetAsync` | `Payments.GetInfoAsync` | `getPaymentInfo` | `POST /v1/payment/info` |
| `Payments.QrAsync` | `Payments.GetQrAsync` | `getPaymentQr` | `POST /v1/payment/qr` |
| `Payments.HistoryAsync`, `Payments.ListAsync` | `Payments.ListHistoryAsync` | `listPaymentHistory` | `POST /v1/payment/history` |
| `Payments.ServicesAsync` | `Payments.ListServicesAsync` | `listPaymentServices` | `POST /v1/payment/services` |
| `Refunds.ResolveAsync` | `Payments.ResolveAsync` | `resolvePayment` | `POST /v1/payment/resolve` |
| `Payments.SendEmailAsync` | `Payments.SendEmailAsync` | `sendPaymentEmail` | `POST /v1/payment/send-email` |
| — (new) | `Payments.SetCheckoutConfigAsync` | `setCheckoutConfig` | `POST /v1/checkout-config/set` |
| `PayoutLinks.CancelAsync` | `PayoutLinks.CancelAsync` | `cancelPayoutLink` | `POST /v1/payout/link/cancel` |
| `PayoutLinks.ClaimAsync` | `PayoutLinks.ClaimPayoutAsync` | `claimPayout` | `POST /v1/claim/{token}` |
| `PayoutLinks.CreateAsync` | `PayoutLinks.CreateAsync` | `createPayoutLink` | `POST /v1/payout/link` |
| `PayoutLinks.BatchAsync` | `PayoutLinks.CreateBatchAsync` | `createPayoutLinkBatch` | `POST /v1/payout/link/batch` |
| `PayoutLinks.InfoAsync`, `PayoutLinks.GetAsync` | `PayoutLinks.GetAsync` | `getPayoutLink` | `POST /v1/payout/link/info` |
| `PayoutLinks.ClaimPreviewAsync` | `PayoutLinks.GetPayoutClaimAsync` | `getPayoutClaim` | `GET /v1/claim/{token}` |
| `PayoutLinks.ListAsync` | `PayoutLinks.ListAsync` | `listPayoutLinks` | `POST /v1/payout/link/list` |
| `Payouts.ApproveAsync` | `Payouts.ApproveAsync` | `approvePayout` | `POST /v1/payout/approve` |
| `Payouts.CalculateAsync` | `Payouts.CalculateAsync` | `calculatePayout` | `POST /v1/payout/calculate` |
| `Payouts.CancelAsync` | `Payouts.CancelAsync` | `cancelPayout` | `POST /v1/payout/cancel` |
| `Payouts.CreateAsync` | `Payouts.CreateAsync` | `createPayout` | `POST /v1/payout` |
| `Payouts.MassAsync` | `Payouts.CreateMassAsync` | `createMassPayout` | `POST /v1/payout/mass` |
| `Transfers.BatchAsync` | `Payouts.CreateTransferBatchAsync` | `createTransferBatch` | `POST /v1/transfer/batch` |
| `Payouts.InfoAsync`, `Payouts.GetAsync` | `Payouts.GetInfoAsync` | `getPayoutInfo` | `POST /v1/payout/info` |
| `Payouts.HistoryAsync`, `Payouts.ListAsync` | `Payouts.ListHistoryAsync` | `listPayoutHistory` | `POST /v1/payout/history` |
| `Payouts.ServicesAsync` | `Payouts.ListServicesAsync` | `listPayoutServices` | `POST /v1/payout/services` |
| `Transfers.ToPersonalAsync` | `Payouts.TransferToPersonalAsync` | `transferToPersonal` | `POST /v1/transfer/to-personal` |
| `Transfers.ToUserAsync` | `Payouts.TransferToUserAsync` | `transferToUser` | `POST /v1/transfer/to-user` |
| `Payouts.ValidateAsync` | `Payouts.ValidateAsync` | `validatePayout` | `POST /v1/payout/validate` |
| `Account.ReferralAsync` | `Referrals.GetInfoAsync` | `getReferralInfo` | `POST /v1/referral/info` |
| `Wallets.RefundBlockedDepositAsync` | `Refunds.BlockedWalletAsync` | `refundBlockedWallet` | `POST /v1/wallet/blocked-address-refund` |
| `Refunds.CreateAsync` | `Refunds.PaymentAsync` | `refundPayment` | `POST /v1/payment/refund` |
| `Sandbox.FaucetAsync` | `Sandbox.FaucetAsync` | `sandboxFaucet` | `POST /v1/sandbox/faucet` |
| `Sandbox.WebhooksAsync` | `Sandbox.ListWebhooksAsync` | `sandboxListWebhooks` | `GET /v1/sandbox/webhooks` |
| `Merchants.CreateSandboxAsync` | `Sandbox.OnboardStoreAsync` | `onboardSandboxStore` | `POST /v1/merchants/{id}/sandbox` |
| `Sandbox.ReplayAsync` | `Sandbox.ReplayWebhookAsync` | `sandboxReplayWebhook` | `POST /v1/sandbox/webhooks/replay` |
| `Sandbox.ResetAsync` | `Sandbox.ResetAsync` | `sandboxReset` | `POST /v1/sandbox/reset` |
| `Sandbox.DepositAsync` | `Sandbox.SimulateDepositAsync` | `sandboxSimulateDeposit` | `POST /v1/sandbox/deposit` |
| `Account.VrcsAsync` | `Settings.ConfigureVrcsAsync` | `configureVrcs` | `POST /v1/vrcs` |
| `Settings.DeleteAutoWithdrawAsync` | `Settings.DeleteAutoWithdrawRuleAsync` | `deleteAutoWithdrawRule` | `POST /v1/auto-withdraw/delete` |
| `Settings.GetAccuracyAsync` | `Settings.GetAccuracyAsync` | `getAccuracy` | `POST /v1/payment/accuracy/get` |
| — (new) | `Settings.GetAutoConvertAsync` | `getAutoConvert` | `POST /v1/payment/autoconvert/get` |
| `Settings.GetAutoRefundAsync` | `Settings.GetAutoRefundAsync` | `getAutoRefund` | `POST /v1/payment/autorefund/get` |
| `Settings.GetPaymentFeeConfigAsync` | `Settings.GetPaymentFeeConfigAsync` | `getPaymentFeeConfig` | `POST /v1/payment/fee-config/get` |
| `Payouts.GetFeeConfigAsync` | `Settings.GetPayoutFeeConfigAsync` | `getPayoutFeeConfig` | `POST /v1/payout/fee-config/get` |
| `Payouts.GetRefundFeeConfigAsync` | `Settings.GetRefundFeeConfigAsync` | `getRefundFeeConfig` | `POST /v1/payout/refund-fee-config/get` |
| `Settings.ListAcceptedAsync` | `Settings.ListAcceptedCurrenciesAsync` | `listAcceptedCurrencies` | `POST /v1/payment/accepted/list` |
| — (new) | `Settings.ListApiLogAsync` | `listApiLog` | `POST /v1/payment/api-log` |
| `Settings.ListAutoWithdrawAsync` | `Settings.ListAutoWithdrawRulesAsync` | `listAutoWithdrawRules` | `POST /v1/auto-withdraw/list` |
| `Settings.ListDiscountsAsync` | `Settings.ListDiscountsAsync` | `listDiscounts` | `POST /v1/payment/discount/list` |
| `Settings.SetAcceptedAsync` | `Settings.SetAcceptedCurrenciesAsync` | `setAcceptedCurrencies` | `POST /v1/payment/accepted/set` |
| `Settings.SetAccuracyAsync` | `Settings.SetAccuracyAsync` | `setAccuracy` | `POST /v1/payment/accuracy/set` |
| — (new) | `Settings.SetAutoConvertAsync` | `setAutoConvert` | `POST /v1/payment/autoconvert/set` |
| `Settings.SetAutoRefundAsync` | `Settings.SetAutoRefundAsync` | `setAutoRefund` | `POST /v1/payment/autorefund/set` |
| `Settings.SetAutoWithdrawAsync` | `Settings.SetAutoWithdrawRuleAsync` | `setAutoWithdrawRule` | `POST /v1/auto-withdraw/set` |
| `Settings.SetDiscountAsync` | `Settings.SetDiscountAsync` | `setDiscount` | `POST /v1/payment/discount/set` |
| `Settings.SetPaymentFeeConfigAsync` | `Settings.SetPaymentFeeConfigAsync` | `setPaymentFeeConfig` | `POST /v1/payment/fee-config/set` |
| `Payouts.SetFeeConfigAsync` | `Settings.SetPayoutFeeConfigAsync` | `setPayoutFeeConfig` | `POST /v1/payout/fee-config/set` |
| `Payouts.SetRefundFeeConfigAsync` | `Settings.SetRefundFeeConfigAsync` | `setRefundFeeConfig` | `POST /v1/payout/refund-fee-config/set` |
| `Splits.CreateRuleAsync` | `Splits.CreateRuleAsync` | `createSplitRule` | `POST /v1/split/rule` |
| `Splits.DeleteRuleAsync` | `Splits.DeleteRuleAsync` | `deleteSplitRule` | `POST /v1/split/rule/delete` |
| `Splits.GetConfigAsync` | `Splits.GetConfigAsync` | `getSplitConfig` | `POST /v1/split/config/get` |
| `Splits.GetOptInAsync` | `Splits.GetRecipientOptInAsync` | `getSplitRecipientOptIn` | `POST /v1/split/recipient/optin/get` |
| `Splits.ListRulesAsync` | `Splits.ListRulesAsync` | `listSplitRules` | `POST /v1/split/rule/list` |
| `Splits.SetConfigAsync` | `Splits.SetConfigAsync` | `setSplitConfig` | `POST /v1/split/config/set` |
| `Splits.SetOptInAsync` | `Splits.SetRecipientOptInAsync` | `setSplitRecipientOptIn` | `POST /v1/split/recipient/optin` |
| `Wallets.BlockAsync` | `Wallets.BlockAsync` | `blockWallet` | `POST /v1/wallet/block` |
| `Wallets.CreateAsync` | `Wallets.CreateAsync` | `createWallet` | `POST /v1/wallet` |
| `Wallets.QrAsync` | `Wallets.GetQrAsync` | `getWalletQr` | `POST /v1/wallet/qr` |
| `Webhooks.DeliveriesAsync` | `Webhooks.ListDeliveriesAsync` | `listWebhookDeliveries` | `POST /v1/webhooks/deliveries` |
| `Webhooks.RegisterAsync` | `Webhooks.RegisterAsync` | `registerWebhook` | `POST /v1/webhooks` |
| — (new) | `Webhooks.RequeueDeliveryAsync` | `requeueWebhookDelivery` | `POST /v1/webhooks/deliveries/requeue` |
| `Payments.ResendAsync` | `Webhooks.ResendPaymentAsync` | `resendPaymentWebhook` | `POST /v1/payment/resend` |
| `Webhooks.RotateSecretAsync` | `Webhooks.RotateSecretAsync` | `rotateWebhookSecret` | `POST /v1/webhooks/rotate-secret` |
| `Webhooks.TestLegacyAsync` | `Webhooks.SendLegacyTestAsync` | `sendLegacyTestWebhook` | `POST /v1/payment/testing-webhook` |
| — (new) | `Webhooks.SendTestConversionAsync` | `sendTestConversionWebhook` | `POST /v1/test-webhook/conversion` |
| `Webhooks.TestAsync` | `Webhooks.SendTestPaymentAsync` | `sendTestPaymentWebhook` | `POST /v1/test-webhook/payment` |
| `Webhooks.TestAsync` | `Webhooks.SendTestPayoutAsync` | `sendTestPayoutWebhook` | `POST /v1/test-webhook/payout` |
| `Webhooks.TestAsync` | `Webhooks.SendTestWalletAsync` | `sendTestWalletWebhook` | `POST /v1/test-webhook/wallet` |
| — (new) | `Webhooks.SetActiveAsync` | `setWebhookActive` | `POST /v1/webhooks/active` |
| `Merchants.CreateAsync` | — (removed) | — | `POST /v1/merchants` |
