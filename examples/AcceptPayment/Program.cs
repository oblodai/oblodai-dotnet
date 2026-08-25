// Create an invoice, show the payer the address/URL, then poll until it is final.
// Run with: OBLODAI_PUBLIC_ID=… OBLODAI_SECRET=… dotnet run --project examples/AcceptPayment
using Oblodai;
using Oblodai.Contract;

using var oblodai = new OblodaiClient(); // credentials come from OBLODAI_PUBLIC_ID / OBLODAI_SECRET

var invoice = await oblodai.Payments.CreateAsync(new PaymentRequest
{
    Amount = "25",                      // decimal string, never a float
    Currency = "USDT",
    Network = Network.Tron,             // omit to let the payer choose on the pay page
    OrderId = $"order-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
    UrlSuccess = "https://shop.example/thanks",
});

Console.WriteLine($"pay at {invoice.Url}");
Console.WriteLine($"or send {invoice.PayerAmount} {invoice.PayerCurrency} to {invoice.Address}");

var current = invoice;
while (!Statuses.IsPaymentFinal(current.Status))
{
    await Task.Delay(TimeSpan.FromSeconds(10));
    current = await oblodai.Payments.InfoAsync(invoice.Uuid);
}

Console.WriteLine(Statuses.IsPaymentPaid(current.Status)
    ? $"paid {current.AmountPaid} {current.PayerCurrency}"
    : Statuses.IsPaymentUnderpaid(current.Status)
        ? $"underpaid: {current.AmountPaid} of {current.PayerAmount} — resolve with Refunds.ResolveAsync"
        : $"ended as {current.Status}");
