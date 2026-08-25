using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>
/// Developer sandbox (<c>test_</c> keys only): fake money, simulated deposits, webhook inspector.
/// </summary>
public sealed class Sandbox : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Sandbox(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary><c>POST /v1/sandbox/faucet</c> — credit test funds. Payout key.</summary>
    /// <param name="request">Asset and amount to credit.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FaucetResult> FaucetAsync(
        SandboxFaucetRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<FaucetResult>(Routes.PostV1SandboxFaucet, request, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/sandbox/deposit</c> — simulate an on-chain deposit to an invoice (repeat the txid to
    /// add confirmations).
    /// </summary>
    /// <param name="request">Which invoice to pay, and with what.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<SandboxDeposit> DepositAsync(
        SandboxDepositRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<SandboxDeposit>(Routes.PostV1SandboxDeposit, request, options, cancellationToken);

    /// <summary><c>GET /v1/sandbox/webhooks</c> — deliveries with their payloads.</summary>
    /// <param name="page">Page window; sent as query parameters.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<WebhookDelivery> WebhooksAsync(
        PageParams? page = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedGet<WebhookDelivery>(Routes.GetV1SandboxWebhooks, page, options, cancellationToken);

    /// <summary><c>POST /v1/sandbox/webhooks/replay</c> — re-send a terminal (delivered/dead) delivery.</summary>
    /// <param name="deliveryId">Delivery id from <see cref="WebhooksAsync"/>.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<SandboxReplay> ReplayAsync(
        string deliveryId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<SandboxReplay>(
            Routes.PostV1SandboxWebhooksReplay,
            new SandboxWebhooksReplayRequest { DeliveryId = deliveryId },
            options,
            cancellationToken);

    /// <summary><c>POST /v1/sandbox/reset</c> — cancel open invoices and zero balances. Payout key.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<SandboxReset> ResetAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<SandboxReset>(Routes.PostV1SandboxReset, null, options, cancellationToken);
}
