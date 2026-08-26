// Validate first (free, no side effects), then create with your own idempotency key.
// Run with: OBLODAI_PUBLIC_ID=… OBLODAI_SECRET=… dotnet run --project examples/Payout
using Oblodai;
using Oblodai.Contract;

// The same API key that takes payments sends them out again; the client reads it from the environment.
using var oblodai = new OblodaiClient();

const string orderId = "payout-42";
const string address = "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx";

try
{
    var check = await oblodai.Payouts.ValidateAsync(new PayoutValidateRequest
    {
        Amount = "10",
        Currency = "USDT",
        Network = Network.Tron,
        Address = address,
    });

    Console.WriteLine($"will debit {check.PayerAmount}, commission {check.Commission}, fee bearer {check.FeeBearer}");

    // The same key on a retry (even after a restart) can never produce a second payout.
    var payout = await oblodai.Payouts.CreateAsync(
        new PayoutRequest
        {
            Amount = "10",
            Currency = "USDT",
            Network = Network.Tron,
            Address = address,
            OrderId = orderId,
        },
        new RequestOptions { IdempotencyKey = orderId });

    Console.WriteLine($"{payout.Uuid} {payout.Status}");
}
catch (OblodaiException error)
{
    Console.Error.WriteLine(
        $"{error.Code}: {error.Message} {(error.Retryable ? "(retry later)" : string.Empty)} request {error.RequestId}");
}
