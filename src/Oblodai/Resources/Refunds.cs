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

    /// <summary>
    /// <c>POST /v1/payment/refund</c> — refund a paid invoice, fully or partially. Requires the payout key.
    /// <para>
    /// Codes worth branching on: <c>refund.nothing_to_refund</c>, <c>refund.exceeds_refundable</c>,
    /// <c>refund.no_address</c> (the payer address is not refundable — ask for one),
    /// <c>refund.dust</c> (below the network's minimum), <c>refund.reference_collision</c>,
    /// <c>payout.insufficient_funds</c> (retryable), <c>merchant.wrong_key_kind</c>.
    /// </para>
    /// </summary>
    /// <param name="request">Which invoice to refund, and how much.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payout> CreateAsync(
        PaymentRefundRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payout>(Routes.PostV1PaymentRefund, request, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/payment/resolve</c> — settle an underpaid (<c>wrong_amount</c>) invoice.
    /// <para>
    /// Codes worth branching on: <c>payment.not_found</c>, <c>payment.bad_status</c> (not
    /// <c>wrong_amount</c>), <c>refund.nothing_to_refund</c>, <c>refund.no_address</c>,
    /// <c>refund.exceeds_excess</c>.
    /// </para>
    /// </summary>
    /// <param name="request">Which invoice, and whether to accept or refund it.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Resolution> ResolveAsync(
        PaymentResolveRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Resolution>(Routes.PostV1PaymentResolve, request, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/refund/batch</c> — up to 5000 refunds; track with <c>Batches.InfoAsync</c>.
    /// <para>
    /// Codes worth branching on: <c>payout.batch_too_large</c>, <c>payout.empty_batch</c>,
    /// <c>refund.reference_collision</c>, <c>request.missing_field</c> (an item without
    /// <c>reference</c>), <c>merchant.wrong_key_kind</c>, <c>idempotency.key_reused</c>.
    /// </para>
    /// </summary>
    /// <param name="request">The refunds to submit.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<BatchSubmitted> BatchAsync(
        RefundBatchRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<BatchSubmitted>(Routes.PostV1RefundBatch, request, options, cancellationToken);
}
