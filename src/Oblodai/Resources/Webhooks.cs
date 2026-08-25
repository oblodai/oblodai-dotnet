using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>
/// Webhook endpoint management and delivery inspection. Signature verification lives in
/// <see cref="WebhookVerifier"/>, not here.
/// </summary>
public sealed class Webhooks : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Webhooks(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary>
    /// <c>POST /v1/webhooks</c> — register (or replace) the merchant's endpoint; returns the signing
    /// secret once.
    /// </summary>
    /// <param name="url">HTTPS callback URL; private and local addresses are rejected.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<WebhookEndpoint> RegisterAsync(
        string url,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<WebhookEndpoint>(
            Routes.PostV1Webhooks,
            new WebhooksRequest { Url = url },
            options,
            cancellationToken);

    /// <summary>
    /// <c>POST /v1/webhooks/rotate-secret</c> — new secret; the old one keeps verifying until
    /// <c>previous_secret_valid_until</c>. Payout key.
    /// </summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<WebhookSecretRotated> RotateSecretAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<WebhookSecretRotated>(Routes.PostV1WebhooksRotateSecret, null, options, cancellationToken);

    /// <summary><c>POST /v1/webhooks/deliveries</c> — delivery log, newest first.</summary>
    /// <param name="filter">Page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<WebhookDelivery> DeliveriesAsync(
        WebhooksDeliveriesRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedPost<WebhookDelivery>(Routes.PostV1WebhooksDeliveries, filter, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/test-webhook/{payment|payout|wallet}</c> — deliver a sample event of that kind to
    /// <c>url_callback</c>, signed like a real one. The three routes take the same body, so one request
    /// record serves all of them.
    /// </summary>
    /// <param name="kind">Which event kind to rehearse.</param>
    /// <param name="request">Callback URL and the fields to put into the sample event.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentOutOfRangeException">The kind is none of payment, payout or wallet.</exception>
    public Task<WebhookTestResult> TestAsync(
        WebhookKind kind,
        TestWebhookPaymentRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var route = kind.Value switch
        {
            "payment" => Routes.PostV1TestWebhookPayment,
            "payout" => Routes.PostV1TestWebhookPayout,
            "wallet" => Routes.PostV1TestWebhookWallet,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind.Value,
                "no test-webhook route for this kind"),
        };
        return CallAsync<WebhookTestResult>(route, request, options, cancellationToken);
    }

    /// <summary>
    /// <c>POST /v1/payment/testing-webhook</c> — the older rehearsal door (payment events only).
    /// Deprecated: use <see cref="TestAsync"/> with <c>WebhookKind.Payment</c>.
    /// </summary>
    /// <param name="request">Callback URL and the status to put into the sample event.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<WebhookTestResult> TestLegacyAsync(
        PaymentTestingWebhookRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<WebhookTestResult>(Routes.PostV1PaymentTestingWebhook, request, options, cancellationToken);
}
