# Examples

Four runnable programs. Get a **sandbox key** in the dashboard (or provision one against a local
gateway with `Merchants.CreateAsync` + `CreateSandboxAsync`) and put it in the environment:

```bash
export OBLODAI_PUBLIC_ID=test_…
export OBLODAI_SECRET=…
# a local gateway instead of api.oblodai.com:
export OBLODAI_BASE_URL=http://127.0.0.1:8095
```

| Program                                          | What it shows                                                                     |
| ------------------------------------------------ | --------------------------------------------------------------------------------- |
| `dotnet run --project examples/AcceptPayment`     | Create an invoice, show the payer the address, poll until the invoice is final     |
| `dotnet run --project examples/Payout`            | Validate a payout, then create it with your own idempotency key; handle refusals   |
| `dotnet run --project examples/WebhookReceiver`   | Verify deliveries over the raw bytes, deduplicate by id, drop out-of-order events  |
| `dotnet run --project examples/Sandbox`           | Faucet → invoice → simulated deposit → paid, and the sandbox webhook log           |

All four read the one key above — a merchant has a single API key, and it signs payouts as well as
payments. The receiver additionally reads `OBLODAI_WEBHOOK_SECRET` (and
`OBLODAI_WEBHOOK_PREVIOUS_SECRET` during a rotation) and listens on `http://127.0.0.1:8096/hook`.
