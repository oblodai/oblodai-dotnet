using Oblodai;

namespace Oblodai.Examples;

/// <summary>End to end in the sandbox with a test_ key: fake money in, a simulated deposit, the webhook log.</summary>
public static class SandboxExample
{
    /// <summary>Run the example.</summary>
    /// <param name="oblodai">A client with a sandbox API key.</param>
    /// <param name="output">Where the example writes what it sees.</param>
    public static async Task RunAsync(OblodaiClient oblodai, TextWriter output)
    {
        await oblodai.Sandbox.FaucetAsync(amount: 1000m, asset: "USDT");

        var invoice = await oblodai.Payments.CreateAsync(
            amount: 25m, currency: "USDT", network: "tron", orderId: $"sbx-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");

        await oblodai.Sandbox.SimulateDepositAsync(
            invoice.Uuid, amount: "25", confirmations: 20, txid: $"sbx-tx-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");

        output.WriteLine((await oblodai.Payments.GetInfoAsync(uuid: invoice.Uuid)).Status); // paid

        var check = await oblodai.Payouts.ValidateAsync("TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx", 10m, "USDT", network: "tron");
        output.WriteLine($"payout would debit {check.PayerAmount}, fee {check.Commission}");

        // A lazy list: pages are fetched as the loop goes.
        await foreach (var delivery in oblodai.Sandbox.ListWebhooksAsync(limit: 20))
        {
            output.WriteLine($"{delivery.EventType} {delivery.Status} attempts={delivery.Attempts}");
        }
    }
}
