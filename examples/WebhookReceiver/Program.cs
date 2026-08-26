// A webhook receiver on the BCL's HttpListener: verify over the RAW body, deduplicate by
// X-Webhook-Id, ignore stale sequences. The same three rules apply in ASP.NET Core — read the body
// with `request.EnableBuffering()` / `ReadAsBytesAsync` and pass `name => Request.Headers[name]`.
//
// Run with: OBLODAI_WEBHOOK_SECRET=… dotnet run --project examples/WebhookReceiver
using System.Net;
using Oblodai;
using Oblodai.Models;

var secret = Environment.GetEnvironmentVariable("OBLODAI_WEBHOOK_SECRET")
             ?? throw new InvalidOperationException("set OBLODAI_WEBHOOK_SECRET");
var previousSecret = Environment.GetEnvironmentVariable("OBLODAI_WEBHOOK_PREVIOUS_SECRET"); // during a rotation

var seenDeliveries = new HashSet<string>();          // X-Webhook-Id; use your database in production
var lastSequence = new Dictionary<string, long>();   // per object uuid

using var listener = new HttpListener();
listener.Prefixes.Add("http://127.0.0.1:8096/");
listener.Start();
Console.WriteLine("listening on http://127.0.0.1:8096/hook");

while (listener.IsListening)
{
    var context = await listener.GetContextAsync();
    using var response = context.Response;

    byte[] rawBody;
    using (var buffer = new MemoryStream())
    {
        await context.Request.InputStream.CopyToAsync(buffer);
        rawBody = buffer.ToArray();
    }

    WebhookDeliveryInfo delivery;
    try
    {
        delivery = WebhookVerifier.VerifyDelivery(
            rawBody,
            name => context.Request.Headers[name],
            new WebhookVerifyOptions { Secret = secret, PreviousSecret = previousSecret });
    }
    catch (SignatureException error)
    {
        Console.Error.WriteLine($"rejected: {error.Code}");
        response.StatusCode = (int)HttpStatusCode.BadRequest;
        continue;
    }

    response.StatusCode = (int)HttpStatusCode.OK;

    // A rehearsal (webhooks.test, sandbox) is signed like a live delivery, but no money moved: never
    // let it reach the code that credits an order.
    if (delivery.IsTest)
    {
        Console.WriteLine($"rehearsal delivery {delivery.EventType} — acknowledged, not applied");
        continue;
    }

    // A retry of a delivery already handled, or an event older than what we applied: acknowledge and drop.
    if (delivery.Id is { } id && !seenDeliveries.Add(id))
    {
        continue;
    }

    var webhookEvent = delivery.Event;
    if (WebhookVerifier.IsStale(webhookEvent, lastSequence.GetValueOrDefault(webhookEvent.Uuid)))
    {
        continue;
    }

    lastSequence[webhookEvent.Uuid] = webhookEvent.Sequence;

    switch (webhookEvent)
    {
        case PaymentEvent payment when Statuses.IsPaymentPaid(payment.Status):
            Console.WriteLine($"order paid: {payment.OrderId} ({payment.PaymentAmount} {payment.PayerCurrency})");
            break;
        case PaymentEvent payment:
            Console.WriteLine($"invoice {payment.Uuid} is {payment.Status}");
            break;
        case PayoutEvent payout:
            Console.WriteLine($"payout {payout.Uuid} is {payout.Status}");
            break;
        case WalletEvent deposit:
            Console.WriteLine($"deposit on static wallet {deposit.Address}: {deposit.PaymentAmount}");
            break;
    }
}
