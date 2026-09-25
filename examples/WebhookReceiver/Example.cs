using System.Net;
using Oblodai;

namespace Oblodai.Examples;

/// <summary>
/// The rules of a webhook receiver: verify over the RAW body, deduplicate by the state id (<see cref="WebhookDeliveryInfo.EventId"/>),
/// ignore stale sequences, never act on a rehearsal. The HTTP server around it is yours (see Program.cs
/// for HttpListener; in ASP.NET Core read the body with <c>Request.EnableBuffering()</c> and pass
/// <c>name =&gt; Request.Headers[name]</c>).
/// </summary>
public sealed class WebhookReceiverExample
{
    private readonly WebhookVerifyOptions _options;
    private readonly TextWriter _output;
    private readonly HashSet<string> _seenStates = [];              // WebhookDeliveryInfo.EventId; your database in production
    private readonly Dictionary<string, long> _lastSequence = [];   // per object

    /// <summary>A receiver for one endpoint secret (plus the previous one during a rotation).</summary>
    /// <param name="secret">The endpoint secret.</param>
    /// <param name="previousSecret">The outgoing secret while a rotation overlaps, else null.</param>
    /// <param name="output">Where the receiver writes what it does.</param>
    /// <param name="now">Clock (unix seconds), for tests; the system clock when null.</param>
    public WebhookReceiverExample(string secret, string? previousSecret, TextWriter output, Func<long>? now = null)
    {
        _options = new WebhookVerifyOptions { Secret = secret, PreviousSecret = previousSecret, Now = now };
        _output = output;
    }

    /// <summary>Handle one delivery and return the status to answer with.</summary>
    /// <param name="rawBody">The request body, byte for byte.</param>
    /// <param name="header">Case-insensitive header lookup.</param>
    public HttpStatusCode Handle(byte[] rawBody, Func<string, string?> header)
    {
        WebhookDeliveryInfo delivery;
        try
        {
            delivery = WebhookVerifier.VerifyDelivery(rawBody, header, _options);
        }
        catch (SignatureException error)
        {
            // Not from Oblodai (or not fresh): refuse it.
            _output.WriteLine($"rejected: {error.Code}");
            return HttpStatusCode.BadRequest;
        }
        catch (WebhookPayloadException error)
        {
            // The signature matched — this delivery IS authentic, we just could not read it. Refusing it
            // would tell the gateway the endpoint is broken and eventually retire it; ask for a retry.
            _output.WriteLine($"authentic but unreadable: {error.Message}");
            return HttpStatusCode.InternalServerError;
        }

        // A rehearsal (webhooks.send_test_*, sandbox) is signed like a live delivery, but no money moved:
        // never let it reach the code that credits an order.
        if (delivery.IsTest)
        {
            _output.WriteLine($"rehearsal delivery {delivery.EventType} — acknowledged, not applied");
            return HttpStatusCode.OK;
        }

        // A resend of a state already handled: acknowledge and drop.
        if (delivery.EventId is { } eventId && !_seenStates.Add(eventId))
        {
            return HttpStatusCode.OK;
        }

        var webhookEvent = delivery.Event;
        var key = webhookEvent switch
        {
            PaymentWebhook payment => payment.Uuid,
            PayoutWebhook payout => payout.Uuid,
            WalletWebhook wallet => wallet.Uuid,
            ConversionWebhook conversion => conversion.Id,
            _ => webhookEvent.Type,
        };
        if (WebhookVerifier.IsStale(webhookEvent, _lastSequence.TryGetValue(key, out var last) ? last : null))
        {
            return HttpStatusCode.OK;
        }

        if (webhookEvent.EventSequence is { } sequence)
        {
            _lastSequence[key] = sequence;
        }

        switch (webhookEvent)
        {
            case PaymentWebhook payment when Statuses.IsPaymentPaid(payment.Status):
                _output.WriteLine($"order paid: {payment.OrderId} ({payment.PaymentAmount} {payment.PayerCurrency})");
                break;
            case PaymentWebhook payment:
                _output.WriteLine($"invoice {payment.Uuid} is {payment.Status}");
                break;
            case PayoutWebhook payout:
                _output.WriteLine($"payout {payout.Uuid} is {payout.Status}");
                break;
            case WalletWebhook deposit:
                _output.WriteLine($"deposit on static wallet {deposit.Address}: {deposit.PaymentAmount}");
                break;
            default:
                // An event family this SDK version does not know: acknowledged, logged, never acted on.
                _output.WriteLine($"unknown event type \"{webhookEvent.Type}\"");
                break;
        }

        return HttpStatusCode.OK;
    }
}
