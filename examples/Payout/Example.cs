using Oblodai;

namespace Oblodai.Examples;

/// <summary>Validate first (free, no side effects), then create with your own idempotency key.</summary>
public static class PayoutExample
{
    /// <summary>Run the example.</summary>
    /// <param name="oblodai">A client with the merchant's API key.</param>
    /// <param name="output">Where the example writes what it sees.</param>
    public static async Task RunAsync(OblodaiClient oblodai, TextWriter output)
    {
        const string orderId = "payout-42";
        const string address = "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx";

        try
        {
            var check = await oblodai.Payouts.ValidateAsync(address, 10m, "USDT", network: "tron");
            output.WriteLine($"will debit {check.PayerAmount}, commission {check.Commission}, fee bearer {check.FeeBearer}");

            // The same key on a retry (even after a restart) can never produce a second payout.
            var payout = await oblodai.Payouts.CreateAsync(
                address,
                10m,
                "USDT",
                orderId,
                network: "tron",
                options: new RequestOptions { IdempotencyKey = orderId });

            output.WriteLine($"{payout.Uuid} {payout.Status}");
        }
        catch (OblodaiException error)
        {
            // Message is "[code] text (request_id=…)" — quote the request id to support.
            output.WriteLine($"{error.Message}{(error.Retryable ? " — retry later" : string.Empty)}");
        }
    }
}
