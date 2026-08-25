// End to end in the sandbox with a test_ key: fake money in, a simulated deposit, the webhook log.
// Run with: OBLODAI_PUBLIC_ID=test_… OBLODAI_SECRET=… dotnet run --project examples/Sandbox
using Oblodai;
using Oblodai.Contract;
using Oblodai.Resources;

using var oblodai = new OblodaiClient();

await oblodai.Sandbox.FaucetAsync(new SandboxFaucetRequest { Asset = "USDT", Amount = "1000" });

var invoice = await oblodai.Payments.CreateAsync(new PaymentRequest
{
    Amount = "25",
    Currency = "USDT",
    Network = Network.Tron,
    OrderId = $"sbx-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
});

await oblodai.Sandbox.DepositAsync(new SandboxDepositRequest
{
    InvoiceId = invoice.Uuid,
    Amount = "25",
    Confirmations = 20,
    Txid = $"sbx-tx-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
});

Console.WriteLine((await oblodai.Payments.InfoAsync(invoice.Uuid)).Status); // paid

var check = await oblodai.Payouts.ValidateAsync(new PayoutValidateRequest
{
    Amount = "10",
    Currency = "USDT",
    Network = Network.Tron,
    Address = "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx",
});
Console.WriteLine($"payout would debit {check.PayerAmount}, fee {check.Commission}");

await foreach (var delivery in oblodai.Sandbox.WebhooksAsync(new PageParams { Limit = 20 }))
{
    Console.WriteLine($"{delivery.EventType} {delivery.Status} attempts={delivery.Attempts}");
}
