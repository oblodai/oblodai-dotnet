using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>
/// Refunds are payouts in the invoice's own asset; underpayments are resolved (accept or refund).
/// Requires the payout key.
/// </summary>
public sealed class Refunds : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Refunds(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary><c>POST /v1/payment/refund</c> — refund a paid invoice, fully or partially. Requires the payout key.</summary>
    /// <param name="request">Which invoice to refund, and how much.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payout> CreateAsync(
        PaymentRefundRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payout>(Routes.PostV1PaymentRefund, request, options, cancellationToken);

    /// <summary><c>POST /v1/payment/resolve</c> — settle an underpaid (<c>wrong_amount</c>) invoice.</summary>
    /// <param name="request">Which invoice, and whether to accept or refund it.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Resolution> ResolveAsync(
        PaymentResolveRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Resolution>(Routes.PostV1PaymentResolve, request, options, cancellationToken);

    /// <summary><c>POST /v1/refund/batch</c> — up to 5000 refunds; track with <c>Batches.InfoAsync</c>.</summary>
    /// <param name="request">The refunds to submit.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<BatchSubmitted> BatchAsync(
        RefundBatchRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<BatchSubmitted>(Routes.PostV1RefundBatch, request, options, cancellationToken);
}
