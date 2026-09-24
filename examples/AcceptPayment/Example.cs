using Oblodai;

namespace Oblodai.Examples;

/// <summary>Create an invoice, show the payer the address/URL, then poll until it is final.</summary>
public static class AcceptPaymentExample
{
    /// <summary>Run the example.</summary>
    /// <param name="oblodai">A client with the merchant's API key.</param>
    /// <param name="output">Where the example writes what it sees.</param>
    /// <param name="pollEvery">Pause between status checks.</param>
    public static async Task<PaymentStatus> RunAsync(OblodaiClient oblodai, TextWriter output, TimeSpan pollEvery)
    {
        var invoice = await oblodai.Payments.CreateAsync(
            amount: 25m,                    // decimal (or Model.From with a decimal string), never a double
            currency: "USDT",
            network: "tron",                // omit to let the payer choose on the pay page
            orderId: $"order-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            urlSuccess: "https://shop.example/thanks");

        output.WriteLine($"pay at {invoice.Url}");
        output.WriteLine($"or send {invoice.PayerAmount} {invoice.PayerCurrency} to {invoice.Address}");

        var status = invoice.Status;
        var paid = invoice.AmountPaid;
        while (!Statuses.IsPaymentFinal(status))
        {
            await Task.Delay(pollEvery);
            var current = await oblodai.Payments.GetInfoAsync(uuid: invoice.Uuid);
            (status, paid) = (current.Status, current.AmountPaid);
        }

        output.WriteLine(Statuses.IsPaymentPaid(status)
            ? $"paid {paid} {invoice.PayerCurrency}"
            : Statuses.IsPaymentUnderpaid(status)
                ? $"underpaid: {paid} of {invoice.PayerAmount} — resolve with Payments.ResolveAsync"
                : $"ended as {status}");
        return status;
    }
}
